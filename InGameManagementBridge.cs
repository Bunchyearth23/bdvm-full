using System;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using BDVM.Adapters;
using BDVM.Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BDVM;

public static partial class Main
{
    private static InGameManagementPanel? managementPanel;
    private static long deliveryTrackGeneration = -1;
    private static System.Collections.Generic.IReadOnlyList<InitialDeliveryTrackRule> discoveredDeliveryTracks = Array.Empty<InitialDeliveryTrackRule>();
    private static System.Collections.Generic.IReadOnlyList<InitialDeliveryTrackRule> DiscoveredDeliveryTracks()
    {
        var generation = Volatile.Read(ref economicWorkerGeneration);
        if (generation != deliveryTrackGeneration || discoveredDeliveryTracks.Count == 0)
        {
            discoveredDeliveryTracks = BDVMStarterDeliveryRadio.DiscoverDeliveryTracks();
            deliveryTrackGeneration = generation;
        }
        return discoveredDeliveryTracks;
    }
    private static bool inGameAwaitingRemote; private static string inGameReadContext = "";
    private static string InGameContext() => Volatile.Read(ref economicWorkerGeneration) + ":" +
        runtimeRoleDetector?.Detect().Role + ":" + runtimeStateProvider?.LocalPlayerId + ":" +
        (configuredClient == null ? "none" : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(configuredClient).ToString());

    private static Task<InGameManagementView>? ReadInGameManagement()
    {
        var currentContext = InGameContext();
        if (inGameReadContext != currentContext) { inGameAwaitingRemote = false; inGameReadContext = currentContext; }
        if (runtimeRoleDetector?.Detect().Role == NetworkRole.MultiplayerClient)
        {
            if (!inGameAwaitingRemote)
            {
                lock (clientStateGate) clientStateReceivedAt = default;
                RequestClientAuthoritativeState();
                inGameAwaitingRemote = true;
            }
            string json;
            lock (clientStateGate)
            {
                if (clientStateTransferPending)
                {
                    if (clientStateRequestId != null && clientRequestTracker?.ResultOrTimeout(clientStateRequestId, DateTimeOffset.UtcNow).Code == "request-timeout")
                    {
                        inGameAwaitingRemote = false;
                        throw new InvalidOperationException("The host did not answer in time. Refresh to retry.");
                    }
                    return null;
                }
                if (clientAuthoritativeState == null) { inGameAwaitingRemote = false; throw new InvalidOperationException("The host has not provided Management data. Refresh to retry."); }
                json = clientAuthoritativeState;
            }
            inGameAwaitingRemote = false;
            return SnapshotWorker.Run<InGameManagementView>(() => json, raw => InGameManagementModel.Build(JObject.Parse((string)raw), true));
        }
        inGameAwaitingRemote = false;
        return SnapshotWorker.Run<InGameManagementView>(() => CaptureDispatchSnapshot("in-game", true),
            raw => InGameManagementModel.Build(JObject.FromObject(raw, JsonSerializer.Create(webJsonSettings)), false));
    }

    private static string ExecuteInGameManagement(string payload)
    {
        try
        {
            var command = JObject.Parse(payload);
            if ((string?)command["action"] == "host.grant-money") return GrantHostMoney((string?)command["correlationId"] ?? "");
            return NormalizeInGameReceipt(HandleRemoteDispatchIntent("in-game", payload, true));
        }
        catch (UnauthorizedAccessException ex) { return JsonConvert.SerializeObject(new { status = "refused", message = ex.Message }); }
        catch (ArgumentException ex) { return JsonConvert.SerializeObject(new { status = "refused", message = ex.Message }); }
        catch (InvalidOperationException ex) when (ex.Message.IndexOf("insufficient funds", StringComparison.OrdinalIgnoreCase) >= 0)
        { return JsonConvert.SerializeObject(new { status = "refused", message = "There is not enough money in the selected account." }); }
        // Other failures may occur after a native effect. Keep the same request for reconciliation.
    }

    private static string GrantHostMoney(string requestId)
    {
        var allowed = runtimeRoleDetector != null &&
            NetworkAuthorityPolicy.CanExecuteEconomy(runtimeRoleDetector.Detect(), out _) &&
            runtimeStateProvider?.Current != null && runtimeSettings.EnableWalletBridge;
        HostMoneyGrant.Execute(allowed, requestId,
            id => runtimeStateProvider!.Current!.Economy.Commands.Any(c => c.CommandId == id && c.State == CommandState.Succeeded),
            () => SynchronizeHostWallet("host-magic-grant"),
            () => hostWallet.ReadBalance(),
            (id, target) =>
            {
                var record = runtimeStateProvider!.SynchronizeLocalWallet(id, target, "host-magic-grant");
                if (record.State != CommandState.Succeeded) throw new InvalidOperationException(record.ResultCode);
            },
            () => { if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Wallet grant could not be staged."); });
        return JsonConvert.SerializeObject(new { status = "succeeded", correlationId = requestId });
    }

    private static string NormalizeInGameReceipt(string json)
    {
        var response = JObject.Parse(json);
        if (response["result"] is JObject result)
        {
            var action = (string?)result["action"] ?? "";
            var state = result["state"] ?? result["State"];
            Type? type = action == "market.purchase" ? typeof(MarketPurchaseState) :
                action.StartsWith("initial-delivery.", StringComparison.Ordinal) ? typeof(InitialDeliveryState) :
                action == "wallet.transfer" || (action.StartsWith("company.", StringComparison.Ordinal) && action != "company.dissolve") ? typeof(CommandState) : null;
            var outcome = result["outcome"] ?? result["Outcome"];
            if (outcome != null) { state = outcome; type = typeof(FleetCommandOutcome); }
            var name = state?.Type == JTokenType.Integer && type != null ? Enum.GetName(type, (int)state) : (string?)state;
            if (name != null) result["state"] = name;
            if (name == "Rejected" || name == "Compensated")
            {
                response["status"] = "refused";
                response["message"] = InGameManagementModel.Humanize((string?)(result["resultCode"] ?? result["ResultCode"]) ?? "The host refused this action.");
            }
        }
        return response.ToString(Formatting.None);
    }

    private static string? PollInGameManagement(string requestId)
    {
        if (runtimeRoleDetector?.Detect().Role != NetworkRole.MultiplayerClient || clientRequestTracker == null) return null;
        var result = clientRequestTracker.ResultOrTimeout(requestId, DateTimeOffset.UtcNow);
        if (result.Code == "pending") return null;
        if (result.Status == ProtocolResultStatus.TimedOut)
            return JsonConvert.SerializeObject(new { status = "unknown", message = "The host has not confirmed the outcome. Retry the same request." });
        if (result.Status == ProtocolResultStatus.Rejected)
            return JsonConvert.SerializeObject(new { status = "refused", message = result.Detail });
        return result.Payload != null && result.Payload.Length > 0 ? NormalizeInGameReceipt(System.Text.Encoding.UTF8.GetString(result.Payload)) :
            JsonConvert.SerializeObject(new { status = "unknown", message = "The host response has no action receipt." });
    }
}