# Web-to-Lead (Microsoft Forms + Power Automate)

**Audience:** implementers. **Purpose:** how anonymous inbound (Path B) enters OpenTavu, from a public web form to a triaged `tavu_lead`, using only Microsoft Power Platform pieces.

## Where this fits

OpenTavu has two entry paths (see `sales-model.md`). Path A (networking, referrals) creates a `contact` directly. Path B is anonymous inbound (web forms, cold emails): it lands in the `tavu_lead` buffer first, then AI triage recommends what to do with it. This document covers the web-form variant of Path B.

The pipeline:

`Microsoft Form  ->  Power Automate (Fl.Lead.WebToLead)  ->  tavu_lead created  ->  Pl.Lead.Triage (AI)  ->  human decides (Approve & Promote / Link to Existing / Discard)`

The same flow can also deliver a lead magnet by email, which is how OpenTavu's own guide is distributed (optional, see step 4).

## The form

A public Microsoft Form collects the minimum: Name, Email, Company (optional), Message (optional), and a Language choice (used to localize both the lead and, if used, the emailed asset). Any form provider works; Microsoft Forms keeps it inside the tenant at no extra cost.

## The flow: `Fl.Lead.WebToLead`

Connections used: Microsoft Forms, Microsoft Dataverse, OneDrive for Business, Office 365 Outlook.

1. **Trigger, "When a new response is submitted"** (Microsoft Forms webhook on the capture form). Fires once per response.
2. **Get response details** (Microsoft Forms): reads Name, Email, Company, Message, and Language for that response.
3. **Create the lead** (Dataverse, create `tavu_lead`):

   | Lead field | Value |
   |---|---|
   | `tavu_subject` | derived from the response (e.g. "Website inquiry - {Name}, {Company}") |
   | `tavu_firstname` | Name |
   | `tavu_companyname` | Company |
   | `tavu_email` | Email |
   | `tavu_source` | Web Form (`576600000`) |
   | `tavu_leadlanguage` | Espanol `576600000` / English `576600001`, from the form's Language answer |
   | `tavu_sourcedetails` | "[source: website / lead magnet: OpenTavu SMB CRM Guide]" followed by the Message |

4. **Deliver the asset (optional, language-branched):** an If on the Language answer:
   - Espanol: get the ES PDF from OneDrive and email it (Office 365 Outlook), subject "Tu guia de OpenTavu ya esta aqui".
   - English: get the EN PDF from OneDrive and email it, subject "Your OpenTavu guide is here".
   The PDFs, the OneDrive path, and the form id are tenant-configured (they live in the flow and OneDrive), not shipped in the managed solution.

5. **Triage runs automatically.** Creating the `tavu_lead` fires `Pl.Lead.Triage` (async, on create). The AI matches the lead against existing `contact` / `account` records and writes a recommendation (promote / link / discard) with a confidence score. A human then approves in one click. See `module3-lead-triage-build-plan.md`.

## Notes

- The Language choice drives two things: `tavu_leadlanguage` (so downstream AI text matches the lead's language) and the emailed guide's language.
- The `[source: ...]` prefix in `tavu_sourcedetails` tags provenance for reporting (which asset or campaign produced the lead).
- Naming follows the convention `Fl.<Area>.<Purpose>` (see the naming conventions).
- Reusability: swap Microsoft Forms for any web form; the create-lead plus triage core does not change. The email-delivery branch is optional and specific to a lead-magnet use.

## Document control

| Version | Date | Author | Notes |
|---|---|---|---|
| 1.0 | 2026-08-17 | Gustavo Gonzalez Villani (with Cowork) | Initial doc of the web-to-lead flow `Fl.Lead.WebToLead`: Microsoft Forms webhook, get response, create `tavu_lead`, language-branched guide email; triage fires on create. Fills the flow-documentation gap (flows were previously undocumented in the repo). |
