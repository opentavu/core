<#
.SYNOPSIS
  Release smoke test: install the managed OpenTavu solutions into a DISPOSABLE environment the
  way a stranger would, run the setup routine, and fail if the diagnostic finds a real problem.

.DESCRIPTION
  1. Imports OpenTavu Core (managed), then optionally OpenTavu Integrations (managed).
  2. Calls the Custom API tavu_InitializeConfiguration with Mode=full until Complete=true
     (the same thing the "Verify and complete configuration" button does).
  3. Calls it with Mode=diagnose and evaluates the JSON report.
  Errors fail the run. Warnings also fail it (they usually mean the seed and the schema drifted),
  unless they match -ExpectedWarnings or -AllowWarnings is set. Findings that are normal on a
  fresh install, before the installer configures AI, are listed in -ExpectedErrors.

  Requires: pac CLI (any auth profile with access to the target) and Azure CLI signed in
  (az login) with a user that is System Administrator in the target environment.

.PARAMETER EnvironmentUrl
  The disposable environment, e.g. https://opentavu-smoke.crm.dynamics.com. Never the dev environment.

.PARAMETER CoreZip
  Path to the managed Core zip, e.g. _export\OpenTavu_managed.zip.

.PARAMETER IntegrationsZip
  Optional path to the managed Integrations zip. Imported after Core.

.PARAMETER IntegrationsSettingsFile
  Optional deployment settings JSON (connection references) for the Integrations import
  (create one with: pac solution create-settings --solution-zip <zip> --settings-file <json>).

.PARAMETER SkipImport
  Only run the setup routine and the diagnostic (solutions already imported).

.EXAMPLE
  .\tools\release-smoke.ps1 -EnvironmentUrl https://opentavu-smoke.crm.dynamics.com -CoreZip .\_export\OpenTavu_managed.zip
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EnvironmentUrl,
    [string]$CoreZip,
    [string]$IntegrationsZip,
    [string]$IntegrationsSettingsFile,
    [switch]$SkipImport,
    [int]$MaxFullRuns = 10,
    [switch]$AllowWarnings,
    [string[]]$ExpectedErrors = @(
        '^No gateway is configured and System Settings has no Default AI Model'
    ),
    [string[]]$ExpectedWarnings = @(
        '^The company profile is empty'
    ),
    [string[]]$ProtectedUrls = @(
        'https://opentavu.crm.dynamics.com'
    ),
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$org = $EnvironmentUrl.TrimEnd('/')

# ---------- Safety ----------
foreach ($p in $ProtectedUrls) {
    if ($org -ieq $p.TrimEnd('/')) { throw "Refusing to run against ${org}: it is a protected (non-disposable) environment." }
}
if ($org -notmatch '^https://[^/]+\.dynamics\.com$') { throw "EnvironmentUrl must look like https://<org>.crm.dynamics.com" }
foreach ($cmd in @('pac', 'az')) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) { throw "'$cmd' is not on PATH." }
}

function Invoke-Pac {
    param([string[]]$PacArgs)
    Write-Host "> pac $($PacArgs -join ' ')" -ForegroundColor DarkGray
    & pac @PacArgs
    if ($LASTEXITCODE -ne 0) { throw "pac $($PacArgs -join ' ') failed with exit code $LASTEXITCODE" }
}

# ---------- 1. Import ----------
if (-not $SkipImport) {
    if (-not $CoreZip) { throw "-CoreZip is required unless -SkipImport is set." }
    if (-not (Test-Path $CoreZip)) { throw "Core zip not found: $CoreZip" }
    if ($CoreZip -notmatch '_managed\.zip$') { Write-Warning "The Core zip name does not end in _managed.zip. A stranger installs the MANAGED package; test that one." }

    Write-Host "`n=== Import OpenTavu Core into $org ===" -ForegroundColor Cyan
    Invoke-Pac @('solution', 'import', '--path', (Resolve-Path $CoreZip).Path, '--environment', $org, '--activate-plugins', '--async', '--max-async-wait-time', '60')

    if ($IntegrationsZip) {
        if (-not (Test-Path $IntegrationsZip)) { throw "Integrations zip not found: $IntegrationsZip" }
        Write-Host "`n=== Import OpenTavu Integrations into $org ===" -ForegroundColor Cyan
        $intArgs = @('solution', 'import', '--path', (Resolve-Path $IntegrationsZip).Path, '--environment', $org, '--activate-plugins', '--async', '--max-async-wait-time', '60')
        if ($IntegrationsSettingsFile) { $intArgs += @('--settings-file', (Resolve-Path $IntegrationsSettingsFile).Path) }
        else { Write-Warning "No -IntegrationsSettingsFile: connection references stay unbound and those flows stay off. The import itself is what is being tested." }
        Invoke-Pac $intArgs
    }
}

# ---------- 2. Token ----------
$token = & az account get-access-token --resource $org --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or -not $token) { throw "Could not get a token for $org. Run 'az login' with a user of that environment." }
$headers = @{
    Authorization      = "Bearer $token"
    'Content-Type'     = 'application/json; charset=utf-8'
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
}

function Invoke-Setup {
    param([string]$Mode)
    $uri = "$org/api/data/v9.2/tavu_InitializeConfiguration"
    $body = (@{ Mode = $Mode } | ConvertTo-Json -Compress)
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            return Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -Body $body -TimeoutSec 300
        } catch {
            if ($attempt -eq 3) { throw }
            Write-Warning "Call failed ($($_.Exception.Message)); retrying in 20 s (the Custom API can take a moment after import)."
            Start-Sleep -Seconds 20
        }
    }
}

# ---------- 3. Setup (Mode=full) ----------
Write-Host "`n=== Setup: Mode=full ===" -ForegroundColor Cyan
$run = 0
do {
    $run++
    $r = Invoke-Setup -Mode 'full'
    Write-Host "Run $run :" -NoNewline; Write-Host " $($r.Summary)"
} until ($r.Complete -or $run -ge $MaxFullRuns)
if (-not $r.Complete) { throw "Setup did not complete after $MaxFullRuns runs." }

# ---------- 4. Diagnose ----------
Write-Host "`n=== Diagnose ===" -ForegroundColor Cyan
$d = Invoke-Setup -Mode 'diagnose'
$report = $d.Report | ConvertFrom-Json

if (-not $ReportPath) {
    $dir = Join-Path $repo '_export'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $ReportPath = Join-Path $dir ("smoke-report-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
[IO.File]::WriteAllText($ReportPath, $d.Report, (New-Object System.Text.UTF8Encoding($false)))

function Test-Expected {
    param([string]$Message, [string[]]$Patterns)
    foreach ($p in $Patterns) { if ($Message -match $p) { return $true } }
    return $false
}

$realErrors = @(); $realWarnings = @(); $expected = @()
foreach ($i in @($report.items)) {
    $line = "[$($i.level)] $($i.area): $($i.message)"
    switch ($i.level) {
        'error'   { if (Test-Expected $i.message $ExpectedErrors)   { $expected += $line } else { $realErrors += $line } }
        'warning' { if (Test-Expected $i.message $ExpectedWarnings) { $expected += $line } else { $realWarnings += $line } }
    }
}

if ($expected.Count) {
    Write-Host "Expected on a fresh install (configured later by the installer):" -ForegroundColor DarkGray
    $expected | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
}
if ($realWarnings.Count) {
    Write-Host "Warnings:" -ForegroundColor Yellow
    $realWarnings | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
}
if ($realErrors.Count) {
    Write-Host "Errors:" -ForegroundColor Red
    $realErrors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
}
Write-Host "`nReport saved to $ReportPath"

$fail = ($realErrors.Count -gt 0) -or ((-not $AllowWarnings) -and $realWarnings.Count -gt 0)
if ($fail) {
    Write-Host "SMOKE TEST FAILED: $($realErrors.Count) errors, $($realWarnings.Count) warnings." -ForegroundColor Red
    exit 1
}
Write-Host "SMOKE TEST PASSED: a clean install configures itself with no unexpected findings." -ForegroundColor Green
