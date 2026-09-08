using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using BDVM.Domain;
using MPAPI;
using UnityEngine;

namespace BDVM.Adapters;

internal static class UnityReadOnlyProjection
{
    public static string[] Components(GameObject? gameObject) => gameObject == null
        ? Array.Empty<string>()
        : gameObject.GetComponentsInChildren<Component>(true)
            .Where(component => component != null)
            .Select(component => component.GetType().FullName ?? component.GetType().Name)
            .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();

    public static OriginRecord Origin(object? source, GameObject? prefab)
    {
        var assembly = source?.GetType().Assembly.GetName().Name;
        var customComponents = Components(prefab).Where(name =>
            !name.StartsWith("UnityEngine.", StringComparison.Ordinal) &&
            !name.StartsWith("DV.", StringComparison.Ordinal) &&
            name != "TrainCar").ToArray();
        return new OriginRecord
        {
            Kind = customComponents.Length == 0 ? "game-or-unattributed-runtime" : "runtime-mod-component",
            ProviderId = customComponents.FirstOrDefault(),
            AssemblyName = assembly
        };
    }
}

public sealed class UnityVehicleDefinitionReader : IVehicleDefinitionReader
{
    public IReadOnlyList<VehicleDefinitionRecord> ReadLoadedDefinitions(string correlationId)
    {
        var liveries = Globals.G?.Types?.Liveries;
        if (liveries == null) return Array.Empty<VehicleDefinitionRecord>();
        return liveries.Select(livery =>
        {
            var id = livery?.id;
            var prefab = livery?.prefab;
            return new VehicleDefinitionRecord
            {
                ExistingDefinitionId = id,
                Type = livery?.parentType?.id,
                Origin = UnityReadOnlyProjection.Origin(livery, prefab),
                Components = UnityReadOnlyProjection.Components(prefab),
                Resolution = string.IsNullOrWhiteSpace(id) ? ResolutionState.MissingIdentifier :
                    prefab == null ? ResolutionState.MissingDefinition : ResolutionState.Resolved,
                ResolutionDetail = prefab == null ? "Loaded livery has no prefab." : null
            };
        }).ToArray();
    }
}

public sealed class UnityVisibleVehicleReader : IVisibleVehicleReader
{
    public IReadOnlyList<VehicleInstanceRecord> ReadVisibleInventory(string correlationId)
    {
        var cars = UnityEngine.Object.FindObjectsOfType<TrainCar>();
        if (cars == null) return Array.Empty<VehicleInstanceRecord>();
        return cars.Where(car => car != null).Distinct().Select(car =>
        {
            var definitionId = car.carLivery?.id;
            var matchingDefinitions = Globals.G.Types.Liveries.Count(livery => livery != null && livery.id == definitionId);
            var isExternalAi = HasComponentNamed(car.gameObject, "AITrafficCarMarker") || HasComponentNamed(car.gameObject, "AIEngineer");
            string? netId = null;
            var api = MultiplayerAPI.Instance;
            if (api != null && api.TryGetNetId(car, out ushort value) && value != 0) netId = value.ToString();
            var members = car.trainset?.cars?.Where(member => member != null)
                .Select(member => member.CarGUID).Where(id => !string.IsNullOrWhiteSpace(id)).ToArray() ?? Array.Empty<string>();
            var resolution = isExternalAi ? ResolutionState.ExcludedExternalTraffic :
                string.IsNullOrWhiteSpace(car.CarGUID) ? ResolutionState.MissingIdentifier :
                matchingDefinitions == 0 ? ResolutionState.MissingDefinition :
                matchingDefinitions > 1 ? ResolutionState.AmbiguousDefinition : ResolutionState.Resolved;
            return new VehicleInstanceRecord
            {
                ExistingPersistentId = car.CarGUID,
                ExistingVisibleId = car.ID,
                ExistingSessionNetId = netId,
                DefinitionId = definitionId,
                Type = car.carType.ToString(),
                Origin = UnityReadOnlyProjection.Origin(car.carLivery, car.carLivery?.prefab),
                Components = UnityReadOnlyProjection.Components(car.gameObject),
                TrainsetMemberPersistentIds = members,
                Resolution = resolution,
                ResolutionDetail = isExternalAi ? "External AI Traffic; visible but excluded from economic inventory." :
                    matchingDefinitions == 0 ? "No loaded definition matches the instance livery ID." :
                    matchingDefinitions > 1 ? "More than one loaded definition matches the instance livery ID." : null
            };
        }).ToArray();
    }

    private static bool HasComponentNamed(GameObject gameObject, string typeName) =>
        gameObject.GetComponentsInChildren<Component>(true).Any(component =>
            component != null && (component.GetType().Name == typeName || component.GetType().FullName?.EndsWith("." + typeName, StringComparison.Ordinal) == true));
}
