#!/bin/bash
# UIAutomation Server — One-command installer for Amazon Linux 2023
# Usage: curl -fsSL https://raw.githubusercontent.com/oddgames/ui-automation/main/server/install.sh | bash
set -euo pipefail

echo ""
echo "==================================="
echo "  UIAutomation Server — Installing"
echo "==================================="
echo ""

# 1. Install Docker
echo "[1/6] Installing Docker..."
sudo yum update -y -q
sudo yum install -y -q docker git
sudo systemctl enable --now docker
sudo usermod -a -G docker ec2-user

# 2. Install Docker Compose
echo "[2/6] Installing Docker Compose..."
sudo mkdir -p /usr/local/lib/docker/cli-plugins
sudo curl -fsSL https://github.com/docker/compose/releases/latest/download/docker-compose-linux-x86_64 \
  -o /usr/local/lib/docker/cli-plugins/docker-compose
sudo chmod +x /usr/local/lib/docker/cli-plugins/docker-compose

# 3. Install Node.js
echo "[3/6] Installing Node.js..."
curl -fsSL https://rpm.nodesource.com/setup_20.x | sudo bash - > /dev/null 2>&1
sudo yum install -y -q nodejs

# 4. Clone repo
echo "[4/6] Cloning repository..."
sudo mkdir -p /opt
cd /opt
if [ -d uiat ]; then
    echo "   /opt/uiat already exists, pulling latest..."
    cd uiat && git pull --quiet
else
    sudo git clone --quiet https://github.com/oddgames/ui-automation.git uiat
fi
sudo chown -R ec2-user:ec2-user /opt/uiat
cd /opt/uiat

# 5. Build web viewer + start server
echo "[5/6] Building web viewer and starting server..."
cd web && npm ci --silent && npm run build --silent && cd ..
cd server && sg docker -c "docker compose up -d --build" && cd ..

# 6. Enable auto-updater
echo "[6/6] Enabling auto-updater..."
chmod +x /opt/uiat/server/auto-update.sh
sudo cp /opt/uiat/server/uiat-updater.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now uiat-updater

IP=$(curl -s http://169.254.169.254/latest/meta-data/public-ipv4 2>/dev/null || echo "YOUR_IP")

echo ""
echo "==================================="
echo "  Done!"
echo "==================================="
echo ""
echo "  Server: http://$IP"
echo ""
echo "  Auto-updates every 5 minutes from GitHub."
echo "  Push to main and it deploys itself."
echo ""
echo "  Logs:   tail -f /var/log/uiat-update.log"
echo "  Server: cd /opt/uiat/server && docker compose logs -f"
echo ""
