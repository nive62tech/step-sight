#!/usr/bin/env bash
set -euo pipefail
# Run as part of approved host deployment; API alone receives metadata credentials.
iptables -N DG_METADATA 2>/dev/null || true
iptables -F DG_METADATA
iptables -A DG_METADATA -s 172.28.0.10/32 -d 169.254.169.254/32 -j RETURN
iptables -A DG_METADATA -d 169.254.169.254/32 -j REJECT
iptables -A DG_METADATA -j RETURN
iptables -C DOCKER-USER -j DG_METADATA 2>/dev/null || iptables -I DOCKER-USER 1 -j DG_METADATA
# Validate after every Docker/network restart. Do not weaken IMDSv2 to repair federation.
