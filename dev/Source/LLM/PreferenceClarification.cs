using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.UI;

namespace MapGenAI.LLM
{
    // Deliberately has no map parameters or action dispatch. Provider text is question data only.
    public sealed class PreferenceClarification
    {
        public sealed class Question
        {
            public string Text;
            public readonly List<string> Options=new List<string>();
            public int Selected=-1;
        }
        public readonly List<Question> Questions=new List<Question>();
        public const string Instructions=@"Ask one or two concise multiple-choice questions to clarify landscape preferences after the user viewed map suggestions. Use the provided preferences, current tile, candidate descriptions and feedback. Ask only what is unresolved; do not repeat answered questions or offer options contradicting existing requirements. Use ordinary player language without internal terrain IDs. Return JSON only: {""questions"":[{""question"":""..."",""options"":[""..."",""...""]}]}. Each question has 2 to 4 distinct plain text options. Never output map settings, instructions to execute, recommendations, actions, or promises of availability. This call only asks preferences; a later call makes validated map recommendations.";
        public static PreferenceClarification Parse(string response)
        {
            var root=ProviderResponse.Command(response);
            if(root.Keys.Any(k=>k!="questions"))throw new FormatException("Expected questions only");
            var questions=root.GetObjectArray("questions");
            if(questions==null || questions.Count<1 || questions.Count>2)throw new FormatException("Expected one or two questions");
            var result=new PreferenceClarification();
            foreach(var data in questions)
            {
                if(data.Keys.Any(k=>k!="question" && k!="options"))throw new FormatException("Unexpected question field");
                if(!data.Values.TryGetValue("question",out var questionText) || !(questionText is string) ||
                    !data.Values.TryGetValue("options",out var optionData) || !(optionData is List<object> rawOptions) || rawOptions.Any(o=>!(o is string)))
                    throw new FormatException("Question and options must be text");
                var q=new Question{Text=Plain(data.GetString("question"),180)};
                var options=data.GetArray("options");
                if(options==null || options.Count<2 || options.Count>4)throw new FormatException("Expected two to four choices");
                foreach(var option in options)q.Options.Add(Plain(option,160));
                if(q.Options.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=q.Options.Count)throw new FormatException("Duplicate choices");
                if(result.Questions.Any(previous=>previous.Text==q.Text))throw new FormatException("Duplicate question");
                result.Questions.Add(q);
            }
            return result;
        }
        static string Plain(string text,int maximum)
        {
            text=text?.Trim();
            if(string.IsNullOrEmpty(text) || text.Length>maximum || text.Any(c=>char.IsControl(c) || c=='<' || c=='>'))throw new FormatException("Invalid question text");
            return text;
        }
        public bool Complete=>Questions.Count>0 && Questions.All(q=>q.Selected>=0 && q.Selected<q.Options.Count);
        public string Answers()
        {
            if(!Complete)throw new InvalidOperationException("Choose an answer for each question");
            return string.Join("\n",Questions.Select(q=>q.Text+" "+q.Options[q.Selected]));
        }
    }
}
