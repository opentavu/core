# OpenTavu, Teams Sync Wizard (design)

> Living design doc for the "Enable Teams sync" wizard: a guided popup launched from the Teams row
> of the `tavu_meetingsource` config table that tells a tenant admin exactly which setup gate is
> missing (instead of a raw 403) and how to fix it. Backend already exists; this doc governs the
> UI + the thin proxy that feeds it.
> Related: `module3b-activity-capture-build-plan.md`, `view-definitions.md` (§22 Meeting + Meeting
> Source), gateway `Functions/TeamsHealthFunction.cs`, `Intake/GraphTranscriptClient.cs`.

## Design intent

Enabling Teams transcript sync depends on **two independent things**:

1. **Tenant config (the 3 gates)**, org-level, checked by Microsoft Graph:
   - **Gate 1, Consent & permissions:** admin consent granted to the OpenTavu app for
     `OnlineMeetingTranscript.Read.All` + `OnlineMeetings.Read.All`.
   - **Gate 2, Application access policy:** `New-/Grant-CsApplicationAccessPolicy` covers the
     polled user (Teams PowerShell).
   - **Gate 3, Transcript API access:** `Set-CsTeamsMeetingConfiguration -Identity Global
     -EnableGraphTranscriptAccess $true` (Teams PowerShell).
2. **Per-user opt-in:** each user whose meetings we ingest must be flagged
   `systemuser.tavu_meetingsyncenabled = true`. This is what the poller reads
   (`GetMeetingSyncUserIdsAsync`), not the gates.

The wizard's job (MVP) is to **diagnose the 3 gates and guide remediation**, then **point the admin
to enable users** (Settings → Users → Meeting Sync Enabled). Enabling users is a documented manual
step, not done inside the wizard (kept out per "simplicity as competitive advantage"; see Roadmap).

## What already exists (do not rebuild)

- `GraphTranscriptClient.ProbeTranscriptAccessAsync(userId)` runs the same `getAllTranscripts` call
  the poller uses with a 1-minute window and classifies the outcome into the 3 gates
  (`TeamsHealthProbe`). Never throws.
- `GET /api/teams/health[?userId=<aad-object-id>]` (`TeamsHealthFunction`), header
  `X-OpenTavu-Tenant-Key`. If `userId` omitted, probes the first Dataverse-flagged user; if none,
  returns `{ healthy:false, reason:"NoUserToProbe" }`. Response shape:
  ```json
  {
    "healthy": false,
    "probedUserId": "<guid>",
    "gates": { "consentAndPermissions": "Pass|Fail|Unknown",
               "applicationAccessPolicy": "...", "transcriptApiAccess": "..." },
    "remediation": { "consentAndPermissions": "<hint or null>", "...": null },
    "detail": { "httpStatus": 403, "errorCode": "...", "message": "..." }
  }
  ```
- The poller stamps `tavu_lastsync` / `tavu_itemscreated` on the `tavu_meetingsource` row named
  "Teams" every run (banner data).

## Architecture decision: the wizard does NOT call the gateway directly

The tenant key (`X-OpenTavu-Tenant-Key`) is a shared secret and must never ship in client JS, and a
browser call to the gateway would also hit CORS. So the wizard reaches the health endpoint through
a **server-side proxy**, reusing the exact pattern `Pl.Case.SlaAssignment` uses to call the gateway:
a plugin reads the env vars `tavu_GatewayUrl` + `tavu_GatewayKey` and makes the HTTP call with the
key server-side.

Flow: **web resource (wizard) → `Xrm.WebApi` → Custom API `tavu_ProbeMeetingSource` (plugin) →
`GET /api/teams/health` → back up as raw JSON**. Same "client → Custom API → gateway" shape already
used by `tavu_AssociateMeeting` / `tavu_BuildMeetingEmailDraft`.

## Components

### 1. Custom API `tavu_ProbeMeetingSource` (global/unbound)

Thin proxy. Reads `tavu_GatewayUrl` / `tavu_GatewayKey` (environment variables, same as
`Pl.Case.SlaAssignment`), calls the health endpoint, returns the body verbatim so the wizard owns
the rendering (plugin stays dumb; Graph error strings can change without a plugin redeploy).

- **Request:** `SourceName` [String, optional, default `Teams`], `UserId` [String, optional,
  Entra object id to probe].
- **Response:** `Ok` [Boolean] (transport succeeded), `HttpStatus` [Int], `HealthJson` [String]
  (raw gateway JSON, or a small proxy-level error JSON when env vars are missing / gateway
  unreachable).
- **Routing:** `SourceName == "Teams"` → `GET {tavu_GatewayUrl}/api/teams/health[?userId=]`. Other
  names → `{ healthy:false, reason:"SourceNotSupported" }` (Fathom etc. add their own route later).
- **Service context:** config reads (`environmentvariable*`) on SystemService; no record writes.
- Plugin: `src/Plugins/Pl.MeetingSource.Probe/Probe.cs`, inherits `PluginBase`, registered as the
  Custom API's plugin type (no SDK message step; the message is the trigger). Mirror
  `Pl.Meeting.Associate` structure and the SlaAssignment HTTP/env-var helpers.

### 2. Web resource `tavu_teamssyncwizard.html`

Self-contained popup. On open (and on "Re-check"): calls `tavu_ProbeMeetingSource` via
`Xrm.WebApi.online.execute`, parses `HealthJson`, renders the 3 gates as a step list
(Pass green / Fail red / Unknown grey), and for each failing gate shows its remediation with
copy-paste blocks:

- Gate 1 → admin-consent link + the required app permissions.
- Gate 2 → `New-CsApplicationAccessPolicy` / `Grant-CsApplicationAccessPolicy` snippet.
- Gate 3 → `Set-CsTeamsMeetingConfiguration -Identity Global -EnableGraphTranscriptAccess $true`.

Handles the two non-gate outcomes: `NoUserToProbe` (tell the admin to flag a user first, link to
Settings → Users) and `httpStatus 404 / "no transcript yet"` (setup looks fine, validate with a real
transcribed meeting). Final step (always shown): **"Enable users for sync"** instructions pointing to
**Settings → Users → Meeting Sync Enabled**. Reuses the `tavu_systemsettings_open.html` conventions
(sandbox-safe `getXrm()`, `credentials:"include"`).

### 3. Form script `tavu_meetingsource_form.js` (namespace `OpenTavu.MeetingSource.Form`)

Command handler `OpenTavu.MeetingSource.Form.openTeamsWizard(primaryControl)` opens the web resource
as a centered modal: `Xrm.Navigation.navigateTo({ pageType:"webresource",
webResourceName:"tavu_teamssyncwizard.html", data:<sourceName+userId?> }, { target:2, position:1,
width:{value:560,unit:"px"}, height:{value:640,unit:"px"} })`. Self-filters: the button is meant for
the Teams source row (MVP: Teams only; later branch by `tavu_provider`).

## Manual registration (Dataverse, Gustavo's step)

1. Create Custom API `tavu_ProbeMeetingSource` (unbound) with the params above; bind the plugin type
   from the built `Pl.MeetingSource.Probe` assembly.
2. Confirm env vars `tavu_GatewayUrl` + `tavu_GatewayKey` exist (already used by SLA); the health
   route lives on the same gateway.
3. Upload web resource `tavu_teamssyncwizard.html` and JS `tavu_meetingsource_form.js`.
4. Add a **command bar button** on the `tavu_meetingsource` form ("Test & configure sync") → Run
   JavaScript → `OpenTavu.MeetingSource.Form.openTeamsWizard`, pass PrimaryControl. Visible on the
   Teams row.
5. Add all to the OpenTavu solution.

## Roadmap (out of MVP)

- **Enable users from the wizard:** a step that lists users and toggles
  `systemuser.tavu_meetingsyncenabled` in-place (extra systemuser writes + test surface; deferred).
- **Fathom (and other sources):** each gets its own `/api/<source>/health` route and a branch in the
  probe / wizard. The `tavu_ProbeMeetingSource` name is source-generic on purpose.

## Document control

| Version | Date | Author | Notes |
|---|---|---|---|
| 1.0 | 2026-08-12 | Gustavo González Villani (with Cowork) | Initial design. MVP = diagnose the 3 tenant gates via a popup on the Teams `tavu_meetingsource` row, with copy-paste remediation and a final step pointing to Settings → Users → Meeting Sync Enabled. Wizard reaches `/api/teams/health` through the new thin Custom API proxy `tavu_ProbeMeetingSource` (reusing the SlaAssignment env-var/gateway pattern) so the tenant key stays server-side. Enabling users from the wizard and non-Teams sources deferred to Roadmap. |
