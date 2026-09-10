using System;
using System.Collections.Generic;
using System.Linq;
using BDVM.Domain;
using BDVM.SelfShuntBridge;
using DV;
using DV.Booklets;
using DV.Logic.Job;
using DV.ThingTypes;
using HarmonyLib;
using SelfShunt;
using SelfShunt.API;
using UnityEngine;

namespace BDVM.Adapters;

internal static class UnityIndustrialJobAdapter
{
    public static int RestoreCorrelations(VehicleAcquisitionSnapshot snapshot, SelfShuntIndustrialLifecycleAdapter lifecycle)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
        if (JobsManager.Instance == null) return 0;
        var jobsById = AllJobs().Where(value => value != null && !string.IsNullOrWhiteSpace(value.ID))
            .GroupBy(value => value.ID, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var recoverable = snapshot.IndustrialContracts.Where(value =>
                value.State != IndustrialContractState.Completed && value.State != IndustrialContractState.Cancelled && value.State != IndustrialContractState.Expired &&
                value.AssignedWagons.Count > 0)
            .OrderBy(value => value.ContractId, StringComparer.Ordinal)
            .ToArray();
        foreach (var contract in recoverable.Where(value => value.State == IndustrialContractState.Active || value.State == IndustrialContractState.DeliveryPending))
        {
            if (!jobsById.TryGetValue(contract.ContractId, out var matches))
                throw new InvalidOperationException("The persisted active industrial contract has no loaded SelfShunt job yet: " + contract.ContractId + ".");
            if (matches.Length != 1)
                throw new InvalidOperationException("The persisted active industrial contract has an ambiguous SelfShunt job identity: " + contract.ContractId + ".");
        }
        var restored = 0;
        foreach (var contract in recoverable.Where(value => jobsById.ContainsKey(value.ContractId)))
        {
            if (jobsById[contract.ContractId].Length != 1)
                throw new InvalidOperationException("Multiple loaded SelfShunt jobs match persisted industrial contract " + contract.ContractId + ".");
            if (!lifecycle.TryRegister(Registration(contract)))
                throw new InvalidOperationException("SelfShunt refused recovery of persisted industrial job " + contract.ContractId + ".");
            restored++;
        }
        return restored;
    }

    public static void RequireCancellationObserved(IndustrialContract contract)
    {
        if (contract == null) throw new ArgumentNullException(nameof(contract));
        if (JobsManager.Instance == null)
        {
            if (contract.State == IndustrialContractState.Active || contract.State == IndustrialContractState.DeliveryPending)
                throw new InvalidOperationException("The external job runtime is unavailable; active contract cancellation is not authoritative.");
            return;
        }
        var matches = AllJobs().Where(value => value != null && string.Equals(value.ID, contract.ContractId, StringComparison.Ordinal)).Take(2).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("Multiple external jobs match the industrial contract.");
        if (matches.Length == 0)
        {
            if (contract.State == IndustrialContractState.Active || contract.State == IndustrialContractState.DeliveryPending)
                throw new InvalidOperationException("The active external job disappeared; reconcile it before releasing the operator consist.");
            return;
        }
        if (matches[0].State != JobState.Abandoned && matches[0].State != JobState.Expired)
            throw new InvalidOperationException("Abandon or expire the external SelfShunt job before cancelling its BDVM contract.");
    }

    public static string CreateForContract(VehicleAcquisitionSnapshot snapshot, IndustrialContract contract, SelfShuntIndustrialLifecycleAdapter lifecycle)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (contract == null) throw new ArgumentNullException(nameof(contract));
        if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
        if (contract.State != IndustrialContractState.Reserved || contract.AssignedWagons.Count == 0)
            throw new InvalidOperationException("The industrial contract must be reserved with operator wagons before its job can be created.");
        if (JobsManager.Instance == null || CarSpawner.Instance == null) throw new InvalidOperationException("The job runtime is not ready.");

        var cargo = Globals.G.Types.cargos.FirstOrDefault(value => value != null &&
            (string.Equals(value.v1.ToString(), contract.CargoId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(value.id, contract.CargoId, StringComparison.OrdinalIgnoreCase)))?.v1 ?? CargoType.None;
        if (cargo == CargoType.None) throw new InvalidOperationException("The contract cargo is not known to the loaded game: " + contract.CargoId + ".");

        var sourceStation = ResolveStation(contract.OriginFacilityId) ?? throw new InvalidOperationException("The origin facility is not a loaded station: " + contract.OriginFacilityId + ".");
        var destinationStation = ResolveStation(contract.DestinationFacilityId) ?? throw new InvalidOperationException("The destination facility is not a loaded station: " + contract.DestinationFacilityId + ".");
        var trainCars = ResolveAssignedCars(snapshot, contract);
        var cars = trainCars.Select(value => value.logicCar ?? throw new InvalidOperationException("An assigned wagon has no logic car.")).ToList();
        var source = WarehouseMachineController.allControllers.FirstOrDefault(value => value != null && value.warehouseMachine != null &&
            StationFor(value.warehouseMachine.WarehouseTrack) == sourceStation && value.warehouseMachine.SupportedCargoTypes.Contains(cargo) &&
            cars.All(car => car.CurrentTrack == value.warehouseMachine.WarehouseTrack));
        if (source == null) throw new InvalidOperationException("Place every assigned wagon fully on one compatible loading track at " + contract.OriginFacilityId + ".");
        var destination = WarehouseMachineController.allControllers.FirstOrDefault(value => value != null && value.warehouseMachine != null &&
            StationFor(value.warehouseMachine.WarehouseTrack) == destinationStation && value.warehouseMachine.SupportedCargoTypes.Contains(cargo));
        if (destination == null) throw new InvalidOperationException("No compatible unloading track exists at " + contract.DestinationFacilityId + ".");

        var existing = AllJobs().FirstOrDefault(value => value != null && string.Equals(value.ID, contract.ContractId, StringComparison.Ordinal));
        var registration = Registration(contract);
        if (!lifecycle.TryRegister(registration)) throw new InvalidOperationException("SelfShunt refused the externally-authoritative zero-wage job registration.");
        if (existing != null) return "SelfShunt job " + contract.ContractId + " was already present and was re-correlated.";

        var cargoPerCar = AllocateCargo(contract, cars);
        var chain = new StationsChainData(sourceStation.logicStation.ID, destinationStation.logicStation.ID);
        var chainObject = new GameObject("ChainJob[BDVM industrial]: " + sourceStation.logicStation.ID + " - " + destinationStation.logicStation.ID);
        chainObject.transform.SetParent(sourceStation.transform);
        try
        {
            var definition = chainObject.AddComponent<StaticDirectJobDefinition>();
            definition.displayCars = trainCars.Select(value => new Car_data("?", value.carLivery, false, false, 0f, 0f, 0f)).ToList();
            definition.cargoAmountPerCar = cargoPerCar;
            definition.carsToTransport = cars;
            definition.loadMachine = source.warehouseMachine;
            definition.unloadMachine = destination.warehouseMachine;
            definition.transportedCargo = cargo;
            definition.ForceJobId(contract.ContractId);
            definition.PopulateBaseJobDefinition(sourceStation.logicStation, 7200f, 0f, chain, JobLicenses.Basic);
            var controller = new JobChainController(chainObject) { carsForJobChain = cars };
            controller.AddJobDefinitionToChain(definition);
            controller.FinalizeSetupAndGenerateFirstJob(false);
            if (!AllJobs().Any(value => value != null && string.Equals(value.ID, contract.ContractId, StringComparison.Ordinal)))
                throw new InvalidOperationException("SelfShunt did not publish the generated job.");
        }
        catch
        {
            UnityEngine.Object.Destroy(chainObject);
            throw;
        }
        return "Created zero-wage SelfShunt job " + contract.ContractId + " for " + trainCars.Length + " operator wagon(s).";
    }

    private static SelfShuntExternalJobRegistration Registration(IndustrialContract contract) => new SelfShuntExternalJobRegistration
    {
        OperationId = "industrial-contract-register:" + contract.ContractId,
        JobId = contract.ContractId,
        StationId = contract.OriginFacilityId,
        CargoId = contract.CargoId,
        DisplayReward = checked(contract.BaseReward + contract.ScarcityBonus)
    };

    private static TrainCar[] ResolveAssignedCars(VehicleAcquisitionSnapshot snapshot, IndustrialContract contract)
    {
        var result = new List<TrainCar>();
        foreach (var assignment in contract.AssignedWagons)
        {
            var asset = snapshot.Assets.Assets.Single(value => value.AssetId == assignment.AssetId);
            if (asset.GameLink.State != PersistentLinkState.Resolved || string.IsNullOrWhiteSpace(asset.GameLink.Value))
                throw new InvalidOperationException("Assigned wagon is not physically resolved: " + assignment.AssetId + ".");
            var car = CarSpawner.Instance.AllCars.SingleOrDefault(value => value != null && string.Equals(value.CarGUID, asset.GameLink.Value, StringComparison.OrdinalIgnoreCase));
            if (car == null) throw new InvalidOperationException("Assigned wagon is not physically present: " + assignment.AssetId + ".");
            result.Add(car);
        }
        if (result.Select(value => value.CarGUID).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
            throw new InvalidOperationException("The assigned consist contains a duplicate physical wagon.");
        return result.ToArray();
    }

    private static List<float> AllocateCargo(IndustrialContract contract, IReadOnlyList<Car> cars)
    {
        var remaining = contract.Quantity;
        var result = new List<float>(cars.Count);
        for (var index = 0; index < cars.Count; index++)
        {
            var declared = contract.AssignedWagons[index].Capacity;
            var physical = (decimal)Math.Max(0f, cars[index].capacity);
            var quantity = Math.Min(remaining, Math.Min(declared, physical));
            result.Add((float)quantity);
            remaining -= quantity;
        }
        if (remaining > 0.01m) throw new InvalidOperationException("The physically present consist cannot carry the reserved contract quantity.");
        return result;
    }

    private static StationController? ResolveStation(string facilityId) => StationController.allStations.FirstOrDefault(value => value != null && value.logicStation != null &&
        (string.Equals(value.logicStation.ID.ToString(), facilityId, StringComparison.OrdinalIgnoreCase) || string.Equals(value.stationInfo?.YardID, facilityId, StringComparison.OrdinalIgnoreCase)));

    private static StationController? StationFor(Track track)
    {
        var id = track?.ID?.FullDisplayID ?? "";
        return StationController.allStations.FirstOrDefault(value => value != null && value.logicStation != null && !string.IsNullOrWhiteSpace(value.stationInfo?.YardID) && id.StartsWith(value.stationInfo!.YardID + "-", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<Job> AllJobs()
    {
        var field = AccessTools.Field(typeof(JobsManager), "allJobs") ?? throw new MissingFieldException(typeof(JobsManager).FullName, "allJobs");
        return field.GetValue(JobsManager.Instance) as IReadOnlyList<Job> ?? throw new InvalidOperationException("JobsManager.allJobs has an unsupported shape.");
    }
}
