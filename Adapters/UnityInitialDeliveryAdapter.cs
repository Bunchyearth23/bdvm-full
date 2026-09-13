using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BDVM.Adapters;
using BDVM.Domain;
using DV;
using DV.ThingTypes;
using UnityEngine;

namespace BDVM;

internal sealed class UnityInitialDeliveryAdapter : IInitialDeliveryPort
{
    private static readonly object Gate = new object();
    private static readonly Dictionary<string, string[]> Completed = new Dictionary<string, string[]>(StringComparer.Ordinal);
    private readonly Dictionary<string, InitialDeliveryTargetKind> allowedTracks;
    private readonly IReadOnlyDictionary<string, double> requestedStartSpans;
    private readonly IReadOnlyDictionary<string, bool> requestedDirections;
    private readonly VehicleAcquisitionSnapshot? snapshot;
    private static readonly BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly MethodInfo? GetUninitializedSpawnDataMethod = typeof(CarSpawner).GetMethod("GetUninitializedSpawnData", PrivateStatic);
    private static readonly MethodInfo? PopulateSpawnDataMethod = typeof(CarSpawner).GetMethod("PopulateSpawnData", PrivateStatic);

    public UnityInitialDeliveryAdapter(IEnumerable<InitialDeliveryTrackRule> rules, IReadOnlyDictionary<string, double>? requestedStartSpans = null,
        IReadOnlyDictionary<string, bool>? requestedDirections = null, VehicleAcquisitionSnapshot? snapshot = null)
    {
        allowedTracks = (rules ?? Array.Empty<InitialDeliveryTrackRule>())
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.TrackId))
            .GroupBy(x => x.TrackId.Trim(), StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Single().Kind, StringComparer.Ordinal);
        this.requestedStartSpans = requestedStartSpans ?? new Dictionary<string, double>(StringComparer.Ordinal);
        this.requestedDirections = requestedDirections ?? new Dictionary<string, bool>(StringComparer.Ordinal);
        this.snapshot = snapshot;
    }

    public InitialDeliveryPortResult Preflight(string operationId, string trackId, InitialDeliveryTargetKind targetKind, IReadOnlyList<string> definitionIds)
    {
        try
        {
            if (!allowedTracks.TryGetValue(trackId, out var configuredKind) || configuredKind != targetKind)
                return Refused("track-not-configured-as-" + targetKind);
            var track = ResolveTrack(trackId);
            var liveries = ResolveLiveries(definitionIds);
            if (CarSpawner.Instance == null || CarSpawner.Instance.PoolSetupInProgress) return Unknown("car-spawner-not-ready");
            var required = CarSpawner.Instance.GetTotalCarLiveriesLength(liveries, true) + 20f;
            if (track.curve == null || track.curve.length < required) return Refused("delivery-track-too-short");
            return Applied(Array.Empty<string>(), "preflight-approved");
        }
        catch (Exception exception) { return Refused("preflight-" + exception.GetType().Name + ":" + exception.Message); }
    }

    public InitialDeliveryPortResult Place(string operationId, string trackId, InitialDeliveryTargetKind targetKind, IReadOnlyList<string> definitionIds)
    {
        lock (Gate)
        {
            if (!UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.PurchasedDelivery, "bdvm:UnityInitialDeliveryAdapter.Place")) return Refused("world-population-policy-refused");
            if (Completed.TryGetValue(operationId, out var known)) return InspectKnown(known);
            var preflight = Preflight(operationId + ":repeat", trackId, targetKind, definitionIds);
            if (preflight.Outcome != WorldOwnershipOutcome.Applied) return preflight;
            List<TrainCar>? spawned = null;
            try
            {
                var track = ResolveTrack(trackId);
                var liveries = ResolveLiveries(definitionIds);
                var consistLength = CarSpawner.Instance.GetTotalCarLiveriesLength(liveries, true);
                var requested = requestedStartSpans.TryGetValue(trackId, out var span) ? span - consistLength / 2d : 10d;
                var startSpan = Math.Max(5d, Math.Min(requested, track.curve.length - consistLength - 5d));
                var flip = requestedDirections.TryGetValue(trackId, out var withTrackDirection) && !withTrackDirection;
                spawned = CarSpawner.Instance.SpawnCarTypesOnTrackStrict(liveries, track, true, true, startSpan, flip, false, false);
                if (spawned == null || spawned.Count != liveries.Count || spawned.Any(x => x == null || !Guid.TryParse(x.CarGUID, out var guid) || guid == Guid.Empty))
                {
                    if (spawned != null && spawned.Count > 0) CarSpawner.Instance.DeleteTrainCars(spawned, true);
                    return Unknown("spawn-returned-incomplete-component-set");
                }
                var guids = spawned.Select(x => Guid.Parse(x.CarGUID).ToString("D")).ToArray();
                Completed.Add(operationId, guids);
                return Applied(guids, "spawn-confirmed");
            }
            catch (Exception exception)
            {
                return spawned != null && spawned.Count > 0
                    ? Unknown("spawn-exception-after-world-effect:" + exception.GetType().Name)
                    : Refused("spawn-exception-before-world-effect:" + exception.GetType().Name);
            }
        }
    }

    public IEnumerator PlaceCoroutine(string operationId, string trackId, InitialDeliveryTargetKind targetKind,
        IReadOnlyList<string> definitionIds, Action<InitialDeliveryPortResult> completed)
    {
        if (completed == null) throw new ArgumentNullException(nameof(completed));
        InitialDeliveryPortResult? knownResult = null;
        lock (Gate)
        {
            if (Completed.TryGetValue(operationId, out var known)) knownResult = InspectKnown(known);
        }
        if (knownResult != null) { completed(knownResult); yield break; }
        if (!UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.PurchasedDelivery, "bdvm:UnityInitialDeliveryAdapter.PlaceCoroutine"))
        {
            completed(Refused("world-population-policy-refused"));
            yield break;
        }
        var preflight = Preflight(operationId + ":preflight", trackId, targetKind, definitionIds);
        if (preflight.Outcome != WorldOwnershipOutcome.Applied)
        {
            completed(preflight);
            yield break;
        }
        yield return null;

        RailTrack track;
        List<TrainCarLivery> liveries;
        object spawnData;
        string preparationError;
        if (!TryPrepareSpawnData(trackId, definitionIds, out track, out liveries, out spawnData, out preparationError))
        {
            completed(Unknown(preparationError));
            yield break;
        }
        yield return null;

        var carDataField = spawnData.GetType().GetField("carData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var carData = carDataField?.GetValue(spawnData) as Array;
        if (carData == null || carData.Length != liveries.Count)
        {
            completed(Unknown("spawn-data-incomplete-component-set"));
            yield break;
        }
        var spawned = new List<TrainCar>(carData.Length);
        var itemType = carData.GetType().GetElementType()!;
        var prefabField = itemType.GetField("prefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var positionField = itemType.GetField("position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var forwardField = itemType.GetField("forward", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var reversedField = itemType.GetField("orientationReversed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prefabField == null || positionField == null || forwardField == null || reversedField == null)
        {
            completed(Unknown("spawn-async-api-unavailable"));
            yield break;
        }
        var reversed = new bool[carData.Length];
        for (var index = 0; index < carData.Length; index++)
        {
            var item = carData.GetValue(index)!;
            if (!TrySpawnCarFromData(item, prefabField, positionField, forwardField, reversedField, track, out var car, out var orientationReversed))
            {
                completed(Unknown("spawn-returned-incomplete-component-set"));
                yield break;
            }
            spawned.Add(car!);
            reversed[index] = orientationReversed;
            yield return null;
        }

        var last = spawned.Count - 1;
        if (!TryFinalizeConsist(spawned, reversed))
        {
            completed(Unknown("spawn-finalization-failed"));
            yield break;
        }
        yield return null;

        var guids = spawned.Select(x => Guid.Parse(x.CarGUID).ToString("D")).ToArray();
        lock (Gate) Completed[operationId] = guids;
        completed(Applied(guids, "spawn-confirmed-async"));
    }

    private bool TryPrepareSpawnData(string trackId, IReadOnlyList<string> definitionIds, out RailTrack track,
        out List<TrainCarLivery> liveries, out object spawnData, out string error)
    {
        track = null!; liveries = null!; spawnData = null!; error = "spawn-async-preparation-failed";
        try
        {
            track = ResolveTrack(trackId);
            liveries = ResolveLiveries(definitionIds);
            var consistLength = CarSpawner.Instance.GetTotalCarLiveriesLength(liveries, true);
            var requested = requestedStartSpans.TryGetValue(trackId, out var span) ? span - consistLength / 2d : 10d;
            var startSpan = Math.Max(5d, Math.Min(requested, track.curve.length - consistLength - 5d));
            var flip = requestedDirections.TryGetValue(trackId, out var withTrackDirection) && !withTrackDirection;
            spawnData = PrepareSpawnData(liveries, track, startSpan, flip);
            return true;
        }
        catch (Exception exception) { error = "spawn-async-preparation:" + exception.GetType().Name; return false; }
    }

    private static bool TrySpawnCar(GameObject prefab, RailTrack track, Vector3 position, Vector3 forward, out TrainCar? car)
    {
        try
        {
            car = CarSpawner.Instance.SpawnCar(prefab, track, position, forward, false, false);
            return car != null && Guid.TryParse(car.CarGUID, out var guid) && guid != Guid.Empty;
        }
        catch { car = null; return false; }
    }

    private static bool TrySpawnCarFromData(object item, FieldInfo prefabField, FieldInfo positionField, FieldInfo forwardField,
        FieldInfo reversedField, RailTrack track, out TrainCar? car, out bool orientationReversed)
    {
        try
        {
            var prefab = prefabField.GetValue(item) as GameObject;
            if (prefab == null) { car = null; orientationReversed = false; return false; }
            orientationReversed = (bool)reversedField.GetValue(item)!;
            return TrySpawnCar(prefab, track, (Vector3)positionField.GetValue(item)!, (Vector3)forwardField.GetValue(item)!, out car);
        }
        catch { car = null; orientationReversed = false; return false; }
    }

    private static object PrepareSpawnData(List<TrainCarLivery> liveries, RailTrack track, double startSpan, bool flip)
    {
        if (GetUninitializedSpawnDataMethod == null || PopulateSpawnDataMethod == null)
            throw new MissingMethodException(typeof(CarSpawner).FullName, "SpawnData preparation API");
        var orientations = liveries.Select(_ => false).ToList();
        var spawnData = GetUninitializedSpawnDataMethod.Invoke(null, new object[] { liveries, orientations, track, flip });
        if (spawnData == null) throw new InvalidOperationException("CarSpawner returned no spawn data.");
        var args = new object[] { spawnData, startSpan, 0d };
        PopulateSpawnDataMethod.Invoke(null, args);
        return args[0] ?? throw new InvalidOperationException("CarSpawner populated no spawn data.");
    }

    private static void SetPreventAutoCouple(TrainCar car, object coupler)
    {
        if (coupler == null) return;
        var property = coupler.GetType().GetProperty("preventAutoCouple", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanWrite) property.SetValue(coupler, true, null);
        else coupler.GetType().GetField("preventAutoCouple", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(coupler, true);
    }

    private static void ApplyHandbrake(TrainCar car)
    {
        var member = (MemberInfo?)typeof(TrainCar).GetField("brakeSystem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? typeof(TrainCar).GetProperty("brakeSystem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var brakeSystem = member switch
        {
            FieldInfo field => field.GetValue(car),
            PropertyInfo property => property.GetValue(car, null),
            _ => null
        };
        if (brakeSystem == null) return;
        var hasHandbrake = brakeSystem.GetType().GetProperty("hasHandbrake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(brakeSystem, null)
            ?? brakeSystem.GetType().GetField("hasHandbrake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(brakeSystem);
        if (!(hasHandbrake is bool available) || !available) return;
        brakeSystem.GetType().GetMethod("SetHandbrakePosition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.Invoke(brakeSystem, new object[] { 1f, true });
    }

    private static bool TryFinalizeConsist(IReadOnlyList<TrainCar> spawned, IReadOnlyList<bool> reversed)
    {
        try
        {
            var last = spawned.Count - 1;
            SetPreventAutoCouple(spawned[0], reversed[0] ? spawned[0].rearCoupler : spawned[0].frontCoupler);
            SetPreventAutoCouple(spawned[last], reversed[last] ? spawned[last].frontCoupler : spawned[last].rearCoupler);
            ApplyHandbrake(UnityEngine.Random.value < 0.5f ? spawned[0] : spawned[last]);
            return true;
        }
        catch { return false; }
    }

    public InitialDeliveryPortResult Inspect(string operationId, string trackId, InitialDeliveryTargetKind targetKind, IReadOnlyList<string> definitionIds)
    {
        lock (Gate)
        {
            if (Completed.TryGetValue(operationId, out var known)) return InspectKnown(known);
            var persisted = PersistedCarGuids(operationId, trackId, targetKind, definitionIds);
            return persisted == null ? Unknown("delivery-operation-not-observed-in-runtime-or-persisted-state") : InspectKnown(persisted);
        }
    }

    private string[]? PersistedCarGuids(string operationId, string trackId, InitialDeliveryTargetKind targetKind, IReadOnlyList<string> definitionIds)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(operationId)) return null;
        var grant = snapshot.InitialDeliveries.SingleOrDefault(value =>
            !string.IsNullOrWhiteSpace(value.PlacementCommandId) && string.Equals(value.PlacementCommandId + ":spawn", operationId, StringComparison.Ordinal));
        if (grant == null || !string.Equals(grant.TargetTrackId, trackId, StringComparison.Ordinal) || grant.TargetKind != targetKind ||
            !grant.DefinitionIds.SequenceEqual(definitionIds ?? Array.Empty<string>(), StringComparer.Ordinal)) return null;
        var guids = new List<string>();
        foreach (var assetId in grant.AssetIds)
        {
            var asset = snapshot.Assets.Assets.SingleOrDefault(value => string.Equals(value.AssetId, assetId, StringComparison.Ordinal));
            if (asset == null || asset.GameLink.State != PersistentLinkState.Resolved || !Guid.TryParse(asset.GameLink.Value, out var parsed) || parsed == Guid.Empty) return null;
            guids.Add(parsed.ToString("D"));
        }
        return guids.Count == grant.AssetIds.Count && guids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == guids.Count ? guids.ToArray() : null;
    }

    private static InitialDeliveryPortResult InspectKnown(IReadOnlyList<string> guids)
    {
        var visible = new HashSet<string>((CarSpawner.Instance?.AllCars ?? new List<TrainCar>()).Where(x => x != null).Select(x => x.CarGUID), StringComparer.OrdinalIgnoreCase);
        return guids.All(visible.Contains) ? Applied(guids, "all-components-visible") : Unknown("one-or-more-components-not-visible");
    }

    private static RailTrack ResolveTrack(string trackId)
    {
        var matches = UnityEngine.Object.FindObjectsOfType<RailTrack>().Where(x => x != null && string.Equals(x.LogicTrack()?.ID?.ToString(), trackId, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException(matches.Length == 0 ? "delivery-track-not-found" : "delivery-track-identity-ambiguous");
        return matches[0];
    }

    private static List<TrainCarLivery> ResolveLiveries(IReadOnlyList<string> definitionIds)
    {
        if (definitionIds == null || definitionIds.Count == 0) throw new InvalidOperationException("delivery-component-list-empty");
        var result = new List<TrainCarLivery>();
        foreach (var id in definitionIds)
        {
            var runtimeId = string.Equals(id, "CarFlatcar", StringComparison.Ordinal) ? "FlatbedEmpty" : id;
            var matches = Globals.G.Types.Liveries.Where(x => x != null && string.Equals(x.id, runtimeId, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1 || matches[0].prefab == null) throw new InvalidOperationException(matches.Length == 0 ? "livery-not-found:" + id : "livery-identity-ambiguous:" + id);
            result.Add(matches[0]);
        }
        return result;
    }

    private static InitialDeliveryPortResult Applied(IReadOnlyList<string> guids, string detail) => new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Applied, PersistentCarGuids = guids, Detail = detail };
    private static InitialDeliveryPortResult Refused(string detail) => new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.NotApplied, Detail = detail };
    private static InitialDeliveryPortResult Unknown(string detail) => new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Unknown, Detail = detail };
}
