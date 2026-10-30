<#
.SYNOPSIS
  Import OpenTavu configuration files written by tools/export-config.ps1 into an environment (upsert, idempotent).

.DESCRIPTION
  Reads manifest.json and one JSON file per table from -InputDir and, table by table in the manifest's order:
  finds each row in the target by its natural key (never by GUID), creates it when missing, and updates only the
  columns whose value differs. A second run changes nothing. Lookups are resolved in the target by the referenced
  row's key (a code, a name, the task key, a user's email, a team's name); a reference that does not resolve is left
  unchanged and listed at the end as a manual step. File columns (the company profile's logo) are uploaded only when
  their content differs. Active or inactive state is carried.

  Unlike the setup routine's seed, which only fills empty fields, an import makes the carried columns equal to the
  files: use it to move a configured firm to another environment. Rows that exist only in the target are left alone.

  Use -WhatIf first: it reads the target and reports what would be created or changed, writing nothing.

  Not imported, because export-config.ps1 never exports them: secrets, environment variable values, business data,
  users with meeting sync on, the gateway's application user. The script lists them as manual steps.

  Requires Azure CLI signed in (az login) with a user that is System Administrator in the target, and the OpenTavu
  solution installed there (run Verify Setup first, so the seeded reference rows the files point at exist).

.PARAMETER EnvironmentUrl
  The target environment, e.g. https://contoso.crm.dynamics.com.

.PARAMETER InputDir
  The folder export-config.ps1 wrote.

.EXAMPLE
  .\tools\import-config.ps1 -EnvironmentUrl https://contoso-prod.crm.dynamics.com -InputDir ..\config\contoso -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)][string]$EnvironmentUrl,
    [Parameter(Mandatory = $true)][string]$InputDir
)

$ErrorActionPreference = 'Stop'
$org = $EnvironmentUrl.TrimEnd('/')
if ($org -notmatch '^https://[^/]+\.dynamics\.com$') { throw "EnvironmentUrl must look like https://<org>.crm.dynamics.com" }
if (-not (Get-Command az -ErrorAction SilentlyContinue)) { throw "'az' is not on PATH." }
$manifestPath = Join-Path $InputDir 'manifest.json'
if (-not (Test-Path $manifestPath)) { throw "No manifest.json in $InputDir. Run export-config.ps1 first." }
$manifest = Get-Content -Raw -Encoding UTF8 $manifestPath | ConvertFrom-Json

# ---------- Web API ----------
$token = & az account get-access-token --resource $org --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or -not $token) { throw "Could not get a token for $org. Run 'az login' with a user of that environment." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'Content-Type'     = 'application/json; charset=utf-8'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
}
$api = "$org/api/data/v9.2"

function Get-Api([string]$Path) { Invoke-RestMethod -Method Get -Uri "$api/$Path" -Headers $headers -TimeoutSec 120 }
function Send-Api([string]$Method, [string]$Path, $Body) {
    $json = $Body | ConvertTo-Json -Depth 10
    Invoke-RestMethod -Method $Method -Uri "$api/$Path" -Headers ($headers + @{ Prefer = 'return=representation' }) `
        -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec 120
}

$metaCache = @{}
function Get-Meta([string]$Table) {
    if (-not $metaCache.ContainsKey($Table)) {
        $m = Get-Api "EntityDefinitions(LogicalName='$Table')?`$select=EntitySetName,PrimaryIdAttribute&`$expand=ManyToOneRelationships(`$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName)"
        $nav = @{}
        foreach ($r in $m.ManyToOneRelationships) { $nav[$r.ReferencingAttribute] = $r.ReferencingEntityNavigationPropertyName }
        $metaCache[$Table] = @{ Set = $m.EntitySetName; Id = $m.PrimaryIdAttribute; Nav = $nav }
    }
    $metaCache[$Table]
}

function Format-Literal($Value) {
    if ($Value -is [datetime]) { return $Value.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') }
    if ($Value -is [bool]) { return $Value.ToString().ToLowerInvariant() }
    if ($Value -is [int] -or $Value -is [long] -or $Value -is [double] -or $Value -is [decimal]) { return [Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture) }
    if ($Value -match '^\d{4}-\d{2}-\d{2}T') { return $Value }
    "'" + ("$Value" -replace "'", "''") + "'"
}

# A reference resolved in the target: the id of the row whose key field holds the key, or $null (cached).
$refCache = @{}
function Resolve-Ref($Ref) {
    $k = "$($Ref.'@ref')|$($Ref.keyField)|$($Ref.key)"
    if (-not $refCache.ContainsKey($k)) {
        $m = Get-Meta $Ref.'@ref'
        $found = @((Get-Api "$($m.Set)?`$select=$($m.Id)&`$filter=$($Ref.keyField) eq $(Format-Literal $Ref.key)").value)
        $refCache[$k] = if ($found.Count -eq 1) { $found[0].($m.Id) } else { $null }
        if ($found.Count -gt 1) { Write-Warning "$($Ref.'@ref') has $($found.Count) rows with $($Ref.keyField) = $($Ref.key); the reference is not set." }
    }
    $refCache[$k]
}

function Test-Same($A, $B) {
    if (($null -eq $A -or "$A" -eq '') -and ($null -eq $B -or "$B" -eq '')) { return $true }
    if ($null -eq $A -or $null -eq $B) { return $false }
    $numA = 0.0; $numB = 0.0
    if (($A -isnot [string]) -and ($B -isnot [string]) -and [double]::TryParse("$A", [ref]$numA) -and [double]::TryParse("$B", [ref]$numB)) { return $numA -eq $numB }
    return "$A" -ceq "$B"
}

$manual = New-Object System.Collections.Generic.List[string]
$totals = [ordered]@{}

foreach ($table in $manifest.tables) {
    $doc = Get-Content -Raw -Encoding UTF8 (Join-Path $InputDir "$table.json") | ConvertFrom-Json
    $m = Get-Meta $table
    $singleton = ($doc.key[0] -eq '@singleton')
    $count = [ordered]@{ created = 0; updated = 0; unchanged = 0; skipped = 0 }

    foreach ($row in $doc.rows) {
        $f = $row.fields
        $names = @($f.PSObject.Properties.Name)
        $label = if ($singleton) { $table } else { "$table [" + (($doc.key | ForEach-Object { $v = $f.$_; if ($v.'@ref') { $v.key } else { $v } }) -join ', ') + "]" }

        # 1. The values to write: plain columns as they are, references resolved to the target's ids.
        $want = [ordered]@{}; $binds = [ordered]@{}; $unresolved = @()
        foreach ($n in $names) {
            $v = $f.$n
            if ($null -ne $v -and $v.PSObject.Properties['@ref']) {
                $id = Resolve-Ref $v
                if ($id) { $binds[$n] = $id } else { $unresolved += $n; $manual.Add("$label.${n}: no $($v.'@ref') with $($v.keyField) = $($v.key) in the target; set it by hand.") }
            } else { $want[$n] = $v }
        }
        $refNull = @($names | Where-Object { $null -eq $f.$_ -and $m.Nav.ContainsKey($_) })
        foreach ($n in $refNull) { $want.Remove($n); $binds[$n] = $null }

        # 2. The row in the target, by its key.
        $selectCols = @($want.Keys) + @($binds.Keys | ForEach-Object { "_$($_)_value" }) + @('statecode', 'statuscode', $m.Id)
        if ($singleton) {
            $found = @((Get-Api "$($m.Set)?`$select=$($selectCols -join ',')&`$top=2").value)
            if ($found.Count -gt 1) { throw "$table should have one row and has more." }
        } else {
            $filter = @()
            foreach ($k in $doc.key) {
                if ($binds.Contains($k)) { $filter += "_$($k)_value eq $($binds[$k])" }
                elseif ($unresolved -contains $k) { $filter = $null; break }
                else { $filter += "$k eq $(Format-Literal $want[$k])" }
            }
            if ($null -eq $filter) { Write-Warning "$label skipped: its key refers to a row the target does not have."; $count.skipped++; continue }
            $found = @((Get-Api "$($m.Set)?`$select=$($selectCols -join ',')&`$filter=$($filter -join ' and ')").value)
            if ($found.Count -gt 1) { Write-Warning "$label skipped: $($found.Count) rows in the target have this key."; $count.skipped++; continue }
        }
        $current = if ($found.Count -eq 1) { $found[0] } else { $null }

        # 3. What differs.
        $body = [ordered]@{}; $changed = @()
        foreach ($n in $want.Keys) {
            if ($null -eq $current -or -not (Test-Same $want[$n] $current.$n)) { $body[$n] = $want[$n]; $changed += $n }
        }
        foreach ($n in $binds.Keys) {
            $nav = $m.Nav[$n]
            if (-not $nav) { throw "No navigation property for $table.$n in the target." }
            $now = if ($current) { $current."_$($n)_value" } else { $null }
            if ($null -eq $binds[$n]) {
                if ($now) { $changed += $n } # cleared after the main update
            } elseif ("$now" -ne "$($binds[$n])") {
                $body["$nav@odata.bind"] = "/$((Get-Meta (($f.$n).'@ref')).Set)($($binds[$n]))"; $changed += $n
            }
        }
        $stateChange = $null -ne $row.statecode -and ($null -eq $current -or [int]$current.statecode -ne [int]$row.statecode)
        if ($stateChange) { $changed += 'statecode' }
        $fileChanges = @()
        if ($row.files) {
            foreach ($p in $row.files.PSObject.Properties) {
                $same = $false
                if ($current) {
                    $tmp = [IO.Path]::GetTempFileName()
                    try {
                        Invoke-WebRequest -Method Get -Uri "$api/$($m.Set)($($current.($m.Id)))/$($p.Name)/`$value" -Headers $headers -OutFile $tmp -UseBasicParsing | Out-Null
                        $same = ((Get-FileHash -Algorithm SHA256 $tmp).Hash -eq $p.Value.sha256)
                    } catch { $same = $false } finally { Remove-Item $tmp -ErrorAction SilentlyContinue -WhatIf:$false }
                }
                if (-not $same) { $fileChanges += $p; $changed += $p.Name }
            }
        }

        if ($changed.Count -eq 0) { $count.unchanged++; Write-Verbose "$label unchanged"; continue }
        $action = if ($current) { "update $($changed -join ', ')" } else { 'create' }
        if (-not $PSCmdlet.ShouldProcess($label, $action)) { if ($current) { $count.updated++ } else { $count.created++ }; continue }

        # 4. Write.
        if ($current) {
            $id = $current.($m.Id)
            if ($body.Count) { Send-Api 'PATCH' "$($m.Set)($id)" $body | Out-Null }
        } else {
            $id = (Send-Api 'POST' $m.Set $body).($m.Id)
        }
        foreach ($n in @($binds.Keys | Where-Object { $null -eq $binds[$_] -and $current -and $current."_$($_)_value" })) {
            Invoke-RestMethod -Method Delete -Uri "$api/$($m.Set)($id)/$($m.Nav[$n])/`$ref" -Headers $headers | Out-Null
        }
        if ($stateChange) { Send-Api 'PATCH' "$($m.Set)($id)" ([ordered]@{ statecode = [int]$row.statecode; statuscode = [int]$row.statuscode }) | Out-Null }
        foreach ($p in $fileChanges) {
            $path = Join-Path $InputDir $p.Value.path
            Invoke-RestMethod -Method Patch -Uri "$api/$($m.Set)($id)/$($p.Name)" -InFile $path `
                -Headers @{ Authorization = "Bearer $token"; 'Content-Type' = 'application/octet-stream'; 'x-ms-file-name' = $p.Value.name } | Out-Null
        }
        if ($current) { $count.updated++ } else { $count.created++ }
        Write-Host "$label`: $action"
    }
    $totals[$table] = $count
}

$verb = if ($WhatIfPreference) { 'would be' } else { 'were' }
Write-Host "`nRows that $verb created, updated, unchanged, skipped:"
foreach ($t in $totals.Keys) { $c = $totals[$t]; Write-Host ("  {0,-30} {1,3} {2,3} {3,3} {4,3}" -f $t, $c.created, $c.updated, $c.unchanged, $c.skipped) }

Write-Host "`nManual steps in $org (never carried by these files):"
foreach ($s in $manual) { Write-Host "  - $s" }
Write-Host "  - Environment variables tavu_GatewayUrl and tavu_GatewayKey (the key is a secret)."
Write-Host "  - The gateway's application user and its security role."
Write-Host "  - Model keys and other secrets, in the gateway's Key Vault, under the secret names the AI models carry."
Write-Host "  - Users whose Teams meetings are captured: turn on Meeting Sync (tavu_meetingsyncenabled) on each user."
