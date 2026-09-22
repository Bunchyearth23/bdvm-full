using System;
using System.Threading;
using System.Threading.Tasks;

namespace BDVM.Adapters;

internal static class SnapshotWorker
{
    private static readonly SemaphoreSlim Slots = new SemaphoreSlim(2, 2);
    public static Task<string> Run(Func<object> capture, Func<object, string> format) => Run<string>(capture, format);
    public static Task<T> Run<T>(Func<object> capture, Func<object, T> format)
    {
        if (!Slots.Wait(0)) throw new InvalidOperationException("Snapshot workers are busy; retry shortly.");
        try
        {
            var owned = capture();
            return Task.Run(() => { try { return format(owned); } finally { Slots.Release(); } });
        }
        catch { Slots.Release(); throw; }
    }
}
