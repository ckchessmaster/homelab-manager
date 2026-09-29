#!/usr/bin/env bash
# ==============================================================================
# ControlPlane Compute Node Agent Linux Setup Script
# ==============================================================================
# Usage:
#   curl -sSL https://controlplane.example.com/api/v1/agents/install.sh | sudo bash -s -- \
#     --hub-url wss://controlplane.example.com/agent-hub \
#     --token <TOKEN> \
#     --node-id <NODE_ID> \
#     [--insecure]
# ==============================================================================

set -euo pipefail

HUB_URL=""
TOKEN=""
NODE_ID=""
BINARY_URL=""
INSTALL_DIR="/usr/local/bin"
SERVICE_NAME="controlplane-agent"
INSECURE=false

usage() {
    cat <<EOF
Usage: $0 --hub-url <url> --token <token> --node-id <id> [options]

Required Arguments:
  --hub-url <url>       WebSocket URL of ControlPlane (e.g. wss://cp.example.com/agent-hub)
  --token <token>       Node authentication token / API key
  --node-id <id>        Unique Host GUID or node identifier

Options:
  --binary-url <url>    Optional direct download URL for agent binary
  --install-dir <dir>   Installation directory (default: /usr/local/bin)
  --service-name <name> Systemd service name (default: controlplane-agent)
  --insecure            Bypass TLS/SSL certificate verification
  -h, --help            Show this help message
EOF
    exit 1
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        --hub-url)
            HUB_URL="$2"
            shift 2
            ;;
        --token)
            TOKEN="$2"
            shift 2
            ;;
        --node-id)
            NODE_ID="$2"
            shift 2
            ;;
        --binary-url)
            BINARY_URL="$2"
            shift 2
            ;;
        --install-dir)
            INSTALL_DIR="$2"
            shift 2
            ;;
        --service-name)
            SERVICE_NAME="$2"
            shift 2
            ;;
        --insecure)
            INSECURE=true
            shift
            ;;
        -h|--help)
            usage
            ;;
        *)
            echo "Unknown option: $1" >&2
            usage
            ;;
    esac
done

if [ -z "$HUB_URL" ] || [ -z "$TOKEN" ] || [ -z "$NODE_ID" ]; then
    echo "Error: --hub-url, --token, and --node-id are required arguments." >&2
    usage
fi

# 1. Require root elevation
if [ "$(id -u)" -ne 0 ]; then
    echo "Error: ControlPlane Agent installation requires root privileges. Please run with sudo or as root." >&2
    exit 1
fi

echo "================================================="
echo "   ControlPlane Compute Node Agent Linux Setup   "
echo "================================================="
echo "Target Node ID:  $NODE_ID"
echo "Hub URL:         $HUB_URL"
echo "Install Dir:     $INSTALL_DIR"
echo "Service Name:    $SERVICE_NAME"
if [ "$INSECURE" = true ]; then
    echo "TLS Validation:  Disabled (--insecure active)"
fi
echo ""

# 2. Detect CPU architecture
UNAME_M="$(uname -m)"
case "$UNAME_M" in
    x86_64|amd64)
        ARCH="linux-amd64"
        ;;
    aarch64|arm64)
        ARCH="linux-arm64"
        ;;
    armv7l|armv6l)
        ARCH="linux-armv7"
        ;;
    *)
        echo "Error: Unsupported CPU architecture '$UNAME_M'." >&2
        exit 1
        ;;
esac
echo "Detected architecture: $ARCH"

# 3. Derive BinaryUrl if not specified
if [ -z "$BINARY_URL" ]; then
    HTTP_BASE="$HUB_URL"
    SCHEME="http"
    if [[ "$HTTP_BASE" =~ ^wss:// ]]; then
        SCHEME="https"
        HOST_PORT="${HTTP_BASE#wss://}"
    elif [[ "$HTTP_BASE" =~ ^ws:// ]]; then
        SCHEME="http"
        HOST_PORT="${HTTP_BASE#ws://}"
    elif [[ "$HTTP_BASE" =~ ^https:// ]]; then
        SCHEME="https"
        HOST_PORT="${HTTP_BASE#https://}"
    else
        SCHEME="http"
        HOST_PORT="${HTTP_BASE#http://}"
    fi
    HOST_PORT="${HOST_PORT%%/*}"
    BINARY_URL="${SCHEME}://${HOST_PORT}/api/v1/agents/binaries/${ARCH}"
fi

echo "Agent binary download URL: $BINARY_URL"

# 4. Prepare target directory
mkdir -p "$INSTALL_DIR"
DEST_BIN="${INSTALL_DIR}/controlplane-agent"

# 5. Stop existing service if active
if command -v systemctl >/dev/null 2>&1 && systemctl is-active --quiet "$SERVICE_NAME" 2>/dev/null; then
    echo "Stopping active service '$SERVICE_NAME' for upgrade..."
    systemctl stop "$SERVICE_NAME" || true
fi

# 6. Download agent binary
TEMP_BIN="/tmp/controlplane-agent-$$.tmp"
rm -f "$TEMP_BIN"

CURL_EXTRA=""
WGET_EXTRA=""
if [ "$INSECURE" = true ]; then
    CURL_EXTRA="-k"
    WGET_EXTRA="--no-check-certificate"
fi

echo "Downloading agent binary..."
if command -v curl >/dev/null 2>&1; then
    curl -fsSL $CURL_EXTRA "$BINARY_URL" -o "$TEMP_BIN"
elif command -v wget >/dev/null 2>&1; then
    wget -q $WGET_EXTRA -O "$TEMP_BIN" "$BINARY_URL"
else
    echo "Error: Neither 'curl' nor 'wget' was found on this system. Please install one of them." >&2
    exit 1
fi

if [ ! -s "$TEMP_BIN" ]; then
    echo "Error: Downloaded binary is empty or missing." >&2
    rm -f "$TEMP_BIN"
    exit 1
fi

mv -f "$TEMP_BIN" "$DEST_BIN"
chmod +x "$DEST_BIN"
echo "Binary installed to $DEST_BIN"

# 7. Configure and start systemd service
if command -v systemctl >/dev/null 2>&1; then
    SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"
    INSECURE_CLI_FLAG=""
    if [ "$INSECURE" = true ]; then
        INSECURE_CLI_FLAG=" --insecure"
    fi

    echo "Configuring systemd service ($SERVICE_FILE)..."
    cat <<EOF > "$SERVICE_FILE"
[Unit]
Description=ControlPlane Compute Node Agent
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
ExecStart=${DEST_BIN} --hub-url ${HUB_URL}${INSECURE_CLI_FLAG} --token ${TOKEN} --node-id ${NODE_ID}
Restart=always
RestartSec=5
KillMode=process
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
EOF

    systemctl daemon-reload
    systemctl enable "$SERVICE_NAME"
    echo "Starting $SERVICE_NAME service..."
    systemctl restart "$SERVICE_NAME"

    sleep 2
    if systemctl is-active --quiet "$SERVICE_NAME"; then
        echo "================================================="
        echo " ControlPlane Agent installed & active! (NodeId: $NODE_ID)"
        echo " Outbound WebSocket handshake in progress."
        echo "================================================="
    else
        echo "Warning: Service was started but reports inactive state." >&2
        echo "Inspect logs: journalctl -u $SERVICE_NAME -n 25 --no-pager" >&2
    fi
else
    echo "Warning: systemctl not detected on this host."
    echo "Agent binary installed at $DEST_BIN. Run manually:"
    echo "  $DEST_BIN --hub-url \"$HUB_URL\" --token \"$TOKEN\" --node-id \"$NODE_ID\""
fi
