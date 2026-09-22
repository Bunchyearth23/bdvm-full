using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BDVM.Domain;
using DV.CabControls;
using DV.Interaction;
using DV.Logic.Job;
using HarmonyLib;
using MPAPI;
using MPAPI.Interfaces;
using UnityEngine;

namespace BDVM.Adapters;

internal static class UnityCompanyRollingStockAccess
{
    private static AcquisitionRuntimeStateProvider? provider;
    private static INetworkRoleDetector? role;
    private static CompanyRollingStockAccess? index;
    private static VehicleAcquisitionSnapshot? indexedState;
    private static long indexedRevision = -1;
    private static Func<long> generation = () => 0;
    public static void Configure(AcquisitionRuntimeStateProvider state, INetworkRoleDetector detector, Func<long> worldGeneration)
    {
        generation = worldGeneration; provider = state; role = detector; index = null; indexedState = null; indexedRevision = -1;
        RollingStockAccess.SetValidator((actor, guid) => AllowsGuid(ActorId(actor), guid));
    }
    private static string ActorId(IPlayer? actor)
    {
        if (actor == null || actor.IsHost) return provider?.LocalPlayerId ?? "";
        if (!ReferenceEquals(MultiplayerAPI.Server?.GetPlayer(actor.PlayerId), actor)) return "";
        return actor is IPersistentPlayerIdentity persistent && persistent.PersistentId != Guid.Empty
            ? PlayerIdentity.FromMultiplayerGuid(persistent.PersistentId) : "";
    }
    private static bool IsClient => role?.Detect().Role == NetworkRole.MultiplayerClient;
    private static bool AllowsGuid(string actorId, string guid)
    {
        if (IsClient) return true; // Client presentation is never authorization; host checks every action.
        if (provider == null) return true;
        var state = provider.Current;
        if (state == null) { index = null; indexedState = null; return false; }
        var revision = provider.Revision;
        if (index == null || !ReferenceEquals(state, indexedState) || revision != indexedRevision)
        { index = new CompanyRollingStockAccess(state); indexedState = state; indexedRevision = revision; }
        return index.Allows(actorId, guid);
    }
    public static bool AllowsCar(TrainCar? car)
    {
        if (car == null || provider == null || IsClient) return true;
        try
        {
            var actor = ActorId(RollingStockAccess.CurrentActor);
            if (!AllowsVehicleAndConnections(actor, car)) return false;
            if (car.trainset?.cars != null)
                foreach (var other in car.trainset.cars)
                    if (other != null && other != car && !AllowsVehicleAndConnections(actor, other)) return false;
            return true;
        }
        catch { return false; }
    }
    private static bool AllowsVehicleAndConnections(string actor, TrainCar car)
    {
        if (!AllowsGuid(actor, car.CarGUID)) return false;
        var frontHose = car.frontCoupler?.GetAirHoseConnectedTo()?.train;
        var rearHose = car.rearCoupler?.GetAirHoseConnectedTo()?.train;
        var frontMu = car.muModule?.FrontCable?.connectedTo?.muModule?.train;
        var rearMu = car.muModule?.RearCable?.connectedTo?.muModule?.train;
        return (frontHose == null || AllowsGuid(actor, frontHose.CarGUID)) &&
            (rearHose == null || AllowsGuid(actor, rearHose.CarGUID)) &&
            (frontMu == null || AllowsGuid(actor, frontMu.CarGUID)) &&
            (rearMu == null || AllowsGuid(actor, rearMu.CarGUID));
    }
    public static bool AllowsObject(GameObject? target) => target == null || AllowsCar(TrainCar.Resolve(target));
    public static bool AllowsWarehouse(WarehouseMachineController controller)
    {
        if (provider == null || IsClient) return true;
        var track = controller.warehouseMachine?.WarehouseTrack;
        if (track == null) return false;
        var registry = TrainCarRegistry.Instance;
        foreach (var car in track.GetCarsFullyOnTrack().Concat(track.GetCarsPartiallyOnTrack()).Distinct())
            if (registry == null || !registry.logicCarToTrainCar.TryGetValue(car, out var physical) || !AllowsCar(physical)) return false;
        return true;
    }
    public static IEnumerator GuardWarehouse(IEnumerator original, WarehouseMachineController controller, IPlayer? actor) =>
        GuardWarehouseCore(original, controller, actor, generation());
    private static IEnumerator GuardWarehouseCore(IEnumerator original, WarehouseMachineController controller, IPlayer? actor, long worldGeneration)
    {
        try
        {
            while (true)
            {
                object? next;
                using (RollingStockAccess.ForActor(actor!))
                {
                    if (worldGeneration != generation() || controller == null) yield break;
                    if (!AllowsWarehouse(controller))
                    {
                        AccessTools.Field(typeof(WarehouseMachineController), "loadUnloadCoro").SetValue(controller, null);
                        yield break;
                    }
                    if (!original.MoveNext()) yield break;
                    next = original.Current;
                }
                yield return next is IEnumerator nested ? GuardWarehouseCore(nested, controller, actor, worldGeneration) : next;
            }
        }
        finally { (original as IDisposable)?.Dispose(); }
    }
}

[HarmonyPatch(typeof(AGrabHandler), nameof(AGrabHandler.InteractionPassThrough))]
internal static class CompanyGrabTargetPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(AGrabHandler __instance, ref bool __result)
    { if (UnityCompanyRollingStockAccess.AllowsObject(__instance.gameObject)) return true; __result = true; return false; }
}

[HarmonyPatch(typeof(ControlImplBase), nameof(ControlImplBase.InteractionAllowed), MethodType.Getter)]
internal static class CompanyControlAllowedPatch
{
    [HarmonyPostfix]
    private static void Postfix(ControlImplBase __instance, ref bool __result)
    { if (__result) __result = UnityCompanyRollingStockAccess.AllowsObject(__instance.gameObject); }
}

// Feed/Use paths cover already-held controls and VR. End/release is always allowed.
[HarmonyPatch]
internal static class CompanyGrabInputPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] { typeof(AGrabHandler).Assembly, typeof(TrainCar).Assembly }
        .SelectMany(assembly => assembly.GetTypes()).Where(type => typeof(AGrabHandler).IsAssignableFrom(type))
        .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        .Where(method => !method.IsAbstract && (method.Name == "StartInteraction" || method.Name == "FeedPosition" || method.Name == "FeedValue" || method.Name == "Use"));
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(AGrabHandler __instance)
    {
        if (UnityCompanyRollingStockAccess.AllowsObject(__instance.gameObject)) return true;
        __instance.ForceEndInteraction(); return false;
    }
}

[HarmonyPatch(typeof(MouseWheelHoverScroller), "OnScrolled")]
internal static class CompanyHoverScrollPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(MouseWheelHoverScroller __instance) => UnityCompanyRollingStockAccess.AllowsObject(__instance.CurrentItem);
}

[HarmonyPatch(typeof(DV.HUD.UICouplingHelper), "HandleCoupling")]
internal static class CompanyCouplingUiPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(Coupler coupler) => coupler == null ||
        (UnityCompanyRollingStockAccess.AllowsCar(coupler.train) &&
         UnityCompanyRollingStockAccess.AllowsCar(coupler.coupledTo?.train) &&
         UnityCompanyRollingStockAccess.AllowsCar(coupler.GetFirstCouplerInRange()?.train));
}

[HarmonyPatch(typeof(LocomotiveRemoteController), "Transmit")]
internal static class CompanyRemoteControlPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(object ___pairedLocomotive) => ___pairedLocomotive == null ||
        (___pairedLocomotive is Component component && UnityCompanyRollingStockAccess.AllowsObject(component.gameObject));
}

[HarmonyPatch]
internal static class CompanyWarehouseStartPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(WarehouseMachineController), "StartLoadSequence");
        yield return AccessTools.Method(typeof(WarehouseMachineController), "StartUnloadSequence");
    }
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(WarehouseMachineController __instance) => UnityCompanyRollingStockAccess.AllowsWarehouse(__instance);
}

[HarmonyPatch(typeof(WarehouseMachineController), "DelayedLoadUnload")]
internal static class CompanyWarehouseSequencePatch
{
    [HarmonyPostfix]
    private static void Postfix(WarehouseMachineController __instance, ref IEnumerator __result) =>
        __result = UnityCompanyRollingStockAccess.GuardWarehouse(__result, __instance, RollingStockAccess.CurrentActor);
}

[HarmonyPatch]
internal static class CompanyKeyboardDrivePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(InteractablesKeyboardControl), "Update");
        yield return AccessTools.Method(typeof(InteractablesKeyboardControl), "FixedUpdate");
    }
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(InteractablesKeyboardControl __instance) => UnityCompanyRollingStockAccess.AllowsObject(__instance.gameObject);
}

[HarmonyPatch(typeof(DV.HUD.UICouplingHelper), "DoMU")]
internal static class CompanyMuUiPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(Coupler coupler) => coupler == null ||
        (UnityCompanyRollingStockAccess.AllowsCar(coupler.train) && UnityCompanyRollingStockAccess.AllowsCar(coupler.coupledTo?.train));
}

[HarmonyPatch(typeof(DV.HUD.UICouplingHelper), "HandleBrakeHose")]
internal static class CompanyHoseUiPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(bool front, Coupler ___couplerFront, Coupler ___couplerRear)
    {
        var coupler = front ? ___couplerFront : ___couplerRear;
        return coupler == null || (UnityCompanyRollingStockAccess.AllowsCar(coupler.train) &&
            UnityCompanyRollingStockAccess.AllowsCar(coupler.GetAirHoseConnectedTo()?.train) &&
            UnityCompanyRollingStockAccess.AllowsCar(coupler.coupledTo?.train));
    }
}

// Revalidate queued scroll input while preserving the native release timeout.
[HarmonyPatch(typeof(MouseWheelHoverScroller), "FixedUpdate")]
internal static class CompanyQueuedScrollPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Prefix(MouseWheelHoverScroller __instance, ref int ___currentScrollAmount)
    {
        if (___currentScrollAmount != 0 && !UnityCompanyRollingStockAccess.AllowsObject(__instance.CurrentItem))
            ___currentScrollAmount = 0;
    }
}