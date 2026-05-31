#!/bin/bash
# Run this on the OVH server (ssh in first)
# Usage: bash deploy.sh

set -e
OVH_HOST="51.79.156.217"
OVH_USER="ubuntu"  # change if different

echo "=== Deploying db-tula to OVH ==="

# 1. Install .NET 9 if not present
if ! command -v dotnet &>/dev/null; then
    wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
    chmod +x dotnet-install.sh
    ./dotnet-install.sh --channel 9.0 --install-dir /usr/share/dotnet
    ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
fi

# 2. Create directories
sudo mkdir -p /var/www/dbtula-api
sudo mkdir -p /var/www/dbtula-web
sudo chown -R www-data:www-data /var/www/dbtula-api /var/www/dbtula-web

# 3. Copy API files (run from Windows side via scp before this)
# scp -r deploy/api/* ubuntu@51.79.156.217:/var/www/dbtula-api/
# scp deploy/appsettings.Production.json ubuntu@51.79.156.217:/var/www/dbtula-api/

# 4. Copy React build
# scp -r web/dbtula-web/dist/* ubuntu@51.79.156.217:/var/www/dbtula-web/

# 5. Install systemd service
sudo cp /var/www/dbtula-api/dbtula-api.service /etc/systemd/system/dbtula-api.service
sudo systemctl daemon-reload
sudo systemctl enable dbtula-api
sudo systemctl restart dbtula-api

# 6. Configure Nginx
sudo cp /var/www/dbtula-api/nginx-dbtula.conf /etc/nginx/sites-available/dbtula
sudo ln -sf /etc/nginx/sites-available/dbtula /etc/nginx/sites-enabled/dbtula
sudo nginx -t && sudo systemctl reload nginx

echo "=== Deployment complete ==="
echo "API status: $(sudo systemctl is-active dbtula-api)"
echo "Visit: http://dbtula.dhanman.com"
