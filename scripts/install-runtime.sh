#!/usr/bin/env bash
# Run only on the explicitly approved, isolated pilot instance through SSM.
set -euo pipefail
umask 077
cd /opt/desktop-guide
compose_version=v5.4.0
compose_sha256=837fd1d35bf6a494f41b5b5988269a7be79de337cf1a1a6ff0e45ab51bb4e9be
install -d /usr/local/lib/docker/cli-plugins
if ! docker compose version >/dev/null 2>&1; then
  curl --fail --location --silent --show-error --proto '=https' --tlsv1.2 \
    "https://github.com/docker/compose/releases/download/${compose_version}/docker-compose-linux-x86_64" \
    --output /opt/desktop-guide/docker-compose.download
  printf '%s  %s\n' "$compose_sha256" /opt/desktop-guide/docker-compose.download | sha256sum --check --status
  install -m 0755 /opt/desktop-guide/docker-compose.download /usr/local/lib/docker/cli-plugins/docker-compose
  rm /opt/desktop-guide/docker-compose.download
fi
docker compose version
test -f .env && test -f gcp-external-account.json
chmod 600 .env
chmod 644 gcp-external-account.json
# Never print `docker compose config`: it expands the database password.
docker compose -f compose.production.yaml config --quiet
docker compose -f compose.production.yaml up -d --wait db
# Create the fixed API bridge before allowing only its address to use IMDSv2.
./runtime-firewall.sh
install -m 0644 desktop-guide-firewall.service /etc/systemd/system/desktop-guide-firewall.service
install -m 0644 desktop-guide-backup.service /etc/systemd/system/desktop-guide-backup.service
install -m 0644 desktop-guide-backup.timer /etc/systemd/system/desktop-guide-backup.timer
systemctl daemon-reload
systemctl enable --now desktop-guide-firewall.service desktop-guide-backup.timer
docker compose -f compose.production.yaml run --rm --no-deps api \
  .venv/bin/alembic -c services/api/alembic.ini upgrade head
docker compose -f compose.production.yaml up -d api proxy
for attempt in $(seq 1 30); do
  if curl --fail --silent http://127.0.0.1/health/ready >/dev/null; then
    rm -f BOOTSTRAP_ONLY
    printf 'Production API ready; live inference qualification is separate.\n'
    exit 0
  fi
  sleep 2
done
printf 'API readiness failed; inspect only bounded operational diagnostics.\n' >&2
exit 1
