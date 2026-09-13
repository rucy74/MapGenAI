using System;
using System.Linq;
using MapGenAI.UI;

namespace MapGenAI.LLM
{
    public static class ProviderResponse
    {
        public static string Gemini(string json)
        {
            var root = SimpleJson.Parse(json);
            var candidates = root.GetObjectArray("candidates");
            if (candidates == null || candidates.Count == 0)
                throw new FormatException("Provider returned no candidate: " + (root.GetObject("promptFeedback")?.GetString("blockReason") ?? "empty response"));
            var candidate = candidates[0];
            var finish = candidate.GetString("finishReason");
            if (finish != null && finish != "STOP") throw new FormatException("Provider stopped before completion: " + finish);
            var parts = candidate.GetObject("content")?.GetObjectArray("parts");
            var text = parts == null ? null : string.Concat(parts.Where(p => !p.GetBool("thought")).Select(p => p.GetString("text") ?? ""));
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Provider returned no text");
            return text;
        }
        public static string OpenAI(string json)
        {
            var choices = SimpleJson.Parse(json).GetObjectArray("choices");
            if (choices == null || choices.Count == 0) throw new FormatException("Provider returned no choices");
            string finish = choices[0].GetString("finish_reason");
            if (finish != null && finish != "stop") throw new FormatException("Provider stopped before completion: " + finish);
            var message = choices[0].GetObject("message");
            if (!string.IsNullOrEmpty(message?.GetString("refusal"))) throw new FormatException("Provider declined the request");
            var text = message?.GetString("content");
            if (text == null)
            {
                var parts = message?.GetObjectArray("content");
                if (parts != null) text = string.Concat(parts.Select(p => p.GetString("text") ?? ""));
            }
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Provider returned no text");
            return text;
        }
        public static SimpleJsonObject Command(string response)
        {
            string text = response?.Trim();
            if (text != null && text.StartsWith("```",StringComparison.Ordinal) && text.EndsWith("```",StringComparison.Ordinal))
            {
                int line = text.IndexOf('\n');
                if (line < 0) throw new FormatException("Incomplete response block");
                string language = text.Substring(3,line-3).Trim();
                if (language != "" && !language.Equals("json",StringComparison.OrdinalIgnoreCase)) throw new FormatException("Expected JSON response block");
                text = text.Substring(line+1,text.Length-line-4).Trim();
            }
            var command=SimpleJson.Parse(text);
            // Observed provider shorthand: only a top-level shape_ops payload can be wrapped.
            // Mixed locations or other root-level settings are ambiguous and must be rejected.
            if(command.GetString("action")=="generate" && command.ContainsKey("shape_ops"))
            {
                if(command.ContainsKey("params") || command.Keys.Any(k=>k!="action" && k!="description" && k!="shape_ops") || command.GetObjectArray("shape_ops")==null)
                    throw new FormatException("Ambiguous top-level terrain edits");
                var parameters=new SimpleJsonObject();parameters.Values["shape_ops"]=command.Values["shape_ops"];
                command.Values.Remove("shape_ops");command.Values["params"]=parameters;
            }
            return command;
        }
    }
}
