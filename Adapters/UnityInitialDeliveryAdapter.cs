using System;
using System.Collections.Generic;
using System.Linq;
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

    public UnityInitialDeliveryAdapter(IEnumerable<InitialDeliveryTrackRule> rules, IReadOnlyDictionary<string, double>? requestedStartSpans = null)
    {
        allowedTracks = (rules ?? Array.Empty<InitialDeliveryTrackRule>())
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.TrackId))
            .GroupBy(x => x.TrackId.Trim(), StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Single().Kind, StringComparer.Ordinal);
        this.requestedStartSpans = requestedStartSpans ?? new Dictionary<string, double>(StringComparer.Ordinal);
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
            if (CarSpawner.Instance.AllCars.Any(x => x != null && (x.FrontBogie?.track == track || x.RearBogie?.track == track))) return Refused("delivery-track-occupied");
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
                spawned = CarSpawner.Instance.SpawnCarTypesOnTrackStrict(liveries, track, true, true, startSpan, false, false, false);
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

    public InitialDeliveryPortResult Inspect(string operationId, string trackId, InitialDeliveryTargetKind targetKind, IReadOnlyList<string> definitionIds)
    {
        lock (Gate)
        {
            if (!Completed.TryGetValue(operationId, out var known)) return Unknown("delivery-operation-not-observed-in-this-runtime");
            return InspectKnown(known);
        }
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
