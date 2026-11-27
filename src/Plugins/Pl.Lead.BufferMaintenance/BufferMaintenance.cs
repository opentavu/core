using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;

namespace Pl.Lead.BufferMaintenance
{
    /// <summary>
    /// Custom API <c>tavu_RunLeadBufferMaintenance</c>: the daily lead buffer rules that Fl.Lead.BufferDailyMaintenance
    /// applied (Decision 60: no capability depends on Power Automate). The gateway calls it once a day; the System
    /// Settings button "Run daily jobs now" calls it too.
    ///
    /// Rules (the flow's, unchanged): days in buffer = whole days since the lead was created (UTC). Band: Stale when
    /// days > System Settings' tavu_leadbufferstaledays (14 when empty), else Aging when days > tavu_leadbufferagingdays
    /// (7 when empty), else Fresh. An Awaiting Human Review lead with days > stale days is closed as Stale; New,
    /// AI Processing and Manual Review Required leads are never closed (Gustavo, 2026-10-31).
    ///
    /// Unlike the flow, which rewrote every active lead every day, only leads whose band changes (or that close) are
    /// written, so a second run the same day writes nothing and no lead's Modified On moves without a reason;
    /// tavu_daysinbuffer is therefore the days at the last change (decision B, 2026-10-31). When the run completes it
    /// stamps System Settings' tavu_dailyjobslastrun, which Verify Setup reads.
    ///
    /// Outputs: Summary (string), LeadsUpdated, LeadsClosedStale (integers), Complete (boolean). The call stops after
    /// <see cref="Budget"/> and answers Complete = false; calling again finishes the rest.
    /// </summary>
    /// <remarks>
    /// Registration: Custom API tavu_RunLeadBufferMaintenance (unbound action, execute privilege
    /// prvWritetavu_SystemSettings), main operation, synchronous. Runs with the system service.
    /// </remarks>
    public class BufferMaintenance : PluginBase
    {
        // ----- Schema constants -----
        private const string Lead = "tavu_lead";
        private const string LeadId = "tavu_leadid";
        private const string CreatedOn = "createdon";
        private const string StateCode = "statecode";
        private const string StatusCode = "statuscode";
        private const string BufferAlert = "tavu_bufferalert";
        private const string DaysInBuffer = "tavu_daysinbuffer";

        private const string Settings = "tavu_systemsettings";
        private const string AgingDays = "tavu_leadbufferagingdays";
        private const string StaleDays = "tavu_leadbufferstaledays";
        private const string LastRun = "tavu_dailyjobslastrun";

        internal const int BandFresh = 576600000;
        internal const int BandAging = 576600001;
        internal const int BandStale = 576600002;
        internal const int StatusAwaitingReview = 576600003;
        internal const int StatusStale = 576600009;
        private const int StateActive = 0;
        private const int StateInactive = 1;
        private const int DefaultAgingDays = 7;
        private const int DefaultStaleDays = 14;

        private const string OutSummary = "Summary";
        private const string OutUpdated = "LeadsUpdated";
        private const string OutClosed = "LeadsClosedStale";
        private const string OutComplete = "Complete";

        private readonly Func<DateTime> _utcNow;

        /// <summary>How long one call works before it answers Complete = false (the platform limit is 2 minutes).</summary>
        internal TimeSpan Budget { get; set; } = TimeSpan.FromSeconds(90);

        /// <summary>Rows per page for every query.</summary>
        internal int PageSize { get; set; } = 5000;

        public BufferMaintenance() : this(() => DateTime.UtcNow) { }

        internal BufferMaintenance(Func<DateTime> utcNow) : base(typeof(BufferMaintenance))
        {
            _utcNow = utcNow;
        }

        protected override void ExecuteInternal(LocalPluginContext localContext)
        {
            if (localContext == null) throw new ArgumentNullException(nameof(localContext));
            IOrganizationService svc = localContext.SystemService;
            var clock = Stopwatch.StartNew();
            DateTime now = _utcNow();

            var sq = new QueryExpression(Settings) { ColumnSet = new ColumnSet(AgingDays, StaleDays), TopCount = 1, NoLock = true };
            Entity settings = svc.RetrieveMultiple(sq).Entities is DataCollection<Entity> s && s.Count > 0 ? s[0] : null;
            int aging = settings?.GetAttributeValue<int?>(AgingDays) ?? DefaultAgingDays;
            int stale = settings?.GetAttributeValue<int?>(StaleDays) ?? DefaultStaleDays;

            // days > n  <=>  created on or before now minus (n + 1) days
            DateTime staleCut = now.AddDays(-(stale + 1));
            DateTime agingCut = now.AddDays(-(aging + 1));

            int updated = 0, closed = 0;
            bool complete = true;

            // 1. Awaiting Human Review past the stale days: close as Stale (with the band and days, in one write).
            var close = LeadsQuery();
            close.Criteria.AddCondition(StatusCode, ConditionOperator.Equal, StatusAwaitingReview);
            close.Criteria.AddCondition(CreatedOn, ConditionOperator.LessEqual, staleCut);
            foreach (Entity l in ReadAll(svc, close))
            {
                if (clock.Elapsed > Budget) { complete = false; break; }
                var u = new Entity(Lead, l.Id);
                u[BufferAlert] = new OptionSetValue(BandStale);
                u[DaysInBuffer] = Days(l, now);
                u[StateCode] = new OptionSetValue(StateInactive);
                u[StatusCode] = new OptionSetValue(StatusStale);
                svc.Update(u);
                closed++;
            }

            // 2. Active leads whose band is not the one their age gives them.
            if (complete)
            {
                var bands = new[]
                {
                    new KeyValuePair<int, QueryExpression>(BandStale, BandQuery(BandStale, null, staleCut)),
                    new KeyValuePair<int, QueryExpression>(BandAging, BandQuery(BandAging, staleCut, agingCut)),
                    new KeyValuePair<int, QueryExpression>(BandFresh, BandQuery(BandFresh, Max(staleCut, agingCut), null)),
                };
                foreach (KeyValuePair<int, QueryExpression> band in bands)
                {
                    foreach (Entity l in ReadAll(svc, band.Value))
                    {
                        if (clock.Elapsed > Budget) { complete = false; break; }
                        int days = Days(l, now);
                        var u = new Entity(Lead, l.Id);
                        u[BufferAlert] = new OptionSetValue(Band(days, aging, stale));
                        u[DaysInBuffer] = days;
                        svc.Update(u);
                        updated++;
                    }
                    if (!complete) break;
                }
            }

            if (complete && settings != null)
                svc.Update(new Entity(Settings, settings.Id) { [LastRun] = now });

            string summary = string.Format(CultureInfo.InvariantCulture,
                "Lead buffer: {0} leads moved to a new band, {1} closed as Stale (aging after {2} days, stale after {3}).{4}",
                updated, closed, aging, stale, complete ? string.Empty : " Not finished yet: call again to continue.");
            localContext.Trace(summary);
            ParameterCollection outputs = localContext.PluginExecutionContext.OutputParameters;
            outputs[OutSummary] = summary;
            outputs[OutUpdated] = updated;
            outputs[OutClosed] = closed;
            outputs[OutComplete] = complete;
        }

        /// <summary>The flow's band for a lead <paramref name="days"/> days old.</summary>
        internal static int Band(int days, int aging, int stale) =>
            days > stale ? BandStale : days > aging ? BandAging : BandFresh;

        /// <summary>Whole days since the lead was created, as the flow counted them (UTC, rounded down).</summary>
        internal static int Days(Entity lead, DateTime now) =>
            (int)Math.Floor((now - lead.GetAttributeValue<DateTime>(CreatedOn).ToUniversalTime()).TotalDays);

        private static QueryExpression LeadsQuery()
        {
            var q = new QueryExpression(Lead) { ColumnSet = new ColumnSet(CreatedOn), NoLock = true };
            q.Criteria.AddCondition(StateCode, ConditionOperator.Equal, StateActive);
            q.AddOrder(LeadId, OrderType.Ascending);
            return q;
        }

        /// <summary>Active leads created in (after, onOrBefore] whose band is not <paramref name="band"/> (or empty).</summary>
        private static QueryExpression BandQuery(int band, DateTime? after, DateTime? onOrBefore)
        {
            QueryExpression q = LeadsQuery();
            if (after.HasValue) q.Criteria.AddCondition(CreatedOn, ConditionOperator.GreaterThan, after.Value);
            if (onOrBefore.HasValue) q.Criteria.AddCondition(CreatedOn, ConditionOperator.LessEqual, onOrBefore.Value);
            var differs = new FilterExpression(LogicalOperator.Or);
            differs.AddCondition(BufferAlert, ConditionOperator.NotEqual, band);
            differs.AddCondition(BufferAlert, ConditionOperator.Null);
            q.Criteria.AddFilter(differs);
            return q;
        }

        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

        /// <summary>
        /// Every row of the query, page by page with the paging cookie, read before anything is written (writing moves
        /// rows out of the result, which would make a later page skip some).
        /// </summary>
        private List<Entity> ReadAll(IOrganizationService svc, QueryExpression q)
        {
            q.PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 };
            var all = new List<Entity>();
            while (true)
            {
                EntityCollection page = svc.RetrieveMultiple(q);
                all.AddRange(page.Entities);
                if (!page.MoreRecords) return all;
                q.PageInfo.PageNumber++;
                q.PageInfo.PagingCookie = page.PagingCookie;
            }
        }
    }
}
