using System;
using BDVM.Domain;
using UnityEngine;

namespace BDVM.Adapters;

public static class UnityFleetDeletionReconciliation
{
    private static AcquisitionRuntimeStateProvider? provider;
    private static INetworkRoleDetector? authority;
    private static Func<bool>? persist;
    private static Action<string>? log;
    private static CarSpawner? attached;

    public static void Configure(AcquisitionRuntimeStateProvider stateProvider, INetworkRoleDetector roleDetector, Func<bool> persistState, Action<string> logger)
    { provider = stateProvider; authority = roleDetector; persist = persistState; log = logger; TryAttach(); }

    public static void TryAttach()
    {
        var spawner = CarSpawner.Instance;
        if (spawner == null || ReferenceEquals(spawner, attached)) return;
        if (attached != null) attached.CarAboutToBeDeleted -= OnCarAboutToBeDeleted;
        attached = spawner;
        attached.CarAboutToBeDeleted += OnCarAboutToBeDeleted;
        log?.Invoke("[correlation=fleet-deletion] [event=car-delete-listener-attached]");
    }

    public static void Reset()
    {
        if (attached != null) attached.CarAboutToBeDeleted -= OnCarAboutToBeDeleted;
        attached = null; provider = null; authority = null; persist = null; log = null;
    }

    private static void OnCarAboutToBeDeleted(TrainCar car)
    {
        if (car == null || string.IsNullOrWhiteSpace(car.CarGUID) || provider?.Current == null || authority == null ||
            !NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out _)) return;
        var managed = provider.Current.Assets.Assets.Exists(asset => string.Equals(asset.GameLink?.Value, car.CarGUID, StringComparison.OrdinalIgnoreCase));
        if (!managed) return;
        try
        {
            var commandId = "host-radio-clear:" + car.CarGUID.ToLowerInvariant();
            var record = provider.ConfirmPhysicalFleetRemoval(commandId, car.CarGUID, "CarSpawner.CarAboutToBeDeleted", authority);
            if (persist == null || !persist()) throw new InvalidOperationException("Physical fleet removal could not be staged in SaveGameData.");
            log?.Invoke("[correlation=" + commandId + "] [event=fleet-physical-removal] asset=" + record.AssetId + ", result=" + record.ResultCode);
        }
        catch (Exception exception) { log?.Invoke("[correlation=fleet-deletion] [event=fleet-physical-removal-failed] carGuid=" + car.CarGUID + ", error=" + exception.Message); }
    }
}
