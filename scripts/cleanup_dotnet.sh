#!/bin/bash

# Define the deployment directory
DEPLOY_DIR="/home/ubuntu/dotnet"

echo "Stopping .NET application if running..."
sudo systemctl stop dotnet-api || true

echo "Removing old deployment files..."
sudo rm -rf $DEPLOY_DIR/*

echo "Cleanup complete!"