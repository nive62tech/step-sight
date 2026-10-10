#!/usr/bin/env bash
set -euo pipefail
umask 077
: "${BACKUP_BUCKET:?set the dedicated pilot data bucket}"
backup_region="${BACKUP_REGION:-ap-south-1}"
cd /opt/desktop-guide
backup_name="desktop-guide-$(date -u +%Y%m%dT%H%M%SZ).dump"
# Stream PostgreSQL's custom format directly to encrypted S3; no local raw backup file.
docker compose -f compose.production.yaml exec -T db pg_dump -U desktopguide -d desktopguide -Fc |
  aws s3 cp - "s3://${BACKUP_BUCKET}/backups/${backup_name}" --sse AES256 --only-show-errors --region "$backup_region"
printf 'Encrypted database backup uploaded: %s\n' "$backup_name"
