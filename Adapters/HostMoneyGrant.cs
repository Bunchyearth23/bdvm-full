using System;

namespace BDVM.Adapters;

// Synchronous Unity-owner orchestration. Persist the domain receipt before paying the native wallet.
internal static class HostMoneyGrant
{
    public const long Amount = 100000;
    public static void Execute(bool allowed, string requestId, Func<string, bool> committed,
        Action reconcile, Func<long> balance, Action<string, long> creditDomain, Action stage)
    {
        if (!allowed) throw new UnauthorizedAccessException("Ce bouton est réservé à l'hôte.");
        if (!Guid.TryParseExact(requestId, "N", out _)) throw new ArgumentException("Invalid grant request identity.");
        var commandId = "host-magic-grant:" + requestId;
        if (!committed(commandId))
        {
            reconcile();
            var target = checked(balance() + Amount);
            if (target > 9007199254740991L) throw new ArgumentException("Wallet balance is too large.");
            creditDomain(commandId, target);
        }
        stage();
        reconcile();
        stage();
    }
}
