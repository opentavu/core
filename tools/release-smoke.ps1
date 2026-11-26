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
  4. Least-privilege check of the "OpenTavu Gateway" role. Runs the gateway's Dataverse read and
     write paths (agent runtime reads, contact, conversation and transcript file, lead with the
     handoff owner, the agent's hand-to-human and close updates, case with interaction, note and
     SLA status, meeting with the owner change and the Meeting Source status) as a principal that
     holds only that role, and fails the run when any call is refused. Among the privileges these
     paths need beyond table access: prvReadAsyncOperation (creates that start system jobs),
     prvAssignActivity (the meeting owner change), prvReadEntity and prvReadAttribute (the
     tavu_meeting.tavu_source option metadata read).

  The principal is the application user of the Entra app given in -GatewayAppId: the script finds
  it (or creates it in the root business unit), adds the "OpenTavu Gateway" role when it is
  missing, and fails without changing anything when the user holds any other role, directly or
  through a team. No client secret is read or needed: every gateway call of the check is sent with the
  administrator's token and the MSCRMCallerID header (impersonation). The Web API WhoAmI returns
  the signed-in caller even when impersonating, so it cannot prove impersonation. The proof is a
  row instead: the first impersonated call creates a contact (as the gateway's contact create
  does), the administrator reads its created-by user, which must be the application user or the
  check stops ("impersonation not in effect"), and then deletes that probe contact (the role holds
  no contact delete). A refused probe create is a FAIL with the privilege name and also stops the
  check. Impersonation requires the signed-in user to hold prvActOnBehalfOfAnotherUser (System
  Administrator does).

  The check writes test rows (prefix "release-smoke role check", example.com email, a fictitious
  phone number) and leaves them in the disposable environment; only its fixture agent is
  deactivated at the end. Each step is reported as PASS, FAIL or SKIP (a step that needs a row a
  failed step did not produce), with the privilege name when the platform names one. The step
  results go to _export\smoke-rolecheck-<yyyyMMdd-HHmmss>.json. Tokens and headers are never
  printed or written.

  Contract with the gateway: the steps mirror the calls of OpenTavu.Gateway/Dataverse/DataverseClient.cs
  in github.com/opentavu/gateway (method, entity set, columns, binds, choice values, If-Match).
  When DataverseClient gains a new Dataverse call, this check gets the same call.

  The run exits 1 when the diagnose fails or any step of the check fails, 0 otherwise.

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

.PARAMETER GatewayAppId
  The application (client) id of an Entra app registration in the disposable environment's tenant.
  Used only to find or create that app's application user in the environment, the principal of
  the least-privilege check. No secret is read: the check impersonates the user (MSCRMCallerID)
  with the administrator's token, which needs prvActOnBehalfOfAnotherUser. Required unless
  -SkipRoleCheck is set.

.PARAMETER SkipRoleCheck
  Skips the least-privilege check of the "OpenTavu Gateway" role. Prints a warning; the run is
  not valid for a release.

.PARAMETER RoleCheckOnly
  Runs only the least-privilege check, for an environment already installed and set up: implies
  -SkipImport and skips the setup (Mode=full) and the diagnostic. Needs -GatewayAppId.

.EXAMPLE
  .\tools\release-smoke.ps1 -EnvironmentUrl https://opentavu-smoke.crm.dynamics.com -CoreZip .\_export\OpenTavu_managed.zip -SkipRoleCheck

.EXAMPLE
  .\tools\release-smoke.ps1 -EnvironmentUrl https://opentavu-smoke.crm.dynamics.com -CoreZip .\_export\OpenTavu_managed.zip -GatewayAppId 11111111-2222-3333-4444-555555555555

  Full release run: import, setup, diagnose, then the least-privilege check as the application
  user of that app registration. Add -RoleCheckOnly to run only the check on an installed environment.
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
        'https://opentavu.crm.dynamics.com',
        'https://opentavu-dev.crm.dynamics.com',
        'https://opentavudev.crm.dynamics.com'
    ),
    [string]$ReportPath,
    [string]$GatewayAppId,
    [switch]$SkipRoleCheck,
    [switch]$RoleCheckOnly
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$org = $EnvironmentUrl.TrimEnd('/')

# ---------- Safety ----------
foreach ($p in $ProtectedUrls) {
    if ($org -ieq $p.TrimEnd('/')) { throw "Refusing to run against ${org}: it is a protected (non-disposable) environment." }
}
if ($org -notmatch '^https://[^/]+\.dynamics\.com$') { throw "EnvironmentUrl must look like https://<org>.crm.dynamics.com" }

# ---------- Least-privilege check options (checked before any pac or az call) ----------
if (-not $GatewayAppId -and -not $SkipRoleCheck) {
    throw "Pass -GatewayAppId <application (client) id> to run the least-privilege check of the OpenTavu Gateway role, or -SkipRoleCheck to skip it (not valid for a release)."
}
if ($GatewayAppId -and $SkipRoleCheck) { throw "Pass either -GatewayAppId or -SkipRoleCheck, not both." }
if ($RoleCheckOnly -and $SkipRoleCheck) { throw "-RoleCheckOnly runs only the least-privilege check; it cannot be combined with -SkipRoleCheck." }
$gatewayAppGuid = [Guid]::Empty
if ($GatewayAppId) {
    if (-not [Guid]::TryParse($GatewayAppId, [ref]$gatewayAppGuid) -or $gatewayAppGuid -eq [Guid]::Empty) {
        throw "-GatewayAppId must be the non-empty GUID of an Entra application (client) id."
    }
}
if ($RoleCheckOnly) { $SkipImport = $true }

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


# ---------- Least-privilege check: helpers ----------
# Every call of the check goes through Invoke-Dv. It builds a copy of $headers (never changes it), adds
# MSCRMCallerID only when impersonating, and returns status, response and the platform's error message.
# Tokens and header values are never printed or written.
$script:principalId = $null
$script:steps = New-Object System.Collections.ArrayList
$script:created = [ordered]@{}

function New-CallHeaders {
    param([switch]$Impersonate, [hashtable]$Extra)
    $h = @{}
    foreach ($k in $headers.Keys) { if ($k -ne 'Content-Type') { $h[$k] = $headers[$k] } }
    if ($Impersonate) {
        if (-not $script:principalId) { throw "Internal error: impersonated call without a principal." }
        $h['MSCRMCallerID'] = $script:principalId
    }
    if ($Extra) { foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] } }
    return $h
}

function Invoke-Dv {
    param(
        [string]$Method,
        [string]$Path,
        $Body,
        [switch]$Impersonate,
        [hashtable]$Extra,
        [string]$ContentType = 'application/json; charset=utf-8'
    )
    $req = @{
        Method          = $Method
        Uri             = "$org/api/data/v9.2/$Path"
        Headers         = (New-CallHeaders -Impersonate:$Impersonate -Extra $Extra)
        UseBasicParsing = $true
        TimeoutSec      = 120
    }
    if ($null -ne $Body) {
        if ($Body -is [byte[]]) { $bytes = $Body }
        elseif ($Body -is [string]) { $bytes = [Text.Encoding]::UTF8.GetBytes($Body) }
        else { $bytes = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 6 -Compress)) }
        $req.Body = $bytes
        $req.ContentType = $ContentType
    }
    try {
        $r = Invoke-WebRequest @req
        return [pscustomobject]@{ Ok = $true; Status = [int]$r.StatusCode; Response = $r; Message = $null; Privilege = $null }
    } catch {
        $err = $_
        $status = 0
        $text = $null
        $resp = $err.Exception.Response
        if ($null -ne $resp) { try { $status = [int]$resp.StatusCode } catch { $status = 0 } }
        if ($err.ErrorDetails -and $err.ErrorDetails.Message) {
            $text = $err.ErrorDetails.Message
        } elseif ($null -ne $resp -and ($resp | Get-Member -Name GetResponseStream -MemberType Method)) {
            # Windows PowerShell 5.1: the body is on the WebException's response stream.
            try { $text = (New-Object IO.StreamReader($resp.GetResponseStream())).ReadToEnd() } catch { $text = $null }
        }
        $message = $null
        if ($text) {
            try { $j = $text | ConvertFrom-Json; if ($j.error -and $j.error.message) { $message = [string]$j.error.message } } catch { $message = $null }
            if (-not $message) { $message = $text }
        }
        if (-not $message) { $message = $err.Exception.Message }
        $message = ($message -replace '\s+', ' ').Trim()
        if ($message.Length -gt 600) { $message = $message.Substring(0, 600) + '...' }
        $privs = @([regex]::Matches($message, 'prv[A-Za-z0-9]+') | ForEach-Object { $_.Value } | Select-Object -Unique)
        $privilege = $null
        if ($privs.Count) { $privilege = $privs -join ', ' }
        return [pscustomobject]@{ Ok = $false; Status = $status; Response = $null; Message = $message; Privilege = $privilege }
    }
}

function Get-StepTarget {
    param([string]$Path)
    return (($Path -split '\?')[0] -replace '\([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\)', '(id)')
}

function Add-StepResult {
    param([string]$Name, [string]$Result, [string]$Method, [string]$Target, [int]$Status, [string]$Message, [string]$Privilege)
    [void]$script:steps.Add([ordered]@{
        step = $Name; result = $Result; method = $Method; target = $Target; status = $Status
        message = $Message; privilege = $Privilege
    })
    $color = 'Green'
    if ($Result -eq 'FAIL') { $color = 'Red' } elseif ($Result -eq 'SKIP') { $color = 'Yellow' }
    $statusText = '-'
    if ($Status) { $statusText = "$Status" }
    $line = '{0,-4} {1,-6} {2} {3}  ({4})' -f $Result, $Method, $Target, $statusText, $Name
    Write-Host $line -ForegroundColor $color
    if ($Result -eq 'FAIL') {
        if ($Privilege) { Write-Host "       missing privilege: $Privilege" -ForegroundColor Red }
        if ($Message) { Write-Host "       $Message" -ForegroundColor Red }
    } elseif ($Result -eq 'SKIP' -and $Message) {
        Write-Host "       $Message" -ForegroundColor Yellow
    }
}

# Runs one step. -Needs is an ordered map "step name" -> value; an empty value records SKIP
# "skipped (needs <step>)" and sends nothing. -Validate gets the response and returns an error text or
# nothing. Returns the response on PASS and $null otherwise. -AsAdmin sends the call without
# MSCRMCallerID (principal setup and fixture only).
function Invoke-Step {
    param(
        [string]$Name,
        [string]$Method,
        [string]$Path,
        $Body,
        [hashtable]$Extra,
        [string]$ContentType = 'application/json; charset=utf-8',
        [System.Collections.IDictionary]$Needs,
        [scriptblock]$Validate,
        [switch]$AsAdmin
    )
    $target = Get-StepTarget $Path
    if ($Needs) {
        foreach ($k in $Needs.Keys) {
            if (-not $Needs[$k]) {
                Add-StepResult -Name $Name -Result 'SKIP' -Method $Method -Target $target -Status 0 -Message "skipped (needs $k)"
                return $null
            }
        }
    }
    $r = Invoke-Dv -Method $Method -Path $Path -Body $Body -Impersonate:(-not $AsAdmin) -Extra $Extra -ContentType $ContentType
    if (-not $r.Ok) {
        Add-StepResult -Name $Name -Result 'FAIL' -Method $Method -Target $target -Status $r.Status -Message $r.Message -Privilege $r.Privilege
        return $null
    }
    if ($Validate) {
        $problem = & $Validate $r
        if ($problem) {
            Add-StepResult -Name $Name -Result 'FAIL' -Method $Method -Target $target -Status $r.Status -Message ([string]$problem)
            return $null
        }
    }
    Add-StepResult -Name $Name -Result 'PASS' -Method $Method -Target $target -Status $r.Status
    return $r
}

function Get-Json {
    param($R)
    if ($null -eq $R -or -not $R.Response.Content) { return $null }
    $c = $R.Response.Content
    if ($c -is [byte[]]) { $c = [Text.Encoding]::UTF8.GetString($c) }
    return ($c | ConvertFrom-Json)
}

function Get-Rows {
    param($R)
    $j = Get-Json $R
    if ($null -eq $j -or $null -eq $j.value) { return @() }
    return @($j.value)
}

function Get-CreatedId {
    param($R)
    if ($null -eq $R) { return $null }
    $v = $null
    foreach ($k in $R.Response.Headers.Keys) { if ($k -ieq 'OData-EntityId') { $v = $R.Response.Headers[$k] } }
    if ($v -is [array]) { $v = $v[0] }
    if ([string]$v -match '\(([0-9a-fA-F-]{36})\)\s*$') { return $Matches[1] }
    return $null
}

function ConvertTo-ODataLiteral {
    # The gateway's Literal(): quote-escaped and URL-encoded (phone numbers carry "+").
    param([string]$Value)
    return "'" + [Uri]::EscapeDataString($Value.Replace("'", "''")) + "'"
}

function Get-IsoUtc {
    param([datetime]$When)
    return $When.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
}

# ---------- Least-privilege check ----------
# Mirrors the Dataverse calls of OpenTavu.Gateway/Dataverse/DataverseClient.cs (gateway main). When the
# gateway's DataverseClient gains a new Dataverse call, add the same call here.
function Invoke-RoleCheck {
    $roleName = 'OpenTavu Gateway'
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $prefix = "release-smoke role check $stamp"
    $state = [ordered]@{ principalId = $null; applicationId = $gatewayAppGuid.ToString(); roleId = $null; roleName = $roleName }
    $fixtureAgentId = $null

    $dir = Join-Path $repo '_export'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $roleReportPath = Join-Path $dir ("smoke-rolecheck-{0}.json" -f $stamp)

    try {
        # ----- Principal setup, as the administrator (no impersonation) -----
        Write-Host "Principal setup (as the signed-in administrator):" -ForegroundColor DarkGray
        $r = Invoke-Step -AsAdmin -Name 'setup: WhoAmI (administrator)' -Method 'GET' -Path 'WhoAmI'
        if (-not $r) { return }
        $adminId = [string](Get-Json $r).UserId

        $adminAad = $null
        $r = Invoke-Step -AsAdmin -Name 'setup: administrator Entra object id' -Method 'GET' -Path ('systemusers({0})?$select=azureactivedirectoryobjectid' -f $adminId)
        if ($r) { $adminAad = [string](Get-Json $r).azureactivedirectoryobjectid }

        $r = Invoke-Step -AsAdmin -Name 'setup: root business unit' -Method 'GET' -Path 'businessunits?$select=businessunitid,name&$filter=_parentbusinessunitid_value eq null' -Validate {
            param($x) if (@(Get-Rows $x).Count -ne 1) { 'expected exactly one root business unit' }
        }
        if (-not $r) { return }
        $rootBu = [string](Get-Rows $r)[0].businessunitid

        $r = Invoke-Step -AsAdmin -Name 'setup: OpenTavu Gateway role' -Method 'GET' -Path ('roles?$select=roleid,name&$filter=name eq ''{0}'' and _businessunitid_value eq {1}' -f $roleName, $rootBu) -Validate {
            param($x) if (@(Get-Rows $x).Count -eq 0) { 'the installed Core solution has no OpenTavu Gateway role' }
        }
        if (-not $r) { return }
        $roleId = [string](Get-Rows $r)[0].roleid
        $state.roleId = $roleId

        $r = Invoke-Step -AsAdmin -Name 'setup: find the application user' -Method 'GET' -Path ('systemusers?$select=systemuserid,isdisabled,fullname&$filter=applicationid eq {0}' -f $gatewayAppGuid)
        if (-not $r) { return }
        $users = @(Get-Rows $r)
        $principal = $null
        if ($users.Count -gt 0) {
            $principal = [string]$users[0].systemuserid
        } else {
            $r = Invoke-Step -AsAdmin -Name 'setup: create the application user' -Method 'POST' -Path 'systemusers' -Body ([ordered]@{
                applicationid                = $gatewayAppGuid.ToString()
                'businessunitid@odata.bind'  = "/businessunits($rootBu)"
            })
            if (-not $r) { return }
            $principal = Get-CreatedId $r
            $script:created['applicationUser'] = $principal
        }
        $state.principalId = $principal
        $r = Invoke-Step -AsAdmin -Name 'setup: application user enabled' -Method 'GET' -Path ('systemusers({0})?$select=isdisabled' -f $principal) -Needs ([ordered]@{ 'setup: create the application user' = $principal }) -Validate {
            param($x) if ((Get-Json $x).isdisabled -eq $true) { 'the application user is disabled; enable it and run again' }
        }
        if (-not $r) { return }

        # Least privilege is verified, never forced: the user's direct roles plus the roles of every team it
        # belongs to must be exactly the one OpenTavu Gateway role. The only role write is the association
        # below, when the user holds no role at all.
        function Get-PrincipalRoles {
            param([string]$Label)
            $all = @()
            $rr = Invoke-Step -AsAdmin -Name "setup: direct roles ($Label)" -Method 'GET' -Path ('systemusers({0})/systemuserroles_association?$select=roleid,name' -f $principal)
            if (-not $rr) { return $null }
            $all += @(Get-Rows $rr | ForEach-Object { [pscustomobject]@{ id = [string]$_.roleid; name = [string]$_.name; via = 'direct' } })
            $rt = Invoke-Step -AsAdmin -Name "setup: teams ($Label)" -Method 'GET' -Path ('systemusers({0})/teammembership_association?$select=teamid,name' -f $principal)
            if (-not $rt) { return $null }
            foreach ($t in @(Get-Rows $rt)) {
                $rtr = Invoke-Step -AsAdmin -Name "setup: roles of team $($t.name) ($Label)" -Method 'GET' -Path ('teams({0})/teamroles_association?$select=roleid,name' -f $t.teamid)
                if (-not $rtr) { return $null }
                $all += @(Get-Rows $rtr | ForEach-Object { [pscustomobject]@{ id = [string]$_.roleid; name = [string]$_.name; via = "team $($t.name)" } })
            }
            return ,$all
        }

        $roles = Get-PrincipalRoles -Label 'before'
        if ($null -eq $roles) { return }
        $others = @($roles | Where-Object { $_.id -ne $roleId })
        if ($others.Count) {
            $names = ($roles | ForEach-Object { "$($_.name) ($($_.via))" }) -join ', '
            Add-StepResult -Name 'setup: least privilege' -Result 'FAIL' -Method 'GET' -Target 'roles' -Status 0 -Message "The application user must hold only the $roleName role; it holds: $names. Nothing was changed."
            return
        }
        if (@($roles).Count -eq 0) {
            $r = Invoke-Step -AsAdmin -Name 'setup: assign the OpenTavu Gateway role' -Method 'POST' -Path ('systemusers({0})/systemuserroles_association/$ref' -f $principal) -Body @{
                '@odata.id' = "$org/api/data/v9.2/roles($roleId)"
            }
            if (-not $r) { return }
            $roles = Get-PrincipalRoles -Label 'after'
            if ($null -eq $roles) { return }
        }
        $distinct = @($roles | ForEach-Object { $_.id } | Select-Object -Unique)
        if ($distinct.Count -ne 1 -or $distinct[0] -ne $roleId) {
            $names = ($roles | ForEach-Object { "$($_.name) ($($_.via))" }) -join ', '
            if (-not $names) { $names = '(none)' }
            Add-StepResult -Name 'setup: least privilege' -Result 'FAIL' -Method 'GET' -Target 'roles' -Status 0 -Message "The application user must hold exactly the $roleName role; it holds: $names."
            return
        }
        Add-StepResult -Name 'setup: least privilege (only the OpenTavu Gateway role)' -Result 'PASS' -Method 'GET' -Target 'roles' -Status 200

        # Fixture agent, created by the administrator: creating agents is not a gateway path (the role has
        # only Read and Append To on tavu_agent). Deactivated in the finally block below.
        $r = Invoke-Step -AsAdmin -Name 'setup: fixture agent' -Method 'POST' -Path 'tavu_agents' -Body ([ordered]@{
            tavu_name      = $prefix
            tavu_agenttype = 576600000   # Lead Qualification
            tavu_mode      = 576600000   # Act
        })
        $fixtureAgentId = Get-CreatedId $r
        if ($fixtureAgentId) { $script:created['fixtureAgent'] = $fixtureAgentId }

        # ----- Impersonation -----
        $script:principalId = $principal
        Write-Host "`nImpersonated as the application user (MSCRMCallerID):" -ForegroundColor DarkGray
        # Proof of impersonation. The Web API WhoAmI returns the signed-in caller even with MSCRMCallerID,
        # so a row is the proof: create a contact impersonated (the gateway's contact create), read its
        # created-by user as the administrator, then delete it as the administrator (the role holds no
        # contact delete). A refused create or a different creator stops the check.
        # contact.lastname holds at most 50 characters (platform default): this value is 46.
        $probeStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $r = Invoke-Step -Name 'impersonation proof: contact create' -Method 'POST' -Path 'contacts' -Body ([ordered]@{
            lastname      = "release-smoke role check $probeStamp proof"
            emailaddress1 = "release-smoke-$probeStamp-proof@example.com"
        }) -Validate {
            param($x) if (-not (Get-CreatedId $x)) { 'the create returned no row id' }
        }
        if (-not $r) { return }
        $probeId = Get-CreatedId $r
        $proof = Invoke-Step -AsAdmin -Name 'impersonation proof: created by the application user' -Method 'GET' -Path ('contacts({0})?$select=_createdby_value' -f $probeId) -Validate {
            param($x) if ([string](Get-Json $x)._createdby_value -ine $principal) { 'impersonation not in effect: the row was not created by the application user' }
        }
        $del = Invoke-Step -AsAdmin -Name 'impersonation proof: delete the probe contact' -Method 'DELETE' -Path ('contacts({0})' -f $probeId)
        if (-not $del) { $script:created['impersonationProbeContact'] = $probeId }
        if (-not $proof) { return }

        # ----- Test values -----
        $nowUtc = (Get-Date).ToUniversalTime()
        $email = "release-smoke-$stamp@example.com"
        $phoneDigits = '155555501' + (Get-Random -Minimum 10 -Maximum 99)   # +1 555 555 01xx, fictitious
        $phone = "+$phoneDigits"
        $displayName = 'Release Smoke'
        $agentSelect = 'tavu_agentid,tavu_name,tavu_agenttype,tavu_mode,tavu_bookingurl,' +
            'tavu_greeting,tavu_outofhoursmessage,tavu_nextstepmessage,tavu_holdingmessage,' +
            'tavu_turnlimit,tavu_abandonafterhours,tavu_approvaltimeoutminutes,tavu_tokenbudget,' +
            '_tavu_aitaskconfiguration_value,_tavu_businesscalendar_value,' +
            'tavu_usecompanyprofile,tavu_useworkinghours,tavu_useknowledgearticles,' +
            '_tavu_handoffuser_value,_tavu_handoffteam_value,tavu_bookingsbusiness,tavu_bookingsserviceid,' +
            'tavu_createleadfordisqualified,tavu_reopenafterhours,' +
            'tavu_passedtoteammessage,tavu_followupgreeting,tavu_bookingintro,tavu_bookedmessage,tavu_keptmessage,' +
            'tavu_cancelledmessage,tavu_disqualifiedmessage,tavu_offtopicclosemessage,' +
            'tavu_slotsearchdays,tavu_slotsperoffer,tavu_bookingdeclinesbeforelink,tavu_movedeclinesbeforekeep,' +
            'tavu_confirmationlimitminutes,tavu_owneremailintervalminutes,tavu_offtopiclimit,tavu_whatsappphonenumberid,tavu_onlinemeeting'
        $conversationSelect = 'tavu_conversationid,tavu_channel,tavu_channelcontactid,_tavu_agent_value,' +
            'tavu_firstinboundon,tavu_lastinboundon,tavu_firstresponseon,tavu_messagecount,' +
            'tavu_phone,statuscode,tavu_answers,tavu_tokensused,tavu_pendingreply,tavu_language,tavu_outcomereason,' +
            'tavu_displayname,tavu_booking,tavu_appointmentid,_tavu_lead_value'
        $leadSelect = 'tavu_leadid,statecode,tavu_fullname,tavu_firstname,tavu_lastname,tavu_mobilephone,tavu_phone,' +
            'tavu_email,tavu_companyname,tavu_subject,tavu_source,tavu_leadlanguage,tavu_sourcedetails'
        $ifMatch = @{ 'If-Match' = '*' }

        # ----- Reads the paths start with -----
        [void](Invoke-Step -Name 'agent: active by type' -Method 'GET' -Path ('tavu_agents?$select={0}&$filter=statecode eq 0 and tavu_agenttype eq 576600000&$orderby=createdon asc&$top=1' -f $agentSelect))
        [void](Invoke-Step -Name 'agent: active by WhatsApp number id' -Method 'GET' -Path ('tavu_agents?$select={0}&$filter=statecode eq 0 and tavu_whatsappphonenumberid eq {1}&$orderby=createdon asc&$top=2' -f $agentSelect, (ConvertTo-ODataLiteral "rs$phoneDigits")))
        [void](Invoke-Step -Name 'agent: every active agent' -Method 'GET' -Path ('tavu_agents?$select={0}&$filter=statecode eq 0&$orderby=createdon asc' -f $agentSelect))
        [void](Invoke-Step -Name 'agent: by id' -Method 'GET' -Path ('tavu_agents({0})?$select={1}' -f $fixtureAgentId, $agentSelect) -Needs ([ordered]@{ 'setup: fixture agent' = $fixtureAgentId }))
        [void](Invoke-Step -Name 'system settings: mailboxes' -Method 'GET' -Path 'tavu_systemsettingses?$select=tavu_notificationsendermailbox,tavu_caseintakemailbox&$filter=statecode eq 0&$orderby=createdon asc&$top=1')
        [void](Invoke-Step -Name 'company profile' -Method 'GET' -Path 'tavu_companyprofiles?$select=tavu_name,tavu_servicesdescription,tavu_targetcustomer,tavu_tone,tavu_neversay,tavu_website,tavu_publiclinks&$filter=statecode eq 0&$orderby=createdon asc&$top=1')

        # The gateway reads one AI task (and its model) by id; pick an active one, or a random id (an empty
        # result still proves the read privilege).
        $taskId = [Guid]::NewGuid().ToString(); $modelId = [Guid]::NewGuid().ToString()
        $r = Invoke-Step -Name 'AI task: pick an active one' -Method 'GET' -Path 'tavu_aitaskconfigurations?$select=tavu_aitaskconfigurationid,_tavu_model_value&$filter=statecode eq 0&$top=1'
        $rows = @(Get-Rows $r)
        if ($rows.Count) { $taskId = [string]$rows[0].tavu_aitaskconfigurationid; if ($rows[0]._tavu_model_value) { $modelId = [string]$rows[0]._tavu_model_value } }
        [void](Invoke-Step -Name 'AI task configuration by id' -Method 'GET' -Path ('tavu_aitaskconfigurations?$select=tavu_aitaskconfigurationid,tavu_name,tavu_systemprompt,_tavu_model_value,tavu_temperature,tavu_maxoutputtokens,tavu_confidencethreshold,tavu_tokenbudget&$filter=tavu_aitaskconfigurationid eq {0} and statecode eq 0' -f $taskId))
        [void](Invoke-Step -Name 'AI model by id' -Method 'GET' -Path ('tavu_aimodels?$select=tavu_deploymentmodelid&$filter=tavu_aimodelid eq {0} and statecode eq 0' -f $modelId))
        [void](Invoke-Step -Name 'qualification criteria of the agent' -Method 'GET' -Path ('tavu_qualificationcriterions?$select=tavu_qualificationcriterionid,tavu_order,tavu_name,tavu_purpose,tavu_required,tavu_weight,tavu_disqualifyingrule,tavu_leadfield&$filter=_tavu_agent_value eq {0} and statecode eq 0&$orderby=tavu_order asc' -f $fixtureAgentId) -Needs ([ordered]@{ 'setup: fixture agent' = $fixtureAgentId }))

        $calendarId = [Guid]::NewGuid().ToString()
        $r = Invoke-Step -Name 'business calendar: pick an active one' -Method 'GET' -Path 'tavu_businesscalendars?$select=tavu_businesscalendarid&$filter=statecode eq 0&$top=1'
        $rows = @(Get-Rows $r)
        if ($rows.Count) { $calendarId = [string]$rows[0].tavu_businesscalendarid }
        [void](Invoke-Step -Name 'business calendar by id' -Method 'GET' -Path ('tavu_businesscalendars?$select=tavu_businesscalendarid,tavu_name,tavu_is24x7,tavu_timezone&$filter=tavu_businesscalendarid eq {0} and statecode eq 0' -f $calendarId))
        [void](Invoke-Step -Name 'calendar working hours' -Method 'GET' -Path ('tavu_calendarworkinghourses?$select=tavu_dayofweek,tavu_starttime,tavu_endtime&$filter=_tavu_calendar_value eq {0} and statecode eq 0' -f $calendarId))
        [void](Invoke-Step -Name 'business closures' -Method 'GET' -Path ('tavu_businessclosures?$select=tavu_date&$filter=statecode eq 0 and (_tavu_calendar_value eq {0} or _tavu_calendar_value eq null)' -f $calendarId))
        [void](Invoke-Step -Name 'time zone definition' -Method 'GET' -Path 'timezonedefinitions?$select=userinterfacename,standardname&$filter=timezonecode eq 85')
        [void](Invoke-Step -Name 'LocalTimeFromUtcTime' -Method 'GET' -Path ('LocalTimeFromUtcTime(TimeZoneCode=@tz,UtcTime=@at)?@tz=85&@at={0}' -f [Uri]::EscapeDataString((Get-IsoUtc $nowUtc))))
        [void](Invoke-Step -Name 'published knowledge articles' -Method 'GET' -Path 'tavu_knowledgearticles?$select=tavu_name,tavu_body&$filter=statecode eq 0 and statuscode eq 576600000 and tavu_audience eq 576600000&$orderby=modifiedon desc&$top=5')
        [void](Invoke-Step -Name 'publisher app id' -Method 'GET' -Path 'appmodules?$select=appmoduleid&$filter=startswith(uniquename,''tavu_'')&$top=2')
        [void](Invoke-Step -Name 'enabled user email' -Method 'GET' -Path ('systemusers?$select=internalemailaddress&$filter=systemuserid eq {0} and isdisabled eq false' -f $adminId))
        [void](Invoke-Step -Name 'meeting sync users' -Method 'GET' -Path 'systemusers?$select=azureactivedirectoryobjectid&$filter=tavu_meetingsyncenabled eq true and azureactivedirectoryobjectid ne null and isdisabled eq false')
        $teamId = $null
        $r = Invoke-Step -Name 'team: pick one (an agent''s handoff team)' -Method 'GET' -Path ('teams?$select=teamid,name&$filter=_businessunitid_value eq {0}&$top=1' -f $rootBu)
        $rows = @(Get-Rows $r)
        if ($rows.Count) { $teamId = [string]$rows[0].teamid }
        [void](Invoke-Step -Name 'team member emails' -Method 'GET' -Path ('systemusers?$select=internalemailaddress&$filter=isdisabled eq false and internalemailaddress ne null and teammembership_association/any(t:t/teamid eq {0})&$top=5' -f $teamId) -Needs ([ordered]@{ 'team: pick one (an agent''s handoff team)' = $teamId }))
        [void](Invoke-Step -Name 'agent timer: open conversations' -Method 'GET' -Path ('tavu_conversations?$select={0}&$filter=statecode eq 0 and (statuscode eq 1 or statuscode eq 576600000)&$orderby=tavu_lastinboundon asc&$top=5' -f $conversationSelect))

        # ----- Contact (agent runtime match and email intake) -----
        [void](Invoke-Step -Name 'contact: by WhatsApp id' -Method 'GET' -Path ('contacts?$select=contactid&$filter=tavu_whatsappid eq {0} and statecode eq 0&$top=2' -f (ConvertTo-ODataLiteral $phoneDigits)))
        [void](Invoke-Step -Name 'contact: by mobile phone' -Method 'GET' -Path ('contacts?$select=contactid&$filter=(mobilephone eq {0} or mobilephone eq {1}) and statecode eq 0&$top=2' -f (ConvertTo-ODataLiteral $phoneDigits), (ConvertTo-ODataLiteral $phone)))
        [void](Invoke-Step -Name 'contact: by email' -Method 'GET' -Path ('contacts?$select=contactid&$filter=emailaddress1 eq ''{0}''&$top=1' -f $email))
        $r = Invoke-Step -Name 'contact: create' -Method 'POST' -Path 'contacts' -Body ([ordered]@{
            lastname      = $prefix
            emailaddress1 = $email
        })
        $contactId = Get-CreatedId $r
        if ($contactId) { $script:created['contact'] = $contactId }

        # ----- Conversation (agent runtime) -----
        [void](Invoke-Step -Name 'conversation: latest by channel identity' -Method 'GET' -Path ('tavu_conversations?$select={0}&$filter=tavu_channel eq 576600000 and tavu_channelcontactid eq {1}&$orderby=createdon desc&$top=1' -f $conversationSelect, (ConvertTo-ODataLiteral $phoneDigits)))
        $convBody = [ordered]@{
            tavu_name             = "WhatsApp, $displayName, $($nowUtc.ToString('yyyy-MM-dd')), $prefix"
            tavu_channel          = 576600000   # WhatsApp
            tavu_channelcontactid = $phoneDigits
            tavu_displayname      = $displayName
            tavu_firstinboundon   = (Get-IsoUtc $nowUtc)
            tavu_lastinboundon    = (Get-IsoUtc $nowUtc)
            tavu_messagecount     = 0
            tavu_phone            = $phone
        }
        if ($fixtureAgentId) { $convBody['tavu_Agent@odata.bind'] = "/tavu_agents($fixtureAgentId)" }
        if ($contactId) { $convBody['tavu_Contact@odata.bind'] = "/contacts($contactId)" }
        $r = Invoke-Step -Name 'conversation: create' -Method 'POST' -Path 'tavu_conversations' -Body $convBody
        $conversationId = Get-CreatedId $r
        if ($conversationId) { $script:created['conversation'] = $conversationId }
        $needConv = [ordered]@{ 'conversation: create' = $conversationId }

        [void](Invoke-Step -Name 'conversation: by id' -Method 'GET' -Path ('tavu_conversations({0})?$select={1}' -f $conversationId, $conversationSelect) -Needs $needConv)
        $convPath = "tavu_conversations($conversationId)"
        [void](Invoke-Step -Name 'conversation: inbound counters' -Method 'PATCH' -Path $convPath -Extra $ifMatch -Needs $needConv -Body ([ordered]@{
            tavu_lastinboundon      = (Get-IsoUtc $nowUtc)
            tavu_messagecount       = 1
            tavu_lastmessagepreview = "$prefix inbound"
            tavu_phone              = $phone
        }))
        [void](Invoke-Step -Name 'conversation: reply sent' -Method 'PATCH' -Path $convPath -Extra $ifMatch -Needs $needConv -Body ([ordered]@{
            tavu_messagecount       = 2
            tavu_lastmessagepreview = "$prefix reply"
            tavu_firstresponseon    = (Get-IsoUtc $nowUtc.AddSeconds(5))
            tavu_responseseconds    = 5
        }))
        [void](Invoke-Step -Name 'conversation: tokens used' -Method 'PATCH' -Path $convPath -Extra $ifMatch -Needs $needConv -Body @{ tavu_tokensused = 100 })
        [void](Invoke-Step -Name 'conversation: language' -Method 'PATCH' -Path $convPath -Extra $ifMatch -Needs $needConv -Body @{ tavu_language = 576600001 })
        [void](Invoke-Step -Name 'conversation: answers' -Method 'PATCH' -Path $convPath -Extra $ifMatch -Needs $needConv -Body @{ tavu_answers = '{"note":"release-smoke role check"}' })
        [void](Invoke-Step -Name 'conversation: booking' -Method 'PATCH' -Path $convPath -Extra $ifMatch -Needs $needConv -Body ([ordered]@{ tavu_booking = '{"note":"release-smoke role check"}'; tavu_appointmentid = $null }))
        [void](Invoke-Step -Name 'conversation: pending reply set' -Method 'PATCH' -Path $convPath -Extra $ifMatch -Needs $needConv -Body @{ tavu_pendingreply = "$prefix pending reply" })
        [void](Invoke-Step -Name 'conversation: pending reply cleared' -Method 'PATCH' -Path $convPath -Extra $ifMatch -Needs $needConv -Body @{ tavu_pendingreply = $null })
        [void](Invoke-Step -Name 'conversation: summary' -Method 'PATCH' -Path $convPath -Extra $ifMatch -Needs $needConv -Body @{ tavu_summary = "$prefix summary" })
        [void](Invoke-Step -Name 'conversation: read summary' -Method 'GET' -Path ('{0}?$select=tavu_summary' -f $convPath) -Needs $needConv)

        $transcriptBytes = [Text.Encoding]::UTF8.GetBytes('[{"note":"release-smoke role check transcript"}]')
        $r = Invoke-Step -Name 'conversation: transcript upload' -Method 'PATCH' -Path "$convPath/tavu_transcript" -Needs $needConv `
            -Extra @{ 'x-ms-file-name' = "transcript-$conversationId.json" } -ContentType 'application/octet-stream' -Body $transcriptBytes
        $uploaded = $null
        if ($r) { $uploaded = 'yes' }
        [void](Invoke-Step -Name 'conversation: transcript download' -Method 'GET' -Path "$convPath/tavu_transcript/`$value" -Extra @{ Accept = 'application/octet-stream' } `
            -Needs ([ordered]@{ 'conversation: create' = $conversationId; 'conversation: transcript upload' = $uploaded }) -Validate {
                param($x)
                $got = $x.Response.RawContentStream.ToArray()
                if ([Convert]::ToBase64String($got) -ne [Convert]::ToBase64String($transcriptBytes)) { 'the downloaded transcript differs from the uploaded bytes' }
            })

        # ----- Lead (CreateOrUpdateLead) -----
        [void](Invoke-Step -Name 'lead: open by email' -Method 'GET' -Path ('tavu_leads?$select={0}&$filter=statecode eq 0 and tavu_email eq {1}&$orderby=modifiedon desc&$top=5' -f $leadSelect, (ConvertTo-ODataLiteral $email)))
        $d = ConvertTo-ODataLiteral $phoneDigits
        [void](Invoke-Step -Name 'lead: open by phone digits' -Method 'GET' -Path ('tavu_leads?$select={0}&$filter=statecode eq 0 and (contains(tavu_mobilephone,{1}) or contains(tavu_phone,{1}))&$orderby=modifiedon desc&$top=20' -f $leadSelect, $d))
        $leadId = [Guid]::NewGuid().ToString()
        $convRef = $conversationId
        if (-not $convRef) { $convRef = '(none)' }
        $r = Invoke-Step -Name 'lead: create with the handoff owner' -Method 'POST' -Path 'tavu_leads' -Body ([ordered]@{
            tavu_leadid          = $leadId
            tavu_fullname        = $displayName
            tavu_firstname       = 'Release'
            tavu_lastname        = 'Smoke'
            tavu_mobilephone     = $phone
            tavu_email           = $email
            tavu_companyname     = "$prefix company"
            tavu_subject         = "$prefix subject"
            tavu_source          = 576600008   # WhatsApp
            tavu_leadlanguage    = 576600001   # English
            tavu_sourcedetails   = "Lead Qualification Agent, conversation ${convRef}:`n$prefix summary"
            'ownerid@odata.bind' = "/systemusers($adminId)"
        })
        if ($r) { $script:created['lead'] = $leadId } else { $leadId = $null }
        $needLead = [ordered]@{ 'lead: create with the handoff owner' = $leadId }
        [void](Invoke-Step -Name 'lead: by id' -Method 'GET' -Path ('tavu_leads?$select={0}&$filter=tavu_leadid eq {1}' -f $leadSelect, $leadId) -Needs $needLead)
        [void](Invoke-Step -Name 'lead: update' -Method 'PATCH' -Path "tavu_leads($leadId)" -Extra $ifMatch -Needs $needLead -Body ([ordered]@{
            tavu_email         = $email
            tavu_sourcedetails = "Lead Qualification Agent, conversation ${convRef}:`n$prefix summary, updated"
        }))
        [void](Invoke-Step -Name 'conversation: link the lead' -Method 'PATCH' -Path $convPath -Extra $ifMatch `
            -Needs ([ordered]@{ 'conversation: create' = $conversationId; 'lead: create with the handoff owner' = $leadId }) -Body @{ 'tavu_Lead@odata.bind' = "/tavu_leads($leadId)" })

        # ----- The agent's closing updates -----
        [void](Invoke-Step -Name 'conversation: hand to human' -Method 'PATCH' -Path $convPath -Extra $ifMatch -Needs $needConv -Body ([ordered]@{
            statuscode         = 576600001   # With Human
            tavu_outcomereason = "$prefix handed to a human"
        }))
        [void](Invoke-Step -Name 'conversation: close as Qualified' -Method 'PATCH' -Path $convPath -Extra $ifMatch -Needs $needConv -Body ([ordered]@{
            statecode          = 1
            statuscode         = 576600002   # Qualified
            tavu_outcomereason = 'meeting booked'
            tavu_pendingreply  = $null
        }))

        # ----- Case (email intake and SLA) -----
        [void](Invoke-Step -Name 'case: by case number' -Method 'GET' -Path ('tavu_cases?$select=tavu_caseid&$filter=tavu_casenumber eq ''RS-{0}''&$top=1' -f $stamp))
        $r = Invoke-Step -Name 'case: create' -Method 'POST' -Path 'tavu_cases' -Needs ([ordered]@{ 'contact: create' = $contactId }) -Body ([ordered]@{
            tavu_title                         = "$prefix case"
            tavu_description                   = "$prefix case description"
            'tavu_Customer_contact@odata.bind' = "/contacts($contactId)"
        })
        $caseId = Get-CreatedId $r
        if ($caseId) { $script:created['case'] = $caseId }
        $needCase = [ordered]@{ 'case: create' = $caseId }
        $intBody = [ordered]@{
            tavu_name              = "$prefix inbound email"
            tavu_body              = "$prefix inbound email body"
            tavu_direction         = 576600000   # Inbound
            tavu_channel           = 576600000   # Email
            'tavu_Case@odata.bind' = "/tavu_cases($caseId)"
        }
        if ($contactId) { $intBody['tavu_FromContact@odata.bind'] = "/contacts($contactId)" }
        $r = Invoke-Step -Name 'case interaction: create' -Method 'POST' -Path 'tavu_caseinteractions' -Needs $needCase -Body $intBody
        $interactionId = Get-CreatedId $r
        if ($interactionId) { $script:created['caseInteraction'] = $interactionId }
        $r = Invoke-Step -Name 'note: attachment on the interaction' -Method 'POST' -Path 'annotations' -Needs ([ordered]@{ 'case interaction: create' = $interactionId }) -Body ([ordered]@{
            subject                                   = 'release-smoke-role-check.txt'
            filename                                  = 'release-smoke-role-check.txt'
            mimetype                                  = 'text/plain'
            documentbody                              = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$prefix attachment"))
            isdocument                                = $true
            'objectid_tavu_caseinteraction@odata.bind' = "/tavu_caseinteractions($interactionId)"
        })
        $noteId = Get-CreatedId $r
        if ($noteId) { $script:created['note'] = $noteId }
        [void](Invoke-Step -Name 'case: state' -Method 'GET' -Path ('tavu_cases({0})?$select=statecode' -f $caseId) -Needs $needCase)
        [void](Invoke-Step -Name 'case: SLA status Warning' -Method 'PATCH' -Path "tavu_cases($caseId)" -Extra $ifMatch -Needs $needCase -Body @{ tavu_slastatus = 576600001 })

        # ----- Meeting (Teams intake) -----
        $teamsValue = 576600000
        $script:teamsOption = $null
        [void](Invoke-Step -Name 'meeting: Teams source option' -Method 'GET' `
            -Path 'EntityDefinitions(LogicalName=''tavu_meeting'')/Attributes(LogicalName=''tavu_source'')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?$select=LogicalName&$expand=OptionSet($select=Options),GlobalOptionSet($select=Options)' `
            -Validate {
                param($x)
                $j = Get-Json $x
                $found = $null
                foreach ($set in @($j.OptionSet, $j.GlobalOptionSet)) {
                    if ($null -eq $set) { continue }
                    foreach ($o in @($set.Options)) {
                        foreach ($l in @($o.Label.LocalizedLabels)) {
                            if ($l.LanguageCode -eq 1033 -and $l.Label -ieq 'Teams') { $found = [int]$o.Value }
                        }
                    }
                }
                if ($null -eq $found) { 'no option labelled Teams in tavu_meeting.tavu_source' }
                else { $script:teamsOption = $found }
            })
        if ($script:teamsOption) { $teamsValue = $script:teamsOption }
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $externalId = -join ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes("release-smoke-$stamp")) | ForEach-Object { $_.ToString('x2') })
        } finally {
            $sha.Dispose()
        }
        [void](Invoke-Step -Name 'meeting: by external id' -Method 'GET' -Path ('tavu_meetings?$select=activityid&$filter=tavu_externalmeetingid eq ''{0}''&$top=1' -f $externalId))
        $start = Get-IsoUtc $nowUtc.AddHours(-1)
        $r = Invoke-Step -Name 'meeting: create' -Method 'POST' -Path 'tavu_meetings' -Body ([ordered]@{
            subject                  = "$prefix meeting"
            tavu_transcript          = "$prefix transcript"
            statuscode               = 576600001
            tavu_externalmeetingid   = $externalId
            tavu_attendees           = $email
            scheduledstart           = $start
            actualstart              = $start
            scheduleddurationminutes = 30
            actualdurationminutes    = 30
            tavu_source              = $teamsValue
        })
        $meetingId = Get-CreatedId $r
        if ($meetingId) { $script:created['meeting'] = $meetingId }
        $ownerId = $null
        $r = Invoke-Step -Name 'meeting: organizer user by Entra object id' -Method 'GET' -Path ('systemusers?$select=systemuserid&$filter=azureactivedirectoryobjectid eq ''{0}'' and isdisabled eq false&$top=1' -f $adminAad) `
            -Needs ([ordered]@{ 'setup: administrator Entra object id' = $adminAad }) -Validate {
                param($x) if (@(Get-Rows $x).Count -eq 0) { 'no enabled user with the administrator''s Entra object id' }
            }
        if ($r) { $ownerId = [string](Get-Rows $r)[0].systemuserid }
        # The gateway treats a failure here as a warning; the check counts it as a failure.
        [void](Invoke-Step -Name 'meeting: owner change to the organizer' -Method 'PATCH' -Path "tavu_meetings($meetingId)" `
            -Needs ([ordered]@{ 'meeting: create' = $meetingId; 'meeting: organizer user by Entra object id' = $ownerId }) -Body @{ 'ownerid@odata.bind' = "/systemusers($ownerId)" })
        $sourceId = $null
        $r = Invoke-Step -Name 'meeting source: Teams row' -Method 'GET' -Path 'tavu_meetingsources?$select=tavu_meetingsourceid&$filter=tavu_name eq ''Teams''&$top=1' -Validate {
            param($x) if (@(Get-Rows $x).Count -eq 0) { 'no Meeting Source row named Teams (run the setup routine)' }
        }
        if ($r) { $sourceId = [string](Get-Rows $r)[0].tavu_meetingsourceid }
        [void](Invoke-Step -Name 'meeting source: sync status' -Method 'PATCH' -Path "tavu_meetingsources($sourceId)" -Extra $ifMatch `
            -Needs ([ordered]@{ 'meeting source: Teams row' = $sourceId }) -Body ([ordered]@{
                tavu_lastsync     = (Get-IsoUtc $nowUtc)
                tavu_itemscreated = 1
            }))
    } finally {
        $script:principalId = $null
        if ($fixtureAgentId) {
            $dr = Invoke-Dv -Method 'PATCH' -Path "tavu_agents($fixtureAgentId)" -Extra @{ 'If-Match' = '*' } -Body ([ordered]@{ statecode = 1; statuscode = 2 })
            if ($dr.Ok) { Write-Host "Fixture agent deactivated." -ForegroundColor DarkGray }
            else { Write-Warning "Could not deactivate the fixture agent $fixtureAgentId (HTTP $($dr.Status)): $($dr.Message)" }
        }
        $counts = [ordered]@{
            passed  = @($script:steps | Where-Object { $_.result -eq 'PASS' }).Count
            failed  = @($script:steps | Where-Object { $_.result -eq 'FAIL' }).Count
            skipped = @($script:steps | Where-Object { $_.result -eq 'SKIP' }).Count
        }
        $out = [ordered]@{
            run           = $stamp
            principalId   = $state.principalId
            applicationId = $state.applicationId
            role          = [ordered]@{ id = $state.roleId; name = $state.roleName }
            counts        = $counts
            steps         = @($script:steps)
            createdRows   = $script:created
        }
        [IO.File]::WriteAllText($roleReportPath, ($out | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding($false)))
        $script:roleCounts = $counts
        $script:roleReportPath = $roleReportPath
    }
}

# ---------- 3. Setup (Mode=full) and 4. Diagnose ----------
$diagnoseRan = $false
$fail = $false
$realErrors = @(); $realWarnings = @(); $expected = @()
if (-not $RoleCheckOnly) {
    Write-Host "`n=== Setup: Mode=full ===" -ForegroundColor Cyan
    $run = 0
    do {
        $run++
        $r = Invoke-Setup -Mode 'full'
        Write-Host "Run $run :" -NoNewline; Write-Host " $($r.Summary)"
    } until ($r.Complete -or $run -ge $MaxFullRuns)
    if (-not $r.Complete) { throw "Setup did not complete after $MaxFullRuns runs." }

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

    # The diagnose verdict is computed as before; the run no longer exits here, so the check still runs.
    $fail = ($realErrors.Count -gt 0) -or ((-not $AllowWarnings) -and $realWarnings.Count -gt 0)
    $diagnoseRan = $true
} else {
    Write-Host "`n-RoleCheckOnly: import, setup and diagnose skipped." -ForegroundColor DarkGray
}

# ---------- 5. Least-privilege check ----------
$script:roleCounts = $null
$script:roleReportPath = $null
if ($SkipRoleCheck) {
    Write-Host "`nLeast-privilege check skipped: not valid for a release." -ForegroundColor Yellow
} else {
    Write-Host "`n=== Least-privilege check: OpenTavu Gateway role ===" -ForegroundColor Cyan
    Invoke-RoleCheck
    $c = $script:roleCounts
    $color = 'Green'
    if ($c.failed) { $color = 'Red' }
    Write-Host ("Least-privilege check: {0} passed, {1} failed, {2} skipped." -f $c.passed, $c.failed, $c.skipped) -ForegroundColor $color
    Write-Host "Check report saved to $($script:roleReportPath)"
}

# ---------- Verdict ----------
$roleFailed = 0
if ($script:roleCounts) { $roleFailed = $script:roleCounts.failed }
if ($fail -or $roleFailed -gt 0) {
    Write-Host "SMOKE TEST FAILED: $($realErrors.Count) errors, $($realWarnings.Count) warnings in the diagnose; $roleFailed failed steps in the least-privilege check." -ForegroundColor Red
    exit 1
}
if ($SkipRoleCheck) {
    Write-Host "SMOKE TEST PASSED: a clean install configures itself with no unexpected findings. The least-privilege check was skipped: not valid for a release." -ForegroundColor Green
} elseif (-not $diagnoseRan) {
    Write-Host "SMOKE TEST PASSED: the least-privilege check passed (setup and diagnose not run: -RoleCheckOnly)." -ForegroundColor Green
} else {
    Write-Host "SMOKE TEST PASSED: a clean install configures itself with no unexpected findings, and the least-privilege check passed." -ForegroundColor Green
}
