using System;
using System.Collections.Generic;
using System.Linq;
using BDVM.Domain;
using DV;
using HarmonyLib;
using UnityEngine;

namespace BDVM;

internal sealed class BDVMStarterDeliveryRadio : MonoBehaviour, ICommsRadioMode
{
    private const float SignalRange = 100f;
    private static Func<IReadOnlyList<InitialDeliveryGrant>> pending = () => Array.Empty<InitialDeliveryGrant>();
    private static Func<RailTrack, double, InitialDeliveryTargetKind, InitialDeliveryGrant, string> deliver = (_, _, _, _) => "BDVM is not ready.";
    private CommsRadioDisplay? display;
    private Transform? signalOrigin;
    private RailTrack? pointedTrack;
    private double pointedSpan;
    private InitialDeliveryTargetKind pointedKind;
    private int selectedIndex;

    public ButtonBehaviourType ButtonBehaviour { get; private set; } = ButtonBehaviourType.Override;
    public Color GetLaserBeamColor() => new Color(0.15f, 0.8f, 1f);
    public void OverrideSignalOrigin(Transform origin) => signalOrigin = origin;

    internal static void Configure(Func<IReadOnlyList<InitialDeliveryGrant>> pendingProvider,
        Func<RailTrack, double, InitialDeliveryTargetKind, InitialDeliveryGrant, string> delivery)
    {
        pending = pendingProvider ?? throw new ArgumentNullException(nameof(pendingProvider));
        deliver = delivery ?? throw new ArgumentNullException(nameof(delivery));
    }

    internal void Initialize(CommsRadioController controller)
    {
        if (controller.deleteControl is CommsRadioCarDeleter template)
        {
            display = template.display;
            signalOrigin = template.signalOrigin;
        }
    }

    public void Enable() { selectedIndex = 0; Refresh(); }
    public void Disable() { pointedTrack = null; }
    public void SetStartingDisplay() => Refresh();

    public void OnUpdate()
    {
        pointedTrack = FindPointedTrack();
        pointedKind = Classify(pointedTrack);
        Refresh();
    }

    public void OnUse()
    {
        var grants = Available();
        if (grants.Count == 0) { Refresh("No delivery pending."); return; }
        if (pointedTrack == null || !IsEligible(pointedTrack)) { Refresh("Aim at a depot/service track."); return; }
        if (selectedIndex >= grants.Count) selectedIndex = 0;
        Refresh(deliver(pointedTrack, pointedSpan, pointedKind, grants[selectedIndex]));
    }

    public bool ButtonACustomAction() => Move(-1);
    public bool ButtonBCustomAction() => Move(1);

    private bool Move(int delta)
    {
        var count = Available().Count;
        if (count == 0) return false;
        selectedIndex = (selectedIndex + delta + count) % count;
        Refresh();
        return true;
    }

    private void Refresh(string? result = null)
    {
        if (display == null) return;
        var grants = Available();
        if (grants.Count == 0) { display.SetDisplay("BDVM DELIVERY", result ?? "No owned stock is awaiting delivery.", ""); return; }
        if (selectedIndex >= grants.Count) selectedIndex = 0;
        var grant = grants[selectedIndex];
        var track = pointedTrack?.LogicTrack()?.ID?.FullDisplayID;
        var target = pointedTrack != null && IsEligible(pointedTrack) ? track + " (eligible)" : "aim at depot/service track";
        display.SetDisplay("BDVM DELIVERY", result ?? $"{selectedIndex + 1}/{grants.Count}: {grant.DefinitionIds[0]}\nTarget: {target}", "deliver");
    }

    private static IReadOnlyList<InitialDeliveryGrant> Available() => pending()
        .Where(x => x.State == InitialDeliveryState.Available).OrderBy(x => x.GrantId, StringComparer.Ordinal).ToArray();

    private RailTrack? FindPointedTrack()
    {
        if (signalOrigin == null || !Physics.Raycast(signalOrigin.position, signalOrigin.forward, out var hit, SignalRange, LayerMask.GetMask("Default"))) return null;
        var match = RailTrackRegistry.Instance?.AllTracks
            .Select(track => new { Track = track, Point = RailTrack.GetPointWithinRangeWithYOffset(track, hit.point, 1.5f) })
            .Where(x => x.Point.HasValue).OrderBy(x => ((Vector3)x.Point!.Value.position - hit.point).sqrMagnitude)
            .FirstOrDefault();
        if (match == null) return null;
        pointedSpan = match.Point!.Value.span;
        return match.Track;
    }

    private static bool IsEligible(RailTrack? track)
    {
        if (track?.LogicTrack()?.ID == null) return false;
        var id = track.LogicTrack().ID.FullDisplayID ?? "";
        if (id.StartsWith("#", StringComparison.Ordinal)) return false;
        return StationController.allStations.Any(station =>
        {
            var range = station != null ? station.GetComponent<StationJobGenerationRange>() : null;
            var yardId = station?.stationInfo?.YardID;
            return range != null && !string.IsNullOrWhiteSpace(yardId) &&
                   id.StartsWith(yardId + "-", StringComparison.OrdinalIgnoreCase) &&
                   range.IsPlayerInJobGenerationZone(range.PlayerSqrDistanceFromStationCenter);
        });
    }

    private static InitialDeliveryTargetKind Classify(RailTrack? track)
    {
        var id = track?.LogicTrack()?.ID?.FullDisplayID ?? "";
        return id.IndexOf("service", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("-S", StringComparison.OrdinalIgnoreCase) >= 0
            ? InitialDeliveryTargetKind.ServiceTrack : InitialDeliveryTargetKind.Depot;
    }
}

[HarmonyPatch(typeof(CommsRadioController), "Awake")]
internal static class BDVMStarterDeliveryRadioPatch
{
    private static void Postfix(CommsRadioController __instance, List<ICommsRadioMode> ___allModes)
    {
        var mode = __instance.gameObject.GetComponent<BDVMStarterDeliveryRadio>() ?? __instance.gameObject.AddComponent<BDVMStarterDeliveryRadio>();
        mode.Initialize(__instance);
        if (!___allModes.Contains(mode)) ___allModes.Add(mode);
    }
}
