"""Opt-in pilot OAuth/ownership check with temporary synthetic users and no email.

Passwords and tokens stay in memory. This tests Hosted UI HTTP/PKCE and the API;
it does not qualify the Windows browser, DPAPI or loopback-listener experience.
"""
import argparse
import base64
import hashlib
import io
import json
import secrets
import time
from datetime import UTC, datetime
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import parse_qs, urlencode, urljoin, urlparse
from uuid import uuid4

import boto3
import httpx
from live_budget import LiveBudget
from PIL import Image, ImageDraw


class LoginForm(HTMLParser):
    def __init__(self):
        super().__init__()
        self.hidden = {}

    def handle_starttag(self, tag, attributes):
        values = dict(attributes)
        if tag == "input" and values.get("type") == "hidden" and "name" in values:
            self.hidden[values["name"]] = values.get("value", "")


def login(config, username, password):
    state, nonce, verifier = (secrets.token_urlsafe(32) for _ in range(3))
    challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).decode().rstrip("=")
    domain = config["CognitoDomain"]
    query = {"client_id": config["CognitoClientId"], "response_type": "code", "redirect_uri": config["CallbackUri"],
             "scope": config["Scopes"], "state": state, "nonce": nonce, "code_challenge": challenge, "code_challenge_method": "S256"}
    with httpx.Client(timeout=20) as browser:
        response = browser.get(domain + "/oauth2/authorize?" + urlencode(query))
        for _ in range(5):
            if not response.is_redirect:
                break
            url = urljoin(str(response.url), response.headers["location"])
            if urlparse(url).netloc != urlparse(domain).netloc:
                raise ValueError("Unexpected sign-in redirect")
            response = browser.get(url)
        response.raise_for_status()
        form = LoginForm()
        form.feed(response.text)
        payload = {**form.hidden, "username": username, "password": password}
        response = browser.post(str(response.url), data=payload, headers={"Origin": domain, "Referer": str(response.url)})
        if not response.is_redirect:
            raise ValueError("Hosted UI did not issue an authorization redirect")
        callback = urljoin(str(response.url), response.headers["location"])
        parsed = urlparse(callback)
        expected = urlparse(config["CallbackUri"])
        if (parsed.scheme, parsed.netloc, parsed.path) != (expected.scheme, expected.netloc, expected.path):
            raise ValueError("Unexpected OAuth callback")
        values = parse_qs(parsed.query)
        if values.get("state") != [state] or len(values.get("code", [])) != 1:
            raise ValueError("OAuth state/code mismatch")
        response = browser.post(domain + "/oauth2/token", data={"grant_type": "authorization_code",
            "client_id": config["CognitoClientId"], "redirect_uri": config["CallbackUri"],
            "code": values["code"][0], "code_verifier": verifier})
        response.raise_for_status()
        return response.json()


def check(config_path: Path, output: Path, synthetic_turn: bool, budget_path: Path):
    config = json.loads(config_path.read_text(encoding="utf-8-sig"))
    if config["ApiOrigin"] != "https://d1gnr0x6ly7io1.cloudfront.net" or config["CognitoClientId"] != "3var8jd5gj9ls3l2rlqa3ket1j":
        raise ValueError("Outside the approved isolated pilot")
    aws = boto3.Session(profile_name="personal", region_name="ap-south-1")
    if aws.client("sts").get_caller_identity()["Account"] != "214422569127":
        raise ValueError("Outside approved AWS account")
    cognito = aws.client("cognito-idp")
    pool = "ap-south-1_qU4Y8lMnm"
    created, sessions = [], []
    report = {"date": datetime.now(UTC).isoformat(), "synthetic_users_only": True, "email_delivery": "suppressed", "tests": {}}
    try:
        tokens = []
        for _ in range(2):
            username = f"dg-synthetic-{uuid4()}@example.invalid"
            password = "S!9a" + secrets.token_urlsafe(32)
            user = cognito.admin_create_user(UserPoolId=pool, Username=username, MessageAction="SUPPRESS",
                TemporaryPassword=password, UserAttributes=[{"Name": "email", "Value": username}, {"Name": "email_verified", "Value": "true"}])
            created.append(user["User"]["Username"])
            cognito.admin_set_user_password(UserPoolId=pool, Username=created[-1], Password=password, Permanent=True)
            tokens.append(login(config, username, password))
        report["tests"]["hosted_ui_pkce"] = "passed"
        with httpx.Client(base_url=config["ApiOrigin"], timeout=65) as api:
            headers = {"Authorization": "Bearer " + tokens[0]["access_token"]}
            other = {"Authorization": "Bearer " + tokens[1]["access_token"]}
            assert api.get("/v1/lessons", headers=headers).status_code == 200
            assert api.get("/v1/lessons", headers={"Authorization": "Bearer " + tokens[0]["id_token"]}).status_code == 401
            report["tests"]["signed_access_and_id_token_rejection"] = "passed"
            response = api.post("/v1/sessions", headers=headers, json={"device_id": str(uuid4()), "scope_id": str(uuid4()),
                "goal": "Show where to save practice settings in this synthetic image" if synthetic_turn else "Synthetic auth validation only"})
            assert response.status_code == 201
            sid = response.json()["id"]
            sessions.append((sid, headers))
            assert api.get(f"/v1/sessions/{sid}", headers=other).status_code == 404
            assert api.delete(f"/v1/sessions/{sid}", headers=other).status_code == 404
            assert api.get(f"/v1/sessions/{sid}", headers=headers).status_code == 200
            report["tests"]["real_owner_isolation"] = "passed"
            if synthetic_turn:
                budget = LiveBudget(budget_path, "gen-lang-client-0644821693")
                # One schema repair plus two transient retries can produce four attempts.
                # Retain all reservations unless actual usage is reconciled from the API ledger.
                for attempt in range(4):
                    budget.reserve(f"deployed API synthetic turn maximum attempt {attempt + 1}")
                image = Image.new("RGB", (800, 450), "white")
                draw = ImageDraw.Draw(image)
                draw.rectangle((520, 330, 750, 410), fill="#286654")
                draw.text((550, 360), "Save practice settings", fill="white")
                stream = io.BytesIO()
                image.save(stream, "PNG")
                current = api.get(f"/v1/sessions/{sid}", headers=headers).json()
                observation_id, geometry_id = str(uuid4()), str(uuid4())
                metadata = {"schema_version": 1, "request_id": str(uuid4()), "session_version": current["session_version"],
                    "event_kind": "start", "observation": {"id": observation_id, "geometry_id": geometry_id,
                    "scope_id": current["scope_id"], "captured_at": datetime.now(UTC).isoformat(),
                    "image_width": 800, "image_height": 450, "privacy_filtered": True}}
                started = time.monotonic()
                response = api.post(f"/v1/sessions/{sid}/turns", headers=headers, files={
                    "metadata": (None, json.dumps(metadata), "application/json"),
                    "screenshot": ("synthetic.png", stream.getvalue(), "image/png")})
                assert response.status_code == 200
                body = response.json()
                assert body["provider"] == "gemini" and body["observation_id"] == observation_id
                if body.get("step"):
                    assert body["step"]["source_observation_id"] == observation_id
                    target = body["step"].get("target")
                    if target:
                        assert target["geometry_id"] == geometry_id
                recovered = api.get(f"/v1/sessions/{sid}/turns/{metadata['request_id']}", headers=headers)
                assert recovered.status_code == 200 and recovered.json() == body
                assert api.get(f"/v1/sessions/{sid}/turns/{metadata['request_id']}", headers=other).status_code == 404
                report["tests"]["same_turn_recovery_and_owner_isolation"] = "passed"
                report["tests"]["deployed_image_turn_and_current_evidence_validation"] = "passed"
                report["turn_round_trip_ms"] = round((time.monotonic() - started) * 1000)
                report["turn_timings_ms"] = body.get("timings_ms", {})
                report["synthetic_media_only"] = True
            response = api.post(config["CognitoDomain"] + "/oauth2/token", data={"grant_type": "refresh_token",
                "client_id": config["CognitoClientId"], "refresh_token": tokens[0]["refresh_token"]})
            assert response.status_code == 200
            assert api.get("/v1/lessons", headers={"Authorization": "Bearer " + response.json()["access_token"]}).status_code == 200
            report["tests"]["cognito_refresh"] = "passed"
            assert api.delete(f"/v1/sessions/{sid}", headers=headers).status_code == 204
            assert api.get(f"/v1/sessions/{sid}", headers=headers).status_code == 404
            sessions.clear()
            report["tests"]["production_deletion_ledger"] = "passed"
            for token in tokens:
                api.post(config["CognitoDomain"] + "/oauth2/revoke", data={"client_id": config["CognitoClientId"], "token": token["refresh_token"]}).raise_for_status()
            report["tests"]["refresh_revocation"] = "passed"
        report["result"] = "passed"
    except Exception as exc:
        report.update(result="failed", error_type=type(exc).__name__)
    finally:
        with httpx.Client(base_url=config["ApiOrigin"], timeout=10) as api:
            for sid, headers in sessions:
                try:
                    api.delete(f"/v1/sessions/{sid}", headers=headers)
                except httpx.HTTPError:
                    pass
        cleanup = []
        for username in created:
            try:
                cognito.admin_delete_user(UserPoolId=pool, Username=username)
            except Exception:
                cleanup.append("synthetic user cleanup failed")
        report["synthetic_user_cleanup"] = "passed" if not cleanup else cleanup
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(report, indent=2))
    return 0 if report.get("result") == "passed" and not cleanup else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--allow-live", action="store_true")
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=Path("artifacts/deployed-auth-check.json"))
    parser.add_argument("--synthetic-turn", action="store_true", help="adds one billable synthetic image turn")
    parser.add_argument("--budget-ledger", type=Path, default=Path("artifacts/live-validation-budget.json"))
    args = parser.parse_args()
    if not args.allow_live:
        parser.error("Temporary synthetic cloud users require --allow-live and deployment authorization")
    raise SystemExit(check(args.config, args.output, args.synthetic_turn, args.budget_ledger))
