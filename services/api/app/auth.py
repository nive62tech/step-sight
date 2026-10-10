import asyncio
import re
import time
from dataclasses import dataclass
from urllib.parse import urlparse

import httpx
import jwt
from fastapi import HTTPException, Request

from app.config import Settings


@dataclass(frozen=True)
class Identity:
    subject: str


class Authenticator:
    def __init__(self, settings: Settings):
        self.settings = settings
        self.keys: dict = {}
        self.expires = 0.0
        self.last_fetch = float("-inf")
        self.lock = asyncio.Lock()
        if settings.auth_mode == "cognito":
            issuer = urlparse(settings.cognito_issuer)
            if issuer.scheme != "https" or not re.fullmatch(r"cognito-idp\.[a-z0-9-]+\.amazonaws\.com", issuer.hostname or "") or not re.fullmatch(r"/[a-z0-9-]+_[A-Za-z0-9]+", issuer.path) or issuer.query or issuer.fragment or issuer.username:
                raise ValueError("invalid Cognito issuer")

    async def authenticate(self, request: Request) -> Identity:
        if self.settings.auth_mode == "mock":
            peer = request.client.host if request.client else ""
            allowed = {"127.0.0.1", "::1"}
            if self.settings.app_env == "test":
                allowed.add("testclient")
            if peer not in allowed:
                raise HTTPException(403, "mock authentication is loopback only")
            subject = request.headers.get("x-dev-user", "development-user")
            if not subject or len(subject) > 100 or not subject.replace("-", "").isalnum():
                raise HTTPException(401, "invalid development identity")
            return Identity("mock:" + subject)
        header = request.headers.get("authorization", "")
        if not header.startswith("Bearer ") or len(header) > 16000:
            raise HTTPException(401, "access token required")
        token = header[7:]
        try:
            token_header = jwt.get_unverified_header(token)
            if token_header.get("alg") != "RS256":
                raise ValueError("algorithm")
            async with self.lock:
                if time.monotonic() >= self.expires or token_header.get("kid") not in self.keys:
                    if time.monotonic() - self.last_fetch < 30:
                        raise ValueError("unknown or unavailable signing key")
                    self.last_fetch = time.monotonic()
                    async with httpx.AsyncClient(timeout=5, follow_redirects=False) as client:
                        response = await client.get(self.settings.cognito_issuer + "/.well-known/jwks.json")
                        response.raise_for_status()
                        if len(response.content) > 65536:
                            raise ValueError("JWKS size")
                        self.keys = {key["kid"]: jwt.PyJWK(key).key for key in response.json()["keys"]}
                        self.expires = time.monotonic() + 600
            key = self.keys[token_header["kid"]]
            claims = jwt.decode(
                token, key, algorithms=["RS256"], issuer=self.settings.cognito_issuer,
                options={"verify_aud": False, "require": ["exp", "iat", "iss", "sub", "token_use", "client_id"]},
                leeway=15,
            )
            if claims["token_use"] != "access" or claims["client_id"] != self.settings.cognito_client_id:
                raise ValueError("token binding")
            if not set(self.settings.cognito_scopes.split()).issubset(set(claims.get("scope", "").split())):
                raise ValueError("scope")
            return Identity(claims["sub"])
        except (ValueError, KeyError, jwt.PyJWTError):
            raise HTTPException(401, "invalid access token") from None
        except httpx.HTTPError:
            raise HTTPException(503, "identity provider temporarily unavailable") from None
