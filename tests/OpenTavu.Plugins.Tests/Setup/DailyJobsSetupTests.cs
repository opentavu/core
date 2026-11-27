using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using OpenTavu.Plugins.Tests.Fakes;
using Pl.Setup.Initialize;

namespace OpenTavu.Plugins.Tests.Setup
{
    /// <summary>The setup routine's part of the daily jobs: the period time zone (decision A) and the last-run check (C).</summary>
    public class DailyJobsSetupTests
    {
        private const int Bogota = 45, Madrid = 105, Caller = 92;

        private static Guid Calendar(PluginHarness h, int zone, bool isDefault, int state = 0) =>
            h.Service.Seed(new Entity("tavu_businesscalendar")
            {
                ["tavu_timezone"] = zone,
                ["tavu_isdefault"] = isDefault,
                ["statecode"] = new OptionSetValue(state),
            });

        [Fact]
        public void Periods_use_the_default_calendars_time_zone_not_the_callers()
        {
            var h = new PluginHarness();
            Calendar(h, Madrid, isDefault: false);
            Calendar(h, Bogota, isDefault: true);

            Assert.Equal(Bogota, Initialize.PeriodTimeZone(h.Service, Caller));
        }

        [Fact]
        public void Without_an_active_default_calendar_periods_use_the_callers_time_zone()
        {
            var h = new PluginHarness();
            Calendar(h, Bogota, isDefault: true, state: 1);   // inactive

            Assert.Equal(Caller, Initialize.PeriodTimeZone(h.Service, Caller));
        }

        private static List<(string Level, string Area, string Message)> Diagnose(PluginHarness h)
        {
            ParameterCollection outputs = h.Run(new Initialize(), "tavu_InitializeConfiguration",
                new Dictionary<string, object> { ["Mode"] = "diagnose" });
            var report = (Dictionary<string, object>)MiniJson.Parse((string)outputs["Report"]);
            return ((List<object>)report["items"]).Cast<Dictionary<string, object>>()
                .Select(i => ((string)i["level"], (string)i["area"], (string)i["message"])).ToList();
        }

        private static PluginHarness WithLastRun(DateTime? lastRun)
        {
            var h = new PluginHarness();
            var settings = new Entity("tavu_systemsettings") { ["tavu_name"] = "System Settings" };
            if (lastRun.HasValue) settings["tavu_dailyjobslastrun"] = lastRun.Value;
            h.Service.Seed(settings);
            return h;
        }

        [Fact]
        public void Daily_jobs_that_never_ran_are_information_not_a_warning()
        {
            var items = Diagnose(WithLastRun(null));

            Assert.Contains(items, i => i.Level == "info" && i.Area == "Daily jobs");
            Assert.DoesNotContain(items, i => i.Level == "warning" && i.Area == "Daily jobs");
        }

        [Fact]
        public void Daily_jobs_that_ran_yesterday_give_no_finding()
        {
            Assert.DoesNotContain(Diagnose(WithLastRun(DateTime.UtcNow.AddHours(-25))), i => i.Area == "Daily jobs");
        }

        [Fact]
        public void Two_missed_days_give_a_warning()
        {
            var items = Diagnose(WithLastRun(DateTime.UtcNow.AddHours(-50)));

            Assert.Contains(items, i => i.Level == "warning" && i.Area == "Daily jobs" && i.Message.Contains("more than two days ago"));
        }
    }
}
