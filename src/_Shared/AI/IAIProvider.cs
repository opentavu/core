using System;

namespace OpenTavu.Dataverse.AI
{
    /// <summary>
    /// Provider-agnostic abstraction for a single AI chat/completion call.
    /// Modules (e.g., Module 1 - Smart Case Categorization) depend only on this
    /// interface; the concrete provider (AzureOpenAIProvider by default) is resolved
    /// from configuration (tavu_aimodel.Provider). Swapping providers means adding a
    /// new implementation, never touching the consuming module.
    ///
    /// Lives in src\_Shared\AI and is linked into plugin/CWA projects "as link",
    /// the same convention used for _Shared\Common.
    /// </summary>
    public interface IAIProvider
    {
        /// <summary>
        /// Executes one completion and returns the model's text output.
        /// Implementations must never throw for an AI/transport failure: they set
        /// Success=false and ErrorMessage, so the calling module can degrade
        /// gracefully (e.g., route the case to Manual Review) instead of crashing.
        /// </summary>
        AICompletionResult Complete(AICompletionRequest request);
    }

    /// <summary>
    /// All inputs needed for one completion. Connection details (endpoint, key, etc.)
    /// are passed in by the caller after resolving tavu_aimodel + the secret, so the
    /// provider stays a thin, stateless transport.
    /// </summary>
    public sealed class AICompletionRequest
    {
        /// <summary>Provider base URL (e.g., https://opentavu-aoai.openai.azure.com/).</summary>
        public string Endpoint { get; set; }

        /// <summary>Azure deployment name or provider model id (e.g., gpt-4o-mini).</summary>
        public string DeploymentOrModel { get; set; }

        /// <summary>Azure API version (e.g., 2024-10-21). Ignored by non-Azure providers.</summary>
        public string ApiVersion { get; set; }

        /// <summary>Resolved API key (read from a Dataverse environment variable / Key Vault).</summary>
        public string ApiKey { get; set; }

        /// <summary>System/instruction prompt.</summary>
        public string SystemPrompt { get; set; }

        /// <summary>User content (the case text + active taxonomy, etc.).</summary>
        public string UserContent { get; set; }

        /// <summary>Sampling temperature. Low (0.0-0.2) for deterministic tasks like categorization.</summary>
        public double Temperature { get; set; }

        /// <summary>Maximum tokens to generate.</summary>
        public int MaxOutputTokens { get; set; }

        /// <summary>
        /// When true, asks the model to return a strict JSON object
        /// (response_format = json_object). Used by categorization.
        /// </summary>
        public bool JsonResponse { get; set; }

        /// <summary>Request timeout in seconds. Defaults applied by the provider if 0.</summary>
        public int TimeoutSeconds { get; set; }
    }

    /// <summary>
    /// Result of a completion. Never an exception for AI/transport errors —
    /// inspect Success and ErrorMessage.
    /// </summary>
    public sealed class AICompletionResult
    {
        public bool Success { get; set; }

        /// <summary>The model's text output. For categorization this is a JSON string.</summary>
        public string Content { get; set; }

        /// <summary>Populated when Success is false.</summary>
        public string ErrorMessage { get; set; }

        /// <summary>Token usage, for the execution log / budgeting (0 if the provider didn't report it).</summary>
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }

        public static AICompletionResult Ok(string content, int promptTokens, int completionTokens)
        {
            return new AICompletionResult
            {
                Success = true,
                Content = content,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens
            };
        }

        public static AICompletionResult Fail(string error)
        {
            return new AICompletionResult { Success = false, ErrorMessage = error };
        }
    }
}
