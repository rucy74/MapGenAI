using System;
using System.Collections.Generic;
using System.IO;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;
using static CoreRegressionTests;

static class RecordedLandformTests
{
    public static void RunAll()
    {
        // Frozen real provider replies and independently reviewed states: clobbering an
        // unrelated field, mutating the input snapshot, or losing a saved field fails.
        Check("Recorded basin resize move exit and coverage edits preserve reviewed states",()=>Replay("ko","basin",7));
        Check("Recorded basic mountains material contour and deletion edits preserve reviewed states",()=>Replay("basic","basic",6));
        Check("Recorded independent basin selection and deletion preserve the other basin",()=>Replay("two","two",3));
    }

    static void Replay(string folder,string prefix,int count)
    {
        string root=Path.Combine(AppContext.BaseDirectory,"landform-fixtures",folder);
        for(int i=1;i<=count;i++)
        {
            string id=prefix+"-"+i.ToString("00");
            string beforeText=File.ReadAllText(Path.Combine(root,id+"-before.json"));
            var before=MapStateCodec.Deserialize(beforeText);
            var response=ProviderResponse.Command(File.ReadAllText(Path.Combine(root,id+"-response.json")));
            var after=MapStateEditor.Merge(before,MapParameterParser.Parse(response.GetObject("params")));
            MapStateValidation.Validate(after);
            string expected=NormalizeOptionalRoads(File.ReadAllText(Path.Combine(root,id+"-after.json")));
            Equal(expected,MapStateCodec.Serialize(after));
            Equal(expected,MapStateCodec.Serialize(MapStateCodec.Deserialize(expected)));
            Equal(NormalizeOptionalRoads(beforeText),MapStateCodec.Serialize(before));
        }
    }

    // The recorded files predate local roads. Add only this new optional default to
    // the comparison, keeping every original field/value (even unknown ones) intact.
    // Normalizing the entire expected snapshot would hide an accidentally dropped field.
    static string NormalizeOptionalRoads(string text)
    {
        var stored=SimpleJson.Parse(text);var state=stored.GetObject("state");
        if(!state.ContainsKey("localRoads") || state.IsNull("localRoads"))
        {
            var normalized=SimpleJson.Parse(MapStateCodec.Serialize(MapStateCodec.Deserialize(text))).GetObject("state");
            state.Values["localRoads"]=normalized.Values["localRoads"];
        }
        // Only the new optional fields may be added to old fixtures. Preserve every
        // recorded value and unknown field instead of reserializing expectations via production.
        if(state.GetObjectArray("elevationShapes") is List<SimpleJsonObject> shapes)
        {
            var entries=new List<object>();
            foreach(var shape in shapes)
            {
                foreach(string key in new[]{"landform","variant","opening","layout","details"})if(!shape.ContainsKey(key))shape.Values[key]=null;
                entries.Add(new SortedDictionary<string,object>(shape.Values,StringComparer.Ordinal));
            }
            state.Values["elevationShapes"]=entries;
        }
        stored.Values["state"]=new SortedDictionary<string,object>(state.Values,StringComparer.Ordinal);
        return SimpleJson.Serialize(stored);
    }
}
