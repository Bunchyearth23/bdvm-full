using System;
using System.Collections.Generic;
using System.Linq;
using BDVM.Domain;
using DV;
using DV.OriginShift;
using HarmonyLib;
using UnityEngine;

namespace BDVM;

internal sealed class BDVMStarterDeliveryRadio : MonoBehaviour, ICommsRadioMode
{
    private const float SignalRange = 100f;
    private static Func<IReadOnlyList<InitialDeliveryGrant>> pending = () => Array.Empty<InitialDeliveryGrant>();
    private static Func<RailTrack, double, bool, InitialDeliveryTargetKind, InitialDeliveryGrant, string> deliver = (_, _, _, _, _) => "BDVM is not ready.";
    private CommsRadioDisplay? display;
    private Transform? signalOrigin;
    private Material? validMaterial;
    private Material? invalidMaterial;
    private ArrowLCD? lcdArrow;
    private CarDestinationHighlighter? highlighter;
    private RailTrack? pointedTrack;
    private double pointedSpan;
    private InitialDeliveryTargetKind pointedKind;
    private Bounds selectedBounds;
    private int selectedIndex;
    private bool placementLocked;
    private bool withTrackDirection = true;
    private bool canSpawn;

    public ButtonBehaviourType ButtonBehaviour { get; private set; } = ButtonBehaviourType.Override;
    public Color GetLaserBeamColor() => new Color(0.15f, 0.8f, 1f);
    public void OverrideSignalOrigin(Transform origin) => signalOrigin = origin;

    internal static void Configure(Func<IReadOnlyList<InitialDeliveryGrant>> pendingProvider, Func<RailTrack, double, bool, InitialDeliveryTargetKind, InitialDeliveryGrant, string> delivery)
    {
        pending = pendingProvider ?? throw new ArgumentNullException(nameof(pendingProvider));
        deliver = delivery ?? throw new ArgumentNullException(nameof(delivery));
    }

    internal void Initialize(CommsRadioController controller)
    {
        var template = controller.GetComponent<CommsRadioCarSpawner>();
        if (template == null) return;
        display = template.display;
        signalOrigin = template.signalOrigin;
        validMaterial = template.validMaterial;
        invalidMaterial = template.invalidMaterial;
        lcdArrow = template.lcdArrow;
        var destination = Instantiate(template.destinationHighlighterGO);
        var arrows = Instantiate(template.directionArrowsHighlighterGO);
        destination.name = "BDVM Delivery Destination Highlighter";
        arrows.name = "BDVM Delivery Direction Highlighter";
        highlighter = new CarDestinationHighlighter(destination, arrows);
        highlighter.TurnOff();
    }

    public void Enable() { selectedIndex = 0; placementLocked = false; withTrackDirection = true; UpdateSelectedBounds(); Refresh(); }
    public void Disable() { pointedTrack = null; placementLocked = false; canSpawn = false; highlighter?.TurnOff(); lcdArrow?.TurnOff(); }
    private void OnDestroy() { highlighter?.Destroy(); highlighter = null; }
    public void SetStartingDisplay() => Refresh();
    public void OnUpdate() { UpdateTarget(); Refresh(); }

    public void OnUse()
    {
        var grants = Available();
        if (grants.Count == 0) { Refresh("No delivery pending."); return; }
        if (!canSpawn || pointedTrack == null) { Refresh("No safe depot/service placement here."); return; }
        if (!placementLocked) { placementLocked = true; Refresh("A/B reverses direction; Use delivers."); return; }
        if (selectedIndex >= grants.Count) selectedIndex = 0;
        var result = deliver(pointedTrack, pointedSpan, withTrackDirection, pointedKind, grants[selectedIndex]);
        placementLocked = false;
        withTrackDirection = true;
        UpdateSelectedBounds();
        Refresh(result);
    }

    public bool ButtonACustomAction() => placementLocked ? Reverse() : Move(-1);
    public bool ButtonBCustomAction() => placementLocked ? Reverse() : Move(1);
    private bool Reverse() { if (!canSpawn) return false; withTrackDirection = !withTrackDirection; Refresh(); return true; }

    private bool Move(int delta)
    {
        var count = Available().Count;
        if (count == 0) return false;
        selectedIndex = (selectedIndex + delta + count) % count;
        UpdateSelectedBounds();
        Refresh();
        return true;
    }

    private void UpdateSelectedBounds()
    {
        var grants = Available();
        if (grants.Count == 0) return;
        if (selectedIndex >= grants.Count) selectedIndex = 0;
        var id = grants[selectedIndex].DefinitionIds[0] == "CarFlatcar" ? "FlatbedEmpty" : grants[selectedIndex].DefinitionIds[0];
        var livery = Globals.G.Types.Liveries.SingleOrDefault(x => x != null && x.id == id);
        if (livery?.prefab != null) selectedBounds = livery.prefab.GetComponent<TrainCar>().Bounds;
    }

    private void UpdateTarget()
    {
        canSpawn = false;
        pointedTrack = null;
        if (signalOrigin == null || !Physics.Raycast(signalOrigin.position, signalOrigin.forward, out var hit, SignalRange, LayerMask.GetMask("Default"))) { ShowInvalidPreview(); return; }
        var match = RailTrackRegistry.Instance?.AllTracks.Select(track => new { Track = track, Point = RailTrack.GetPointWithinRangeWithYOffset(track, hit.point, 3f, -1.75f) })
            .Where(x => x.Point.HasValue).OrderBy(x => ((Vector3)x.Point!.Value.position - hit.point).sqrMagnitude).FirstOrDefault();
        if (match == null) { ShowInvalidPreview(); return; }
        pointedTrack = match.Track;
        pointedKind = Classify(pointedTrack);
        var points = pointedTrack.GetKinkedPointSet()?.points;
        var valid = points == null ? null : CarSpawner.FindClosestValidPointForCarStartingFromIndex(points, match.Point!.Value.index, selectedBounds.extents);
        var point = valid ?? match.Point;
        pointedSpan = point!.Value.span;
        canSpawn = valid.HasValue && IsEligible(pointedTrack);
        var forward = withTrackDirection ? point.Value.forward : -point.Value.forward;
        highlighter?.Highlight((Vector3)point.Value.position + OriginShift.currentMove, forward, selectedBounds, canSpawn ? validMaterial : invalidMaterial);
        if (canSpawn && placementLocked) UpdateDirectionArrow(forward); else lcdArrow?.TurnOff();
    }

    private void ShowInvalidPreview()
    {
        if (signalOrigin != null) highlighter?.Highlight(signalOrigin.position + signalOrigin.forward * 20f, signalOrigin.right, selectedBounds, invalidMaterial);
        lcdArrow?.TurnOff();
    }

    private void UpdateDirectionArrow(Vector3 forward)
    {
        if (signalOrigin == null || lcdArrow == null) return;
        var left = Mathf.Sin(Vector3.SignedAngle(forward, signalOrigin.forward, Vector3.up) * Mathf.Deg2Rad) <= 0f;
        lcdArrow.TurnOn(!left);
    }

    private void Refresh(string? result = null)
    {
        if (display == null) return;
        var grants = Available();
        if (grants.Count == 0) { display.SetDisplay("BDVM DELIVERY", result ?? "No owned stock is awaiting delivery.", ""); return; }
        if (selectedIndex >= grants.Count) selectedIndex = 0;
        var grant = grants[selectedIndex];
        var direction = withTrackDirection ? "track direction" : "reverse direction";
        var prompt = placementLocked ? $"{grant.DefinitionIds[0]}\n{direction}\nA/B reverse; Use confirms" : $"{selectedIndex + 1}/{grants.Count}: {grant.DefinitionIds[0]}\nA/B select; Use chooses placement";
        display.SetDisplay("BDVM DELIVERY", result ?? prompt, canSpawn ? "confirm" : "cancel");
    }

    private static IReadOnlyList<InitialDeliveryGrant> Available() => pending().Where(x => x.State == InitialDeliveryState.Available).OrderBy(x => x.GrantId, StringComparer.Ordinal).ToArray();

    private static bool IsEligible(RailTrack? track)
    {
        if (track?.LogicTrack()?.ID == null) return false;
        var id = track.LogicTrack().ID.FullDisplayID ?? "";
        if (id.StartsWith("#", StringComparison.Ordinal)) return false;
        return StationController.allStations.Any(station =>
        {
            var range = station != null ? station.GetComponent<StationJobGenerationRange>() : null;
            var yardId = station?.stationInfo?.YardID;
            return range != null && !string.IsNullOrWhiteSpace(yardId) && id.StartsWith(yardId + "-", StringComparison.OrdinalIgnoreCase) && range.IsPlayerInJobGenerationZone(range.PlayerSqrDistanceFromStationCenter);
        });
    }

    private static InitialDeliveryTargetKind Classify(RailTrack? track)
    {
        var id = track?.LogicTrack()?.ID?.FullDisplayID ?? "";
        return id.IndexOf("service", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("-S", StringComparison.OrdinalIgnoreCase) >= 0 ? InitialDeliveryTargetKind.ServiceTrack : InitialDeliveryTargetKind.Depot;
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
