using System;
using System.Collections.Generic;
using BDVM.Adapters;

internal static class HostMoneyGrantChecks
{
    public static void Run()
    {
        long domain = 50, native = 50; int pays = 0, stageCalls = 0;
        bool failStage = false, failAfterPay = false;
        var receipts = new HashSet<string>();
        var id = Guid.NewGuid().ToString("N");
        Action<bool,string> execute = (allowed, request) => HostMoneyGrant.Execute(allowed, request,
            receipts.Contains,
            () => { if (native != domain) { native = domain; pays++; if (failAfterPay) { failAfterPay = false; throw new InvalidOperationException(); } } },
            () => native,
            (key, target) => { domain = target; receipts.Add(key); },
            () => { stageCalls++; if (failStage) throw new InvalidOperationException(); });
        try { execute(false, id); throw new Exception("Nonhost accepted"); } catch (UnauthorizedAccessException) { }
        Check(domain == 50 && native == 50 && stageCalls == 0, "Nonhost changed state");
        try { execute(true, "invalid"); throw new Exception("Invalid request accepted"); } catch (ArgumentException) { }
        failStage = true;
        try { execute(true, id); } catch (InvalidOperationException) { }
        Check(domain == 100050 && native == 50 && pays == 0, "Native payment occurred before staging");
        failStage = false; failAfterPay = true;
        try { execute(true, id); } catch (InvalidOperationException) { }
        execute(true, id);
        Check(domain == 100050 && native == domain && pays == 1, "Retry paid twice after native effect");
        domain -= 10; native -= 10; execute(true, id);
        Check(native == 100040 && pays == 1, "Old retry erased spending or recredited");
        execute(true, Guid.NewGuid().ToString("N"));
        Check(native == 200040 && pays == 2, "New click did not pay once");
        Console.WriteLine("PASS host magic money: authority, identity, stage failure, post-payment failure, retry and subsequent spending.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
