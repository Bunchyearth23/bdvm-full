using System;
using System.Collections.Generic;
using System.Linq;
using DV.InventorySystem;
using DV.Logic.Job;
using BDVM.Domain;
using UnityEngine;

namespace BDVM.Adapters;

public sealed class UnityHostWalletAdapter
{
    public long ReadBalance()
    {
        var inventory = Inventory.Instance ?? throw new InvalidOperationException("Vanilla Inventory is unavailable.");
        if (double.IsNaN(inventory.PlayerMoney) || double.IsInfinity(inventory.PlayerMoney) || inventory.PlayerMoney < 0 || inventory.PlayerMoney > long.MaxValue)
            throw new InvalidOperationException("Vanilla wallet balance is invalid or out of range.");
        return Convert.ToInt64(Math.Floor(inventory.PlayerMoney));
    }

    public bool TryDebit(long amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        return Inventory.Instance != null && Inventory.Instance.RemoveMoney(amount);
    }

    public void Credit(long amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var inventory = Inventory.Instance ?? throw new InvalidOperationException("Vanilla Inventory is unavailable.");
        inventory.AddMoney(amount);
    }
}

public sealed class UnityExistingVehicleOwnershipAdapter : IExistingVehicleOwnershipAdapter
{
    public WorldOwnershipOutcome ApplyOwner(string operationId, string persistentCarGuid, AssetOwnerRef owner) => Inspect(persistentCarGuid);
    public WorldOwnershipOutcome InspectOwner(string persistentCarGuid, AssetOwnerRef owner) => Inspect(persistentCarGuid);

    private static WorldOwnershipOutcome Inspect(string persistentCarGuid)
    {
        if (!Guid.TryParse(persistentCarGuid, out var expected) || expected == Guid.Empty)
            return WorldOwnershipOutcome.NotApplied;
        var matches = UnityEngine.Object.FindObjectsOfType<TrainCar>()
            .Where(x => x != null && Guid.TryParse(x.CarGUID, out var actual) && actual == expected).Take(2).Count();
        return matches == 1 ? WorldOwnershipOutcome.Applied : matches == 0 ? WorldOwnershipOutcome.NotApplied : WorldOwnershipOutcome.Unknown;
    }
}

public sealed class UnityAssetReleaseGuard : IAssetBundleReleaseGuard
{
    public AssetReleaseInspection Inspect(string persistentCarGuid)
    {
        if (!Guid.TryParse(persistentCarGuid, out var expected) || expected == Guid.Empty)
            return new AssetReleaseInspection { Status = AssetReleaseStatus.Blocked, Detail = "invalid-persistent-car-guid" };
        var matches = UnityEngine.Object.FindObjectsOfType<TrainCar>()
            .Where(x => x != null && Guid.TryParse(x.CarGUID, out var actual) && actual == expected).Take(2).ToArray();
        if (matches.Length != 1)
            return new AssetReleaseInspection { Status = matches.Length == 0 ? AssetReleaseStatus.Unknown : AssetReleaseStatus.Blocked, Detail = matches.Length == 0 ? "vehicle-not-visible" : "ambiguous-persistent-car-guid" };
        var car = matches[0];
        if (car.derailed) return new AssetReleaseInspection { Status = AssetReleaseStatus.Blocked, Detail = "vehicle-derailed" };
        if (car.trainset?.cars != null && car.trainset.cars.Count > 1)
            return new AssetReleaseInspection { Status = AssetReleaseStatus.Blocked, Detail = "vehicle-coupled; sell a complete validated bundle or uncouple it" };
        if (car.logicCar != null && car.logicCar.LoadedCargoAmount > 0.001f)
            return new AssetReleaseInspection { Status = AssetReleaseStatus.Blocked, Detail = "vehicle-loaded" };
        var job = car.logicCar == null || JobsManager.Instance == null ? null : JobsManager.Instance.GetJobOfCar(car.logicCar);
        if (job != null && string.Equals(job.State.ToString(), "InProgress", StringComparison.Ordinal))
            return new AssetReleaseInspection { Status = AssetReleaseStatus.Blocked, Detail = "vehicle-engaged-in-job" };
        return new AssetReleaseInspection { Status = AssetReleaseStatus.Releasable, Detail = "visible, uncoupled, unloaded, not derailed and not assigned to an active job" };
    }

    public IReadOnlyDictionary<string, AssetReleaseInspection> InspectBundle(IReadOnlyList<string> persistentCarGuids)
    {
        var requested = (persistentCarGuids ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var results = new Dictionary<string, AssetReleaseInspection>(StringComparer.OrdinalIgnoreCase);
        var expected = new HashSet<Guid>();
        foreach (var value in requested)
        {
            if (!Guid.TryParse(value, out var guid) || guid == Guid.Empty)
            {
                results[value ?? ""] = new AssetReleaseInspection { Status = AssetReleaseStatus.Blocked, Detail = "invalid-persistent-car-guid" };
                continue;
            }
            expected.Add(guid);
        }
        var allCars = UnityEngine.Object.FindObjectsOfType<TrainCar>();
        foreach (var value in requested)
        {
            if (results.ContainsKey(value)) continue;
            var guid = Guid.Parse(value);
            var matches = allCars.Where(x => x != null && Guid.TryParse(x.CarGUID, out var actual) && actual == guid).Take(2).ToArray();
            if (matches.Length != 1)
            {
                results[value] = new AssetReleaseInspection { Status = matches.Length == 0 ? AssetReleaseStatus.Unknown : AssetReleaseStatus.Blocked, Detail = matches.Length == 0 ? "vehicle-not-visible" : "ambiguous-persistent-car-guid" };
                continue;
            }
            var car = matches[0];
            if (car.derailed) { results[value] = new AssetReleaseInspection { Status = AssetReleaseStatus.Blocked, Detail = "vehicle-derailed" }; continue; }
            if (car.logicCar != null && car.logicCar.LoadedCargoAmount > 0.001f) { results[value] = new AssetReleaseInspection { Status = AssetReleaseStatus.Blocked, Detail = "vehicle-loaded" }; continue; }
            var job = car.logicCar == null || JobsManager.Instance == null ? null : JobsManager.Instance.GetJobOfCar(car.logicCar);
            if (job != null && string.Equals(job.State.ToString(), "InProgress", StringComparison.Ordinal)) { results[value] = new AssetReleaseInspection { Status = AssetReleaseStatus.Blocked, Detail = "vehicle-engaged-in-job" }; continue; }
            var coupledOutsideBundle = car.trainset?.cars != null && car.trainset.cars.Any(x => x == null || !Guid.TryParse(x.CarGUID, out var coupledGuid) || !expected.Contains(coupledGuid));
            results[value] = coupledOutsideBundle
                ? new AssetReleaseInspection { Status = AssetReleaseStatus.Blocked, Detail = "vehicle-coupled-outside-bundle" }
                : new AssetReleaseInspection { Status = AssetReleaseStatus.Releasable, Detail = "visible, unloaded, not derailed, not assigned to an active job and coupled only inside the complete bundle" };
        }
        return results;
    }
}

public sealed class DelegateAcquisitionCheckpointSink : IAcquisitionCheckpointSink
{
    private readonly Action<string, AcquisitionState> stage;
    public DelegateAcquisitionCheckpointSink(Action<string, AcquisitionState> stage) => this.stage = stage ?? throw new ArgumentNullException(nameof(stage));
    public void Save(string checkpointId, VehicleAcquisitionSnapshot snapshot, AcquisitionState state) => stage(checkpointId, state);
}
