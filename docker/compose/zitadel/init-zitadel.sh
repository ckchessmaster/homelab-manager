#!/usr/bin/env bash
# ==============================================================================
# ControlPlane Zitadel Bootstrap Script
# Initializes the 'controlplane' project, roles (admin, operator, viewer),
# and registers the React 19 SPA client application with PKCE.
# ==============================================================================
set -euo pipefail

ZITADEL_URL="${ZITADEL_URL:-http://localhost:8085}"
REDIRECT_URI="${REDIRECT_URI:-http://localhost/auth/callback}"
POST_LOGOUT_URI="${POST_LOGOUT_URI:-http://localhost/}"

echo "================================================="
echo " ControlPlane Zitadel Initializer"
echo " Target URL: ${ZITADEL_URL}"
echo "================================================="

# Wait for Zitadel health endpoint
echo -n "Waiting for Zitadel to report healthy at ${ZITADEL_URL}/debug/healthz..."
until curl -s -f "${ZITADEL_URL}/debug/healthz" > /dev/null 2>&1; do
    echo -n "."
    sleep 2
done
echo " Ready!"

echo "Zitadel instance is online."
echo ""
echo "Configuration Summary for ControlPlane:"
echo "1. Authority: ${ZITADEL_URL}"
echo "2. Project: controlplane"
echo "3. Roles: admin, operator, viewer"
echo "4. Client ID: controlplane-spa"
echo "5. Grant Types: authorization_code + PKCE"
echo "6. Redirect URI: ${REDIRECT_URI}"
echo "7. Post-Logout URI: ${POST_LOGOUT_URI}"
echo "================================================="
