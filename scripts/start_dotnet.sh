# Create systemd service for .NET API
cat <<EOF | sudo tee /etc/systemd/system/dotnet-api.service
[Unit]
Description=.NET API Service
After=network.target

[Service]
WorkingDirectory=/home/ubuntu/dotnet/publish
ExecStart=$(which dotnet) /home/ubuntu/dotnet/publish/TravelAd_Api.dll
Restart=always
User=ubuntu
Group=ubuntu
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1
ExecStartPre=/bin/sleep 5

[Install]
WantedBy=multi-user.target
EOF

# Reload systemd to apply changes
sudo systemctl daemon-reload

# Set permissions for the service directory
sudo chown -R ubuntu:ubuntu /home/ubuntu/dotnet/publish

# Enable and start the .NET API service
sudo systemctl enable dotnet-api
sudo systemctl restart dotnet-api  # Restart in case it's already running

echo ".NET API service has been successfully started!"