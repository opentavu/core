using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;
using Pl.Setup.Initialize;

namespace Pl.Forecast.Snapshot
{
    /// <summary>
    /// Custom API <c>tavu_RunForecastSnapshot</c>: the daily forecast snapshot that Fl.Forecast.SnapshotDaily wrote
    /// (Decision 60: no capability depends on Power Automate). The gateway's daily timer calls it as its application
    /// user; the System Settings button "Run daily jobs now" calls it too.
    ///
    ///   0. Sales periods are kept ahead first, with the generator of tavu_InitializeConfiguration Mode = periods (the
    ///      fiscal year of today and the next one, in the default business calendar's time zone), so a new quarter
    ///      always has its period before its snapshots.
    ///   1. Every active User-scope sales target with a sales period: a tavu_forecastsnapshot for today, keyed
    ///      "{target id}|{yyyy-MM-dd}" (UTC), and a tavu_reportingcache row keyed "{scope}|{sales rep}|{period}|{business line}".
    ///   2. Every active business-line and company target: the same two rows, with won, committed, best case and open
    ///      pipeline summed over the active User-scope targets of its period (and business line), and gap, forecast,
    ///      attainment, forecast attainment and coverage computed from them. Cache key "{scope}||{period}|{business line}".
    ///
    /// Keys, names, columns and formulas are the flow's, so existing rows are updated, never duplicated. Differences
    /// from the flow, by design: a row is written only when a value changed (a second run the same day writes
    /// nothing), period elapsed is measured at 00:00 UTC of the run's day (so it is the same all day), and a target
    /// with no sales period is skipped and counted instead of failing the run.
    ///
    /// Outputs: Summary (string), TargetsProcessed, SnapshotsWritten, CachesWritten (integers), Complete (boolean).
    /// The call stops after <see cref="Budget"/> and answers Complete = false; calling again finishes the rest.
    /// </summary>
    /// <remarks>
    /// Registration: Custom API tavu_RunForecastSnapshot (unbound action, execute privilege prvWritetavu_ForecastSnapshot),
    /// main operation, synchronous. Targets are read and snapshots and cache rows written with the caller's privileges
    /// (the user service), so the caller needs read on sales targets and periods and create and write on forecast
    /// snapshots and reporting caches; only the missing sales periods are created with the system service (Decision 60
    /// amendment of 2026-11-27: the gateway's role never gets write on System Settings).
    /// </remarks>
    public class Snapshot : PluginBase
    {
        // ----- Schema constants -----
        private const string Target = "tavu_salestarget";
        private const string TargetId = "tavu_salestargetid";
        private const string TargetName = "tavu_name";
        private const string TargetScope = "tavu_scope";
        private const string TargetPeriod = "tavu_salesperiod";
        private const string TargetBusinessLine = "tavu_businessline";
        private const string TargetSalesRep = "tavu_salesrep";
        private const string TargetAmount = "tavu_targetamount";
        private const string TargetWon = "tavu_closedwonamount";
        private const string TargetCommitted = "tavu_committedamount";
        private const string TargetBestCase = "tavu_bestcaseamount";
        private const string TargetOpen = "tavu_openpipelineamount";
        private const string TargetForecast = "tavu_forecastamount";             // decimal on the target
        private const string TargetForecastFinal = "tavu_forecastfinal";        // decimal on the target
        private const string TargetAttainment = "tavu_attainmentpct";
        private const string TargetForecastAttainment = "tavu_forecastattainmentpct";
        private const string TargetGap = "tavu_gaptogoal";                       // decimal on the target
        private const string TargetCoverage = "tavu_coverageratio";

        private const string Period = "tavu_salesperiod";
        private const string PeriodStart = "tavu_startdate";
        private const string PeriodEnd = "tavu_enddate";

        private const string Snap = "tavu_forecastsnapshot";
        private const string SnapKey = "tavu_snapshotkey";
        private const string SnapDate = "tavu_snapshotdate";
        private const string SnapElapsed = "tavu_periodelapsed";
        private const string SnapPace = "tavu_expectedpaceamount";

        private const string Cache = "tavu_reportingcache";
        private const string CacheKey = "tavu_cachekey";
        private const string CacheComputedOn = "tavu_computedon";
        private const string CacheForecastFinal = "tavu_forecastfinal";
        private const string CacheForecastAttainment = "tavu_forecastattainment";
        private const string CacheSalesRep = "tavu_salesrep";

        // Columns written under the same name on the target, the snapshot and the cache.
        private const string Name = "tavu_name";
        private const string Scope = "tavu_scope";
        private const string SalesTarget = "tavu_salestarget";
        private const string SalesPeriod = "tavu_salesperiod";
        private const string BusinessLine = "tavu_businessline";
        private const string Amount = "tavu_targetamount";
        private const string Won = "tavu_closedwonamount";
        private const string Committed = "tavu_committedamount";
        private const string BestCase = "tavu_bestcaseamount";
        private const string Open = "tavu_openpipelineamount";
        private const string Forecast = "tavu_forecastamount";
        private const string Gap = "tavu_gaptogoal";
        private const string Attainment = "tavu_attainment";
        private const string Coverage = "tavu_coverageratio";

        internal const int ScopeUser = 576600000;
        internal const int ScopeBusinessLine = 576600002;
        internal const int ScopeCompany = 576600003;
        private const int StateActive = 0;

        private const string OutSummary = "Summary";
        private const string OutTargets = "TargetsProcessed";
        private const string OutSnapshots = "SnapshotsWritten";
        private const string OutCaches = "CachesWritten";
        private const string OutComplete = "Complete";

        private readonly Func<DateTime> _utcNow;

        /// <summary>How long one call works before it answers Complete = false (the platform limit is 2 minutes).</summary>
        internal TimeSpan Budget { get; set; } = TimeSpan.FromSeconds(90);

        /// <summary>Rows per page for every query.</summary>
        internal int PageSize { get; set; } = 5000;

        public Snapshot() : this(() => DateTime.UtcNow) { }

        internal Snapshot(Func<DateTime> utcNow) : base(typeof(Snapshot))
        {
            _utcNow = utcNow;
        }

        protected override void ExecuteInternal(LocalPluginContext localContext)
        {
            if (localContext == null) throw new ArgumentNullException(nameof(localContext));
            IOrganizationService svc = localContext.UserService;
            var clock = Stopwatch.StartNew();
            DateTime now = _utcNow();
            DateTime day = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);
            string dayText = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            int periodsCreated = KeepPeriodsAhead(localContext);

            var periods = new Dictionary<Guid, Entity>();
            int processed = 0, skipped = 0, snapshots = 0, caches = 0;
            bool complete = true;

            List<Entity> users = ReadAll(svc, TargetsQuery(ScopeUser));
            foreach (Entity t in users)
            {
                if (clock.Elapsed > Budget) { complete = false; break; }
                EntityReference period = t.GetAttributeValue<EntityReference>(TargetPeriod);
                if (period == null) { skipped++; continue; }

                decimal? elapsed = Elapsed(PeriodOf(svc, periods, period.Id), day);
                decimal? amount = MoneyValue(t, TargetAmount);
                string name = t.GetAttributeValue<string>(TargetName);

                var snap = new Entity(Snap);
                snap[SnapKey] = t.Id.ToString("D") + "|" + dayText;
                snap[Name] = "Snapshot " + name + " " + dayText;
                snap[SalesTarget] = new EntityReference(Target, t.Id);
                snap[SalesPeriod] = period;
                snap[Scope] = new OptionSetValue(ScopeUser);
                snap[Amount] = Money(amount);
                snap[Won] = Money(MoneyValue(t, TargetWon));
                snap[Committed] = Money(MoneyValue(t, TargetCommitted));
                snap[BestCase] = Money(MoneyValue(t, TargetBestCase));
                snap[Open] = Money(MoneyValue(t, TargetOpen));
                snap[Forecast] = Money(t.GetAttributeValue<decimal?>(TargetForecast));
                snap[Gap] = Money(t.GetAttributeValue<decimal?>(TargetGap));
                snap[Attainment] = Round(t.GetAttributeValue<decimal?>(TargetAttainment), 1);
                snap[Coverage] = Round(t.GetAttributeValue<decimal?>(TargetCoverage), 2);
                snap[SnapElapsed] = Round(elapsed, 2);
                snap[SnapPace] = Money(elapsed.HasValue ? (amount ?? 0m) * elapsed.Value / 100m : (decimal?)null);
                if (Upsert(svc, snap, SnapKey, SnapDate, now)) snapshots++;

                EntityReference rep = t.GetAttributeValue<EntityReference>(TargetSalesRep);
                EntityReference line = t.GetAttributeValue<EntityReference>(TargetBusinessLine);
                var cache = new Entity(Cache);
                cache[CacheKey] = ScopeUser.ToString(CultureInfo.InvariantCulture) + "|" + IdText(rep) + "|" + period.Id.ToString("D") + "|" + IdText(line);
                cache[Name] = "User: " + name;
                cache[SalesTarget] = new EntityReference(Target, t.Id);
                cache[SalesPeriod] = period;
                cache[Scope] = new OptionSetValue(ScopeUser);
                cache[BusinessLine] = line;
                cache[CacheSalesRep] = rep;
                cache[Amount] = Money(amount);
                cache[Won] = Money(MoneyValue(t, TargetWon));
                cache[Committed] = Money(MoneyValue(t, TargetCommitted));
                cache[BestCase] = Money(MoneyValue(t, TargetBestCase));
                cache[Open] = Money(MoneyValue(t, TargetOpen));
                cache[Forecast] = Money(t.GetAttributeValue<decimal?>(TargetForecast));
                cache[CacheForecastFinal] = Money(t.GetAttributeValue<decimal?>(TargetForecastFinal));
                cache[Gap] = Money(t.GetAttributeValue<decimal?>(TargetGap));
                cache[Attainment] = Round(t.GetAttributeValue<decimal?>(TargetAttainment), 1);
                cache[CacheForecastAttainment] = Round(t.GetAttributeValue<decimal?>(TargetForecastAttainment), 1);
                cache[Coverage] = Round(t.GetAttributeValue<decimal?>(TargetCoverage), 2);
                if (Upsert(svc, cache, CacheKey, CacheComputedOn, now)) caches++;
                processed++;
            }

            if (complete)
            {
                var higher = ReadAll(svc, TargetsQuery(ScopeBusinessLine, ScopeCompany));
                foreach (Entity t in higher)
                {
                    if (clock.Elapsed > Budget) { complete = false; break; }
                    EntityReference period = t.GetAttributeValue<EntityReference>(TargetPeriod);
                    if (period == null) { skipped++; continue; }
                    int scope = t.GetAttributeValue<OptionSetValue>(TargetScope).Value;
                    EntityReference line = scope == ScopeBusinessLine ? t.GetAttributeValue<EntityReference>(TargetBusinessLine) : null;
                    Sums sums = SumChildren(users, period.Id, scope, line);

                    decimal target = MoneyValue(t, TargetAmount) ?? 0m;
                    decimal gap = target - sums.Won;
                    decimal forecast = sums.Won + sums.Committed;
                    decimal? attain = target == 0m ? (decimal?)null : sums.Won / target * 100m;
                    decimal? forecastAttain = target == 0m ? (decimal?)null : forecast / target * 100m;
                    decimal? coverage = gap <= 0m ? (decimal?)null : sums.Open / gap;
                    string name = t.GetAttributeValue<string>(TargetName);

                    var snap = new Entity(Snap);
                    snap[SnapKey] = t.Id.ToString("D") + "|" + dayText;
                    snap[Name] = name + " " + dayText;
                    snap[SalesTarget] = new EntityReference(Target, t.Id);
                    snap[SalesPeriod] = period;
                    snap[Scope] = new OptionSetValue(scope);
                    snap[Amount] = Money(target);
                    snap[Won] = Money(sums.Won);
                    snap[Committed] = Money(sums.Committed);
                    snap[BestCase] = Money(sums.BestCase);
                    snap[Open] = Money(sums.Open);
                    snap[Forecast] = Money(forecast);
                    snap[Gap] = Money(gap);
                    snap[Attainment] = Round(attain, 1);
                    snap[Coverage] = Round(coverage, 2);
                    if (Upsert(svc, snap, SnapKey, SnapDate, now)) snapshots++;

                    var cache = new Entity(Cache);
                    cache[CacheKey] = scope.ToString(CultureInfo.InvariantCulture) + "||" + period.Id.ToString("D") + "|" + IdText(line);
                    cache[Name] = name;
                    cache[SalesTarget] = new EntityReference(Target, t.Id);
                    cache[SalesPeriod] = period;
                    cache[Scope] = new OptionSetValue(scope);
                    cache[BusinessLine] = line;
                    cache[Amount] = Money(target);
                    cache[Won] = Money(sums.Won);
                    cache[Committed] = Money(sums.Committed);
                    cache[BestCase] = Money(sums.BestCase);
                    cache[Open] = Money(sums.Open);
                    cache[Forecast] = Money(forecast);
                    cache[CacheForecastFinal] = Money(forecast);
                    cache[Gap] = Money(gap);
                    cache[Attainment] = Round(attain, 1);
                    cache[CacheForecastAttainment] = Round(forecastAttain, 1);
                    cache[Coverage] = Round(coverage, 2);
                    if (Upsert(svc, cache, CacheKey, CacheComputedOn, now)) caches++;
                    processed++;
                }
            }

            string summary = string.Format(CultureInfo.InvariantCulture,
                "Forecast snapshot {0}: {1} sales periods created, {2} targets, {3} snapshots and {4} cache rows written, {5} targets without a sales period skipped.{6}",
                dayText, periodsCreated, processed, snapshots, caches, skipped, complete ? string.Empty : " Not finished yet: call again to continue.");
            localContext.Trace(summary);
            ParameterCollection outputs = localContext.PluginExecutionContext.OutputParameters;
            outputs[OutSummary] = summary;
            outputs[OutTargets] = processed;
            outputs[OutSnapshots] = snapshots;
            outputs[OutCaches] = caches;
            outputs[OutComplete] = complete;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Creates the missing sales periods, as tavu_InitializeConfiguration Mode = periods does, with the system
        /// service (the gateway's role has no create on sales periods and reads System Settings only). Returns how many
        /// were created.
        /// </summary>
        private static int KeepPeriodsAhead(LocalPluginContext localContext)
        {
            IOrganizationService system = localContext.SystemService;
            int callerZone = SalesPeriodSettings.UserTimeZone(system, localContext.PluginExecutionContext.InitiatingUserId);
            int zone = SalesPeriodSettings.PeriodTimeZone(system, callerZone);
            var report = new SetupReport();
            new SalesPeriodGenerator(system, localContext, report, zone, SalesPeriodSettings.OrganizationLanguage(system)).Run();
            localContext.Trace(report.ToSummary());
            return report.Created;
        }

        private static QueryExpression TargetsQuery(params int[] scopes)
        {
            var q = new QueryExpression(Target)
            {
                ColumnSet = new ColumnSet(TargetName, TargetScope, TargetPeriod, TargetBusinessLine, TargetSalesRep, TargetAmount,
                    TargetWon, TargetCommitted, TargetBestCase, TargetOpen, TargetForecast, TargetForecastFinal, TargetAttainment,
                    TargetForecastAttainment, TargetGap, TargetCoverage),
                NoLock = true,
            };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            q.Criteria.AddCondition(TargetScope, ConditionOperator.In, Array.ConvertAll(scopes, s => (object)s));
            q.AddOrder(TargetId, OrderType.Ascending);
            return q;
        }

        /// <summary>Every row of the query, page by page with the paging cookie.</summary>
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

        private static Entity PeriodOf(IOrganizationService svc, Dictionary<Guid, Entity> cache, Guid id)
        {
            if (!cache.TryGetValue(id, out Entity p))
                cache[id] = p = svc.Retrieve(Period, id, new ColumnSet(PeriodStart, PeriodEnd));
            return p;
        }

        /// <summary>The flow's period elapsed percent (0 before the start, 100 from the end), measured at <paramref name="at"/>.</summary>
        internal static decimal? Elapsed(Entity period, DateTime at)
        {
            DateTime? start = period.GetAttributeValue<DateTime?>(PeriodStart);
            DateTime? end = period.GetAttributeValue<DateTime?>(PeriodEnd);
            if (!start.HasValue || !end.HasValue) return null;
            DateTime s = start.Value.ToUniversalTime(), e = end.Value.ToUniversalTime();
            if (at >= e) return 100m;
            if (at <= s) return 0m;
            return (decimal)(at - s).Ticks * 100m / (e - s).Ticks;
        }

        internal struct Sums { public decimal Won, Committed, BestCase, Open; }

        /// <summary>The flow's roll-up: the active User-scope targets of the period, and of the business line for that scope.
        /// A business-line target with no business line matches no child, as the flow's empty condition did.</summary>
        private static Sums SumChildren(List<Entity> users, Guid period, int scope, EntityReference line)
        {
            var sums = new Sums();
            if (scope == ScopeBusinessLine && line == null) return sums;
            foreach (Entity u in users)
            {
                EntityReference p = u.GetAttributeValue<EntityReference>(TargetPeriod);
                if (p == null || p.Id != period) continue;
                if (scope == ScopeBusinessLine)
                {
                    EntityReference l = u.GetAttributeValue<EntityReference>(TargetBusinessLine);
                    if (l == null || l.Id != line.Id) continue;
                }
                sums.Won += MoneyValue(u, TargetWon) ?? 0m;
                sums.Committed += MoneyValue(u, TargetCommitted) ?? 0m;
                sums.BestCase += MoneyValue(u, TargetBestCase) ?? 0m;
                sums.Open += MoneyValue(u, TargetOpen) ?? 0m;
            }
            return sums;
        }

        /// <summary>
        /// Creates the row, or updates only the columns that differ from the stored ones; the timestamp column is set
        /// only when something is written. True when a row was created or updated.
        /// </summary>
        private static bool Upsert(IOrganizationService svc, Entity wanted, string keyColumn, string stampColumn, DateTime now)
        {
            var columns = new List<string>(wanted.Attributes.Keys);
            var q = new QueryExpression(wanted.LogicalName) { ColumnSet = new ColumnSet(columns.ToArray()), TopCount = 1, NoLock = true };
            q.Criteria.AddCondition(keyColumn, ConditionOperator.Equal, wanted[keyColumn]);
            EntityCollection found = svc.RetrieveMultiple(q);

            if (found.Entities.Count == 0)
            {
                var create = new Entity(wanted.LogicalName);
                foreach (KeyValuePair<string, object> a in wanted.Attributes) if (a.Value != null) create[a.Key] = a.Value;
                create[stampColumn] = now;
                svc.Create(create);
                return true;
            }

            Entity current = found.Entities[0];
            var update = new Entity(wanted.LogicalName, current.Id);
            foreach (KeyValuePair<string, object> a in wanted.Attributes)
                if (!Same(a.Value, current.Contains(a.Key) ? current[a.Key] : null)) update[a.Key] = a.Value;
            if (update.Attributes.Count == 0) return false;
            update[stampColumn] = now;
            svc.Update(update);
            return true;
        }

        internal static bool Same(object a, object b)
        {
            a = Plain(a);
            b = Plain(b);
            if (a == null || b == null) return a == null && b == null;
            if (a is decimal da && b is decimal db) return da == db;
            return a.Equals(b);
        }

        private static object Plain(object v)
        {
            switch (v)
            {
                case Money m: return m.Value;
                case OptionSetValue o: return o.Value;
                case EntityReference r: return r.Id;
                case string s: return s.Length == 0 ? null : s;
                default: return v;
            }
        }

        private static decimal? MoneyValue(Entity e, string column) => e.GetAttributeValue<Money>(column)?.Value;

        /// <summary>Money as Dataverse stores it (2 decimals); null stays null.</summary>
        private static Money Money(decimal? value) => value.HasValue ? new Money(Math.Round(value.Value, 2, MidpointRounding.AwayFromZero)) : null;

        /// <summary>A decimal at the column's precision, so a stored value compares equal to the computed one.</summary>
        private static decimal? Round(decimal? value, int digits) =>
            value.HasValue ? Math.Round(value.Value, digits, MidpointRounding.AwayFromZero) : (decimal?)null;

        private static string IdText(EntityReference r) => r == null ? string.Empty : r.Id.ToString("D");
    }
}
