using System;
using System.Threading.Tasks;
using BDVM.Domain;
using DV;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BDVM.Adapters;

internal static class DedicatedUnityLink
{
    private static DedicatedWorldClient? client;
    private static Task? pending;
    private static bool loaded;
    private static float nextSend;
    private static string lastStatus = "Starting";
    private static Action<string> log = _ => { };

    public static void Configure(ExternalAuthorityConfiguration configuration, Action<string> logger)
    {
        if (client != null) throw new InvalidOperationException("Dedicated world link is already configured.");
        client = new DedicatedWorldClient(configuration);
        log = logger;
        WorldStreamingInit.LoadingFinished += OnLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        Application.quitting += Stop;
    }

    private static void OnLoaded() => loaded = true;
    private static void OnSceneUnloaded(Scene _) => loaded = false;

    public static void Tick(long tick)
    {
        if (client == null || Time.realtimeSinceStartup < nextSend) return;
        if (pending != null)
        {
            if (!pending.IsCompleted) return;
            var status = pending.IsFaulted || pending.IsCanceled ? "Unavailable; local economic writes remain disabled" : "Telemetry connected; physical command adapter pending";
            // Observe a failure without leaking credentials or producing a log every frame.
            _ = pending.Exception;
            if (status != lastStatus) { lastStatus = status; log("[dedicated-world] " + status); }
            pending = null;
        }
        nextSend = Time.realtimeSinceStartup + 2f;
        // Capture Unity values here; HTTP and JSON execute on a worker.
        var isLoaded = loaded;
        var connection = client;
        pending = Task.Run(() => connection.ObserveAsync(isLoaded, tick));
    }

    private static void Stop()
    {
        WorldStreamingInit.LoadingFinished -= OnLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        Application.quitting -= Stop;
        client?.Dispose();
        pending?.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        client = null;
    }
}
