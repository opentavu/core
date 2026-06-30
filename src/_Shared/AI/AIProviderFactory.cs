namespace OpenTavu.Dataverse.AI
{
    /// <summary>
    /// Returns the right IAIProvider for a tavu_aimodel.Provider option value.
    /// Adding a new provider = a new IAIProvider implementation + a case here;
    /// the consuming module never changes.
    /// </summary>
    public static class AIProviderFactory
    {
        // tavu_provider option values — VERIFY against the actual choice values.
        public const int ProviderAzureOpenAI = 576600000;
        public const int ProviderOpenAI      = 576600001;
        // public const int ProviderAnthropic = 576600002;
        // public const int ProviderGoogleGemini = 576600003;

        public static IAIProvider Create(int providerValue)
        {
            switch (providerValue)
            {
                case ProviderOpenAI:
                    return new OpenAIProvider();
                case ProviderAzureOpenAI:
                default:
                    return new AzureOpenAIProvider();
            }
        }
    }
}
