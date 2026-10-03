# Setup: Initialize Configuration (design)

**Status:** design approved (2026-10-02); seed data v1.1.0 ready (441 rows); security roles created; plugin not built yet.
**Components:** Custom API `tavu_InitializeConfiguration`, plugin `Pl.Setup.Initialize`, seed file `src/Plugins/Pl.Setup.Initialize/Seed/opentavu-seed.json`, button on the System Settings form.

## Why

A managed solution carries tables, plugins, forms and flows, but **no rows**. The OpenTavu engine depends on configuration rows (case statuses and their behavior flags, case types, customer tiers, SLA matrix, business calendar, sales stages, AI task prompts). On a clean install those tables are empty, so the engine silently does nothing: no initial status, no AI routing, no SLA, no AI at all. Verified on 2026-10-01: `OpenTavu_1_1_0_2_managed.zip` contains no data, while `installation.md`, `configuration.md` and `README.md` claimed the reference data ships "pre-loaded in the managed solution".

This is an adoption prerequisite, not a feature: without it no third-party install works.

## What it does

One button, "Verify and complete configuration", on the System Settings form, calling `tavu_InitializeConfiguration`:

1. **Seed** the rows in `opentavu-seed.json`, in dependency order. Idempotent: match by a natural key (code or name, never GUID), create what is missing, fill empty fields of existing rows, never overwrite a value the firm already changed. Safe to re-run after every upgrade.
2. **Diagnose** the install and return a checklist: disabled OpenTavu plugin steps, status behavior flags missing or duplicated, gateway environment variables empty, no AI model or no default model, company profile empty, no sales period covering today.

Language: rows are written in the organization base language (`@i18n.es` overrides for 3082). Runtime values: the business calendar takes the installer's time zone; sales periods are generated for the current and next calendar year.

## Seed classification (from a full review of opentavu.crm.dynamics.com, 2026-10-02)

| Table | Rows | Seed? | Why |
|---|---|---|---|
| `tavu_casestatus` | 13 | Yes | Engine: initial status, AI routing, SLA pause and resume depend on the flags |
| `tavu_casetype` | 7 | Yes | Categorization and SLA matching; one default type |
| `tavu_customertierdefinition` | 3 | Yes | SLA matrix; default tier in System Settings |
| `tavu_businesscalendar` + `tavu_calendarworkinghours` | 1 + 10 | Yes, generic | SLA business-time math; renamed "Standard 8x5", installer time zone |
| `tavu_sla` | 4 | Yes | SLA targets by tier and type |
| `tavu_businessline` / `tavu_category` / `tavu_subcategory` | 4 / 12 / 24 | Yes, as starter taxonomy | Without a taxonomy every case routes to manual review; firms edit it |
| `tavu_salesstage` | 4 | Yes | Opportunity pipeline (Decision 35) |
| `tavu_goaltype` | 1 | Yes | Forecasting Phase 1 (Revenue) |
| `tavu_salesperiod` | 4 | Generated | Forecasting needs a period covering today |
| `tavu_unitofmeasureschedule` + `tavu_uom` | 2 + 5 | Yes | Time (Hour, Day = 8 h, Month = 160 h) and Quantity (Unit, License) |
| `tavu_aitaskconfiguration` | 4 active | Yes, without model binding | The prompts are the AI; they lived only in this tenant |
| `tavu_meetingsource` | 1 | Yes, disabled | Teams connector row the wizard expects |
| `tavu_systemsettings` | 1 | Yes, empty fields only | Defaults for every engine setting |
| `tavu_aimodel` | 2 | No | Provider, endpoint and key are installer-specific |
| `tavu_companyprofile` | 1 | No | Firm data |
| `tavu_businessclosure` | 1 | No | Holidays are country-specific |
| `tavu_country` / `tavu_stateprovince` / `tavu_city` | 2 / 85 / 258 | Yes, as the optional `geography` pack (on by default) | United States and Colombia, the two markets served; currency resolved by ISO code when it exists in the org |
| `tavu_pricelist`, `tavu_pricelistitem`, `tavu_product`, `tavu_kitcomponent` | 1 / 1 / 1 / 0 | No | Firm catalog |

Curation applied versus the tenant: em dashes removed from the Lead Triage prompt and the Performance hint; the case categorization prompt now says "exactly as the names given in the lists" (it said "English names", which breaks a Spanish taxonomy); Meeting Capture example domain replaced with contoso.com; provider suffixes removed from task names ("Lead Triage (Open AI)" was bound to the Azure model); all status flags explicit true or false.

## Decisions (2026-10-02)

- Starter taxonomy: seeded as an editable starting point.
- Units of measure: Hour, Day, Month, Unit, License.
- Geography: seeded as an optional pack, on by default.
- Security roles: built in the same delivery (below).

## Security roles (created 2026-10-02 in opentavu.crm.dynamics.com and added to the OpenTavu solution)

Users get **Basic User** (Microsoft) plus one OpenTavu role. In a single business unit, "Local" equals the whole firm.

| Role | OpenTavu records | Configuration tables | Account, contact, notes, activities |
|---|---|---|---|
| OpenTavu Sales | Leads, opportunities, proposals, lines: create own, read all, edit within BU. Cases and interactions: read and link. Targets and forecast: read | Read and link | Create own, read all, edit within BU |
| OpenTavu Service | Cases, interactions, knowledge articles: create own, read all, edit within BU. Sales records: read and link | Read and link | Same as Sales |
| OpenTavu Admin | Everything, organization-wide, including configuration tables | Full | Full |
| OpenTavu Gateway | Cases and interactions: create, read, write. Meeting source: read, write. Everything else: read | Read | Contacts: create and write. Accounts: read. Notes: create. Activities (meetings): create and write |

Activity tables (`tavu_meeting`, `tavu_opportunityclose`, `tavu_timeentry`) are governed by Dataverse's shared Activity privileges, not per-table privileges. The Gateway role is meant to replace System Administrator on the gateway's application user (least privilege), after a test run.

## Sales periods: rolling horizon

Periods change every year, so they must never depend on a person remembering. The plugin generates the periods of the current and next year, and the same logic runs daily from `Fl.Forecast.SnapshotDaily` (already a daily, Dataverse-only flow), so there is always a full year ahead. Granularity follows `tavu_forecastperiodtype` (Month or Quarter). Proposal pending approval: a `tavu_fiscalyearstartmonth` setting (default January) for firms whose fiscal year does not start in January.

## Proposal email prompt: configurable, gateway stays stateless

The prompt moves to `tavu_aitaskconfiguration` (new task key "Proposal Email Draft"). `Pl.Proposal.BuildEmailDraft` reads it in the tenant and sends prompt, temperature and max tokens with the request; the gateway uses what arrives and falls back to its built-in default only when nothing is sent (older tenants keep working). The gateway does not store or validate per-client configuration: configuration lives in each client's Dataverse and travels with the request, which keeps the gateway stateless, multi-tenant without a registry, and identical for self-hosters (Decisions 42 and 46).

## Findings outside the seed (to decide)

1. ~~No OpenTavu security roles exist~~: resolved 2026-10-02 (section above).
2. ~~Legacy table `tavu_aitaskconfig`~~: deleted by Gustavo 2026-10-02. The resolver's error text still names it; fix the message.
3. ~~Two default AI models; OpenAI model on `tavu_AzureOpenAIKey`~~: fixed 2026-10-02 (OpenAI is the only default; its secret name is now `tavu_OpenAIKey`). Note: with `tavu_GatewayUrl` and `tavu_GatewayKey` set, the tenant's model rows are not used at all; the gateway's own model runs.
4. **The proposal email prompt is hardcoded in the gateway**: design agreed (section above); pending the new task-key option value.
5. **Sales periods only cover 2026**: forecasting in this tenant stops having a current period on 2027-01-01.
6. ~~Units of measure~~: resolved in seed v1.1.0.
7. ~~Inactive duplicate case categorization~~: deleted by Gustavo 2026-10-02.
8. **Seed CSVs in `1-Producto/Seed-data` are damaged**: headers start with the literal text `\xEF\xBB\xBF`, `seed_tavu_stateprovince.csv` has double-encoded accents ("AtlÃ¡ntico"), and `seed_tavu_city.csv` repeats Brownsville, Texas. The JSON seed is now the source of truth; the CSVs should be archived.

## Document control

| Version | Date | Author | Notes |
|---|---|---|---|
| 0.1 | 2026-10-02 | Gustavo González Villani (with Claude) | Initial design and seed inventory from a full review of the tenant. |
| 0.2 | 2026-10-02 | Gustavo González Villani (with Claude) | Decisions recorded; geography pack and five units added (seed v1.1.0); security roles created; sales-period rolling horizon and stateless proposal-prompt design. |
