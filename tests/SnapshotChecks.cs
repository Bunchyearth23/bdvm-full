using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BDVM.Adapters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

static class SnapshotChecks
{
    public static void Run()
    {
        var owner = Thread.CurrentThread.ManagedThreadId;
        var settings = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() } };
        var live = new List<Dictionary<string, object>> { new Dictionary<string, object> { ["assetIds"] = new List<string> { "wagon-A" } } };
        var source = new { Rows = live.Select(row => { Check(Thread.CurrentThread.ManagedThreadId == owner, "lazy getter escaped owner"); return row; }),
            Null = (string?)null, Kind = DayOfWeek.Friday };
        var expected = JsonConvert.SerializeObject(source, settings);
        var detached = DetachedWebSnapshot.Capture(source, settings);
        ((List<string>)live[0]["assetIds"]).Add("wagon-B"); live.Clear();
        var json = Task.Run(() => JsonConvert.SerializeObject(detached, settings)).GetAwaiter().GetResult();
        Check(JToken.DeepEquals(JToken.Parse(json), JToken.Parse(expected)), "capture must preserve JSON contract and own nested mutable collections");
        var cycle = new List<object>(); cycle.Add(cycle);
        try { DetachedWebSnapshot.Capture(cycle, settings); throw new Exception("cycle accepted"); } catch (InvalidOperationException) { }
        try { DetachedWebSnapshot.Capture(new Unapproved(), settings); throw new Exception("live type accepted"); } catch (InvalidOperationException) { }

        using var release = new ManualResetEventSlim();
        Func<object, string> format = value => { Check(Thread.CurrentThread.ManagedThreadId != owner, "format must leave owner thread"); release.Wait(); return (string)value; };
        Func<object> capture = () => { Check(Thread.CurrentThread.ManagedThreadId == owner, "capture must stay on owner thread"); return "ok"; };
        var first = SnapshotWorker.Run(capture, format);
        var second = SnapshotWorker.Run(capture, format);
        try { SnapshotWorker.Run(() => throw new Exception("overflow must refuse before capture"), format); throw new Exception("worker limit ignored"); }
        catch (InvalidOperationException) { }
        finally { release.Set(); }
        Task.WaitAll(first, second);
        Check(SnapshotWorker.Run(capture, value => (string)value).GetAwaiter().GetResult() == "ok", "slots must be reusable");
        try { SnapshotWorker.Run(() => throw new InvalidOperationException("capture failed"), format); } catch (InvalidOperationException) { }
        Check(SnapshotWorker.Run(capture, value => (string)value).GetAwaiter().GetResult() == "ok", "failed capture must release its slot");
        Console.WriteLine("PASS snapshot: detached lazy/nested data, JSON parity, cycle/live-type rejection, owner/worker boundary and bounded worker admission.");
    }
    private sealed class Unapproved { public int Dangerous => throw new Exception("must reject before reading live properties"); }
    static void Check(bool condition, string detail) { if (!condition) throw new Exception(detail); }
}
