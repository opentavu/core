using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;

namespace Pl.Setup.Initialize
{
    /// <summary>
    /// Custom API <c>tavu_InitializeConfiguration</c>: makes a fresh (or upgraded) OpenTavu environment
    /// usable and tells the administrator what is still missing.
    ///
    /// A managed solution carries tables, plugins and flows but no rows; the engine depends on
    /// configuration rows (case statuses and their flags, SLA matrix, calendar, sales stages, AI task
    /// prompts...). This API seeds them from the embedded opentavu-seed.json, keeps sales periods ahead,
    /// and runs read-only health checks.
    ///
    /// Request parameters (all optional):
    ///   Mode          "full" (default): seed + sales periods + all checks. Used by the System Settings button.
    ///                 "periods": sales periods + the current-period check. Used daily by Fl.Forecast.SnapshotDaily.
    ///                 "diagnose": checks only, writes nothing.
    ///   ExcludePacks  comma-separated optional packs to skip (for example "geography").
    /// Response:
    ///   Summary   readable text (counts, then errors and warnings with what to do).
    ///   Report    the same as JSON: { complete, created, filled, skipped, errors, warnings, items[] }.
    ///   Complete  false when the time budget ran out before the seed finished; call again to continue.
    ///
    /// Idempotent: safe to run after every upgrade. Never overwrites a value the firm set.
    /// Registration: Custom API (unbound action), plugin type Pl.Setup.Initialize.Initialize, no step.
    /// Uses SystemService for all reads and writes: who may run it is controlled by the Custom API's
    /// Execute Privilege (an administrator-only privilege), not by per-table privileges.
    /// </summary>
    public class Initialize : PluginBase
    {
        private const string InMode = "Mode";
        private const string InExcludePacks = "ExcludePacks";
        private const string OutSummary = "Summary";
        private const string OutReport = "Report";
        private const string OutComplete = "Complete";

        private const string ModeFull = "full";
        private const string ModePeriods = "periods";
        private const string ModeDiagnose = "diagnose";

        private const string SeedResource = "OpenTavu.Seed.json";
        private const int FallbackTimeZoneCode = 92; // (UTC) Coordinated Universal Time

        // The sandbox kills a plugin at 2 minutes; stop seeding well before that and report "not finished".
        private static readonly TimeSpan SeedBudget = TimeSpan.FromSeconds(85);

        public Initialize() : base(typeof(Initialize)) { }

        protected override void ExecuteInternal(LocalPluginContext localContext)
        {
            if (localContext == null) throw new ArgumentNullException(nameof(localContext));
            IPluginExecutionContext ctx = localContext.PluginExecutionContext;
            IOrganizationService svc = localContext.SystemService;
            var clock = Stopwatch.StartNew();

            string mode = ReadString(ctx, InMode);
            mode = string.IsNullOrWhiteSpace(mode) ? ModeFull : mode.Trim().ToLowerInvariant();
            if (mode != ModeFull && mode != ModePeriods && mode != ModeDiagnose)
                throw new InvalidPluginExecutionException("Mode must be 'full', 'periods' or 'diagnose'.");

            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string packs = ReadString(ctx, InExcludePacks);
            if (!string.IsNullOrWhiteSpace(packs))
                foreach (string p in packs.Split(',')) if (p.Trim().Length > 0) excluded.Add(p.Trim());

            int languageCode = OrganizationLanguage(svc);
            int timeZoneCode = UserTimeZone(svc, ctx.InitiatingUserId);
            localContext.Trace("Initialize: mode={0}, language={1}, timeZone={2}.", mode, languageCode, timeZoneCode);

            var report = new SetupReport();

            if (mode == ModeFull)
            {
                Dictionary<string, object> seed = LoadSeed();
                new SeedEngine(svc, localContext, report, clock, SeedBudget, languageCode, timeZoneCode, excluded).Run(seed);
            }

            // Sales periods depend on System Settings, which the seed may have just created.
            if (mode != ModeDiagnose && report.Complete)
            {
                // A failed Dataverse call rolls back the whole run, so failures are not caught here.
                new SalesPeriodGenerator(svc, localContext, report, timeZoneCode, languageCode).Run();
            }

            var checks = new Diagnostics(svc, localContext, report);
            if (mode == ModePeriods) checks.RunPeriodsOnly();
            else if (report.Complete) checks.RunAll();

            ctx.OutputParameters[OutSummary] = report.ToSummary();
            ctx.OutputParameters[OutReport] = report.ToJson();
            ctx.OutputParameters[OutComplete] = report.Complete;
            localContext.Trace("Initialize finished in {0} ms. Complete={1}.", clock.ElapsedMilliseconds, report.Complete);
        }

        private static Dictionary<string, object> LoadSeed()
        {
            Assembly asm = typeof(Initialize).Assembly;
            using (Stream s = asm.GetManifestResourceStream(SeedResource))
            {
                if (s == null)
                    throw new InvalidPluginExecutionException("The seed file is not embedded in Pl.Setup.Initialize (resource '" + SeedResource + "'). Rebuild the assembly.");
                using (var reader = new StreamReader(s, Encoding.UTF8))
                {
                    var seed = MiniJson.Parse(reader.ReadToEnd()) as Dictionary<string, object>;
                    if (seed == null) throw new InvalidPluginExecutionException("The embedded seed file is not a JSON object.");
                    return seed;
                }
            }
        }

        private static int OrganizationLanguage(IOrganizationService svc)
        {
            var q = new QueryExpression("organization") { ColumnSet = new ColumnSet("languagecode"), TopCount = 1, NoLock = true };
            EntityCollection r = svc.RetrieveMultiple(q);
            return r.Entities.Count > 0 ? r.Entities[0].GetAttributeValue<int>("languagecode") : 1033;
        }

        private static int UserTimeZone(IOrganizationService svc, Guid userId)
        {
            var q = new QueryExpression("usersettings") { ColumnSet = new ColumnSet("timezonecode"), TopCount = 1, NoLock = true };
            q.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
            EntityCollection r = svc.RetrieveMultiple(q);
            if (r.Entities.Count == 0 || !r.Entities[0].Contains("timezonecode")) return FallbackTimeZoneCode;
            return r.Entities[0].GetAttributeValue<int>("timezonecode");
        }

        private static string ReadString(IPluginExecutionContext ctx, string name)
        {
            return ctx.InputParameters.Contains(name) ? ctx.InputParameters[name] as string : null;
        }
    }
}
