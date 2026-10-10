# Operations, backups, updates and rollback

## Routine checks

Use SSM for administration; no public SSH/database ingress exists. `/health/live` checks process availability; `/health/ready` checks PostgreSQL and labels configured modes. Neither endpoint proves model/IAM availability. A synthetic provider probe is the separate billable check. Watch CPU/status, memory/disk, log volume, provider latency/errors, unknown usage reservations and monthly spend. Alarms without configured destinations are console-only. Keep one API worker until shared coordination is implemented.

The inference container alone is allowed to reach IMDS through `scripts/runtime-firewall.sh`. The installed systemd unit reapplies it after Docker starts; restart/reboot timing still needs qualification. Verify API refresh succeeds and another container's IMDSv2 PUT fails. Never mount a Docker socket or cloud user credential directory into API/DB. The nonsecret federation descriptor is a read-only file. Production `.env` is owner-readable on encrypted EBS; its generated password originates in the exact SSM SecureString. Do not enable request-body/access/debug model logging or raw media dumps for diagnosis.

Troubleshoot a 409 by pausing/resuming for fresh state/observation, not by replaying an input action. A 401 requires sign-in; a 429 needs allowance/rate review or manual continuation; a 503 needs service/model/identity checks. The desktop remains locally disarmed during service failure. Stop remains available without a working network. A global hotkey conflict is displayed, with panel/tray fallback.

## Backup and restore

On the approved EC2 runtime, `desktop-guide-backup.timer` schedules a daily UTC backup. Its service runs `backup-database.sh`, streaming `pg_dump -Fc` through TLS to S3 with AES256. An actual encrypted object was restored into a fresh local PostgreSQL target and reconciled with the independent ledger; see `artifacts/cloud-backup-restore-check.json`. This does not measure a complete disaster recovery. Backups expire after 14 days plus version lifecycle delay. Restore only snapshots within the supported 14-day window. Proposed recovery objectives are 24h metadata loss and manual recovery within 4h; both remain unmeasured.

User-requested deletions first write an independent encrypted S3 record under `deletions/{session-id}.json`. If that fails, deletion returns 503 and can be retried. Only opaque session ID/version/date is stored; no goal/media/owner text. Ledger records expire after 30 days, exceeding backup recoverability. Automatic retention expiry is also reapplied during restore.

Restore into a separately prepared private empty PostgreSQL target, while API/proxy remain stopped. Use an explicitly authorized restore operator with S3 read/list access to `backups/` and `deletions/`; the normal runtime role deliberately has backup write, not arbitrary backup read. Set an exact `BACKUP_KEY`, then run `restore-database.sh --confirm-disposable-target`. The script refuses a nonempty target, restores without DROP/clean, reads the latest independent deletion ledger and invokes `reconcile-deletions.py` before returning. Missing ledger access/invalid records abort the process. Do not expose a restored API before this succeeds.

Run migrations, schema drift check, readiness, ownership/deletion tests and a synthetic model probe before cutover. Keep the former database isolated for approved recovery, and document rollback/retention. An old database backup alone is never sufficient to honor newer deletion requests. Local `scripts/check-postgres.py` exercises fresh migrations, binary backup and restoration with generated disposable database names; it does not qualify S3/IAM/disaster recovery.

## Release/update and rollback

Build a self-contained Windows x64 ZIP with `package-windows.ps1`. Unsigned output is labelled development-only. Supply a code-signing thumbprint to sign and verify the executable using SHA256 and a timestamp. Verify all package hashes from a trusted channel, not a checksum fetched from an untrusted payload. Check every executable dependency and installer/update policy before wider distribution. The pilot has no automatic unsigned updater; use a manual signed install.

Before update, Stop and exit the client, preserve only opt-in summaries/settings, extract a verified package into a new versioned directory, and relaunch. Startup always has empty permission state; no update/restart inherits approval/nonces. Roll back by stopping/exiting and switching to the prior verified directory. An incompatible wire version fails closed.

Backend releases use immutable image digests. Record the prior digest and migration version, take/verify a metadata backup, stop new lessons, perform additive migration changes, then replace API/proxy. Smoke-test health/auth/provider/owner isolation and check that restarted clients remain disarmed. Roll back to the prior image only if schema-compatible. Destructive Alembic downgrade is disabled; restore an approved fresh target with current deletion reconciliation when necessary.

If cloud costs exceed the target, first stop inference and new lessons, review usage and disable the pilot API deliberately. Stopping EC2 still leaves storage/S3/CloudFront/IP/other costs. Teardown is a separate reviewed operation because Terraform prevents accidental destruction of important resources; do not strip destroy guards as an incidental fix.
