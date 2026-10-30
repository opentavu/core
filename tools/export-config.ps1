<#
.SYNOPSIS
  Export an OpenTavu environment's configuration (the rows a managed solution does not carry) to JSON files.

.DESCRIPTION
  Writes one JSON file per configuration table plus manifest.json into -OutputDir. Rows are identified by a natural
  key (a code, a name, the task key or the table's single row), never by GUID, and lookups are written as references
  by the referenced row's key ({"@ref": table, "keyField": field, "key": value}), the same convention as the setup
  seed. tools/import-config.ps1 reads these files and upserts them into another environment.

  Never exported: secrets of any kind (model keys, the gateway key, client secrets), environment variable values,
  business data (leads, conversations, accounts, cases), user-specific settings (which users have meeting sync on),
  run-time counters. tavu_aimodel and tavu_meetingsource carry only the NAME of the secret the gateway reads.

  Carried tables, in import order: tavu_aimodel, tavu_aitaskconfiguration (matched by task key),
  tavu_businesscalendar, tavu_calendarworkinghours, tavu_businessclosure, tavu_meetingsource, tavu_companyprofile
  (with its logo), tavu_systemsettings, tavu_agent, tavu_qualificationcriterion.

  Read-only. Requires Azure CLI signed in (az login) with a user that can read the environment.

.PARAMETER EnvironmentUrl
  The environment to read, e.g. https://contoso.crm.dynamics.com.

.PARAMETER OutputDir
  Folder for the files; created if missing. Existing files with the same names are replaced.

.EXAMPLE
  .\tools\export-config.ps1 -EnvironmentUrl https://contoso.crm.dynamics.com -OutputDir ..\config\contoso
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EnvironmentUrl,
    [Parameter(Mandatory = $true)][string]$OutputDir
)

$ErrorActionPreference = 'Stop'
$org = $EnvironmentUrl.TrimEnd('/')
if ($org -notmatch '^https://[^/]+\.dynamics\.com$') { throw "EnvironmentUrl must look like https://<org>.crm.dynamics.com" }
if (-not (Get-Command az -ErrorAction SilentlyContinue)) { throw "'az' is not on PATH." }

# ---------- What is carried ----------
# Key: the natural key (one or more fields; '@singleton' for a table with one row). Lookups: column -> the referenced
# table and the field that identifies its rows in any environment. Files: file columns carried as separate files.
$Tables = @(
    @{ Table = 'tavu_aimodel'; Key = @('tavu_name')
       Columns = @('tavu_name', 'tavu_provider', 'tavu_endpoint', 'tavu_deploymentmodelid', 'tavu_apiversion', 'tavu_costtier', 'tavu_isdefault', 'tavu_secretname') },
    @{ Table = 'tavu_aitaskconfiguration'; Key = @('tavu_taskkey')
       Columns = @('tavu_taskkey', 'tavu_name', 'tavu_systemprompt', 'tavu_temperature', 'tavu_confidencethreshold', 'tavu_maxoutputtokens', 'tavu_tokenbudget')
       Lookups = @{ tavu_model = @{ Table = 'tavu_aimodel'; KeyField = 'tavu_name' } } },
    @{ Table = 'tavu_businesscalendar'; Key = @('tavu_code')
       Columns = @('tavu_code', 'tavu_name', 'tavu_description', 'tavu_is24x7', 'tavu_isdefault', 'tavu_timezone') },
    @{ Table = 'tavu_calendarworkinghours'; Key = @('tavu_code')
       Columns = @('tavu_code', 'tavu_name', 'tavu_dayofweek', 'tavu_starttime', 'tavu_endtime')
       Lookups = @{ tavu_calendar = @{ Table = 'tavu_businesscalendar'; KeyField = 'tavu_code' } } },
    # A holiday is one date of one calendar; its code is free text and not unique, so it is not the key.
    @{ Table = 'tavu_businessclosure'; Key = @('tavu_calendar', 'tavu_date')
       Columns = @('tavu_code', 'tavu_name', 'tavu_date')
       Lookups = @{ tavu_calendar = @{ Table = 'tavu_businesscalendar'; KeyField = 'tavu_code' } } },
    @{ Table = 'tavu_meetingsource'; Key = @('tavu_provider')
       Columns = @('tavu_name', 'tavu_provider', 'tavu_sourcetype', 'tavu_enabled', 'tavu_priority', 'tavu_gatewaysecretname') },
    @{ Table = 'tavu_companyprofile'; Key = @('@singleton')
       Columns = @('tavu_name', 'tavu_address', 'tavu_phone', 'tavu_email', 'tavu_website', 'tavu_taxid', 'tavu_fiscalyearstartmonth',
                   'tavu_brandaccentcolor', 'tavu_defaultproposalterms', 'tavu_servicesdescription', 'tavu_targetcustomer', 'tavu_tone',
                   'tavu_neversay', 'tavu_publiclinks')
       Files = @('tavu_logo') },
    @{ Table = 'tavu_systemsettings'; Key = @('@singleton')
       Columns = @('tavu_name', 'tavu_customermode', 'tavu_aienabled', 'tavu_defaultconfidencethreshold', 'tavu_activitythresholddays',
                   'tavu_leadbufferagingdays', 'tavu_leadbufferstaledays', 'tavu_autocreatesalesperiods', 'tavu_forecastperiodtype',
                   'tavu_forecastsnapshotday', 'tavu_coveragetarget', 'tavu_proposalemaildraftenabled', 'tavu_slaautoresume',
                   'tavu_meetingautoprovision', 'tavu_meetingconsolidateddiscovery', 'tavu_meetingfollowupdraftenabled',
                   'tavu_meetingstampengagement', 'tavu_conversationretentiondays', 'tavu_notificationsendermailbox', 'tavu_caseintakemailbox')
       Lookups = @{
           tavu_defaultaimodel      = @{ Table = 'tavu_aimodel'; KeyField = 'tavu_name' }
           tavu_defaultbusinessline = @{ Table = 'tavu_businessline'; KeyField = 'tavu_code' }
           tavu_defaultcountry      = @{ Table = 'tavu_country'; KeyField = 'tavu_isocode2' }
           tavu_defaultcustomertier = @{ Table = 'tavu_customertierdefinition'; KeyField = 'tavu_code' }
       } },
    @{ Table = 'tavu_agent'; Key = @('tavu_name')
       Columns = @('tavu_name', 'tavu_agenttype', 'tavu_mode', 'tavu_whatsappphonenumberid', 'tavu_bookingurl', 'tavu_bookingsbusiness',
                   'tavu_bookingsserviceid', 'tavu_onlinemeeting', 'tavu_usecompanyprofile', 'tavu_useknowledgearticles', 'tavu_useworkinghours',
                   'tavu_createleadfordisqualified', 'tavu_turnlimit', 'tavu_tokenbudget', 'tavu_slotsearchdays', 'tavu_slotsperoffer',
                   'tavu_bookingdeclinesbeforelink', 'tavu_movedeclinesbeforekeep', 'tavu_confirmationlimitminutes',
                   'tavu_owneremailintervalminutes', 'tavu_offtopiclimit', 'tavu_reopenafterhours', 'tavu_abandonafterhours',
                   'tavu_approvaltimeoutminutes', 'tavu_greeting', 'tavu_holdingmessage', 'tavu_nextstepmessage', 'tavu_disqualifiedmessage',
                   'tavu_offtopicclosemessage', 'tavu_outofhoursmessage', 'tavu_passedtoteammessage', 'tavu_followupgreeting',
                   'tavu_bookingintro', 'tavu_bookedmessage', 'tavu_keptmessage', 'tavu_cancelledmessage')
       Lookups = @{
           tavu_handoffuser          = @{ Table = 'systemuser'; KeyField = 'internalemailaddress' }
           tavu_handoffteam          = @{ Table = 'team'; KeyField = 'name' }
           tavu_businesscalendar     = @{ Table = 'tavu_businesscalendar'; KeyField = 'tavu_code' }
           tavu_aitaskconfiguration  = @{ Table = 'tavu_aitaskconfiguration'; KeyField = 'tavu_taskkey' }
       } },
    @{ Table = 'tavu_qualificationcriterion'; Key = @('tavu_agent', 'tavu_name')
       Columns = @('tavu_name', 'tavu_order', 'tavu_purpose', 'tavu_required', 'tavu_weight', 'tavu_disqualifyingrule', 'tavu_leadfield')
       Lookups = @{ tavu_agent = @{ Table = 'tavu_agent'; KeyField = 'tavu_name' } } }
)

# A column whose name says it holds a secret is never exported (names of secrets, *secretname, are fine).
$secretPattern = '(apikey|password|clientsecret|accesstoken|connectionstring|gatewaykey)$'
foreach ($t in $Tables) {
    foreach ($c in @($t.Columns) + @($t.Lookups.Keys)) { if ($c -match $secretPattern) { throw "Refusing to export $($t.Table).${c}: it looks like a secret." } }
}

# ---------- Web API ----------
$token = & az account get-access-token --resource $org --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or -not $token) { throw "Could not get a token for $org. Run 'az login' with a user of that environment." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    Prefer             = 'odata.include-annotations="Microsoft.Dynamics.CRM.lookuplogicalname"'
}
$api = "$org/api/data/v9.2"

function Get-Api([string]$Path) { Invoke-RestMethod -Method Get -Uri "$api/$Path" -Headers $headers -TimeoutSec 120 }

$metaCache = @{}
function Get-Meta([string]$Table) {
    if (-not $metaCache.ContainsKey($Table)) {
        $metaCache[$Table] = Get-Api "EntityDefinitions(LogicalName='$Table')?`$select=EntitySetName,PrimaryIdAttribute"
    }
    $metaCache[$Table]
}

# The key value of a referenced row, cached: the row that table and id point at, read by its key field.
$refCache = @{}
function Get-RefKey([string]$Table, [string]$KeyField, [string]$Id) {
    $k = "$Table|$Id"
    if (-not $refCache.ContainsKey($k)) {
        $m = Get-Meta $Table
        $refCache[$k] = (Get-Api "$($m.EntitySetName)($Id)?`$select=$KeyField").$KeyField
    }
    $refCache[$k]
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
$manifest = [ordered]@{ exportedFrom = $org; exportedOn = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'); tables = @() }

foreach ($t in $Tables) {
    $m = Get-Meta $t.Table
    $lookups = if ($t.Lookups) { $t.Lookups } else { @{} }
    $files = if ($t.Files) { @($t.Files) } else { @() }
    $select = @($t.Columns) + @($lookups.Keys | ForEach-Object { "_$($_)_value" }) + @($files | ForEach-Object { "$($_)_name" }) +
              @('statecode', 'statuscode', $m.PrimaryIdAttribute)
    $rows = @()
    $page = Get-Api "$($m.EntitySetName)?`$select=$($select -join ',')"
    $all = @($page.value)
    while ($page.'@odata.nextLink') { $page = Invoke-RestMethod -Method Get -Uri $page.'@odata.nextLink' -Headers $headers; $all += $page.value }
    if ($t.Key[0] -eq '@singleton' -and $all.Count -gt 1) { throw "$($t.Table) should have one row and has $($all.Count)." }

    foreach ($r in $all) {
        $fields = [ordered]@{}
        foreach ($c in $t.Columns) { $fields[$c] = $r.$c }
        foreach ($c in $lookups.Keys) {
            $id = $r."_$($c)_value"
            if ($null -eq $id) { $fields[$c] = $null; continue }
            $target = $r."_$($c)_value@Microsoft.Dynamics.CRM.lookuplogicalname"
            if ($target -and $target -ne $lookups[$c].Table) { throw "$($t.Table).$c points at $target, expected $($lookups[$c].Table)." }
            $keyValue = Get-RefKey $lookups[$c].Table $lookups[$c].KeyField $id
            if ($null -eq $keyValue -or "$keyValue" -eq '') { Write-Warning "$($t.Table).$c points at a $($lookups[$c].Table) row with no $($lookups[$c].KeyField); left out." ; $fields[$c] = $null; continue }
            $fields[$c] = [ordered]@{ '@ref' = $lookups[$c].Table; keyField = $lookups[$c].KeyField; key = $keyValue }
        }
        $missingKey = @($t.Key | Where-Object { $_ -ne '@singleton' -and ($null -eq $fields[$_] -or "$($fields[$_])" -eq '') })
        if ($missingKey.Count) { Write-Warning "$($t.Table): a row with no $($missingKey -join ', ') is skipped (it cannot be matched in another environment)."; continue }

        $row = [ordered]@{ fields = $fields; statecode = $r.statecode; statuscode = $r.statuscode }
        foreach ($f in $files) {
            if (-not $r."$($f)_name") { continue }
            $id = $r.($m.PrimaryIdAttribute)
            $ext = [IO.Path]::GetExtension($r."$($f)_name")
            $out = "$($t.Table).$f$ext"
            Invoke-WebRequest -Method Get -Uri "$api/$($m.EntitySetName)($id)/$f/`$value" -Headers $headers -OutFile (Join-Path $OutputDir $out) -UseBasicParsing | Out-Null
            $hash = (Get-FileHash -Algorithm SHA256 -Path (Join-Path $OutputDir $out)).Hash
            if (-not $row.Contains('files')) { $row['files'] = [ordered]@{} }
            $row['files'][$f] = [ordered]@{ path = $out; name = $r."$($f)_name"; sha256 = $hash }
        }
        $rows += $row
    }

    $doc = [ordered]@{ table = $t.Table; key = $t.Key; rows = $rows }
    [IO.File]::WriteAllText((Join-Path $OutputDir "$($t.Table).json"), ($doc | ConvertTo-Json -Depth 10), $utf8)
    $manifest.tables += $t.Table
    Write-Host ("{0,-30} {1,3} rows" -f $t.Table, $rows.Count)
}

[IO.File]::WriteAllText((Join-Path $OutputDir 'manifest.json'), ($manifest | ConvertTo-Json -Depth 5), $utf8)
Write-Host "`nExported to $((Resolve-Path $OutputDir).Path)."
Write-Host "Not exported (set by hand in each environment): secrets and the gateway key, environment variable values"
Write-Host "(tavu_GatewayUrl, tavu_GatewayKey), users with meeting sync on (tavu_meetingsyncenabled), the gateway's application user."
