using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OpenTavu.Dataverse.Common;
using OpenTavu.Dataverse.AI;

namespace Pl.Case.Categorize
{
    /// <summary>
    /// Module 1 - Smart Case Categorization.
    /// Async, Post-Operation on Create of tavu_case. Reads the case + the firm's
    /// active typification, asks the AI (via IAIProvider) to categorize it, validates
    /// every proposed value against real active config (hallucination guard), writes
    /// the AI fields, and routes by confidence/multi-intent.
    /// Never blocks case creation: any failure degrades to "Manual Review Required".
    /// </summary>
    /// <remarks>
    /// Plugin Registration Tool configuration:
    ///   Message:              Create
    ///   Primary Entity:       tavu_case
    ///   Stage:                40 (Post-operation)
    ///   Execution Mode:       Asynchronous
    ///   Deployment:           Server
    /// </remarks>
    public class Categorize : PluginBase
    {
        // ============================================================
        // SCHEMA CONSTANTS: VERIFY against actual logical/option values
        // ============================================================

        private const string CaseEntity = "tavu_case";

        // Case columns
        private const string CaseTitle        = "tavu_title";
        private const string CaseDescription  = "tavu_description";
        private const string CaseType         = "tavu_type";           // lookup -> tavu_casetype
        private const string CaseBusinessLine = "tavu_businessline";   // lookup -> tavu_businessline
        private const string CaseCategory     = "tavu_category";       // lookup -> tavu_category
        private const string CaseSubcategory  = "tavu_subcategory";    // lookup -> tavu_subcategory
        private const string CasePriority     = "tavu_priority";       // OptionSet
        private const string CasePriorityRsn  = "tavu_priorityreason";
        private const string CaseIsBillable   = "tavu_isbillable";
        private const string CaseAiConfidence = "tavu_aiconfidencescore";
        private const string CaseAiReasoning  = "tavu_aireasoning";
        private const string CaseAiProblem    = "tavu_aiproblem";
        private const string CaseAiImpact     = "tavu_aibusinessimpact";
        private const string CaseAiMissing    = "tavu_aimissinginfo";
        private const string CaseAiSentiment  = "tavu_aisentiment";    // OptionSet
        private const string CaseAiSummary    = "tavu_aisummary";
        private const string CaseIsAutomated  = "tavu_isautomated";
        private const string CaseMultiIntent  = "tavu_multiintentdetected";

        // Taxonomy entities + fields
        private const string CaseTypeEntity = "tavu_casetype";
        private const string BlEntity       = "tavu_businessline";
        private const string CatEntity      = "tavu_category";
        private const string SubEntity      = "tavu_subcategory";
        private const string FieldName      = "tavu_name";
        private const string FieldAiHint    = "tavu_aicategorizationhint";
        private const string CatParentBl    = "tavu_businessline";  // category -> business line
        private const string SubParentCat   = "tavu_category";      // subcategory -> category

        // Task Key option value for "Case Categorization"
        private const int TaskKeyCaseCategorization = 576600000;

        // Operational status is now a lookup to tavu_casestatus. The two AI outcomes are resolved by
        // flags on that table (config-over-code), not by hardcoded statuscode values.
        private const string CaseStatus            = "tavu_status";            // lookup -> tavu_casestatus
        private const string StatusEntity          = "tavu_casestatus";
        private const string StatusIsCategorized   = "tavu_isaicategorized";   // Yes on "Awaiting Assignment" (resolved by flag, not by name)
        private const string StatusIsManualReview  = "tavu_ismanualreview";    // Yes on "Manual Review Required"

		// tavu_priority option values in tavu_case
		private const int PriorityStandard  = 576600000;
        private const int PriorityExpedited = 576600001;
        private const int PriorityCritical  = 576600002;

		// tavu_aisentiment option values in tavu_case
		private const int SentimentCalm       = 576600000;
        private const int SentimentConcerned  = 576600001;
        private const int SentimentFrustrated = 576600002;
        private const int SentimentCritical   = 576600003;
        private const int SentimentUnknown    = 576600004;

        private const int StateActive = 0;

        public Categorize() : base(typeof(Categorize)) { }

        protected override void ExecuteInternal(LocalPluginContext localContext)
        {
            if (localContext == null) throw new ArgumentNullException(nameof(localContext));

            localContext.Trace("Categorize: ExecuteInternal entered.");

            var ctx = localContext.PluginExecutionContext;

            if (!string.Equals(ctx.MessageName, "Create", StringComparison.OrdinalIgnoreCase))
            {
                localContext.Trace("Message is '{0}', not Create. Exiting.", ctx.MessageName);
                return;
            }

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

            CategorizeCase(localContext, target);

            localContext.Trace("Categorize: ExecuteInternal exiting.");
        }

        private void CategorizeCase(LocalPluginContext localContext, Entity target)
        {
            IOrganizationService svc = localContext.SystemService;

            // --- Case text (Create target carries the input attributes) ---
            string title = target.GetAttributeValue<string>(CaseTitle) ?? string.Empty;
            string description = target.GetAttributeValue<string>(CaseDescription) ?? string.Empty;

            // --- Resolve AI config; degrade to Manual Review if unusable ---
            AIResolvedConfig cfg = AIConfigResolver.Resolve(svc, TaskKeyCaseCategorization);
            if (!cfg.Usable)
            {
                localContext.Trace("AI config not usable: {0}. Routing to Manual Review.", cfg.Reason);
                RouteToManualReview(svc, target.Id, "AI not available: " + cfg.Reason);
                return;
            }

            // --- Gather active taxonomy (also used to validate the AI output) ---
            var tax = LoadTaxonomy(localContext, svc);
            string userContent = BuildUserContent(title, description, tax);

            // --- Call the AI provider (gateway if configured, else direct) ---
            IAIProvider provider = cfg.UseGateway
                ? new GatewayProvider(cfg.GatewayUrl, cfg.GatewayKey)
                : AIProviderFactory.Create(cfg.ProviderValue);
            AICompletionResult ai = provider.Complete(
                AIConfigResolver.ToRequest(cfg, userContent, jsonResponse: true));

            if (!ai.Success)
            {
                localContext.Trace("AI call failed: {0}. Routing to Manual Review.", ai.ErrorMessage);
                RouteToManualReview(svc, target.Id, "AI call failed: " + ai.ErrorMessage);
                return;
            }

            CategorizationOutput o = ParseOutput(localContext, ai.Content);
            if (o == null)
            {
                RouteToManualReview(svc, target.Id, "Could not parse AI response.");
                return;
            }

            // --- Build the update with validated values ---
            var update = new Entity(CaseEntity, target.Id);

            // Validate cascade + type by name against active config (hallucination guard).
            SetLookupIfFound(update, CaseType, CaseTypeEntity, tax.CaseTypes, o.Type);
            bool blOk  = SetLookupIfFound(update, CaseBusinessLine, BlEntity, tax.BusinessLines, o.BusinessLine);
            bool catOk = SetLookupIfFound(update, CaseCategory, CatEntity, tax.Categories, o.Category);
            bool subOk = SetLookupIfFound(update, CaseSubcategory, SubEntity, tax.Subcategories, o.Subcategory);

            // AI text fields (drive the AI Assessment panel).
            update[CaseAiSummary]  = Trunc(o.Summary, 500);
            update[CaseAiProblem]  = Trunc(o.Problem, 1000);
            update[CaseAiImpact]   = Trunc(o.BusinessImpact, 500);
            update[CaseAiMissing]  = Trunc(o.MissingInfo, 1000);
            update[CaseAiReasoning] = o.Reasoning;
            update[CasePriorityRsn] = o.PriorityReason;
            // Stored as a whole percentage (0-100) so the field/header reads "90", not "0.90"
            // (parity with the lead module). The routing threshold below stays on the raw 0-1.
            update[CaseAiConfidence] = decimal.Round((decimal)o.Confidence * 100m, 0);
            update[CaseIsBillable]   = o.IsBillable;
            update[CaseIsAutomated]  = true;
            update[CaseMultiIntent]  = o.MultiIntent;

            int? priority = MapPriority(o.Priority);
            if (priority.HasValue) update[CasePriority] = new OptionSetValue(priority.Value);

            int? sentiment = MapSentiment(o.Sentiment);
            if (sentiment.HasValue) update[CaseAiSentiment] = new OptionSetValue(sentiment.Value);

            // --- Decide routing ---
            bool confident = o.Confidence >= (double)cfg.ConfidenceThreshold;
            bool mappingClean = blOk && catOk; // subcategory optional depending on firm depth
            bool autoAssign = confident && !o.MultiIntent && mappingClean;

            Guid statusId = ResolveStatusIdByFlag(svc, autoAssign ? StatusIsCategorized : StatusIsManualReview);
            if (statusId != Guid.Empty)
                update[CaseStatus] = new EntityReference(StatusEntity, statusId);
            else
                localContext.Trace("Could not resolve target status by flag ({0}); leaving status unchanged.",
                    autoAssign ? StatusIsCategorized : StatusIsManualReview);

            localContext.Trace(
                "Categorization done. confidence={0} threshold={1} multiIntent={2} blOk={3} catOk={4} subOk={5} -> {6}",
                o.Confidence, cfg.ConfidenceThreshold, o.MultiIntent, blOk, catOk, subOk,
                autoAssign ? "Awaiting Assignment" : "Manual Review");

            svc.Update(update);
        }

        // ---------- Manual-review fallback ----------

        private void RouteToManualReview(IOrganizationService svc, Guid caseId, string note)
        {
            var update = new Entity(CaseEntity, caseId);
            Guid mr = ResolveStatusIdByFlag(svc, StatusIsManualReview);
            if (mr != Guid.Empty) update[CaseStatus] = new EntityReference(StatusEntity, mr);
            update[CaseIsAutomated] = true;
            update[CaseAiReasoning] = note;
            svc.Update(update);
        }

        /// <summary>Resolves the id of the single active tavu_casestatus row whose given flag is set.</summary>
        private static Guid ResolveStatusIdByFlag(IOrganizationService svc, string flagField)
        {
            var q = new QueryExpression(StatusEntity) { ColumnSet = new ColumnSet(false), NoLock = true, TopCount = 1 };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            q.Criteria.AddCondition(flagField, ConditionOperator.Equal, true);
            var r = svc.RetrieveMultiple(q);
            return r.Entities.Count > 0 ? r.Entities[0].Id : Guid.Empty;
        }

        // ---------- Taxonomy ----------

        private TaxonomyContext LoadTaxonomy(LocalPluginContext localContext, IOrganizationService svc)
        {
            var tax = new TaxonomyContext();

            tax.CaseTypeList = RetrieveActive(svc, CaseTypeEntity, new ColumnSet(FieldName, FieldAiHint));
            tax.BusinessLineList = RetrieveActive(svc, BlEntity, new ColumnSet(FieldName, FieldAiHint));
            tax.CategoryList = RetrieveActive(svc, CatEntity, new ColumnSet(FieldName, FieldAiHint, CatParentBl));
            tax.SubcategoryList = RetrieveActive(svc, SubEntity, new ColumnSet(FieldName, FieldAiHint, SubParentCat));

            tax.CaseTypes = BuildNameMap(tax.CaseTypeList);
            tax.BusinessLines = BuildNameMap(tax.BusinessLineList);
            tax.Categories = BuildNameMap(tax.CategoryList);
            tax.Subcategories = BuildNameMap(tax.SubcategoryList);

            localContext.Trace("Taxonomy loaded: {0} types, {1} BL, {2} cat, {3} sub.",
                tax.CaseTypeList.Count, tax.BusinessLineList.Count,
                tax.CategoryList.Count, tax.SubcategoryList.Count);
            return tax;
        }

        private string BuildUserContent(string title, string description, TaxonomyContext tax)
        {
            var sb = new StringBuilder();
            sb.AppendLine("CASE");
            sb.AppendLine("Title: " + title);
            sb.AppendLine("Description: " + description);
            sb.AppendLine();
            sb.AppendLine("AVAILABLE CASE TYPES (choose exactly one Name):");
            foreach (var e in tax.CaseTypeList)
                sb.AppendLine("- " + Name(e) + HintSuffix(e));
            sb.AppendLine();
            sb.AppendLine("AVAILABLE CLASSIFICATION (Business Line > Category > Subcategory; use exact Names):");
            AppendCascade(sb, tax);
            sb.AppendLine();
            sb.AppendLine("Return ONLY the JSON object described in the system prompt. "
                + "Use exact Names from the lists above; leave a field empty if nothing fits.");
            return sb.ToString();
        }

        private void AppendCascade(StringBuilder sb, TaxonomyContext tax)
        {
            // Index categories by business line, subcategories by category.
            var catsByBl = new Dictionary<Guid, List<Entity>>();
            foreach (var c in tax.CategoryList)
            {
                var bl = c.GetAttributeValue<EntityReference>(CatParentBl);
                if (bl == null) continue;
                if (!catsByBl.ContainsKey(bl.Id)) catsByBl[bl.Id] = new List<Entity>();
                catsByBl[bl.Id].Add(c);
            }
            var subsByCat = new Dictionary<Guid, List<Entity>>();
            foreach (var s in tax.SubcategoryList)
            {
                var cat = s.GetAttributeValue<EntityReference>(SubParentCat);
                if (cat == null) continue;
                if (!subsByCat.ContainsKey(cat.Id)) subsByCat[cat.Id] = new List<Entity>();
                subsByCat[cat.Id].Add(s);
            }

            foreach (var bl in tax.BusinessLineList)
            {
                sb.AppendLine("- " + Name(bl) + HintSuffix(bl));
                List<Entity> cats;
                if (!catsByBl.TryGetValue(bl.Id, out cats)) continue;
                foreach (var c in cats)
                {
                    sb.AppendLine("  - " + Name(c) + HintSuffix(c));
                    List<Entity> subs;
                    if (!subsByCat.TryGetValue(c.Id, out subs)) continue;
                    foreach (var s in subs)
                        sb.AppendLine("    - " + Name(s) + HintSuffix(s));
                }
            }
        }

        // ---------- AI output parsing ----------

        private CategorizationOutput ParseOutput(LocalPluginContext localContext, string content)
        {
            try
            {
                string json = CleanJson(content);
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return (CategorizationOutput)
                        new DataContractJsonSerializer(typeof(CategorizationOutput)).ReadObject(ms);
                }
            }
            catch (Exception ex)
            {
                localContext.Trace("Failed to parse AI JSON: {0}. Raw: {1}", ex.Message, content);
                return null;
            }
        }

        private static string CleanJson(string content)
        {
            if (string.IsNullOrEmpty(content)) return "{}";
            string s = content.Trim();
            if (s.StartsWith("```"))
            {
                int first = s.IndexOf('{');
                int last = s.LastIndexOf('}');
                if (first >= 0 && last > first) s = s.Substring(first, last - first + 1);
            }
            return s;
        }

        // ---------- helpers ----------

        private static List<Entity> RetrieveActive(IOrganizationService svc, string entity, ColumnSet cols)
        {
            var q = new QueryExpression(entity) { ColumnSet = cols, NoLock = true };
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            EntityCollection result = svc.RetrieveMultiple(q);
            return new List<Entity>(result.Entities);
        }

        private static Dictionary<string, Guid> BuildNameMap(List<Entity> records)
        {
            var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in records)
            {
                string n = e.GetAttributeValue<string>(FieldName);
                if (!string.IsNullOrEmpty(n) && !map.ContainsKey(n)) map[n] = e.Id;
            }
            return map;
        }

        /// <summary>Sets the lookup only if the AI-proposed name maps to a real active record.</summary>
        private static bool SetLookupIfFound(Entity update, string field, string targetEntity,
                                             Dictionary<string, Guid> nameMap, string proposedName)
        {
            if (string.IsNullOrEmpty(proposedName)) return false;
            Guid id;
            if (nameMap.TryGetValue(proposedName.Trim(), out id))
            {
                update[field] = new EntityReference(targetEntity, id);
                return true;
            }
            return false;
        }

        private static int? MapPriority(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            switch (p.Trim().ToLowerInvariant())
            {
                case "standard":  return PriorityStandard;
                case "expedited": return PriorityExpedited;
                case "critical":  return PriorityCritical;
                default: return null;
            }
        }

        private static int? MapSentiment(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            switch (s.Trim().ToLowerInvariant())
            {
                case "calm":       return SentimentCalm;
                case "concerned":  return SentimentConcerned;
                case "frustrated": return SentimentFrustrated;
                case "critical":   return SentimentCritical;
                case "unknown":    return SentimentUnknown;
                default: return null;
            }
        }

        private static string Name(Entity e) { return e.GetAttributeValue<string>(FieldName); }

        private static string HintSuffix(Entity e)
        {
            string h = e.GetAttributeValue<string>(FieldAiHint);
            return string.IsNullOrEmpty(h) ? string.Empty : ": " + h;
        }

        private static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max);
        }

        // ---------- in-memory taxonomy ----------

        private sealed class TaxonomyContext
        {
            public List<Entity> CaseTypeList;
            public List<Entity> BusinessLineList;
            public List<Entity> CategoryList;
            public List<Entity> SubcategoryList;

            public Dictionary<string, Guid> CaseTypes;
            public Dictionary<string, Guid> BusinessLines;
            public Dictionary<string, Guid> Categories;
            public Dictionary<string, Guid> Subcategories;
        }

        // ---------- AI output contract ----------

        [DataContract]
        private sealed class CategorizationOutput
        {
            [DataMember(Name = "type")] public string Type { get; set; }
            [DataMember(Name = "businessLine")] public string BusinessLine { get; set; }
            [DataMember(Name = "category")] public string Category { get; set; }
            [DataMember(Name = "subcategory")] public string Subcategory { get; set; }
            [DataMember(Name = "priority")] public string Priority { get; set; }
            [DataMember(Name = "priorityReason")] public string PriorityReason { get; set; }
            [DataMember(Name = "isBillable")] public bool IsBillable { get; set; }
            [DataMember(Name = "sentiment")] public string Sentiment { get; set; }
            [DataMember(Name = "summary")] public string Summary { get; set; }
            [DataMember(Name = "problem")] public string Problem { get; set; }
            [DataMember(Name = "businessImpact")] public string BusinessImpact { get; set; }
            [DataMember(Name = "missingInfo")] public string MissingInfo { get; set; }
            [DataMember(Name = "multiIntent")] public bool MultiIntent { get; set; }
            [DataMember(Name = "confidence")] public double Confidence { get; set; }
            [DataMember(Name = "reasoning")] public string Reasoning { get; set; }
        }
    }
}
