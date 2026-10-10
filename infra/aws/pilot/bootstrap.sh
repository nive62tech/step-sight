#!/usr/bin/env bash
set -euo pipefail
dnf install -y docker jq openssl
systemctl enable --now docker
install -d -m 0700 /opt/desktop-guide
# No default credentials, API image, or mock public service is started here.
# The reviewed deployment runbook installs pinned Compose/runtime artifacts over SSM.
cat >/opt/desktop-guide/BOOTSTRAP_ONLY <<'EOF'
Infrastructure bootstrap only. Configure production secrets, federation, firewall,
release artifacts, backups, and migrations before starting the API.
EOF
