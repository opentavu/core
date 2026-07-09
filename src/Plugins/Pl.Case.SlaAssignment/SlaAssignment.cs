using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;

namespace Pl.Case.SlaAssignment
{
    /// <summary>
    /// SLA assignment for tavu_case (Increment 1 — target-date calculation, no gateway yet).
    /// When the case Type is set/changed, resolves the applicable SLA (Customer Tier + Type,
    /// with Evaluation Priority and fallbacks), then computes calendar-aware Response/Resolution
    /// Target Dates anchored to createdon (business hours + closures, DST-aware via the SDK's
    /// LocalTime/UtcTime messages). Writes tavu_sla (applied SLA), the two target dates, and
    /// tavu_slastatus = On Track. Modifies the Target in-place (Pre-Operation) — no extra Update.
    ///
    /// Increment 2 (later) adds the call to the OpenTavu gateway to schedule the durable SLA timers.
    /// </summary>
    /// <remarks>
    /// Plugin Registration Tool — register these steps (all Primary Entity = tavu_case, Sync, Server):
    ///   1. SLA assign:   Message Update (+ Create), Filtering Attr: tavu_type,       Stage 20 (Pre-op)
    ///   2. SLA schedule: Message Update (+ Create), Filtering Attr: tavu_type,       Stage 40 (Post-op)
    ///   3. Pause guard:  Message Update,             Filtering Attr: tavu_status,    Stage 20 (Pre-op)
    ///   4. Pause/resume: Message Update,             Filtering Attr: tavu_status,    Stage 40 (Post-op)
    /// The router in ExecuteInternal dispatches by message/stage/changed-attribute.
    /// </remarks>
    public class SlaAssignment : PluginBase
    {
        // ---------- tavu_case ----------
        private const string CaseEntity          = "tavu_case";
        private const string CaseType            = "tavu_type";                 // lookup -> tavu_casetype
        private const string CaseCustomer        = "tavu_customer";             // polymorphic (account|contact)
        private const string CaseAppliedSla      = "tavu_sla";                  // lookup -> tavu_sla (applied SLA)
        private const string CaseResponseTarget  = "tavu_responsetargetdate";   // DateTime (UTC)
        private const string CaseResolutionTarget = "tavu_resolutiontargetdate"; // DateTime (UTC)
        private const string CaseSlaStatus       = "tavu_slastatus";            // Choice
        private const string CaseFirstResponse   = "tavu_firstresponsedate";    // DateTime
        private const string CaseStatus          = "tavu_status";               // lookup -> tavu_casestatus (operational status)
        private const string CasePausedOn        = "tavu_slapausedon";          // DateTime — when the current pause started
        private const string CreatedOn           = "createdon";

        // ---------- tavu_casestatus (status vocabulary + behaviors) ----------
        private const string StatusEntity        = "tavu_casestatus";
        private const string StatusPausesSla     = "tavu_pausessla";            // Yes/No — this status stops the SLA clock
        private const string StatusStateCategory = "tavu_statecategory";        // Choice: Active/Resolved/Cancelled
        private const int    StateCategoryActive    = 576600000;
        private const int    StateCategoryResolved  = 576600001;
        private const int    StateCategoryCancelled = 576600002;

        private const string CaseResolutionDate  = "tavu_resolutiondate";       // DateTime (set on resolve)
        private const int    SlaStatusMet        = 576600003;

        // ---------- tavu_caseinteraction (guardrail) ----------
        private const string InteractionEntity   = "tavu_caseinteraction";
        private const string IxCase              = "tavu_case";                 // lookup -> tavu_case
        private const string IxDirection         = "tavu_direction";            // Choice
        private const int    DirOutbound         = 576600001;                   // agent -> customer

        // ---------- customer (account / contact) ----------
        private const string CustomerTierField   = "tavu_customertier";         // lookup -> tavu_customertierdefinition (same name on both)

        // ---------- tavu_systemsettings (singleton) ----------
        private const string SettingsEntity      = "tavu_systemsettings";
        private const string SettingsDefaultTier = "tavu_defaultcustomertier";  // lookup -> tavu_customertierdefinition (SLA fallback)

        // ---------- tavu_sla ----------
        private const string SlaEntity           = "tavu_sla";
        private const string SlaTier             = "tavu_customertier";         // lookup -> tavu_customertierdefinition
        private const string SlaType             = "tavu_casetype";             // lookup -> tavu_casetype (null = tier default)
        private const string SlaResponseHours    = "tavu_responsetargethours";  // Decimal
        private const string SlaResolutionHours  = "tavu_resolutiontargethours"; // Decimal
        private const string SlaCalendar         = "tavu_calendar";             // lookup -> tavu_businesscalendar
        private const string SlaEvalPriority     = "tavu_evaluationpriority";   // Whole Number (lower = first)

        // ---------- tavu_businesscalendar ----------
        private const string CalEntity           = "tavu_businesscalendar";
        private const string CalTimeZone         = "tavu_timezone";             // Whole Number (TimeZoneCode)
        private const string CalIs247            = "tavu_is24x7";               // Yes/No
        private const string CalIsDefault        = "tavu_isdefault";            // Yes/No

        // ---------- tavu_calendarworkinghours ----------
        private const string WhEntity            = "tavu_calendarworkinghours";
        private const string WhCalendar          = "tavu_calendar";
        private const string WhDayOfWeek         = "tavu_dayofweek";            // Choice 1=Mon .. 7=Sun
        private const string WhStartMinutes      = "tavu_starttime";            // Choice, value = minutes from midnight
        private const string WhEndMinutes        = "tavu_endtime";              // Choice, value = minutes from midnight

        // ---------- tavu_businessclosure ----------
        private const string CloEntity           = "tavu_businessclosure";
        private const string CloDate             = "tavu_date";                 // Date Only
        private const string CloCalendar         = "tavu_calendar";             // optional (null = all calendars)

        // ---------- option values ----------
        private const int SlaStatusOnTrack  = 576600000;
        private const int SlaStatusWarning  = 576600001;
        private const int SlaStatusBreached = 576600002;
        private const int SlaStatusPaused   = 576600004;
        private const int StateActive      = 0;
        private const int MinutesPerDay    = 1440;

        // ---------- gateway scheduling (Increment 2) ----------
        private const string CaseOrchestrationId = "tavu_slaorchestrationid"; // stores the durable instance id
        private const string GatewayUrlVar = "tavu_GatewayUrl";  // env variable: gateway base URL
        private const string GatewayKeyVar = "tavu_GatewayKey";  // env variable: per-tenant key
        private const double WarningFraction = 0.8;              // Warning fires at 80% of the way to resolution
        private const int GatewayTimeoutSeconds = 30;

        // Runs deeper than 1 on purpose: case created (depth 1) -> async Categorize updates
        // tavu_type (depth 2) -> SLA must re-fire. Cannot recurse on itself (Pre-Op edits the
        // Target in place; the async step's only Update writes tavu_slaorchestrationid, which is
        // not a filtering attribute).
        protected override int MaxDepth => 3;

        public SlaAssignment() : base(typeof(SlaAssignment)) { }

        protected override void ExecuteInternal(LocalPluginContext localContext)
        {
            if (localContext == null) throw new ArgumentNullException(nameof(localContext));

            localContext.Trace("SlaAssignment: ExecuteInternal entered.");

            var ctx = localContext.PluginExecutionContext;

            if (!(ctx.InputParameters.Contains("Target") && ctx.InputParameters["Target"] is Entity target))
            {
                localContext.Trace("Target missing or not an Entity. Exiting.");
                return;
            }

            if (!string.Equals(target.LogicalName, CaseEntity, StringComparison.Ordinal))
            {
                localContext.Trace("Unexpected entity '{0}'. Exiting.", target.LogicalName);
                return;
            }

            // Pause/resume path: an Update that changes tavu_status routes here. Pause is status-driven —
            // the new status's tavu_pausessla flag decides. Pre-op (20) = guardrail; Post-op (40) = mechanics.
            if (string.Equals(ctx.MessageName, "Update", StringComparison.OrdinalIgnoreCase) && target.Contains(CaseStatus))
            {
                if (ctx.Stage == 20) GuardStatusChange(localContext, target);
                else HandleStatusChange(localContext, target);
                localContext.Trace("SlaAssignment: ExecuteInternal exiting (status path).");
                return;
            }

            // Pre-Operation (sync) computes the SLA + target dates; Post-Operation (async) schedules
            // the durable timers via the gateway. Route by stage (20 = Pre, 40 = Post).
            if (localContext.PluginExecutionContext.Stage == 40)
                ScheduleSla(localContext, target);
            else
                AssignSla(localContext, target);

            localContext.Trace("SlaAssignment: ExecuteInternal exiting.");
        }

        private void AssignSla(LocalPluginContext localContext, Entity target)
        {
            IOrganizationService svc = localContext.SystemService; // config tables + derived fields
            var ctx = localContext.PluginExecutionContext;
            bool isUpdate = string.Equals(ctx.MessageName, "Update", StringComparison.OrdinalIgnoreCase);

            // On Update the Target only carries changed fields; read the rest from the DB.
            // On Create the record doesn't exist yet — read from Target and anchor on UtcNow.
            Entity stored = null;
            if (isUpdate)
                stored = svc.Retrieve(CaseEntity, target.Id, new ColumnSet(CaseType, CaseCustomer, CreatedOn));

            EntityReference typeRef = target.GetAttributeValue<EntityReference>(CaseType)
                                      ?? stored?.GetAttributeValue<EntityReference>(CaseType);
            EntityReference customerRef = target.GetAttributeValue<EntityReference>(CaseCustomer)
                                          ?? stored?.GetAttributeValue<EntityReference>(CaseCustomer);
            DateTime createdOnUtc = stored != null
                ? stored.GetAttributeValue<DateTime>(CreatedOn)
                : DateTime.UtcNow;

            localContext.Trace("Inputs: type={0} customer={1} createdon={2:o}",
                typeRef?.Id, customerRef == null ? "(none)" : customerRef.LogicalName, createdOnUtc);

            // --- Resolve the customer tier (from account or contact); fall back to the system default ---
            Guid? tierId = ResolveTier(localContext, svc, customerRef);
            if (!tierId.HasValue)
            {
                // Customer has no tier (e.g. an unknown inbound sender, or a manual case where the tier
                // was left blank). Use the configured system-default tier instead of skipping the SLA.
                // The intake never stamps a tier on the contact — a blank tier is honest; this is where
                // it's handled. Documented in service-model §4 matching step (c).
                tierId = ResolveDefaultTier(localContext, svc);
                if (!tierId.HasValue)
                {
                    localContext.Trace(
                        "SLA NOT APPLIED: the case customer has no tier and no fallback is configured. " +
                        "ACTION: set 'tavu_defaultcustomertier' (Default Customer Tier) on the System Settings " +
                        "record (point it to e.g. Standard). Case creation continues without an SLA.");
                    return;
                }
                localContext.Trace("Customer has no tier; using system-default tier {0}.", tierId);
            }

            // --- Resolve the SLA (Tier + Type, eval priority, tier-default fallback) ---
            Entity sla = ResolveSla(localContext, svc, tierId.Value, typeRef?.Id);
            if (sla == null)
            {
                localContext.Trace("No SLA matched tier={0} type={1}. Skipping.", tierId, typeRef?.Id);
                return;
            }

            decimal responseHours = sla.GetAttributeValue<decimal>(SlaResponseHours);
            decimal resolutionHours = sla.GetAttributeValue<decimal>(SlaResolutionHours);
            EntityReference calRef = sla.GetAttributeValue<EntityReference>(SlaCalendar);

            // --- Load calendar (or default calendar); null => naive 24x7 fallback ---
            CalendarModel cal = LoadCalendar(localContext, svc, calRef);

            // --- Compute target dates (calendar-aware, DST-aware) ---
            DateTime responseTargetUtc = ComputeTarget(localContext, svc, createdOnUtc, responseHours, cal);
            DateTime resolutionTargetUtc = ComputeTarget(localContext, svc, createdOnUtc, resolutionHours, cal);

            // --- Write back (Pre-Op: modify Target in place, no extra Update, no recursion) ---
            target[CaseAppliedSla] = new EntityReference(SlaEntity, sla.Id);
            if (responseHours > 0) target[CaseResponseTarget] = responseTargetUtc;
            if (resolutionHours > 0) target[CaseResolutionTarget] = resolutionTargetUtc;
            target[CaseSlaStatus] = new OptionSetValue(SlaStatusOnTrack);

            localContext.Trace("SLA '{0}' applied. response={1:o} resolution={2:o}",
                sla.Id, responseTargetUtc, resolutionTargetUtc);
        }

        // ---------- tier ----------

        private Guid? ResolveTier(LocalPluginContext localContext, IOrganizationService svc, EntityReference customer)
        {
            if (customer == null) return null;
            // customer.LogicalName is "account" or "contact"; the tier field name is the same on both.
            Entity rec;
            try
            {
                rec = svc.Retrieve(customer.LogicalName, customer.Id, new ColumnSet(CustomerTierField));
            }
            catch (Exception ex)
            {
                localContext.Trace("Could not read tier from {0}: {1}", customer.LogicalName, ex.Message);
                return null;
            }
            var tier = rec.GetAttributeValue<EntityReference>(CustomerTierField);
            return tier?.Id;
        }

        /// <summary>
        /// Fallback tier when the customer has none: reads the singleton tavu_systemsettings.tavu_defaultcustomertier.
        /// Uses SystemService (config read). Returns null if there is no settings row or the field is unset.
        /// </summary>
        private Guid? ResolveDefaultTier(LocalPluginContext localContext, IOrganizationService svc)
        {
            var q = new QueryExpression(SettingsEntity)
            {
                ColumnSet = new ColumnSet(SettingsDefaultTier),
                NoLock = true,
                TopCount = 1
            };
            var res = svc.RetrieveMultiple(q);
            if (res.Entities.Count == 0)
            {
                localContext.Trace("No tavu_systemsettings row found; cannot resolve a default tier.");
                return null;
            }
            var tier = res.Entities[0].GetAttributeValue<EntityReference>(SettingsDefaultTier);
            if (tier == null) localContext.Trace("tavu_defaultcustomertier is not set in system settings.");
            return tier?.Id;
        }

        // ---------- pause / resume (tavu_slaonhold) ----------

        /// <summary>
        /// Pre-op guardrail: block moving the case into a pausing status if the agent hasn't responded to
        /// the customer yet (no Outbound interaction). Pausing without a reply is illegitimate.
        /// </summary>
        private void GuardStatusChange(LocalPluginContext localContext, Entity target)
        {
            var statusRef = target.GetAttributeValue<EntityReference>(CaseStatus);
            if (statusRef == null) return;

            IOrganizationService svc = localContext.SystemService;
            if (StatusPauses(svc, statusRef) && !HasOutbound(svc, target.Id))
            {
                localContext.Trace("Pause blocked: pausing status but no Outbound interaction on the case.");
                throw new InvalidPluginExecutionException(
                    "No puedes poner el caso en espera del cliente sin haberle respondido primero.");
            }
        }

        private bool HasOutbound(IOrganizationService svc, Guid caseId)
        {
            var q = new QueryExpression(InteractionEntity)
            {
                ColumnSet = new ColumnSet(false),
                NoLock = true,
                TopCount = 1
            };
            q.Criteria.AddCondition(IxCase, ConditionOperator.Equal, caseId);
            q.Criteria.AddCondition(IxDirection, ConditionOperator.Equal, DirOutbound);
            return svc.RetrieveMultiple(q).Entities.Count > 0;
        }

        /// <summary>True if the given status's tavu_pausessla flag is set.</summary>
        private bool StatusPauses(IOrganizationService svc, EntityReference statusRef)
        {
            var s = svc.Retrieve(StatusEntity, statusRef.Id, new ColumnSet(StatusPausesSla));
            return s.GetAttributeValue<bool>(StatusPausesSla);
        }

        /// <summary>
        /// Post-op: react to a status change.
        ///  - Resolved/Cancelled category  -> finalize the SLA (Met/Breached), stop timers, deactivate.
        ///  - Active category               -> pause or resume based on tavu_pausessla vs current pause state.
        /// </summary>
        private void HandleStatusChange(LocalPluginContext localContext, Entity target)
        {
            var statusRef = target.GetAttributeValue<EntityReference>(CaseStatus);
            if (statusRef == null) return;

            IOrganizationService svc = localContext.SystemService;
            var status = svc.Retrieve(StatusEntity, statusRef.Id, new ColumnSet(StatusPausesSla, StatusStateCategory));
            var category = status.GetAttributeValue<OptionSetValue>(StatusStateCategory);
            int categoryValue = category?.Value ?? StateCategoryActive;

            if (categoryValue == StateCategoryResolved || categoryValue == StateCategoryCancelled)
            {
                Resolve(localContext, target, categoryValue == StateCategoryResolved);
                return;
            }

            bool pauses = status.GetAttributeValue<bool>(StatusPausesSla);
            var c = svc.Retrieve(CaseEntity, target.Id, new ColumnSet(CasePausedOn));
            bool isPaused = c.Contains(CasePausedOn) && c[CasePausedOn] != null;

            if (pauses && !isPaused) Pause(localContext, target);
            else if (!pauses && isPaused) Resume(localContext, target);
            else localContext.Trace("No SLA pause transition needed (pauses={0}, isPaused={1}).", pauses, isPaused);
        }

        /// <summary>
        /// Finalize the SLA when a case moves to a Resolved/Cancelled status: stop the gateway timers,
        /// deactivate the record (statecode = Inactive), and — for Resolved — stamp the resolution date and
        /// judge Met vs Breached against the resolution target.
        /// </summary>
        private void Resolve(LocalPluginContext localContext, Entity target, bool isResolved)
        {
            IOrganizationService svc = localContext.SystemService;
            Guid caseId = target.Id;

            Entity c = svc.Retrieve(CaseEntity, caseId,
                new ColumnSet(CaseOrchestrationId, CaseResolutionTarget, CasePausedOn));

            // Stop any scheduled SLA timers (best-effort).
            string instance = c.GetAttributeValue<string>(CaseOrchestrationId);
            string baseUrl = ReadEnvironmentVariable(svc, GatewayUrlVar);
            string tenantKey = ReadEnvironmentVariable(svc, GatewayKeyVar);
            if (!string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(tenantKey) && !string.IsNullOrEmpty(instance))
                TryCancel(localContext, baseUrl.TrimEnd('/'), tenantKey, instance);

            var upd = new Entity(CaseEntity, caseId);

            if (isResolved)
            {
                DateTime nowUtc = DateTime.UtcNow;
                upd[CaseResolutionDate] = nowUtc;

                int slaStatus = SlaStatusMet;
                if (c.Contains(CaseResolutionTarget))
                {
                    DateTime tr = DateTime.SpecifyKind(c.GetAttributeValue<DateTime>(CaseResolutionTarget), DateTimeKind.Utc);
                    slaStatus = nowUtc <= tr ? SlaStatusMet : SlaStatusBreached;
                }
                upd[CaseSlaStatus] = new OptionSetValue(slaStatus);
                localContext.Trace("Case {0} resolved. SLA {1}.", caseId, slaStatus == SlaStatusMet ? "Met" : "Breached");
            }
            else
            {
                localContext.Trace("Case {0} cancelled; SLA closed without Met/Breached judgment.", caseId);
            }

            // Clear any pause marker and deactivate the case. Set only statecode = Inactive (1); Dataverse
            // assigns the state's default statuscode. (If the org requires an explicit statuscode, add the
            // Inactive reason value here.)
            if (c.Contains(CasePausedOn) && c[CasePausedOn] != null) upd[CasePausedOn] = null;
            upd["statecode"] = new OptionSetValue(1);
            svc.Update(upd);
        }

        /// <summary>Pause: cancel the gateway timers, stamp the pause start, set SLA Status = Paused.</summary>
        private void Pause(LocalPluginContext localContext, Entity target)
        {
            IOrganizationService svc = localContext.SystemService;
            Guid caseId = target.Id;

            Entity c = svc.Retrieve(CaseEntity, caseId, new ColumnSet(CaseOrchestrationId));
            string instance = c.GetAttributeValue<string>(CaseOrchestrationId);

            string baseUrl = ReadEnvironmentVariable(svc, GatewayUrlVar);
            string tenantKey = ReadEnvironmentVariable(svc, GatewayKeyVar);
            if (!string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(tenantKey) && !string.IsNullOrEmpty(instance))
                TryCancel(localContext, baseUrl.TrimEnd('/'), tenantKey, instance);

            var upd = new Entity(CaseEntity, caseId);
            upd[CasePausedOn] = DateTime.UtcNow;
            upd[CaseSlaStatus] = new OptionSetValue(SlaStatusPaused);
            svc.Update(upd);
            localContext.Trace("SLA paused for case {0}.", caseId);
        }

        /// <summary>
        /// Resume: recompute the remaining BUSINESS time (frozen at pause) and re-anchor the targets to
        /// "now", then reschedule the gateway timers. Uses the calendar of the applied SLA.
        /// </summary>
        private void Resume(LocalPluginContext localContext, Entity target)
        {
            IOrganizationService svc = localContext.SystemService;
            Guid caseId = target.Id;

            Entity c = svc.Retrieve(CaseEntity, caseId, new ColumnSet(
                CasePausedOn, CaseResponseTarget, CaseResolutionTarget, CaseAppliedSla, CaseFirstResponse));

            if (!c.Contains(CasePausedOn) || c[CasePausedOn] == null)
            {
                localContext.Trace("Resume with no prior pause (tavu_slapausedon empty); nothing to do.");
                return;
            }

            DateTime pausedOnUtc = DateTime.SpecifyKind(c.GetAttributeValue<DateTime>(CasePausedOn), DateTimeKind.Utc);
            DateTime nowUtc = DateTime.UtcNow;
            CalendarModel cal = GetCaseCalendar(localContext, svc, c);

            var upd = new Entity(CaseEntity, caseId);

            // Resolution target (always re-anchored while the case is active).
            if (c.Contains(CaseResolutionTarget))
            {
                DateTime tr = DateTime.SpecifyKind(c.GetAttributeValue<DateTime>(CaseResolutionTarget), DateTimeKind.Utc);
                if (tr > pausedOnUtc)
                {
                    double remainingMin = BusinessMinutesBetween(svc, pausedOnUtc, tr, cal);
                    upd[CaseResolutionTarget] = ComputeTarget(localContext, svc, nowUtc, (decimal)(remainingMin / 60.0), cal);
                }
            }

            // Response target only if first response hasn't happened yet.
            bool responded = c.Contains(CaseFirstResponse) && c[CaseFirstResponse] != null;
            if (!responded && c.Contains(CaseResponseTarget))
            {
                DateTime tr = DateTime.SpecifyKind(c.GetAttributeValue<DateTime>(CaseResponseTarget), DateTimeKind.Utc);
                if (tr > pausedOnUtc)
                {
                    double remainingMin = BusinessMinutesBetween(svc, pausedOnUtc, tr, cal);
                    upd[CaseResponseTarget] = ComputeTarget(localContext, svc, nowUtc, (decimal)(remainingMin / 60.0), cal);
                }
            }

            upd[CasePausedOn] = null; // clear the pause
            upd[CaseSlaStatus] = new OptionSetValue(SlaStatusOnTrack);
            svc.Update(upd);

            // Reprogram the durable timers against the new targets.
            ScheduleSla(localContext, new Entity(CaseEntity, caseId));
            localContext.Trace("SLA resumed for case {0}.", caseId);
        }

        /// <summary>Loads the calendar of the case's applied SLA (or the default calendar).</summary>
        private CalendarModel GetCaseCalendar(LocalPluginContext localContext, IOrganizationService svc, Entity caseEntity)
        {
            EntityReference calRef = null;
            var slaRef = caseEntity.GetAttributeValue<EntityReference>(CaseAppliedSla);
            if (slaRef != null)
            {
                var sla = svc.Retrieve(SlaEntity, slaRef.Id, new ColumnSet(SlaCalendar));
                calRef = sla.GetAttributeValue<EntityReference>(SlaCalendar);
            }
            return LoadCalendar(localContext, svc, calRef);
        }

        /// <summary>Business minutes between two UTC instants, honoring the calendar (or wall-clock if none).</summary>
        private double BusinessMinutesBetween(IOrganizationService svc, DateTime fromUtc, DateTime toUtc, CalendarModel cal)
        {
            if (toUtc <= fromUtc) return 0;
            if (cal == null) return (toUtc - fromUtc).TotalMinutes;

            DateTime fromLocal = cal.HasTimeZone ? ToLocal(svc, cal.TimeZoneCode, fromUtc) : fromUtc;
            DateTime toLocal = cal.HasTimeZone ? ToLocal(svc, cal.TimeZoneCode, toUtc) : toUtc;
            return CountWorkingMinutes(fromLocal, toLocal, cal);
        }

        /// <summary>Counts working minutes in [start, end] local time across the calendar's intervals/closures.</summary>
        private double CountWorkingMinutes(DateTime start, DateTime end, CalendarModel cal)
        {
            double total = 0;
            DateTime cursor = start;
            for (int guard = 0; guard < 366 && cursor < end; guard++)
            {
                DateTime day = cursor.Date;
                if (!cal.Closures.Contains(day))
                {
                    foreach (var iv in cal.IntervalsFor(day))
                    {
                        DateTime ivStart = day.AddMinutes(iv.Start);
                        DateTime ivEnd = day.AddMinutes(iv.End);
                        DateTime s = cursor > ivStart ? cursor : ivStart;
                        DateTime e = end < ivEnd ? end : ivEnd;
                        if (e > s) total += (e - s).TotalMinutes;
                    }
                }
                cursor = day.AddDays(1);
            }
            return total;
        }

        // ---------- SLA matching ----------

        private Entity ResolveSla(LocalPluginContext localContext, IOrganizationService svc, Guid tierId, Guid? typeId)
        {
            var q = new QueryExpression(SlaEntity)
            {
                ColumnSet = new ColumnSet(SlaResponseHours, SlaResolutionHours, SlaCalendar, SlaType, SlaEvalPriority),
                NoLock = true,
                TopCount = 5
            };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            q.Criteria.AddCondition(SlaTier, ConditionOperator.Equal, tierId);

            // Type-specific OR tier-default (Type empty). Specific rows have lower Eval Priority, so they win.
            var typeFilter = new FilterExpression(LogicalOperator.Or);
            if (typeId.HasValue) typeFilter.AddCondition(SlaType, ConditionOperator.Equal, typeId.Value);
            typeFilter.AddCondition(SlaType, ConditionOperator.Null);
            q.Criteria.AddFilter(typeFilter);

            q.AddOrder(SlaEvalPriority, OrderType.Ascending);

            var result = svc.RetrieveMultiple(q);
            localContext.Trace("SLA candidates: {0}", result.Entities.Count);
            return result.Entities.Count > 0 ? result.Entities[0] : null;
        }

        // ---------- calendar loading ----------

        private CalendarModel LoadCalendar(LocalPluginContext localContext, IOrganizationService svc, EntityReference calRef)
        {
            Entity calRec = null;

            if (calRef != null)
            {
                calRec = svc.Retrieve(CalEntity, calRef.Id, new ColumnSet(CalTimeZone, CalIs247));
            }
            else
            {
                // No calendar on the SLA -> use the org's default calendar.
                var q = new QueryExpression(CalEntity)
                {
                    ColumnSet = new ColumnSet(CalTimeZone, CalIs247),
                    NoLock = true,
                    TopCount = 1
                };
                q.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
                q.Criteria.AddCondition(CalIsDefault, ConditionOperator.Equal, true);
                var def = svc.RetrieveMultiple(q);
                if (def.Entities.Count > 0) calRec = def.Entities[0];
            }

            if (calRec == null)
            {
                localContext.Trace("No calendar (and no default). Falling back to naive elapsed-hours math.");
                return null;
            }

            var model = new CalendarModel
            {
                CalendarId = calRec.Id,
                Is247 = calRec.GetAttributeValue<bool>(CalIs247),
                IntervalsByDay = new Dictionary<int, List<Interval>>(),
                Closures = new HashSet<DateTime>()
            };

            var tz = calRec.GetAttributeValue<int?>(CalTimeZone);
            model.HasTimeZone = tz.HasValue;
            model.TimeZoneCode = tz ?? 0;

            if (!model.Is247)
            {
                var qh = new QueryExpression(WhEntity)
                {
                    ColumnSet = new ColumnSet(WhDayOfWeek, WhStartMinutes, WhEndMinutes),
                    NoLock = true
                };
                qh.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
                qh.Criteria.AddCondition(WhCalendar, ConditionOperator.Equal, model.CalendarId);
                foreach (var wh in svc.RetrieveMultiple(qh).Entities)
                {
                    var dow = wh.GetAttributeValue<OptionSetValue>(WhDayOfWeek);
                    var start = wh.GetAttributeValue<OptionSetValue>(WhStartMinutes);
                    var end = wh.GetAttributeValue<OptionSetValue>(WhEndMinutes);
                    if (dow == null || start == null || end == null) continue;
                    if (end.Value <= start.Value) continue;

                    List<Interval> list;
                    if (!model.IntervalsByDay.TryGetValue(dow.Value, out list))
                    {
                        list = new List<Interval>();
                        model.IntervalsByDay[dow.Value] = list;
                    }
                    list.Add(new Interval { Start = start.Value, End = end.Value });
                }
                foreach (var kv in model.IntervalsByDay)
                    kv.Value.Sort((a, b) => a.Start.CompareTo(b.Start));
            }

            // Closures for this calendar OR global (null calendar).
            var qc = new QueryExpression(CloEntity)
            {
                ColumnSet = new ColumnSet(CloDate, CloCalendar),
                NoLock = true
            };
            qc.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            var cloFilter = new FilterExpression(LogicalOperator.Or);
            cloFilter.AddCondition(CloCalendar, ConditionOperator.Equal, model.CalendarId);
            cloFilter.AddCondition(CloCalendar, ConditionOperator.Null);
            qc.Criteria.AddFilter(cloFilter);
            foreach (var clo in svc.RetrieveMultiple(qc).Entities)
            {
                var d = clo.GetAttributeValue<DateTime>(CloDate);
                if (d != default(DateTime)) model.Closures.Add(d.Date);
            }

            localContext.Trace("Calendar {0}: is247={1} tz={2} days={3} closures={4}",
                model.CalendarId, model.Is247, model.HasTimeZone ? model.TimeZoneCode.ToString() : "n/a",
                model.IntervalsByDay.Count, model.Closures.Count);
            return model;
        }

        // ---------- target-date computation ----------

        private DateTime ComputeTarget(LocalPluginContext localContext, IOrganizationService svc,
                                       DateTime startUtc, decimal hours, CalendarModel cal)
        {
            double minutes = (double)hours * 60.0;
            if (minutes <= 0) return startUtc;

            // No calendar -> naive continuous elapsed time in UTC.
            if (cal == null) return startUtc.AddHours((double)hours);

            DateTime startLocal = cal.HasTimeZone ? ToLocal(svc, cal.TimeZoneCode, startUtc) : startUtc;
            DateTime targetLocal = Walk(startLocal, minutes, cal);
            DateTime targetUtc = cal.HasTimeZone
                ? ToUtc(svc, cal.TimeZoneCode, targetLocal)
                : DateTime.SpecifyKind(targetLocal, DateTimeKind.Utc);
            return targetUtc;
        }

        /// <summary>Walk forward from localStart, consuming minutes only inside working intervals, skipping closures.</summary>
        private DateTime Walk(DateTime localStart, double minutesRemaining, CalendarModel cal)
        {
            DateTime cursor = localStart;
            for (int dayGuard = 0; dayGuard < 366 && minutesRemaining > 0; dayGuard++)
            {
                DateTime day = cursor.Date;

                if (cal.Closures.Contains(day))
                {
                    cursor = day.AddDays(1);
                    continue;
                }

                foreach (var iv in cal.IntervalsFor(day))
                {
                    DateTime ivStart = day.AddMinutes(iv.Start);
                    DateTime ivEnd = day.AddMinutes(iv.End);
                    DateTime effStart = cursor > ivStart ? cursor : ivStart;
                    if (effStart >= ivEnd) continue;

                    double avail = (ivEnd - effStart).TotalMinutes;
                    if (minutesRemaining <= avail)
                        return effStart.AddMinutes(minutesRemaining);

                    minutesRemaining -= avail;
                    cursor = ivEnd;
                }

                cursor = day.AddDays(1); // next day at 00:00
            }
            return cursor; // ran past the guard; degrade gracefully
        }

        private static DateTime ToLocal(IOrganizationService svc, int tzCode, DateTime utc)
        {
            var req = new LocalTimeFromUtcTimeRequest
            {
                TimeZoneCode = tzCode,
                UtcTime = DateTime.SpecifyKind(utc, DateTimeKind.Utc)
            };
            return ((LocalTimeFromUtcTimeResponse)svc.Execute(req)).LocalTime;
        }

        private static DateTime ToUtc(IOrganizationService svc, int tzCode, DateTime local)
        {
            var req = new UtcTimeFromLocalTimeRequest
            {
                TimeZoneCode = tzCode,
                LocalTime = local
            };
            return ((UtcTimeFromLocalTimeResponse)svc.Execute(req)).UtcTime;
        }

        // ---------- SLA scheduling via gateway (Increment 2) ----------

        private void ScheduleSla(LocalPluginContext localContext, Entity target)
        {
            IOrganizationService svc = localContext.SystemService;
            Guid caseId = target.Id;

            Entity c = svc.Retrieve(CaseEntity, caseId,
                new ColumnSet(CreatedOn, CaseResolutionTarget, CaseOrchestrationId, "statecode"));

            var state = c.GetAttributeValue<OptionSetValue>("statecode");
            if (state != null && state.Value != StateActive)
            {
                localContext.Trace("Case not Active; skipping SLA scheduling.");
                return;
            }

            if (!c.Contains(CaseResolutionTarget))
            {
                localContext.Trace("No resolution target date; nothing to schedule.");
                return;
            }

            DateTime resolutionUtc = DateTime.SpecifyKind(c.GetAttributeValue<DateTime>(CaseResolutionTarget), DateTimeKind.Utc);
            DateTime createdUtc = DateTime.SpecifyKind(c.GetAttributeValue<DateTime>(CreatedOn), DateTimeKind.Utc);

            string baseUrl = ReadEnvironmentVariable(svc, GatewayUrlVar);
            string tenantKey = ReadEnvironmentVariable(svc, GatewayKeyVar);
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(tenantKey))
            {
                localContext.Trace("Gateway env vars not set; SLA dates saved but timers not scheduled.");
                return;
            }
            baseUrl = baseUrl.TrimEnd('/');

            // Reschedule: cancel any previous orchestration first (best-effort).
            string existing = c.GetAttributeValue<string>(CaseOrchestrationId);
            if (!string.IsNullOrEmpty(existing))
                TryCancel(localContext, baseUrl, tenantKey, existing);

            // Build transitions, skipping any already in the past.
            DateTime nowUtc = DateTime.UtcNow;
            var transitions = new List<TransitionDto>();

            double totalMinutes = (resolutionUtc - createdUtc).TotalMinutes;
            if (totalMinutes > 0)
            {
                DateTime warningUtc = createdUtc.AddMinutes(totalMinutes * WarningFraction);
                if (warningUtc > nowUtc)
                    transitions.Add(new TransitionDto { AtUtc = Iso(warningUtc), StatusValue = SlaStatusWarning });
            }
            if (resolutionUtc > nowUtc)
                transitions.Add(new TransitionDto { AtUtc = Iso(resolutionUtc), StatusValue = SlaStatusBreached });

            if (transitions.Count == 0)
            {
                localContext.Trace("All SLA transition times are in the past; nothing to schedule.");
                return;
            }

            string instanceId = CallSchedule(localContext, baseUrl, tenantKey, caseId, transitions);
            if (string.IsNullOrEmpty(instanceId))
            {
                localContext.Trace("Gateway did not return an instanceId; not stored.");
                return;
            }

            var upd = new Entity(CaseEntity, caseId);
            upd[CaseOrchestrationId] = instanceId;
            svc.Update(upd);
            localContext.Trace("SLA scheduled. transitions={0} instanceId={1}", transitions.Count, instanceId);
        }

        private string CallSchedule(LocalPluginContext localContext, string baseUrl, string tenantKey,
                                    Guid caseId, List<TransitionDto> transitions)
        {
            try
            {
                var body = new ScheduleRequestDto { CaseId = caseId.ToString(), Transitions = transitions };
                string resp = PostJson(baseUrl + "/api/sla/schedule", tenantKey, Serialize(body));
                var parsed = Deserialize<ScheduleResponseDto>(resp);
                return parsed != null ? parsed.InstanceId : null;
            }
            catch (WebException wex)
            {
                localContext.Trace("SLA schedule HTTP error: {0}", ReadError(wex));
                return null;
            }
            catch (Exception ex)
            {
                localContext.Trace("SLA schedule failed: {0}", ex.Message);
                return null;
            }
        }

        private void TryCancel(LocalPluginContext localContext, string baseUrl, string tenantKey, string instanceId)
        {
            try
            {
                var body = new CancelRequestDto { InstanceId = instanceId };
                PostJson(baseUrl + "/api/sla/cancel", tenantKey, Serialize(body));
                localContext.Trace("Cancelled previous SLA orchestration {0}", instanceId);
            }
            catch (Exception ex)
            {
                localContext.Trace("SLA cancel (best-effort) failed for {0}: {1}", instanceId, ex.Message);
            }
        }

        private static string PostJson(string url, string tenantKey, string jsonBody)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            byte[] payload = Encoding.UTF8.GetBytes(jsonBody);
            var http = (HttpWebRequest)WebRequest.Create(url);
            http.Method = "POST";
            http.ContentType = "application/json";
            http.Accept = "application/json";
            http.Headers["X-OpenTavu-Tenant-Key"] = tenantKey;
            http.Timeout = GatewayTimeoutSeconds * 1000;
            http.ContentLength = payload.Length;

            using (Stream rs = http.GetRequestStream())
            {
                rs.Write(payload, 0, payload.Length);
            }

            using (var response = (HttpWebResponse)http.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static string ReadError(WebException wex)
        {
            try
            {
                if (wex.Response != null)
                {
                    using (Stream s = wex.Response.GetResponseStream())
                    using (var r = new StreamReader(s, Encoding.UTF8))
                    {
                        return r.ReadToEnd();
                    }
                }
            }
            catch { /* ignore */ }
            return wex.Message;
        }

        private static string Iso(DateTime utc)
        {
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc)
                .ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        private static string ReadEnvironmentVariable(IOrganizationService svc, string schemaName)
        {
            if (string.IsNullOrEmpty(schemaName)) return null;

            var defQ = new QueryExpression("environmentvariabledefinition")
            {
                ColumnSet = new ColumnSet("environmentvariabledefinitionid", "defaultvalue"),
                TopCount = 1,
                NoLock = true
            };
            defQ.Criteria.AddCondition("schemaname", ConditionOperator.Equal, schemaName);
            var defs = svc.RetrieveMultiple(defQ);
            if (defs.Entities.Count == 0) return null;

            var def = defs.Entities[0];
            string defaultValue = def.GetAttributeValue<string>("defaultvalue");

            var valQ = new QueryExpression("environmentvariablevalue")
            {
                ColumnSet = new ColumnSet("value"),
                TopCount = 1,
                NoLock = true
            };
            valQ.Criteria.AddCondition("environmentvariabledefinitionid", ConditionOperator.Equal, def.Id);
            var vals = svc.RetrieveMultiple(valQ);

            string current = vals.Entities.Count > 0 ? vals.Entities[0].GetAttributeValue<string>("value") : null;
            return !string.IsNullOrEmpty(current) ? current : defaultValue;
        }

        private static string Serialize<T>(T obj)
        {
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(ms, obj);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static T Deserialize<T>(string json)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(ms);
            }
        }

        [DataContract]
        private class TransitionDto
        {
            [DataMember(Name = "atUtc", Order = 0)] public string AtUtc { get; set; }
            [DataMember(Name = "statusValue", Order = 1)] public int StatusValue { get; set; }
        }

        [DataContract]
        private class ScheduleRequestDto
        {
            [DataMember(Name = "caseId", Order = 0)] public string CaseId { get; set; }
            [DataMember(Name = "transitions", Order = 1)] public List<TransitionDto> Transitions { get; set; }
        }

        [DataContract]
        private class ScheduleResponseDto
        {
            [DataMember(Name = "instanceId")] public string InstanceId { get; set; }
        }

        [DataContract]
        private class CancelRequestDto
        {
            [DataMember(Name = "instanceId", Order = 0)] public string InstanceId { get; set; }
        }

        // ---------- in-memory calendar model ----------

        private sealed class Interval { public int Start; public int End; }

        private sealed class CalendarModel
        {
            public Guid CalendarId;
            public bool Is247;
            public bool HasTimeZone;
            public int TimeZoneCode;
            public Dictionary<int, List<Interval>> IntervalsByDay;
            public HashSet<DateTime> Closures;

            private static readonly List<Interval> FullDay = new List<Interval> { new Interval { Start = 0, End = MinutesPerDay } };
            private static readonly List<Interval> Empty = new List<Interval>();

            /// <summary>Working intervals for a given local day. 24x7 => whole day; else the configured rows.</summary>
            public List<Interval> IntervalsFor(DateTime day)
            {
                if (Is247) return FullDay;
                int dow = ((int)day.DayOfWeek + 6) % 7 + 1; // .NET Sun=0..Sat=6  ->  Mon=1..Sun=7
                List<Interval> list;
                return IntervalsByDay.TryGetValue(dow, out list) ? list : Empty;
            }
        }
    }
}
