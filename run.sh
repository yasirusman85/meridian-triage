#!/usr/bin/env bash
# One command: install client packages, build the SPA into Meridian.Web/wwwroot,
# then start the app (it applies database migrations on startup) at http://localhost:5080.
set -euo pipefail

cd "$(dirname "$0")"

# Vite 8 needs Node 20.19+ or 22.12+; fail early with a clear message instead of mid-build.
node -e 'const [a,b]=process.versions.node.split(".").map(Number); if(!((a===20&&b>=19)||(a===22&&b>=12)||a>22)){console.error("Node "+process.versions.node+" found; Node 20.19+ or 22.12+ is required.");process.exit(1)}'
dotnet --list-sdks | grep -q '^8\.' || { echo ".NET 8 SDK is required." >&2; exit 1; }

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
