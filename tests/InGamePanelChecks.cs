using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BDVM;
using BDVM.Adapters;
using Newtonsoft.Json.Linq;

internal static class InGamePanelChecks
{
    public static void Run()
    {
        HostMoneyGrantChecks.Run();
        var reads = 0; var world = "one"; var sent = new List<string>();
        TaskCompletionSource<InGameManagementView> capture = NewCapture();
        var panel = new InGameManagementPanel(() => { reads++; return capture.Task; },
            payload => { sent.Add(payload); return "{\"status\":\"pending\"}"; },
            _ => "{\"status\":\"unknown\",\"message\":\"Timed out\"}", () => world);
        panel.Tick(); Check(reads == 0, "hidden interface cannot capture");
        panel.SetActive(true); panel.Tick(); panel.Tick();
        Check(reads == 1, "one capture in flight");
        world = "two"; panel.Tick();
        panel.SetActive(false); capture.SetResult(View("old"));
        Pump(panel, () => !(bool)Field(panel,"busy")!);
        Check(Field(panel,"view") == null, "old generation cannot publish after world change");
        capture = NewCapture(); panel.SetActive(true); panel.Tick(); capture.SetResult(View("new"));
        Pump(panel, () => Field(panel,"view") != null);
        Check(((InGameManagementView)Field(panel,"view")!).WalletSummary == "new", "new world owns its view");
        panel.Draw();
        Check(!UnityEngine.GUILayout.Text.Exists(text => text.Contains("diagnostic") || text.Contains("destructive") || text.Contains("SaveGameData")), "player screen has no debug controls");
        Check(!UnityEngine.GUILayout.Text.Contains("Magique : +100 000 $"), "Remote view exposes host money button");
        var displayed = (InGameManagementView)Field(panel,"view")!;
        displayed.CanGrantHostMoney = true;
        UnityEngine.GUILayout.Click = "Magique : +100 000 $";
        panel.Draw();
        Check(sent.Count == 1 && JObject.Parse(sent[0])["action"]!.ToString() == "host.grant-money", "Host button did not send the local money action");
        Check(Guid.TryParseExact(JObject.Parse(sent[0])["correlationId"]!.ToString(), "N", out _), "Host click lacks stable request identity");
        Invoke(panel,"AcceptResult","{\"status\":\"succeeded\"}"); sent.Clear();
        var before = reads; panel.SetActive(false); panel.Tick(); panel.Tick();
        Check(reads == before, "closing stops captures");

        var command = new JObject { ["action"]="market.purchase", ["listingId"]="offer", ["correlationId"]="stable" };
        Invoke(panel,"Send",command); panel.SetActive(true); panel.Tick();
        Check((bool)Field(panel,"uncertain")!, "timeout keeps unresolved receipt");
        Invoke(panel,"Send",(JObject)Field(panel,"pending")!);
        Check(sent.Count == 2 && sent[0] == sent[1], "retry keeps the exact command identity and payload");
        Invoke(panel,"AcceptResult","{\"status\":\"succeeded\",\"result\":{\"state\":\"PlacementPending\"}}");
        Check(Field(panel,"pending") == null && panel.Status.Contains("pending"), "accepted placement is not reported physically complete");
        Invoke(panel,"Send",command);
        world="three"; panel.Tick();
        Check(Field(panel,"pending") == null, "world change drops prior-world command controls");

        var ready = new InGameManagementPanel(() => Task.FromResult(View("cached")), _ => "{}", _ => null, () => "same");
        ready.SetActive(true); ready.Tick(); ready.SetActive(false);
        Pump(ready, () => !(bool)Field(ready,"busy")!);
        Check(Field(ready,"view") == null, "closed view rejects late publication");
        Console.WriteLine("PASS in-game controller: closed/in-flight capture, world generation, pending timeout, identical retry and physical pending outcome.");
    }
    private static TaskCompletionSource<InGameManagementView> NewCapture() => new TaskCompletionSource<InGameManagementView>(TaskCreationOptions.RunContinuationsAsynchronously);
    private static InGameManagementView View(string label) { var view = new InGameManagementView { WalletSummary=label, Areas=new[]{"wallets"} }; view.Rows["wallets"]=new[]{label}; return view; }
    private static object? Field(object target,string name) => target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target);
    private static void Invoke(object target,string name,object argument) => target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,new[]{argument});
    private static void Check(bool result,string message) { if(!result) throw new Exception(message); }
    private static void Pump(InGameManagementPanel panel,Func<bool> done)
    {
        var until=DateTime.UtcNow.AddSeconds(5);
        while(!done() && DateTime.UtcNow < until) { Thread.Sleep(5); panel.Tick(); }
        Check(done(),"controller completion timed out");
    }
}