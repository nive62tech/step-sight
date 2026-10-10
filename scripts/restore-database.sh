#!/usr/bin/env bash
set -euo pipefail
: "${BACKUP_BUCKET:?set the dedicated pilot data bucket}"
: "${BACKUP_KEY:?set an explicit backups/... object key}"
if [[ "${1:-}" != "--confirm-disposable-target" ]]; then
  printf 'Restore requires a separately prepared disposable target and --confirm-disposable-target.\n' >&2
  exit 2
fi
[[ "$BACKUP_KEY" == backups/* ]] || { printf 'Only backup-prefix objects are accepted.\n' >&2; exit 2; }
cd /opt/desktop-guide
backup_region="${BACKUP_REGION:-ap-south-1}"
backup_date=$(aws s3api head-object --bucket "$BACKUP_BUCKET" --key "$BACKUP_KEY" --query LastModified --output text --region "$backup_region")
DG_BACKUP_DATE="$backup_date" python3 - <<'PY'
import os
from datetime import datetime, timedelta, timezone
timestamp = datetime.fromisoformat(os.environ['DG_BACKUP_DATE'].replace('Z', '+00:00'))
age = datetime.now(timezone.utc) - timestamp
if not timedelta(minutes=-5) <= age <= timedelta(days=14):
    raise SystemExit('Backup is outside the supported 14-day recovery window')
PY
existing_tables=$(docker compose -f compose.production.yaml exec -T db psql -U desktopguide -d desktopguide -At -c "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'")
[[ "$existing_tables" == "0" ]] || { printf 'Restore target must be empty.\n' >&2; exit 2; }
# This script never drops or cleans an existing database. Provision a fresh target first.
aws s3 cp "s3://${BACKUP_BUCKET}/${BACKUP_KEY}" - --only-show-errors --region "$backup_region" |
  docker compose -f compose.production.yaml exec -T db pg_restore --exit-on-error -U desktopguide -d desktopguide
umask 077
ledger_dir=$(mktemp -d /tmp/dg-deletions.XXXXXXXX)
aws s3 sync "s3://${BACKUP_BUCKET}/deletions/" "$ledger_dir" --only-show-errors --region "$backup_region"
chown -R 10001:10001 "$ledger_dir"
chmod 0700 "$ledger_dir"
find "$ledger_dir" -maxdepth 1 -type f -name '*.json' -exec chmod 0600 {} \;
docker compose -f compose.production.yaml run --rm --no-deps -v "$ledger_dir:/run/ledger:ro" api \
  .venv/bin/python scripts/reconcile-deletions.py --ledger-directory /run/ledger --confirm-restored-target
find "$ledger_dir" -maxdepth 1 -type f -name '*.json' -delete
rmdir "$ledger_dir"
printf 'Restored and reconciled independent deletions. Run migrations/verification before exposing the API.\n'
