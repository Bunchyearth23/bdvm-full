using System;
using System.Collections.Generic;
using DV;
using BDVM.Domain;
using HarmonyLib;

namespace BDVM.Adapters;

/// <summary>Protects only BDVM-owned or contracted cars from vanilla visit-based cleanup.</summary>
public static class UnityAssetCleanupProtection
{
    private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static Func<VehicleAcquisitionSnapshot?>? stateReader;
    private static INetworkRoleDetector? authority;
    private static Action<string>? log;
    private static bool enabled;

    public static void Configure(bool isEnabled, Func<VehicleAcquisitionSnapshot?> reader, INetworkRoleDetector roleDetector, Action<string> logger)
    {
        enabled = isEnabled; stateReader = reader ?? throw new ArgumentNullException(nameof(reader)); authority = roleDetector ?? throw new ArgumentNullException(nameof(roleDetector)); log = logger; Logged.Clear();
    }

    public static void Reset() { enabled = false; stateReader = null; authority = null; log = null; Logged.Clear(); }

    internal static bool ShouldProtect(TrainCar? car)
    {
        if (!enabled || car == null || string.IsNullOrWhiteSpace(car.CarGUID) || stateReader == null || authority == null || !NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out _)) return false;
        try
        {
            var state = stateReader();
            if (state == null || !AssetLifecycleProtectionPolicy.IsProtected(state, car.CarGUID)) return false;
            if (Logged.Add(car.CarGUID)) log?.Invoke("[correlation=asset-lifecycle] [event=asset-cleanup-protected] carGuid=" + car.CarGUID + ", hook=CarVisitChecker.IsRecentlyVisited");
            return true;
        }
        catch (Exception exception)
        {
            log?.Invoke("[correlation=asset-lifecycle] [event=asset-cleanup-protection-refused] " + exception.Message);
            return false;
        }
    }
}

[HarmonyPatch(typeof(CarVisitChecker), nameof(CarVisitChecker.IsRecentlyVisited), MethodType.Getter)]
internal static class BDVMCarVisitCheckerPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(TrainCar ___car, ref bool ___playerIsInCar, ref bool __result)
    {
        if (!UnityAssetCleanupProtection.ShouldProtect(___car)) return true;
        ___playerIsInCar = true;
        __result = true;
        return false;
    }
}
