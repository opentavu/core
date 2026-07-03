using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OpenTavu.Dataverse.AI
{
    /// <summary>
    /// Resolves, at runtime, the AI configuration for a given task:
    ///   tavu_aitaskconfig (by Task Key) -> tavu_aimodel -> the API key (env variable),
    /// with fallback to tavu_systemsettings defaults. Reusable by every AI module.
    ///
    /// Sandbox-safe: only Microsoft.Xrm.Sdk. Lives in _Shared/AI and is linked into
    /// plugin projects "as link".
    /// </summary>
    public static class AIConfigResolver
    {
        // =====================================================================
        // SCHEMA CONSTANTS — VERIFY each against the actual column logical names
        // (maker portal: table > column > "Schema name"). Adjust if they differ.
        // =====================================================================

        // tavu_systemsettings (singleton)
        private const string SettingsEntity            = "tavu_systemsettings";
        private const string SettingsAiEnabled         = "tavu_aienabled";
        private const string SettingsDefaultModel      = "tavu_defaultaimodel";
        private const string SettingsDefaultThreshold  = "tavu_defaultconfidencethreshold";

        // Gateway mode: environment variables holding the gateway base URL + per-tenant key.
        // When both are present, AI inference is routed through the gateway (no AI key in the tenant).
        private const string GatewayUrlVar = "tavu_GatewayUrl";
        private const string GatewayKeyVar = "tavu_GatewayKey";

        // tavu_aitaskconfig
        private const string TaskEntity        = "tavu_aitaskconfiguration";
        private const string TaskKey           = "tavu_taskkey";            // OptionSet
        private const string TaskModel         = "tavu_model";              // Lookup -> tavu_aimodel
        private const string TaskTemperature   = "tavu_temperature";        // Decimal
        private const string TaskMaxTokens     = "tavu_maxoutputtokens";    // Whole Number
        private const string TaskSystemPrompt  = "tavu_systemprompt";       // Multiline (plain)
        private const string TaskThreshold     = "tavu_confidencethreshold";// Decimal
        private const string TaskTokenBudget   = "tavu_tokenbudget";        // Whole Number

        // tavu_aimodel
        private const string ModelEntity     = "tavu_aimodel";
        private const string ModelProvider   = "tavu_provider";        // OptionSet
        private const string ModelDeployment = "tavu_deploymentmodelid";  // Text
        private const string ModelEndpoint   = "tavu_endpoint";        // Text
        private const string ModelApiVersion = "tavu_apiversion";      // Text
        private const string ModelSecretName = "tavu_secretname";      // Text (env-var schema name)

        private const int StateActive = 0;

        /// <summary>
        /// Builds the resolved config for a task. Never throws for config gaps —
        /// returns a result whose Usable flag is false with a Reason instead, so the
        /// caller can route to Manual Review.
        /// </summary>
        public static AIResolvedConfig Resolve(IOrganizationService service, int taskKeyOptionValue)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));

            var cfg = new AIResolvedConfig();

            // --- System Settings (singleton): kill switch + defaults ---
            Entity settings = RetrieveTop1(service, SettingsEntity, null,
                new ColumnSet(SettingsAiEnabled, SettingsDefaultModel, SettingsDefaultThreshold));

            cfg.AiEnabled = settings != null && settings.GetAttributeValue<bool>(SettingsAiEnabled);
            if (!cfg.AiEnabled)
            {
                cfg.Reason = "AI is disabled in System Settings (AI Enabled = No), or no settings record exists.";
                return cfg;
            }

            decimal defaultThreshold = settings.GetAttributeValue<decimal>(SettingsDefaultThreshold);
            EntityReference defaultModelRef = settings.GetAttributeValue<EntityReference>(SettingsDefaultModel);

            // --- Task config (by Task Key) ---
            var taskFilter = new FilterExpression();
            taskFilter.AddCondition(TaskKey, ConditionOperator.Equal, taskKeyOptionValue);
            taskFilter.AddCondition("statecode", ConditionOperator.Equal, StateActive);

            // One active config per Task Key is the intended design. As a safety
            // net, if more than one is active we deterministically take the most
            // recently modified (instead of an arbitrary "top 1").
            var taskQuery = new QueryExpression(TaskEntity)
            {
                ColumnSet = new ColumnSet(TaskModel, TaskTemperature, TaskMaxTokens,
                                          TaskSystemPrompt, TaskThreshold, TaskTokenBudget),
                Criteria = taskFilter,
                TopCount = 1,
                NoLock = true
            };
            taskQuery.AddOrder("modifiedon", OrderType.Descending);
            EntityCollection taskResult = service.RetrieveMultiple(taskQuery);
            Entity task = taskResult.Entities.Count > 0 ? taskResult.Entities[0] : null;

            if (task == null)
            {
                cfg.Reason = "No active tavu_aitaskconfig found for task key " + taskKeyOptionValue + ".";
                return cfg;
            }

            cfg.TaskConfigId = task.Id;
            cfg.SystemPrompt = task.GetAttributeValue<string>(TaskSystemPrompt);
            cfg.Temperature = (double)task.GetAttributeValue<decimal>(TaskTemperature);
            cfg.MaxOutputTokens = task.GetAttributeValue<int>(TaskMaxTokens);
            cfg.TokenBudget = task.GetAttributeValue<int>(TaskTokenBudget);

            // Threshold: task override if present, else the global default.
            cfg.ConfidenceThreshold = task.Contains(TaskThreshold)
                ? task.GetAttributeValue<decimal>(TaskThreshold)
                : defaultThreshold;

            // --- Gateway mode ---
            // If a gateway URL + key are configured (env variables), route AI inference through
            // the gateway. The tenant then needs NO model endpoint/key/deployment: the gateway
            // holds the AI provider keys and does the model call. This is the target architecture.
            string gatewayUrl = ReadEnvironmentVariable(service, GatewayUrlVar);
            string gatewayKey = ReadEnvironmentVariable(service, GatewayKeyVar);
            if (!string.IsNullOrEmpty(gatewayUrl) && !string.IsNullOrEmpty(gatewayKey))
            {
                cfg.UseGateway = true;
                cfg.GatewayUrl = gatewayUrl;
                cfg.GatewayKey = gatewayKey;
                cfg.Found = true;
                return cfg;
            }

            // --- Direct mode: resolve model: task's model, else the system default ---
            EntityReference modelRef = task.GetAttributeValue<EntityReference>(TaskModel) ?? defaultModelRef;
            if (modelRef == null)
            {
                cfg.Reason = "No model on the task config and no Default AI Model in System Settings.";
                return cfg;
            }

            Entity model = service.Retrieve(ModelEntity, modelRef.Id,
                new ColumnSet(ModelProvider, ModelDeployment, ModelEndpoint, ModelApiVersion, ModelSecretName));

            cfg.ModelId = model.Id;
            cfg.ProviderValue = model.GetAttributeValue<OptionSetValue>(ModelProvider)?.Value ?? 0;
            cfg.DeploymentOrModel = model.GetAttributeValue<string>(ModelDeployment);
            cfg.Endpoint = model.GetAttributeValue<string>(ModelEndpoint);
            cfg.ApiVersion = model.GetAttributeValue<string>(ModelApiVersion);

            // --- Secret (API key) from a Dataverse environment variable ---
            string secretName = model.GetAttributeValue<string>(ModelSecretName);
            cfg.ApiKey = ReadEnvironmentVariable(service, secretName);

            if (string.IsNullOrEmpty(cfg.Endpoint) || string.IsNullOrEmpty(cfg.DeploymentOrModel))
                cfg.Reason = "Model is missing Endpoint or Deployment/Model ID.";
            else if (string.IsNullOrEmpty(cfg.ApiKey))
                cfg.Reason = "API key not found. Check Secret Name '" + secretName + "' and its environment variable value.";
            else
                cfg.Found = true;

            return cfg;
        }

        /// <summary>Builds an AICompletionRequest from a usable resolved config.</summary>
        public static AICompletionRequest ToRequest(AIResolvedConfig cfg, string userContent, bool jsonResponse)
        {
            return new AICompletionRequest
            {
                Endpoint = cfg.Endpoint,
                DeploymentOrModel = cfg.DeploymentOrModel,
                ApiVersion = cfg.ApiVersion,
                ApiKey = cfg.ApiKey,
                SystemPrompt = cfg.SystemPrompt,
                UserContent = userContent,
                Temperature = cfg.Temperature,
                MaxOutputTokens = cfg.MaxOutputTokens,
                JsonResponse = jsonResponse
            };
        }

        // ----- helpers -----

        private static Entity RetrieveTop1(IOrganizationService service, string entity,
                                           FilterExpression filter, ColumnSet columns)
        {
            var query = new QueryExpression(entity)
            {
                ColumnSet = columns,
                TopCount = 1,
                NoLock = true
            };
            if (filter != null) query.Criteria = filter;

            EntityCollection result = service.RetrieveMultiple(query);
            return result.Entities.Count > 0 ? result.Entities[0] : null;
        }

        /// <summary>
        /// Reads a Dataverse environment variable value by schema name
        /// (current value, falling back to the default value).
        /// </summary>
        private static string ReadEnvironmentVariable(IOrganizationService service, string schemaName)
        {
            if (string.IsNullOrEmpty(schemaName)) return null;

            var defFilter = new FilterExpression();
            defFilter.AddCondition("schemaname", ConditionOperator.Equal, schemaName);

            Entity def = RetrieveTop1(service, "environmentvariabledefinition", defFilter,
                new ColumnSet("environmentvariabledefinitionid", "defaultvalue"));
            if (def == null) return null;

            string defaultValue = def.GetAttributeValue<string>("defaultvalue");

            var valFilter = new FilterExpression();
            valFilter.AddCondition("environmentvariabledefinitionid", ConditionOperator.Equal, def.Id);

            Entity val = RetrieveTop1(service, "environmentvariablevalue", valFilter,
                new ColumnSet("value"));

            string current = val?.GetAttributeValue<string>("value");
            return !string.IsNullOrEmpty(current) ? current : defaultValue;
        }
    }

    /// <summary>Resolved AI configuration for one task.</summary>
    public sealed class AIResolvedConfig
    {
        public bool AiEnabled { get; set; }
        public bool Found { get; set; }
        public string Reason { get; set; }

        // Gateway mode (AI inference routed through the OpenTavu gateway; no key in the tenant).
        public bool UseGateway { get; set; }
        public string GatewayUrl { get; set; }
        public string GatewayKey { get; set; }

        public string Endpoint { get; set; }
        public string DeploymentOrModel { get; set; }
        public string ApiVersion { get; set; }
        public string ApiKey { get; set; }
        public int ProviderValue { get; set; }

        public double Temperature { get; set; }
        public int MaxOutputTokens { get; set; }
        public string SystemPrompt { get; set; }
        public decimal ConfidenceThreshold { get; set; }
        public int TokenBudget { get; set; }

        public Guid ModelId { get; set; }
        public Guid TaskConfigId { get; set; }

        /// <summary>True only when AI is enabled and a complete, usable config was resolved.</summary>
        public bool Usable
        {
            get
            {
                if (!AiEnabled || !Found) return false;
                if (UseGateway)
                    return !string.IsNullOrEmpty(GatewayUrl) && !string.IsNullOrEmpty(GatewayKey);
                return !string.IsNullOrEmpty(Endpoint)
                    && !string.IsNullOrEmpty(DeploymentOrModel)
                    && !string.IsNullOrEmpty(ApiKey);
            }
        }
    }
}
