using System;
using System.Collections.Generic;
using System.Linq;
using BDVM.Domain;
using DV;
using DV.Logic.Job;
using DV.ThingTypes;
using HarmonyLib;

namespace BDVM.Adapters;

internal static class UnityIndustrialCargoTagGuard
{
    private static Func<VehicleAcquisitionSnapshot?> snapshot = () => null;
    private static INetworkRoleDetector? authority;
    private static Action persist = () => { };
    private static Action<string> log = _ => { };
    public static string LastRejection { get; private set; } = "";

    public static void Configure(Func<VehicleAcquisitionSnapshot?> state, INetworkRoleDetector role, Action save, Action<string>? logger = null)
    {
        snapshot = state ?? throw new ArgumentNullException(nameof(state)); authority = role ?? throw new ArgumentNullException(nameof(role)); persist = save ?? throw new ArgumentNullException(nameof(save)); log = logger ?? (_ => { });
    }

    public static bool HasStandaloneLoad(WarehouseMachine warehouse)
    {
        var state = snapshot(); if (state == null || warehouse?.WarehouseTrack == null || warehouse.GetCurrentLoadUnloadData(WarehouseTaskType.Loading).Count > 0) return false;
        return TaggedCarsOnTrack(state, warehouse).Any(value => value.Car.LoadedCargoAmount < value.Car.capacity - 0.01f);
    }

    public static bool HasStandaloneUnload(WarehouseMachine warehouse)
    {
        var state = snapshot(); if (state == null || warehouse?.WarehouseTrack == null || warehouse.GetCurrentLoadUnloadData(WarehouseTaskType.Unloading).Count > 0) return false;
        return ManagedCarsOnTrack(state, warehouse).Any(value => value.Car.LoadedCargoAmount > 0.01f && warehouse.SupportedCargoTypes.Contains(value.Car.CurrentCargoTypeInCar));
    }

    public static bool TryStandaloneLoad(WarehouseMachineController machine, out bool handled)
    {
        handled = false; var state = snapshot(); var warehouse = machine?.warehouseMachine;
        if (state == null || warehouse?.WarehouseTrack == null || authority == null || !NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out _) || warehouse.GetCurrentLoadUnloadData(WarehouseTaskType.Loading).Count > 0) return true;
        var rows = TaggedCarsOnTrack(state, warehouse).Where(value => value.Car.LoadedCargoAmount < value.Car.capacity - 0.01f).ToArray();
        if (rows.Length == 0) return true; handled = true;
        if (rows.Any(value => value.Fleet.OperationalState != FleetOperationalState.Available)) return Refuse("Loading refused: tagged rolling stock must be available in Dispatch.");
        var cargoIds = rows.Select(value => value.Tag.CargoId).Distinct(StringComparer.Ordinal).ToArray();
        var sources = rows.Select(value => value.Tag.SourceFacilityId).Distinct(StringComparer.Ordinal).ToArray();
        if (cargoIds.Length != 1 || sources.Length != 1 || !warehouse.WarehouseTrack.ID.FullDisplayID.StartsWith(sources[0] + "-", StringComparison.OrdinalIgnoreCase)) return Refuse("Loading refused: all tagged wagons must use this industry and the same cargo.");
        var cargo = ResolveCargo(cargoIds[0]); if (cargo == null || !warehouse.SupportedCargoTypes.Contains(cargo.Value)) return Refuse("Loading refused: the tagged cargo is not supported here.");
        var stock = state.IndustrialStocks.SingleOrDefault(value => value.FacilityId == sources[0] && value.CargoId == cargoIds[0]);
        var capacity = rows.Sum(value => (decimal)Math.Max(0f, value.Car.capacity - value.Car.LoadedCargoAmount)); var amount = Math.Min(stock?.OnHand ?? 0m, capacity);
        if (stock == null || amount <= 0m) return Refuse("Loading refused: this industry has no tagged cargo available.");
        var remaining = (float)amount; foreach (var row in rows) { var loaded = Math.Min(row.Car.capacity - row.Car.LoadedCargoAmount, remaining); if (loaded <= 0f) continue; AccessTools.Method(typeof(Car), "LoadCargo").Invoke(row.Car, new object[] { loaded, cargo.Value, warehouse }); remaining -= loaded; if (remaining <= 0.001f) break; } stock.OnHand -= amount; stock.Version++;
        foreach (var row in rows.Where(value => value.Tag.Lifetime == CargoTagLifetime.NextLoading)) state.IndustrialCargoTags.Remove(row.Tag);
        persist(); LastRejection = ""; log("[event=standalone-tag-loading] facility=" + sources[0] + ", cargo=" + cargoIds[0] + ", amount=" + amount); return true;
    }

    public static bool TryStandaloneUnload(WarehouseMachineController machine, out bool handled)
    {
        handled = false; var state = snapshot(); var warehouse = machine?.warehouseMachine;
        if (state == null || warehouse?.WarehouseTrack == null || authority == null || !NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out _) || warehouse.GetCurrentLoadUnloadData(WarehouseTaskType.Unloading).Count > 0) return true;
        var rows = ManagedCarsOnTrack(state, warehouse).Where(value => value.Car.LoadedCargoAmount > 0.01f && warehouse.SupportedCargoTypes.Contains(value.Car.CurrentCargoTypeInCar)).ToArray();
        if (rows.Length == 0) return true; handled = true;
        var cargoTypes = rows.Select(value => value.Car.CurrentCargoTypeInCar).Distinct().ToArray(); if (cargoTypes.Length != 1) return Refuse("Unloading refused: all wagons must contain the same cargo.");
        var cargoId = cargoTypes[0].ToString(); var facility = state.IndustrialStocks.Where(value => value.CargoId == cargoId && warehouse.WarehouseTrack.ID.FullDisplayID.StartsWith(value.FacilityId + "-", StringComparison.OrdinalIgnoreCase)).Select(value => value.FacilityId).Distinct().SingleOrDefault();
        var stock = facility == null ? null : state.IndustrialStocks.Single(value => value.FacilityId == facility && value.CargoId == cargoId); if (stock == null) return Refuse("Unloading refused: this industry does not receive that cargo.");
        var free = stock.Capacity - stock.OnHand; var selected = new List<Car>(); decimal amount = 0m;
        foreach (var row in rows) { var loaded = (decimal)row.Car.LoadedCargoAmount; if (amount + loaded <= free + 0.01m) { selected.Add(row.Car); amount += loaded; } }
        if (selected.Count == 0 || amount <= 0m) return Refuse("Unloading refused: the destination industry has insufficient free capacity.");
        foreach (var car in selected) AccessTools.Method(typeof(Car), "UnloadCargo").Invoke(car, new object[] { car.LoadedCargoAmount, cargoTypes[0], warehouse }); stock.OnHand += amount; stock.Version++;
        foreach (var row in rows.Where(value => selected.Contains(value.Car))) { var tag = state.IndustrialCargoTags.SingleOrDefault(value => value.AssetId == row.Fleet.AssetId); if (tag?.Lifetime == CargoTagLifetime.UntilEmpty) state.IndustrialCargoTags.Remove(tag); }
        persist(); LastRejection = ""; log("[event=standalone-tag-unloading] facility=" + facility + ", cargo=" + cargoId + ", amount=" + amount); return true;
    }

    private static CargoType? ResolveCargo(string cargoId) => Globals.G.Types.cargos.FirstOrDefault(value => value != null && (string.Equals(value.v1.ToString(), cargoId, StringComparison.OrdinalIgnoreCase) || string.Equals(value.id, cargoId, StringComparison.OrdinalIgnoreCase)))?.v1;
    private static IEnumerable<(FleetAssetState Fleet, Car Car)> ManagedCarsOnTrack(VehicleAcquisitionSnapshot state, WarehouseMachine warehouse) => state.Fleet.Where(value => value.Kind == FleetVehicleKind.FreightWagon).Select(value => (Fleet:value, Car:UnityRollingStockResolver.Resolve(state, value.AssetId)?.logicCar)).Where(value => value.Car != null && value.Car.CurrentTrack == warehouse.WarehouseTrack).Select(value => (value.Fleet, value.Car!));
    private static IEnumerable<(FleetAssetState Fleet, Car Car, IndustrialCargoTag Tag)> TaggedCarsOnTrack(VehicleAcquisitionSnapshot state, WarehouseMachine warehouse) => ManagedCarsOnTrack(state, warehouse).Select(value => (value.Fleet, value.Car, Tag:state.IndustrialCargoTags.SingleOrDefault(tag => tag.AssetId == value.Fleet.AssetId))).Where(value => value.Tag != null).Select(value => (value.Fleet, value.Car, value.Tag!));

    public static bool ValidateLoad(WarehouseMachineController machine)
    {
        var state = snapshot();
        if (state == null || authority == null || !NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out _)) return true;
        var warehouse = machine?.warehouseMachine;
        var track = warehouse?.WarehouseTrack;
        if (track == null || warehouse == null) return true;
        var cars = state.Fleet.Where(value => value.Kind == FleetVehicleKind.FreightWagon)
            .Select(value => new { Fleet = value, Car = UnityRollingStockResolver.Resolve(state, value.AssetId), Tag = state.IndustrialCargoTags.SingleOrDefault(tag => tag.AssetId == value.AssetId) })
            .Where(value => value.Car?.logicCar?.CurrentTrack == track).ToArray();
        if (cars.Length == 0) return true;
        if (cars.Any(value => value.Fleet.OperationalState == FleetOperationalState.Stored || value.Fleet.OperationalState == FleetOperationalState.Maintenance))
            return Refuse("Loading refused: stored or maintenance rolling stock must be made available in Dispatch.");
        var missing = cars.FirstOrDefault(value => value.Tag == null);
        if (missing != null) return Refuse("Loading refused: wagon " + missing.Fleet.DisplayName + " has no cargo tag in Dispatch.");
        foreach (var item in cars)
        {
            if (!string.IsNullOrWhiteSpace(item.Tag!.SourceFacilityId) &&
                !track.ID.FullDisplayID.StartsWith(item.Tag.SourceFacilityId + "-", StringComparison.OrdinalIgnoreCase))
                return Refuse("Loading refused atomically: this cargo tag belongs to industry " + item.Tag.SourceFacilityId + ".");
            var cargo = Globals.G.Types.cargos.FirstOrDefault(value => value != null && (string.Equals(value.v1.ToString(), item.Tag!.CargoId, StringComparison.OrdinalIgnoreCase) || string.Equals(value.id, item.Tag.CargoId, StringComparison.OrdinalIgnoreCase)));
            if (cargo == null || !warehouse.SupportedCargoTypes.Contains(cargo.v1))
                return Refuse("Loading refused atomically: cargo tag " + item.Tag!.CargoId + " is incompatible with this station.");
            var loaded = item.Car!.logicCar;
            if (loaded.LoadedCargoAmount > 0.01f && loaded.CurrentCargoTypeInCar != cargo.v1)
                return Refuse("Loading refused atomically: a partially loaded wagon cannot change cargo tag.");
        }
        foreach (var jobData in warehouse.GetCurrentLoadUnloadData(WarehouseTaskType.Loading))
        {
            var task = jobData?.tasksAvailableToProcess?.FirstOrDefault();
            var dossier = state.IndustrialContracts.SingleOrDefault(value => value.StockDriven && value.ContractId == (jobData?.id ?? "") && !Terminal(value.State));
            if (task == null || dossier == null) continue;
            var source = state.IndustrialStocks.Single(value => value.FacilityId == dossier.OriginFacilityId && value.CargoId == dossier.CargoId);
            var remaining = Math.Max(0m, Math.Min(source.OnHand, dossier.Quantity - dossier.Manifests.Sum(value => value.LoadedQuantity)));
            if (remaining <= 0m) return Refuse("Loading refused: no tagged cargo remains at this company.");
            AccessTools.Field(typeof(WarehouseTask), "cargoAmount").SetValue(task, (float)Math.Min(remaining, cars.Sum(value => (decimal)Math.Max(0f, value.Car!.logicCar.capacity - value.Car.logicCar.LoadedCargoAmount))));
        }
        LastRejection = "";
        return true;
    }

    public static bool PrepareUnload(WarehouseMachineController machine)
    {
        var state = snapshot(); var warehouse = machine?.warehouseMachine;
        if (state == null || warehouse == null || authority == null || !NetworkAuthorityPolicy.CanExecuteEconomy(authority.Detect(), out _)) return true;
        foreach (var jobData in warehouse.GetCurrentLoadUnloadData(WarehouseTaskType.Unloading))
        {
            var task = jobData?.tasksAvailableToProcess?.FirstOrDefault();
            var dossier = state.IndustrialContracts.SingleOrDefault(value => value.StockDriven && value.ContractId == (jobData?.id ?? "") && !Terminal(value.State));
            if (task == null || dossier == null) continue;
            var destination = state.IndustrialStocks.Single(value => value.FacilityId == dossier.DestinationFacilityId && value.CargoId == dossier.CargoId);
            var free = Math.Max(0m, destination.Capacity - destination.OnHand);
            if (free <= 0m) return Refuse("Unloading refused: the destination company is full; store the consist and retry after it consumes stock.");
            var onboard = task.cars.Sum(value => (decimal)Math.Max(0f, value.LoadedCargoAmount));
            AccessTools.Field(typeof(WarehouseTask), "cargoAmount").SetValue(task, (float)Math.Min(onboard, free));
        }
        LastRejection = ""; return true;
    }

    private static bool Terminal(IndustrialContractState state) => state == IndustrialContractState.Completed || state == IndustrialContractState.Cancelled || state == IndustrialContractState.Expired;

    private static bool Refuse(string reason) { LastRejection = reason; log("[event=industrial-cargo-tag-refused] " + reason); return false; }
}

[HarmonyPatch(typeof(WarehouseMachineController), "StartLoadSequence")]
internal static class UnityIndustrialCargoTagLoadPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    private static bool Prefix(WarehouseMachineController __instance) { var allowed = UnityIndustrialCargoTagGuard.TryStandaloneLoad(__instance, out var handled); return handled ? false : allowed && UnityIndustrialCargoTagGuard.ValidateLoad(__instance); }
}

[HarmonyPatch(typeof(WarehouseMachineController), "StartUnloadSequence")]
internal static class UnityIndustrialCargoTagUnloadPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    private static bool Prefix(WarehouseMachineController __instance) { var allowed = UnityIndustrialCargoTagGuard.TryStandaloneUnload(__instance, out var handled); return handled ? false : allowed && UnityIndustrialCargoTagGuard.PrepareUnload(__instance); }
}

[HarmonyPatch(typeof(WarehouseMachine), nameof(WarehouseMachine.AnyTrainToLoadPresentOnTrack))]
internal static class UnityStandaloneTagLoadAvailabilityPatch { [HarmonyPostfix] private static void Postfix(WarehouseMachine __instance, ref bool __result) { if (!__result) __result = UnityIndustrialCargoTagGuard.HasStandaloneLoad(__instance); } }

[HarmonyPatch(typeof(WarehouseMachine), nameof(WarehouseMachine.AnyTrainToUnloadPresentOnTrack))]
internal static class UnityStandaloneTagUnloadAvailabilityPatch { [HarmonyPostfix] private static void Postfix(WarehouseMachine __instance, ref bool __result) { if (!__result) __result = UnityIndustrialCargoTagGuard.HasStandaloneUnload(__instance); } }
