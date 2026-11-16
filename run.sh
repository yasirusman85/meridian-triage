#!/usr/bin/env bash
# One command: install client packages, build the SPA into Meridian.Web/wwwroot,
# then start the app (it applies database migrations on startup) at http://localhost:5080.
set -euo pipefail

cd "$(dirname "$0")"

echo "==> Building client"
(
  cd src/client
  if [ -f package-lock.json ]; then
    npm ci --no-audit --no-fund
  else
    npm install --no-audit --no-fund
  fi
  npm run build
)

echo "==> Starting Meridian at http://localhost:5080"
exec dotnet run --project src/Meridian.Web --urls http://localhost:5080
