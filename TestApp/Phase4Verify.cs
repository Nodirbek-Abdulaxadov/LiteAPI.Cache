using LiteAPI.Cache;

public static class Phase4Verify
{
    public static void Run()
    {
        Console.WriteLine("Phase4 verify: start");

        JustCache.Initialize();
        JustCache.ClearAll();

        VerifyJsonPath();
        VerifyIndexAndFind();
        VerifyEval();

        Console.WriteLine("Phase4 verify: OK");
    }

    private static void VerifyJsonPath()
    {
        JustCache.ClearAll();

        JustCache.SetString("j:1", "{\"name\":\"a\",\"age\":10,\"tags\":[\"x\"]}");

        var age = JustCache.JsonGetString("j:1", "$.age");
        if (age != "10")
            throw new Exception($"JSON.GET age mismatch: {age}");

        if (!JustCache.JsonSet("j:1", "$.age", "11"))
            throw new Exception("JSON.SET age failed");

        age = JustCache.JsonGetString("j:1", "$.age");
        if (age != "11")
            throw new Exception($"JSON.GET age after set mismatch: {age}");

        if (!JustCache.JsonSet("j:1", "$.name", "\"b\""))
            throw new Exception("JSON.SET name failed");

        var name = JustCache.JsonGetString("j:1", "$.name");
        if (name != "\"b\"")
            throw new Exception($"JSON.GET name mismatch: {name}");

        if (!JustCache.JsonSet("j:1", "$.tags[1]", "\"y\""))
            throw new Exception("JSON.SET tags[1] failed");

        var tag1 = JustCache.JsonGetString("j:1", "$.tags[1]");
        if (tag1 != "\"y\"")
            throw new Exception($"JSON.GET tags[1] mismatch: {tag1}");
    }

    private static void VerifyIndexAndFind()
    {
        JustCache.ClearAll();

        JustCache.SetString("p:1", "{\"age\":5,\"name\":\"a\"}");
        JustCache.SetString("p:2", "{\"age\":15,\"name\":\"b\"}");
        JustCache.SetString("p:3", "{\"age\":20,\"name\":\"c\"}");

        if (!JustCache.CreateNumericIndex("age"))
            throw new Exception("CreateNumericIndex failed");

        var keys = JustCache.FindKeys("age >= 15");
        if (!(keys.Contains("p:2") && keys.Contains("p:3")) || keys.Contains("p:1"))
            throw new Exception($"FindKeys age >= 15 mismatch: [{string.Join(",", keys)}]");

        keys = JustCache.FindKeys("age == 5");
        if (!(keys.Count == 1 && keys[0] == "p:1"))
            throw new Exception($"FindKeys age == 5 mismatch: [{string.Join(",", keys)}]");
    }

    private static void VerifyEval()
    {
        JustCache.ClearAll();

        var ok = JustCache.EvalString("SET e:k1 hello");
        if (ok != "OK")
            throw new Exception($"EVAL SET mismatch: {ok}");

        var v = JustCache.EvalString("GET e:k1");
        if (v != "hello")
            throw new Exception($"EVAL GET mismatch: {v}");

        var del = JustCache.EvalString("DEL e:k1");
        if (del != "1")
            throw new Exception($"EVAL DEL mismatch: {del}");

        var js = JustCache.EvalString("JSON.SET e:j $.a 1");
        if (js != "1")
            throw new Exception($"EVAL JSON.SET mismatch: {js}");

        var jg = JustCache.EvalString("JSON.GET e:j $.a");
        if (jg != "1")
            throw new Exception($"EVAL JSON.GET mismatch: {jg}");
    }
}
