using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;

namespace Pl.Setup.Initialize
{
    /// <summary>
    /// Read-only health checks of an OpenTavu install. Each check reports what is wrong AND what to do,
    /// in words an administrator can act on. Nothing here writes data.
    /// </summary>
    internal sealed class Diagnostics
    {
        private const int StateActive = 0;
        private const int StepDisabled = 1;

        private readonly IOrganizationService _svc;
        private readonly LocalPluginContext _ctx;
        private readonly SetupReport _report;

        public Diagnostics(IOrganizationService svc, LocalPluginContext ctx, SetupReport report)
        {
            _svc = svc;
            _ctx = ctx;
            _report = report;
        }

        public void RunAll()
        {
            RunCheck("Plugin steps", CheckPluginSteps);
            RunCheck("Case statuses", CheckCaseStatuses);
            RunCheck("Case types", CheckSingleDefault("tavu_casetype", "tavu_isdefault", "Case types",
                "No active case type is marked as default; cases without a type cannot be matched to an SLA."));
            RunCheck("Business calendar", CheckSingleDefault("tavu_businesscalendar", "tavu_isdefault", "Business calendar",
                "No active business calendar is marked as default; SLA target dates cannot be calculated."));
            RunCheck("AI", CheckAi);
            RunCheck("Company profile", CheckCompanyProfile);
            RunCheck("Agents", CheckAgents);
            RunCheck("Mailboxes", CheckMailboxes);
            RunCheck("Sales periods", CheckCurrentPeriod);
        }

        /// <summary>Only the check the daily flow needs.</summary>
        public void RunPeriodsOnly()
        {
            RunCheck("Sales periods", CheckCurrentPeriod);
        }

        /// <summary>
        /// Runs one check. A failed Dataverse call rolls back the whole run (including the seed), so a
        /// failing check is not swallowed: it stops with a message naming the check.
        /// </summary>
        private void RunCheck(string area, Action check)
        {
            try { check(); }
            catch (InvalidPluginExecutionException) { throw; }
            catch (Exception ex)
            {
                _ctx.Trace("Diagnostic {0} failed: {1}", area, ex.ToString());
                throw new InvalidPluginExecutionException("Setup check '" + area + "' failed: " + ex.Message, ex);
            }
        }

        // ------------------------------------------------------------------ checks

        /// <summary>Disabled steps of OpenTavu assemblies (Pl.*) stop features silently.</summary>
        private void CheckPluginSteps()
        {
            var q = new QueryExpression("sdkmessageprocessingstep")
            {
                ColumnSet = new ColumnSet("name"),
                NoLock = true
            };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, StepDisabled);
            LinkEntity type = q.AddLink("plugintype", "plugintypeid", "plugintypeid");
            type.LinkCriteria.AddCondition("assemblyname", ConditionOperator.BeginsWith, "Pl.");

            DataCollection<Entity> steps = _svc.RetrieveMultiple(q).Entities;
            if (steps.Count == 0)
            {
                _report.Ok("Plugin steps", "All OpenTavu plugin steps are enabled.");
                return;
            }

            var names = new List<string>();
            foreach (Entity s in steps)
            {
                if (names.Count == 8) { names.Add("..."); break; }
                names.Add(s.GetAttributeValue<string>("name"));
            }
            _report.Fail("Plugin steps", string.Format(CultureInfo.InvariantCulture,
                "{0} OpenTavu plugin steps are disabled, so their features do nothing: {1}. Enable them in the Plug-in Registration Tool or Power Apps (Solutions > OpenTavu > Plug-in steps).",
                steps.Count, string.Join(", ", names.ToArray())));
        }

        /// <summary>The case engine resolves statuses by flags; each role needs exactly one (or at least one) row.</summary>
        private void CheckCaseStatuses()
        {
            var q = new QueryExpression("tavu_casestatus")
            {
                ColumnSet = new ColumnSet("tavu_name", "tavu_isdefaultnew", "tavu_isaicategorized", "tavu_ismanualreview", "tavu_isresumetarget"),
                NoLock = true
            };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            DataCollection<Entity> rows = _svc.RetrieveMultiple(q).Entities;
            if (rows.Count == 0)
            {
                _report.Fail("Case statuses", "There are no active case statuses. Run this setup again or create them in Service Setup > Case Statuses.");
                return;
            }

            ExactlyOne(rows, "tavu_isdefaultnew", "Is Default New",
                "new cases are created without a status");
            AtLeastOne(rows, "tavu_isaicategorized", "Is AI Categorized", Level.Error,
                "cases categorized by AI keep their initial status");
            AtLeastOne(rows, "tavu_ismanualreview", "Is Manual Review", Level.Error,
                "cases the AI is not sure about are not routed to manual review");
            AtLeastOne(rows, "tavu_isresumetarget", "Is Resume Target", Level.Warning,
                "a paused SLA has no status to resume to");
        }

        private void ExactlyOne(DataCollection<Entity> rows, string flag, string label, string consequence)
        {
            List<string> on = Flagged(rows, flag);
            if (on.Count == 1) return;
            if (on.Count == 0)
                _report.Fail("Case statuses", "No active status has '" + label + "' = Yes, so " + consequence + ". Mark exactly one.");
            else
                _report.Warn("Case statuses", "Several statuses have '" + label + "' = Yes (" + string.Join(", ", on.ToArray()) + "). Keep exactly one; the first by sort order is used.");
        }

        private void AtLeastOne(DataCollection<Entity> rows, string flag, string label, Level level, string consequence)
        {
            if (Flagged(rows, flag).Count > 0) return;
            _report.Add(level, "Case statuses", "No active status has '" + label + "' = Yes, so " + consequence + ".");
        }

        private static List<string> Flagged(DataCollection<Entity> rows, string flag)
        {
            var list = new List<string>();
            foreach (Entity r in rows)
                if (r.GetAttributeValue<bool>(flag)) list.Add(r.GetAttributeValue<string>("tavu_name"));
            return list;
        }

        private Action CheckSingleDefault(string table, string flag, string area, string missingMessage)
        {
            return delegate
            {
                var q = new QueryExpression(table) { ColumnSet = new ColumnSet(flag), NoLock = true };
                q.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
                q.Criteria.AddCondition(flag, ConditionOperator.Equal, true);
                int n = _svc.RetrieveMultiple(q).Entities.Count;
                if (n == 0) _report.Fail(area, missingMessage);
                else if (n > 1) _report.Warn(area, n.ToString(CultureInfo.InvariantCulture) + " active rows are marked as default; keep exactly one.");
            };
        }

        /// <summary>
        /// Gateway mode needs both environment variables; direct mode needs a complete default model and its key.
        /// Also flags model rows whose provider label means nothing in gateway mode (the confusion this check exists for).
        /// </summary>
        private void CheckAi()
        {
            Entity settings = Top1("tavu_systemsettings", new ColumnSet("tavu_aienabled", "tavu_defaultaimodel"));
            if (settings == null)
            {
                _report.Fail("AI", "There is no System Settings record. Run this setup again.");
                return;
            }
            if (!settings.GetAttributeValue<bool>("tavu_aienabled"))
            {
                _report.Info("AI", "AI is turned off in System Settings (AI Enabled = No); AI tasks will not run.");
                return;
            }

            string url = EnvironmentVariable("tavu_GatewayUrl");
            string key = EnvironmentVariable("tavu_GatewayKey");
            bool hasUrl = !string.IsNullOrWhiteSpace(url), hasKey = !string.IsNullOrWhiteSpace(key);

            var models = new QueryExpression("tavu_aimodel")
            {
                ColumnSet = new ColumnSet("tavu_name", "tavu_provider", "tavu_deploymentmodelid", "tavu_endpoint", "tavu_secretname", "tavu_isdefault"),
                NoLock = true
            };
            models.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            DataCollection<Entity> modelRows = _svc.RetrieveMultiple(models).Entities;

            int defaults = 0;
            foreach (Entity m in modelRows) if (m.GetAttributeValue<bool>("tavu_isdefault")) defaults++;
            if (defaults > 1)
                _report.Warn("AI", defaults.ToString(CultureInfo.InvariantCulture) + " AI models are marked 'Is Default'. Keep one; the Default AI Model in System Settings is the one used.");

            EntityReference defaultRef = settings.GetAttributeValue<EntityReference>("tavu_defaultaimodel");

            if (hasUrl != hasKey)
            {
                _report.Fail("AI", "Only one of the environment variables tavu_GatewayUrl and tavu_GatewayKey has a value. Set both to use the gateway, or clear both to call the AI provider directly.");
                return;
            }

            if (hasUrl)
            {
                _report.Ok("AI", "Gateway mode: AI runs through " + url + ".");
                if (defaultRef == null)
                    _report.Info("AI", "No Default AI Model in System Settings: tasks without their own model run on the gateway's default model.");

                // Same model name under different provider labels: in gateway mode only the name travels.
                var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (Entity m in modelRows)
                {
                    string n = m.GetAttributeValue<string>("tavu_deploymentmodelid");
                    if (string.IsNullOrWhiteSpace(n)) continue;
                    List<string> list;
                    if (!byName.TryGetValue(n.Trim(), out list)) { list = new List<string>(); byName[n.Trim()] = list; }
                    list.Add(m.GetAttributeValue<string>("tavu_name"));
                }
                foreach (var kv in byName)
                {
                    if (kv.Value.Count < 2) continue;
                    _report.Info("AI", "In gateway mode these AI models are the same model ('" + kv.Key + "'): " + string.Join(", ", kv.Value.ToArray()) +
                        ". The gateway's provider is used; their Provider, Endpoint and Secret Name are ignored. Consider keeping only one.");
                }
                return;
            }

            // Direct mode
            if (defaultRef == null)
            {
                _report.Fail("AI", "No gateway is configured and System Settings has no Default AI Model. Set the gateway variables, or create an AI model and select it as Default AI Model.");
                return;
            }
            Entity model = null;
            foreach (Entity m in modelRows) if (m.Id == defaultRef.Id) model = m;
            if (model == null)
            {
                _report.Fail("AI", "The Default AI Model in System Settings is inactive or no longer exists.");
                return;
            }
            string name = model.GetAttributeValue<string>("tavu_name");
            if (string.IsNullOrWhiteSpace(model.GetAttributeValue<string>("tavu_deploymentmodelid")))
                _report.Fail("AI", "AI model '" + name + "' has no Deployment / Model ID.");
            string secret = model.GetAttributeValue<string>("tavu_secretname");
            if (string.IsNullOrWhiteSpace(secret))
                _report.Fail("AI", "AI model '" + name + "' has no Secret Name (the environment variable that holds the API key).");
            else if (string.IsNullOrWhiteSpace(EnvironmentVariable(secret.Trim())))
                _report.Fail("AI", "The environment variable '" + secret.Trim() + "' named by AI model '" + name + "' has no value. Paste the provider API key there.");
            else
                _report.Ok("AI", "Direct mode: AI runs on model '" + name + "'.");
        }

        private void CheckCompanyProfile()
        {
            Entity p = Top1("tavu_companyprofile", new ColumnSet("tavu_name"));
            if (p == null || string.IsNullOrWhiteSpace(p.GetAttributeValue<string>("tavu_name")))
                _report.Warn("Company profile", "The company profile is empty: proposal PDFs have no letterhead and the fiscal year is assumed to start in January. Fill it in Settings > Company Profile.");
        }

        /// <summary>
        /// The gateway's default operator range for each agent limit (gateway AgentCaps.Defaults; an operator can change them
        /// with Agent__Caps__* app settings). A value outside is clamped by the gateway, never refused.
        /// </summary>
        private static readonly string[][] AgentLimitRanges =
        {
            new[] { "tavu_slotsearchdays", "Slot Search Days", "1", "60" },
            new[] { "tavu_slotsperoffer", "Times per Offer", "1", "5" },
            new[] { "tavu_bookingdeclinesbeforelink", "Declines Before the Booking Link", "1", "5" },
            new[] { "tavu_movedeclinesbeforekeep", "Declines Before Keeping a Meeting", "1", "5" },
            new[] { "tavu_confirmationlimitminutes", "Confirmation Valid For (Minutes)", "5", "1440" },
            new[] { "tavu_owneremailintervalminutes", "Owner Email Interval (Minutes)", "5", "1440" },
            new[] { "tavu_offtopiclimit", "Off-Topic Messages Before Closing", "2", "5" },
            new[] { "tavu_turnlimit", "Turn Limit", "4", "40" },
            new[] { "tavu_tokenbudget", "Token Budget", "1", "100000" },
        };

        /// <summary>
        /// Active agents: limits outside the gateway's default ranges, WhatsApp numbers missing (with more than one agent) or
        /// shared, and the System Settings mailboxes the gateway reads (owner notification sender, case intake).
        /// </summary>
        private void CheckAgents()
        {
            var columns = new List<string> { "tavu_name", "tavu_whatsappphonenumberid" };
            foreach (string[] r in AgentLimitRanges) columns.Add(r[0]);
            var q = new QueryExpression("tavu_agent") { ColumnSet = new ColumnSet(columns.ToArray()), NoLock = true };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            q.AddOrder("createdon", OrderType.Ascending);
            DataCollection<Entity> agents = _svc.RetrieveMultiple(q).Entities;
            if (agents.Count == 0) return;

            int problems = 0;
            var byNumber = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (Entity a in agents)
            {
                string name = a.GetAttributeValue<string>("tavu_name");
                foreach (string[] r in AgentLimitRanges)
                {
                    if (!a.Contains(r[0])) continue;
                    int value = a.GetAttributeValue<int>(r[0]);
                    // An empty or zero turn limit or token budget means the default.
                    if (value == 0 && (r[0] == "tavu_turnlimit" || r[0] == "tavu_tokenbudget")) continue;
                    int min = int.Parse(r[2], CultureInfo.InvariantCulture), max = int.Parse(r[3], CultureInfo.InvariantCulture);
                    if (value >= min && value <= max) continue;
                    problems++;
                    _report.Warn("Agents", string.Format(CultureInfo.InvariantCulture,
                        "Agent '{0}': {1} is {2}, outside the gateway's range {3} to {4}; it will be clamped to {5}. Set a value in the range, or ask the gateway operator to widen it.",
                        name, r[1], value, min, max, value < min ? min : max));
                }

                string number = (a.GetAttributeValue<string>("tavu_whatsappphonenumberid") ?? string.Empty).Trim();
                if (number.Length == 0) continue;
                List<string> same;
                if (!byNumber.TryGetValue(number, out same)) { same = new List<string>(); byNumber[number] = same; }
                same.Add(name);
            }

            if (agents.Count > 1)
                foreach (Entity a in agents)
                {
                    if (!string.IsNullOrWhiteSpace(a.GetAttributeValue<string>("tavu_whatsappphonenumberid"))) continue;
                    problems++;
                    _report.Warn("Agents", "Agent '" + a.GetAttributeValue<string>("tavu_name") + "' has no WhatsApp Phone Number ID while " +
                        agents.Count.ToString(CultureInfo.InvariantCulture) + " agents are active. A message that arrives on a number no agent has goes to the oldest Lead Qualification agent. Set the number on each agent.");
                }
            foreach (var kv in byNumber)
            {
                if (kv.Value.Count < 2) continue;
                problems++;
                _report.Warn("Agents", "Agents " + string.Join(", ", kv.Value.ToArray()) + " have the same WhatsApp Phone Number ID (" + kv.Key +
                    "); the oldest gets every message. Give each agent its own number, or deactivate one.");
            }
            if (problems == 0)
                _report.Ok("Agents", agents.Count.ToString(CultureInfo.InvariantCulture) + " active agent(s); limits within the gateway's default ranges.");
        }

        /// <summary>The System Settings mailboxes: well formed, and a sender while an agent is active (else no owner emails).</summary>
        private void CheckMailboxes()
        {
            Entity settings = Top1("tavu_systemsettings", new ColumnSet("tavu_notificationsendermailbox", "tavu_caseintakemailbox"));
            string sender = settings == null ? null : settings.GetAttributeValue<string>("tavu_notificationsendermailbox");
            string intake = settings == null ? null : settings.GetAttributeValue<string>("tavu_caseintakemailbox");
            if (string.IsNullOrWhiteSpace(sender) && Top1Active("tavu_agent") != null)
                _report.Warn("Mailboxes", "An agent is active and System Settings has no Notification Sender Mailbox, so owner emails are off. Set a mailbox of your Microsoft 365; the gateway's app needs Mail.Send limited to it.");
            else if (!LooksLikeMailbox(sender))
                _report.Warn("Mailboxes", "The Notification Sender Mailbox '" + sender.Trim() + "' is not an email address; owner emails will fail.");
            if (!string.IsNullOrWhiteSpace(intake) && !LooksLikeMailbox(intake))
                _report.Warn("Mailboxes", "The Case Intake Mailbox '" + intake.Trim() + "' is not an email address; the gateway cannot read it.");
        }

        /// <summary>One "@", something before it, a dot after it, and no spaces.</summary>
        private static bool LooksLikeMailbox(string value)
        {
            string v = value.Trim();
            int at = v.IndexOf('@');
            return at > 0 && at == v.LastIndexOf('@') && v.IndexOf('.', at) > at + 1 && !v.EndsWith(".", StringComparison.Ordinal) && v.IndexOf(' ') < 0;
        }

        private void CheckCurrentPeriod()
        {
            DateTime now = DateTime.UtcNow;
            var q = new QueryExpression("tavu_salesperiod") { ColumnSet = new ColumnSet("tavu_name"), TopCount = 1, NoLock = true };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            q.Criteria.AddCondition("tavu_startdate", ConditionOperator.LessEqual, now);
            // The end date is the last day at local midnight, so allow that whole day.
            q.Criteria.AddCondition("tavu_enddate", ConditionOperator.GreaterEqual, now.AddDays(-1));
            if (_svc.RetrieveMultiple(q).Entities.Count == 0)
                _report.Fail("Sales periods", "No active sales period covers today, so forecasting has no current period. Turn on 'Auto-create Sales Periods' in System Settings or create the period in Sales Setup > Sales Periods.");
        }

        // ------------------------------------------------------------------ helpers

        private Entity Top1Active(string entity)
        {
            var q = new QueryExpression(entity) { ColumnSet = new ColumnSet(false), TopCount = 1, NoLock = true };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            EntityCollection r = _svc.RetrieveMultiple(q);
            return r.Entities.Count > 0 ? r.Entities[0] : null;
        }

        private Entity Top1(string entity, ColumnSet columns)
        {
            var q = new QueryExpression(entity) { ColumnSet = columns, TopCount = 1, NoLock = true };
            EntityCollection r = _svc.RetrieveMultiple(q);
            return r.Entities.Count > 0 ? r.Entities[0] : null;
        }

        /// <summary>Current value of an environment variable, falling back to its default value.</summary>
        private string EnvironmentVariable(string schemaName)
        {
            var def = new QueryExpression("environmentvariabledefinition")
            {
                ColumnSet = new ColumnSet("defaultvalue"),
                TopCount = 1,
                NoLock = true
            };
            def.Criteria.AddCondition("schemaname", ConditionOperator.Equal, schemaName);
            EntityCollection defs = _svc.RetrieveMultiple(def);
            if (defs.Entities.Count == 0) return null;

            var val = new QueryExpression("environmentvariablevalue")
            {
                ColumnSet = new ColumnSet("value"),
                TopCount = 1,
                NoLock = true
            };
            val.Criteria.AddCondition("environmentvariabledefinitionid", ConditionOperator.Equal, defs.Entities[0].Id);
            EntityCollection vals = _svc.RetrieveMultiple(val);
            string current = vals.Entities.Count > 0 ? vals.Entities[0].GetAttributeValue<string>("value") : null;
            return !string.IsNullOrEmpty(current) ? current : defs.Entities[0].GetAttributeValue<string>("defaultvalue");
        }
    }
}
