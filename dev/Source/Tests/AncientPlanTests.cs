using System;
using MapGenAI.MapGen;
using MapGenAI.UI;
using static CoreRegressionTests;

static class AncientPlanTests
{
    public static void RunAll()
    {
        const string request=@"{""structure_ops"":[{""op"":""add"",""structure"":{""id"":""danger"",""kind"":""ancient_danger"",""position"":[0.7,0.7],""width"":18,""height"":16,""spacing"":12}}]}";
        Check("Positioned ancient danger preserves density and survives state roundtrip",()=>
        {
            var before=new TileMapState{dangerDensity=.6f,ruinDensity=.2f};
            var after=MapStateEditor.Merge(before,MapParameterParser.Parse(SimpleJson.Parse(request)));
            Equal(.6f,after.dangerDensity);Equal(.2f,after.ruinDensity);Equal("ancient_danger",after.structures[0].kind);
            Equal(MapStateCodec.Serialize(after),MapStateCodec.Serialize(MapStateCodec.Deserialize(MapStateCodec.Serialize(after))));
            Equal(true,MapStateDescription.Describe(before,after,true).Contains("고대 위협"));
            var removed=MapStateEditor.Merge(after,MapParameterParser.Parse(SimpleJson.Parse(@"{""structure_ops"":[{""op"":""remove"",""id"":""danger""}]}")));
            Equal(0,removed.structures.Count);Equal(.6f,removed.dangerDensity);
        });
        Check("Native ancient danger limits reject unsupported size rotation and count atomically",()=>
        {
            var before=MapStateEditor.Merge(new TileMapState(),MapParameterParser.Parse(SimpleJson.Parse(request)));string saved=MapStateCodec.Serialize(before);
            foreach(string change in new[]{@"""width"":14",@"""height"":21",@"""count"":3",@"""rotation"":90",@"""kind"":""ruin"""})
            {
                Throws(()=>MapStateEditor.Merge(before,MapParameterParser.Parse(SimpleJson.Parse(@"{""structure_ops"":[{""op"":""update"",""id"":""danger"",""changes"":{"+change+"}}]}"))));
                Equal(saved,MapStateCodec.Serialize(before));
            }
            var grown=MapStateEditor.Merge(before,MapParameterParser.Parse(SimpleJson.Parse(@"{""structure_ops"":[{""op"":""update"",""id"":""danger"",""changes"":{""count"":2}}]}")));
            Equal(2,grown.structures[0].count);
        });
        Check("Ancient danger total cap does not change the separate ruin allowance",()=>
        {
            var s=new TileMapState();
            s.structures.Add(new StructurePlan{id="a",kind="ancient_danger",position=new[]{.3f,.3f},width=18,height=18,count=2});
            s.structures.Add(new StructurePlan{id="b",kind="ancient_danger",position=new[]{.7f,.7f},width=18,height=18,count=2});
            s.structures.Add(new StructurePlan{id="c",kind="ruin",position=new[]{.7f,.3f},count=8});
            StructurePlans.Validate(s);
            var copy=s.Clone();copy.structures[2].kind="ancient_danger";copy.structures[2].count=1;copy.structures[2].width=18;copy.structures[2].height=18;
            Throws(()=>StructurePlans.Validate(copy));Equal("ruin",s.structures[2].kind);
        });
    }
}
