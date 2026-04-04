#!/bin/bash
# Auto-update script — checks GitHub for new commits every 5 minutes.
# Rebuilds web viewer and restarts server on changes.
#
# Install as systemd service:
#   sudo cp /opt/uiat/server/uiat-updater.service /etc/systemd/system/
#   sudo systemctl enable --now uiat-updater

REPO_DIR="/opt/uiat"
LOG="/var/log/uiat-update.log"

while true; do
    sleep 300

    cd "$REPO_DIR" || exit 1

    LOCAL=$(git rev-parse HEAD)
    git fetch origin main --quiet 2>/dev/null
    REMOTE=$(git rev-parse origin/main)

    if [ "$LOCAL" != "$REMOTE" ]; then
        echo "[$(date)] New commits detected, updating..." >> "$LOG"
        git pull origin main --quiet

        # Rebuild web viewer
        if [ -d web ]; then
            cd web
            npm ci --silent
            npm run build --silent
            cd "$REPO_DIR"
        fi

        # Rebuild and restart server
        cd server
        docker compose up -d --build
        echo "[$(date)] Deploy complete." >> "$LOG"
    fi
done
