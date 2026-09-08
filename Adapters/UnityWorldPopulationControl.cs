using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BDVM.Domain;
using BDVM.PassengerJobsBridge;
using BDVM.SelfShuntBridge;
using HarmonyLib;

namespace BDVM.Adapters;

public enum WorldPopulationRuntimeState { Disabled, AwaitingCareer, Active, ClientObserver, Refused }

public static class UnityWorldPopulationControl
{
    private static readonly HashSet<string> LoggedDecisions = new HashSet<string>(StringComparer.Ordinal);
    private static WorldPopulationPolicy policy = WorldPopulationPolicy.StrictDefaults();
    private static INetworkRoleDetector? authority;
    private static Action<string>? log;
    private static bool configured;
    private static SelfShuntGeneratorControl? selfShunt;
    private static PassengerJobsGenerationControl? passengerJobs;

    public static WorldPopulationRuntimeState State { get; private set; } = WorldPopulationRuntimeState.Disabled;
    public static string ResultCode { get; private set; } = "population-control-disabled";

    public static void Configure(bool enabled, WorldPopulationPolicy configuredPolicy, INetworkRoleDetector roleDetector, Action<string> logger)
    {
        configured = enabled;
        policy = configuredPolicy ?? WorldPopulationPolicy.StrictDefaults();
        authority = roleDetector ?? throw new ArgumentNullException(nameof(roleDetector));
        log = logger ?? throw new ArgumentNullException(nameof(logger));
        WorldPopulationPolicyEngine.Validate(policy);
        LoggedDecisions.Clear(); selfShunt = null; passengerJobs = null;
        State = enabled ? WorldPopulationRuntimeState.AwaitingCareer : WorldPopulationRuntimeState.Disabled;
        ResultCode = enabled ? "population-control-awaiting-career" : "population-control-disabled";
        Log("bootstrap", WorldPopulationSource.Unknown, enabled ? "configured" : "disabled", ResultCode, policy.Strict.ToString());
    }

    public static void ObserveNewCareer(bool skipTutorial)
    {
        if (!configured) return;
        if (!skipTutorial) { Refuse("strict-population-tutorial-refused"); return; }
        Activate("new-career-non-tutorial");
    }

    public static void ObserveExistingSave(SaveGameData data)
    {
        if (!configured) return;
        var hasBdvmCheckpoint = data?.GetJObject("BDVM")?["SaveGameIntegration"]?.Type == Newtonsoft.Json.Linq.JTokenType.String;
        if (!hasBdvmCheckpoint) { Refuse("strict-population-existing-save-without-bdvm-checkpoint"); return; }
        Activate("existing-bdvm-career");
    }

    public static bool ShouldRun(WorldPopulationSource source, string origin, int existingPhysicalCount = 0)
    {
        if (!configured || State != WorldPopulationRuntimeState.Active) return true;
        var decision = WorldPopulationPolicyEngine.Evaluate(policy, new WorldPopulationRequest
        {
            CorrelationId = "world-population:" + origin,
            Source = source,
            Origin = origin,
            ExistingPhysicalCount = Math.Max(0, existingPhysicalCount)
        });
        var key = source + "|" + origin + "|" + decision.ResultCode;
        if (LoggedDecisions.Add(key)) Log("decision", source, origin, decision.ResultCode, decision.Detail);
        return decision.Decision == WorldPopulationDecisionKind.Allow;
    }

    private static void Activate(string context)
    {
        var reason = "authority unavailable";
        if (authority == null || !NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out reason))
        {
            State = WorldPopulationRuntimeState.ClientObserver; ResultCode = "population-control-client-observer";
            Log("activation", WorldPopulationSource.Unknown, context, ResultCode, reason); return;
        }
        if (!PassengerJobsGenerationControl.TryCreate(out passengerJobs, out var passengerCode)) { Refuse(passengerCode); return; }
        if (!SelfShuntBridgeLocator.TryCreate(out selfShunt, out var selfShuntCode)) { passengerJobs = null; Refuse(selfShuntCode); return; }
        var operation = "bdvm-world-population:" + Guid.NewGuid().ToString("N");
        if (!passengerJobs!.TrySet(operation + ":passengerjobs", true)) { Refuse("passengerjobs-generation-suspension-failed"); return; }
        if (!selfShunt!.TrySetStrictEconomyPolicy(operation + ":selfshunt", true))
        {
            passengerJobs.TrySet(operation + ":passengerjobs-rollback", false);
            Refuse("selfshunt-generation-suspension-failed"); return;
        }
        State = WorldPopulationRuntimeState.Active; ResultCode = "strict-population-control-active";
        Log("activation", WorldPopulationSource.Unknown, context, ResultCode, "vanilla, Multiplayer, SelfShunt and PassengerJobs generators are governed");
    }

    private static void Refuse(string code)
    {
        State = WorldPopulationRuntimeState.Refused; ResultCode = code;
        Log("activation-refused", WorldPopulationSource.Unknown, "career", code, "No vanilla Harmony source is suppressed after refusal.");
    }

    private static void Log(string eventName, WorldPopulationSource source, string origin, string code, string detail) =>
        log?.Invoke("[correlation=world-population] [event=" + eventName + "] source=" + source + ", origin=" + origin + ", result=" + code + ", detail=" + detail);
}

[HarmonyPatch(typeof(StartGameData_NewCareer), "PrepareNewSaveData")]
internal static class BDVMNewCareerPopulationPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Prefix(bool skipTutorial) => UnityWorldPopulationControl.ObserveNewCareer(skipTutorial);
}

[HarmonyPatch(typeof(StartGameData_FromSaveGame), "LoadingNonBlockingCoro")]
internal static class BDVMExistingCareerPopulationPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Prefix(StartGameData_FromSaveGame __instance) => UnityWorldPopulationControl.ObserveExistingSave(__instance.GetSaveGameData());
}

[HarmonyPatch(typeof(StationLocoSpawner), "Update")]
internal static class BDVMNaturalLocomotivePopulationPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix() => UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.NaturalLocomotive, "vanilla:StationLocoSpawner.Update");
}

[HarmonyPatch(typeof(StationProceduralJobsController), nameof(StationProceduralJobsController.TryToGenerateJobs))]
internal static class BDVMVanillaJobPopulationPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix() => UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.ContractProvidedVehicle, "vanilla:StationProceduralJobsController.TryToGenerateJobs");
}

[HarmonyPatch(typeof(SpawnCarsTutorial), nameof(SpawnCarsTutorial.SpawnTutorialCars))]
internal static class BDVMTutorialPopulationPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix() => UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.UnsupportedTutorial, "vanilla:SpawnCarsTutorial.SpawnTutorialCars");
}

[HarmonyPatch(typeof(StartGameData_NewFreeRoam), "SpawnFreeRoamTrain")]
internal static class BDVMFreeRoamPopulationPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix() => UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.UnsupportedTutorial, "vanilla:StartGameData_NewFreeRoam.SpawnFreeRoamTrain");
}

[HarmonyPatch(typeof(StartGameData_FromSaveGame), "BringCabooseToClosestStationAfterJobsGenerate")]
internal static class BDVMAutomaticCaboosePopulationPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(ref IEnumerator __result)
    {
        if (UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.ContractProvidedVehicle, "vanilla:StartGameData_FromSaveGame.BringCabooseToClosestStationAfterJobsGenerate")) return true;
        __result = Empty(); return false;
    }

    private static IEnumerator Empty() { yield break; }
}

[HarmonyPatch(typeof(DV.CommsRadioCarSpawner), "OnUse")]
internal static class BDVMSandboxCarSpawnerPopulationPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix() => UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.Unknown, "vanilla:CommsRadioCarSpawner.OnUse");
}

[HarmonyPatch(typeof(DV.CommsRadioCrewVehicle), "OnUse")]
internal static class BDVMCrewRecoveryPopulationPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix() => UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.RecoveryRequired, "vanilla:CommsRadioCrewVehicle.OnUse");
}

[HarmonyPatch(typeof(CarsSaveManager), "InstantiateCarFromSavegame")]
internal static class BDVMSaveRecoveryPopulationPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix() => UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.RecoveryRequired, "vanilla:CarsSaveManager.InstantiateCarFromSavegame");
}

[HarmonyPatch(typeof(GarageCarSpawner), nameof(GarageCarSpawner.ForceCarsRespawn))]
internal static class BDVMGarageRecoveryPopulationPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix() => UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.RecoveryRequired, "vanilla:GarageCarSpawner.ForceCarsRespawn");
}

[HarmonyPatch(typeof(DV.LocoRestoration.LocoRestorationController), "Start")]
internal static class BDVMLocoRestorationPopulationPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix() => UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.RecoveryRequired, "vanilla:LocoRestorationController.Start");
}

[HarmonyPatch]
internal static class BDVMMultiplayerNaturalLocomotivePatch
{
    private static MethodBase? TargetMethod() => AccessTools.Method("Multiplayer.Patches.World.StationLocoSpawner_Start_Patch:SpawnLocomotives");
    private static bool Prepare() => TargetMethod() != null;
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix() => UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.NaturalLocomotive, "multiplayer:StationLocoSpawner.SpawnLocomotives");
}

[HarmonyPatch]
internal static class BDVMExternalTrafficPopulationPatch
{
    private static MethodBase? TargetMethod() => AccessTools.Method("AITraffic.Fleet.TrainSpawner:SpawnAITrainInternal");
    private static bool Prepare() => TargetMethod() != null;
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix() => UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.ExternalTraffic, "aitraffic:TrainSpawner.SpawnAITrainInternal");
}

[HarmonyPatch]
internal static class BDVMMultiplayerClientSpawnRequestPatch
{
    private static MethodBase? TargetMethod() => AccessTools.Method("Multiplayer.Networking.Managers.Server.NetworkServer:OnServerboundTrainSpawnRequestPacket");
    private static bool Prepare() => TargetMethod() != null;
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix() => UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.Unknown, "multiplayer:NetworkServer.OnServerboundTrainSpawnRequestPacket");
}

[HarmonyPatch]
internal static class BDVMMultiplayerWorkTrainRequestPatch
{
    private static MethodBase? TargetMethod() => AccessTools.Method("Multiplayer.Networking.Managers.Server.NetworkServer:OnServerboundWorkTrainRequestPacket");
    private static bool Prepare() => TargetMethod() != null;
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix() => UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.RecoveryRequired, "multiplayer:NetworkServer.OnServerboundWorkTrainRequestPacket");
}
