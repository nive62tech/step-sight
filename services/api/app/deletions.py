import asyncio
import json
from datetime import UTC, datetime

from fastapi import HTTPException


async def record_deletion(bucket: str, session_id: str):
    if not bucket:
        return  # Development/test have no remote backup ledger.

    def write():
        import boto3
        from botocore.config import Config

        client = boto3.client("s3", region_name="ap-south-1", config=Config(connect_timeout=3, read_timeout=5, retries={"total_max_attempts": 1}))
        try:
            client.put_object(Bucket=bucket, Key=f"deletions/{session_id}.json", ServerSideEncryption="AES256",
                ContentType="application/json", Body=json.dumps({"schema_version": 1, "session_id": session_id,
                "deleted_at": datetime.now(UTC).isoformat()}).encode())
        finally:
            client.close()

    try:
        await asyncio.to_thread(write)
    except Exception:
        raise HTTPException(503, "deletion could not be recorded safely; retry deletion later") from None
