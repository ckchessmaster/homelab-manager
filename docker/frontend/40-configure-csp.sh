#!/bin/sh
set -e

# Dynamically scope Content-Security-Policy connect-src and frame-src if an authority or extra origins are configured
if [ -n "$ZITADEL_AUTHORITY" ] || [ -n "$CSP_CONNECT_SRC" ] || [ -n "$CSP_FRAME_SRC" ]; then
    CONNECT_SOURCES="'self' ws: wss:"
    FRAME_SOURCES="'self'"

    if [ -n "$ZITADEL_AUTHORITY" ]; then
        CONNECT_SOURCES="$CONNECT_SOURCES $ZITADEL_AUTHORITY"
        FRAME_SOURCES="$FRAME_SOURCES $ZITADEL_AUTHORITY"
    fi

    if [ -n "$CSP_CONNECT_SRC" ]; then
        CONNECT_SOURCES="$CONNECT_SOURCES $CSP_CONNECT_SRC"
    fi

    if [ -n "$CSP_FRAME_SRC" ]; then
        FRAME_SOURCES="$FRAME_SOURCES $CSP_FRAME_SRC"
    fi

    sed -i "s|connect-src 'self' ws: wss:;|connect-src $CONNECT_SOURCES;|g" /etc/nginx/nginx.conf
    sed -i "s|frame-src 'self';|frame-src $FRAME_SOURCES;|g" /etc/nginx/nginx.conf
fi
