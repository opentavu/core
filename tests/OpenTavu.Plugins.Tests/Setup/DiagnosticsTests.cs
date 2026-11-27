using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using OpenTavu.Plugins.Tests.Fakes;
using Pl.Setup.Initialize;

namespace OpenTavu.Plugins.Tests.Setup
{
    /// <summary>
    /// Verify Setup's checks (tavu_InitializeConfiguration, Mode = diagnose) on environments with nothing configured.
    /// A fresh install must give findings, never an exception (v1.3.0 smoke test, CheckMailboxes).
    /// </summary>
    public class DiagnosticsTests
    {
        private static (bool Complete, List<(string Level, string Area, string Message)> Items) Diagnose(PluginHarness h)
        {
            ParameterCollection outputs = h.Run(new Initialize(), "tavu_InitializeConfiguration",
                new Dictionary<string, object> { ["Mode"] = "diagnose" });
            var report = (Dictionary<string, object>)MiniJson.Parse((string)outputs["Report"]);
            var items = ((List<object>)report["items"]).Cast<Dictionary<string, object>>()
                .Select(i => ((string)i["level"], (string)i["area"], (string)i["message"])).ToList();
            return ((bool)outputs["Complete"], items);
        }

        [Fact]
        public void An_empty_environment_gives_findings_and_no_exception()
        {
            var h = new PluginHarness();

            var (complete, items) = Diagnose(h);

            Assert.True(complete);
            Assert.Contains(items, i => i.Level == "error" && i.Area == "Case statuses");
            Assert.Contains(items, i => i.Level == "error" && i.Area == "Case types");
            Assert.Contains(items, i => i.Level == "error" && i.Area == "Business calendar");
            Assert.Contains(items, i => i.Level == "error" && i.Area == "AI" && i.Message.StartsWith("There is no System Settings record"));
            Assert.Contains(items, i => i.Level == "warning" && i.Area == "Company profile");
            Assert.Contains(items, i => i.Level == "error" && i.Area == "Sales periods");
            // No agent and no mailbox: nothing to report about either.
            Assert.DoesNotContain(items, i => i.Area == "Agents" || i.Area == "Mailboxes");
        }

        [Fact]
        public void Empty_mailboxes_and_no_agent_give_no_mailbox_finding()
        {
            // The state of every fresh install after the seed: System Settings exists, both mailboxes empty, no agent.
            // Before the fix, CheckMailboxes trimmed the null sender and the whole run failed.
            var h = new PluginHarness();
            h.Service.Seed(new Entity("tavu_systemsettings") { ["tavu_name"] = "System Settings", ["tavu_aienabled"] = true });

            var (_, items) = Diagnose(h);

            Assert.DoesNotContain(items, i => i.Area == "Mailboxes");
        }

        [Fact]
        public void An_active_agent_without_a_sender_mailbox_is_reported()
        {
            var h = new PluginHarness();
            h.Service.Seed(new Entity("tavu_systemsettings") { ["tavu_name"] = "System Settings" });
            h.Service.Seed(new Entity("tavu_agent") { ["tavu_name"] = "Agent" });

            var (_, items) = Diagnose(h);

            Assert.Contains(items, i => i.Level == "warning" && i.Area == "Mailboxes" && i.Message.Contains("no Notification Sender Mailbox"));
        }
    }
}
