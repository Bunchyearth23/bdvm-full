using System;
using System.Linq;
using BDVM.Domain;
using BDVM.SelfShuntBridge;

namespace BDVM.Adapters;

public sealed class UnitySelfShuntIndustrialSink : ISelfShuntIndustrialLifecycleSink
{
    private readonly AcquisitionRuntimeStateProvider provider;
    private readonly INetworkRoleDetector authority;
    private readonly Func<bool> checkpoint;
    private readonly Action<string> log;

    public UnitySelfShuntIndustrialSink(AcquisitionRuntimeStateProvider provider, INetworkRoleDetector authority, Func<bool> checkpoint, Action<string>? log = null)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        this.checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        this.log = log ?? (_ => { });
    }

    public bool TryObserveExternalJob(string operationId, string jobId, string stationId, string cargoId) => Execute(operationId, jobId, () =>
    {
        var contract = Contract(jobId);
        if (contract.State != IndustrialContractState.Reserved && contract.State != IndustrialContractState.Active && contract.State != IndustrialContractState.DeliveryPending)
            throw new InvalidOperationException("External SelfShunt job does not reference a prepared BDVM contract.");
        if (contract.AssignedWagons.Count == 0 || !string.Equals(contract.CargoId, cargoId, StringComparison.OrdinalIgnoreCase) ||
            (!string.Equals(contract.OriginFacilityId, stationId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(stationId)))
            throw new InvalidOperationException("External SelfShunt job metadata does not match the BDVM contract.");
    });

    public bool TryObserveLoading(string operationId, string jobId, decimal cumulativeQuantity) => Execute(operationId, jobId, () =>
    {
        var snapshot = Snapshot(); var contract = Contract(jobId); var tick = snapshot.LeaseClock.ActiveTick;
        var engine = Engine(snapshot);
        if (contract.State == IndustrialContractState.Reserved) engine.Activate(operationId + ":activate", contract.ContractId, tick);
        ReconcilePhysicalManifests(engine, snapshot, contract, operationId, tick, loading: true);
        if (contract.Manifests.Sum(value => value.LoadedQuantity) + 0.02m < cumulativeQuantity)
            throw new InvalidOperationException("SelfShunt loading event exceeds physically observed assigned-wagon cargo.");
    });

    public bool TryObserveDelivery(string operationId, string jobId, decimal cumulativeQuantity) => Execute(operationId, jobId, () =>
    {
        var snapshot = Snapshot(); var contract = Contract(jobId); var tick = snapshot.LeaseClock.ActiveTick;
        var engine = Engine(snapshot);
        ReconcilePhysicalManifests(engine, snapshot, contract, operationId, tick, loading: false);
        if (contract.DeliveredQuantity + 0.02m < cumulativeQuantity)
            throw new InvalidOperationException("SelfShunt delivery event exceeds physically observed assigned-wagon unloading.");
    });

    public bool TryObserveCancellation(string operationId, string jobId, bool expired) => Execute(operationId, jobId, () =>
    {
        var contract = Contract(jobId);
        if (contract.State == IndustrialContractState.Completed || contract.State == IndustrialContractState.Cancelled || contract.State == IndustrialContractState.Expired) return;
        Engine(Snapshot()).Cancel(operationId, contract.ContractId);
    });

    public bool TryResumeProduction(string operationId, string jobId) => Execute(operationId, jobId, () =>
    {
        var snapshot = Snapshot(); var contract = Contract(jobId); var engine = Engine(snapshot); var tick = snapshot.LeaseClock.ActiveTick;
        if (contract.State == IndustrialContractState.Active || contract.State == IndustrialContractState.DeliveryPending)
            ReconcilePhysicalManifests(engine, snapshot, contract, operationId + ":completion", tick, loading: false);
        if (contract.State != IndustrialContractState.Completed) throw new InvalidOperationException("Production cannot resume before the correlated contract is complete.");
        foreach (var recipe in snapshot.IndustrialRecipes.Where(value => value.FacilityId == contract.OriginFacilityId || value.FacilityId == contract.DestinationFacilityId)
                     .OrderBy(value => value.RecipeId).ToArray())
            engine.AdvanceProduction(operationId + ":" + recipe.RecipeId, recipe.RecipeId, tick);
    });

    private static void ReconcilePhysicalManifests(IndustrialEconomyEngine engine, VehicleAcquisitionSnapshot snapshot, IndustrialContract contract, string operationId, long tick, bool loading)
    {
        foreach (var manifest in contract.Manifests.ToArray())
        {
            var car = UnityRollingStockResolver.Resolve(snapshot, manifest.AssetId) ?? throw new InvalidOperationException("Assigned wagon is not physically present.");
            var onBoard = (decimal)Math.Max(0f, car.LoadedCargoAmount);
            if (loading)
            {
                if (onBoard > manifest.LoadedQuantity + 0.02m)
                    engine.RecordLoading(operationId + ":" + manifest.AssetId + ":" + onBoard, contract.ContractId, manifest.AssetId, onBoard, tick);
            }
            else
            {
                var unloaded = Math.Max(0m, manifest.LoadedQuantity - onBoard);
                if (unloaded > manifest.UnloadedQuantity + 0.02m)
                    engine.RecordUnloading(operationId + ":" + manifest.AssetId + ":" + unloaded, contract.ContractId, manifest.AssetId, unloaded, tick);
            }
        }
    }

    private bool Execute(string operationId, string jobId, Action action)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(operationId) || string.IsNullOrWhiteSpace(jobId)) return false;
            if (!NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out var reason)) throw new InvalidOperationException(reason);
            action();
            IndustrialEconomyValidation.Validate(Snapshot());
            if (!checkpoint()) throw new InvalidOperationException("Industrial lifecycle checkpoint failed.");
            log("[event=selfshunt-industrial-applied] operation=" + operationId + ", job=" + jobId);
            return true;
        }
        catch (Exception exception)
        {
            log("[event=selfshunt-industrial-refused] operation=" + operationId + ", job=" + jobId + ", error=" + exception.Message);
            return false;
        }
    }

    private VehicleAcquisitionSnapshot Snapshot() => provider.Current ?? throw new InvalidOperationException("BDVM runtime state is unavailable.");
    private IndustrialContract Contract(string jobId) => Snapshot().IndustrialContracts.Single(value => value.ContractId == jobId);
    private IndustrialEconomyEngine Engine(VehicleAcquisitionSnapshot snapshot) => new IndustrialEconomyEngine(snapshot, authority,
        new UnityIndustrialExecutionPort(snapshot), new UnityCargoTransferObservationPort(snapshot), new UnityWagonCompatibilityPort(snapshot));
}
