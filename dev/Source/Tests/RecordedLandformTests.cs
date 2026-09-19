using System;
using System.IO;
using MapGenAI.LLM;
using MapGenAI.MapGen;
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
            string expected=File.ReadAllText(Path.Combine(root,id+"-after.json"));
            Equal(expected,MapStateCodec.Serialize(after));
            Equal(expected,MapStateCodec.Serialize(MapStateCodec.Deserialize(expected)));
            Equal(beforeText,MapStateCodec.Serialize(before));
        }
    }
}
