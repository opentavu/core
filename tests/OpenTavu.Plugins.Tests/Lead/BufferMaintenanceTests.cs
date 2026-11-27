using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using OpenTavu.Plugins.Tests.Fakes;
using BufferPlugin = Pl.Lead.BufferMaintenance.BufferMaintenance;

namespace OpenTavu.Plugins.Tests.Lead
{
    /// <summary>tavu_RunLeadBufferMaintenance: the flow's aging and stale rules, written only on a change.</summary>
    public class BufferMaintenanceTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 16, 5, 0, 0, DateTimeKind.Utc);
        private const int Fresh = 576600000, Aging = 576600001, StaleBand = 576600002;
        private const int New = 576600001, AwaitingReview = 576600003, ManualReview = 576600004, StaleStatus = 576600009;

        private readonly PluginHarness _h = new PluginHarness();
        private readonly Guid _settings;

        public BufferMaintenanceTests()
        {
            _settings = _h.Service.Seed(new Entity("tavu_systemsettings")
            {
                ["tavu_leadbufferagingdays"] = 7,
                ["tavu_leadbufferstaledays"] = 14,
            });
        }

        /// <summary>A lead created <paramref name="days"/> whole days (plus an hour) before <see cref="Now"/>.</summary>
        private Guid Lead(int days, int status = New, int? band = null)
        {
            var l = new Entity("tavu_lead")
            {
                ["createdon"] = Now.AddDays(-days).AddHours(-1),
                ["statuscode"] = new OptionSetValue(status),
            };
            if (band.HasValue) l["tavu_bufferalert"] = new OptionSetValue(band.Value);
            return _h.Service.Seed(l);
        }

        private Entity Row(Guid id) => _h.Service.Rows("tavu_lead").Single(l => l.Id == id);
        private int? BandOf(Guid id) => Row(id).GetAttributeValue<OptionSetValue>("tavu_bufferalert")?.Value;

        private ParameterCollection Run(DateTime? at = null, Action<BufferPlugin> setup = null)
        {
            var plugin = new BufferPlugin(() => at ?? Now);
            setup?.Invoke(plugin);
            return _h.Run(plugin, "tavu_RunLeadBufferMaintenance");
        }

        [Theory]
        [InlineData(0, Fresh)]
        [InlineData(7, Fresh)]       // days > aging (7) is Aging, so 7 is still Fresh
        [InlineData(8, Aging)]
        [InlineData(14, Aging)]      // days > stale (14) is Stale
        [InlineData(15, StaleBand)]
        public void The_band_follows_the_flows_boundaries(int days, int band)
        {
            Guid id = Lead(days);

            Run();

            Assert.Equal(band, BandOf(id));
            Assert.Equal(days, Row(id)["tavu_daysinbuffer"]);
        }

        [Fact]
        public void Awaiting_review_past_the_stale_days_closes_as_stale()
        {
            Guid old = Lead(15, AwaitingReview);
            Guid edge = Lead(14, AwaitingReview);

            ParameterCollection outputs = Run();

            Entity closed = Row(old);
            Assert.Equal(1, closed.GetAttributeValue<OptionSetValue>("statecode").Value);
            Assert.Equal(StaleStatus, closed.GetAttributeValue<OptionSetValue>("statuscode").Value);
            Assert.Equal(StaleBand, BandOf(old));
            Assert.Equal(0, Row(edge).GetAttributeValue<OptionSetValue>("statecode").Value);
            Assert.Equal(1, outputs["LeadsClosedStale"]);
        }

        [Theory]
        [InlineData(New)]
        [InlineData(ManualReview)]
        public void New_and_manual_review_leads_get_the_stale_band_but_never_close(int status)
        {
            Guid id = Lead(30, status);

            Run();

            Assert.Equal(0, Row(id).GetAttributeValue<OptionSetValue>("statecode").Value);
            Assert.Equal(StaleBand, BandOf(id));
        }

        [Fact]
        public void A_lead_already_in_its_band_is_not_written()
        {
            Lead(3, band: Fresh);
            Lead(10, band: Aging);
            Lead(20, band: StaleBand);

            ParameterCollection outputs = Run();

            Assert.Equal(new[] { "Update tavu_systemsettings" }, _h.Service.Writes);   // only the last-run stamp
            Assert.Equal(0, outputs["LeadsUpdated"]);
        }

        [Fact]
        public void A_second_run_the_same_day_writes_only_the_stamp()
        {
            Lead(3);
            Lead(10);
            Lead(20, AwaitingReview);
            Run();
            _h.Service.Writes.Clear();

            Run(Now.AddHours(6));

            Assert.Equal(new[] { "Update tavu_systemsettings" }, _h.Service.Writes);
        }

        [Fact]
        public void A_lead_moves_band_the_day_it_crosses_the_boundary()
        {
            Guid id = Lead(7);
            Run();
            Assert.Equal(Fresh, BandOf(id));

            Run(Now.AddDays(1));

            Assert.Equal(Aging, BandOf(id));
            Assert.Equal(8, Row(id)["tavu_daysinbuffer"]);
        }

        [Fact]
        public void Empty_settings_use_7_and_14_days()
        {
            _h.Service.Update(new Entity("tavu_systemsettings", _settings) { ["tavu_leadbufferagingdays"] = null, ["tavu_leadbufferstaledays"] = null });
            Guid aging = Lead(8);
            Guid stale = Lead(15);

            Run();

            Assert.Equal(Aging, BandOf(aging));
            Assert.Equal(StaleBand, BandOf(stale));
        }

        [Fact]
        public void A_completed_run_stamps_the_last_run_on_system_settings()
        {
            Run();

            Entity settings = Assert.Single(_h.Service.Rows("tavu_systemsettings"));
            Assert.Equal(Now, settings["tavu_dailyjobslastrun"]);
        }

        [Fact]
        public void Every_page_of_leads_is_processed()
        {
            Guid[] ids = Enumerable.Range(0, 5).Select(_ => Lead(10)).ToArray();

            ParameterCollection outputs = Run(setup: p => p.PageSize = 2);

            Assert.Equal(5, outputs["LeadsUpdated"]);
            Assert.All(ids, id => Assert.Equal(Aging, BandOf(id)));
        }

        [Fact]
        public void Leads_are_written_with_the_callers_privileges_and_only_the_stamp_with_the_system_service()
        {
            Lead(3);
            Lead(10);
            Lead(20, AwaitingReview);

            Run();

            Assert.Equal(new[] { "user Update tavu_lead", "user Update tavu_lead", "user Update tavu_lead", "system Update tavu_systemsettings" },
                _h.WritesBy);
        }

        [Fact]
        public void Out_of_time_answers_not_complete_without_a_stamp_and_the_next_call_finishes()
        {
            Guid[] ids = Enumerable.Range(0, 3).Select(_ => Lead(10)).ToArray();

            ParameterCollection first = Run(setup: p => p.Budget = TimeSpan.Zero);
            Assert.False((bool)first["Complete"]);
            Assert.False(_h.Service.Rows("tavu_systemsettings").Single().Contains("tavu_dailyjobslastrun"));

            ParameterCollection second = Run();

            Assert.True((bool)second["Complete"]);
            Assert.All(ids, id => Assert.Equal(Aging, BandOf(id)));
        }
    }
}
