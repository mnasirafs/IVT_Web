<#
.SYNOPSIS
  Build IVT (Azure configuration), deploy it to the staging slot, smoke-test it, and optionally swap it into production.

.EXAMPLE
  .\scripts\deploy-azure.ps1            # build + deploy to https://ivt-staging.azurewebsites.net, then test it in a browser
  .\scripts\deploy-azure.ps1 -Swap      # same, then swap staging into production (no downtime)
  .\scripts\deploy-azure.ps1 -SwapOnly  # swap what is already on staging into production

  Requires: Visual Studio (MSBuild) and the Azure CLI, signed in with `az login`.
  Roll back a bad release by swapping again: az webapp deployment slot swap -g rg-carrierinvoice-dev-ase-001 -n IVT --slot staging
#>
param(
    [switch]$Swap,
    [switch]$SwapOnly,
    [string]$ResourceGroup = "rg-carrierinvoice-dev-ase-001",
    [string]$App = "IVT",
    [string]$Slot = "staging"
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$stagingUrl = "https://$($App.ToLower())-$Slot.azurewebsites.net"

function Test-Site([string]$url) {
    for ($i = 1; $i -le 6; $i++) {
        try {
            $status = Invoke-RestMethod "$url/Entra/Status" -TimeoutSec 120
            if (-not $status.enabled -or -not $status.secretConfigured) { throw "Entra sign-in is not configured on $url" }
            Write-Host "OK  $url (redirect $($status.redirectUri))" -ForegroundColor Green
            return
        } catch {
            if ($i -eq 6) { throw "Smoke test failed for ${url}: $($_.Exception.Message)" }
            Start-Sleep -Seconds 15   # the site is still starting after the restart
        }
    }
}

if (-not $SwapOnly) {
    $msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    if (-not $msbuild) { throw "MSBuild not found; install Visual Studio." }

    Write-Host "Building (Azure configuration)..."
    & $msbuild "$repo\AFSDataGateway\IVT.csproj" /p:Configuration=Azure /p:DeployOnBuild=true /v:q /nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }

    $site = "$repo\AFSDataGateway\obj\Azure\Package\PackageTmp"
    if (Test-Path "$site\App_Data\ivt-chat.secrets.config") { throw "Refusing to deploy: secrets file is in the package." }
    $zip = Join-Path ([IO.Path]::GetTempPath()) "ivt-azure.zip"
    Remove-Item $zip -ErrorAction SilentlyContinue
    Compress-Archive -Path "$site\*" -DestinationPath $zip

    Write-Host "Deploying to slot '$Slot'..."
    az webapp deploy -g $ResourceGroup -n $App --slot $Slot --type zip --src-path $zip --clean true --restart true -o none
    if ($LASTEXITCODE -ne 0) { throw "Deployment failed." }
    Remove-Item $zip -ErrorAction SilentlyContinue
    Test-Site $stagingUrl
    Write-Host "Check $stagingUrl in a browser (sign in, open a few pages) before swapping."
}

if ($Swap -or $SwapOnly) {
    Write-Host "Swapping '$Slot' into production..."
    az webapp deployment slot swap -g $ResourceGroup -n $App --slot $Slot --target-slot production -o none
    if ($LASTEXITCODE -ne 0) { throw "Swap failed." }
    Test-Site "https://$($App.ToLower()).azurewebsites.net"
    Write-Host "Production updated. The previous version is now on '$Slot' (swap again to roll back)."
}
