<#
.SYNOPSIS
  Export the OpenTavu solutions from the development environment and unpack them into src/,
  so every schema change (tables, columns, forms, views, Custom APIs, plugin steps, flows)
  shows up as a reviewable git diff instead of an opaque zip.

.DESCRIPTION
  For each solution (OpenTavu -> src/Solution, OpenTavuIntegrations -> src/Solution.Integrations):
    1. pac solution export, unmanaged and managed, into _export/ (gitignored)
    2. pac solution unpack --packagetype Both (same format as the existing src/Solution)
    3. Secret guard: strips environment variable default values and current values
       (they are set per environment, never committed) and fails if anything that looks
       like a key or connection secret appears in the unpacked files.
  It never commits or pushes. Review the diff, then commit yourself.

.PARAMETER Environment
  Dataverse URL or GUID of the DEVELOPMENT environment. Default: the active pac auth profile.

.PARAMETER AuthProfile
  Name of a pac auth profile to select before exporting (pac auth list shows them).

.PARAMETER SkipIntegrations
  Only sync OpenTavu Core.

.PARAMETER NoExport
  Skip the export and unpack the zips already in _export/.

.PARAMETER Force
  Continue even if src/Solution* has uncommitted changes (they would be overwritten).

.EXAMPLE
  .\tools\sync-solution.ps1 -Environment https://opentavu.crm.dynamics.com
#>
[CmdletBinding()]
param(
    [string]$Environment,
    [string]$AuthProfile,
    [switch]$SkipIntegrations,
    [switch]$NoExport,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$exportDir = Join-Path $repo '_export'

$targets = @(
    @{ Name = 'OpenTavu';             Folder = 'src\Solution' },
    @{ Name = 'OpenTavuIntegrations'; Folder = 'src\Solution.Integrations' }
)
if ($SkipIntegrations) { $targets = @($targets[0]) }

function Invoke-Pac {
    param([string[]]$PacArgs)
    Write-Host "> pac $($PacArgs -join ' ')" -ForegroundColor DarkGray
    & pac @PacArgs
    if ($LASTEXITCODE -ne 0) { throw "pac $($PacArgs -join ' ') failed with exit code $LASTEXITCODE" }
}

# ---------- Preconditions ----------
foreach ($cmd in @('pac', 'git')) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) { throw "'$cmd' is not on PATH." }
}

$folders = $targets | ForEach-Object { $_.Folder -replace '\\', '/' }
$dirty = & git -C $repo status --porcelain -- $folders
if ($dirty -and -not $Force) {
    Write-Host $($dirty -join "`n")
    throw "src/Solution* has uncommitted changes. Commit or stash them first, or rerun with -Force."
}

if ($AuthProfile) { Invoke-Pac @('auth', 'select', '--name', $AuthProfile) }

Write-Host "`nSource environment:" -ForegroundColor Cyan
if ($Environment) { Write-Host "  $Environment" } else { Invoke-Pac @('org', 'who') }

New-Item -ItemType Directory -Force -Path $exportDir | Out-Null

# ---------- Export + unpack ----------
foreach ($t in $targets) {
    $zip        = Join-Path $exportDir "$($t.Name).zip"
    $zipManaged = Join-Path $exportDir "$($t.Name)_managed.zip"
    $folder     = Join-Path $repo $t.Folder

    Write-Host "`n=== $($t.Name) -> $($t.Folder) ===" -ForegroundColor Cyan

    if (-not $NoExport) {
        $envArgs = @(); if ($Environment) { $envArgs = @('--environment', $Environment) }
        Invoke-Pac (@('solution', 'export', '--name', $t.Name, '--path', $zip, '--overwrite', '--async') + $envArgs)
        Invoke-Pac (@('solution', 'export', '--name', $t.Name, '--path', $zipManaged, '--managed', '--overwrite', '--async') + $envArgs)
    }
    foreach ($z in @($zip, $zipManaged)) {
        if (-not (Test-Path $z)) { throw "Missing $z (run without -NoExport)." }
    }

    # Both: reads <name>.zip and <name>_managed.zip side by side, same layout as the existing src/Solution.
    Invoke-Pac @('solution', 'unpack', '--zipfile', $zip, '--folder', $folder, '--packagetype', 'Both', '--allowDelete', '--allowWrite', '--clobber')
}

# ---------- Line endings ----------
# .gitattributes stores text as LF. Solution Packager writes CRLF, which makes git print one
# "CRLF will be replaced by LF" warning per file. Normalize here (BOM preserved, UTF-16 skipped).
$lfExt = @('.xml', '.json', '.js', '.html', '.htm', '.css', '.resx', '.yml', '.yaml', '.txt', '.svg')
$normalized = 0
foreach ($t in $targets) {
    Get-ChildItem (Join-Path $repo $t.Folder) -Recurse -File | Where-Object { $lfExt -contains $_.Extension.ToLowerInvariant() } | ForEach-Object {
        $bytes = [IO.File]::ReadAllBytes($_.FullName)
        if ($bytes.Length -ge 2 -and (($bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) -or ($bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF))) { return }
        $text = [Text.Encoding]::UTF8.GetString($bytes)
        if ($text.Contains("`r`n")) {
            [IO.File]::WriteAllBytes($_.FullName, [Text.Encoding]::UTF8.GetBytes($text.Replace("`r`n", "`n")))
            $normalized++
        }
    }
}
Write-Host "`nLine endings: $normalized files normalized to LF."

# ---------- Secret guard ----------
Write-Host "`n=== Secret guard ===" -ForegroundColor Cyan
$stripped = @()
foreach ($t in $targets) {
    $evDir = Join-Path (Join-Path $repo $t.Folder) 'environmentvariabledefinitions'
    if (-not (Test-Path $evDir)) { continue }

    Get-ChildItem $evDir -Recurse -Filter 'environmentvariabledefinition.xml' | ForEach-Object {
        $xml = [IO.File]::ReadAllText($_.FullName)
        $clean = [regex]::Replace($xml, '(?s)\s*<defaultvalue>.*?</defaultvalue>', '')
        if ($clean -ne $xml) {
            [IO.File]::WriteAllText($_.FullName, $clean, (New-Object System.Text.UTF8Encoding($false)))
            $stripped += "default value: $($_.Directory.Name)"
        }
    }
    Get-ChildItem $evDir -Recurse -Filter 'environmentvariablevalues.json' | ForEach-Object {
        Remove-Item $_.FullName -Force
        $stripped += "current value: $($_.Directory.Name)"
    }
}
if ($stripped.Count -gt 0) {
    Write-Host "Stripped (environment variables are set per environment, never committed):" -ForegroundColor Yellow
    $stripped | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
} else {
    Write-Host "No environment variable values found."
}

$patterns = @(
    '(?<![A-Za-z0-9])sk-(proj-)?[A-Za-z0-9_\-]{20,}', # OpenAI style keys
    'AccountKey=[A-Za-z0-9+/=]{20,}',                # Azure Storage connection strings
    'SharedAccessSignature=|[?&]sig=[A-Za-z0-9%]{20,}',
    '-----BEGIN [A-Z ]*PRIVATE KEY-----',
    '(?i)(api[-_]?key|client[-_]?secret|password)"?\s*[:=]\s*"[^"\s]{12,}"'
)
$textExt = @('.xml', '.json', '.js', '.html', '.htm', '.css', '.resx', '.yml', '.yaml', '.txt')
$hits = @()
foreach ($t in $targets) {
    $root = Join-Path $repo $t.Folder
    Get-ChildItem $root -Recurse -File | Where-Object { $textExt -contains $_.Extension.ToLowerInvariant() } | ForEach-Object {
        $file = $_
        foreach ($p in $patterns) {
            $m = Select-String -Path $file.FullName -Pattern $p -AllMatches -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($m) { $hits += "$($file.FullName.Substring($repo.Length + 1)):$($m.LineNumber)  [$p]" }
        }
    }
}
if ($hits.Count -gt 0) {
    Write-Host "Possible secrets in the unpacked solution. Do NOT commit until each one is removed:" -ForegroundColor Red
    $hits | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
Write-Host "No secret patterns found."

# ---------- Summary ----------
Write-Host "`n=== Result ===" -ForegroundColor Cyan
foreach ($t in $targets) {
    $sx = Join-Path (Join-Path $repo $t.Folder) 'Other\Solution.xml'
    if (Test-Path $sx) {
        $ver = ([xml](Get-Content $sx -Raw)).ImportExportXml.SolutionManifest.Version
        Write-Host ("{0,-22} version {1}" -f $t.Name, $ver)
    }
}
$status = & git -C $repo status --porcelain --untracked-files=all -- $folders
$added    = @($status | Where-Object { $_ -match '^\?\?|^A ' }).Count
$modified = @($status | Where-Object { $_ -match '^ M|^M ' }).Count
$deleted  = @($status | Where-Object { $_ -match '^ D|^D ' }).Count
Write-Host "Files: $added new, $modified modified, $deleted deleted."
if ($status) {
    Write-Host "`nNext: review, then commit schema and code together:" -ForegroundColor Cyan
    Write-Host "  git add src/Solution src/Solution.Integrations"
    Write-Host "  git diff --cached --stat -- src/Solution src/Solution.Integrations"
    Write-Host "  git commit -m 'chore(solution): sync from dev <version>'"
} else {
    Write-Host "No schema changes since the last sync."
}
