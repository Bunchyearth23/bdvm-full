using System;
using System.Collections.Generic;
using System.Linq;
using BDVM.Domain;
using DV;
using DV.Booklets;
using DV.Logic.Job;
using DV.ThingTypes;
using HarmonyLib;
using BDVM.SelfShuntBridge;
using SelfShunt.API;
using SelfShunt;
using UnityEngine;

namespace BDVM.Adapters;

internal static class UnityStarterFreightJobAdapter
{
    private const string JobPrefix = "BDVM-CT-";

    public static string Create(VehicleAcquisitionSnapshot snapshot, string playerId, SelfShuntIndustrialLifecycleAdapter lifecycle)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (lifecycle == null) throw new ArgumentNullException(nameof(lifecycle));
        if (JobsManager.Instance == null || CarSpawner.Instance == null) throw new InvalidOperationException("The job runtime is not ready.");

        var owned = snapshot.Ownership.Where(x => x.Owner.Kind == AssetOwnerKind.Player && x.Owner.OwnerId == playerId).Select(x => x.AssetId).ToHashSet(StringComparer.Ordinal);
        var assetsByGuid = snapshot.Assets.Assets.Where(x => owned.Contains(x.AssetId) && (x.DefinitionId == "CarFlatcar" || x.DefinitionId == "FlatbedEmpty") && x.GameLink.State == PersistentLinkState.Resolved && !string.IsNullOrWhiteSpace(x.GameLink.Value))
            .ToDictionary(x => x.GameLink.Value!, x => x, StringComparer.OrdinalIgnoreCase);
        var guids = assetsByGuid.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var trainCars = CarSpawner.Instance.AllCars.Where(x => x != null && guids.Contains(x.CarGUID)).ToArray();
        if (trainCars.Length < 3 || trainCars.Any(x => x.logicCar == null)) throw new InvalidOperationException("Deliver all three starter flatcars before creating the freight job.");
        trainCars = trainCars.Take(3).ToArray();
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

        var id = JobPrefix + new string(playerId.Where(char.IsLetterOrDigit).ToArray());
        var existing = AllJobs().FirstOrDefault(x => x != null && string.Equals(x.ID, id, StringComparison.Ordinal));
        var existingContract = snapshot.IndustrialContracts.SingleOrDefault(x => x.ContractId == id);
        if (existing != null && existingContract != null) return "Job " + existing.ID + " is already correlated with contract " + existingContract.State + ".";
        if (existing != null) throw new InvalidOperationException("The external job exists without its authoritative BDVM contract.");
        var sourceId = sourceStation.logicStation.ID.ToString();
        var destinationId = choice.Station.logicStation.ID.ToString();
        var cargoId = choice.Cargo.ToString();
        var quantity = cars.Sum(car => (decimal)Math.Max(0f, car.capacity));
        var assetIds = trainCars.Select(car => assetsByGuid[car.CarGUID].AssetId).ToArray();
        var engine = new IndustrialEconomyEngine(snapshot, new UnityRuntimeHostAuthority(), new UnityIndustrialExecutionPort(snapshot),
            new UnityCargoTransferObservationPort(snapshot), new UnityWagonCompatibilityPort(snapshot));
        EnsureStock(engine, snapshot, "starter-stock-source:" + id, sourceId, cargoId, quantity, quantity);
        EnsureStock(engine, snapshot, "starter-stock-destination:" + id, destinationId, cargoId, 0m, quantity * 4m);
        IndustrialContract contract;
        if (existingContract == null)
        {
            contract = engine.CreateTransportOffer(id, sourceId, destinationId, cargoId, quantity, AccountRef.Player(playerId), 15000, 0,
                snapshot.LeaseClock.ActiveTick, 0, new WagonRequirement
                {
                    CargoId = cargoId,
                    MinimumWagonCount = trainCars.Length,
                    MinimumTotalCapacity = quantity,
                    AllowedDefinitionIds = assetIds.Select(assetId => snapshot.Assets.Assets.Single(asset => asset.AssetId == assetId).DefinitionId).Distinct(StringComparer.Ordinal).ToList()
                }, 0);
            engine.Accept("starter-contract-accept:" + id, contract.ContractId, contract.Version, snapshot.LeaseClock.ActiveTick, 600, null);
            engine.AssignWagons("starter-contract-wagons:" + id, playerId, contract.ContractId, contract.Version, AssetOwnerRef.Player(playerId), assetIds, snapshot.LeaseClock.ActiveTick);
        }
        else
        {
            contract = existingContract;
            var sameAssets = contract.AssignedWagons.Select(value => value.AssetId).OrderBy(value => value, StringComparer.Ordinal)
                .SequenceEqual(assetIds.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal);
            if ((contract.State != IndustrialContractState.Reserved && contract.State != IndustrialContractState.Active && contract.State != IndustrialContractState.DeliveryPending) ||
                !sameAssets || !string.Equals(contract.OriginFacilityId, sourceId, StringComparison.Ordinal) ||
                !string.Equals(contract.DestinationFacilityId, destinationId, StringComparison.Ordinal) ||
                !string.Equals(contract.CargoId, cargoId, StringComparison.OrdinalIgnoreCase) || contract.Quantity != quantity)
                throw new InvalidOperationException("The persisted starter contract conflicts with the currently observed consist or route.");
        }
        if (!lifecycle.TryRegister(new SelfShuntExternalJobRegistration { OperationId = "starter-contract-register:" + id, JobId = id, StationId = sourceId, CargoId = cargoId }))
        {
            engine.Cancel("starter-contract-register-rollback:" + id, contract.ContractId);
            throw new InvalidOperationException("SelfShunt refused the authoritative external job registration.");
        }
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
        definition.PopulateBaseJobDefinition(sourceStation.logicStation, 7200f, 0f, chain, JobLicenses.Basic);
        var controller = new JobChainController(chainObject) { carsForJobChain = cars.ToList() };
        controller.AddJobDefinitionToChain(definition);
        controller.FinalizeSetupAndGenerateFirstJob(false);
        return "Created zero-wage SelfShunt job and BDVM contract " + id + ": " + sourceStation.stationInfo.YardID + " -> " + choice.Station.stationInfo.YardID + ", cargo=" + choice.Cargo + ", wagons=" + cars.Count + ".";
    }

    private static void EnsureStock(IndustrialEconomyEngine engine, VehicleAcquisitionSnapshot snapshot, string commandId, string facilityId, string cargoId, decimal onHand, decimal capacity)
    {
        var stock = snapshot.IndustrialStocks.SingleOrDefault(value => value.FacilityId == facilityId && value.CargoId == cargoId);
        if (stock == null) { engine.ConfigureStock(commandId, facilityId, cargoId, onHand, capacity); return; }
        if (stock.OnHand >= onHand && stock.Capacity >= capacity) return;
        engine.ConfigureStock(commandId, facilityId, cargoId, Math.Max(stock.OnHand, onHand), Math.Max(stock.Capacity, capacity));
    }

    private sealed class UnityRuntimeHostAuthority : INetworkRoleDetector
    {
        public NetworkRoleReport Detect() => new NetworkRoleDetector(new MultiplayerNetworkApiStateReader()).Detect();
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
