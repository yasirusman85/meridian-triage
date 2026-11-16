# One command: install client packages, build the SPA into Meridian.Web/wwwroot,
# then start the app (it applies database migrations on startup) at http://localhost:5080.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Vite 8 needs Node 20.19+ or 22.12+; fail early with a clear message instead of mid-build.
$nv = [version]((node --version).TrimStart('v'))
if (-not (($nv.Major -eq 20 -and $nv.Minor -ge 19) -or ($nv.Major -eq 22 -and $nv.Minor -ge 12) -or $nv.Major -gt 22)) { throw "Node $nv found; Node 20.19+ or 22.12+ is required." }
if (-not (dotnet --list-sdks | Select-String '^8\.')) { throw '.NET 8 SDK is required.' }

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
