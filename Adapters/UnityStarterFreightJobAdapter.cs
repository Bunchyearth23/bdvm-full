using System;
using System.Collections.Generic;
using System.Linq;
using BDVM.Domain;
using DV;
using DV.Booklets;
using DV.Logic.Job;
using DV.ThingTypes;
using HarmonyLib;
using SelfShunt;
using UnityEngine;

namespace BDVM.Adapters;

internal static class UnityStarterFreightJobAdapter
{
    private const string JobPrefix = "BDVM-CT-";

    public static string Create(VehicleAcquisitionSnapshot snapshot, string playerId)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (JobsManager.Instance == null || CarSpawner.Instance == null) throw new InvalidOperationException("The job runtime is not ready.");
        var existing = AllJobs().FirstOrDefault(x => x != null && x.ID != null && x.ID.StartsWith(JobPrefix, StringComparison.Ordinal));
        if (existing != null) return "Job " + existing.ID + " is already available.";

        var owned = snapshot.Ownership.Where(x => x.Owner.Kind == AssetOwnerKind.Player && x.Owner.OwnerId == playerId).Select(x => x.AssetId).ToHashSet(StringComparer.Ordinal);
        var guids = snapshot.Assets.Assets.Where(x => owned.Contains(x.AssetId) && (x.DefinitionId == "CarFlatcar" || x.DefinitionId == "FlatbedEmpty") && x.GameLink.State == PersistentLinkState.Resolved && !string.IsNullOrWhiteSpace(x.GameLink.Value))
            .Select(x => x.GameLink.Value!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var trainCars = CarSpawner.Instance.AllCars.Where(x => x != null && guids.Contains(x.CarGUID)).ToArray();
        if (trainCars.Length < 3 || trainCars.Any(x => x.logicCar == null)) throw new InvalidOperationException("Deliver all three starter flatcars before creating the freight job.");
        var cars = trainCars.Select(x => x.logicCar).ToList();

        var source = WarehouseMachineController.allControllers.FirstOrDefault(x => x != null && x.warehouseMachine != null && cars.All(c => c.CurrentTrack == x.warehouseMachine.WarehouseTrack));
        if (source == null) throw new InvalidOperationException("Place all three starter flatcars fully on the same warehouse loading track.");
        var sourceStation = StationFor(source.warehouseMachine.WarehouseTrack);
        if (sourceStation == null) throw new InvalidOperationException("The loading track is not attached to a known station.");

        var choice = (from cargo in Globals.G.Types.cargos
                      where cargo != null && cargo.v1 != CargoType.None && source.warehouseMachine.SupportedCargoTypes.Contains(cargo.v1)
                      where cars.All(car => DVObjectModel.current.CargoToLoadableCarTypes.TryGetValue(cargo, out var types) && types.Contains(car.carType.parentType))
                      from target in WarehouseMachineController.allControllers
                      where target != null && target != source && target.warehouseMachine != null && target.warehouseMachine.SupportedCargoTypes.Contains(cargo.v1)
                      let station = StationFor(target.warehouseMachine.WarehouseTrack)
                      where station != null && station != sourceStation
                      orderby Vector3.Distance(source.transform.position, target.transform.position)
                      select new { Cargo = cargo.v1, Target = target, Station = station }).FirstOrDefault();
        if (choice == null) throw new InvalidOperationException("No compatible destination warehouse and cargo were found for the starter flatcars.");

        var id = JobPrefix + Math.Abs(playerId.GetHashCode()).ToString("X8");
        var chain = new StationsChainData(sourceStation.logicStation.ID, choice.Station.logicStation.ID);
        var chainObject = new GameObject("ChainJob[BDVM owned freight]: " + sourceStation.logicStation.ID + " - " + choice.Station.logicStation.ID);
        chainObject.transform.SetParent(sourceStation.transform);
        var definition = chainObject.AddComponent<StaticDirectJobDefinition>();
        definition.displayCars = trainCars.Select(x => new Car_data("?", x.carLivery, false, false, 0f, 0f, 0f)).ToList();
        definition.cargoAmountPerCar = cars.Select(x => x.capacity).ToList();
        definition.carsToTransport = cars;
        definition.loadMachine = source.warehouseMachine;
        definition.unloadMachine = choice.Target.warehouseMachine;
        definition.transportedCargo = choice.Cargo;
        definition.ForceJobId(id);
        definition.PopulateBaseJobDefinition(sourceStation.logicStation, 7200f, 15000f, chain, JobLicenses.Basic);
        var controller = new JobChainController(chainObject) { carsForJobChain = cars.ToList() };
        controller.AddJobDefinitionToChain(definition);
        controller.FinalizeSetupAndGenerateFirstJob(false);
        return "Created " + id + ": " + sourceStation.stationInfo.YardID + " -> " + choice.Station.stationInfo.YardID + ", cargo=" + choice.Cargo + ", wagons=" + cars.Count + ".";
    }

    private static StationController? StationFor(Track track)
    {
        var id = track?.ID?.FullDisplayID ?? "";
        return StationController.allStations.FirstOrDefault(x => x != null && x.logicStation != null && !string.IsNullOrWhiteSpace(x.stationInfo?.YardID) && id.StartsWith(x.stationInfo!.YardID + "-", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<Job> AllJobs()
    {
        var field = AccessTools.Field(typeof(JobsManager), "allJobs") ?? throw new MissingFieldException(typeof(JobsManager).FullName, "allJobs");
        return field.GetValue(JobsManager.Instance) as IReadOnlyList<Job> ?? throw new InvalidOperationException("JobsManager.allJobs has an unsupported shape.");
    }
}
