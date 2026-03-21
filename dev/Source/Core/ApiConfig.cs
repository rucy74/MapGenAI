using Verse;

namespace MapGenAI
{
    public class ApiConfig : IExposable
    {
        public bool IsEnabled = true;
        public LLMProvider Provider = LLMProvider.Gemini;
        public string ApiKey = "";
        public string SelectedModel = "";
        public string CustomBaseUrl = "";   // Local / Custom 프로바이더용

        public void ExposeData()
        {
            Scribe_Values.Look(ref IsEnabled, "isEnabled", true);
            Scribe_Values.Look(ref Provider, "provider", LLMProvider.Gemini);
            Scribe_Values.Look(ref ApiKey, "apiKey", "");
            Scribe_Values.Look(ref SelectedModel, "selectedModel", "");
            Scribe_Values.Look(ref CustomBaseUrl, "customBaseUrl", "");
        }

        public bool IsValid()
        {
            if (!IsEnabled) return false;
            if (Provider == LLMProvider.Local || Provider == LLMProvider.Custom)
                return !string.IsNullOrWhiteSpace(CustomBaseUrl) && !string.IsNullOrWhiteSpace(SelectedModel);
            return !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(SelectedModel);
        }
    }
}
