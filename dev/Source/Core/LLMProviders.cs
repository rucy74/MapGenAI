namespace MapGenAI
{
    public enum LLMProvider
    {
        Gemini,
        OpenAI,
        DeepSeek,
        Grok,
        GLM,
        GLMCoding,
        AlibabaIntl,
        AlibabaCN,
        OpenRouter,
        Local,
        Custom
    }

    public static class LLMProviderRegistry
    {
        public static readonly LLMProvider[] All =
        {
            LLMProvider.Gemini,
            LLMProvider.OpenAI,
            LLMProvider.DeepSeek,
            LLMProvider.Grok,
            LLMProvider.GLM,
            LLMProvider.GLMCoding,
            LLMProvider.AlibabaIntl,
            LLMProvider.AlibabaCN,
            LLMProvider.OpenRouter,
            LLMProvider.Local,
            LLMProvider.Custom,
        };

        public static string GetLabel(LLMProvider p)
        {
            switch (p)
            {
                case LLMProvider.Gemini:      return "Google";
                case LLMProvider.OpenAI:      return "OpenAI";
                case LLMProvider.DeepSeek:    return "DeepSeek";
                case LLMProvider.Grok:        return "Grok";
                case LLMProvider.GLM:         return "GLM";
                case LLMProvider.GLMCoding:   return "GLM (Coding)";
                case LLMProvider.AlibabaIntl: return "Alibaba (Intl)";
                case LLMProvider.AlibabaCN:   return "Alibaba (CN)";
                case LLMProvider.OpenRouter:  return "OpenRouter";
                case LLMProvider.Local:       return "Local";
                case LLMProvider.Custom:      return "Custom";
                default:                      return "Unknown";
            }
        }

        // OpenAI-compatible base URL (Gemini / Local / Custom 은 별도 처리)
        public static string GetBaseUrl(LLMProvider p)
        {
            switch (p)
            {
                case LLMProvider.OpenAI:      return "https://api.openai.com";
                case LLMProvider.DeepSeek:    return "https://api.deepseek.com";
                case LLMProvider.Grok:        return "https://api.x.ai";
                case LLMProvider.GLM:         return "https://api.z.ai/api/paas";
                case LLMProvider.GLMCoding:   return "https://api.z.ai/api/coding/paas";
                case LLMProvider.AlibabaIntl: return "https://dashscope-intl.aliyuncs.com/compatible-mode";
                case LLMProvider.AlibabaCN:   return "https://dashscope.aliyuncs.com/compatible-mode";
                case LLMProvider.OpenRouter:  return "https://openrouter.ai/api";
                default:                      return "";
            }
        }

        public static string GetDefaultModel(LLMProvider p)
        {
            switch (p)
            {
                case LLMProvider.Gemini:      return "gemini-2.5-flash";
                case LLMProvider.OpenAI:      return "gpt-4o-mini";
                case LLMProvider.DeepSeek:    return "deepseek-chat";
                case LLMProvider.Grok:        return "grok-3-mini";
                case LLMProvider.GLM:         return "glm-4-flash";
                case LLMProvider.GLMCoding:   return "codegeex-4";
                case LLMProvider.AlibabaIntl: return "qwen-plus";
                case LLMProvider.AlibabaCN:   return "qwen-plus";
                case LLMProvider.OpenRouter:  return "openai/gpt-4o-mini";
                case LLMProvider.Local:       return "llama3";
                default:                      return "";
            }
        }
    }
}
