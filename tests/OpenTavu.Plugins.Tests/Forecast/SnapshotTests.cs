using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using OpenTavu.Plugins.Tests.Fakes;
using SnapshotPlugin = Pl.Forecast.Snapshot.Snapshot;

namespace OpenTavu.Plugins.Tests.Forecast
{
    /// <summary>tavu_RunForecastSnapshot against the rows Fl.Forecast.SnapshotDaily wrote.</summary>
    public class SnapshotTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 16, 5, 0, 0, DateTimeKind.Utc);
        private const int User = 576600000, Line = 576600002, Company = 576600003;

        private readonly PluginHarness _h = new PluginHarness();
        private readonly Guid _period;

        public SnapshotTests()
        {
            // November 2026 in UTC: on the 16th at 00:00, 15 of 30 days have passed (50 %).
            _period = _h.Service.Seed(new Entity("tavu_salesperiod")
            {
                ["tavu_name"] = "Nov 2026",
                ["tavu_startdate"] = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                ["tavu_enddate"] = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            });
        }

        private Guid UserTarget(string name, decimal target, decimal won, decimal committed, Guid? line = null, Guid? period = null)
        {
            var t = new Entity("tavu_salestarget")
            {
                ["tavu_name"] = name,
                ["tavu_scope"] = new OptionSetValue(User),
                ["tavu_salesrep"] = new EntityReference("systemuser", Guid.NewGuid()),
                ["tavu_targetamount"] = new Money(target),
                ["tavu_closedwonamount"] = new Money(won),
                ["tavu_committedamount"] = new Money(committed),
                ["tavu_bestcaseamount"] = new Money(5m),
                ["tavu_openpipelineamount"] = new Money(40m),
                ["tavu_forecastamount"] = won + committed,
                ["tavu_forecastfinal"] = won + committed,
                ["tavu_gaptogoal"] = target - won,
                ["tavu_attainmentpct"] = won / target * 100m,
                ["tavu_forecastattainmentpct"] = (won + committed) / target * 100m,
                ["tavu_coverageratio"] = 40m / (target - won),
            };
            if (period != Guid.Empty) t["tavu_salesperiod"] = new EntityReference("tavu_salesperiod", period ?? _period);
            if (line.HasValue) t["tavu_businessline"] = new EntityReference("tavu_businessline", line.Value);
            return _h.Service.Seed(t);
        }

        private Guid HigherTarget(string name, int scope, decimal target, Guid? line = null)
        {
            var t = new Entity("tavu_salestarget")
            {
                ["tavu_name"] = name,
                ["tavu_scope"] = new OptionSetValue(scope),
                ["tavu_targetamount"] = new Money(target),
                ["tavu_salesperiod"] = new EntityReference("tavu_salesperiod", _period),
            };
            if (line.HasValue) t["tavu_businessline"] = new EntityReference("tavu_businessline", line.Value);
            return _h.Service.Seed(t);
        }

        private ParameterCollection Run(DateTime? at = null, Action<SnapshotPlugin> setup = null)
        {
            var plugin = new SnapshotPlugin(() => at ?? Now);
            setup?.Invoke(plugin);
            return _h.Run(plugin, "tavu_RunForecastSnapshot");
        }

        private Entity Snapshot(Guid target, string day = "2026-11-16") =>
            _h.Service.Rows("tavu_forecastsnapshot").Single(s => (string)s["tavu_snapshotkey"] == target.ToString("D") + "|" + day);

        private static decimal Money(Entity e, string column) => e.GetAttributeValue<Money>(column).Value;

        [Fact]
        public void A_user_target_gets_the_flows_snapshot_and_cache_row()
        {
            Guid t = UserTarget("Ana Q4", 1000m, 300m, 200m);

            ParameterCollection outputs = Run();

            Entity snap = Snapshot(t);
            Assert.Equal("Snapshot Ana Q4 2026-11-16", snap["tavu_name"]);
            Assert.Equal(t, snap.GetAttributeValue<EntityReference>("tavu_salestarget").Id);
            Assert.Equal(_period, snap.GetAttributeValue<EntityReference>("tavu_salesperiod").Id);
            Assert.Equal(User, snap.GetAttributeValue<OptionSetValue>("tavu_scope").Value);
            Assert.Equal(1000m, Money(snap, "tavu_targetamount"));
            Assert.Equal(300m, Money(snap, "tavu_closedwonamount"));
            Assert.Equal(500m, Money(snap, "tavu_forecastamount"));
            Assert.Equal(700m, Money(snap, "tavu_gaptogoal"));
            Assert.Equal(30.0m, snap["tavu_attainment"]);
            Assert.Equal(0.06m, snap["tavu_coverageratio"]);              // 40 / 700, 2 decimals
            Assert.Equal(50.00m, snap["tavu_periodelapsed"]);             // 15 of 30 days, at 00:00 UTC
            Assert.Equal(500m, Money(snap, "tavu_expectedpaceamount"));   // 1000 x 50 %
            Assert.Equal(Now, snap["tavu_snapshotdate"]);

            Entity rep = _h.Service.Rows("tavu_salestarget").Single(r => r.Id == t);
            Entity cache = Assert.Single(_h.Service.Rows("tavu_reportingcache"));
            Assert.Equal("576600000|" + rep.GetAttributeValue<EntityReference>("tavu_salesrep").Id.ToString("D") + "|" + _period.ToString("D") + "|", cache["tavu_cachekey"]);
            Assert.Equal("User: Ana Q4", cache["tavu_name"]);
            Assert.Equal(500m, Money(cache, "tavu_forecastfinal"));
            Assert.Equal(50.0m, cache["tavu_forecastattainment"]);
            Assert.False(cache.Contains("tavu_businessline"));

            Assert.Equal(1, outputs["TargetsProcessed"]);
            Assert.Equal(1, outputs["SnapshotsWritten"]);
            Assert.Equal(1, outputs["CachesWritten"]);
            Assert.True((bool)outputs["Complete"]);
        }

        [Fact]
        public void A_second_run_the_same_day_writes_nothing()
        {
            UserTarget("Ana Q4", 1000m, 300m, 200m);
            HigherTarget("Company Q4", Company, 5000m);
            Run();
            _h.Service.Writes.Clear();

            ParameterCollection outputs = Run(Now.AddHours(10));

            Assert.Empty(_h.Service.Writes);
            Assert.Equal(0, outputs["SnapshotsWritten"]);
            Assert.Equal(0, outputs["CachesWritten"]);
        }

        [Fact]
        public void A_changed_amount_updates_only_that_targets_rows()
        {
            Guid ana = UserTarget("Ana Q4", 1000m, 300m, 200m);
            UserTarget("Luis Q4", 1000m, 100m, 100m);
            Run();
            _h.Service.Writes.Clear();
            _h.Service.Update(new Entity("tavu_salestarget", ana) { ["tavu_closedwonamount"] = new Money(400m) });
            _h.Service.Writes.Clear();

            Run(Now.AddHours(1));

            Assert.Equal(new[] { "Update tavu_forecastsnapshot", "Update tavu_reportingcache" }, _h.Service.Writes);
            Assert.Equal(400m, Money(Snapshot(ana), "tavu_closedwonamount"));
        }

        [Fact]
        public void The_next_day_adds_a_snapshot_and_keeps_the_one_cache_row()
        {
            Guid t = UserTarget("Ana Q4", 1000m, 300m, 200m);
            Run();

            Run(Now.AddDays(1));

            Assert.Equal(2, _h.Service.Rows("tavu_forecastsnapshot").Count());
            Assert.Equal(53.33m, Snapshot(t, "2026-11-17")["tavu_periodelapsed"]);   // 16 of 30 days
            Assert.Single(_h.Service.Rows("tavu_reportingcache"));
        }

        [Fact]
        public void A_row_the_flow_wrote_today_is_updated_not_duplicated()
        {
            Guid t = UserTarget("Ana Q4", 1000m, 300m, 200m);
            _h.Service.Seed(new Entity("tavu_forecastsnapshot")
            {
                ["tavu_snapshotkey"] = t.ToString("D") + "|2026-11-16",
                ["tavu_name"] = "Snapshot Ana Q4 2026-11-16",
                ["tavu_closedwonamount"] = new Money(250m),
            });

            Run();

            Entity snap = Assert.Single(_h.Service.Rows("tavu_forecastsnapshot"));
            Assert.Equal(300m, Money(snap, "tavu_closedwonamount"));
        }

        [Fact]
        public void Business_line_and_company_targets_roll_up_their_user_targets()
        {
            Guid consulting = Guid.NewGuid(), audit = Guid.NewGuid();
            UserTarget("Ana", 1000m, 300m, 200m, consulting);
            UserTarget("Luis", 1000m, 100m, 100m, consulting);
            UserTarget("Eva", 1000m, 600m, 0m, audit);
            Guid line = HigherTarget("Consulting Q4", Line, 1000m, consulting);
            Guid company = HigherTarget("Company Q4", Company, 0m);

            Run();

            Entity l = Snapshot(line);
            Assert.Equal("Consulting Q4 2026-11-16", l["tavu_name"]);
            Assert.Equal(400m, Money(l, "tavu_closedwonamount"));    // Ana + Luis, not Eva
            Assert.Equal(300m, Money(l, "tavu_committedamount"));
            Assert.Equal(700m, Money(l, "tavu_forecastamount"));
            Assert.Equal(600m, Money(l, "tavu_gaptogoal"));
            Assert.Equal(40.0m, l["tavu_attainment"]);
            Assert.Equal(0.13m, l["tavu_coverageratio"]);              // open 80 / gap 600
            Assert.False(l.Contains("tavu_periodelapsed"));            // the flow writes neither on higher scopes
            Assert.False(l.Contains("tavu_expectedpaceamount"));

            Entity lineCache = _h.Service.Rows("tavu_reportingcache").Single(c => (string)c["tavu_cachekey"] == "576600002||" + _period.ToString("D") + "|" + consulting.ToString("D"));
            Assert.Equal("Consulting Q4", lineCache["tavu_name"]);
            Assert.Equal(700m, Money(lineCache, "tavu_forecastfinal"));
            Assert.Equal(70.0m, lineCache["tavu_forecastattainment"]);
            Assert.Equal(consulting, lineCache.GetAttributeValue<EntityReference>("tavu_businessline").Id);

            Entity c = Snapshot(company);
            Assert.Equal(1000m, Money(c, "tavu_closedwonamount"));    // every user target of the period
            Assert.False(c.Contains("tavu_attainment"));               // target 0: no attainment
            Assert.False(c.Contains("tavu_coverageratio"));            // gap 0 - 1000 <= 0: no coverage
            Entity companyCache = _h.Service.Rows("tavu_reportingcache").Single(x => (string)x["tavu_cachekey"] == "576600003||" + _period.ToString("D") + "|");
            Assert.False(companyCache.Contains("tavu_businessline"));
        }

        [Fact]
        public void A_target_with_no_period_is_skipped_and_the_run_completes()
        {
            UserTarget("No period", 1000m, 0m, 0m, period: Guid.Empty);
            UserTarget("Ana Q4", 1000m, 300m, 200m);

            ParameterCollection outputs = Run();

            Assert.Single(_h.Service.Rows("tavu_forecastsnapshot"));
            Assert.True((bool)outputs["Complete"]);
            Assert.Contains("1 targets without a sales period skipped", (string)outputs["Summary"]);
        }

        [Fact]
        public void Every_page_of_targets_is_processed()
        {
            for (int i = 0; i < 5; i++) UserTarget("T" + i, 1000m, 100m, 0m);

            ParameterCollection outputs = Run(setup: p => p.PageSize = 2);

            Assert.Equal(5, outputs["TargetsProcessed"]);
            Assert.Equal(5, _h.Service.Rows("tavu_forecastsnapshot").Count());
        }

        [Fact]
        public void Out_of_time_answers_not_complete_and_the_next_call_finishes()
        {
            for (int i = 0; i < 3; i++) UserTarget("T" + i, 1000m, 100m, 0m);

            ParameterCollection first = Run(setup: p => p.Budget = TimeSpan.Zero);
            ParameterCollection second = Run();

            Assert.False((bool)first["Complete"]);
            Assert.True((bool)second["Complete"]);
            Assert.Equal(3, _h.Service.Rows("tavu_forecastsnapshot").Count());
        }
    }
}
