using System;
using System.Collections.Generic;
using System.Linq;
using BDVM.Domain;
using DV;
using DV.Logic.Job;
using DV.ThingTypes;

namespace BDVM.Adapters;

internal static class UnityRollingStockResolver
{
    public static TrainCar? Resolve(VehicleAcquisitionSnapshot snapshot, string assetId)
    {
        var asset = snapshot.Assets.Assets.SingleOrDefault(value => value.AssetId == assetId);
        if (asset == null || asset.GameLink.State != PersistentLinkState.Resolved || string.IsNullOrWhiteSpace(asset.GameLink.Value)) return null;
        return TrainCarRegistry.Instance?.GetTrainCarByCarGuid(asset.GameLink.Value);
    }

    public static string[] PersistentGuids(VehicleAcquisitionSnapshot snapshot, IEnumerable<string> assetIds)
    {
        return assetIds.Select(assetId => snapshot.Assets.Assets.SingleOrDefault(value => value.AssetId == assetId)?.GameLink.Value)
            .Select(value => Guid.TryParse(value, out var parsed) && parsed != Guid.Empty ? parsed.ToString("D") : "")
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static CargoType_v2? ResolveCargo(string cargoId)
    {
        if (Globals.G?.Types?.cargos == null || string.IsNullOrWhiteSpace(cargoId)) return null;
        return Globals.G.Types.cargos.SingleOrDefault(cargo => cargo != null &&
            (string.Equals(cargo.id, cargoId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(cargo.v1.ToString(), cargoId, StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed class UnityWagonCompatibilityPort : IWagonCompatibilityPort
{
    private readonly VehicleAcquisitionSnapshot snapshot;
    public UnityWagonCompatibilityPort(VehicleAcquisitionSnapshot snapshot) => this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public WagonCompatibility Inspect(string assetId, string definitionId, string cargoId)
    {
        var car = UnityRollingStockResolver.Resolve(snapshot, assetId);
        var cargo = UnityRollingStockResolver.ResolveCargo(cargoId);
        if (car == null || car.logicCar == null) return Refused("persistent-wagon-not-present");
        if (!string.Equals(car.carLivery?.id, definitionId, StringComparison.Ordinal)) return Refused("definition-mismatch");
        if (cargo == null || DVObjectModel.current?.CargoToLoadableCarTypes == null ||
            !DVObjectModel.current.CargoToLoadableCarTypes.TryGetValue(cargo, out var types) ||
            !types.Contains(car.logicCar.carType.parentType)) return Refused("cargo-incompatible");
        var capacity = (decimal)Math.Max(0f, car.logicCar.capacity);
        return capacity > 0m
            ? new WagonCompatibility { Compatible = true, Capacity = capacity, Detail = "unity-wagon-compatible" }
            : Refused("wagon-capacity-unavailable");
    }

    private static WagonCompatibility Refused(string detail) => new WagonCompatibility { Compatible = false, Capacity = 0m, Detail = detail };
}

public sealed class UnityCargoTransferObservationPort : ICargoTransferObservationPort
{
    private const decimal Tolerance = 0.02m;
    private readonly VehicleAcquisitionSnapshot snapshot;
    public UnityCargoTransferObservationPort(VehicleAcquisitionSnapshot snapshot) => this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public WorldOwnershipOutcome InspectLoading(string operationId, string contractId, string assetId, decimal cumulativeQuantity)
    {
        var contract = snapshot.IndustrialContracts.SingleOrDefault(value => value.ContractId == contractId);
        var car = UnityRollingStockResolver.Resolve(snapshot, assetId);
        var cargo = contract == null ? null : UnityRollingStockResolver.ResolveCargo(contract.CargoId);
        if (contract == null || car?.logicCar == null || cargo == null) return WorldOwnershipOutcome.Unknown;
        var amount = (decimal)Math.Max(0f, car.logicCar.LoadedCargoAmount);
        if (cumulativeQuantity > Tolerance && car.logicCar.CurrentCargoTypeInCar != cargo.v1) return WorldOwnershipOutcome.NotApplied;
        return Math.Abs(amount - cumulativeQuantity) <= Tolerance ? WorldOwnershipOutcome.Applied : WorldOwnershipOutcome.NotApplied;
    }

    public WorldOwnershipOutcome InspectUnloading(string operationId, string contractId, string assetId, decimal cumulativeQuantity)
    {
        var contract = snapshot.IndustrialContracts.SingleOrDefault(value => value.ContractId == contractId);
        var manifest = contract?.Manifests.SingleOrDefault(value => value.AssetId == assetId);
        var car = UnityRollingStockResolver.Resolve(snapshot, assetId);
        var cargo = contract == null ? null : UnityRollingStockResolver.ResolveCargo(contract.CargoId);
        if (manifest == null || car?.logicCar == null || cargo == null) return WorldOwnershipOutcome.Unknown;
        var expectedOnBoard = Math.Max(0m, manifest.LoadedQuantity - cumulativeQuantity);
        var amount = (decimal)Math.Max(0f, car.logicCar.LoadedCargoAmount);
        if (expectedOnBoard > Tolerance && car.logicCar.CurrentCargoTypeInCar != cargo.v1) return WorldOwnershipOutcome.NotApplied;
        return Math.Abs(amount - expectedOnBoard) <= Tolerance ? WorldOwnershipOutcome.Applied : WorldOwnershipOutcome.NotApplied;
    }
}

public sealed class UnityIndustrialExecutionPort : IIndustrialExecutionPort
{
    private readonly VehicleAcquisitionSnapshot snapshot;
    public UnityIndustrialExecutionPort(VehicleAcquisitionSnapshot snapshot) => this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    public bool Available => JobsManager.Instance != null && TrainCarRegistry.Instance != null;

    public WorldOwnershipOutcome InspectDelivery(string operationId, string contractId, decimal cumulativeQuantity)
    {
        var contract = snapshot.IndustrialContracts.SingleOrDefault(value => value.ContractId == contractId);
        if (contract == null || contract.AssignedWagons.Count == 0 || cumulativeQuantity < 0m || cumulativeQuantity > contract.Quantity) return WorldOwnershipOutcome.NotApplied;
        var guids = UnityRollingStockResolver.PersistentGuids(snapshot, contract.AssignedWagons.Select(value => value.AssetId));
        if (guids.Length != contract.AssignedWagons.Count) return WorldOwnershipOutcome.Unknown;
        return new UnityMissionLifecyclePort().Inspect(contractId, guids);
    }
}

public sealed class AttestedIndustrialExecutionPort : IIndustrialExecutionPort
{
    private readonly string operationId;
    private readonly string contractId;
    private readonly decimal cumulativeQuantity;
    public AttestedIndustrialExecutionPort(string operationId, string contractId, decimal cumulativeQuantity)
    {
        this.operationId = operationId;
        this.contractId = contractId;
        this.cumulativeQuantity = cumulativeQuantity;
    }
    public bool Available => true;
    public WorldOwnershipOutcome InspectDelivery(string observedOperationId, string observedContractId, decimal observedCumulativeQuantity) =>
        string.Equals(operationId, observedOperationId, StringComparison.Ordinal) &&
        string.Equals(contractId, observedContractId, StringComparison.Ordinal) &&
        cumulativeQuantity == observedCumulativeQuantity
            ? WorldOwnershipOutcome.Applied
            : WorldOwnershipOutcome.NotApplied;
}
