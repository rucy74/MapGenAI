using System;
using System.Text.Json;
using MapGenAI.UI;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "hang")
        {
            SimpleJson.Parse("{\"action\":\"generate\",\"params\":{\"elevation_shapes\":[}");
            Console.WriteLine("RETURNED");
            return;
        }
        var control = SimpleJson.Parse("{\"action\":\"generate\",\"params\":{\"elevation_shapes\":[{\"type\":\"bump\"}]}}");
        Console.WriteLine($"CONTROL action={control.GetString("action")} shapes={control.GetObject("params").GetObjectArray("elevation_shapes").Count}");
        var empty = SimpleJson.Parse("{\"elevation_shapes\":[]}");
        Console.WriteLine($"EMPTY_ARRAY objectArrayNull={empty.GetObjectArray("elevation_shapes") == null} primitiveArrayCount={empty.GetArray("elevation_shapes").Count}");
        string truncated = "{\"action\":\"generate\",\"params\":{\"hill_amount\":1.2}";
        bool rejected = false;
        try { using var _ = JsonDocument.Parse(truncated); }
        catch (JsonException) { rejected = true; }
        var accepted = SimpleJson.Parse(truncated);
        Console.WriteLine($"TRUNCATED strictRejected={rejected} modAction={accepted.GetString("action")} modHillAmount={accepted.GetObject("params").GetFloat("hill_amount")}");
        string unicode = "{\"message\":\"\\uD55C\\uAE00\"}";
        var unicodeParsed = SimpleJson.Parse(unicode).GetString("message");
        Console.WriteLine($"UNICODE expected=한글 actual={unicodeParsed} equal={unicodeParsed == "한글"}");
    }
}
