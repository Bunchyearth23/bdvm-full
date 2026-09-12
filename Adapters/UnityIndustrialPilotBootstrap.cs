using System;
using System.Collections.Generic;
using System.Linq;
using BDVM.Domain;
using DV;
using DV.Logic.Job;
using DV.ThingTypes;
using UnityEngine;

namespace BDVM.Adapters;

internal sealed class IndustrialPilotBootstrapResult
{
    public string PolicyId { get; set; } = "";
    public string OriginFacilityId { get; set; } = "";
    public string DestinationFacilityId { get; set; } = "";
    public string CargoId { get; set; } = "";
    public decimal BatchQuantity { get; set; }
    public string RecipeId { get; set; } = "";
}

internal static class UnityIndustrialPilotBootstrap
{
    // Discovery only: it creates a finite store for every cargo type accepted by
    // a loaded warehouse. Routes, wagons and contracts remain player decisions.
    public static void EnsureDefaultStocks(VehicleAcquisitionSnapshot snapshot, IndustrialEconomyEngine engine, string commandPrefix)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (engine == null) throw new ArgumentNullException(nameof(engine));
        foreach (var stationGroup in WarehouseMachineController.allControllers
            .Where(controller => controller?.warehouseMachine != null)
            .Select(controller => new { Controller = controller, Station = StationFor(controller.warehouseMachine.WarehouseTrack) })
            .Where(value => value.Station != null)
            .GroupBy(value => value.Station!.logicStation.ID.ToString(), StringComparer.Ordinal))
        {
            foreach (var cargo in stationGroup.SelectMany(value => value.Controller.warehouseMachine.SupportedCargoTypes)
                .Where(value => value != CargoType.None).Distinct().OrderBy(value => value.ToString(), StringComparer.Ordinal))
                EnsureStock(snapshot, engine, commandPrefix + ":" + stationGroup.Key + ":" + cargo, stationGroup.Key, cargo.ToString(), 100m, 200m);

            var supported = stationGroup.SelectMany(value => value.Controller.warehouseMachine.SupportedCargoTypes)
                .Where(value => value != CargoType.None).Select(value => value.ToString()).ToHashSet(StringComparer.Ordinal);
            if (CanonicalIndustryFlows.RawMaterialFacilities.Contains(stationGroup.Key) && CanonicalIndustryFlows.Outputs.TryGetValue(stationGroup.Key, out var rawOutputs))
                foreach (var cargoId in rawOutputs.Where(supported.Contains))
                    EnsureRecipe(snapshot, engine, commandPrefix, "canonical-source", stationGroup.Key, cargoId, "", 0m, cargoId, 10m, 60);
            if (CanonicalIndustryFlows.CityFacilities.Contains(stationGroup.Key))
                foreach (var cargoId in supported)
                    EnsureRecipe(snapshot, engine, commandPrefix, "canonical-sink", stationGroup.Key, cargoId, cargoId, 10m, "", 0m, 60);
        }
    }

    public static IndustrialPilotBootstrapResult Configure(VehicleAcquisitionSnapshot snapshot, IndustrialEconomyEngine engine,
        IReadOnlyList<string> selectedAssetIds, AssetOwnerRef operatorRef, long tick, string commandPrefix)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (engine == null) throw new ArgumentNullException(nameof(engine));
        if (operatorRef == null) throw new ArgumentNullException(nameof(operatorRef));
        if (string.IsNullOrWhiteSpace(commandPrefix)) throw new ArgumentException("A command prefix is required.", nameof(commandPrefix));
        var ids = (selectedAssetIds ?? Array.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) throw new InvalidOperationException("Select at least one available owned or leased freight wagon first.");
        var fleet = ids.Select(id => snapshot.Fleet.Single(value => value.AssetId == id)).ToArray();
        if (fleet.Any(value => value.Kind != FleetVehicleKind.FreightWagon || value.OperationalState != FleetOperationalState.Available))
            throw new InvalidOperationException("Every selected pilot asset must be an available freight wagon.");
        if (ids.Any(id => !IsControlledBy(snapshot, id, operatorRef, tick)))
            throw new InvalidOperationException("Every selected pilot wagon must be owned by, or actively leased to, the chosen operator.");

        var compatibility = new UnityWagonCompatibilityPort(snapshot);
        var choices = (from cargo in Globals.G.Types.cargos
                       where cargo != null && cargo.v1 != CargoType.None
                       let cargoId = cargo.v1.ToString()
                       let observations = ids.Select(id => compatibility.Inspect(id, snapshot.Assets.Assets.Single(value => value.AssetId == id).DefinitionId, cargoId)).ToArray()
                       where observations.All(value => value.Compatible && value.Capacity > 0m)
                       let stations = WarehouseMachineController.allControllers
                           .Where(controller => controller != null && controller.warehouseMachine != null && controller.warehouseMachine.SupportedCargoTypes.Contains(cargo.v1))
                           .Select(controller => new { Controller = controller, Station = StationFor(controller.warehouseMachine.WarehouseTrack) })
                           .Where(value => value.Station != null).GroupBy(value => value.Station!.logicStation.ID.ToString(), StringComparer.Ordinal)
                           .Select(stationGroup => stationGroup.First()).OrderBy(value => value.Station!.logicStation.ID.ToString(), StringComparer.Ordinal).ToArray()
                       where stations.Length >= 2
                       let source = stations[0]
                       from destination in stations.Skip(1)
                       orderby cargoId, Vector3.Distance(source.Controller.transform.position, destination.Controller.transform.position)
                       select new { Cargo = cargo.v1, Observations = observations, SourceStation = source.Station!, DestinationStation = destination.Station!, Distance = Vector3.Distance(source.Controller.transform.position, destination.Controller.transform.position) }).Take(48).ToArray();
        if (choices.Length == 0) throw new InvalidOperationException("No loaded source/sink warehouse network supports all selected wagons.");

        IndustrialPilotBootstrapResult? first = null;
        for (var choiceIndex = 0; choiceIndex < choices.Length; choiceIndex++)
        {
            var choice = choices[choiceIndex];
            var origin = choice.SourceStation.logicStation.ID.ToString();
            var destinationId = choice.DestinationStation.logicStation.ID.ToString();
            var cargoIdValue = choice.Cargo.ToString();
            var totalCapacity = choice.Observations.Sum(value => value.Capacity);
            var batch = Math.Max(1m, Math.Min(30m, totalCapacity));
            var stockCapacity = Math.Max(batch * 8m, totalCapacity * 4m);
            var prefix = commandPrefix + ":flow:" + choiceIndex;
            EnsureStock(snapshot, engine, prefix + ":origin", origin, cargoIdValue, stockCapacity / 2m, stockCapacity);
            EnsureStock(snapshot, engine, prefix + ":destination", destinationId, cargoIdValue, stockCapacity / 2m, stockCapacity);
            var recipeId = "pilot-source:" + origin + ":" + cargoIdValue;
            if (!snapshot.IndustrialRecipes.Any(value => value.RecipeId == recipeId)) engine.ConfigureRecipe(prefix + ":source", recipeId, origin, "", 0m, cargoIdValue, batch, 60, 32);
            var sinkRecipeId = "pilot-sink:" + destinationId + ":" + cargoIdValue;
            if (!snapshot.IndustrialRecipes.Any(value => value.RecipeId == sinkRecipeId)) engine.ConfigureRecipe(prefix + ":sink", sinkRecipeId, destinationId, cargoIdValue, batch, "", 0m, 180, 32);
            var policyId = "pilot-transport:" + origin + ":" + destinationId + ":" + cargoIdValue;
            var existingPolicy = snapshot.IndustrialTransportPolicies.SingleOrDefault(value => value.PolicyId == policyId);
            if (existingPolicy == null)
                engine.ConfigureTransportPolicy(prefix + ":policy", policyId, origin, destinationId, cargoIdValue, batch, batch * 4m,
                    2000 + decimal.ToInt64(decimal.Ceiling((decimal)choice.Distance * 0.12m)), 1500, 600, 600, 0,
                    new WagonRequirement { CargoId = cargoIdValue, MinimumWagonCount = 1, MinimumTotalCapacity = batch,
                        AllowedDefinitionIds = new List<string>() },
                    true, 3600, EstimateOperatingCost(batch, ids.Length, choice.Distance));
            else if (existingPolicy.WagonRequirement.AllowedDefinitionIds.Count > 0)
                engine.ConfigureTransportPolicy(prefix + ":policy-migrate-stock-driven", policyId, origin, destinationId, cargoIdValue,
                    existingPolicy.BatchQuantity, existingPolicy.DestinationTargetQuantity, existingPolicy.BaseReward, existingPolicy.MaximumScarcityBonus,
                    600, 600, 0, new WagonRequirement { CargoId = cargoIdValue, MinimumWagonCount = existingPolicy.WagonRequirement.MinimumWagonCount,
                        MinimumTotalCapacity = existingPolicy.WagonRequirement.MinimumTotalCapacity, AllowedDefinitionIds = new List<string>() }, true,
                    existingPolicy.DeliveryDurationTicks, existingPolicy.EstimatedOperatingCost);
            first ??= new IndustrialPilotBootstrapResult { PolicyId = policyId, OriginFacilityId = origin, DestinationFacilityId = destinationId, CargoId = cargoIdValue, BatchQuantity = batch, RecipeId = recipeId };
        }
        var transformStations = WarehouseMachineController.allControllers.Where(controller => controller?.warehouseMachine != null)
            .Select(controller => new { Controller = controller, Station = StationFor(controller.warehouseMachine.WarehouseTrack) })
            .Where(value => value.Station != null).GroupBy(value => value.Station!.logicStation.ID.ToString(), StringComparer.Ordinal);
        foreach (var stationGroup in transformStations)
        {
            var supported = stationGroup.SelectMany(value => value.Controller.warehouseMachine.SupportedCargoTypes).Where(value => value != CargoType.None)
                .Distinct().Where(cargo => ids.All(id => compatibility.Inspect(id, snapshot.Assets.Assets.Single(asset => asset.AssetId == id).DefinitionId, cargo.ToString()).Compatible))
                .OrderBy(value => value.ToString(), StringComparer.Ordinal).Take(2).ToArray();
            if (supported.Length < 2) continue;
            var facility = stationGroup.Key; var inputCargo = supported[0].ToString(); var outputCargo = supported[1].ToString();
            var capacity = Math.Max(100m, choices.Where(value => value.SourceStation.logicStation.ID.ToString() == facility || value.DestinationStation.logicStation.ID.ToString() == facility).Select(value => value.Observations.Sum(observation => observation.Capacity) * 4m).DefaultIfEmpty(100m).Max());
            EnsureStock(snapshot, engine, commandPrefix + ":transform:" + facility + ":input", facility, inputCargo, capacity / 2m, capacity);
            EnsureStock(snapshot, engine, commandPrefix + ":transform:" + facility + ":output", facility, outputCargo, capacity / 2m, capacity);
            var transformId = "pilot-transform:" + facility + ":" + inputCargo + ":" + outputCargo;
            if (!snapshot.IndustrialRecipes.Any(value => value.RecipeId == transformId))
                engine.ConfigureRecipe(commandPrefix + ":transform:" + facility + ":recipe", transformId, facility, inputCargo, 5m, outputCargo, 5m, 90, 32);
        }
        return first!;
    }

    private static long EstimateOperatingCost(decimal quantity, int wagonCount, float distance)
    {
        // A conservative quote floor: switching/setup plus fuel, consumables and wear exposure.
        // Actual player choices can exceed this estimate and turn the movement into a loss.
        return checked(2500L + wagonCount * 900L + decimal.ToInt64(decimal.Ceiling(quantity * 85m)) + (long)Math.Ceiling(distance * 0.65f));
    }

    private static bool IsControlledBy(VehicleAcquisitionSnapshot snapshot, string assetId, AssetOwnerRef operatorRef, long tick)
    {
        var ownership = snapshot.Ownership.SingleOrDefault(value => value.AssetId == assetId);
        if (ownership?.Owner?.Key == operatorRef.Key) return true;
        return snapshot.Leases.Any(value => value.AssetIds.Contains(assetId) && value.Lessee?.Key == operatorRef.Key &&
            (value.State == LeaseState.Active || value.State == LeaseState.Delinquent) && value.StartTick <= tick &&
            (value.EndTick <= 0 || tick < value.EndTick));
    }

    private static void EnsureStock(VehicleAcquisitionSnapshot snapshot, IndustrialEconomyEngine engine, string commandId, string facilityId, string cargoId, decimal onHand, decimal capacity)
    {
        var existing = snapshot.IndustrialStocks.SingleOrDefault(value => value.FacilityId == facilityId && value.CargoId == cargoId);
        if (existing == null) { engine.ConfigureStock(commandId, facilityId, cargoId, onHand, capacity); return; }
        // Discovering work again must never refill cargo already consumed or reserved.
        if (existing.Capacity < capacity) throw new InvalidOperationException("The existing facility is too small for these wagons. Choose a smaller consist or ask the host to review capacity.");
    }

    private static void EnsureRecipe(VehicleAcquisitionSnapshot snapshot, IndustrialEconomyEngine engine, string commandPrefix, string kind, string facilityId,
        string cargoId, string inputCargoId, decimal inputQuantity, string outputCargoId, decimal outputQuantity, long cadenceTicks)
    {
        var recipeId = kind + ":" + facilityId + ":" + cargoId;
        if (!snapshot.IndustrialRecipes.Any(value => value.RecipeId == recipeId))
            engine.ConfigureRecipe(commandPrefix + ":" + recipeId, recipeId, facilityId, inputCargoId, inputQuantity, outputCargoId, outputQuantity, cadenceTicks, 32);
    }

    private static StationController? StationFor(Track track)
    {
        var id = track?.ID?.FullDisplayID ?? "";
        return StationController.allStations.FirstOrDefault(value => value != null && value.logicStation != null && !string.IsNullOrWhiteSpace(value.stationInfo?.YardID) && id.StartsWith(value.stationInfo!.YardID + "-", StringComparison.OrdinalIgnoreCase));
    }
}
