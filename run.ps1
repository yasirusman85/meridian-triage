# One command: install client packages, build the SPA into Meridian.Web/wwwroot,
# then start the app (it applies database migrations on startup) at http://localhost:5080.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Write-Host '==> Building client'
Push-Location src/client
try {
    if (Test-Path package-lock.json) { npm ci --no-audit --no-fund } else { npm install --no-audit --no-fund }
    if ($LASTEXITCODE -ne 0) { throw 'npm install failed' }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'client build failed' }
}
finally {
    Pop-Location
}

Write-Host '==> Starting Meridian at http://localhost:5080'
dotnet run --project src/Meridian.Web --urls http://localhost:5080
exit $LASTEXITCODE
