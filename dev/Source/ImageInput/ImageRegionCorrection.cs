using System;
using System.Collections.Generic;
using MapGenAI.LLM;

namespace MapGenAI.ImageInput
{
    public static class ImageRegionCorrection
    {
        public const string Prompt = @"The user selected a connected region in an editable RimWorld terrain plan. You may change only its terrain label. Preserve all other regions. Reply with one JSON object: {""action"":""relabel"",""label"":""soil""} or {""action"":""ask"",""message"":""explain limitation or ask a question""}. Allowed labels: natural,mountain,water,shallow_water,soil,rich_soil,sand,marsh,mud,ice. If asked to move, reshape, resize, add buildings, or add forests, use ask and explain those edits are not supported by this region tool. Never claim those changes happened. Reply in the user's language.";
        public static ImageMapData Apply(ImageMapData map,IEnumerable<int> selection,string response,out string message)
        {
            var root=ProviderResponse.Command(response);
            if(root.GetString("action")=="ask") {message=root.GetString("message")??"No terrain label was changed.";return map.Clone();}
            if(root.GetString("action")!="relabel") throw new FormatException("Unsupported image region correction");
            char label=ImageMapData.Label(root.GetString("label"));
            message="영역 지형 / Region terrain → "+root.GetString("label");
            return map.Relabel(selection,label);
        }
    }
}
