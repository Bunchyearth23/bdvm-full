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
        var choice = (from cargo in Globals.G.Types.cargos
                      where cargo != null && cargo.v1 != CargoType.None
                      let cargoId = cargo.v1.ToString()
                      let observations = ids.Select(id =>
                      {
                          var asset = snapshot.Assets.Assets.Single(value => value.AssetId == id);
                          return compatibility.Inspect(id, asset.DefinitionId, cargoId);
                      }).ToArray()
                      where observations.All(value => value.Compatible && value.Capacity > 0m)
                      from source in WarehouseMachineController.allControllers
                      where source != null && source.warehouseMachine != null && source.warehouseMachine.SupportedCargoTypes.Contains(cargo.v1)
                      let sourceStation = StationFor(source.warehouseMachine.WarehouseTrack)
                      where sourceStation != null
                      from destination in WarehouseMachineController.allControllers
                      where destination != null && destination != source && destination.warehouseMachine != null && destination.warehouseMachine.SupportedCargoTypes.Contains(cargo.v1)
                      let destinationStation = StationFor(destination.warehouseMachine.WarehouseTrack)
                      where destinationStation != null && destinationStation != sourceStation
                      orderby Vector3.Distance(source.transform.position, destination.transform.position)
                      select new { Cargo = cargo.v1, Observations = observations, SourceStation = sourceStation, DestinationStation = destinationStation }).FirstOrDefault();
        if (choice == null) throw new InvalidOperationException("No loaded origin/destination warehouse pair supports all selected wagons.");

        var origin = choice.SourceStation!.logicStation.ID.ToString();
        var destinationId = choice.DestinationStation!.logicStation.ID.ToString();
        var cargoIdValue = choice.Cargo.ToString();
        var totalCapacity = choice.Observations.Sum(value => value.Capacity);
        var batch = Math.Max(1m, Math.Min(30m, totalCapacity));
        var stockCapacity = Math.Max(batch * 8m, totalCapacity * 4m);
        EnsureStock(snapshot, engine, commandPrefix + ":origin", origin, cargoIdValue, batch * 2m, stockCapacity);
        EnsureStock(snapshot, engine, commandPrefix + ":destination", destinationId, cargoIdValue, 0m, stockCapacity);
        var feedstock = "BDVMFeedstock." + cargoIdValue;
        EnsureStock(snapshot, engine, commandPrefix + ":feedstock", origin, feedstock, 1000m, 1000m);
        var recipeId = "pilot-production:" + origin + ":" + cargoIdValue;
        if (!snapshot.IndustrialRecipes.Any(value => value.RecipeId == recipeId))
            engine.ConfigureRecipe(commandPrefix + ":recipe", recipeId, origin, feedstock, 1m, cargoIdValue, batch, 60, 32);
        var policyId = "pilot-transport:" + origin + ":" + destinationId + ":" + cargoIdValue;
        var policy = snapshot.IndustrialTransportPolicies.SingleOrDefault(value => value.PolicyId == policyId);
        if (policy == null)
            engine.ConfigureTransportPolicy(commandPrefix + ":policy", policyId, origin, destinationId, cargoIdValue, batch, batch * 4m,
                15000, 5000, 600, 600, 0,
                new WagonRequirement { CargoId = cargoIdValue, MinimumWagonCount = 1, MinimumTotalCapacity = batch,
                    AllowedDefinitionIds = ids.Select(id => snapshot.Assets.Assets.Single(value => value.AssetId == id).DefinitionId).Distinct(StringComparer.Ordinal).ToList() },
                true, 3600);
        else if (!policy.Enabled || policy.OriginFacilityId != origin || policy.DestinationFacilityId != destinationId || policy.CargoId != cargoIdValue ||
                 policy.WagonRequirement == null || ids.Any(id => !policy.WagonRequirement.AllowedDefinitionIds.Contains(
                     snapshot.Assets.Assets.Single(value => value.AssetId == id).DefinitionId, StringComparer.Ordinal)))
            throw new InvalidOperationException("The existing pilot policy is incompatible with the selected wagons or loaded station pair.");
        engine.PublishTransportNeed(commandPrefix + ":publish", policyId, tick);
        return new IndustrialPilotBootstrapResult { PolicyId = policyId, OriginFacilityId = origin, DestinationFacilityId = destinationId, CargoId = cargoIdValue, BatchQuantity = batch, RecipeId = recipeId };
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
        if (existing.Capacity >= capacity && existing.OnHand >= onHand) return;
        engine.ConfigureStock(commandId, facilityId, cargoId, Math.Max(existing.OnHand, onHand), Math.Max(existing.Capacity, capacity));
    }

    private static StationController? StationFor(Track track)
    {
        var id = track?.ID?.FullDisplayID ?? "";
        return StationController.allStations.FirstOrDefault(value => value != null && value.logicStation != null && !string.IsNullOrWhiteSpace(value.stationInfo?.YardID) && id.StartsWith(value.stationInfo!.YardID + "-", StringComparison.OrdinalIgnoreCase));
    }
}
