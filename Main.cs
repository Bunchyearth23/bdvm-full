using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BDVM.Adapters;
using BDVM.Domain;
using BDVM.Management;
using BDVM.PassengerJobsBridge;
using BDVM.SelfShuntBridge;
using BDVM.Web;
using DV;
using DV.Logic.Job;
using DV.ThingTypes;
using HarmonyLib;
using MPAPI;
using MPAPI.Interfaces;
using MPAPI.Types;
using UnityEngine;
using UnityModManagerNet;

namespace BDVM;

public static class Main
{
    private static UnityModManager.ModEntry? mod;
    private static DiagnosticService? diagnostic;
    private static string status = "Ready. No diagnostic has been run.";
    private static bool automaticExportPending;
    private static int automaticExportDelayFrames;
    private static Harmony? saveHarmony;
    private static NetworkRoleDetector? runtimeRoleDetector;
    private static AcquisitionRuntimeStateProvider? runtimeStateProvider;
    private static RuntimeSaveSettings runtimeSettings = RuntimeSaveSettings.SafeDefaults();
    private static readonly Newtonsoft.Json.JsonSerializerSettings webJsonSettings = new Newtonsoft.Json.JsonSerializerSettings
    {
        ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
        Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() }
    };
    private static readonly UnityHostWalletAdapter hostWallet = new UnityHostWalletAdapter();
    private static readonly UnityVisibleVehicleReader visibleVehicles = new UnityVisibleVehicleReader();
    private static IReadOnlyList<VehicleInstanceRecord> selectableVehicles = Array.Empty<VehicleInstanceRecord>();
    private static string companyName = "";
    private static string transferAmount = "100";
    private static string offerPrice = "1000";
    private static int selectedVehicleIndex = -1;
    private static int acquisitionVehicleFilter;
    private static string? preparedOfferId;
    private static bool acquireForCompany;
    private static string? selectedFleetAssetId;
    private static int fleetFilter;
    private static string fleetDisplayName = "";
    private static string resaleProceeds = "100";
    private static string? preparedResaleQuoteId;
    private static readonly HashSet<string> bundleSelection = new HashSet<string>(StringComparer.Ordinal);
    private static string governanceTargetPlayerId = "";
    private static int governancePolicy;
    private static string governancePolicyCompanyId = "";
    private static string liquidationDebts = "0";
    private static string liquidationPenalties = "0";
    private static string liquidationConfirmation = "";
    private static int maintenanceActionIndex;
    private static string maintenanceMaximumCost = "1000";
    private static string maintenanceTripId = "";
    private static bool maintenanceCompanyPayer;
    private static string? activeOperatingCostSessionId;
    private static string marketLocation = "LocalYard";
    private static string marketBasePrice = "100000";
    private static string marketTransferFee = "1000";
    private static string marketCondition = "1";
    private static string marketFactor = "1";
    private static string marketBuybackRate = "0.5";
    private static string marketNewStock = "0";
    private static string? selectedMarketListingId;
    private static bool marketBuyForCompany;
    private static string? selectedInitialDeliveryGrantId;
    private static string? selectedLeaseId;
    private static string leaseDeposit = "1000";
    private static string leaseInitialFee = "100";
    private static string leaseRent = "100";
    private static string leaseInterval = "10";
    private static string leaseDuration = "100";
    private static string leasePurchaseOption = "";
    private static string leaseDamageMaximum = "1000";
    private static string leaseCondition = "1";
    private static string leaseAdvanceTicks = "10";
    private static bool leaseForCompany;
    private static string? selectedOutboundLeaseId;
    private static string outboundLeaseRent = "100";
    private static string outboundLeaseInterval = "10";
    private static string outboundLeaseDuration = "100";
    private static string outboundLeaseRecallFee = "100";
    private static string outboundLeaseCondition = "1";
    private static string outboundLeaseDestination = "external-market";
    private static string outboundLeaseReturnLocation = "LocalYard";
    private static string missionId = "validation-job";
    private static string missionMaximumRevenue = "100000";
    private static int missionKind;
    private static bool missionForCompany;
    private static string? selectedAssignmentId;
    private static string industrialFacilityId = "LocalA";
    private static string industrialDestinationId = "LocalB";
    private static string industrialCargoId = "Logs";
    private static string industrialStockOnHand = "100";
    private static string industrialStockCapacity = "1000";
    private static string industrialQuantity = "30";
    private static string industrialBaseReward = "15000";
    private static string industrialScarcityBonus = "0";
    private static string industrialPreparationTicks = "600";
    private static string industrialPreparationPenalty = "0";
    private static string industrialPolicyId = "local-freight-policy";
    private static string industrialDestinationTarget = "100";
    private static string industrialMaximumScarcityBonus = "5000";
    private static string industrialOfferLifetime = "600";
    private static string industrialDeliveryDuration = "3600";
    private static string industrialMinimumWagons = "1";
    private static string industrialMinimumCapacity = "30";
    private static bool industrialPolicyEnabled = true;
    private static string industrialRecipeId = "local-production";
    private static string industrialRecipeInputCargo = "Logs";
    private static string industrialRecipeOutputCargo = "Lumber";
    private static string industrialRecipeInputQuantity = "2";
    private static string industrialRecipeOutputQuantity = "1";
    private static string industrialRecipeCadence = "60";
    private static string industrialRecipeBacklog = "32";
    private static string? selectedIndustrialContractId;
    private static bool industrialForCompany;
    private static string passengerRouteId = "LocalA-LocalB";
    private static string passengerOrigin = "LocalA";
    private static string passengerDestination = "LocalB";
    private static string passengerDemand = "100";
    private static string passengerMaximumDemand = "500";
    private static string passengerDemandGrowth = "20";
    private static string passengerFrequency = "100";
    private static string passengerFare = "100";
    private static string passengerLatePenalty = "10";
    private static string passengerCapacity = "50";
    private static string passengerJourneyTicks = "100";
    private static string passengerJobId = "passenger-validation-job";
    private static string? selectedPassengerContractId;
    private static bool passengerForCompany;
    private static string dynamicMinimum = "0.8";
    private static string dynamicMaximum = "1.2";
    private static string dynamicSmoothing = "0.2";
    private static string dynamicMaximumStep = "0.05";
    private static string dynamicSupplyWeight = "0.25";
    private static string dynamicDemandWeight = "0.35";
    private static string dynamicUtilizationWeight = "0.2";
    private static string financingPoolId = "validation-bank";
    private static string financingPoolCapital = "1000000";
    private static string financingPrincipal = "10000";
    private static string financingInterestBps = "500";
    private static string financingInstallment = "1000";
    private static string financingInterval = "10";
    private static string financingMaturity = "100";
    private static string financingGuarantee = "1000";
    private static string financingTransactionAmount = "1000";
    private static bool financingForCompany;
    private static int financingKind;
    private static string? selectedFinancingContractId;
    private static string triageTracks = "YARD-A,YARD-B";
    private static string? selectedTriagePlanId;
    private static int walletSyncFrames;
    private static int populationControlRetryFrames;
    private static double economicClockAccumulator;
    private static double pendingTimeSkipTicks;
    private static IServer? configuredServer;
    private static IClient? configuredClient;
    private static MultiplayerServerProtocolAdapter? serverProtocol;
    private static MultiplayerClientProtocolAdapter? clientProtocol;
    private static PersistentMultiplayerWalletAdapter? multiplayerWallet;
    private static ClientRequestTracker? clientRequestTracker;
    private static readonly object clientStateGate = new object();
    private static readonly Dictionary<string, ProtocolResult> clientProtocolResults = new Dictionary<string, ProtocolResult>(StringComparer.Ordinal);
    private static readonly Queue<string> clientProtocolResultOrder = new Queue<string>();
    private static readonly AuthoritativeStateSnapshotAssembler clientStateAssembler = new AuthoritativeStateSnapshotAssembler();
    private static readonly AuthoritativeStateSnapshotPager authoritativeStatePager = new AuthoritativeStateSnapshotPager();
    private static string? clientAuthoritativeState;
    private static bool clientStateTransferPending;
    private static string? clientStateRequestId;
    private static DateTimeOffset clientStateReceivedAt;
    private static InGameCompanyWindow? inGameWindow;
    private static WebModuleHost? managementWebModules;
    private static WebSessionRegistry? managementWebSessions;
    private static WebIntentGateway? managementWebIntents;
    private static ManagementAuthorityGateway? managementAuthority;
    private static RuntimeManagementPort? managementWebPort;
    private static readonly Dictionary<string, WebSession> managementWebTransportSessions = new Dictionary<string, WebSession>(StringComparer.Ordinal);
    private static SelfShuntIndustrialLifecycleAdapter? selfShuntIndustrialLifecycle;
    private static bool industrialCorrelationsRestored;

    public static bool Load(UnityModManager.ModEntry modEntry)
    {
        mod = modEntry;
        var trace = new UmmTrace(modEntry);
        var roleDetector = new NetworkRoleDetector(new MultiplayerNetworkApiStateReader());
        runtimeRoleDetector = roleDetector;
        diagnostic = new DiagnosticService(
            roleDetector,
            new UnityVehicleDefinitionReader(),
            new UnityVisibleVehicleReader(),
            new JsonDiagnosticWriter(Path.Combine(modEntry.Path, "diagnostics")),
            trace);
        modEntry.OnGUI = OnGui;
        modEntry.OnUpdate = OnUpdate;
        ConfigureInGameWindow(modEntry);
        BDVMStarterDeliveryRadio.Configure(PendingInitialDeliveries, DeliverFromRadio);
        RemoteDispatchBridge.Configure(BuildRemoteDispatchState, HandleRemoteDispatchIntent);
        RemoteDispatchBridge.ConfigureTrustedTransport(BuildRemoteDispatchState, HandleRemoteDispatchIntent);
        ConfigureManagementWeb(modEntry);
        WorldStreamingInit.LoadingFinished += OnWorldLoadingFinished;
        runtimeSettings = RuntimeSaveSettings.Load(
            Path.Combine(modEntry.Path, "runtime-settings.json"),
            message => modEntry.Logger.Warning("[correlation=save-settings] " + message));
        var strictPopulationEnabled = runtimeSettings.EnableStrictWorldPopulation && runtimeSettings.EnableSaveGameDataHook;
        UnityWorldPopulationControl.Configure(strictPopulationEnabled, runtimeSettings.WorldPopulationPolicy, roleDetector, message => modEntry.Logger.Log(message));
        if (runtimeSettings.EnableStrictWorldPopulation && !runtimeSettings.EnableSaveGameDataHook)
            modEntry.Logger.Error("[correlation=world-population] Strict population control refused because the SaveGameData hook is disabled.");
        UnityAssetCleanupProtection.Configure(runtimeSettings.EnableAssetLifecycle, () => runtimeStateProvider?.Current, roleDetector, message => modEntry.Logger.Log(message));
        modEntry.Logger.Log("[correlation=asset-lifecycle] Cleanup protection hook " + (runtimeSettings.EnableAssetLifecycle ? "enabled for exact BDVM CarGUID ownership/contract matches." : "disabled by feature flag."));
        if (runtimeSettings.EnableMultiplayerProtocol)
        {
            MultiplayerAPI.ServerStarted += ConfigureMultiplayerServer;
            MultiplayerAPI.ClientStarted += ConfigureMultiplayerClient;
            MultiplayerAPI.ServerStopped += ClearMultiplayerServer;
            MultiplayerAPI.ClientStopped += ClearMultiplayerClient;
            if (MultiplayerAPI.Instance != null)
                MultiplayerAPI.Instance.SetModCompatibility(modEntry.Info.Id, MultiplayerCompatibility.All);
            if (MultiplayerAPI.Server != null) ConfigureMultiplayerServer(MultiplayerAPI.Server);
            if (MultiplayerAPI.Client != null) ConfigureMultiplayerClient(MultiplayerAPI.Client);
        }
        if (runtimeSettings.EnableSaveGameDataHook)
        {
            runtimeStateProvider = new AcquisitionRuntimeStateProvider();
            var handler = new HostSaveGameUpdateHandler(runtimeStateProvider, message => modEntry.Logger.Log("[correlation=save-runtime] " + message));
            SaveGameRuntimeHook.Configure(
                new SaveGameFeatureFlags { EnableSaveGameDataHook = true },
                roleDetector,
                handler,
                message => modEntry.Logger.Log("[correlation=save-runtime] " + message),
                (message, exception) => modEntry.Logger.Error("[correlation=save-runtime] " + message + " " + exception));
            saveHarmony = new Harmony(modEntry.Info.Id + ".SaveGameData");
            saveHarmony.PatchAll();
            if (strictPopulationEnabled)
                modEntry.Logger.Warning("[correlation=world-population] Strict world population hooks installed; activation still requires an eligible non-tutorial BDVM career and authoritative external controls.");
            modEntry.Logger.Warning("[correlation=bootstrap] SaveGameData runtime hook ENABLED by explicit runtime-settings.json.");
        }
        else
        {
            SaveGameRuntimeHook.Reset();
            modEntry.Logger.Log("[correlation=bootstrap] BDVM diagnostic loaded; SaveGameData runtime hook disabled (safe default), no economy mutation hooks registered.");
        }
        if (runtimeSettings.EnableMultiplayerProtocol)
        {
            if (MultiplayerAPI.Server != null) ConfigureMultiplayerServer(MultiplayerAPI.Server);
            if (MultiplayerAPI.Client != null) ConfigureMultiplayerClient(MultiplayerAPI.Client);
        }
        return true;
    }

    private static void ConfigureManagementWeb(UnityModManager.ModEntry entry)
    {
        var port = new RuntimeManagementPort(BuildRemoteDispatchState, HandleRemoteDispatchIntent);
        managementWebPort = port;
        managementAuthority = new ManagementAuthorityGateway(port);
        managementWebModules = new WebModuleHost();
        var dispatch = managementWebModules.Load(new BDVM.Dispatch.DispatchWebModule());
        var loaded = managementWebModules.Load(new ManagementWebModule());
        managementWebSessions = new WebSessionRegistry();
        managementWebIntents = new WebIntentGateway(managementWebModules, managementWebSessions, new ManagementAuthoritativeWebIntentExecutor(port));
        managementWebTransportSessions.Clear();
        RemoteDispatchBridge.ConfigureWeb(BuildManagementWebShell, BuildManagementWebSnapshot, HandleManagementWebIntent, ReadManagementWebAsset);
        RemoteDispatchBridge.ConfigureTrustedWebTransport(BuildManagementWebShell, BuildManagementWebSnapshot, HandleManagementWebIntent, ReadManagementWebAsset);
        entry.Logger.Log("[correlation=management-web] [event=composition-ready] modules=" + dispatch.ModuleId + "," + loaded.ModuleId + ", states=" + dispatch.State + "," + loaded.State + ", authority=host-only, transport=RemoteDispatchLive");
    }

    private static string BuildManagementWebShell(string transportIdentity)
    {
        var shell = new WebShellService(managementWebModules ?? throw new InvalidOperationException("BDVM web modules are unavailable."))
            .Snapshot(WebConnectionState.Online, Guid.NewGuid().ToString("N"));
        shell.PrincipalId = transportIdentity;
        shell.DisplayName = transportIdentity;
        return Newtonsoft.Json.JsonConvert.SerializeObject(shell, webJsonSettings);
    }

    private static string BuildManagementWebShell(string transportIdentity, bool isLoopbackRequest)
        => BuildManagementWebShell(transportIdentity);

    private static string BuildManagementWebSnapshot(string transportIdentity)
    {
        RequireHostAuthority();
        var snapshot = (managementWebPort ?? throw new InvalidOperationException("BDVM management web port is unavailable."))
            .ReadSnapshot(transportIdentity, Guid.NewGuid().ToString("N"));
        return Newtonsoft.Json.JsonConvert.SerializeObject(snapshot, webJsonSettings);
    }

    private static string BuildManagementWebSnapshot(string transportIdentity, bool isLoopbackRequest)
    {
        RequireHostAuthority();
        var state = runtimeStateProvider?.Current ?? throw new InvalidOperationException("BDVM career state is unavailable.");
        var actorId = ResolveAuthenticatedRuntimeActor(transportIdentity, state, isLoopbackRequest);
        var snapshot = (managementWebPort ?? throw new InvalidOperationException("BDVM management web port is unavailable."))
            .ReadSnapshot(actorId, Guid.NewGuid().ToString("N"));
        return Newtonsoft.Json.JsonConvert.SerializeObject(snapshot, webJsonSettings);
    }

    private static string HandleManagementWebIntent(string transportIdentity, string payload)
    {
        RequireHostAuthority();
        if (System.Text.Encoding.UTF8.GetByteCount(payload ?? "") > WebIntentGateway.MaximumPayloadBytes)
            throw new ArgumentException("BDVM management intent payload is too large.");
        var body = Newtonsoft.Json.Linq.JObject.Parse(payload ?? "{}");
        var envelope = new WebIntentEnvelope
        {
            SchemaVersion = (int?)body["schemaVersion"] ?? 0,
            ModuleId = (string?)body["moduleId"] ?? "",
            IntentType = (string?)body["intentType"] ?? "",
            CorrelationId = (string?)body["correlationId"] ?? "",
            IdempotencyKey = (string?)body["idempotencyKey"] ?? "",
            ExpectedVersion = (long?)body["expectedVersion"] ?? -1,
            PayloadJson = body["payload"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "{}"
        };
        var session = GetManagementWebTransportSession(transportIdentity);
        var result = (managementWebIntents ?? throw new InvalidOperationException("BDVM management intent gateway is unavailable."))
            .Submit(new WebSessionRequest { SessionId = session.SessionId, CsrfToken = session.CsrfToken, OriginAuthority = session.OriginAuthority }, envelope);
        Newtonsoft.Json.Linq.JToken data;
        try { data = Newtonsoft.Json.Linq.JToken.Parse(result.ResultJson ?? "{}"); }
        catch (Newtonsoft.Json.JsonException) { data = new Newtonsoft.Json.Linq.JObject(); }
        return Newtonsoft.Json.JsonConvert.SerializeObject(new { state = result.State.ToString(), result.Code, result.CorrelationId, result.Replayed, data });
    }

    private static string HandleManagementWebIntent(string transportIdentity, string payload, bool isLoopbackRequest)
    {
        var state = runtimeStateProvider?.Current ?? throw new InvalidOperationException("BDVM career state is unavailable.");
        return HandleManagementWebIntent(ResolveAuthenticatedRuntimeActor(transportIdentity, state, isLoopbackRequest), payload);
    }

    private static WebSession GetManagementWebTransportSession(string transportIdentity)
    {
        lock (managementWebTransportSessions)
        {
            if (managementWebTransportSessions.TryGetValue(transportIdentity, out var existing) && existing.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
                return existing;
            var permissions = (managementWebModules ?? throw new InvalidOperationException("BDVM web modules are unavailable."))
                .Modules.SelectMany(module => module.Manifest.Permissions).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var created = (managementWebSessions ?? throw new InvalidOperationException("BDVM web sessions are unavailable."))
                .Create(transportIdentity, "localhost", permissions, transportIdentity);
            managementWebTransportSessions[transportIdentity] = created;
            return created;
        }
    }

    private static string ReadManagementWebAsset(string transportIdentity, string assetKey)
    {
        _ = transportIdentity;
        var assets = new Dictionary<string, (System.Reflection.Assembly Assembly, string Resource)>(StringComparer.OrdinalIgnoreCase)
        {
            ["shell.html"] = (typeof(WebShellService).Assembly, "BDVM.Web.Assets.shell.html"),
            ["shell.css"] = (typeof(WebShellService).Assembly, "BDVM.Web.Assets.shell.css"),
            ["shell.js"] = (typeof(WebShellService).Assembly, "BDVM.Web.Assets.shell.js"),
            ["bootstrap.js"] = (typeof(WebShellService).Assembly, "BDVM.Web.Assets.bootstrap.js"),
            ["modules/bdvm.management/app.js"] = (typeof(ManagementWebModule).Assembly, "BDVM.Management.Assets.app.js"),
            ["modules/bdvm.management/app.css"] = (typeof(ManagementWebModule).Assembly, "BDVM.Management.Assets.app.css")
        };
        if (!assets.TryGetValue((assetKey ?? "").Trim('/'), out var asset)) throw new FileNotFoundException("Unknown BDVM web asset.");
        using (var stream = asset.Assembly.GetManifestResourceStream(asset.Resource) ?? throw new FileNotFoundException("BDVM web asset is missing from its module.", asset.Resource))
        using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8)) return reader.ReadToEnd();
    }

    private static string ReadManagementWebAsset(string transportIdentity, string assetKey, bool isLoopbackRequest)
        => ReadManagementWebAsset(transportIdentity, assetKey);

    private static void ConfigureInGameWindow(UnityModManager.ModEntry entry)
    {
        var host = GameObject.Find("BDVM.InGameWindow") ?? new GameObject("BDVM.InGameWindow");
        UnityEngine.Object.DontDestroyOnLoad(host);
        inGameWindow = host.GetComponent<InGameCompanyWindow>() ?? host.AddComponent<InGameCompanyWindow>();
        inGameWindow.Configure(() => DrawCompanyPanel(entry), () => status, message => entry.Logger.Log(message));
        entry.Logger.Log("[correlation=ingame-ui] [event=ui-ready] toggle=F7, persistentButton=true");
    }

    private static void ConfigureMultiplayerServer(IServer server)
    {
        if (server == null || ReferenceEquals(configuredServer, server) || runtimeStateProvider == null || runtimeRoleDetector == null) return;
        var sink = new DelegateAcquisitionCheckpointSink((checkpoint, stage) =>
        {
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance))
                throw new InvalidOperationException("Could not stage multiplayer acquisition in SaveGameData.");
            mod?.Logger.Log("[correlation=" + checkpoint + "] [event=multiplayer-acquisition-stage] stage=" + stage);
        });
        var executor = new RuntimeCompanyIntentExecutor(runtimeStateProvider, runtimeRoleDetector,
            new UnityExistingVehicleOwnershipAdapter(), sink,
            correlation =>
            {
                if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance))
                    throw new InvalidOperationException("Could not stage multiplayer economy state in SaveGameData.");
            },
            message => mod?.Logger.Log(message), runtimeSettings.StarterBundleDefinitionIds, new FullModuleIntentExecutor(), runtimeSettings.StartingPersonalBalance);
        serverProtocol = new MultiplayerServerProtocolAdapter(server, new PersistentMultiplayerPeerIdentityResolver(), new CompanyProtocolHost(executor), message => mod?.Logger.Log(message));
        multiplayerWallet = new PersistentMultiplayerWalletAdapter(server);
        server.RegisterSerializablePacket<BDVMSerializablePacket>(serverProtocol.Receive);
        configuredServer = server;
        mod?.Logger.Log("[correlation=multiplayer-bootstrap] [event=protocol-registered] side=server, protocol=" + CompanyProtocolLimits.CurrentVersion + ", api=" + MultiplayerAPI.LoadedApiVersion);
    }

    private static void ConfigureMultiplayerClient(IClient client)
    {
        if (client == null || ReferenceEquals(configuredClient, client)) return;
        clientProtocol = new MultiplayerClientProtocolAdapter(client);
        clientRequestTracker = new ClientRequestTracker(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(20));
        client.RegisterSerializablePacket<BDVMSerializablePacket>(packet =>
        {
            try
            {
                var result = clientProtocol.Receive(packet);
                clientRequestTracker?.Accept(result);
                string? nextRequestId = null;
                string? nextPayload = null;
                lock (clientStateGate)
                {
                    if (!clientProtocolResults.ContainsKey(result.RequestId)) clientProtocolResultOrder.Enqueue(result.RequestId);
                    clientProtocolResults[result.RequestId] = result;
                    while (clientProtocolResultOrder.Count > 64) clientProtocolResults.Remove(clientProtocolResultOrder.Dequeue());
                    if (result.Status == ProtocolResultStatus.Succeeded && result.Code == "module-state-page" && result.Payload != null && result.Payload.Length > 0 && string.Equals(result.RequestId, clientStateRequestId, StringComparison.Ordinal))
                    {
                        var page = AuthoritativeStatePageCodec.Decode(result.Payload);
                        var completed = clientStateAssembler.Accept(page);
                        if (completed != null)
                        {
                            clientAuthoritativeState = new System.Text.UTF8Encoding(false, true).GetString(completed);
                            clientStateReceivedAt = DateTimeOffset.UtcNow;
                            clientStateTransferPending = false;
                            clientStateRequestId = null;
                        }
                        else
                        {
                            var nextOffset = clientStateAssembler.NextOffset;
                            nextRequestId = "state-page-" + page.SnapshotToken + "-" + nextOffset;
                            nextPayload = Newtonsoft.Json.JsonConvert.SerializeObject(new { token = page.SnapshotToken, offset = nextOffset });
                            clientStateRequestId = nextRequestId;
                        }
                    }
                    else if (result.Status == ProtocolResultStatus.Succeeded && result.Code == "module-operation-succeeded")
                    {
                        clientAuthoritativeState = null;
                        clientStateReceivedAt = default;
                        clientStateTransferPending = false;
                        clientStateRequestId = null;
                        clientStateAssembler.Reset();
                    }
                    else if (result.Status == ProtocolResultStatus.Rejected && string.Equals(result.RequestId, clientStateRequestId, StringComparison.Ordinal))
                    {
                        clientStateTransferPending = false;
                        clientStateRequestId = null;
                        clientStateAssembler.Reset();
                    }
                }
                if (nextRequestId != null) SendClientModuleIntent(nextRequestId, "state.get", nextPayload!);
                mod?.Logger.Log("[correlation=" + result.RequestId + "] [event=multiplayer-result] status=" + result.Status + ", code=" + result.Code + ", version=" + result.AuthoritativeVersion + ", detail=" + result.Detail);
            }
            catch (Exception exception)
            {
                lock (clientStateGate) { clientStateTransferPending = false; clientStateRequestId = null; clientStateAssembler.Reset(); }
                mod?.Logger.Error("[correlation=multiplayer-result] Invalid host result refused: " + exception);
            }
        });
        configuredClient = client;
        mod?.Logger.Log("[correlation=multiplayer-bootstrap] [event=protocol-registered] side=client, protocol=" + CompanyProtocolLimits.CurrentVersion + ", api=" + MultiplayerAPI.LoadedApiVersion);
    }

    private static void ClearMultiplayerServer() { configuredServer = null; serverProtocol = null; multiplayerWallet = null; }
    private static void ClearMultiplayerClient() { configuredClient = null; clientProtocol = null; clientRequestTracker = null; lock (clientStateGate) { clientProtocolResults.Clear(); clientProtocolResultOrder.Clear(); clientAuthoritativeState = null; clientStateReceivedAt = default; clientStateTransferPending = false; clientStateRequestId = null; clientStateAssembler.Reset(); } }

    private static void OnWorldLoadingFinished()
    {
        selfShuntIndustrialLifecycle?.Dispose();
        selfShuntIndustrialLifecycle = null;
        industrialCorrelationsRestored = false;
        automaticExportPending = true;
        automaticExportDelayFrames = 120;
        mod?.Logger.Log("[correlation=runtime-validation] World loading finished; read-only diagnostic export scheduled.");
    }

    private static void OnUpdate(UnityModManager.ModEntry entry, float deltaTime)
    {
        if (clientProtocol != null && clientRequestTracker != null)
            foreach (var retry in clientRequestTracker.DueRetries(DateTimeOffset.UtcNow))
                clientProtocol.SendIntent(retry);
        if (++populationControlRetryFrames >= 60)
        {
            populationControlRetryFrames = 0;
            UnityWorldPopulationControl.RetryPendingActivation();
            TryRestoreIndustrialJobCorrelations(entry);
        }
        AdvanceEconomicRuntime(entry, deltaTime);
        if (runtimeSettings.EnableWalletBridge && runtimeStateProvider?.Current != null && !runtimeStateProvider.Current.OperatingCosts.Any(x => x.State == OperatingCostState.Open || x.ExternalSettlement == ExternalSettlementState.Pending || x.ExternalSettlement == ExternalSettlementState.Conflict) && !runtimeStateProvider.Current.Assignments.Any(x => x.State == MissionAssignmentState.Active || x.State == MissionAssignmentState.CompletionPending || x.ExternalSettlement == ExternalSettlementState.Pending) && ++walletSyncFrames >= 120)
        {
            walletSyncFrames = 0;
            TrySynchronizeHostWallet(entry, "periodic-vanilla-observation");
        }

        if (!automaticExportPending) return;

        if (automaticExportDelayFrames-- > 0)
            return;

        automaticExportPending = false;
        try
        {
            status = "Automatic export: " + diagnostic!.ExportAuthoritative();
        }
        catch (InvalidOperationException exception)
        {
            entry.Logger.Log("[correlation=runtime-validation] Authoritative export unavailable; writing a non-authoritative visibility observation: " + exception.Message);
            try { status = "Automatic observation: " + diagnostic!.ExportVisibilityObservation(); }
            catch (Exception observationException) { status = "Automatic observation failed: " + observationException.Message; }
        }
        catch (Exception exception)
        {
            status = "Automatic export failed: " + exception.Message;
            entry.Logger.Error("[correlation=runtime-validation] Automatic diagnostic export failed: " + exception);
        }

        InitializeRuntimeState(entry);
    }

    private static void AdvanceEconomicRuntime(UnityModManager.ModEntry entry, float deltaTime)
    {
        if (runtimeStateProvider?.Current == null || runtimeRoleDetector == null || Time.timeScale <= 0f || deltaTime <= 0f ||
            !NetworkAuthorityPolicy.CanExecuteEconomy(runtimeRoleDetector.Detect(), out _)) return;
        economicClockAccumulator += deltaTime;
        var activeTicks = (long)Math.Floor(economicClockAccumulator);
        var skippedTicks = (long)Math.Floor(pendingTimeSkipTicks);
        if (activeTicks < 10 && skippedTicks <= 0) return;
        if (activeTicks > 0) economicClockAccumulator -= activeTicks;
        if (skippedTicks > 0) pendingTimeSkipTicks -= skippedTicks;
        var snapshot = runtimeStateProvider.Current;
        var correlation = "economic-clock:" + snapshot.LeaseClock.ActiveTick + ":" + activeTicks + ":" + skippedTicks;
        var domainCommitted = false; var externalDebited = false; long previewDelta = 0;
        var remoteActors = ConnectedRemotePlayerIds();
        try
        {
            if (runtimeSettings.EnableWalletBridge) TrySynchronizeHostWallet(entry, "before-economic-clock");
            foreach (var remoteActor in remoteActors)
                SynchronizeRemotePlayerWallet(remoteActor, correlation + ":before:" + remoteActor);
            var playerWallet = snapshot.Economy.Wallets.SingleOrDefault(value => value.Account.Kind == AccountKind.Player && value.Account.OwnerId == runtimeStateProvider.LocalPlayerId);
            var personalBefore = playerWallet?.Balance ?? 0;
            var advance = new LeaseClockAdvance
            {
                CommandId = correlation,
                ActiveGameplayTicks = activeTicks,
                FastTravelTicks = skippedTicks,
                SleepTicks = 0,
                SessionOpen = true,
                Paused = false
            };
            previewDelta = PreviewLeaseClockPersonalDelta(snapshot, advance);
            if (runtimeSettings.EnableWalletBridge && previewDelta < 0)
            {
                if (!hostWallet.TryDebit(-previewDelta)) throw new InvalidOperationException("The authoritative personal wallet cannot fund the pending lease installments.");
                externalDebited = true;
            }
            var tick = runtimeStateProvider.AdvanceLocalLeaseClock(advance, runtimeRoleDetector, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter());
            domainCommitted = true;
            var personalAfter = playerWallet?.Balance ?? personalBefore;
            if (runtimeSettings.EnableWalletBridge && personalAfter != personalBefore)
            {
                var delta = personalAfter - personalBefore;
                if (delta != previewDelta) throw new InvalidOperationException("Lease clock preview diverged from the committed wallet delta.");
                if (delta > 0) hostWallet.Credit(delta);
            }
            if (runtimeSettings.EnableIndustrialPilot)
            {
                var engine = IndustrialEngine(snapshot);
                foreach (var contract in snapshot.IndustrialContracts.Where(value => value.State == IndustrialContractState.Reserved && value.PreparationExpiresTick > 0 && value.PreparationExpiresTick <= tick).ToArray())
                    engine.ExpirePreparation(correlation + ":expire:" + contract.ContractId, contract.ContractId, tick);
                foreach (var recipe in snapshot.IndustrialRecipes.OrderBy(value => value.RecipeId).ToArray())
                    engine.AdvanceProduction(correlation + ":production:" + recipe.RecipeId, recipe.RecipeId, tick);
                foreach (var policy in snapshot.IndustrialTransportPolicies.Where(value => value.Enabled).OrderBy(value => value.PolicyId).ToArray())
                    engine.PublishTransportNeed(correlation + ":need:" + policy.PolicyId, policy.PolicyId, tick);
            }
            foreach (var remoteActor in remoteActors)
                SettleRemoteWalletToInternal(remoteActor, correlation + ":after:" + remoteActor);
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Economic clock state could not be staged in SaveGameData.");
            if (skippedTicks > 0 || snapshot.Leases.Any(value => value.State == LeaseState.Active || value.State == LeaseState.Delinquent) || snapshot.OutboundLeases.Any(value => value.State == OutboundLeaseState.Active))
                entry.Logger.Log("[correlation=" + correlation + "] [event=economic-clock-advanced] activeTicks=" + activeTicks + ", timeSkipTicks=" + skippedTicks + ", tick=" + tick + ", personalDelta=" + (personalAfter - personalBefore));
        }
        catch (Exception exception)
        {
            if (!domainCommitted)
            {
                if (externalDebited) hostWallet.Credit(-previewDelta);
                economicClockAccumulator += activeTicks;
                pendingTimeSkipTicks += skippedTicks;
                entry.Logger.Error("[correlation=" + correlation + "] [event=economic-clock-refused-before-commit] externalRefunded=" + externalDebited + ", error=" + exception);
            }
            else
            {
                var staged = SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance);
                entry.Logger.Error("[correlation=" + correlation + "] [event=economic-clock-postcommit-failure] requeued=false, emergencySaveStaged=" + staged + ", error=" + exception);
            }
        }
    }

    internal static void RecordAuthoritativeTimeSkip(float seconds)
    {
        if (seconds > 0f && !float.IsNaN(seconds) && !float.IsInfinity(seconds)) pendingTimeSkipTicks += seconds;
    }

    private static long PreviewLeaseClockPersonalDelta(VehicleAcquisitionSnapshot snapshot, LeaseClockAdvance advance)
    {
        var playerId = runtimeStateProvider?.LocalPlayerId ?? throw new InvalidOperationException("Local player identity is unavailable.");
        var clone = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(snapshot), snapshot.CheckpointId);
        var before = clone.Economy.Wallets.SingleOrDefault(value => value.Account.Kind == AccountKind.Player && value.Account.OwnerId == playerId)?.Balance ?? 0;
        new LeaseEngine(clone, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter()).Advance(advance);
        new OutboundLeaseEngine(clone, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new DeclaredOffSceneLeaseSimulationPort()).ProcessClock(advance.CommandId + ":outbound");
        var after = clone.Economy.Wallets.SingleOrDefault(value => value.Account.Kind == AccountKind.Player && value.Account.OwnerId == playerId)?.Balance ?? before;
        return checked(after - before);
    }

    private static void InitializeRuntimeState(UnityModManager.ModEntry entry)
    {
        if (!SaveGameRuntimeHook.Enabled || runtimeStateProvider == null)
            return;
        if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance))
        {
            status = "BDVM runtime state initialization refused; see Player.log.";
            return;
        }
        var legacyBalance = runtimeSettings.EnableWalletBridge ? hostWallet.ReadBalance() : 0;
        var player = runtimeStateProvider.EnsureLocalPlayer(legacyBalance);
        var starterDefinitions = runtimeSettings.StarterBundleDefinitionIds ?? new List<string>();
        if (starterDefinitions.Count > 0)
        {
            var starter = runtimeStateProvider.GrantLocalStarterBundle("starter-bundle:" + player.PlayerId, starterDefinitions, runtimeRoleDetector!);
            entry.Logger.Log("[correlation=runtime-bootstrap] [event=starter-bundle] player=" + player.PlayerId + ", firstGrant=" + starter.GrantId + ", deliveryMode=one-vehicle-per-radio-placement, components=" + string.Join(",", starterDefinitions) + ", state=" + starter.State);
        }
        if (runtimeSettings.EnableWalletBridge)
            TrySynchronizeHostWallet(entry, "world-load");
        EnsureSelfShuntIndustrialLifecycle(entry);
        SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance);
        status = "BDVM 0.3.0 beta ready for " + player.PlayerId + ".";
        entry.Logger.Log("[correlation=runtime-bootstrap] Runtime state ready; player=" + player.PlayerId + ", legacyBalancePolicy=host-keeps-existing-balance, walletBridge=" + runtimeSettings.EnableWalletBridge + ", transfers=" + runtimeSettings.EnableCompanyTransfers + ", acquisition=" + runtimeSettings.EnableVehicleAcquisition + ".");
        entry.Logger.Log("[correlation=wallet-migration] [event=wallet-migration-policy] policy=host-keeps-existing-balance-v1, player=" + player.PlayerId + ", observedVanillaBalance=" + legacyBalance + ", remotePlayerInitialBalance=0");
        if (runtimeSettings.VerboseLogging)
            entry.Logger.Log("[correlation=runtime-bootstrap] [event=state-summary] players=" + runtimeStateProvider.Current!.Economy.Players.Count + ", companies=" + runtimeStateProvider.Current.Economy.Companies.Count + ", wallets=" + runtimeStateProvider.Current.Economy.Wallets.Count + ", assets=" + runtimeStateProvider.Current.Assets.Assets.Count + ", offers=" + runtimeStateProvider.Current.Offers.Count);
    }

    private static void EnsureSelfShuntIndustrialLifecycle(UnityModManager.ModEntry entry)
    {
        if (!runtimeSettings.EnableIndustrialPilot || runtimeStateProvider?.Current == null || runtimeRoleDetector == null || selfShuntIndustrialLifecycle != null) return;
        var sink = new UnitySelfShuntIndustrialSink(runtimeStateProvider, runtimeRoleDetector,
            () => SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance),
            message => entry.Logger.Log("[correlation=selfshunt-industrial] " + message));
        selfShuntIndustrialLifecycle = new SelfShuntIndustrialLifecycleAdapter(SelfShunt.SelfShuntApi.Instance, sink,
            message => entry.Logger.Log("[correlation=selfshunt-industrial] " + message));
        entry.Logger.Log("[correlation=selfshunt-industrial] [event=lifecycle-subscribed] api=" + SelfShunt.SelfShuntApi.Instance.ApiVersion + ", host=" + SelfShunt.SelfShuntApi.Instance.IsHost + ", externalAuthority=" + SelfShunt.SelfShuntApi.Instance.IsExternalEconomicAuthority);
        TryRestoreIndustrialJobCorrelations(entry);
    }

    private static void TryRestoreIndustrialJobCorrelations(UnityModManager.ModEntry entry)
    {
        if (industrialCorrelationsRestored || selfShuntIndustrialLifecycle == null || runtimeStateProvider?.Current == null ||
            !SelfShunt.SelfShuntApi.Instance.IsHost || !SelfShunt.SelfShuntApi.Instance.IsExternalEconomicAuthority) return;
        try
        {
            var restored = UnityIndustrialJobAdapter.RestoreCorrelations(runtimeStateProvider.Current, selfShuntIndustrialLifecycle);
            industrialCorrelationsRestored = true;
            entry.Logger.Log("[correlation=selfshunt-industrial] [event=persisted-correlations-restored] count=" + restored);
        }
        catch (Exception exception)
        {
            entry.Logger.Error("[correlation=selfshunt-industrial] [event=persisted-correlation-recovery-pending] " + exception);
        }
    }

    private static void TrySynchronizeHostWallet(UnityModManager.ModEntry entry, string source)
    {
        if (runtimeStateProvider?.Current == null) return;
        try
        {
            var playerId = runtimeStateProvider.LocalPlayerId!;
            var actual = hostWallet.ReadBalance();
            var wallet = runtimeStateProvider.Current.Economy.Wallets.SingleOrDefault(x => x.Account.Kind == AccountKind.Player && x.Account.OwnerId == playerId);
            if (wallet != null && wallet.Balance == actual) return;
            var correlation = Guid.NewGuid().ToString("N");
            var result = runtimeStateProvider.SynchronizeLocalWallet("wallet-sync:" + correlation, actual, source);
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance))
                throw new InvalidOperationException("Wallet synchronization could not be staged for save.");
            entry.Logger.Log("[correlation=" + correlation + "] [event=wallet-sync] source=" + source + ", balance=" + actual + ", state=" + result.State + ", result=" + result.ResultCode);
        }
        catch (Exception exception)
        {
            entry.Logger.Error("[correlation=wallet-sync] Host wallet synchronization refused: " + exception);
        }
    }

    private static void OnGui(UnityModManager.ModEntry entry) => DrawCompanyPanel(entry);

    private static void DrawCompanyPanel(UnityModManager.ModEntry entry)
    {
        GUILayout.Label("BDVM 0.3.0 beta — modular economy build; strict rolling-stock population policy available");
        GUILayout.Label("SaveGameData hook: " + (SaveGameRuntimeHook.Enabled ? "enabled" : "disabled"));
        GUILayout.Label("World population: " + UnityWorldPopulationControl.State + " / " + UnityWorldPopulationControl.ResultCode);
        GUILayout.Label("Read-only output: " + Path.Combine(entry.Path, "diagnostics"));
        if (GUILayout.Button("Export authoritative host diagnostic"))
        {
            try { status = "Exported: " + diagnostic!.ExportAuthoritative(); }
            catch (Exception exception) { status = "Export refused or failed: " + exception.Message; }
        }
        if (GUILayout.Button("Export non-authoritative visibility observation"))
        {
            try { status = "Exported observation: " + diagnostic!.ExportVisibilityObservation(); }
            catch (Exception exception) { status = "Observation failed: " + exception.Message; }
        }
        if (GUILayout.Button("Run complete destructive dev-save validation"))
            RunAutomatedInGameValidation(entry);
        GUILayout.Label(status);

        var snapshot = runtimeStateProvider?.Current;
        var playerId = runtimeStateProvider?.LocalPlayerId;
        if (snapshot == null || string.IsNullOrWhiteSpace(playerId))
        {
            GUILayout.Label("Company runtime state is unavailable until a career is loaded with the save hook enabled.");
            return;
        }

        var player = snapshot.Economy.Players.Find(x => x.PlayerId == playerId);
        var personalWallet = snapshot.Economy.Wallets.Find(x => x.Account.Kind == AccountKind.Player && x.Account.OwnerId == playerId);
        var company = player?.CompanyId == null ? null : snapshot.Economy.Companies.Find(x => x.CompanyId == player.CompanyId);
        var companyWallet = company == null ? null : snapshot.Economy.Wallets.Find(x => x.Account.Kind == AccountKind.Company && x.Account.OwnerId == company.CompanyId);
        GUILayout.Label("Player identity: " + playerId);
        GUILayout.Label("Personal wallet: " + (personalWallet?.Balance ?? 0) + (runtimeSettings.EnableWalletBridge ? " (mirrored from authoritative vanilla host wallet)" : " (wallet bridge disabled)"));
        GUILayout.Label(company == null ? "Status: independent" : "Company: " + company.Name + " | company wallet: " + (companyWallet?.Balance ?? 0));
        DrawPendingLiquidationRecovery(entry, snapshot, player!);

        if (company == null)
        {
            GUILayout.Label("Company name:");
            companyName = GUILayout.TextField(companyName ?? "", 64);
            if (GUILayout.Button("Create free company (starting balance: 0)"))
            {
                try
                {
                    if (!NetworkAuthorityPolicy.CanExecuteEconomy(runtimeRoleDetector!.Detect(), out var reason))
                        throw new InvalidOperationException(reason);
                    var normalized = (companyName ?? "").Trim().ToUpperInvariant();
                    var commandId = "ui-create:" + playerId + ":" + normalized;
                    var result = runtimeStateProvider!.CreateCompany(commandId, companyName ?? "");
                    if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance))
                        throw new InvalidOperationException("Company was created in memory but could not be staged for save.");
                    status = "Create company: " + result.State + " / " + result.ResultCode + ". Save normally, then reload to verify persistence.";
                    entry.Logger.Log("[correlation=" + commandId + "] [event=company-create] state=" + result.State + ", result=" + result.ResultCode);
                }
                catch (Exception exception)
                {
                    status = "Create company refused: " + exception.Message;
                    entry.Logger.Error("[correlation=company-create] [event=company-create-refused] " + exception);
                }
            }
            DrawIndependentGovernance(entry, snapshot, player!);
        }
        else
        {
            DrawCompanyTransfers(entry);
            DrawCompanyGovernance(entry, snapshot, player!, company);
        }

        DrawVehicleAcquisition(entry, company != null);
        DrawFiniteMarket(entry, snapshot, company != null);
        DrawDynamicEconomy(entry, snapshot);
        DrawFinancing(entry, snapshot, company != null);
        DrawInboundLeasing(entry, snapshot, company != null);
        DrawFleetManagement(entry, snapshot, player!, company);
        DrawOutboundLeasing(entry, snapshot);
        DrawMissionAssignments(entry, snapshot, company != null);
        DrawIndustrialEconomy(entry, snapshot, company != null);
        DrawTriageAssistance(entry, snapshot);
        DrawPassengerEconomy(entry, snapshot, company != null);
        DrawLicenseStatus(entry, snapshot);
    }

    private static void DrawIndependentGovernance(UnityModManager.ModEntry entry, VehicleAcquisitionSnapshot snapshot, PlayerEconomicState player)
    {
        GUILayout.Space(6f);
        GUILayout.Label("Company governance");
        if (!runtimeSettings.EnableCompanyGovernance) { GUILayout.Label("Company governance is disabled by runtime settings."); return; }
        foreach (var invitation in snapshot.Economy.MembershipRequests.Where(x => x.PlayerId == player.PlayerId && x.Kind == MembershipRequestKind.Invitation && x.State == MembershipRequestState.Pending).ToArray())
        {
            var company = snapshot.Economy.Companies.SingleOrDefault(x => x.CompanyId == invitation.CompanyId);
            GUILayout.Label("Invitation: " + (company?.Name ?? invitation.CompanyId));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Accept invitation")) ExecuteGovernance(entry, "invitation-accept", () => runtimeStateProvider!.RespondToInvitationFor("invitation-response:" + Guid.NewGuid().ToString("N"), player.PlayerId, invitation.RequestId, true, runtimeRoleDetector!));
            if (GUILayout.Button("Refuse invitation")) ExecuteGovernance(entry, "invitation-refuse", () => runtimeStateProvider!.RespondToInvitationFor("invitation-response:" + Guid.NewGuid().ToString("N"), player.PlayerId, invitation.RequestId, false, runtimeRoleDetector!));
            GUILayout.EndHorizontal();
        }
        foreach (var company in snapshot.Economy.Companies.Where(x => !x.Liquidating).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray())
        {
            var pending = snapshot.Economy.MembershipRequests.Any(x => x.PlayerId == player.PlayerId && x.CompanyId == company.CompanyId && x.Kind == MembershipRequestKind.Application && x.State == MembershipRequestState.Pending);
            GUILayout.BeginHorizontal();
            GUILayout.Label(company.Name + " | " + company.Members.Count + " member(s) | " + company.MembershipPolicy);
            GUI.enabled = !pending && company.MembershipPolicy != MembershipPolicy.InvitationOnly;
            if (GUILayout.Button(pending ? "Application pending" : "Apply", GUILayout.Width(150f))) ExecuteGovernance(entry, "membership-apply", () => runtimeStateProvider!.ApplyToCompanyFor("membership-apply:" + Guid.NewGuid().ToString("N"), player.PlayerId, company.CompanyId, runtimeRoleDetector!));
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }
    }

    private static void DrawCompanyGovernance(UnityModManager.ModEntry entry, VehicleAcquisitionSnapshot snapshot, PlayerEconomicState player, CompanyState company)
    {
        GUILayout.Space(6f);
        GUILayout.Label("Company governance | leader: " + company.LeaderId + " | policy: " + company.MembershipPolicy + " | version: " + company.Version);
        if (!runtimeSettings.EnableCompanyGovernance) { GUILayout.Label("Company governance is disabled by runtime settings."); return; }
        if (!string.Equals(governancePolicyCompanyId, company.CompanyId, StringComparison.Ordinal))
        {
            governancePolicyCompanyId = company.CompanyId;
            governancePolicy = company.MembershipPolicy == MembershipPolicy.InvitationOnly ? 1 : company.MembershipPolicy == MembershipPolicy.Open ? 2 : 0;
        }
        var canManageMembers = company.LeaderId == player.PlayerId || (company.DelegatedPermissions.TryGetValue(player.PlayerId, out var memberRights) && memberRights.Contains(CompanyPermission.ManageMembers));
        var canManagePermissions = company.LeaderId == player.PlayerId || (company.DelegatedPermissions.TryGetValue(player.PlayerId, out var permissionRights) && permissionRights.Contains(CompanyPermission.ManagePermissions));
        governancePolicy = GUILayout.Toolbar(governancePolicy, new[] { "Applications", "Invitation only", "Open" });
        GUI.enabled = canManageMembers;
        if (GUILayout.Button("Apply membership policy"))
        {
            var policy = governancePolicy == 1 ? MembershipPolicy.InvitationOnly : governancePolicy == 2 ? MembershipPolicy.Open : MembershipPolicy.ApplicationWithApproval;
            ExecuteGovernance(entry, "membership-policy", () => runtimeStateProvider!.SetMembershipPolicyFor("membership-policy:" + Guid.NewGuid().ToString("N"), player.PlayerId, company.CompanyId, policy, runtimeRoleDetector!));
        }
        GUILayout.Label("Known player ID for invitation or delegation:");
        governanceTargetPlayerId = GUILayout.TextField(governanceTargetPlayerId ?? "", 96);
        GUILayout.BeginHorizontal();
        GUI.enabled = canManageMembers;
        if (GUILayout.Button("Invite player")) ExecuteGovernance(entry, "player-invite", () => runtimeStateProvider!.InvitePlayerFor("player-invite:" + Guid.NewGuid().ToString("N"), player.PlayerId, company.CompanyId, governanceTargetPlayerId, runtimeRoleDetector!));
        GUI.enabled = company.LeaderId == player.PlayerId && company.Members.Contains(governanceTargetPlayerId);
        if (GUILayout.Button("Transfer leadership")) ExecuteGovernance(entry, "leadership-transfer", () => runtimeStateProvider!.TransferLeadershipFor("leadership-transfer:" + Guid.NewGuid().ToString("N"), player.PlayerId, company.CompanyId, governanceTargetPlayerId, runtimeRoleDetector!));
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        foreach (var application in snapshot.Economy.MembershipRequests.Where(x => x.CompanyId == company.CompanyId && x.Kind == MembershipRequestKind.Application && x.State == MembershipRequestState.Pending).ToArray())
        {
            GUILayout.Label("Application: " + application.PlayerId);
            GUILayout.BeginHorizontal();
            GUI.enabled = canManageMembers;
            if (GUILayout.Button("Accept")) ExecuteGovernance(entry, "application-accept", () => runtimeStateProvider!.DecideApplicationFor("application-decision:" + Guid.NewGuid().ToString("N"), player.PlayerId, application.RequestId, true, runtimeRoleDetector!));
            if (GUILayout.Button("Refuse")) ExecuteGovernance(entry, "application-refuse", () => runtimeStateProvider!.DecideApplicationFor("application-decision:" + Guid.NewGuid().ToString("N"), player.PlayerId, application.RequestId, false, runtimeRoleDetector!));
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }
        GUILayout.Label("Members: " + string.Join(", ", company.Members));
        if (company.Members.Contains(governanceTargetPlayerId) && governanceTargetPlayerId != company.LeaderId)
        {
            var rights = company.DelegatedPermissions.TryGetValue(governanceTargetPlayerId, out var delegated) ? delegated : new List<CompanyPermission>();
            foreach (CompanyPermission permission in Enum.GetValues(typeof(CompanyPermission)))
            {
                var enabled = rights.Contains(permission);
                GUI.enabled = canManagePermissions;
                if (GUILayout.Button((enabled ? "Revoke " : "Grant ") + permission + " — " + governanceTargetPlayerId))
                    ExecuteGovernance(entry, "permission-change", () => runtimeStateProvider!.SetPermissionFor("permission-change:" + Guid.NewGuid().ToString("N"), player.PlayerId, company.CompanyId, governanceTargetPlayerId, permission, !enabled, runtimeRoleDetector!));
                GUI.enabled = true;
            }
        }
        GUI.enabled = company.LeaderId != player.PlayerId;
        if (GUILayout.Button("Leave company")) ExecuteGovernance(entry, "company-leave", () => runtimeStateProvider!.LeaveCompanyFor("company-leave:" + Guid.NewGuid().ToString("N"), player.PlayerId, runtimeRoleDetector!));
        GUI.enabled = true;
        GUILayout.Label("Dissolution liabilities — debts then penalties:");
        GUILayout.BeginHorizontal();
        liquidationDebts = GUILayout.TextField(liquidationDebts ?? "0", 20);
        liquidationPenalties = GUILayout.TextField(liquidationPenalties ?? "0", 20);
        GUILayout.EndHorizontal();
        GUILayout.Label("Type DISSOLVE to confirm contract cancellation, vehicle sale and final distribution:");
        liquidationConfirmation = GUILayout.TextField(liquidationConfirmation ?? "", 16);
        var canDissolve = company.LeaderId == player.PlayerId || (company.DelegatedPermissions.TryGetValue(player.PlayerId, out var ownRights) && ownRights.Contains(CompanyPermission.Dissolve));
        GUI.enabled = canDissolve && string.Equals(liquidationConfirmation, "DISSOLVE", StringComparison.Ordinal);
        if (GUILayout.Button("Dissolve company permanently")) ExecuteCompanyLiquidation(entry, player, company);
        GUI.enabled = true;
    }

    private static void ExecuteCompanyLiquidation(UnityModManager.ModEntry entry, PlayerEconomicState player, CompanyState company)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            if (!long.TryParse(liquidationDebts, out var debts) || debts < 0 || !long.TryParse(liquidationPenalties, out var penalties) || penalties < 0) throw new InvalidOperationException("Debts and penalties must be non-negative whole numbers.");
            var personal = runtimeStateProvider!.Current!.Economy.Wallets.Single(x => x.Account.Key == "Player:" + player.PlayerId);
            var before = personal.Balance;
            var releaseGuard = new UnityAssetReleaseGuard();
            var result = runtimeStateProvider.DissolveCompanyFor("company-dissolve:" + correlation, player.PlayerId, company.CompanyId, debts, penalties, runtimeRoleDetector!, releaseGuard, new UnityExistingVehicleOwnershipAdapter(), new CompositeCompanyContractCancellationPort(new CompanyWorkflowCancellationPort(runtimeStateProvider.Current!, runtimeRoleDetector!), new OutboundLeaseCompanyContractCancellationPort(runtimeStateProvider.Current!, runtimeRoleDetector!, releaseGuard, new DeclaredOffSceneLeaseSimulationPort()), new FinancingCompanyContractCancellationPort(runtimeStateProvider.Current!, runtimeRoleDetector!)), new SaveGameLiquidationCheckpointPort(entry));
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Liquidation state could not be staged for save.");
            var after = runtimeStateProvider.Current!.Economy.Wallets.Single(x => x.Account.Key == "Player:" + player.PlayerId).Balance;
            if (result.State == CompanyLiquidationState.Succeeded && after > before) hostWallet.Credit(after - before);
            liquidationConfirmation = "";
            status = "Company liquidation: " + result.State + " / " + result.ResultCode + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=company-liquidation] company=" + result.CompanyId + ", assets=" + string.Join(",", result.AssetIds) + ", beneficiaries=" + string.Join(",", result.BeneficiaryIds) + ", debts=" + result.Debts + ", penalties=" + result.Penalties + ", contractsCancelled=" + result.ContractsCancelled + ", ownershipCommitted=" + result.OwnershipCommitted + ", economyCommitted=" + result.EconomyCommitted + ", state=" + result.State + ", result=" + result.ResultCode);
        }
        catch (Exception exception)
        {
            status = "Company liquidation refused: " + exception.Message;
            entry.Logger.Error("[correlation=" + correlation + "] [event=company-liquidation-refused] company=" + company.CompanyId + ", error=" + exception);
        }
    }

    private static void DrawPendingLiquidationRecovery(UnityModManager.ModEntry entry, VehicleAcquisitionSnapshot snapshot, PlayerEconomicState player)
    {
        var pending = snapshot.CompanyLiquidations.Count(x => x.State == CompanyLiquidationState.ReconcileRequired);
        if (pending == 0 || !runtimeSettings.EnableCompanyGovernance) return;
        if (!GUILayout.Button("Reconcile pending company liquidations (" + pending + ")")) return;
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            var wallet = snapshot.Economy.Wallets.Single(x => x.Account.Key == "Player:" + player.PlayerId);
            var before = wallet.Balance;
            var releaseGuard = new UnityAssetReleaseGuard();
            var results = runtimeStateProvider!.ReconcilePendingCompanyLiquidations(runtimeRoleDetector!, releaseGuard, new UnityExistingVehicleOwnershipAdapter(), new CompositeCompanyContractCancellationPort(new CompanyWorkflowCancellationPort(runtimeStateProvider.Current!, runtimeRoleDetector!), new OutboundLeaseCompanyContractCancellationPort(runtimeStateProvider.Current!, runtimeRoleDetector!, releaseGuard, new DeclaredOffSceneLeaseSimulationPort()), new FinancingCompanyContractCancellationPort(runtimeStateProvider.Current!, runtimeRoleDetector!)), new SaveGameLiquidationCheckpointPort(entry));
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Liquidation recovery could not be staged for save.");
            var after = snapshot.Economy.Wallets.Single(x => x.Account.Key == "Player:" + player.PlayerId).Balance;
            if (after > before) hostWallet.Credit(after - before);
            foreach (var result in results) entry.Logger.Log("[correlation=" + correlation + "] [event=company-liquidation-reconcile] command=" + result.CommandId + ", company=" + result.CompanyId + ", state=" + result.State + ", result=" + result.ResultCode);
            status = "Company liquidation recovery processed: " + results.Count + ".";
        }
        catch (Exception exception)
        {
            status = "Company liquidation recovery failed: " + exception.Message;
            entry.Logger.Error("[correlation=" + correlation + "] [event=company-liquidation-reconcile-failed] error=" + exception);
        }
    }

    private static void ExecuteGovernance(UnityModManager.ModEntry entry, string eventName, Func<CommandRecord> action)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            var result = action();
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Governance mutation could not be staged for save.");
            status = "Governance: " + result.State + " / " + result.ResultCode + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=" + eventName + "] command=" + result.CommandId + ", requester=" + result.RequesterId + ", company=" + result.CompanyId + ", operations=" + string.Join(",", result.OperationIds) + ", state=" + result.State + ", result=" + result.ResultCode);
        }
        catch (Exception exception)
        {
            status = "Governance refused: " + exception.Message;
            entry.Logger.Error("[correlation=" + correlation + "] [event=" + eventName + "-refused] error=" + exception);
        }
    }

    private static void DrawCompanyTransfers(UnityModManager.ModEntry entry)
    {
        GUILayout.Label("Company transfer amount:");
        transferAmount = GUILayout.TextField(transferAmount ?? "", 20);
        if (!runtimeSettings.EnableCompanyTransfers)
        {
            GUILayout.Label("Company transfers are disabled by runtime settings.");
            return;
        }
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Contribute personal → company")) ExecuteCompanyTransfer(entry, true);
        if (GUILayout.Button("Withdraw company → personal")) ExecuteCompanyTransfer(entry, false);
        GUILayout.EndHorizontal();
    }

    private static void ExecuteCompanyTransfer(UnityModManager.ModEntry entry, bool toCompany)
    {
        var correlation = Guid.NewGuid().ToString("N");
        var externalChanged = false;
        long amount = 0;
        try
        {
            RequireHostAuthority();
            if (!long.TryParse(transferAmount, out amount) || amount <= 0) throw new InvalidOperationException("Transfer amount must be a positive whole number.");
            TrySynchronizeHostWallet(entry, "before-company-transfer");
            if (toCompany)
            {
                if (!hostWallet.TryDebit(amount)) throw new InvalidOperationException("The authoritative personal wallet has insufficient funds.");
                externalChanged = true;
            }
            var result = runtimeStateProvider!.TransferLocalCompany("company-transfer:" + correlation, amount, toCompany);
            if (result.State != CommandState.Succeeded)
            {
                if (externalChanged) hostWallet.Credit(amount);
                externalChanged = false;
                throw new InvalidOperationException(result.ResultCode);
            }
            if (!toCompany)
            {
                hostWallet.Credit(amount);
                externalChanged = true;
            }
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance))
            {
                var compensation = runtimeStateProvider.TransferLocalCompany("company-transfer-compensation:" + correlation, amount, !toCompany);
                if (compensation.State != CommandState.Succeeded) throw new InvalidOperationException("Save staging and transfer compensation both failed.");
                if (toCompany) hostWallet.Credit(amount);
                else if (!hostWallet.TryDebit(amount)) throw new InvalidOperationException("Save staging failed and the vanilla withdrawal could not be reversed.");
                externalChanged = false;
                throw new InvalidOperationException("Save staging failed; transfer was compensated.");
            }
            status = (toCompany ? "Contribution" : "Withdrawal") + " succeeded: " + amount + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=company-transfer] direction=" + (toCompany ? "personal-to-company" : "company-to-personal") + ", amount=" + amount + ", result=" + result.ResultCode);
        }
        catch (Exception exception)
        {
            status = "Company transfer refused: " + exception.Message;
            entry.Logger.Error("[correlation=" + correlation + "] [event=company-transfer-refused] direction=" + (toCompany ? "personal-to-company" : "company-to-personal") + ", amount=" + amount + ", externalChanged=" + externalChanged + ", error=" + exception);
            TrySynchronizeHostWallet(entry, "after-transfer-refusal");
        }
    }

    private static void DrawVehicleAcquisition(UnityModManager.ModEntry entry, bool hasCompany)
    {
        GUILayout.Label("Existing vehicle acquisition");
        if (!runtimeSettings.EnableVehicleAcquisition)
        {
            GUILayout.Label("Vehicle acquisition is disabled by runtime settings.");
            return;
        }
        if (GUILayout.Button("Refresh resolved visible vehicles"))
        {
            selectableVehicles = visibleVehicles.ReadVisibleInventory(Guid.NewGuid().ToString("N"))
                .Where(x => x.Resolution == ResolutionState.Resolved)
                .OrderBy(x => AcquisitionCategoryOrder(x)).ThenBy(x => x.ExistingVisibleId, StringComparer.Ordinal).Take(200).ToArray();
            selectedVehicleIndex = selectableVehicles.Count == 0 ? -1 : 0;
            status = "Resolved non-traffic vehicles available: " + selectableVehicles.Count + ".";
        }
        acquisitionVehicleFilter = GUILayout.SelectionGrid(acquisitionVehicleFilter, new[] { "All", "Locomotives", "Freight", "Passenger" }, 4);
        var displayed = Enumerable.Range(0, selectableVehicles.Count).Where(i => MatchesAcquisitionFilter(selectableVehicles[i], acquisitionVehicleFilter)).ToArray();
        GUILayout.Label("Showing " + displayed.Length + " of " + selectableVehicles.Count + " resolved vehicles. Locomotives are sorted first; use the category filters instead of relying on the visible ID prefix.");
        foreach (var i in displayed)
        {
            var vehicle = selectableVehicles[i];
            var selected = i == selectedVehicleIndex ? "> " : "  ";
            var category = FleetVehicleClassifier.Classify(vehicle.Type, vehicle.DefinitionId);
            if (GUILayout.Button(selected + "[" + category + "] " + (vehicle.ExistingVisibleId ?? "unnamed") + " | " + vehicle.DefinitionId + " | " + vehicle.ExistingPersistentId))
                selectedVehicleIndex = i;
        }
        GUILayout.Label("Offer price (validation value):");
        offerPrice = GUILayout.TextField(offerPrice ?? "", 20);
        if (GUILayout.Button("Prepare audited offer for selected vehicle")) PrepareVehicleOffer(entry);
        if (!string.IsNullOrWhiteSpace(preparedOfferId))
        {
            acquireForCompany = hasCompany && GUILayout.Toggle(acquireForCompany, "Buyer and payer: company (off = personal)");
            if (GUILayout.Button("Acquire prepared existing vehicle")) AcquirePreparedVehicle(entry, acquireForCompany && hasCompany);
        }
    }

    private static int AcquisitionCategoryOrder(VehicleInstanceRecord vehicle)
    {
        switch (FleetVehicleClassifier.Classify(vehicle.Type, vehicle.DefinitionId))
        {
            case FleetVehicleKind.Locomotive: return 0;
            case FleetVehicleKind.PassengerCar: return 1;
            case FleetVehicleKind.FreightWagon: return 2;
            default: return 3;
        }
    }

    private static bool MatchesAcquisitionFilter(VehicleInstanceRecord vehicle, int filter)
    {
        if (filter == 0) return true;
        var kind = FleetVehicleClassifier.Classify(vehicle.Type, vehicle.DefinitionId);
        return filter == 1 ? kind == FleetVehicleKind.Locomotive : filter == 2 ? kind == FleetVehicleKind.FreightWagon : kind == FleetVehicleKind.PassengerCar;
    }

    private static void PrepareVehicleOffer(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            if (selectedVehicleIndex < 0 || selectedVehicleIndex >= selectableVehicles.Count) throw new InvalidOperationException("Select one resolved visible vehicle first.");
            if (!long.TryParse(offerPrice, out var price) || price < 0) throw new InvalidOperationException("Offer price must be a non-negative whole number.");
            var vehicle = selectableVehicles[selectedVehicleIndex];
            var offer = runtimeStateProvider!.PrepareVisibleVehicleOffer(vehicle, price);
            preparedOfferId = offer.OfferId;
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance))
                throw new InvalidOperationException("Offer was prepared in memory but could not be staged for save.");
            status = "Offer prepared: " + offer.OfferId + " / price " + offer.Price + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=offer-prepared] offer=" + offer.OfferId + ", asset=" + offer.AssetId + ", carGuid=" + vehicle.ExistingPersistentId + ", definition=" + vehicle.DefinitionId + ", price=" + offer.Price + ", source=" + offer.ReferenceSource + ", condition=" + offer.ObservedCondition + ", rate=" + offer.AppliedRate);
        }
        catch (Exception exception)
        {
            status = "Offer refused: " + exception.Message;
            entry.Logger.Error("[correlation=" + correlation + "] [event=offer-refused] " + exception);
        }
    }

    private static void AcquirePreparedVehicle(UnityModManager.ModEntry entry, bool forCompany)
    {
        var correlation = Guid.NewGuid().ToString("N");
        var externalDebited = false;
        long price = 0;
        try
        {
            RequireHostAuthority();
            TrySynchronizeHostWallet(entry, "before-vehicle-acquisition");
            var offer = runtimeStateProvider!.Current!.Offers.Single(x => x.OfferId == preparedOfferId);
            price = offer.Price;
            if (!forCompany && price > 0)
            {
                if (!hostWallet.TryDebit(price)) throw new InvalidOperationException("The authoritative personal wallet has insufficient funds.");
                externalDebited = true;
            }
            var sink = new DelegateAcquisitionCheckpointSink((checkpoint, stage) =>
            {
                if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Could not stage acquisition in SaveGameData.");
                entry.Logger.Log("[correlation=" + correlation + "] [event=acquisition-stage] checkpoint=" + checkpoint + ", stage=" + stage);
            });
            var result = runtimeStateProvider.AcquireLocal("vehicle-acquire:" + correlation, preparedOfferId!, forCompany, runtimeRoleDetector!, new UnityExistingVehicleOwnershipAdapter(), sink);
            if (externalDebited && (result.State == AcquisitionState.Compensated || result.State == AcquisitionState.Rejected))
            {
                hostWallet.Credit(price);
                externalDebited = false;
            }
            SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance);
            status = "Acquisition: " + result.State + " / " + result.ResultCode + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=vehicle-acquisition] offer=" + result.OfferId + ", asset=" + result.AssetId + ", buyer=" + result.Buyer.Key + ", payer=" + result.Payer.Key + ", price=" + result.Price + ", state=" + result.State + ", result=" + result.ResultCode + ", externalDebited=" + externalDebited);
        }
        catch (Exception exception)
        {
            if (externalDebited && price > 0) hostWallet.Credit(price);
            status = "Acquisition refused: " + exception.Message;
            entry.Logger.Error("[correlation=" + correlation + "] [event=vehicle-acquisition-refused] price=" + price + ", refunded=" + externalDebited + ", error=" + exception);
            TrySynchronizeHostWallet(entry, "after-acquisition-refusal");
        }
    }

    private static void DrawFiniteMarket(UnityModManager.ModEntry entry, VehicleAcquisitionSnapshot snapshot, bool hasCompany)
    {
        GUILayout.Space(8f); GUILayout.Label("Finite vehicle market (incremental validation module)");
        if (!runtimeSettings.EnableFiniteMarket) { GUILayout.Label("Finite market is disabled by runtime settings."); return; }
        GUILayout.Label("Catalog purchases create owned virtual stock. Initial delivery is free once and restricted to configured depot/service tracks.");
        GUILayout.Label("Location | base price | transfer fee | observed condition | market factor | buyback rate | new stock:");
        GUILayout.BeginHorizontal();
        marketLocation = GUILayout.TextField(marketLocation ?? "", 32); marketBasePrice = GUILayout.TextField(marketBasePrice ?? "", 16);
        marketTransferFee = GUILayout.TextField(marketTransferFee ?? "", 16); marketCondition = GUILayout.TextField(marketCondition ?? "", 8);
        marketFactor = GUILayout.TextField(marketFactor ?? "", 8); marketBuybackRate = GUILayout.TextField(marketBuybackRate ?? "", 8);
        marketNewStock = GUILayout.TextField(marketNewStock ?? "", 6);
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Configure selected definition and publish this existing vehicle")) PublishFiniteMarketVehicle(entry);
        if (GUILayout.Button("Generate one stock-backed catalog listing")) GenerateFiniteMarketOrder(entry);
        foreach (var listing in snapshot.Market.Listings.Where(x => x.State == MarketListingState.Available || x.State == MarketListingState.DeliveryPending).OrderBy(x => x.ExpiresTick).Take(50).ToArray())
        {
            var marker = listing.ListingId == selectedMarketListingId ? "> " : "  ";
            if (GUILayout.Button(marker + listing.DefinitionId + " | " + listing.LocationId + " | " + listing.Price + " | " + listing.State + " | stock-backed=" + (listing.Kind == MarketListingKind.NewOrder))) selectedMarketListingId = listing.ListingId;
        }
        if (!string.IsNullOrWhiteSpace(selectedMarketListingId))
        {
            marketBuyForCompany = hasCompany && GUILayout.Toggle(marketBuyForCompany, "Buyer and payer: company (off = personal)");
            if (GUILayout.Button("Purchase selected finite listing")) PurchaseFiniteMarketListing(entry, marketBuyForCompany && hasCompany);
        }
        var pending = snapshot.Market.Purchases.Count(x => x.State == MarketPurchaseState.ReconcileRequired);
        if (pending > 0 && GUILayout.Button("Reconcile legacy pending market purchases (" + pending + ")")) ReconcileFiniteMarket(entry);
        DrawInitialDeliveries(entry, snapshot);
        if (runtimeSettings.EnableIndustrialPilot && GUILayout.Button("Create starter rolling-stock freight job")) CreateStarterFreightJob(entry);
        if (GUILayout.Button("Advance validation market clock by 100 ticks and expire due listings")) AdvanceFiniteMarketClock(entry);
    }

    private static void DrawInitialDeliveries(UnityModManager.ModEntry entry, VehicleAcquisitionSnapshot snapshot)
    {
        GUILayout.Space(4f);
        GUILayout.Label("Owned stock awaiting initial delivery:");
        foreach (var grant in snapshot.InitialDeliveries.Where(x => x.State != InitialDeliveryState.Delivered).OrderBy(x => x.GrantId).Take(30).ToArray())
        {
            var marker = grant.GrantId == selectedInitialDeliveryGrantId ? "> " : "  ";
            if (GUILayout.Button(marker + string.Join(" + ", grant.DefinitionIds) + " | " + grant.Owner.Key + " | " + grant.State + " | " + grant.ResultCode)) selectedInitialDeliveryGrantId = grant.GrantId;
        }
        if (string.IsNullOrWhiteSpace(selectedInitialDeliveryGrantId)) return;
        var selected = snapshot.InitialDeliveries.SingleOrDefault(x => x.GrantId == selectedInitialDeliveryGrantId);
        if (selected == null) { selectedInitialDeliveryGrantId = null; return; }
        if (runtimeSettings.InitialDeliveryTracks == null || runtimeSettings.InitialDeliveryTracks.Count == 0)
        {
            GUILayout.Label("No delivery track is configured. Add exact track IDs to runtime-settings.json before enabling physical placement.");
            return;
        }
        foreach (var rule in runtimeSettings.InitialDeliveryTracks.OrderBy(x => x.TrackId).ToArray())
            if (GUILayout.Button("Place once on " + rule.Kind + " track " + rule.TrackId)) PlaceInitialDelivery(entry, selected, rule);
        if ((selected.State == InitialDeliveryState.PlacementPending || selected.State == InitialDeliveryState.ReconcileRequired) && GUILayout.Button("Reconcile selected physical delivery")) ReconcileInitialDeliveries(entry);
    }

    private static void PublishFiniteMarketVehicle(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            if (selectedVehicleIndex < 0 || selectedVehicleIndex >= selectableVehicles.Count) throw new InvalidOperationException("Refresh and select one resolved non-traffic vehicle first.");
            if (!long.TryParse(marketBasePrice, out var basePrice) || basePrice <= 0 || !long.TryParse(marketTransferFee, out var fee) || fee < 0 ||
                !decimal.TryParse(marketCondition, out var condition) || !decimal.TryParse(marketFactor, out var factor) || !decimal.TryParse(marketBuybackRate, out var buyback) ||
                !int.TryParse(marketNewStock, out var stock) || stock < 0)
                throw new InvalidOperationException("Market values are invalid. Use whole currency amounts and decimal factors between 0 and 1 where applicable.");
            var vehicle = selectableVehicles[selectedVehicleIndex];
            runtimeStateProvider!.ConfigureFiniteMarketDefinition(vehicle.DefinitionId!, vehicle.Type ?? "Unknown", basePrice, fee, 0.8m, 1.2m, buyback, marketLocation, stock);
            var listing = runtimeStateProvider.PublishVisibleMarketListing("market-listing:" + correlation, vehicle, marketLocation, condition, factor, runtimeRoleDetector!, new UnityExistingVehicleOwnershipAdapter());
            selectedMarketListingId = listing.ListingId;
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Market listing could not be staged in SaveGameData.");
            status = "Finite listing ready: " + listing.Price + " at " + listing.LocationId + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=finite-market-listing] listing=" + listing.ListingId + ", asset=" + listing.AssetId + ", definition=" + listing.DefinitionId + ", location=" + listing.LocationId + ", reference=" + listing.ReferenceValue + ", factor=" + listing.MarketFactor + ", fee=" + listing.TransferFee + ", price=" + listing.Price);
        }
        catch (Exception exception) { status = "Finite listing refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=finite-market-listing-refused] " + exception); }
    }

    private static void GenerateFiniteMarketOrder(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            if (selectedVehicleIndex < 0 || selectedVehicleIndex >= selectableVehicles.Count) throw new InvalidOperationException("Select a resolved vehicle whose definition is already configured with positive new stock.");
            var definitionId = selectableVehicles[selectedVehicleIndex].DefinitionId!;
            var listing = runtimeStateProvider!.GenerateFiniteMarketOrder("market-new-order:" + correlation, definitionId, marketLocation, runtimeRoleDetector!, new UnityExistingVehicleOwnershipAdapter());
            selectedMarketListingId = listing.ListingId;
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("New-order listing could not be staged in SaveGameData.");
            status = "New-order listing generated from finite stock; runtime delivery remains disabled.";
            entry.Logger.Log("[correlation=" + correlation + "] [event=finite-market-new-order] listing=" + listing.ListingId + ", definition=" + listing.DefinitionId + ", location=" + listing.LocationId + ", price=" + listing.Price + ", expires=" + listing.ExpiresTick);
        }
        catch (Exception exception) { status = "New-order listing refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=finite-market-new-order-refused] " + exception); }
    }

    private static void AdvanceFiniteMarketClock(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try { RequireHostAuthority(); var next = checked(runtimeStateProvider!.Current!.Market.ClockTick + 100); runtimeStateProvider.AdvanceFiniteMarket(next, runtimeRoleDetector!, new UnityExistingVehicleOwnershipAdapter()); if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Market clock could not be staged in SaveGameData."); status = "Market clock advanced to " + next + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=finite-market-clock] tick=" + next); }
        catch (Exception exception) { status = "Market clock advance refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=finite-market-clock-refused] " + exception); }
    }

    private static void PurchaseFiniteMarketListing(UnityModManager.ModEntry entry, bool forCompany)
    {
        var correlation = Guid.NewGuid().ToString("N"); var externalDebited = false; var domainCommitted = false; long price = 0;
        try
        {
            RequireHostAuthority(); TrySynchronizeHostWallet(entry, "before-finite-market-purchase");
            var listing = runtimeStateProvider!.Current!.Market.Listings.Single(x => x.ListingId == selectedMarketListingId); price = listing.Price;
            if (!forCompany && price > 0) { if (!hostWallet.TryDebit(price)) throw new InvalidOperationException("The authoritative personal wallet has insufficient funds."); externalDebited = true; }
            var result = runtimeStateProvider.PurchaseLocalMarket("market-purchase:" + correlation, listing.ListingId, forCompany, runtimeRoleDetector!, new UnityExistingVehicleOwnershipAdapter(), new DisabledMarketDeliveryPort());
            domainCommitted = result.State == MarketPurchaseState.Succeeded || result.State == MarketPurchaseState.ReconcileRequired;
            if (externalDebited && (result.State == MarketPurchaseState.Rejected || result.State == MarketPurchaseState.Compensated)) { hostWallet.Credit(price); externalDebited = false; }
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Market purchase could not be staged in SaveGameData.");
            status = "Finite market purchase: " + result.State + " / " + result.ResultCode + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=finite-market-purchase] listing=" + result.ListingId + ", asset=" + result.AssetId + ", payer=" + result.Payer.Key + ", buyer=" + result.Buyer.Key + ", price=" + result.Price + ", state=" + result.State + ", result=" + result.ResultCode + ", externalDebited=" + externalDebited);
        }
        catch (Exception exception) { if (externalDebited && !domainCommitted && price > 0) hostWallet.Credit(price); status = domainCommitted ? "Finite market purchase committed but save staging requires retry: " + exception.Message : "Finite market purchase refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=finite-market-purchase-refused] domainCommitted=" + domainCommitted + ", externalDebited=" + externalDebited + ", error=" + exception); if (!domainCommitted) TrySynchronizeHostWallet(entry, "after-finite-market-refusal"); }
    }

    private static void ReconcileFiniteMarket(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try { RequireHostAuthority(); var results = runtimeStateProvider!.ReconcilePendingMarketPurchases(runtimeRoleDetector!, new UnityExistingVehicleOwnershipAdapter(), new DisabledMarketDeliveryPort()); SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance); status = "Market reconciliation: " + results.Count + " record(s)."; foreach (var result in results) entry.Logger.Log("[correlation=" + correlation + "] [event=finite-market-reconcile] command=" + result.CommandId + ", state=" + result.State + ", result=" + result.ResultCode); }
        catch (Exception exception) { status = "Market reconciliation refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=finite-market-reconcile-failed] " + exception); }
    }

    private static void PlaceInitialDelivery(UnityModManager.ModEntry entry, InitialDeliveryGrant grant, InitialDeliveryTrackRule rule, double? aimedSpan = null, bool? withTrackDirection = null)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            // The in-game radio supplies a validated one-shot track rule; configured rules remain
            // supported for the legacy/debug panel.
            var spans = aimedSpan.HasValue ? new Dictionary<string, double>(StringComparer.Ordinal) { [rule.TrackId] = aimedSpan.Value } : null;
            var directions = withTrackDirection.HasValue ? new Dictionary<string, bool>(StringComparer.Ordinal) { [rule.TrackId] = withTrackDirection.Value } : null;
            var snapshot = runtimeStateProvider!.Current!;
            var adapter = new UnityInitialDeliveryAdapter((runtimeSettings.InitialDeliveryTracks ?? new List<InitialDeliveryTrackRule>()).Concat(new[] { rule }), spans, directions, snapshot);
            var result = runtimeStateProvider.PlaceLocalInitialDelivery("initial-delivery:" + correlation, grant.GrantId, rule.TrackId, rule.Kind, runtimeRoleDetector!, adapter, new SaveGameInitialDeliveryCheckpointPort(entry));
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Initial delivery state could not be staged in SaveGameData.");
            status = "Initial delivery: " + result.State + " / " + result.ResultCode + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=initial-delivery] grant=" + result.GrantId + ", owner=" + result.Owner.Key + ", track=" + result.TargetTrackId + ", kind=" + result.TargetKind + ", components=" + string.Join(",", result.AssetIds) + ", state=" + result.State + ", result=" + result.ResultCode);
        }
        catch (Exception exception) { status = "Initial delivery refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=initial-delivery-refused] grant=" + grant.GrantId + ", track=" + rule.TrackId + ", error=" + exception); }
    }

    private static IReadOnlyList<InitialDeliveryGrant> PendingInitialDeliveries() => runtimeStateProvider?.Current?.InitialDeliveries
        ?.Where(x => x.State != InitialDeliveryState.Delivered).ToArray() ?? Array.Empty<InitialDeliveryGrant>();

    private static string DeliverFromRadio(RailTrack track, double aimedSpan, bool withTrackDirection, InitialDeliveryTargetKind kind, InitialDeliveryGrant grant)
    {
        if (mod == null) return "BDVM is not ready.";
        var trackId = track.LogicTrack()?.ID?.ToString();
        if (string.IsNullOrWhiteSpace(trackId)) return "Track identity is unavailable.";
        PlaceInitialDelivery(mod, grant, new InitialDeliveryTrackRule { TrackId = trackId!, Kind = kind }, aimedSpan, withTrackDirection);
        return status;
    }

    private static IReadOnlyList<InitialDeliveryTrackRule> LeaseReturnTrackRules(VehicleAcquisitionSnapshot snapshot)
    {
        var configured = runtimeSettings.InitialDeliveryTracks ?? new List<InitialDeliveryTrackRule>();
        var delivered = snapshot.InitialDeliveries
            .Where(value => value.State == InitialDeliveryState.Delivered && !string.IsNullOrWhiteSpace(value.TargetTrackId) && value.TargetKind.HasValue)
            .Select(value => new InitialDeliveryTrackRule { TrackId = value.TargetTrackId!, Kind = value.TargetKind!.Value });
        return configured.Concat(delivered)
            .GroupBy(value => value.TrackId.Trim(), StringComparer.Ordinal)
            .Select(group => new InitialDeliveryTrackRule { TrackId = group.Key, Kind = group.First().Kind })
            .OrderBy(value => value.TrackId, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<InitialDeliveryTrackRule> InitialDeliveryReconciliationTrackRules(VehicleAcquisitionSnapshot snapshot)
    {
        var pending = snapshot.InitialDeliveries
            .Where(value => (value.State == InitialDeliveryState.PlacementPending || value.State == InitialDeliveryState.ReconcileRequired) &&
                !string.IsNullOrWhiteSpace(value.TargetTrackId) && value.TargetKind.HasValue)
            .Select(value => new InitialDeliveryTrackRule { TrackId = value.TargetTrackId!, Kind = value.TargetKind!.Value });
        return LeaseReturnTrackRules(snapshot).Concat(pending)
            .GroupBy(value => value.TrackId.Trim(), StringComparer.Ordinal)
            .Select(group => new InitialDeliveryTrackRule { TrackId = group.Key, Kind = group.First().Kind })
            .OrderBy(value => value.TrackId, StringComparer.Ordinal).ToArray();
    }

    private static void ReconcileInitialDeliveries(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            var snapshot = runtimeStateProvider!.Current!;
            var results = runtimeStateProvider.ReconcilePendingInitialDeliveries(runtimeRoleDetector!, new UnityInitialDeliveryAdapter(InitialDeliveryReconciliationTrackRules(snapshot), snapshot: snapshot), new SaveGameInitialDeliveryCheckpointPort(entry));
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Reconciled initial delivery state could not be staged in SaveGameData.");
            status = "Initial delivery reconciliation: " + results.Count + " record(s).";
            foreach (var result in results) entry.Logger.Log("[correlation=" + correlation + "] [event=initial-delivery-reconcile] grant=" + result.GrantId + ", state=" + result.State + ", result=" + result.ResultCode);
        }
        catch (Exception exception) { status = "Initial delivery reconciliation refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=initial-delivery-reconcile-failed] " + exception); }
    }

    private static void CreateStarterFreightJob(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            EnsureSelfShuntIndustrialLifecycle(entry);
            status = UnityStarterFreightJobAdapter.Create(runtimeStateProvider!.Current!, runtimeStateProvider.LocalPlayerId!, selfShuntIndustrialLifecycle ?? throw new InvalidOperationException("SelfShunt industrial lifecycle is unavailable."));
            StageIndustrialSave();
            entry.Logger.Log("[correlation=" + correlation + "] [event=starter-freight-job-created] " + status);
        }
        catch (Exception exception)
        {
            status = "Starter freight job refused: " + exception.Message;
            entry.Logger.Error("[correlation=" + correlation + "] [event=starter-freight-job-refused] " + exception);
        }
    }

    private static void DrawDynamicEconomy(UnityModManager.ModEntry entry, VehicleAcquisitionSnapshot snapshot)
    {
        GUILayout.Space(8f); GUILayout.Label("Dynamic market balancing (bounded, smoothed, deterministic)");
        if (!runtimeSettings.EnableDynamicEconomy) { GUILayout.Label("Dynamic economy is disabled by runtime settings."); return; }
        GUILayout.Label("Minimum | maximum | smoothing | max step | supply weight | demand weight | utilization weight:");
        GUILayout.BeginHorizontal(); dynamicMinimum = GUILayout.TextField(dynamicMinimum, 8); dynamicMaximum = GUILayout.TextField(dynamicMaximum, 8); dynamicSmoothing = GUILayout.TextField(dynamicSmoothing, 8); dynamicMaximumStep = GUILayout.TextField(dynamicMaximumStep, 8); dynamicSupplyWeight = GUILayout.TextField(dynamicSupplyWeight, 8); dynamicDemandWeight = GUILayout.TextField(dynamicDemandWeight, 8); dynamicUtilizationWeight = GUILayout.TextField(dynamicUtilizationWeight, 8); GUILayout.EndHorizontal();
        var selectedListing = snapshot.Market.Listings.SingleOrDefault(x => x.ListingId == selectedMarketListingId); var category = selectedListing?.CategoryId;
        if (!string.IsNullOrWhiteSpace(category) && GUILayout.Button("Configure policy for selected category: " + category)) ConfigureDynamicPolicy(entry, category!);
        if (snapshot.DynamicEconomy.Policies.Count > 0 && GUILayout.Button("Recalculate all dynamic factors at next economic tick")) RecalculateDynamicEconomy(entry);
        foreach (var metric in snapshot.DynamicEconomy.Metrics.OrderBy(x => x.CategoryId).Take(30).ToArray()) GUILayout.Label(metric.CategoryId + " | factor=" + metric.SmoothedFactor + " | raw=" + metric.RawFactor + " | supply=" + metric.SupplyRatio + " | demand=" + metric.DemandRatio + " | utilization=" + metric.UtilizationRatio + " | lessor availability=" + metric.LessorAvailabilityRatio);
        if (snapshot.DynamicEconomy.Profitability.Count > 0) GUILayout.Label("Per-asset operating profitability (capital shown separately):");
        foreach (var item in snapshot.DynamicEconomy.Profitability.OrderByDescending(x => x.NetOperatingResult).Take(30).ToArray()) { var fleet = snapshot.Fleet.SingleOrDefault(x => x.AssetId == item.AssetId); GUILayout.Label((fleet?.DisplayName ?? item.AssetId) + " | revenue=" + item.OperatingRevenue + " | costs=" + item.OperatingCosts + " | net=" + item.NetOperatingResult + " | acquisition cash=" + item.AcquisitionCash + " | services=" + item.CompletedServices); }
    }

    private static void ConfigureDynamicPolicy(UnityModManager.ModEntry entry, string category)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); if (!decimal.TryParse(dynamicMinimum, out var minimum) || !decimal.TryParse(dynamicMaximum, out var maximum) || !decimal.TryParse(dynamicSmoothing, out var smoothing) || !decimal.TryParse(dynamicMaximumStep, out var step) || !decimal.TryParse(dynamicSupplyWeight, out var supply) || !decimal.TryParse(dynamicDemandWeight, out var demand) || !decimal.TryParse(dynamicUtilizationWeight, out var utilization)) throw new InvalidOperationException("Dynamic policy values are invalid."); var policy = runtimeStateProvider!.ConfigureLocalDynamicMarketPolicy("dynamic-policy:" + correlation, category, minimum, maximum, smoothing, step, supply, demand, utilization, runtimeRoleDetector!); SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance); status = "Dynamic policy configured: " + policy.CategoryId + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=dynamic-policy-configured] category=" + policy.CategoryId + ", bounds=" + policy.MinimumFactor + ".." + policy.MaximumFactor + ", smoothing=" + policy.Smoothing + ", maxStep=" + policy.MaximumStep + ", weights=" + policy.SupplyWeight + "/" + policy.DemandWeight + "/" + policy.UtilizationWeight); } catch (Exception exception) { status = "Dynamic policy refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=dynamic-policy-refused] " + exception); }
    }

    private static void RecalculateDynamicEconomy(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); var snapshot = runtimeStateProvider!.Current!; var tick = Math.Max(Math.Max(snapshot.Market.ClockTick, snapshot.LeaseClock.ActiveTick), snapshot.DynamicEconomy.LastCalculatedTick) + 1; var metrics = runtimeStateProvider.RecalculateLocalDynamicEconomy("dynamic-recalculate:" + correlation, tick, runtimeRoleDetector!); if (!string.IsNullOrWhiteSpace(selectedMarketListingId)) { var listing = snapshot.Market.Listings.SingleOrDefault(x => x.ListingId == selectedMarketListingId); if (listing != null) marketFactor = runtimeStateProvider.LocalDynamicFactorForDefinition(listing.DefinitionId, runtimeRoleDetector!).ToString(); } SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance); status = "Dynamic economy recalculated for " + metrics.Count + " category(s)."; foreach (var metric in metrics) entry.Logger.Log("[correlation=" + correlation + "] [event=dynamic-factor] category=" + metric.CategoryId + ", tick=" + metric.CalculatedTick + ", factor=" + metric.SmoothedFactor + ", raw=" + metric.RawFactor + ", supply=" + metric.SupplyRatio + ", demand=" + metric.DemandRatio + ", utilization=" + metric.UtilizationRatio + ", lessorAvailability=" + metric.LessorAvailabilityRatio); } catch (Exception exception) { status = "Dynamic recalculation refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=dynamic-recalculation-refused] " + exception); }
    }

    private static void DrawFinancing(UnityModManager.ModEntry entry, VehicleAcquisitionSnapshot snapshot, bool hasCompany)
    {
        GUILayout.Space(8f); GUILayout.Label("Financing (explicit backed pool; incremental validation module)");
        if (!runtimeSettings.EnableFinancing) { GUILayout.Label("Financing is disabled by runtime settings."); return; }
        GUILayout.Label("Validation lender pool ID | externally backed capital:"); GUILayout.BeginHorizontal(); financingPoolId = GUILayout.TextField(financingPoolId, 32); financingPoolCapital = GUILayout.TextField(financingPoolCapital, 14); GUILayout.EndHorizontal();
        if (!snapshot.Financing.Pools.Any(x => x.PoolId == financingPoolId) && GUILayout.Button("Register explicit validation lender pool")) ExecuteFinancing(entry, "pool-register", () => { if (!long.TryParse(financingPoolCapital, out var capital)) throw new InvalidOperationException("Pool capital is invalid."); var result = runtimeStateProvider!.RegisterLocalFinancingPool("financing-pool:" + financingPoolId, financingPoolId, capital, runtimeRoleDetector!); return "pool=" + result.PoolId + ", available=" + result.AvailableCapital; });
        foreach (var pool in snapshot.Financing.Pools.Take(10).ToArray()) GUILayout.Label("Pool " + pool.PoolId + " | available=" + pool.AvailableCapital + " | initial=" + pool.InitialCapital + " | payments=" + pool.ReceivedPayments + " | written-off=" + pool.WrittenOff);
        financingKind = GUILayout.Toolbar(financingKind, new[] { "Loan", "Credit line" });
        GUILayout.Label("Principal/limit | interest bps | installment | interval | maturity | guarantee:"); GUILayout.BeginHorizontal(); financingPrincipal = GUILayout.TextField(financingPrincipal, 12); financingInterestBps = GUILayout.TextField(financingInterestBps, 8); financingInstallment = GUILayout.TextField(financingInstallment, 12); financingInterval = GUILayout.TextField(financingInterval, 8); financingMaturity = GUILayout.TextField(financingMaturity, 8); financingGuarantee = GUILayout.TextField(financingGuarantee, 12); GUILayout.EndHorizontal();
        financingForCompany = hasCompany && GUILayout.Toggle(financingForCompany, "Debtor: company (personal runtime bridge remains disabled until atomic vanilla-wallet settlement is proven)");
        GUI.enabled = financingForCompany && snapshot.Financing.Pools.Any(x => x.PoolId == financingPoolId);
        if (GUILayout.Button("Create fully disclosed financing offer")) ExecuteFinancing(entry, "offer", () => { if (!long.TryParse(financingPrincipal, out var principal) || !int.TryParse(financingInterestBps, out var bps) || !long.TryParse(financingInstallment, out var installment) || !long.TryParse(financingInterval, out var interval) || !long.TryParse(financingMaturity, out var maturity) || !long.TryParse(financingGuarantee, out var guarantee)) throw new InvalidOperationException("Financing terms are invalid."); var correlation = Guid.NewGuid().ToString("N"); var contract = runtimeStateProvider!.OfferLocalFinancing("financing-offer:" + correlation, "financing:" + correlation, financingKind == 0 ? FinancingKind.Loan : FinancingKind.CreditLine, true, financingPoolId, principal, bps, installment, interval, maturity, guarantee, runtimeRoleDetector!); selectedFinancingContractId = contract.ContractId; return "offer=" + contract.ContractId + ", terms=" + contract.Terms; });
        GUI.enabled = true;
        foreach (var contract in snapshot.Financing.Contracts.Where(x => x.State != FinancingState.Settled && x.State != FinancingState.Cancelled && x.State != FinancingState.WrittenOff).Take(30).ToArray()) if (GUILayout.Button((contract.ContractId == selectedFinancingContractId ? "> " : "  ") + contract.ContractId + " | " + contract.Kind + " | " + contract.State + " | debtor=" + contract.Debtor.Key + " | principal=" + contract.OutstandingPrincipal + "/" + contract.PrincipalLimit + " | interest=" + contract.AccruedInterest + " | next=" + contract.NextDueTick + " | " + contract.Terms)) selectedFinancingContractId = contract.ContractId;
        if (string.IsNullOrWhiteSpace(selectedFinancingContractId)) return; var selected = snapshot.Financing.Contracts.Single(x => x.ContractId == selectedFinancingContractId);
        if (selected.State == FinancingState.Offered && GUILayout.Button("Accept selected disclosed offer")) ExecuteFinancing(entry, "accept", () => { var result = runtimeStateProvider!.AcceptLocalFinancing("financing-accept:" + Guid.NewGuid().ToString("N"), selected.ContractId, runtimeRoleDetector!); return result.ResultCode + ", amount=" + result.Amount; });
        financingTransactionAmount = GUILayout.TextField(financingTransactionAmount, 12);
        if (selected.Kind == FinancingKind.CreditLine && selected.State == FinancingState.Active && GUILayout.Button("Draw entered amount from selected credit line")) ExecuteFinancing(entry, "draw", () => { if (!long.TryParse(financingTransactionAmount, out var amount)) throw new InvalidOperationException("Draw amount is invalid."); var result = runtimeStateProvider!.DrawLocalCredit("financing-draw:" + Guid.NewGuid().ToString("N"), selected.ContractId, amount, runtimeRoleDetector!); return result.ResultCode + ", amount=" + result.Amount; });
        if ((selected.State == FinancingState.Active || selected.State == FinancingState.Defaulted) && GUILayout.Button("Repay entered amount")) ExecuteFinancing(entry, "repay", () => { if (!long.TryParse(financingTransactionAmount, out var amount)) throw new InvalidOperationException("Repayment amount is invalid."); var result = runtimeStateProvider!.RepayLocalFinancing("financing-repay:" + Guid.NewGuid().ToString("N"), selected.ContractId, amount, runtimeRoleDetector!); return result.ResultCode + ", paid=" + result.Amount; });
        if (GUILayout.Button("Process financing at current economic clock")) ExecuteFinancing(entry, "clock", () => "paid=" + runtimeStateProvider!.ProcessLocalFinancingClock("financing-clock:" + Guid.NewGuid().ToString("N"), snapshot.LeaseClock.ActiveTick, runtimeRoleDetector!));
    }

    private static void ExecuteFinancing(UnityModManager.ModEntry entry, string action, Func<string> execute)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); var detail = execute(); if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Financing state could not be staged in SaveGameData."); status = "Financing " + action + ": " + detail; entry.Logger.Log("[correlation=" + correlation + "] [event=financing-" + action + "] " + detail); } catch (Exception exception) { status = "Financing " + action + " refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=financing-" + action + "-refused] " + exception); }
    }

    private static void DrawInboundLeasing(UnityModManager.ModEntry entry, VehicleAcquisitionSnapshot snapshot, bool hasCompany)
    {
        GUILayout.Space(8f); GUILayout.Label("Inbound leasing (incremental validation module)");
        if (!runtimeSettings.EnableInboundLeasing) { GUILayout.Label("Inbound leasing is disabled by runtime settings."); return; }
        GUILayout.Label("Deposit | initial fee | rent | interval | duration | purchase option (blank = none) | max damage | condition:");
        GUILayout.BeginHorizontal(); leaseDeposit = GUILayout.TextField(leaseDeposit, 12); leaseInitialFee = GUILayout.TextField(leaseInitialFee, 12); leaseRent = GUILayout.TextField(leaseRent, 12); leaseInterval = GUILayout.TextField(leaseInterval, 8); leaseDuration = GUILayout.TextField(leaseDuration, 8); leasePurchaseOption = GUILayout.TextField(leasePurchaseOption, 12); leaseDamageMaximum = GUILayout.TextField(leaseDamageMaximum, 12); leaseCondition = GUILayout.TextField(leaseCondition, 8); GUILayout.EndHorizontal();
        if (GUILayout.Button("Create lease offer from selected catalog or existing listing")) CreateInboundLeaseOffer(entry);
        foreach (var lease in snapshot.Leases.Where(x => x.State != LeaseState.Returned && x.State != LeaseState.Purchased && x.State != LeaseState.Cancelled).Take(50).ToArray())
        {
            var marker = lease.LeaseId == selectedLeaseId ? "> " : "  ";
            if (GUILayout.Button(marker + lease.LeaseId + " | " + lease.State + " | assets=" + lease.AssetIds.Count + " | deposit=" + lease.Deposit + " | rent=" + lease.RentAmount + " | debt=" + lease.OutstandingDebt)) selectedLeaseId = lease.LeaseId;
        }
        if (string.IsNullOrWhiteSpace(selectedLeaseId)) return;
        var selected = snapshot.Leases.Single(x => x.LeaseId == selectedLeaseId);
        leaseForCompany = hasCompany && GUILayout.Toggle(leaseForCompany, "Lessee and payer: company (off = personal)");
        if (selected.State == LeaseState.Offered && GUILayout.Button("Accept selected lease")) AcceptInboundLease(entry, leaseForCompany && hasCompany);
        if (selected.State == LeaseState.Active || selected.State == LeaseState.Delinquent || selected.State == LeaseState.ReturnDue)
        {
            GUILayout.Label("Active economic ticks (session-open simulation; pause/closed time is excluded):"); leaseAdvanceTicks = GUILayout.TextField(leaseAdvanceTicks, 10);
            if (GUILayout.Button("Advance active lease clock")) AdvanceInboundLeaseClock(entry);
            if (GUILayout.Button("Return selected lease (authoritative condition; depot/service track required)")) ReturnInboundLease(entry);
            if (selected.PurchaseOptionPrice != null && GUILayout.Button("Exercise purchase option")) PurchaseInboundLease(entry);
        }
        var pending = snapshot.LeaseActions.Count(x => x.State == LeaseActionState.ReconcileRequired);
        if (pending > 0 && GUILayout.Button("Reconcile pending lease purchases (" + pending + ")")) ReconcileInboundLeases(entry);
    }

    private static void CreateInboundLeaseOffer(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority(); var listing = runtimeStateProvider!.Current!.Market.Listings.Single(x => x.ListingId == selectedMarketListingId);
            if (!long.TryParse(leaseDeposit, out var deposit) || !long.TryParse(leaseInitialFee, out var fee) || !long.TryParse(leaseRent, out var rent) || !long.TryParse(leaseInterval, out var interval) || !long.TryParse(leaseDuration, out var duration) || !long.TryParse(leaseDamageMaximum, out var damage) || !decimal.TryParse(leaseCondition, out var condition)) throw new InvalidOperationException("Lease terms are invalid.");
            long? option = string.IsNullOrWhiteSpace(leasePurchaseOption) ? null : long.Parse(leasePurchaseOption);
            var lease = listing.Kind == MarketListingKind.ExistingAsset && !string.IsNullOrWhiteSpace(listing.AssetId)
                ? runtimeStateProvider.CreateLocalLeaseOffer("lease:" + correlation, new[] { listing.AssetId! }, deposit, fee, rent, interval, duration, option, condition, damage, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter())
                : runtimeStateProvider.CreateLocalCatalogListingLeaseOffer("lease:" + correlation, new[] { listing.ListingId }, deposit, fee, rent, interval, duration, option, condition, damage, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter());
            selectedLeaseId = lease.LeaseId;
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Lease offer could not be staged in SaveGameData.");
            status = "Lease offer created: " + lease.LeaseId + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=lease-offer] lease=" + lease.LeaseId + ", source=" + listing.Kind + ", assets=" + string.Join(",", lease.AssetIds) + ", deposit=" + lease.Deposit + ", fee=" + lease.InitialFee + ", rent=" + lease.RentAmount + ", interval=" + lease.RentIntervalTicks + ", duration=" + lease.DurationTicks + ", option=" + lease.PurchaseOptionPrice);
        }
        catch (Exception exception) { status = "Lease offer refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=lease-offer-refused] " + exception); }
    }

    private static void AcceptInboundLease(UnityModManager.ModEntry entry, bool forCompany)
    {
        var correlation = Guid.NewGuid().ToString("N"); var externalDebited = false; long due = 0;
        LeaseContract? lease = null;
        try
        {
            RequireHostAuthority(); TrySynchronizeHostWallet(entry, "before-lease-accept"); lease = runtimeStateProvider!.Current!.Leases.Single(x => x.LeaseId == selectedLeaseId); due = checked(lease.Deposit + lease.InitialFee);
            if (!forCompany && due > 0) { if (!hostWallet.TryDebit(due)) throw new InvalidOperationException("The authoritative personal wallet has insufficient funds."); externalDebited = true; }
            var result = runtimeStateProvider.AcceptLocalLease("lease-accept:" + correlation, lease.LeaseId, forCompany, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter());
            if (result.State != LeaseActionState.Succeeded) throw new InvalidOperationException(result.ResultCode);
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Accepted lease could not be staged in SaveGameData.");
            status = "Lease accepted; deposit held separately."; entry.Logger.Log("[correlation=" + correlation + "] [event=lease-accept] lease=" + lease.LeaseId + ", payer=" + lease.Payer?.Key + ", lessee=" + lease.Lessee?.Key + ", amount=" + result.Amount + ", externalDebited=" + externalDebited);
        }
        catch (Exception exception)
        {
            var domainCommitted = lease != null && lease.State != LeaseState.Offered;
            var refunded = externalDebited && !domainCommitted;
            if (refunded) hostWallet.Credit(due);
            status = domainCommitted ? "Lease accepted but persistence requires recovery; do not retry payment." : "Lease acceptance refused: " + exception.Message;
            entry.Logger.Error("[correlation=" + correlation + "] [event=lease-accept-failed] externalDebited=" + externalDebited + ", externalRefunded=" + refunded + ", domainCommitted=" + domainCommitted + ", error=" + exception);
        }
    }

    private static void AdvanceInboundLeaseClock(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N"); var domainCommitted = false; var externalDebited = false; long previewDelta = 0;
        try
        {
            RequireHostAuthority(); if (!long.TryParse(leaseAdvanceTicks, out var ticks) || ticks < 0) throw new InvalidOperationException("Active ticks must be non-negative."); TrySynchronizeHostWallet(entry, "before-lease-clock");
            var playerId = runtimeStateProvider!.LocalPlayerId!; var snapshot = runtimeStateProvider.Current!; var wallet = snapshot.Economy.Wallets.Single(x => x.Account.Key == "Player:" + playerId); var before = wallet.Balance;
            var advance = new LeaseClockAdvance { CommandId = "lease-clock:" + correlation, SessionOpen = true, ActiveGameplayTicks = ticks };
            previewDelta = PreviewLeaseClockPersonalDelta(snapshot, advance);
            if (previewDelta < 0) { if (!hostWallet.TryDebit(-previewDelta)) throw new InvalidOperationException("The authoritative personal wallet cannot fund the pending lease installments."); externalDebited = true; }
            var tick = runtimeStateProvider.AdvanceLocalLeaseClock(advance, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter());
            domainCommitted = true;
            var delta = wallet.Balance - before; var debit = Math.Max(0, -delta); var credit = Math.Max(0, delta);
            if (delta != previewDelta) throw new InvalidOperationException("Lease clock preview diverged from the committed wallet delta.");
            if (credit > 0) hostWallet.Credit(credit);
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Lease clock could not be staged in SaveGameData.");
            status = "Lease clock: " + tick + "; personal debit=" + debit + "; outbound credit=" + credit + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=lease-clock] tick=" + tick + ", activeDelta=" + ticks + ", personalDebit=" + debit + ", outboundCredit=" + credit);
        }
        catch (Exception exception)
        {
            if (externalDebited && !domainCommitted) hostWallet.Credit(-previewDelta);
            status = domainCommitted ? "Lease clock committed but persistence requires recovery; do not advance it again." : "Lease clock refused: " + exception.Message;
            entry.Logger.Error("[correlation=" + correlation + "] [event=lease-clock-failed] externalRefunded=" + (externalDebited && !domainCommitted) + ", domainCommitted=" + domainCommitted + ", error=" + exception);
        }
    }

    private static void ReturnInboundLease(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try { RequireHostAuthority(); var snapshot = runtimeStateProvider!.Current!; var lease = snapshot.Leases.Single(x => x.LeaseId == selectedLeaseId); var condition = ReadMinimumVehicleCondition(snapshot, lease.AssetIds); var result = runtimeStateProvider.ReturnLocalLease("lease-return:" + correlation, lease.LeaseId, condition, runtimeRoleDetector!, new UnityLeaseReturnGuard(LeaseReturnTrackRules(snapshot)), new UnityExistingVehicleOwnershipAdapter()); if (result.State != LeaseActionState.Succeeded) throw new InvalidOperationException(result.ResultCode); if (lease.Payer?.Kind == AccountKind.Player && result.Amount > 0) hostWallet.Credit(result.Amount); if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Lease return could not be staged in SaveGameData."); status = "Lease returned; deposit refund=" + result.Amount + ", debt=" + lease.OutstandingDebt + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=lease-return] lease=" + lease.LeaseId + ", refund=" + result.Amount + ", debt=" + lease.OutstandingDebt + ", authoritativeCondition=" + condition); }
        catch (Exception exception) { status = "Lease return refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=lease-return-refused] " + exception); }
    }

    private static decimal ReadMinimumVehicleCondition(VehicleAcquisitionSnapshot snapshot, IReadOnlyList<string> assetIds)
    {
        if (assetIds == null || assetIds.Count == 0) throw new InvalidOperationException("At least one vehicle is required for condition observation.");
        var reader = new UnityVehicleConditionReader(snapshot);
        return assetIds.Select(reader.Read).Min();
    }

    private static void PurchaseInboundLease(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        LeaseContract? lease = null; long externalDebit = 0; var externalDebited = false;
        try
        {
            RequireHostAuthority(); TrySynchronizeHostWallet(entry, "before-lease-purchase"); lease = runtimeStateProvider!.Current!.Leases.Single(x => x.LeaseId == selectedLeaseId);
            externalDebit = CalculateLeasePurchaseDebit(lease);
            if (lease.Payer?.Kind == AccountKind.Player && externalDebit > 0)
            {
                if (!hostWallet.TryDebit(externalDebit)) throw new InvalidOperationException("The authoritative personal wallet has insufficient funds.");
                externalDebited = true;
            }
            var result = runtimeStateProvider.PurchaseLocalLease("lease-purchase:" + correlation, lease.LeaseId, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter());
            if (result.State == LeaseActionState.Rejected) throw new InvalidOperationException(result.ResultCode);
            if (result.Amount != externalDebit && lease.Payer?.Kind == AccountKind.Player) throw new InvalidOperationException("Lease purchase debit changed during the authoritative transaction.");
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Lease purchase could not be staged in SaveGameData.");
            status = "Lease purchase: " + result.State + " / " + result.ResultCode + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=lease-purchase] lease=" + lease.LeaseId + ", amount=" + result.Amount + ", state=" + result.State + ", result=" + result.ResultCode + ", externalDebited=" + externalDebited);
        }
        catch (Exception exception)
        {
            var domainCommitted = lease != null && (lease.State == LeaseState.PurchasePending || lease.State == LeaseState.Purchased);
            var refunded = externalDebited && !domainCommitted;
            if (refunded) hostWallet.Credit(externalDebit);
            status = domainCommitted ? "Lease purchase committed but persistence requires recovery; do not retry payment." : "Lease purchase refused: " + exception.Message;
            entry.Logger.Error("[correlation=" + correlation + "] [event=lease-purchase-failed] externalDebited=" + externalDebited + ", externalRefunded=" + refunded + ", domainCommitted=" + domainCommitted + ", error=" + exception);
        }
    }

    private static long CalculateLeasePurchaseDebit(LeaseContract lease)
    {
        if (lease.PurchaseOptionPrice == null) throw new InvalidOperationException("Purchase option is unavailable.");
        var total = checked(lease.PurchaseOptionPrice.Value + lease.OutstandingDebt);
        return total - Math.Min(lease.HeldDeposit, total);
    }

    private static void ReconcileInboundLeases(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); var results = runtimeStateProvider!.ReconcilePendingLeasePurchases(runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter()); SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance); status = "Lease recovery processed: " + results.Count + "."; foreach (var result in results) entry.Logger.Log("[correlation=" + correlation + "] [event=lease-reconcile] command=" + result.CommandId + ", state=" + result.State + ", result=" + result.ResultCode); } catch (Exception exception) { status = "Lease recovery refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=lease-reconcile-failed] " + exception); }
    }

    private static void DrawOutboundLeasing(UnityModManager.ModEntry entry, VehicleAcquisitionSnapshot snapshot)
    {
        GUILayout.Space(8f); GUILayout.Label("Outbound leasing (declared off-scene simulation; no AI driver)");
        if (!runtimeSettings.EnableOutboundLeasing) { GUILayout.Label("Outbound leasing is disabled by runtime settings."); return; }
        GUILayout.Label("The economic registry reserves the asset. This incremental adapter does not drive, despawn or teleport the visible vehicle.");
        GUILayout.Label("Rent | interval | duration | early recall fee | condition:");
        GUILayout.BeginHorizontal(); outboundLeaseRent = GUILayout.TextField(outboundLeaseRent, 12); outboundLeaseInterval = GUILayout.TextField(outboundLeaseInterval, 8); outboundLeaseDuration = GUILayout.TextField(outboundLeaseDuration, 8); outboundLeaseRecallFee = GUILayout.TextField(outboundLeaseRecallFee, 12); outboundLeaseCondition = GUILayout.TextField(outboundLeaseCondition, 8); GUILayout.EndHorizontal();
        GUILayout.Label("Declared destination | deterministic return location:");
        GUILayout.BeginHorizontal(); outboundLeaseDestination = GUILayout.TextField(outboundLeaseDestination, 32); outboundLeaseReturnLocation = GUILayout.TextField(outboundLeaseReturnLocation, 32); GUILayout.EndHorizontal();
        if (!string.IsNullOrWhiteSpace(selectedFleetAssetId) && GUILayout.Button("Publish selected idle asset or its complete bundle for outbound lease")) PublishOutboundLease(entry);
        foreach (var contract in snapshot.OutboundLeases.Where(x => x.State != OutboundLeaseState.Returned && x.State != OutboundLeaseState.Cancelled).Take(50).ToArray())
        {
            var marker = contract.ContractId == selectedOutboundLeaseId ? "> " : "  ";
            if (GUILayout.Button(marker + contract.ContractId + " | " + contract.State + " | assets=" + contract.AssetIds.Count + " | rent=" + contract.RentAmount + " | return=" + contract.ReturnLocation)) selectedOutboundLeaseId = contract.ContractId;
        }
        if (string.IsNullOrWhiteSpace(selectedOutboundLeaseId)) return;
        var selected = snapshot.OutboundLeases.Single(x => x.ContractId == selectedOutboundLeaseId);
        if (selected.State == OutboundLeaseState.Offered && GUILayout.Button("Activate selected outbound lease")) ActivateOutboundLease(entry);
        if ((selected.State == OutboundLeaseState.Active || selected.State == OutboundLeaseState.ReturnDue) && GUILayout.Button("Record third-party return now")) ReturnOutboundLease(entry, false);
        if ((selected.State == OutboundLeaseState.Offered || selected.State == OutboundLeaseState.Active || selected.State == OutboundLeaseState.ReturnDue) && GUILayout.Button("Recall/cancel selected outbound lease")) ReturnOutboundLease(entry, true);
        var pending = snapshot.OutboundLeaseActions.Count(x => x.State == OutboundLeaseActionState.ReconcileRequired);
        if (pending > 0 && GUILayout.Button("Reconcile pending outbound lease transitions (" + pending + ")")) ReconcileOutboundLeases(entry);
    }

    private static IReadOnlyList<string> SelectedOutboundAssetIds(VehicleAcquisitionSnapshot snapshot)
    {
        var selected = selectedFleetAssetId ?? throw new InvalidOperationException("Select one owned fleet asset first.");
        var bundle = snapshot.Assets.Bundles.SingleOrDefault(x => x.ComponentAssetIds.Contains(selected));
        return bundle == null ? new[] { selected } : bundle.ComponentAssetIds.ToArray();
    }

    private static void PublishOutboundLease(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority(); if (!long.TryParse(outboundLeaseRent, out var rent) || !long.TryParse(outboundLeaseInterval, out var interval) || !long.TryParse(outboundLeaseDuration, out var duration) || !long.TryParse(outboundLeaseRecallFee, out var recallFee) || !decimal.TryParse(outboundLeaseCondition, out var condition)) throw new InvalidOperationException("Outbound lease terms are invalid.");
            var contract = runtimeStateProvider!.PublishLocalOutboundLease("outbound-publish:" + correlation, "outbound:" + correlation, SelectedOutboundAssetIds(runtimeStateProvider.Current!), rent, interval, duration, recallFee, condition, outboundLeaseDestination, outboundLeaseReturnLocation, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new DeclaredOffSceneLeaseSimulationPort()); selectedOutboundLeaseId = contract.ContractId;
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Outbound lease publication could not be staged in SaveGameData.");
            status = "Outbound lease published; asset reserved for explicit off-scene simulation."; entry.Logger.Log("[correlation=" + correlation + "] [event=outbound-lease-published] contract=" + contract.ContractId + ", owner=" + contract.Owner.Key + ", assets=" + string.Join(",", contract.AssetIds) + ", rent=" + contract.RentAmount + ", interval=" + contract.RentIntervalTicks + ", duration=" + contract.DurationTicks + ", return=" + contract.ReturnLocation + ", simulation=declared-off-scene");
        }
        catch (Exception exception) { status = "Outbound lease publication refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=outbound-lease-publish-refused] " + exception); }
    }

    private static void ActivateOutboundLease(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); var result = runtimeStateProvider!.ActivateLocalOutboundLease("outbound-activate:" + correlation, selectedOutboundLeaseId!, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new DeclaredOffSceneLeaseSimulationPort()); if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Outbound lease activation could not be staged in SaveGameData."); status = "Outbound lease activation: " + result.State + " / " + result.ResultCode + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=outbound-lease-activated] contract=" + result.ContractId + ", state=" + result.State + ", result=" + result.ResultCode + ", simulation=declared-off-scene"); } catch (Exception exception) { status = "Outbound lease activation refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=outbound-lease-activate-refused] " + exception); }
    }

    private static void ReturnOutboundLease(UnityModManager.ModEntry entry, bool recall)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); if (!decimal.TryParse(outboundLeaseCondition, out var condition)) throw new InvalidOperationException("Outbound return condition is invalid."); var result = recall ? runtimeStateProvider!.RecallLocalOutboundLease("outbound-recall:" + correlation, selectedOutboundLeaseId!, condition, outboundLeaseReturnLocation, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new DeclaredOffSceneLeaseSimulationPort()) : runtimeStateProvider!.ReturnLocalOutboundLease("outbound-return:" + correlation, selectedOutboundLeaseId!, condition, outboundLeaseReturnLocation, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new DeclaredOffSceneLeaseSimulationPort()); if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Outbound lease return could not be staged in SaveGameData."); status = "Outbound lease " + (recall ? "recall" : "return") + ": " + result.State + " / " + result.ResultCode + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=outbound-lease-" + (recall ? "recalled" : "returned") + "] contract=" + result.ContractId + ", fee=" + result.Amount + ", state=" + result.State + ", result=" + result.ResultCode + ", condition=" + condition + ", return=" + outboundLeaseReturnLocation); } catch (Exception exception) { status = "Outbound lease return refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=outbound-lease-return-refused] " + exception); }
    }

    private static void ReconcileOutboundLeases(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); var results = runtimeStateProvider!.ReconcilePendingOutboundLeases(runtimeRoleDetector!, new UnityAssetReleaseGuard(), new DeclaredOffSceneLeaseSimulationPort()); SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance); status = "Outbound lease recovery processed: " + results.Count + "."; foreach (var result in results) entry.Logger.Log("[correlation=" + correlation + "] [event=outbound-lease-reconcile] command=" + result.CommandId + ", contract=" + result.ContractId + ", state=" + result.State + ", result=" + result.ResultCode); } catch (Exception exception) { status = "Outbound lease recovery refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=outbound-lease-reconcile-failed] " + exception); }
    }

    private static void DrawFleetManagement(UnityModManager.ModEntry entry, VehicleAcquisitionSnapshot snapshot, PlayerEconomicState player, CompanyState? company)
    {
        GUILayout.Space(8f);
        GUILayout.Label("Fleet management (owned locomotives and wagons)");
        if (!runtimeSettings.EnableFleetManagement)
        {
            GUILayout.Label("Fleet management is disabled by runtime settings.");
            return;
        }
        var owned = snapshot.Fleet.Where(f =>
        {
            var ownership = snapshot.Ownership.SingleOrDefault(x => x.AssetId == f.AssetId);
            return ownership != null && ((ownership.Owner.Kind == AssetOwnerKind.Player && ownership.Owner.OwnerId == player.PlayerId) ||
                (company != null && ownership.Owner.Kind == AssetOwnerKind.Company && ownership.Owner.OwnerId == company.CompanyId) ||
                (f.Operator != null && ((f.Operator.Kind == AssetOwnerKind.Player && f.Operator.OwnerId == player.PlayerId) || (company != null && f.Operator.Kind == AssetOwnerKind.Company && f.Operator.OwnerId == company.CompanyId))));
        }).OrderBy(x => x.Kind).ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        GUILayout.Label("Owned: " + owned.Length + " | locomotives: " + owned.Count(x => x.Kind == FleetVehicleKind.Locomotive) +
            " | freight wagons: " + owned.Count(x => x.Kind == FleetVehicleKind.FreightWagon) +
            " | passenger cars: " + owned.Count(x => x.Kind == FleetVehicleKind.PassengerCar));
        foreach (var unsettled in snapshot.OperatingCosts.Where(x => x.State == OperatingCostState.Open || x.ExternalSettlement == ExternalSettlementState.Pending || x.ExternalSettlement == ExternalSettlementState.Conflict).ToArray())
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Cost session " + unsettled.Action + " | " + unsettled.ResultCode)) selectedFleetAssetId = unsettled.AssetId;
            if ((unsettled.ExternalSettlement == ExternalSettlementState.Pending || unsettled.ExternalSettlement == ExternalSettlementState.Conflict) && GUILayout.Button("Recover reimbursement", GUILayout.Width(190f))) RecoverOperatingCostSettlement(entry, unsettled.SessionId);
            GUILayout.EndHorizontal();
        }
        var pendingResales = snapshot.Resales.Count(x => x.State == ResaleState.ReconcileRequired);
        if (pendingResales > 0 && GUILayout.Button("Reconcile pending vehicle sales (" + pendingResales + ")")) ExecutePendingResaleReconciliation(entry);
        fleetFilter = GUILayout.Toolbar(fleetFilter, new[] { "All", "Locos", "Freight", "Passenger" });
        var filtered = owned.Where(x => fleetFilter == 0 || (int)x.Kind == fleetFilter).Take(50).ToArray();
        if (selectedFleetAssetId != null && !owned.Any(x => x.AssetId == selectedFleetAssetId)) selectedFleetAssetId = null;
        foreach (var fleet in filtered)
        {
            var ownership = snapshot.Ownership.Single(x => x.AssetId == fleet.AssetId);
            var selected = fleet.AssetId == selectedFleetAssetId ? "> " : "  ";
            GUILayout.BeginHorizontal();
            var wasInBundleSelection = bundleSelection.Contains(fleet.AssetId);
            var isInBundleSelection = GUILayout.Toggle(wasInBundleSelection, "Bundle", GUILayout.Width(78f));
            if (isInBundleSelection != wasInBundleSelection)
            {
                if (isInBundleSelection) bundleSelection.Add(fleet.AssetId); else bundleSelection.Remove(fleet.AssetId);
            }
            if (GUILayout.Button(selected + fleet.DisplayName + " | " + fleet.Kind + " | " + fleet.OperationalState + " | owner " + ownership.Owner.Key))
            {
                selectedFleetAssetId = fleet.AssetId;
                fleetDisplayName = fleet.DisplayName;
                preparedResaleQuoteId = null;
            }
            GUILayout.EndHorizontal();
        }
        bundleSelection.RemoveWhere(id => !owned.Any(x => x.AssetId == id));
        GUILayout.BeginHorizontal();
        GUILayout.Label("Bundle selection: " + bundleSelection.Count);
        GUI.enabled = bundleSelection.Count >= 2;
        if (GUILayout.Button("Create persistent bundle")) CreateFleetBundle(entry);
        GUI.enabled = bundleSelection.Count > 0;
        if (GUILayout.Button("Clear bundle selection")) bundleSelection.Clear();
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        if (selectedFleetAssetId == null)
        {
            GUILayout.Label(owned.Length == 0 ? "Acquire a vehicle to populate the fleet." : "Select one owned vehicle to manage it.");
            return;
        }

        var selectedFleet = snapshot.Fleet.Single(x => x.AssetId == selectedFleetAssetId);
        var selectedOwner = snapshot.Ownership.Single(x => x.AssetId == selectedFleetAssetId).Owner;
        GUILayout.Label("Selected asset: " + selectedFleet.AssetId + " | operator: " + (selectedFleet.Operator?.Key ?? "not assigned") +
            " | location: " + (selectedFleet.LastKnownLocation ?? "unknown") + " | version: " + selectedFleet.Version);
        GUILayout.BeginHorizontal();
        fleetDisplayName = GUILayout.TextField(fleetDisplayName ?? "", 48);
        if (GUILayout.Button("Rename", GUILayout.Width(120f))) ExecuteFleetCommand(entry, FleetCommandAction.Rename, displayName: fleetDisplayName);
        GUILayout.EndHorizontal();
        GUILayout.Label("Operational state (manual validation state only):");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Available")) ExecuteFleetCommand(entry, FleetCommandAction.SetOperationalState, operationalState: FleetOperationalState.Available);
        if (GUILayout.Button("In service")) ExecuteFleetCommand(entry, FleetCommandAction.SetOperationalState, operationalState: FleetOperationalState.InService);
        if (GUILayout.Button("Maintenance")) ExecuteFleetCommand(entry, FleetCommandAction.SetOperationalState, operationalState: FleetOperationalState.Maintenance);
        if (GUILayout.Button("Stored")) ExecuteFleetCommand(entry, FleetCommandAction.SetOperationalState, operationalState: FleetOperationalState.Stored);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Assign operator: personal")) ExecuteFleetCommand(entry, FleetCommandAction.AssignOperator, target: AssetOwnerRef.Player(player.PlayerId));
        GUI.enabled = company != null;
        if (GUILayout.Button("Assign operator: company") && company != null) ExecuteFleetCommand(entry, FleetCommandAction.AssignOperator, target: AssetOwnerRef.Company(company.CompanyId));
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Clear operator assignment")) ExecuteFleetCommand(entry, FleetCommandAction.ClearOperator);
        if (selectedOwner.Kind == AssetOwnerKind.Player && company != null)
        {
            if (GUILayout.Button("Transfer ownership: personal → company"))
                ExecuteFleetCommand(entry, FleetCommandAction.TransferOwnership, target: AssetOwnerRef.Company(company.CompanyId));
        }
        else if (selectedOwner.Kind == AssetOwnerKind.Company)
        {
            if (GUILayout.Button("Transfer ownership: company → personal"))
                ExecuteFleetCommand(entry, FleetCommandAction.TransferOwnership, target: AssetOwnerRef.Player(player.PlayerId));
        }
        DrawVehicleResale(entry, snapshot, selectedFleet);
        DrawOperatingCosts(entry, snapshot, selectedFleet, company);
    }

    private static void DrawMissionAssignments(UnityModManager.ModEntry entry, VehicleAcquisitionSnapshot snapshot, bool hasCompany)
    {
        GUILayout.Space(8f); GUILayout.Label("Mission assignments and finance routing (manual validation)");
        if (!runtimeSettings.EnableMissionAssignments) { GUILayout.Label("Mission assignments are disabled by runtime settings."); return; }
        missionKind = GUILayout.Toolbar(missionKind, new[] { "Freight", "Passenger" }); missionId = GUILayout.TextField(missionId ?? "", 64); missionMaximumRevenue = GUILayout.TextField(missionMaximumRevenue ?? "", 16);
        missionForCompany = hasCompany && GUILayout.Toggle(missionForCompany, "Operator and beneficiary: company (off = independent player)");
        if (GUILayout.Button("Reserve selected fleet asset or its complete bundle for mission")) ReserveMissionAssignment(entry, missionForCompany && hasCompany);
        foreach (var assignment in snapshot.Assignments.Where(x => x.State != MissionAssignmentState.Cancelled).Take(50).ToArray())
        {
            var marker = assignment.AssignmentId == selectedAssignmentId ? "> " : "  "; if (GUILayout.Button(marker + assignment.MissionId + " | " + assignment.Kind + " | " + assignment.Operator.Key + " | " + assignment.State + " | revenue=" + assignment.ActualRevenue)) selectedAssignmentId = assignment.AssignmentId;
        }
        if (string.IsNullOrWhiteSpace(selectedAssignmentId)) return; var selected = snapshot.Assignments.Single(x => x.AssignmentId == selectedAssignmentId);
        if (selected.State == MissionAssignmentState.Reserved && GUILayout.Button("Start assignment and freeze current vanilla balance")) StartMissionAssignment(entry);
        if ((selected.State == MissionAssignmentState.Active || selected.State == MissionAssignmentState.CompletionPending) && GUILayout.Button("Observe vanilla completion and complete with full consist")) CompleteMissionAssignment(entry);
        if (selected.State != MissionAssignmentState.Completed && selected.State != MissionAssignmentState.Cancelled && GUILayout.Button("Cancel assignment and release consist")) CancelMissionAssignment(entry);
        if (selected.ExternalSettlement == ExternalSettlementState.Pending && GUILayout.Button("Recover company mission wallet settlement")) RecoverMissionSettlement(entry);
    }

    private static void ReserveMissionAssignment(UnityModManager.ModEntry entry, bool forCompany)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority(); if (string.IsNullOrWhiteSpace(selectedFleetAssetId)) throw new InvalidOperationException("Select one owned or leased fleet asset first."); if (!long.TryParse(missionMaximumRevenue, out var maximum) || maximum < 0) throw new InvalidOperationException("Maximum revenue must be non-negative.");
            var assetId = selectedFleetAssetId!; var snapshot = runtimeStateProvider!.Current!; var bundle = snapshot.Assets.Bundles.SingleOrDefault(x => x.ComponentAssetIds.Contains(assetId)); var ids = bundle?.ComponentAssetIds ?? new List<string> { assetId };
            var assignment = runtimeStateProvider.ReserveLocalAssignment("assignment-reserve:" + correlation, "assignment:" + correlation, missionId, missionKind == 0 ? MissionAssignmentKind.Freight : MissionAssignmentKind.Passenger, ids, forCompany, maximum, runtimeRoleDetector!, new UnityMissionLifecyclePort()); selectedAssignmentId = assignment.AssignmentId;
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Assignment could not be staged in SaveGameData."); status = "Mission consist reserved: " + assignment.AssignmentId + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=assignment-reserved] assignment=" + assignment.AssignmentId + ", mission=" + assignment.MissionId + ", kind=" + assignment.Kind + ", operator=" + assignment.Operator.Key + ", assets=" + string.Join(",", assignment.AssetIds) + ", maximum=" + assignment.MaximumExpectedRevenue);
        }
        catch (Exception exception) { status = "Mission reservation refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=assignment-reserve-refused] " + exception); }
    }

    private static void StartMissionAssignment(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); TrySynchronizeHostWallet(entry, "before-assignment-start"); var assignment = runtimeStateProvider!.StartLocalAssignment("assignment-start:" + correlation, selectedAssignmentId!, hostWallet.ReadBalance(), runtimeRoleDetector!, new UnityMissionLifecyclePort()); if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Assignment start could not be staged."); status = "Assignment active; complete the external mission, then observe it here."; entry.Logger.Log("[correlation=" + correlation + "] [event=assignment-started] assignment=" + assignment.AssignmentId + ", vanillaBefore=" + assignment.VanillaBalanceBefore); } catch (Exception exception) { status = "Assignment start refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=assignment-start-refused] " + exception); }
    }

    private static void CompleteMissionAssignment(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority(); var assignment = runtimeStateProvider!.Current!.Assignments.Single(x => x.AssignmentId == selectedAssignmentId); var assignmentResult = runtimeStateProvider.CompleteLocalAssignment("assignment-complete:" + correlation, assignment.AssignmentId, hostWallet.ReadBalance(), assignment.AssetIds, runtimeRoleDetector!, new UnityMissionLifecyclePort());
            if (assignmentResult.ExternalSettlement == ExternalSettlementState.Pending && assignmentResult.ActualRevenue > 0) { if (!hostWallet.TryDebit(assignmentResult.ActualRevenue)) throw new InvalidOperationException("Company revenue was routed internally but could not be removed from the vanilla host wallet."); runtimeStateProvider.MarkLocalMissionSettlement(assignmentResult.AssignmentId, hostWallet.ReadBalance(), runtimeRoleDetector!, new UnityMissionLifecyclePort()); runtimeStateProvider.SynchronizeLocalWallet("mission-wallet-settlement:" + correlation, hostWallet.ReadBalance(), "company-mission-settlement"); }
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Mission completion could not be staged in SaveGameData."); status = "Mission completed: revenue " + assignmentResult.ActualRevenue + " -> " + assignmentResult.Operator.Key + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=assignment-completed] assignment=" + assignmentResult.AssignmentId + ", mission=" + assignmentResult.MissionId + ", operator=" + assignmentResult.Operator.Key + ", revenue=" + assignmentResult.ActualRevenue + ", externalSettlement=" + assignmentResult.ExternalSettlement);
        }
        catch (Exception exception) { status = "Mission completion refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=assignment-complete-refused] " + exception); }
    }

    private static void RecoverMissionSettlement(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); var assignment = runtimeStateProvider!.Current!.Assignments.Single(x => x.AssignmentId == selectedAssignmentId); var observed = hostWallet.ReadBalance(); if (observed == assignment.VanillaBalanceAfter && assignment.ActualRevenue > 0 && !hostWallet.TryDebit(assignment.ActualRevenue)) throw new InvalidOperationException("Vanilla mission revenue cannot be removed."); var result = runtimeStateProvider.MarkLocalMissionSettlement(assignment.AssignmentId, hostWallet.ReadBalance(), runtimeRoleDetector!, new UnityMissionLifecyclePort()); if (result.ExternalSettlement == ExternalSettlementState.Applied) runtimeStateProvider.SynchronizeLocalWallet("mission-wallet-recovery:" + correlation, hostWallet.ReadBalance(), "company-mission-recovery"); SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance); status = "Mission settlement recovery: " + result.ExternalSettlement + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=assignment-settlement-recovery] assignment=" + result.AssignmentId + ", state=" + result.ExternalSettlement + ", observed=" + hostWallet.ReadBalance()); } catch (Exception exception) { status = "Mission settlement recovery refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=assignment-settlement-recovery-refused] " + exception); }
    }

    private static void CancelMissionAssignment(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); var result = runtimeStateProvider!.CancelLocalAssignment("assignment-cancel:" + correlation, selectedAssignmentId!, runtimeRoleDetector!, new UnityMissionLifecyclePort()); if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Assignment cancellation could not be staged."); status = "Assignment cancelled; consist released."; entry.Logger.Log("[correlation=" + correlation + "] [event=assignment-cancelled] assignment=" + result.AssignmentId + ", assets=" + string.Join(",", result.AssetIds)); } catch (Exception exception) { status = "Assignment cancellation refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=assignment-cancel-refused] " + exception); }
    }

    private static void DrawIndustrialEconomy(UnityModManager.ModEntry entry, VehicleAcquisitionSnapshot snapshot, bool hasCompany)
    {
        GUILayout.Space(8f);
        GUILayout.Label("Industrial contracts and production");
        if (!runtimeSettings.EnableIndustrialPilot) { GUILayout.Label("Industrial economy is disabled by runtime settings."); return; }
        GUILayout.Label("Facility | cargo | on-hand | capacity:");
        GUILayout.BeginHorizontal();
        industrialFacilityId = GUILayout.TextField(industrialFacilityId, 20);
        industrialCargoId = GUILayout.TextField(industrialCargoId, 20);
        industrialStockOnHand = GUILayout.TextField(industrialStockOnHand, 10);
        industrialStockCapacity = GUILayout.TextField(industrialStockCapacity, 10);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Configure origin stock")) ConfigureIndustrialStock(entry, industrialFacilityId, industrialCargoId);
        industrialDestinationId = GUILayout.TextField(industrialDestinationId, 20);
        if (GUILayout.Button("Configure destination stock")) ConfigureIndustrialStock(entry, industrialDestinationId, industrialCargoId);
        GUILayout.EndHorizontal();
        foreach (var stock in snapshot.IndustrialStocks.OrderBy(value => value.FacilityId).ThenBy(value => value.CargoId).Take(40).ToArray())
            GUILayout.Label(stock.FacilityId + " | " + stock.CargoId + " | on hand=" + stock.OnHand + "/" + stock.Capacity + " | outbound=" + stock.ReservedOutbound + " | inbound=" + stock.ReservedInbound);

        GUILayout.Label("Recipe | input | input/cycle | output | output/cycle | cadence | backlog:");
        GUILayout.BeginHorizontal();
        industrialRecipeId = GUILayout.TextField(industrialRecipeId, 20); industrialRecipeInputCargo = GUILayout.TextField(industrialRecipeInputCargo, 16);
        industrialRecipeInputQuantity = GUILayout.TextField(industrialRecipeInputQuantity, 8); industrialRecipeOutputCargo = GUILayout.TextField(industrialRecipeOutputCargo, 16);
        industrialRecipeOutputQuantity = GUILayout.TextField(industrialRecipeOutputQuantity, 8); industrialRecipeCadence = GUILayout.TextField(industrialRecipeCadence, 8);
        industrialRecipeBacklog = GUILayout.TextField(industrialRecipeBacklog, 8);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Configure recipe at origin facility")) ConfigureIndustrialRecipe(entry);
        if (GUILayout.Button("Advance selected recipe at current economic tick")) AdvanceIndustrialProduction(entry);
        GUILayout.EndHorizontal();
        foreach (var recipe in snapshot.IndustrialRecipes.OrderBy(value => value.RecipeId).Take(30).ToArray())
            GUILayout.Label(recipe.RecipeId + " | " + recipe.FacilityId + " | " + recipe.InputQuantity + " " + recipe.InputCargoId + " -> " + recipe.OutputQuantity + " " + recipe.OutputCargoId + " | pending=" + recipe.PendingCycles + " | completed=" + recipe.CompletedCycles);

        GUILayout.Label("Transport policy | target | batch | rewards | offer/preparation ticks | wagon requirement:");
        GUILayout.BeginHorizontal();
        industrialPolicyId = GUILayout.TextField(industrialPolicyId, 24); industrialDestinationTarget = GUILayout.TextField(industrialDestinationTarget, 8);
        industrialQuantity = GUILayout.TextField(industrialQuantity, 8); industrialBaseReward = GUILayout.TextField(industrialBaseReward, 10);
        industrialMaximumScarcityBonus = GUILayout.TextField(industrialMaximumScarcityBonus, 10); industrialOfferLifetime = GUILayout.TextField(industrialOfferLifetime, 8);
        industrialDeliveryDuration = GUILayout.TextField(industrialDeliveryDuration, 8);
        industrialPreparationTicks = GUILayout.TextField(industrialPreparationTicks, 8); industrialPreparationPenalty = GUILayout.TextField(industrialPreparationPenalty, 8);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        industrialMinimumWagons = GUILayout.TextField(industrialMinimumWagons, 6); industrialMinimumCapacity = GUILayout.TextField(industrialMinimumCapacity, 8);
        industrialPolicyEnabled = GUILayout.Toggle(industrialPolicyEnabled, "Policy enabled");
        if (GUILayout.Button("Configure shortage-driven transport policy")) ConfigureIndustrialTransportPolicy(entry);
        if (GUILayout.Button("Publish selected policy now")) PublishIndustrialTransportNeed(entry);
        GUILayout.EndHorizontal();
        foreach (var policy in snapshot.IndustrialTransportPolicies.OrderBy(value => value.PolicyId).Take(30).ToArray())
            GUILayout.Label(policy.PolicyId + " | " + policy.OriginFacilityId + " -> " + policy.DestinationFacilityId + " | " + policy.CargoId + " | batch=" + policy.BatchQuantity + " | target=" + policy.DestinationTargetQuantity + " | enabled=" + policy.Enabled);
        industrialForCompany = hasCompany && GUILayout.Toggle(industrialForCompany, "Operator, beneficiary and optional penalty payer: company (off = independent player)");
        if (GUILayout.Button("Discover stations and configure a pilot chain for selected freight wagon(s)"))
            ConfigureDiscoveredIndustrialPilot(entry, industrialForCompany && hasCompany);
        foreach (var need in snapshot.IndustrialTransportNeeds.Where(value => value.State == IndustrialNeedState.Available).OrderBy(value => value.ExpiresTick).Take(30).ToArray())
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(need.NeedId + " | " + need.OriginFacilityId + " -> " + need.DestinationFacilityId + " | " + need.Quantity + " " + need.CargoId + " | reward=" + (need.BaseReward + need.ScarcityBonus) + " | expires=" + need.ExpiresTick);
            if (GUILayout.Button("Accept need")) AcceptIndustrialTransportNeed(entry, need, industrialForCompany && hasCompany);
            GUILayout.EndHorizontal();
        }

        GUILayout.Label("Origin | destination | cargo | quantity | base reward | scarcity bonus | preparation ticks | expiry penalty:");
        GUILayout.BeginHorizontal();
        industrialFacilityId = GUILayout.TextField(industrialFacilityId, 16); industrialDestinationId = GUILayout.TextField(industrialDestinationId, 16);
        industrialCargoId = GUILayout.TextField(industrialCargoId, 14); industrialQuantity = GUILayout.TextField(industrialQuantity, 8);
        industrialBaseReward = GUILayout.TextField(industrialBaseReward, 10); industrialScarcityBonus = GUILayout.TextField(industrialScarcityBonus, 10);
        industrialPreparationTicks = GUILayout.TextField(industrialPreparationTicks, 8); industrialPreparationPenalty = GUILayout.TextField(industrialPreparationPenalty, 8);
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Create stock-backed freight contract")) CreateIndustrialContract(entry, industrialForCompany && hasCompany);
        foreach (var contract in snapshot.IndustrialContracts.OrderByDescending(value => value.CreatedTick).Take(50).ToArray())
        {
            var marker = contract.ContractId == selectedIndustrialContractId ? "> " : "  ";
            if (GUILayout.Button(marker + contract.ContractId + " | " + contract.OriginFacilityId + " -> " + contract.DestinationFacilityId + " | " + contract.CargoId + " " + contract.DeliveredQuantity + "/" + contract.Quantity + " | " + contract.State + " | paid=" + contract.PaidAmount))
                selectedIndustrialContractId = contract.ContractId;
        }
        if (string.IsNullOrWhiteSpace(selectedIndustrialContractId)) return;
        var selected = snapshot.IndustrialContracts.SingleOrDefault(value => value.ContractId == selectedIndustrialContractId);
        if (selected == null) { selectedIndustrialContractId = null; return; }
        if (selected.State == IndustrialContractState.Offered && GUILayout.Button("Accept contract and reserve cargo")) AcceptIndustrialContract(entry, selected, industrialForCompany && hasCompany);
        if (selected.State == IndustrialContractState.Reserved)
        {
            GUILayout.Label("Select freight wagons in Fleet (or mark several for the bundle), then assign them here.");
            if (GUILayout.Button("Assign selected operator wagons")) AssignIndustrialWagons(entry, selected, industrialForCompany && hasCompany);
            if (selected.AssignedWagons.Count > 0 && GUILayout.Button("Create zero-wage SelfShunt job and activate contract")) ActivateIndustrialContract(entry, selected);
            if (selected.PreparationExpiresTick > 0 && snapshot.LeaseClock.ActiveTick >= selected.PreparationExpiresTick && GUILayout.Button("Expire overdue preparation reservation")) ExpireIndustrialContract(entry, selected);
        }
        if (selected.State == IndustrialContractState.Active || selected.State == IndustrialContractState.DeliveryPending)
        {
            foreach (var manifest in selected.Manifests)
            {
                var wagon = snapshot.Fleet.SingleOrDefault(value => value.AssetId == manifest.AssetId);
                GUILayout.Label((wagon?.DisplayName ?? manifest.AssetId) + " | loaded=" + manifest.LoadedQuantity + " | unloaded=" + manifest.UnloadedQuantity);
            }
            if (GUILayout.Button("Reconcile physical cargo for every assigned wagon")) ReconcileIndustrialCargo(entry, selected);
            if (GUILayout.Button("Reconcile completed external job and aggregate delivery")) ReconcileIndustrialCompletedJob(entry, selected);
        }
        if (selected.State != IndustrialContractState.Completed && selected.State != IndustrialContractState.Cancelled && selected.State != IndustrialContractState.Expired && GUILayout.Button("Cancel contract (abandon any external job first)"))
            CancelIndustrialContract(entry, selected);
    }

    private static IndustrialEconomyEngine IndustrialEngine(VehicleAcquisitionSnapshot snapshot) =>
        new IndustrialEconomyEngine(snapshot, runtimeRoleDetector!, new UnityIndustrialExecutionPort(snapshot), new UnityCargoTransferObservationPort(snapshot), new UnityWagonCompatibilityPort(snapshot));

    private static void ConfigureDiscoveredIndustrialPilot(UnityModManager.ModEntry entry, bool forCompany)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            var snapshot = runtimeStateProvider!.Current!;
            var player = snapshot.Economy.Players.Single(value => value.PlayerId == runtimeStateProvider.LocalPlayerId);
            var operatorRef = ResolveIndustrialOperator(snapshot, player, forCompany);
            var ids = bundleSelection.Count > 0 ? bundleSelection.ToArray() : string.IsNullOrWhiteSpace(selectedFleetAssetId) ? Array.Empty<string>() : new[] { selectedFleetAssetId! };
            var result = UnityIndustrialPilotBootstrap.Configure(snapshot, IndustrialEngine(snapshot), ids, operatorRef, snapshot.LeaseClock.ActiveTick, "industrial-pilot:" + correlation);
            industrialPolicyId = result.PolicyId; industrialFacilityId = result.OriginFacilityId; industrialDestinationId = result.DestinationFacilityId;
            industrialCargoId = result.CargoId; industrialRecipeId = result.RecipeId; industrialQuantity = result.BatchQuantity.ToString();
            StageIndustrialSave();
            status = "Pilot chain configured: " + result.OriginFacilityId + " -> " + result.DestinationFacilityId + " / " + result.CargoId + ". Accept the available need to continue.";
            entry.Logger.Log("[correlation=" + correlation + "] [event=industrial-pilot-configured] policy=" + result.PolicyId + ", operator=" + operatorRef.Key + ", assets=" + string.Join(",", ids));
        }
        catch (Exception exception) { status = "Industrial pilot configuration refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=industrial-pilot-refused] " + exception); }
    }

    private static void ConfigureIndustrialStock(UnityModManager.ModEntry entry, string facilityId, string cargoId)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            if (!decimal.TryParse(industrialStockOnHand, out var onHand) || !decimal.TryParse(industrialStockCapacity, out var capacity)) throw new InvalidOperationException("Stock values are invalid.");
            var stock = IndustrialEngine(runtimeStateProvider!.Current!).ConfigureStock("industrial-stock:" + correlation, facilityId, cargoId, onHand, capacity);
            StageIndustrialSave(); status = "Industrial stock configured: " + stock.Key + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=industrial-stock-configured] stock=" + stock.Key + ", onHand=" + stock.OnHand + ", capacity=" + stock.Capacity);
        }
        catch (Exception exception) { status = "Industrial stock refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=industrial-stock-refused] " + exception); }
    }

    private static void ConfigureIndustrialRecipe(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            if (!decimal.TryParse(industrialRecipeInputQuantity, out var input) || !decimal.TryParse(industrialRecipeOutputQuantity, out var output) || !long.TryParse(industrialRecipeCadence, out var cadence) || !int.TryParse(industrialRecipeBacklog, out var backlog)) throw new InvalidOperationException("Recipe values are invalid.");
            var recipe = IndustrialEngine(runtimeStateProvider!.Current!).ConfigureRecipe("industrial-recipe:" + correlation, industrialRecipeId, industrialFacilityId,
                industrialRecipeInputCargo, input, industrialRecipeOutputCargo, output, cadence, backlog);
            StageIndustrialSave(); status = "Industrial recipe configured: " + recipe.RecipeId + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=industrial-recipe-configured] recipe=" + recipe.RecipeId + ", facility=" + recipe.FacilityId);
        }
        catch (Exception exception) { status = "Industrial recipe refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=industrial-recipe-refused] " + exception); }
    }

    private static void AdvanceIndustrialProduction(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try { RequireHostAuthority(); var tick = runtimeStateProvider!.Current!.LeaseClock.ActiveTick; var cycles = IndustrialEngine(runtimeStateProvider.Current).AdvanceProduction("industrial-production:" + correlation, industrialRecipeId, tick); StageIndustrialSave(); status = "Production advanced: " + cycles + " completed cycle(s)."; entry.Logger.Log("[correlation=" + correlation + "] [event=industrial-production-advanced] recipe=" + industrialRecipeId + ", tick=" + tick + ", cycles=" + cycles); }
        catch (Exception exception) { status = "Production advance refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=industrial-production-refused] " + exception); }
    }

    private static void ConfigureIndustrialTransportPolicy(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            if (!decimal.TryParse(industrialQuantity, out var batch) || !decimal.TryParse(industrialDestinationTarget, out var target) ||
                !long.TryParse(industrialBaseReward, out var reward) || !long.TryParse(industrialMaximumScarcityBonus, out var maximumBonus) ||
                !long.TryParse(industrialOfferLifetime, out var lifetime) || !long.TryParse(industrialDeliveryDuration, out var deliveryDuration) || !long.TryParse(industrialPreparationTicks, out var preparation) ||
                !long.TryParse(industrialPreparationPenalty, out var penalty) || !int.TryParse(industrialMinimumWagons, out var minimumWagons) ||
                !decimal.TryParse(industrialMinimumCapacity, out var minimumCapacity)) throw new InvalidOperationException("Transport policy values are invalid.");
            var policy = IndustrialEngine(runtimeStateProvider!.Current!).ConfigureTransportPolicy("industrial-policy:" + correlation, industrialPolicyId,
                industrialFacilityId, industrialDestinationId, industrialCargoId, batch, target, reward, maximumBonus, lifetime, preparation, penalty,
                new WagonRequirement { CargoId = industrialCargoId, MinimumWagonCount = minimumWagons, MinimumTotalCapacity = minimumCapacity }, industrialPolicyEnabled, deliveryDuration);
            StageIndustrialSave(); status = "Industrial transport policy configured: " + policy.PolicyId + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=industrial-policy-configured] policy=" + policy.PolicyId + ", enabled=" + policy.Enabled);
        }
        catch (Exception exception) { status = "Industrial policy refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=industrial-policy-refused] " + exception); }
    }

    private static void PublishIndustrialTransportNeed(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority(); var snapshot = runtimeStateProvider!.Current!;
            var need = IndustrialEngine(snapshot).PublishTransportNeed("industrial-need-publish:" + correlation, industrialPolicyId, snapshot.LeaseClock.ActiveTick);
            StageIndustrialSave(); status = need == null ? "No transport need is currently required." : "Transport need available: " + need.NeedId + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=industrial-need-published] policy=" + industrialPolicyId + ", need=" + (need?.NeedId ?? "none"));
        }
        catch (Exception exception) { status = "Transport need publication refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=industrial-need-publish-refused] " + exception); }
    }

    private static void AcceptIndustrialTransportNeed(UnityModManager.ModEntry entry, IndustrialTransportNeed need, bool forCompany)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority(); var snapshot = runtimeStateProvider!.Current!; var player = snapshot.Economy.Players.Single(value => value.PlayerId == runtimeStateProvider.LocalPlayerId);
            var beneficiary = forCompany ? AccountRef.Company(player.CompanyId ?? throw new InvalidOperationException("Company membership is required.")) : AccountRef.Player(player.PlayerId);
            var payer = need.PreparationPenalty > 0 ? beneficiary : null;
            var contract = IndustrialEngine(snapshot).AcceptTransportNeed("industrial-need-accept:" + correlation, need.NeedId, need.Version, beneficiary, payer, snapshot.LeaseClock.ActiveTick);
            selectedIndustrialContractId = contract.ContractId; StageIndustrialSave(); status = "Transport need accepted and cargo reserved: " + contract.ContractId + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=industrial-need-accepted] need=" + need.NeedId + ", contract=" + contract.ContractId + ", beneficiary=" + beneficiary.Key);
        }
        catch (Exception exception) { status = "Transport need acceptance refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=industrial-need-accept-refused] " + exception); }
    }

    private static void CreateIndustrialContract(UnityModManager.ModEntry entry, bool forCompany)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            if (!decimal.TryParse(industrialQuantity, out var quantity) || !long.TryParse(industrialBaseReward, out var reward) || !long.TryParse(industrialScarcityBonus, out var bonus) || !long.TryParse(industrialPreparationPenalty, out var penalty)) throw new InvalidOperationException("Contract values are invalid.");
            var snapshot = runtimeStateProvider!.Current!; var player = snapshot.Economy.Players.Single(value => value.PlayerId == runtimeStateProvider.LocalPlayerId);
            var beneficiary = forCompany ? AccountRef.Company(player.CompanyId ?? throw new InvalidOperationException("Company membership is required.")) : AccountRef.Player(player.PlayerId);
            var contract = IndustrialEngine(snapshot).CreateTransportOffer("BDVM-CT-" + correlation, industrialFacilityId, industrialDestinationId, industrialCargoId, quantity,
                beneficiary, reward, bonus, snapshot.LeaseClock.ActiveTick, 0, new WagonRequirement { CargoId = industrialCargoId, MinimumWagonCount = 1, MinimumTotalCapacity = quantity }, penalty);
            selectedIndustrialContractId = contract.ContractId; StageIndustrialSave(); status = "Industrial contract created: " + contract.ContractId + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=industrial-contract-created] contract=" + contract.ContractId + ", origin=" + contract.OriginFacilityId + ", destination=" + contract.DestinationFacilityId + ", cargo=" + contract.CargoId + ", quantity=" + contract.Quantity + ", beneficiary=" + contract.Beneficiary.Key);
        }
        catch (Exception exception) { status = "Industrial contract creation refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=industrial-contract-create-refused] " + exception); }
    }

    private static void AcceptIndustrialContract(UnityModManager.ModEntry entry, IndustrialContract contract, bool forCompany)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority(); if (!long.TryParse(industrialPreparationTicks, out var duration)) throw new InvalidOperationException("Preparation duration is invalid.");
            var snapshot = runtimeStateProvider!.Current!; var player = snapshot.Economy.Players.Single(value => value.PlayerId == runtimeStateProvider.LocalPlayerId);
            var payer = contract.PreparationPenalty <= 0 ? null : forCompany ? AccountRef.Company(player.CompanyId ?? throw new InvalidOperationException("Company membership is required.")) : AccountRef.Player(player.PlayerId);
            var result = IndustrialEngine(snapshot).Accept("industrial-accept:" + correlation, contract.ContractId, contract.Version, snapshot.LeaseClock.ActiveTick, duration, payer);
            StageIndustrialSave(); status = "Industrial contract accepted; cargo reserved until tick " + result.PreparationExpiresTick + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=industrial-contract-accepted] contract=" + result.ContractId + ", expires=" + result.PreparationExpiresTick);
        }
        catch (Exception exception) { status = "Industrial acceptance refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=industrial-accept-refused] " + exception); }
    }

    private static void AssignIndustrialWagons(UnityModManager.ModEntry entry, IndustrialContract contract, bool forCompany)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority(); var snapshot = runtimeStateProvider!.Current!; var player = snapshot.Economy.Players.Single(value => value.PlayerId == runtimeStateProvider.LocalPlayerId);
            var ids = bundleSelection.Count > 0 ? bundleSelection.ToArray() : string.IsNullOrWhiteSpace(selectedFleetAssetId) ? Array.Empty<string>() : new[] { selectedFleetAssetId! };
            var operatorRef = forCompany ? AssetOwnerRef.Company(player.CompanyId ?? throw new InvalidOperationException("Company membership is required.")) : AssetOwnerRef.Player(player.PlayerId);
            var result = IndustrialEngine(snapshot).AssignWagons("industrial-assign:" + correlation, player.PlayerId, contract.ContractId, contract.Version, operatorRef, ids, snapshot.LeaseClock.ActiveTick);
            StageIndustrialSave(); status = "Assigned " + result.AssignedWagons.Count + " compatible wagon(s).";
            entry.Logger.Log("[correlation=" + correlation + "] [event=industrial-wagons-assigned] contract=" + result.ContractId + ", operator=" + operatorRef.Key + ", assets=" + string.Join(",", ids));
        }
        catch (Exception exception) { status = "Industrial wagon assignment refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=industrial-wagons-refused] " + exception); }
    }

    private static void ActivateIndustrialContract(UnityModManager.ModEntry entry, IndustrialContract contract)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority(); EnsureSelfShuntIndustrialLifecycle(entry);
            var snapshot = runtimeStateProvider!.Current!;
            PreflightIndustrialActivation(snapshot, contract, snapshot.LeaseClock.ActiveTick);
            var created = UnityIndustrialJobAdapter.CreateForContract(snapshot, contract, selfShuntIndustrialLifecycle ?? throw new InvalidOperationException("SelfShunt industrial lifecycle is unavailable."));
            var result = IndustrialEngine(snapshot).Activate("industrial-activate:" + correlation, contract.ContractId, snapshot.LeaseClock.ActiveTick);
            StageIndustrialSave(); status = created + " Contract state: " + result.State + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=industrial-contract-activated] contract=" + result.ContractId + ", job=zero-wage-selfshunt, state=" + result.State);
        }
        catch (Exception exception) { status = "Industrial activation refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=industrial-contract-activate-refused] " + exception); }
    }

    private static void PreflightIndustrialActivation(VehicleAcquisitionSnapshot snapshot, IndustrialContract contract, long tick)
    {
        var clone = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(snapshot), snapshot.CheckpointId);
        new IndustrialEconomyEngine(clone, runtimeRoleDetector!, new DisabledIndustrialExecutionPort())
            .Activate("industrial-activation-preflight:" + contract.ContractId + ":" + tick, contract.ContractId, tick);
    }
    private static void ExpireIndustrialContract(UnityModManager.ModEntry entry, IndustrialContract contract) => MutateIndustrialContract(entry, "expire", contract,
        (engine, tick) => engine.ExpirePreparation("industrial-expire:" + Guid.NewGuid().ToString("N"), contract.ContractId, tick));
    private static void CancelIndustrialContract(UnityModManager.ModEntry entry, IndustrialContract contract)
    {
        MutateIndustrialContract(entry, "cancel", contract, (engine, _) =>
        {
            UnityIndustrialJobAdapter.RequireCancellationObserved(contract);
            return engine.Cancel("industrial-cancel:" + Guid.NewGuid().ToString("N"), contract.ContractId);
        });
    }

    private static void MutateIndustrialContract(UnityModManager.ModEntry entry, string operation, IndustrialContract contract, Func<IndustrialEconomyEngine, long, IndustrialContract> action)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try { RequireHostAuthority(); var snapshot = runtimeStateProvider!.Current!; var result = action(IndustrialEngine(snapshot), snapshot.LeaseClock.ActiveTick); StageIndustrialSave(); status = "Industrial contract " + operation + ": " + result.State + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=industrial-contract-" + operation + "] contract=" + result.ContractId + ", state=" + result.State); }
        catch (Exception exception) { status = "Industrial " + operation + " refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=industrial-contract-" + operation + "-refused] " + exception); }
    }

    private static void ReconcileIndustrialCargo(UnityModManager.ModEntry entry, IndustrialContract contract)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority(); var snapshot = runtimeStateProvider!.Current!;
            ReconcileIndustrialCargoState(snapshot, contract, "industrial-cargo:" + correlation);
            StageIndustrialSave(); status = "Physical cargo reconciled for " + contract.Manifests.Count + " wagon(s); delivered=" + contract.DeliveredQuantity + "/" + contract.Quantity + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=industrial-cargo-reconciled] contract=" + contract.ContractId + ", delivered=" + contract.DeliveredQuantity);
        }
        catch (Exception exception) { status = "Industrial cargo reconciliation refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=industrial-cargo-reconcile-refused] " + exception); }
    }

    private static void ReconcileIndustrialCompletedJob(UnityModManager.ModEntry entry, IndustrialContract contract)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try { RequireHostAuthority(); var snapshot = runtimeStateProvider!.Current!; var result = ReconcileIndustrialCargoState(snapshot, contract, "industrial-job:" + correlation); if (new UnityIndustrialExecutionPort(snapshot).InspectDelivery("industrial-job:" + correlation, result.ContractId, result.Quantity) != WorldOwnershipOutcome.Applied) throw new InvalidOperationException("The exact external job and assigned consist are not authoritatively completed."); if (result.State != IndustrialContractState.Completed) throw new InvalidOperationException("Physical wagon unloading is incomplete."); StageIndustrialSave(); status = "External job reconciliation: " + result.State + "; delivered=" + result.DeliveredQuantity + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=industrial-delivery-reconciled] contract=" + result.ContractId + ", state=" + result.State + ", delivered=" + result.DeliveredQuantity); }
        catch (Exception exception) { status = "External job reconciliation refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=industrial-delivery-reconcile-refused] " + exception); }
    }

    private static IndustrialContract ReconcileIndustrialCargoState(VehicleAcquisitionSnapshot snapshot, IndustrialContract contract, string operationPrefix)
    {
        var engine = IndustrialEngine(snapshot); var tick = snapshot.LeaseClock.ActiveTick;
        foreach (var manifest in contract.Manifests.ToArray())
        {
            var car = UnityRollingStockResolver.Resolve(snapshot, manifest.AssetId) ?? throw new InvalidOperationException("Assigned wagon is not physically present: " + manifest.AssetId);
            var observed = (decimal)Math.Max(0f, car.LoadedCargoAmount);
            if (observed > manifest.LoadedQuantity + 0.02m) engine.RecordLoading(operationPrefix + ":load:" + manifest.AssetId + ":" + observed, contract.ContractId, manifest.AssetId, observed, tick);
            else
            {
                var unloaded = Math.Max(0m, manifest.LoadedQuantity - observed);
                if (unloaded > manifest.UnloadedQuantity + 0.02m) engine.RecordUnloading(operationPrefix + ":unload:" + manifest.AssetId + ":" + unloaded, contract.ContractId, manifest.AssetId, unloaded, tick);
            }
        }
        return contract;
    }

    private static void StageIndustrialSave()
    {
        IndustrialEconomyValidation.Validate(runtimeStateProvider!.Current!);
        if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Industrial state could not be staged in SaveGameData.");
    }

    private static void DrawTriageAssistance(UnityModManager.ModEntry entry, VehicleAcquisitionSnapshot snapshot)
    {
        GUILayout.Space(8f); GUILayout.Label("Triage assistance (planning only; no AI driver)");
        if (!runtimeSettings.EnableTriageAssistance) { GUILayout.Label("Triage assistance is disabled by runtime settings."); return; }
        GUILayout.Label("SelfShunt 1.0.0 exposes job generation and Multiplayer job packets, but no proven public logistics-command hook. Execution therefore remains fail-closed.");
        triageTracks = GUILayout.TextField(triageTracks, 128);
        var assignment = snapshot.Assignments.SingleOrDefault(x => x.AssignmentId == selectedAssignmentId);
        GUI.enabled = assignment != null && (assignment.State == MissionAssignmentState.Reserved || assignment.State == MissionAssignmentState.Active);
        if (GUILayout.Button("Create planning-only yard sequence for selected assignment"))
        {
            var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); var tracks = triageTracks.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray(); var plan = runtimeStateProvider!.CreateLocalTriagePlan("triage-plan:" + correlation, "triage:" + correlation, assignment!.AssignmentId, tracks, runtimeRoleDetector!); selectedTriagePlanId = plan.PlanId; if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Triage plan could not be staged in SaveGameData."); status = "Triage plan created: " + plan.PlanId + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=triage-plan-created] plan=" + plan.PlanId + ", assignment=" + plan.AssignmentId + ", level=" + plan.Level + ", tracks=" + string.Join(",", plan.OrderedTrackIds) + ", aiDriver=false"); } catch (Exception exception) { status = "Triage plan refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=triage-plan-refused] " + exception); }
        }
        GUI.enabled = true;
        foreach (var plan in snapshot.TriageAssistance.Plans.Take(30).ToArray()) if (GUILayout.Button((plan.PlanId == selectedTriagePlanId ? "> " : "  ") + plan.PlanId + " | " + plan.Level + " | " + plan.State + " | assignment=" + plan.AssignmentId + " | tracks=" + string.Join(" > ", plan.OrderedTrackIds))) selectedTriagePlanId = plan.PlanId;
        var selected = snapshot.TriageAssistance.Plans.SingleOrDefault(x => x.PlanId == selectedTriagePlanId); if (selected != null && selected.State == TriagePlanState.Planned && GUILayout.Button("Cancel selected triage plan")) { var correlation = Guid.NewGuid().ToString("N"); try { var result = runtimeStateProvider!.CancelLocalTriagePlan("triage-cancel:" + correlation, selected.PlanId, runtimeRoleDetector!); SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance); status = "Triage plan cancelled."; entry.Logger.Log("[correlation=" + correlation + "] [event=triage-plan-cancelled] plan=" + result.PlanId); } catch (Exception exception) { status = "Triage cancellation refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=triage-plan-cancel-refused] " + exception); } }
    }

    private static void DrawPassengerEconomy(UnityModManager.ModEntry entry, VehicleAcquisitionSnapshot snapshot, bool hasCompany)
    {
        GUILayout.Space(8f); GUILayout.Label("Passenger economy (existing PassengerJobs observation; no second payout)");
        if (!runtimeSettings.EnablePassengerEconomy) { GUILayout.Label("Passenger economy is disabled by runtime settings."); return; }
        var passengerJobsMod = UnityModManager.FindMod("PassengerJobs");
        var passengerJobs = new PassengerJobsRuntimeBridge(passengerJobsMod?.Info?.Version, passengerJobsMod?.Assembly);
        GUILayout.Label("PassengerJobs bridge: " + passengerJobs.Status.State + " | version=" + passengerJobs.Status.ModVersion + " | " + passengerJobs.Status.Code);
        if (!passengerJobs.Status.IsAvailable) { GUILayout.Label("Passenger actions are unavailable until a compatible PassengerJobs runtime is loaded."); return; }
        GUILayout.Label("Route | origin | destination:"); GUILayout.BeginHorizontal(); passengerRouteId = GUILayout.TextField(passengerRouteId, 24); passengerOrigin = GUILayout.TextField(passengerOrigin, 20); passengerDestination = GUILayout.TextField(passengerDestination, 20); GUILayout.EndHorizontal();
        GUILayout.Label("Initial demand | maximum | growth/interval | desired frequency | fare/passenger | late penalty/tick:"); GUILayout.BeginHorizontal(); passengerDemand = GUILayout.TextField(passengerDemand, 8); passengerMaximumDemand = GUILayout.TextField(passengerMaximumDemand, 8); passengerDemandGrowth = GUILayout.TextField(passengerDemandGrowth, 8); passengerFrequency = GUILayout.TextField(passengerFrequency, 8); passengerFare = GUILayout.TextField(passengerFare, 10); passengerLatePenalty = GUILayout.TextField(passengerLatePenalty, 10); GUILayout.EndHorizontal();
        if (!snapshot.PassengerRoutes.Any(x => x.RouteId == passengerRouteId) && GUILayout.Button("Configure passenger route demand")) ConfigurePassengerRoute(entry);
        foreach (var route in snapshot.PassengerRoutes.Take(30).ToArray()) { GUILayout.Label(route.RouteId + " | " + route.OriginId + " -> " + route.DestinationId + " | demand=" + route.DemandUnits + "/" + route.MaximumDemandUnits + " | punctuality=" + (route.PunctualityBasisPoints / 100m) + "%"); if (GUILayout.Button("Refresh demand at current economic tick — " + route.RouteId)) RefreshPassengerDemand(entry, route.RouteId); }
        GUILayout.Label("Existing PassengerJobs job ID | capacity | scheduled journey ticks:"); GUILayout.BeginHorizontal(); passengerJobId = GUILayout.TextField(passengerJobId, 32); passengerCapacity = GUILayout.TextField(passengerCapacity, 8); passengerJourneyTicks = GUILayout.TextField(passengerJourneyTicks, 8); GUILayout.EndHorizontal();
        passengerForCompany = hasCompany && GUILayout.Toggle(passengerForCompany, "Operator and beneficiary: company (off = independent player)");
        if (snapshot.PassengerRoutes.Any(x => x.RouteId == passengerRouteId) && !string.IsNullOrWhiteSpace(selectedFleetAssetId) && GUILayout.Button("Reserve selected consist for existing passenger job")) ReservePassengerService(entry, passengerForCompany && hasCompany);
        foreach (var contract in snapshot.PassengerContracts.Where(x => x.State != PassengerContractState.Cancelled).Take(50).ToArray()) { var marker = contract.ContractId == selectedPassengerContractId ? "> " : "  "; if (GUILayout.Button(marker + contract.ContractId + " | " + contract.State + " | " + contract.BookedPassengers + "/" + contract.Capacity + " passengers | quote max=" + contract.MaximumQuotedRevenue + " | paid=" + contract.PaidRevenue)) selectedPassengerContractId = contract.ContractId; }
        if (string.IsNullOrWhiteSpace(selectedPassengerContractId)) return; var selected = snapshot.PassengerContracts.Single(x => x.ContractId == selectedPassengerContractId);
        if (selected.State == PassengerContractState.Reserved && GUILayout.Button("Start existing passenger job observation")) StartPassengerService(entry);
        if ((selected.State == PassengerContractState.Active || selected.State == PassengerContractState.CompletionPending) && GUILayout.Button("Observe PassengerJobs completion and settle once")) CompletePassengerService(entry);
        if (selected.State != PassengerContractState.Completed && selected.State != PassengerContractState.Cancelled && GUILayout.Button("Cancel passenger service and restore demand")) CancelPassengerService(entry);
    }

    private static void ConfigurePassengerRoute(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); if (!int.TryParse(passengerDemand, out var demand) || !int.TryParse(passengerMaximumDemand, out var maximum) || !int.TryParse(passengerDemandGrowth, out var growth) || !long.TryParse(passengerFrequency, out var frequency) || !long.TryParse(passengerFare, out var fare) || !long.TryParse(passengerLatePenalty, out var penalty)) throw new InvalidOperationException("Passenger route parameters are invalid."); var route = runtimeStateProvider!.ConfigureLocalPassengerRoute("passenger-route:" + correlation, passengerRouteId, passengerOrigin, passengerDestination, demand, maximum, growth, frequency, fare, penalty, runtimeRoleDetector!, new UnityMissionLifecyclePort()); if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Passenger route could not be staged in SaveGameData."); status = "Passenger route configured: " + route.RouteId + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=passenger-route-configured] route=" + route.RouteId + ", origin=" + route.OriginId + ", destination=" + route.DestinationId + ", demand=" + route.DemandUnits + ", maximum=" + route.MaximumDemandUnits + ", frequency=" + route.DesiredFrequencyTicks + ", fare=" + route.BaseFarePerPassenger + ", latePenalty=" + route.LatePenaltyPerTick); } catch (Exception exception) { status = "Passenger route refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=passenger-route-refused] " + exception); }
    }

    private static void RefreshPassengerDemand(UnityModManager.ModEntry entry, string routeId)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); var route = runtimeStateProvider!.RefreshLocalPassengerDemand("passenger-demand:" + correlation, routeId, runtimeRoleDetector!, new UnityMissionLifecyclePort()); SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance); status = "Passenger demand refreshed: " + route.DemandUnits + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=passenger-demand-refreshed] route=" + route.RouteId + ", demand=" + route.DemandUnits + ", tick=" + runtimeStateProvider.Current!.LeaseClock.ActiveTick); } catch (Exception exception) { status = "Passenger demand refresh refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=passenger-demand-refused] " + exception); }
    }

    private static void ReservePassengerService(UnityModManager.ModEntry entry, bool forCompany)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); RequirePassengerJobsJob(passengerJobId); if (!int.TryParse(passengerCapacity, out var capacity) || !long.TryParse(passengerJourneyTicks, out var journey)) throw new InvalidOperationException("Passenger capacity or journey duration is invalid."); var now = runtimeStateProvider!.Current!.LeaseClock.ActiveTick; var contract = runtimeStateProvider.ReserveLocalPassengerService("passenger-reserve:" + correlation, "passenger:" + correlation, passengerRouteId, passengerJobId, SelectedOutboundAssetIds(runtimeStateProvider.Current), forCompany, capacity, now, checked(now + journey), runtimeRoleDetector!, new UnityMissionLifecyclePort()); selectedPassengerContractId = contract.ContractId; if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Passenger service could not be staged in SaveGameData."); status = "Passenger service reserved: " + contract.BookedPassengers + " passenger(s)."; entry.Logger.Log("[correlation=" + correlation + "] [event=passenger-service-reserved] contract=" + contract.ContractId + ", job=" + contract.PassengerJobId + ", route=" + contract.RouteId + ", operator=" + contract.Operator.Key + ", assets=" + string.Join(",", contract.AssetIds) + ", booked=" + contract.BookedPassengers + ", capacity=" + contract.Capacity + ", quoteMax=" + contract.MaximumQuotedRevenue); } catch (Exception exception) { status = "Passenger service reservation refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=passenger-service-reserve-refused] " + exception); }
    }

    private static void RequirePassengerJobsJob(string jobId)
    {
        var passengerJobsMod = UnityModManager.FindMod("PassengerJobs");
        var bridge = new PassengerJobsRuntimeBridge(passengerJobsMod?.Info?.Version, passengerJobsMod?.Assembly);
        if (!bridge.Status.IsAvailable) throw new InvalidOperationException("PassengerJobs bridge unavailable: " + bridge.Status.Code);
        var job = JobsManager.Instance.currentJobs.FirstOrDefault(x => string.Equals(x.ID, jobId, StringComparison.Ordinal));
        if (job == null)
        {
            var allJobs = AccessTools.Field(typeof(JobsManager), "allJobs")?.GetValue(JobsManager.Instance) as IEnumerable<Job>;
            job = allJobs?.FirstOrDefault(x => string.Equals(x.ID, jobId, StringComparison.Ordinal));
        }
        if (job == null || !bridge.IsPassengerJob(job)) throw new InvalidOperationException("The selected ID is not a loaded PassengerJobs mission.");
    }

    private static void StartPassengerService(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); TrySynchronizeHostWallet(entry, "before-passenger-start"); var observed = hostWallet.ReadBalance(); var contract = runtimeStateProvider!.StartLocalPassengerService("passenger-start:" + correlation, selectedPassengerContractId!, observed, runtimeStateProvider.Current!.LeaseClock.ActiveTick, runtimeRoleDetector!, new UnityMissionLifecyclePort()); SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance); status = "Passenger service observation active."; entry.Logger.Log("[correlation=" + correlation + "] [event=passenger-service-started] contract=" + contract.ContractId + ", vanillaBefore=" + observed + ", departureTick=" + contract.ActualDepartureTick); } catch (Exception exception) { status = "Passenger service start refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=passenger-service-start-refused] " + exception); }
    }

    private static void CompletePassengerService(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); var contract = runtimeStateProvider!.Current!.PassengerContracts.Single(x => x.ContractId == selectedPassengerContractId); var result = runtimeStateProvider.CompleteLocalPassengerService("passenger-complete:" + correlation, contract.ContractId, hostWallet.ReadBalance(), runtimeStateProvider.Current.LeaseClock.ActiveTick, contract.AssetIds, runtimeRoleDetector!, new UnityMissionLifecyclePort()); var assignment = runtimeStateProvider.Current.Assignments.Single(x => x.AssignmentId == result.AssignmentId); if (assignment.ExternalSettlement == ExternalSettlementState.Pending) { var delta = hostWallet.ReadBalance() - assignment.ExpectedVanillaBalance; if (delta > 0 && !hostWallet.TryDebit(delta)) throw new InvalidOperationException("Passenger settlement was committed but the vanilla wallet debit failed."); if (delta < 0) hostWallet.Credit(-delta); runtimeStateProvider.MarkLocalMissionSettlement(assignment.AssignmentId, hostWallet.ReadBalance(), runtimeRoleDetector!, new UnityMissionLifecyclePort()); } if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Passenger completion could not be staged in SaveGameData."); status = "Passenger service: " + result.State + "; paid=" + result.PaidRevenue + "; punctuality penalty=" + result.PunctualityPenalty + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=passenger-service-completed] contract=" + result.ContractId + ", observedVanilla=" + result.ObservedVanillaRevenue + ", paid=" + result.PaidRevenue + ", penalty=" + result.PunctualityPenalty + ", arrivalTick=" + result.ActualArrivalTick + ", settlement=" + assignment.ExternalSettlement); } catch (Exception exception) { status = "Passenger completion refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=passenger-service-complete-refused] " + exception); }
    }

    private static void CancelPassengerService(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); var result = runtimeStateProvider!.CancelLocalPassengerService("passenger-cancel:" + correlation, selectedPassengerContractId!, runtimeRoleDetector!, new UnityMissionLifecyclePort()); SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance); status = "Passenger service cancelled; demand restored."; entry.Logger.Log("[correlation=" + correlation + "] [event=passenger-service-cancelled] contract=" + result.ContractId + ", booked=" + result.BookedPassengers + ", demandRestored=" + result.DemandRestored); } catch (Exception exception) { status = "Passenger cancellation refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=passenger-service-cancel-refused] " + exception); }
    }

    private static void DrawOperatingCosts(UnityModManager.ModEntry entry, VehicleAcquisitionSnapshot snapshot, FleetAssetState fleet, CompanyState? company)
    {
        GUILayout.Space(6f);
        GUILayout.Label("Manual operating cost observation");
        if (!runtimeSettings.EnableOperatingCosts) { GUILayout.Label("Operating cost observation is disabled by runtime settings."); return; }
        var assetCosts = snapshot.OperatingCosts.Where(x => x.AssetId == fleet.AssetId && x.State == OperatingCostState.Settled).ToArray();
        GUILayout.Label("Recorded for asset: " + assetCosts.Sum(x => x.ActualCost) + " | sessions: " + assetCosts.Length + " | open sessions suspend automatic wallet sync.");
        var open = snapshot.OperatingCosts.SingleOrDefault(x => x.AssetId == fleet.AssetId && x.State == OperatingCostState.Open);
        if (open != null)
        {
            activeOperatingCostSessionId = open.SessionId;
            GUILayout.Label("Session open: " + open.Action + " | payer " + open.Payer.Key + " | maximum " + open.MaximumAuthorizedCost + ". Use the vanilla service manually, then complete observation.");
            try { GUILayout.Label("Authoritative vehicle condition after: " + new UnityVehicleConditionReader(snapshot).Read(fleet.AssetId)); }
            catch (Exception exception) { GUILayout.Label("Vehicle condition unavailable: " + exception.Message); }
            if (GUILayout.Button("Complete cost observation from current vanilla wallet")) CompleteOperatingCost(entry, open.SessionId);
            if (GUILayout.Button("Cancel observation (only if vanilla wallet is unchanged)")) CancelOperatingCost(entry, open.SessionId);
            return;
        }
        maintenanceActionIndex = GUILayout.Toolbar(maintenanceActionIndex, new[] { "Inspect", "Service", "Repair", "Refuel" });
        GUILayout.Label("Maximum authorized cost:");
        maintenanceMaximumCost = GUILayout.TextField(maintenanceMaximumCost ?? "1000", 20);
        try { GUILayout.Label("Authoritative vehicle condition before: " + new UnityVehicleConditionReader(snapshot).Read(fleet.AssetId)); }
        catch (Exception exception) { GUILayout.Label("Vehicle condition unavailable: " + exception.Message); }
        GUILayout.Label("Optional trip ID:");
        GUILayout.BeginHorizontal();
        maintenanceTripId = GUILayout.TextField(maintenanceTripId ?? "", 48);
        GUILayout.EndHorizontal();
        maintenanceCompanyPayer = company != null && GUILayout.Toggle(maintenanceCompanyPayer, "Company pays and reimburses the actual vanilla debit (off = personal)");
        if (GUILayout.Button("Begin manual cost observation")) BeginOperatingCost(entry, fleet.AssetId);
    }

    private static void BeginOperatingCost(UnityModManager.ModEntry entry, string assetId)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority(); TrySynchronizeHostWallet(entry, "before-operating-cost");
            if (!long.TryParse(maintenanceMaximumCost, out var maximum) || maximum < 0) throw new InvalidOperationException("Maximum cost must be a non-negative whole number.");
            var condition = new UnityVehicleConditionReader(runtimeStateProvider!.Current!).Read(assetId);
            var action = (MaintenanceAction)Math.Max(0, Math.Min(3, maintenanceActionIndex));
            var record = runtimeStateProvider!.BeginLocalOperatingCost("operating-cost:" + correlation, assetId, action, maintenanceCompanyPayer, maximum, hostWallet.ReadBalance(), condition, maintenanceTripId, runtimeRoleDetector!);
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Operating cost session could not be staged for save.");
            activeOperatingCostSessionId = record.SessionId;
            status = "Manual " + record.Action + " observation open. Close BDVM, use vanilla service, then return and complete it.";
            entry.Logger.Log("[correlation=" + correlation + "] [event=operating-cost-begin] session=" + record.SessionId + ", asset=" + record.AssetId + ", action=" + record.Action + ", payer=" + record.Payer.Key + ", maximum=" + record.MaximumAuthorizedCost + ", balanceBefore=" + record.VanillaBalanceBefore + ", conditionBefore=" + record.ConditionBefore + ", trip=" + (record.TripId ?? "none"));
        }
        catch (Exception exception)
        {
            status = "Operating cost session refused: " + exception.Message;
            entry.Logger.Error("[correlation=" + correlation + "] [event=operating-cost-begin-refused] asset=" + assetId + ", error=" + exception);
        }
    }

    private static void CompleteOperatingCost(UnityModManager.ModEntry entry, string sessionId)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            var open = runtimeStateProvider!.Current!.OperatingCosts.Single(value => value.SessionId == sessionId);
            var condition = new UnityVehicleConditionReader(runtimeStateProvider.Current).Read(open.AssetId);
            var observedAfter = hostWallet.ReadBalance();
            var record = runtimeStateProvider.CompleteLocalOperatingCost(sessionId, observedAfter, condition, runtimeRoleDetector!);
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Operating cost result could not be staged for save.");
            if (record.State == OperatingCostState.Settled && record.ExternalSettlement == ExternalSettlementState.Pending)
            {
                if (hostWallet.ReadBalance() != record.VanillaBalanceAfter) throw new InvalidOperationException("Vanilla wallet changed before company reimbursement; settlement requires review.");
                hostWallet.Credit(record.ExternalReimbursement);
                record = runtimeStateProvider.MarkLocalOperatingCostExternalSettlement(sessionId, hostWallet.ReadBalance(), runtimeRoleDetector!);
                if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("External reimbursement marker could not be staged for save.");
            }
            activeOperatingCostSessionId = null;
            status = "Operating cost: " + record.State + " / " + record.ResultCode + " / actual " + record.ActualCost + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=operating-cost-complete] session=" + record.SessionId + ", asset=" + record.AssetId + ", action=" + record.Action + ", payer=" + record.Payer.Key + ", actual=" + record.ActualCost + ", balanceBefore=" + record.VanillaBalanceBefore + ", balanceAfter=" + record.VanillaBalanceAfter + ", conditionBefore=" + record.ConditionBefore + ", conditionAfter=" + record.ConditionAfter + ", external=" + record.ExternalSettlement + ", result=" + record.ResultCode);
        }
        catch (Exception exception)
        {
            status = "Operating cost completion failed: " + exception.Message;
            entry.Logger.Error("[correlation=" + correlation + "] [event=operating-cost-complete-failed] session=" + sessionId + ", error=" + exception);
        }
    }

    private static void RecoverOperatingCostSettlement(UnityModManager.ModEntry entry, string sessionId)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            var record = runtimeStateProvider!.Current!.OperatingCosts.Single(x => x.SessionId == sessionId);
            var actual = hostWallet.ReadBalance();
            var expectedSettled = checked(record.VanillaBalanceAfter + record.ExternalReimbursement);
            if (actual == record.VanillaBalanceAfter && record.ExternalReimbursement > 0) { hostWallet.Credit(record.ExternalReimbursement); actual = hostWallet.ReadBalance(); }
            else if (actual != expectedSettled) throw new InvalidOperationException("Vanilla wallet no longer matches either side of the pending reimbursement.");
            record = runtimeStateProvider.MarkLocalOperatingCostExternalSettlement(sessionId, actual, runtimeRoleDetector!);
            if (record.ExternalSettlement != ExternalSettlementState.Applied) throw new InvalidOperationException(record.ResultCode);
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Recovered reimbursement marker could not be staged for save.");
            status = "Company reimbursement recovered: " + record.ExternalReimbursement + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=operating-cost-reimbursement-recovered] session=" + sessionId + ", amount=" + record.ExternalReimbursement + ", wallet=" + actual);
        }
        catch (Exception exception)
        {
            status = "Operating cost reimbursement recovery refused: " + exception.Message;
            entry.Logger.Error("[correlation=" + correlation + "] [event=operating-cost-reimbursement-recovery-refused] session=" + sessionId + ", error=" + exception);
        }
    }

    private static void CancelOperatingCost(UnityModManager.ModEntry entry, string sessionId)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            var record = runtimeStateProvider!.CancelLocalOperatingCost(sessionId, hostWallet.ReadBalance(), runtimeRoleDetector!);
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Operating cost cancellation could not be staged for save.");
            activeOperatingCostSessionId = null;
            status = "Operating cost observation cancelled; reserved company funds were released.";
            entry.Logger.Log("[correlation=" + correlation + "] [event=operating-cost-cancelled] session=" + record.SessionId + ", payer=" + record.Payer.Key + ", reservation=" + record.ReservedAmount + ", result=" + record.ResultCode);
        }
        catch (Exception exception)
        {
            status = "Operating cost cancellation refused: " + exception.Message;
            entry.Logger.Error("[correlation=" + correlation + "] [event=operating-cost-cancel-refused] session=" + sessionId + ", error=" + exception);
        }
    }

    private static void DrawVehicleResale(UnityModManager.ModEntry entry, VehicleAcquisitionSnapshot snapshot, FleetAssetState fleet)
    {
        GUILayout.Space(6f);
        GUILayout.Label("Vehicle resale (audited validation quote)");
        if (!runtimeSettings.EnableVehicleResale)
        {
            GUILayout.Label("Vehicle resale is disabled by runtime settings.");
            return;
        }
        var asset = snapshot.Assets.Assets.Single(x => x.AssetId == fleet.AssetId);
        var hasMarketPrice = runtimeSettings.EnableFiniteMarket && snapshot.Market.Catalog.Any(x => x.DefinitionId == asset.DefinitionId);
        GUILayout.Label(hasMarketPrice ? "Dynamic market buyback uses the market condition/factor fields; proceeds are calculated, not entered." : "Net proceeds (must not exceed the frozen purchase reference):");
        if (!hasMarketPrice) resaleProceeds = GUILayout.TextField(resaleProceeds ?? "", 20);
        var bundle = snapshot.Assets.Bundles.SingleOrDefault(x => x.ComponentAssetIds.Contains(fleet.AssetId));
        if (bundle != null) GUILayout.Label("Commercial bundle: " + bundle.BundleId + " | components: " + bundle.ComponentAssetIds.Count + ". Every component will be checked and sold together.");
        if (GUILayout.Button(bundle == null ? "Prepare resale quote for selected vehicle" : "Prepare resale quote for complete bundle"))
        {
            var correlation = Guid.NewGuid().ToString("N");
            try
            {
                RequireHostAuthority();
                VehicleResaleQuote quote;
                if (hasMarketPrice && bundle == null)
                {
                    if (!decimal.TryParse(marketCondition, out var condition) || !decimal.TryParse(marketFactor, out var factor)) throw new InvalidOperationException("Market condition and factor must be valid decimals.");
                    quote = runtimeStateProvider!.PrepareLocalMarketBuybackQuote("market-buyback:" + correlation, fleet.AssetId, condition, factor, 0, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter());
                }
                else
                {
                    if (!long.TryParse(resaleProceeds, out var proceeds) || proceeds < 0) throw new InvalidOperationException("Resale proceeds must be a non-negative whole number.");
                    quote = bundle == null
                        ? runtimeStateProvider!.PrepareLocalResaleQuote("resale-quote:" + correlation, fleet.AssetId, proceeds, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter())
                        : runtimeStateProvider!.PrepareLocalBundleResaleQuote("resale-quote:" + correlation, bundle.BundleId, proceeds, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter());
                }
                preparedResaleQuoteId = quote.QuoteId;
                if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Resale quote could not be staged for save.");
                status = "Resale quote prepared: " + quote.Proceeds + " from reference " + quote.ReferenceValue + ".";
                entry.Logger.Log("[correlation=" + correlation + "] [event=resale-quote] quote=" + quote.QuoteId + ", asset=" + quote.AssetId + ", bundle=" + (quote.BundleId ?? "none") + ", components=" + string.Join(",", quote.AssetIds) + ", seller=" + quote.Seller.Key + ", payee=" + quote.Payee.Key + ", proceeds=" + quote.Proceeds + ", reference=" + quote.ReferenceValue + ", source=" + quote.ReferenceSource + ", condition=" + quote.ObservedCondition + ", rate=" + quote.AppliedRate + ", fuel=" + quote.FuelValue + ", fee=" + quote.TransferFee);
            }
            catch (Exception exception)
            {
                status = "Resale quote refused: " + exception.Message;
                entry.Logger.Error("[correlation=" + correlation + "] [event=resale-quote-refused] asset=" + fleet.AssetId + ", error=" + exception);
            }
        }
        if (!string.IsNullOrWhiteSpace(preparedResaleQuoteId) && GUILayout.Button(bundle == null ? "Sell selected vehicle to merchant" : "Sell complete bundle to merchant")) ExecuteVehicleResale(entry);
    }

    private static void CreateFleetBundle(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            var bundle = runtimeStateProvider!.CreateLocalBundle("bundle:" + correlation, bundleSelection.ToArray(), runtimeRoleDetector!);
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Bundle was created in memory but could not be staged for save.");
            selectedFleetAssetId = bundle.ComponentAssetIds[0];
            bundleSelection.Clear();
            status = "Bundle created with " + bundle.ComponentAssetIds.Count + " components. Save normally, then reload to verify.";
            entry.Logger.Log("[correlation=" + correlation + "] [event=bundle-created] bundle=" + bundle.BundleId + ", components=" + string.Join(",", bundle.ComponentAssetIds));
        }
        catch (Exception exception)
        {
            status = "Bundle creation refused: " + exception.Message;
            entry.Logger.Error("[correlation=" + correlation + "] [event=bundle-create-refused] components=" + string.Join(",", bundleSelection) + ", error=" + exception);
        }
    }

    private static void ExecutePendingResaleReconciliation(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            var results = runtimeStateProvider!.ReconcilePendingResales(runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter());
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Reconciled sales could not be staged for save.");
            foreach (var result in results)
            {
                var quote = runtimeStateProvider.Current!.ResaleQuotes.Single(x => x.QuoteId == result.QuoteId);
                if (result.State == ResaleState.Succeeded && quote.Payee.Kind == AccountKind.Player && result.Proceeds > 0) hostWallet.Credit(result.Proceeds);
                entry.Logger.Log("[correlation=" + correlation + "] [event=resale-reconcile] command=" + result.CommandId + ", state=" + result.State + ", result=" + result.ResultCode + ", components=" + string.Join(",", result.AssetIds));
            }
            status = "Resale reconciliation processed: " + results.Count + ".";
        }
        catch (Exception exception)
        {
            status = "Resale reconciliation failed: " + exception.Message;
            entry.Logger.Error("[correlation=" + correlation + "] [event=resale-reconcile-failed] error=" + exception);
        }
    }

    private static void ExecuteVehicleResale(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        var externalCredited = false;
        long proceeds = 0;
        try
        {
            RequireHostAuthority();
            var quote = runtimeStateProvider!.Current!.ResaleQuotes.Single(x => x.QuoteId == preparedResaleQuoteId);
            proceeds = quote.Proceeds;
            var result = runtimeStateProvider.SellLocalVehicle("resale:" + correlation, quote.QuoteId, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter());
            if (result.State != ResaleState.Succeeded)
            {
                throw new InvalidOperationException(result.ResultCode + ": " + result.ReleaseDetail);
            }
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Resale succeeded in memory but could not be staged for save.");
            if (quote.Payee.Kind == AccountKind.Player && proceeds > 0)
            {
                hostWallet.Credit(proceeds);
                externalCredited = true;
            }
            preparedResaleQuoteId = null;
            selectedFleetAssetId = null;
            status = "Vehicle sold to merchant for " + proceeds + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=vehicle-resale] quote=" + result.QuoteId + ", asset=" + result.AssetId + ", components=" + string.Join(",", result.AssetIds) + ", proceeds=" + result.Proceeds + ", reference=" + result.ReferenceValue + ", source=" + result.ReferenceSource + ", condition=" + result.ObservedCondition + ", rate=" + result.AppliedRate + ", fuel=" + result.FuelValue + ", fee=" + result.TransferFee + ", release=" + result.ReleaseDetail + ", result=" + result.ResultCode);
        }
        catch (Exception exception)
        {
            status = "Vehicle resale refused: " + exception.Message;
            entry.Logger.Error("[correlation=" + correlation + "] [event=vehicle-resale-refused] quote=" + preparedResaleQuoteId + ", proceeds=" + proceeds + ", externalCredited=" + externalCredited + ", error=" + exception);
            if (!externalCredited) TrySynchronizeHostWallet(entry, "after-resale-refusal");
        }
    }

    private static void ExecuteFleetCommand(UnityModManager.ModEntry entry, FleetCommandAction action, FleetOperationalState? operationalState = null, AssetOwnerRef? target = null, string? displayName = null)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            if (string.IsNullOrWhiteSpace(selectedFleetAssetId)) throw new InvalidOperationException("Select one owned fleet asset first.");
            var result = runtimeStateProvider!.ManageLocalFleet("fleet:" + correlation, selectedFleetAssetId!, action, runtimeRoleDetector!, operationalState, target, displayName);
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance))
                throw new InvalidOperationException("Fleet command was applied in memory but could not be staged for save.");
            status = "Fleet " + action + ": " + result.Outcome + " / " + result.ResultCode + ". Save normally, then reload to verify.";
            entry.Logger.Log("[correlation=" + correlation + "] [event=fleet-command] action=" + action + ", asset=" + result.AssetId + ", outcome=" + result.Outcome + ", result=" + result.ResultCode + ", versionBefore=" + result.FleetVersionBefore + ", versionAfter=" + result.FleetVersionAfter + ", detail=" + result.Detail);
        }
        catch (Exception exception)
        {
            status = "Fleet command refused: " + exception.Message;
            entry.Logger.Error("[correlation=" + correlation + "] [event=fleet-command-refused] action=" + action + ", asset=" + selectedFleetAssetId + ", error=" + exception);
        }
    }

    private static void DrawLicenseStatus(UnityModManager.ModEntry entry, VehicleAcquisitionSnapshot snapshot)
    {
        if (!runtimeSettings.EnableLicenseQuotes) return;
        GUILayout.Label("License economy: observation/quote mode; configured rules=" + snapshot.Economy.LicenseEconomy.Rules.Count + ". No vanilla hard gate is added.");
        if (GUILayout.Button("Log license economy status"))
        {
            var correlation = Guid.NewGuid().ToString("N");
            entry.Logger.Log("[correlation=" + correlation + "] [event=license-economy-observation] rules=" + snapshot.Economy.LicenseEconomy.Rules.Count + ", records=" + snapshot.Economy.LicenseEconomy.Records.Count + ", hardGate=false");
            status = "License economy observation logged; no charge was applied.";
        }
    }

    private static void RunAutomatedInGameValidation(UnityModManager.ModEntry entry)
    {
        var correlation = "ingame-validation:" + Guid.NewGuid().ToString("N");
        var lines = new List<string>();
        var passed = 0;
        var failed = 0;
        var skipped = 0;

        void Check(string name, Action validation)
        {
            try
            {
                validation();
                passed++;
                lines.Add("PASS | " + name);
            }
            catch (Exception exception)
            {
                failed++;
                lines.Add("FAIL | " + name + " | " + exception.GetType().Name + ": " + exception.Message);
            }
        }

        void Skip(string name, string reason)
        {
            skipped++;
            lines.Add("SKIP | " + name + " | " + reason);
        }

        var snapshot = runtimeStateProvider?.Current;
        Check("career runtime is initialized", () =>
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(runtimeStateProvider?.LocalPlayerId))
                throw new InvalidOperationException("No initialized BDVM career is available.");
        });

        if (snapshot != null)
        {
            Check("all runtime feature flags are enabled", () =>
            {
                var disabled = typeof(RuntimeSaveSettings).GetProperties()
                    .Where(x => x.Name.StartsWith("Enable", StringComparison.Ordinal) && x.PropertyType == typeof(bool) && !(bool)x.GetValue(runtimeSettings, null))
                    .Select(x => x.Name)
                    .ToArray();
                if (disabled.Length > 0) throw new InvalidOperationException("Disabled: " + string.Join(", ", disabled));
            });
            Check("authoritative economy role", () =>
            {
                if (!NetworkAuthorityPolicy.CanExecuteEconomy(runtimeRoleDetector!.Detect(), out var reason))
                    throw new InvalidOperationException(reason);
            });
            Check("complete persistent state invariants", () => VehicleAcquisitionPersistence.Validate(snapshot));
            Check("company economy invariants", () => CompanyEconomyPersistence.Validate(snapshot.Economy));
            Check("finite market invariants", () => FiniteMarketValidation.Validate(snapshot.Market));
            Check("dynamic economy invariants", () => DynamicEconomyValidation.Validate(snapshot.DynamicEconomy));
            Check("initial delivery invariants", () => InitialDeliveryValidation.Validate(snapshot));
            Check("inbound lease invariants", () => LeaseValidation.Validate(snapshot));
            Check("outbound lease invariants", () => OutboundLeaseValidation.Validate(snapshot));
            Check("mission assignment invariants", () => MissionAssignmentValidation.Validate(snapshot));
            Check("industrial contract invariants", () => IndustrialEconomyValidation.Validate(snapshot));
            Check("passenger economy invariants", () => PassengerEconomyValidation.Validate(snapshot));
            Check("financing invariants", () => FinancingValidation.Validate(snapshot.Financing, snapshot));
            Check("asset lifecycle invariants", () => AssetLifecycleValidation.Validate(snapshot.AssetLifecycle, snapshot));
            Check("dedicated authority invariants", () => DedicatedAuthorityValidation.Validate(snapshot.DedicatedAuthority));
            Check("triage assistance invariants", () => TriageAssistanceValidation.Validate(snapshot.TriageAssistance, snapshot));
            Check("world population policy invariants", () => WorldPopulationPolicyEngine.Validate(runtimeSettings.WorldPopulationPolicy));
            Check("strict rolling-stock control is active", () =>
            {
                if (UnityWorldPopulationControl.State != WorldPopulationRuntimeState.Active)
                    throw new InvalidOperationException(UnityWorldPopulationControl.State + " / " + UnityWorldPopulationControl.ResultCode);
            });
            Check("economic rolling-stock spawn sources are denied", () =>
            {
                if (UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.NaturalLocomotive, "validation:natural-locomotive")) throw new InvalidOperationException("Natural locomotives are still allowed.");
                if (UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.ContractProvidedVehicle, "validation:contract-rolling-stock")) throw new InvalidOperationException("Contract-provided rolling stock is still allowed.");
                if (UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.Unknown, "validation:unknown-spawn")) throw new InvalidOperationException("Unknown spawn sources are still allowed.");
            });
            Check("authorized delivery and external traffic remain allowed", () =>
            {
                if (!UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.PurchasedDelivery, "validation:purchased-delivery")) throw new InvalidOperationException("Purchased delivery is blocked.");
                if (!UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.StarterDelivery, "validation:starter-delivery")) throw new InvalidOperationException("Starter delivery is blocked.");
                if (!UnityWorldPopulationControl.ShouldRun(WorldPopulationSource.ExternalTraffic, "validation:external-traffic")) throw new InvalidOperationException("External traffic is blocked.");
            });
            Check("physical rolling-stock inventory is observable", () =>
            {
                var count = CarSpawner.Instance?.AllCars?.Count ?? throw new InvalidOperationException("CarSpawner inventory is unavailable.");
                lines.Add("INFO | physical rolling stock at validation start | " + count);
            });
            Check("SelfShunt strict-generation bridge is available", () =>
            {
                if (!SelfShuntBridgeLocator.TryCreate(out _, out var code)) throw new InvalidOperationException(code);
            });
            Check("Passenger Jobs runtime bridge is available", () =>
            {
                var passengerMod = UnityModManager.FindMod("PassengerJobs");
                var bridge = new PassengerJobsRuntimeBridge(passengerMod?.Info?.Version, passengerMod?.Assembly);
                if (!bridge.Status.IsAvailable) throw new InvalidOperationException(bridge.Status.Code);
            });
            Check("Multiplayer API runtime is loaded", () =>
            {
                if (MultiplayerAPI.Instance == null) throw new InvalidOperationException("Multiplayer API instance is unavailable.");
            });
            Check("industrial production advances, backpressures and spawns no rolling stock", () =>
            {
                var clone = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(snapshot), snapshot.CheckpointId);
                var facility = "DEV-PRODUCTION-" + Guid.NewGuid().ToString("N");
                clone.IndustrialStocks.Add(new IndustrialStock { FacilityId = facility, CargoId = "Input", OnHand = 10m, Capacity = 10m });
                clone.IndustrialStocks.Add(new IndustrialStock { FacilityId = facility, CargoId = "Output", OnHand = 0m, Capacity = 1m });
                clone.IndustrialRecipes.Add(new IndustrialRecipe { RecipeId = facility, FacilityId = facility, InputCargoId = "Input", InputQuantity = 2m, OutputCargoId = "Output", OutputQuantity = 1m, CadenceTicks = 10, MaximumBacklogCycles = 3 });
                var assetCount = clone.Assets.Assets.Count;
                var fleetCount = clone.Fleet.Count;
                var engine = new IndustrialEconomyEngine(clone, runtimeRoleDetector!, new DisabledIndustrialExecutionPort());
                var first = engine.AdvanceProduction("dev-production-first:" + correlation, facility, 20);
                var recipe = clone.IndustrialRecipes.Single(x => x.RecipeId == facility);
                if (first != 1 || recipe.PendingCycles != 1) throw new InvalidOperationException("Production backpressure was not preserved.");
                clone.IndustrialStocks.Single(x => x.FacilityId == facility && x.CargoId == "Output").OnHand = 0m;
                var second = engine.AdvanceProduction("dev-production-second:" + correlation, facility, 20);
                if (second != 1 || recipe.PendingCycles != 0) throw new InvalidOperationException("Pending production did not resume.");
                if (clone.Assets.Assets.Count != assetCount || clone.Fleet.Count != fleetCount) throw new InvalidOperationException("Production created rolling stock.");
                IndustrialEconomyValidation.Validate(clone);
            });
            Check("shortage-driven transport need reserves stock without spawning rolling stock", () =>
            {
                var clone = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(snapshot), snapshot.CheckpointId);
                var suffix = Guid.NewGuid().ToString("N");
                var origin = "DEV-ORIGIN-" + suffix;
                var destination = "DEV-DESTINATION-" + suffix;
                var cargo = "DEV-CARGO-" + suffix;
                var policyId = "DEV-POLICY-" + suffix;
                var assetCount = clone.Assets.Assets.Count;
                var fleetCount = clone.Fleet.Count;
                var engine = new IndustrialEconomyEngine(clone, runtimeRoleDetector!, new DisabledIndustrialExecutionPort());
                engine.ConfigureStock("dev-need-origin:" + correlation, origin, cargo, 20m, 20m);
                engine.ConfigureStock("dev-need-destination:" + correlation, destination, cargo, 0m, 20m);
                engine.ConfigureTransportPolicy("dev-need-policy:" + correlation, policyId, origin, destination, cargo, 10m, 15m,
                    100, 50, 100, 20, 0, new WagonRequirement { CargoId = cargo, MinimumWagonCount = 1, MinimumTotalCapacity = 10m }, true, 200);
                var need = engine.PublishTransportNeed("dev-need-publish:" + correlation, policyId, clone.LeaseClock.ActiveTick)
                    ?? throw new InvalidOperationException("Expected a shortage-driven transport need.");
                var playerId = runtimeStateProvider!.LocalPlayerId!;
                var contract = engine.AcceptTransportNeed("dev-need-accept:" + correlation, need.NeedId, need.Version,
                    AccountRef.Player(playerId), null, clone.LeaseClock.ActiveTick);
                var source = clone.IndustrialStocks.Single(value => value.FacilityId == origin && value.CargoId == cargo);
                var target = clone.IndustrialStocks.Single(value => value.FacilityId == destination && value.CargoId == cargo);
                if (contract.State != IndustrialContractState.Reserved || source.ReservedOutbound != 10m || target.ReservedInbound != 10m ||
                    contract.DeliveryDeadlineTick != clone.LeaseClock.ActiveTick + 200)
                    throw new InvalidOperationException("Transport need acceptance did not preserve its authoritative reservations and deadline.");
                if (clone.Assets.Assets.Count != assetCount || clone.Fleet.Count != fleetCount)
                    throw new InvalidOperationException("Transport need publication created rolling stock.");
                IndustrialEconomyValidation.Validate(clone);
            });
            Check("save serialization round trip", () =>
            {
                var serialized = VehicleAcquisitionPersistence.Serialize(snapshot);
                var restored = VehicleAcquisitionPersistence.Deserialize(serialized, snapshot.CheckpointId);
                if (restored.CheckpointId != snapshot.CheckpointId)
                    throw new InvalidOperationException("Round-trip checkpoint mismatch.");
                if (restored.Economy.Wallets.Count != snapshot.Economy.Wallets.Count || restored.Fleet.Count != snapshot.Fleet.Count || restored.InitialDeliveries.Count != snapshot.InitialDeliveries.Count)
                    throw new InvalidOperationException("Round-trip durable collection count mismatch.");
            });
            Check("local player and wallet linkage", () =>
            {
                var playerId = runtimeStateProvider!.LocalPlayerId!;
                var player = snapshot.Economy.Players.Single(x => x.PlayerId == playerId);
                if (!snapshot.Economy.Wallets.Any(x => x.Account.Kind == AccountKind.Player && x.Account.OwnerId == player.PlayerId))
                    throw new InvalidOperationException("Local player wallet is missing.");
            });
            Check("authoritative diagnostic export", () => diagnostic!.ExportAuthoritative());

            Check("dev company is available", () =>
            {
                var local = snapshot.Economy.Players.Single(x => x.PlayerId == runtimeStateProvider!.LocalPlayerId);
                if (string.IsNullOrWhiteSpace(local.CompanyId))
                {
                    var created = runtimeStateProvider!.CreateCompany("dev-validation-company:" + correlation, "BDVM Automated Validation");
                    if (created.State != CommandState.Succeeded) throw new InvalidOperationException(created.ResultCode);
                }
            });

            var destructivePlayer = snapshot.Economy.Players.Single(x => x.PlayerId == runtimeStateProvider!.LocalPlayerId);
            if (!string.IsNullOrWhiteSpace(destructivePlayer.CompanyId))
            {
                Check("zero company transfer is refused", () => ExpectTransferRefusal("dev-zero:" + correlation, 0));
                Check("negative company transfer is refused", () => ExpectTransferRefusal("dev-negative:" + correlation, -1));
                Check("overdrawn company withdrawal is refused", () =>
                {
                    var companyWallet = snapshot.Economy.Wallets.Single(x => x.Account.Kind == AccountKind.Company && x.Account.OwnerId == destructivePlayer.CompanyId);
                    ExpectTransferRefusal("dev-overdraw:" + correlation, checked(companyWallet.Balance + 1), false);
                });

                if (hostWallet.ReadBalance() > 0)
                {
                    Check("real contribution and withdrawal round trip", () =>
                    {
                        TrySynchronizeHostWallet(entry, "automated-validation-before-transfer");
                        var personal = snapshot.Economy.Wallets.Single(x => x.Account.Kind == AccountKind.Player && x.Account.OwnerId == destructivePlayer.PlayerId);
                        var companyWallet = snapshot.Economy.Wallets.Single(x => x.Account.Kind == AccountKind.Company && x.Account.OwnerId == destructivePlayer.CompanyId);
                        var personalBefore = personal.Balance;
                        var companyBefore = companyWallet.Balance;
                        var externalDebited = false;
                        var contributed = false;
                        try
                        {
                            if (!hostWallet.TryDebit(1)) throw new InvalidOperationException("Vanilla wallet debit failed.");
                            externalDebited = true;
                            var contribution = runtimeStateProvider!.TransferLocalCompany("dev-contribution:" + correlation, 1, true);
                            if (contribution.State != CommandState.Succeeded) throw new InvalidOperationException(contribution.ResultCode);
                            contributed = true;
                            var withdrawal = runtimeStateProvider.TransferLocalCompany("dev-withdrawal:" + correlation, 1, false);
                            if (withdrawal.State != CommandState.Succeeded) throw new InvalidOperationException(withdrawal.ResultCode);
                            contributed = false;
                            hostWallet.Credit(1);
                            externalDebited = false;
                            if (personal.Balance != personalBefore || companyWallet.Balance != companyBefore)
                                throw new InvalidOperationException("Round-trip balances did not return to their starting values.");
                        }
                        catch
                        {
                            if (contributed) runtimeStateProvider!.TransferLocalCompany("dev-transfer-compensation:" + correlation, 1, false);
                            if (externalDebited) hostWallet.Credit(1);
                            throw;
                        }
                    });
                }
                else Skip("real contribution and withdrawal round trip", "personal wallet has no available unit");
            }
            else Skip("company transfer mutations", "company creation was refused");

            Check("advance dynamic market clock by 100 ticks", () =>
            {
                runtimeStateProvider!.AdvanceFiniteMarket(checked(snapshot.Market.ClockTick + 100), runtimeRoleDetector!, new UnityExistingVehicleOwnershipAdapter());
            });

            Check("create affordable destructive market fixture", () =>
            {
                var fixtureId = "BDVM.DevValidation." + correlation.Substring(correlation.LastIndexOf(':') + 1);
                runtimeStateProvider!.ConfigureFiniteMarketDefinition(fixtureId, "Freight", 1, 0, 1m, 1m, 0.5m, "DevValidationYard", 1);
                runtimeStateProvider.GenerateFiniteMarketOrder("dev-market-listing:" + correlation, fixtureId, "DevValidationYard", runtimeRoleDetector!, new UnityExistingVehicleOwnershipAdapter());
            });

            var affordableListing = snapshot.Market.Listings
                .Where(x => x.State == MarketListingState.Available && x.Price <= hostWallet.ReadBalance())
                .OrderBy(x => x.Price)
                .FirstOrDefault();
            if (affordableListing == null) Skip("real finite-market purchase", "no affordable available listing");
            else
            {
                Check("real finite-market purchase", () =>
                {
                    TrySynchronizeHostWallet(entry, "automated-validation-before-purchase");
                    var debited = false;
                    var committed = false;
                    try
                    {
                        if (affordableListing.Price > 0 && !hostWallet.TryDebit(affordableListing.Price))
                            throw new InvalidOperationException("Vanilla wallet debit failed.");
                        debited = affordableListing.Price > 0;
                        var purchase = runtimeStateProvider!.PurchaseLocalMarket("dev-market-purchase:" + correlation, affordableListing.ListingId, false, runtimeRoleDetector!, new UnityExistingVehicleOwnershipAdapter(), new DisabledMarketDeliveryPort());
                        committed = purchase.State == MarketPurchaseState.Succeeded || purchase.State == MarketPurchaseState.ReconcileRequired;
                        if (!committed) throw new InvalidOperationException(purchase.ResultCode);
                    }
                    catch
                    {
                        if (debited && !committed) hostWallet.Credit(affordableListing.Price);
                        throw;
                    }
                });
            }

            destructivePlayer = snapshot.Economy.Players.Single(x => x.PlayerId == runtimeStateProvider!.LocalPlayerId);
            var destructiveCompany = string.IsNullOrWhiteSpace(destructivePlayer.CompanyId)
                ? null
                : snapshot.Economy.Companies.Single(x => x.CompanyId == destructivePlayer.CompanyId);
            if (destructiveCompany == null) Skip("company dissolution", "local player has no company");
            else if (destructiveCompany.LeaderId != destructivePlayer.PlayerId && (!destructiveCompany.DelegatedPermissions.TryGetValue(destructivePlayer.PlayerId, out var rights) || !rights.Contains(CompanyPermission.Dissolve)))
                Skip("company dissolution", "local player lacks dissolution permission");
            else
            {
                Check("company dissolution, contract cancellation and asset liquidation", () =>
                {
                    var personal = snapshot.Economy.Wallets.Single(x => x.Account.Kind == AccountKind.Player && x.Account.OwnerId == destructivePlayer.PlayerId);
                    var before = personal.Balance;
                    var releaseGuard = new UnityAssetReleaseGuard();
                    var result = runtimeStateProvider!.DissolveCompanyFor("dev-dissolution:" + correlation, destructivePlayer.PlayerId, destructiveCompany.CompanyId, 0, 0, runtimeRoleDetector!, releaseGuard, new UnityExistingVehicleOwnershipAdapter(), new CompositeCompanyContractCancellationPort(new CompanyWorkflowCancellationPort(snapshot, runtimeRoleDetector!), new OutboundLeaseCompanyContractCancellationPort(snapshot, runtimeRoleDetector!, releaseGuard, new DeclaredOffSceneLeaseSimulationPort()), new FinancingCompanyContractCancellationPort(snapshot, runtimeRoleDetector!)), new SaveGameLiquidationCheckpointPort(entry));
                    if (result.State != CompanyLiquidationState.Succeeded) throw new InvalidOperationException(result.ResultCode);
                    var credited = personal.Balance - before;
                    if (credited > 0) hostWallet.Credit(credited);
                });
            }

            Check("stage destructive validation in SaveGameData", () =>
            {
                if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance))
                    throw new InvalidOperationException("The mutated validation state could not be staged for save.");
            });
        }

        var reportDirectory = Path.Combine(entry.Path, "diagnostics");
        Directory.CreateDirectory(reportDirectory);
        var reportPath = Path.Combine(reportDirectory, "ingame-validation-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".txt");
        var header = new[]
        {
            "BDVM automated in-game validation",
            "UTC: " + DateTime.UtcNow.ToString("O"),
            "Correlation: " + correlation,
            "Mode: destructive development-save validation",
            "Result: " + passed + " passed, " + failed + " failed, " + skipped + " skipped",
            ""
        };
        File.WriteAllLines(reportPath, header.Concat(lines));
        status = failed == 0
            ? "Destructive validation passed: " + passed + " passed, " + skipped + " skipped. Report: " + reportPath
            : "Destructive validation failed: " + failed + " failed, " + passed + " passed, " + skipped + " skipped. Report: " + reportPath;
        entry.Logger.Log("[correlation=" + correlation + "] [event=ingame-validation] mode=destructive, passed=" + passed + ", failed=" + failed + ", skipped=" + skipped + ", report=" + reportPath);
    }

    private static void ExpectTransferRefusal(string commandId, long amount, bool toCompany = true)
    {
        try
        {
            var result = runtimeStateProvider!.TransferLocalCompany(commandId, amount, toCompany);
            if (result.State == CommandState.Rejected) return;
            throw new InvalidOperationException("Transfer unexpectedly returned " + result.State + " / " + result.ResultCode + ".");
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException exception) when (!exception.Message.StartsWith("Transfer unexpectedly returned", StringComparison.Ordinal)) { }
    }

    private static void RequireHostAuthority()
    {
        if (runtimeRoleDetector == null) throw new InvalidOperationException("Host authority is unavailable.");
        if (!NetworkAuthorityPolicy.CanExecuteEconomy(runtimeRoleDetector.Detect(), out var reason))
            throw new InvalidOperationException(reason);
    }

    private static string ResolveAuthenticatedRuntimeActor(string transportIdentity, VehicleAcquisitionSnapshot snapshot, bool isLoopbackRequest = false)
    {
        if (isLoopbackRequest)
        {
            var localPlayerId = runtimeStateProvider?.LocalPlayerId;
            if (string.IsNullOrWhiteSpace(localPlayerId)) throw new UnauthorizedAccessException("The local economic player is unavailable.");
            runtimeStateProvider!.EnsurePersistentPlayer(localPlayerId!, 0);
            return localPlayerId!;
        }
        var connected = new List<AuthenticatedTransportActor>();
        if (configuredServer != null)
        {
            var resolver = new PersistentMultiplayerPeerIdentityResolver();
            foreach (var peer in configuredServer.Players.Where(value => value != null))
                if (resolver.TryResolve(peer, out var durablePlayerId))
                    connected.Add(new AuthenticatedTransportActor { TransportIdentity = peer.Username, PlayerId = durablePlayerId });
        }
        var actorId = AuthenticatedActorRouting.Resolve(transportIdentity, runtimeStateProvider?.LocalPlayerId,
            snapshot.Economy.Players.Select(value => value.PlayerId), connected);
        runtimeStateProvider!.EnsurePersistentPlayer(actorId, 0);
        return actorId;
    }

    private static string BuildRemoteDispatchState(string transportIdentity)
        => BuildRemoteDispatchState(transportIdentity, false);

    private static string BuildRemoteDispatchState(string transportIdentity, bool isLoopbackRequest)
    {
        if (runtimeRoleDetector?.Detect().Role == NetworkRole.MultiplayerClient)
            return RequestClientAuthoritativeState();
        RequireHostAuthority(); var snapshot = runtimeStateProvider?.Current ?? throw new InvalidOperationException("BDVM career state is unavailable."); var playerId = ResolveAuthenticatedRuntimeActor(transportIdentity, snapshot, isLoopbackRequest);
        if (!string.Equals(playerId, runtimeStateProvider!.LocalPlayerId, StringComparison.Ordinal))
            SynchronizeRemotePlayerWallet(playerId, "state:" + Guid.NewGuid().ToString("N"));
        var player = snapshot.Economy.Players.Single(value => value.PlayerId == playerId);
        var visibility = new AuthenticatedActorVisibility(player.PlayerId, player.CompanyId);
        var visibleAssetIds = snapshot.Ownership.Where(value => visibility.CanView(value.Owner)).Select(value => value.AssetId)
            .Concat(snapshot.Leases.Where(value => visibility.CanView(value.Lessee)).SelectMany(value => value.AssetIds))
            .Distinct(StringComparer.Ordinal).ToArray();
        var visibleAssignmentIds = snapshot.Assignments.Where(value => visibility.IsPlayer(value.RequestedBy) || visibility.CanView(value.Operator))
            .Select(value => value.AssignmentId).ToArray();
        var liveFleetLocations = ReadLiveFleetLocations(snapshot);
        var missionChoices = LoadedMissionChoices();
        var locationChoices = (StationController.allStations ?? Enumerable.Empty<StationController>())
            .Where(value => value != null && value.logicStation != null)
            .Select(value => value.logicStation.ID.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .Select(value => new { id = value, name = FriendlyLocationName(value) }).ToArray();
        var cargoChoices = Globals.G.Types.cargos.Where(value => value != null && value.v1 != CargoType.None)
            .Select(value => value.v1.ToString()).Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .Select(value => new { id = value, name = FriendlyIdentifier(value) }).ToArray();
        var catalogCandidates = new UnityVehicleDefinitionReader().ReadLoadedDefinitions("management-catalog")
            .Where(value => value.Resolution == ResolutionState.Resolved && !string.IsNullOrWhiteSpace(value.ExistingDefinitionId))
            .GroupBy(value => value.ExistingDefinitionId!, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .OrderBy(value => value.ExistingDefinitionId, StringComparer.Ordinal)
            .Select(value => new
            {
                definitionId = value.ExistingDefinitionId,
                displayName = FriendlyIdentifier(value.ExistingDefinitionId!),
                categoryId = FleetVehicleClassifier.Classify(value.Type, value.ExistingDefinitionId).ToString(),
                typeId = value.Type,
                provider = value.Origin?.ProviderId ?? value.Origin?.AssemblyName
            }).ToArray();
        var payload = new
        {
            schema = "bdvm.remote-dispatch", schemaVersion = 2, release = "0.3.0-beta", transportIdentity, authorityActor = playerId,
            supportedIntents = new[] { "fleet.set-state", "fleet.rename", "fleet.bundle", "fleet.resale", "fleet.maintenance", "company.create", "company.apply", "company.invite", "company.decide-application", "company.respond-invitation", "company.leave", "company.policy", "company.permission", "company.transfer-leadership", "company.dissolve", "wallet.transfer", "market.configure", "market.generate-order", "market.purchase", "initial-delivery.place", "lease.manage", "assignment.cancel", "assignment.manage", "finance.manage", "yard.manage", "industry.manage" },
            wallets = snapshot.Economy.Wallets.Where(x => visibility.CanView(x.Account)).Select(x => new { account = x.Account.Key, x.Balance, x.Version }),
            walletMirror = snapshot.Economy.ExternalWalletMirrors.Where(x => visibility.IsPlayer(x.PlayerId)).Select(x => new { x.PlayerId, x.LastSynchronizedBalance, x.LastOperationId, x.Version }),
            companies = snapshot.Economy.Companies.Select(x => new { x.CompanyId, x.Name, x.LeaderId, members = visibility.IsCompany(x.CompanyId) ? x.Members.ToArray() : Array.Empty<string>(), delegatedPermissions = visibility.IsCompany(x.CompanyId) ? x.DelegatedPermissions.ToDictionary(p => p.Key, p => p.Value.Select(v => v.ToString()).ToArray()) : new Dictionary<string, string[]>(), x.MembershipPolicy, x.Liquidating, x.Version }),
            membershipRequests = snapshot.Economy.MembershipRequests.Where(x => visibility.IsPlayer(x.PlayerId) || visibility.IsCompany(x.CompanyId)).Select(x => new { x.RequestId, kind = x.Kind.ToString(), state = x.State.ToString(), x.PlayerId, x.CompanyId, x.Version }),
            fleet = snapshot.Fleet.Where(x => visibleAssetIds.Contains(x.AssetId, StringComparer.Ordinal)).Select(x => new { x.AssetId, x.DisplayName, definitionId = snapshot.Assets.Assets.SingleOrDefault(a => a.AssetId == x.AssetId)?.DefinitionId, kind = x.Kind.ToString(), state = x.OperationalState.ToString(), owner = snapshot.Ownership.Single(o => o.AssetId == x.AssetId).Owner.Key, operatorRef = x.Operator?.Key, LastKnownLocation = liveFleetLocations.TryGetValue(x.AssetId, out var liveTrackId) ? liveTrackId : x.LastKnownLocation, x.Version }),
            market = snapshot.Market.Listings.Select(x => new { displayName = FriendlyIdentifier(x.DefinitionId), x.ListingId, kind = x.Kind.ToString(), state = x.State.ToString(), x.AssetId, x.DefinitionId, locationName = FriendlyLocationName((x.LocationId ?? "").Split('-')[0]), x.LocationId, x.Price, x.ExpiresTick, x.Version }),
            catalog = snapshot.Market.Catalog.Select(x => new { displayName = FriendlyIdentifier(x.DefinitionId), x.DefinitionId, x.CategoryId, x.BasePrice, x.TransferFee, x.BuybackRate, x.Version }),
            catalogCandidates,
            locationChoices,
            cargoChoices,
            marketStock = snapshot.Market.Stock.Select(x => new { displayName = FriendlyIdentifier(x.DefinitionId), locationName = FriendlyLocationName((x.LocationId ?? "").Split('-')[0]), x.LocationId, x.DefinitionId, x.Available, x.Capacity, x.Version }),
            initialDeliveries = snapshot.InitialDeliveries.Where(x => visibility.CanView(x.Owner) || visibility.CanView(x.AuthorizedOperator)).Select(x => new { x.GrantId, owner = x.Owner.Key, authorizedOperator = x.AuthorizedOperator?.Key, x.AssetIds, x.DefinitionIds, state = x.State.ToString(), x.FreePlacement, x.TargetTrackId, targetKind = x.TargetKind?.ToString(), x.ResultCode, x.Version }),
            deliveryTracks = LeaseReturnTrackRules(snapshot).Select(x => new { x.TrackId, kind = x.Kind.ToString() }),
            operatingCosts = snapshot.OperatingCosts.Where(x => visibility.IsPlayer(x.RequesterId) || visibility.CanView(x.Payer)).Select(x => new { x.SessionId, x.AssetId, action = x.Action.ToString(), payer = x.Payer.Key, state = x.State.ToString(), x.MaximumAuthorizedCost, x.ReservedAmount, x.ActualCost, x.SubsidizedExcess, x.ConditionBefore, x.ConditionAfter, settlement = x.ExternalSettlement.ToString(), x.ResultCode }),
            leases = snapshot.Leases.Where(x => x.State == LeaseState.Offered || visibility.CanView(x.Lessee) || visibility.CanView(x.Payer)).Select(x => new { x.LeaseId, state = x.State.ToString(), x.AssetIds, lessee = x.Lessee?.Key, payer = x.Payer?.Key, x.Deposit, x.HeldDeposit, x.InitialFee, x.RentAmount, x.RentIntervalTicks, x.DurationTicks, x.StartTick, x.EndTick, x.NextDueTick, x.PurchaseOptionPrice, x.ConditionAtStart, x.ConditionAtReturn, x.OutstandingDebt, x.Version }),
            assignments = snapshot.Assignments.Where(x => visibleAssignmentIds.Contains(x.AssignmentId, StringComparer.Ordinal)).Select(x => new { x.AssignmentId, x.MissionId, kind = x.Kind.ToString(), state = x.State.ToString(), x.AssetIds, operatorRef = x.Operator.Key, x.ActualRevenue, settlement = x.ExternalSettlement.ToString(), x.Version }),
            missionChoices = missionChoices.Select(x => new { x.MissionId, displayName = x.Kind + " job — " + x.MissionId + " (" + x.State + ")", x.Kind, x.State }),
            industrial = new { enabled = runtimeSettings.EnableIndustrialPilot, pilotPersonalWagons = ControllableIndustrialPilotWagonIds(snapshot, playerId, false), pilotCompanyWagons = ControllableIndustrialPilotWagonIds(snapshot, playerId, true), stocks = snapshot.IndustrialStocks.Select(x => new { x.FacilityId, x.CargoId, x.OnHand, x.Capacity, x.ReservedOutbound, x.ReservedInbound, x.Version }), recipes = snapshot.IndustrialRecipes.Select(x => new { x.RecipeId, x.FacilityId, x.InputCargoId, x.InputQuantity, x.OutputCargoId, x.OutputQuantity, x.CadenceTicks, x.MaximumBacklogCycles, x.PendingCycles, x.CompletedCycles, x.LastProductionTick, x.Version }), policies = snapshot.IndustrialTransportPolicies.Select(x => new { x.PolicyId, x.OriginFacilityId, x.DestinationFacilityId, x.CargoId, x.BatchQuantity, x.DestinationTargetQuantity, x.BaseReward, x.MaximumScarcityBonus, x.OfferLifetimeTicks, x.PreparationDurationTicks, x.DeliveryDurationTicks, x.PreparationPenalty, requirement = x.WagonRequirement, x.Enabled, x.NextNeedSequence, x.Version }), needs = snapshot.IndustrialTransportNeeds.Select(x => new { x.NeedId, x.PolicyId, x.OriginFacilityId, x.DestinationFacilityId, x.CargoId, x.Quantity, x.BaseReward, x.ScarcityBonus, x.PublishedTick, x.ExpiresTick, x.PreparationDurationTicks, x.DeliveryDurationTicks, x.PreparationPenalty, requirement = x.WagonRequirement, state = x.State.ToString(), x.AcceptedContractId, x.Version }), contracts = snapshot.IndustrialContracts.Where(x => x.State == IndustrialContractState.Offered || visibility.CanView(x.Beneficiary)).Select(x => new { x.ContractId, x.OriginFacilityId, x.DestinationFacilityId, x.CargoId, x.Quantity, x.DeliveredQuantity, x.BaseReward, x.ScarcityBonus, x.PaidAmount, deadlineTick = x.DeliveryDeadlineTick, requirement = x.WagonRequirement, compatiblePersonalWagons = CompatibleIndustrialWagonIds(snapshot, playerId, x, false), compatibleCompanyWagons = CompatibleIndustrialWagonIds(snapshot, playerId, x, true), assignedWagons = x.AssignedWagons.Select(w => new { w.AssetId, w.DefinitionId, w.Capacity, w.LeaseId }), manifests = x.Manifests.Select(m => new { m.AssetId, m.LoadedQuantity, m.UnloadedQuantity, m.LoadOperationIds, m.UnloadOperationIds }), state = x.State.ToString(), x.Version }) },
            outboundLeases = new { enabled = runtimeSettings.EnableOutboundLeasing, simulation = "declared-off-scene", contracts = snapshot.OutboundLeases.Where(x => visibility.CanView(x.Owner) || visibility.CanView(x.Beneficiary)).Select(x => new { x.ContractId, x.AssetIds, owner = x.Owner.Key, beneficiary = x.Beneficiary.Key, x.RentAmount, x.RentIntervalTicks, x.DurationTicks, x.StartTick, x.EndTick, state = x.State.ToString(), x.ConditionAtStart, x.ConditionAtReturn, x.ReturnLocation, x.Version }) },
            passengers = new { enabled = runtimeSettings.EnablePassengerEconomy, routes = snapshot.PassengerRoutes.Select(x => new { routeName = FriendlyLocationName(x.OriginId) + " → " + FriendlyLocationName(x.DestinationId), x.RouteId, originName = FriendlyLocationName(x.OriginId), x.OriginId, destinationName = FriendlyLocationName(x.DestinationId), x.DestinationId, x.DemandUnits, x.MaximumDemandUnits, x.DemandPerInterval, x.DesiredFrequencyTicks, x.BaseFarePerPassenger, x.LatePenaltyPerTick, x.PunctualityBasisPoints, x.Version }), contracts = snapshot.PassengerContracts.Where(x => visibleAssignmentIds.Contains(x.AssignmentId, StringComparer.Ordinal)).Select(x => new { serviceName = "Passenger service " + x.PassengerJobId, x.ContractId, x.RouteId, x.PassengerJobId, x.AssignmentId, x.AssetIds, operatorRef = x.Operator.Key, x.Capacity, x.BookedPassengers, x.MaximumQuotedRevenue, x.ObservedVanillaRevenue, x.PunctualityPenalty, x.PaidRevenue, state = x.State.ToString(), x.Version }) },
            dynamicEconomy = new { enabled = runtimeSettings.EnableDynamicEconomy, metrics = snapshot.DynamicEconomy.Metrics.Select(x => new { x.CategoryId, x.SupplyRatio, x.DemandRatio, x.UtilizationRatio, x.LessorAvailabilityRatio, x.RawFactor, x.SmoothedFactor, x.CalculatedTick, x.Version }), profitability = snapshot.DynamicEconomy.Profitability.Where(x => visibleAssetIds.Contains(x.AssetId, StringComparer.Ordinal)).Select(x => new { x.AssetId, x.OperatingRevenue, x.OperatingCosts, x.NetOperatingResult, x.AcquisitionCash, x.CompletedServices, x.Version }) },
            assetLifecycle = new { enabled = runtimeSettings.EnableAssetLifecycle, cleanupProtectionAdapter = "CarVisitChecker.IsRecentlyVisited-exact-CarGUID-host-only", records = snapshot.AssetLifecycle.Records.Where(x => visibleAssetIds.Contains(x.AssetId, StringComparer.Ordinal)).Select(x => new { x.AssetId, status = x.Status.ToString(), protection = x.ProtectionStatus.ToString(), x.LastKnownMapRevision, x.LastKnownTrackId, x.Detail, x.Version }) },
            worldPopulation = new { configured = runtimeSettings.EnableStrictWorldPopulation, runtimeState = UnityWorldPopulationControl.State.ToString(), UnityWorldPopulationControl.ResultCode, policyVersion = runtimeSettings.WorldPopulationPolicy?.SchemaVersion ?? 0, strict = runtimeSettings.WorldPopulationPolicy?.Strict ?? false },
            financing = new { enabled = runtimeSettings.EnableFinancing, pools = snapshot.Financing.Pools.Select(x => new { x.PoolId, x.AvailableCapital, x.InitialCapital, x.ReceivedPayments, x.WrittenOff, x.Version }), contracts = snapshot.Financing.Contracts.Where(x => visibility.CanView(x.Debtor)).Select(x => new { x.ContractId, kind = x.Kind.ToString(), debtor = x.Debtor.Key, x.PrincipalLimit, x.ReservedCapital, x.OutstandingPrincipal, x.AccruedInterest, x.InterestBasisPoints, x.MinimumInstallment, x.IntervalTicks, x.NextDueTick, x.MaturityTick, x.GuaranteeAmount, x.HeldGuarantee, state = x.State.ToString(), x.Terms, x.Version }) },
            triageAssistance = new { enabled = runtimeSettings.EnableTriageAssistance, executionAdapter = "disabled-until-public-selfshunt-hook-is-proven", plans = snapshot.TriageAssistance.Plans.Where(x => visibleAssignmentIds.Contains(x.AssignmentId, StringComparer.Ordinal)).Select(x => new { x.PlanId, x.AssignmentId, level = x.Level.ToString(), x.AssetIds, x.OrderedTrackIds, state = x.State.ToString(), x.ResultCode, x.Version }) }
        };
        return Newtonsoft.Json.JsonConvert.SerializeObject(payload, webJsonSettings);
    }

    private static IReadOnlyDictionary<string, string> ReadLiveFleetLocations(VehicleAcquisitionSnapshot snapshot)
    {
        var observations = UnityEngine.Object.FindObjectsOfType<TrainCar>()
            .Where(car => car != null)
            .Select(car => new AssetLifecycleObservation
            {
                PersistentCarGuid = car.CarGUID ?? "",
                DefinitionId = car.carLivery?.id ?? "",
                TrackId = car.logicCar?.CurrentTrack?.ID?.ToString()
            }).ToArray();
        return FleetLocationProjection.Resolve(snapshot, observations);
    }

    private sealed class RuntimeMissionChoice
    {
        public string MissionId { get; set; } = "";
        public string Kind { get; set; } = "Freight";
        public string State { get; set; } = "";
    }

    private static IReadOnlyList<RuntimeMissionChoice> LoadedMissionChoices()
    {
        var manager = JobsManager.Instance;
        if (manager == null) return Array.Empty<RuntimeMissionChoice>();
        var current = manager.currentJobs?.Where(value => value != null) ?? Enumerable.Empty<Job>();
        var all = AccessTools.Field(typeof(JobsManager), "allJobs")?.GetValue(manager) as IEnumerable<Job> ?? Enumerable.Empty<Job>();
        var passengerMod = UnityModManager.FindMod("PassengerJobs");
        var passengerBridge = new PassengerJobsRuntimeBridge(passengerMod?.Info?.Version, passengerMod?.Assembly);
        return current.Concat(all).Where(value => value != null && !string.IsNullOrWhiteSpace(value.ID))
            .GroupBy(value => value.ID, StringComparer.Ordinal).Select(group => group.First())
            .Select(value => new RuntimeMissionChoice
            {
                MissionId = value.ID,
                Kind = MissionKind(passengerBridge, value),
                State = value.State.ToString()
            })
            .OrderBy(value => value.Kind, StringComparer.Ordinal).ThenBy(value => value.MissionId, StringComparer.Ordinal).ToArray();
    }

    private static string MissionKind(PassengerJobsRuntimeBridge bridge, Job job)
    {
        if (!bridge.Status.IsAvailable) return "Freight";
        try { return bridge.IsPassengerJob(job) ? "Passenger" : "Freight"; }
        catch { return "Freight"; }
    }

    private static string FriendlyLocationName(string id)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SM"] = "Steel Mill", ["HB"] = "Harbor", ["MF"] = "Machine Factory and Town",
            ["FF"] = "Food Factory and Town", ["GF"] = "Goods Factory and Town", ["FM"] = "Farm",
            ["CM"] = "Coal Mine", ["IMW"] = "Iron Mine West", ["IME"] = "Iron Mine East",
            ["OWN"] = "Oil Well North", ["OWC"] = "Oil Well Central", ["OR"] = "Oil Refinery",
            ["CSW"] = "City South West", ["CW"] = "City West", ["MB"] = "Military Base",
            ["MFMB"] = "Military Fuel Depot", ["FRS"] = "Forest South", ["FRN"] = "Forest North",
            ["SW"] = "Sawmill"
        };
        return names.TryGetValue(id ?? "", out var name) ? name : FriendlyIdentifier(id);
    }

    private static string FriendlyIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Unknown";
        var spaced = System.Text.RegularExpressions.Regex.Replace(value.Replace('_', ' ').Replace('-', ' '), "(?<=[a-z0-9])(?=[A-Z])", " ");
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced.ToLowerInvariant());
    }

    private static IReadOnlyList<string> CompatibleIndustrialWagonIds(VehicleAcquisitionSnapshot snapshot, string playerId, IndustrialContract contract, bool forCompany)
    {
        if (contract.State != IndustrialContractState.Reserved || runtimeRoleDetector == null) return Array.Empty<string>();
        var player = snapshot.Economy.Players.SingleOrDefault(value => value.PlayerId == playerId);
        if (player == null || (forCompany && string.IsNullOrWhiteSpace(player.CompanyId))) return Array.Empty<string>();
        try
        {
            var operatorRef = forCompany ? AssetOwnerRef.Company(player.CompanyId!) : AssetOwnerRef.Player(player.PlayerId);
            return IndustrialEngine(snapshot).FindCompatibleWagons(player.PlayerId, contract.ContractId, operatorRef, snapshot.LeaseClock.ActiveTick)
                .Select(value => value.AssetId).ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    private static AssetOwnerRef ResolveIndustrialOperator(VehicleAcquisitionSnapshot snapshot, PlayerEconomicState player, bool forCompany)
    {
        if (!forCompany) return AssetOwnerRef.Player(player.PlayerId);
        var companyId = player.CompanyId ?? throw new InvalidOperationException("Company membership is required.");
        var company = snapshot.Economy.Companies.Single(value => value.CompanyId == companyId);
        if (company.Liquidating || (company.LeaderId != player.PlayerId && (!company.DelegatedPermissions.TryGetValue(player.PlayerId, out var rights) || !rights.Contains(CompanyPermission.ManageFleet))))
            throw new InvalidOperationException("Company fleet-management permission is required.");
        return AssetOwnerRef.Company(companyId);
    }

    private static IReadOnlyList<string> ControllableIndustrialPilotWagonIds(VehicleAcquisitionSnapshot snapshot, string playerId, bool forCompany)
    {
        var player = snapshot.Economy.Players.SingleOrDefault(value => value.PlayerId == playerId);
        if (player == null) return Array.Empty<string>();
        AssetOwnerRef operatorRef;
        try { operatorRef = ResolveIndustrialOperator(snapshot, player, forCompany); }
        catch { return Array.Empty<string>(); }
        var tick = snapshot.LeaseClock.ActiveTick;
        return snapshot.Fleet.Where(value => value.Kind == FleetVehicleKind.FreightWagon && value.OperationalState == FleetOperationalState.Available)
            .Where(value => snapshot.Ownership.Any(owner => owner.AssetId == value.AssetId && owner.Owner.Key == operatorRef.Key) ||
                snapshot.Leases.Any(lease => lease.AssetIds.Contains(value.AssetId) && lease.Lessee?.Key == operatorRef.Key &&
                    (lease.State == LeaseState.Active || lease.State == LeaseState.Delinquent) && lease.StartTick <= tick && (lease.EndTick <= 0 || tick < lease.EndTick)))
            .Select(value => value.AssetId).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static long ActorWalletBalance(VehicleAcquisitionSnapshot snapshot, string actorId)
        => snapshot.Economy.Wallets.Single(value => value.Account.Key == AccountRef.Player(actorId).Key).Balance;

    private static long SynchronizeRemotePlayerWallet(string actorId, string correlation)
    {
        var adapter = multiplayerWallet ?? throw new InvalidOperationException("The persistent Multiplayer wallet capability is unavailable.");
        var external = adapter.EnsureAndRead(actorId, correlation, runtimeSettings.StartingPersonalBalance);
        var plan = runtimeStateProvider!.PlanExternalWalletMirror(actorId, external, correlation);
        long synchronized;
        switch (plan.Action)
        {
            case ExternalWalletMirrorAction.None:
                synchronized = plan.InternalBalance;
                break;
            case ExternalWalletMirrorAction.ImportExternal:
                var record = runtimeStateProvider.SynchronizeWalletFor("multiplayer-wallet-import:" + correlation, actorId, external, "multiplayer-individual-wallet");
                if (record.State != CommandState.Succeeded) throw new InvalidOperationException(record.ResultCode);
                synchronized = external;
                break;
            case ExternalWalletMirrorAction.CreditExternal:
                synchronized = adapter.Credit(actorId, "wallet-mirror:" + correlation, plan.Amount);
                if (synchronized != plan.InternalBalance) throw new InvalidOperationException("The persistent Multiplayer wallet credit did not reach the authoritative BDVM balance.");
                break;
            case ExternalWalletMirrorAction.DebitExternal:
                if (!adapter.TryDebit(actorId, "wallet-mirror:" + correlation, plan.Amount, out synchronized))
                    throw new InvalidOperationException("The persistent Multiplayer wallet cannot fund the authoritative BDVM balance change.");
                if (synchronized != plan.InternalBalance) throw new InvalidOperationException("The persistent Multiplayer wallet debit did not reach the authoritative BDVM balance.");
                break;
            default:
                throw new InvalidOperationException("The persistent Multiplayer wallet and BDVM wallet both changed since their last synchronized generation; automatic reconciliation is refused.");
        }
        runtimeStateProvider.CompleteExternalWalletMirror(actorId, synchronized, correlation);
        if (plan.Action != ExternalWalletMirrorAction.None)
            mod?.Logger.Log("[correlation=" + correlation + "] [event=multiplayer-wallet-reconciled] actor=" + actorId + ", action=" + plan.Action + ", amount=" + plan.Amount + ", balance=" + synchronized);
        return synchronized;
    }

    private static void RequireRemoteWalletMatch(string actorId, string correlation)
    {
        var synchronized = SynchronizeRemotePlayerWallet(actorId, correlation);
        if (synchronized != ActorWalletBalance(runtimeStateProvider!.Current!, actorId))
            throw new InvalidOperationException("The persistent Multiplayer wallet and BDVM personal wallet diverged.");
    }

    private static void SettleRemoteWalletToInternal(string actorId, string operationId)
    {
        SynchronizeRemotePlayerWallet(actorId, operationId);
    }

    private static IReadOnlyList<string> ConnectedRemotePlayerIds()
    {
        if (configuredServer == null || runtimeStateProvider?.LocalPlayerId == null) return Array.Empty<string>();
        return configuredServer.Players.Select(MultiplayerPlayerIdentityAdapter.RequirePersistentPlayerId)
            .Where(value => !string.Equals(value, runtimeStateProvider.LocalPlayerId, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static long ExactMissionRevenue(VehicleAcquisitionSnapshot snapshot, MissionAssignment assignment, UnityMissionLifecyclePort lifecycle)
    {
        var guids = assignment.AssetIds.Select(assetId => snapshot.Assets.Assets.Single(value => value.AssetId == assetId).GameLink.Value)
            .Select(value => Guid.TryParse(value, out var parsed) && parsed != Guid.Empty ? parsed.ToString("D") : throw new InvalidOperationException("Every assigned asset requires one persistent CarGUID."))
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        var observation = lifecycle.InspectSettlement(assignment.MissionId, guids);
        if (observation.Outcome != WorldOwnershipOutcome.Applied)
            throw new InvalidOperationException(observation.Outcome == WorldOwnershipOutcome.Unknown ? "Mission settlement is not authoritative yet." : "The exact external mission is not completed.");
        if (observation.Revenue < 0 || observation.Revenue > assignment.MaximumExpectedRevenue)
            throw new InvalidOperationException("Observed mission revenue is outside the authorized range.");
        return observation.Revenue;
    }

    private static MissionAssignment CompleteAssignmentAsActor(string correlation, string actorId, bool isLocalActor, string assignmentId, IReadOnlyList<string> arrived)
    {
        var snapshot = runtimeStateProvider!.Current!;
        var lifecycle = new UnityMissionLifecyclePort();
        var commandId = "remote-assignment-complete:" + correlation;
        if (snapshot.AssignmentCommands.Any(value => string.Equals(value.CommandId, commandId, StringComparison.Ordinal)))
            return runtimeStateProvider.CompleteAssignmentFor(commandId, actorId, assignmentId,
                isLocalActor ? hostWallet.ReadBalance() : ActorWalletBalance(snapshot, actorId), arrived, runtimeRoleDetector!, lifecycle,
                isLocalActor ? MissionSettlementMode.ExternalWalletIncludesRevenue : MissionSettlementMode.InternalWalletReceivesRevenue);
        if (isLocalActor)
            return runtimeStateProvider.CompleteAssignmentFor(commandId, actorId, assignmentId, hostWallet.ReadBalance(), arrived, runtimeRoleDetector!, lifecycle);
        var assignment = snapshot.Assignments.Single(value => value.AssignmentId == assignmentId);
        var revenue = ExactMissionRevenue(snapshot, assignment, lifecycle);
        var creditRemotePlayer = assignment.Operator.Kind == AssetOwnerKind.Player;
        if (revenue > 0 && !hostWallet.TryDebit(revenue)) throw new InvalidOperationException("The shared vanilla mission payment could not be removed before routing it to the authoritative beneficiary.");
        var externalCredited = false;
        try
        {
            if (revenue > 0 && creditRemotePlayer)
            {
                multiplayerWallet!.Credit(actorId, "mission-revenue:" + correlation, revenue);
                externalCredited = true;
            }
            var completed = runtimeStateProvider.CompleteAssignmentFor(commandId, actorId, assignmentId,
                ActorWalletBalance(snapshot, actorId), arrived, runtimeRoleDetector!, lifecycle, MissionSettlementMode.InternalWalletReceivesRevenue);
            if (creditRemotePlayer) RequireRemoteWalletMatch(actorId, "mission-revenue-result:" + correlation);
            return completed;
        }
        catch
        {
            if (externalCredited && assignment.State != MissionAssignmentState.Completed &&
                !multiplayerWallet!.TryDebit(actorId, "mission-revenue-compensation:" + correlation, revenue, out _))
                throw new InvalidOperationException("Mission completion failed and its individual-wallet revenue could not be compensated.");
            if (revenue > 0 && assignment.State != MissionAssignmentState.Completed) hostWallet.Credit(revenue);
            throw;
        }
    }

    private static PassengerServiceContract CompletePassengerServiceAsActor(string correlation, string actorId, bool isLocalActor, string contractId, IReadOnlyList<string> arrived)
    {
        var snapshot = runtimeStateProvider!.Current!;
        var contract = snapshot.PassengerContracts.Single(value => value.ContractId == contractId);
        var assignment = snapshot.Assignments.Single(value => value.AssignmentId == contract.AssignmentId);
        var lifecycle = new UnityMissionLifecyclePort();
        var commandId = "remote-passenger-complete:" + correlation;
        if (snapshot.PassengerCommands.Any(value => string.Equals(value.CommandId, commandId, StringComparison.Ordinal)))
            return runtimeStateProvider.CompletePassengerServiceFor(commandId, actorId, contractId,
                isLocalActor ? hostWallet.ReadBalance() : ActorWalletBalance(snapshot, actorId), snapshot.LeaseClock.ActiveTick, arrived,
                runtimeRoleDetector!, lifecycle, isLocalActor ? MissionSettlementMode.ExternalWalletIncludesRevenue : MissionSettlementMode.InternalWalletReceivesRevenue);
        if (isLocalActor)
            return runtimeStateProvider.CompletePassengerServiceFor(commandId, actorId, contractId, hostWallet.ReadBalance(),
                snapshot.LeaseClock.ActiveTick, arrived, runtimeRoleDetector!, lifecycle);
        var revenue = ExactMissionRevenue(snapshot, assignment, lifecycle);
        var creditRemotePlayer = assignment.Operator.Kind == AssetOwnerKind.Player;
        if (revenue > 0 && !hostWallet.TryDebit(revenue)) throw new InvalidOperationException("The shared vanilla passenger payment could not be removed before routing it to the authoritative beneficiary.");
        var externalCredited = false;
        try
        {
            if (revenue > 0 && creditRemotePlayer)
            {
                multiplayerWallet!.Credit(actorId, "passenger-revenue:" + correlation, revenue);
                externalCredited = true;
            }
            var completed = runtimeStateProvider.CompletePassengerServiceFor(commandId, actorId, contractId,
                ActorWalletBalance(snapshot, actorId), snapshot.LeaseClock.ActiveTick, arrived, runtimeRoleDetector!, lifecycle, MissionSettlementMode.InternalWalletReceivesRevenue);
            if (creditRemotePlayer) RequireRemoteWalletMatch(actorId, "passenger-revenue-result:" + correlation);
            return completed;
        }
        catch
        {
            if (externalCredited && contract.State != PassengerContractState.Completed &&
                !multiplayerWallet!.TryDebit(actorId, "passenger-revenue-compensation:" + correlation, revenue, out _))
                throw new InvalidOperationException("Passenger completion failed and its individual-wallet revenue could not be compensated.");
            if (revenue > 0 && contract.State != PassengerContractState.Completed) hostWallet.Credit(revenue);
            throw;
        }
    }

    private static bool IsIndustrialAdministrationOperation(string operation)
        => operation == "configure-pilot" || operation == "configure-stock" || operation == "configure-recipe" || operation == "configure-policy" ||
            operation == "publish-need" || operation == "create" || operation == "advance-production" || operation == "expire";

    private static void RequireIndustrialContractControl(VehicleAcquisitionSnapshot snapshot, PlayerEconomicState player, IndustrialContract contract)
    {
        if (contract.Beneficiary.Kind == AccountKind.Player)
        {
            if (!string.Equals(contract.Beneficiary.OwnerId, player.PlayerId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The industrial contract belongs to another player.");
            return;
        }
        if (contract.Beneficiary.Kind != AccountKind.Company || ResolveIndustrialOperator(snapshot, player, true).Key != AssetOwnerRef.Company(contract.Beneficiary.OwnerId).Key)
            throw new UnauthorizedAccessException("Company fleet-management permission is required for this industrial contract.");
    }

    private static void RequireMarketPurchaseReplayMatch(MarketPurchaseRecord record, string actorId, string listingId, bool forCompany)
    {
        if (!string.Equals(record.RequesterId, actorId, StringComparison.Ordinal) ||
            !string.Equals(record.ListingId, listingId, StringComparison.Ordinal))
            throw new InvalidOperationException("Market purchase command ID payload conflict.");
        if (forCompany)
        {
            if (record.Payer.Kind != AccountKind.Company || record.Buyer.Kind != AssetOwnerKind.Company ||
                string.IsNullOrWhiteSpace(record.Payer.OwnerId) ||
                !string.Equals(record.Payer.OwnerId, record.Buyer.OwnerId, StringComparison.Ordinal))
                throw new InvalidOperationException("Market purchase command ID payload conflict.");
        }
        else if (record.Payer.Kind != AccountKind.Player || record.Buyer.Kind != AssetOwnerKind.Player ||
                 !string.Equals(record.Payer.OwnerId, actorId, StringComparison.Ordinal) ||
                 !string.Equals(record.Buyer.OwnerId, actorId, StringComparison.Ordinal))
            throw new InvalidOperationException("Market purchase command ID payload conflict.");
    }

    private static void RequireResaleReplayMatch(VehicleResaleRecord record, string actorId, string quoteId)
    {
        if (!string.Equals(record.RequesterId, actorId, StringComparison.Ordinal) ||
            !string.Equals(record.QuoteId, quoteId, StringComparison.Ordinal))
            throw new InvalidOperationException("Fleet resale command ID payload conflict.");
    }

    private static string HandleRemoteDispatchIntent(string transportIdentity, string payload)
        => HandleRemoteDispatchIntent(transportIdentity, payload, false);

    private static string HandleRemoteDispatchIntent(string transportIdentity, string payload, bool isLoopbackRequest)
    {
        if (runtimeRoleDetector?.Detect().Role == NetworkRole.MultiplayerClient)
            return SubmitClientModuleIntent(payload);
        RequireHostAuthority(); if (payload.Length > 4096) throw new ArgumentException("BDVM intent payload is too large."); var body = Newtonsoft.Json.Linq.JObject.Parse(payload); var action = (string?)body["action"] ?? ""; var correlation = (string?)body["correlationId"] ?? Guid.NewGuid().ToString("N"); if (correlation.Length > 96) throw new ArgumentException("Correlation ID is too long.");
        var actorId = ResolveAuthenticatedRuntimeActor(transportIdentity, runtimeStateProvider?.Current ?? throw new InvalidOperationException("BDVM career state is unavailable."), isLoopbackRequest);
        var isLocalActor = string.Equals(actorId, runtimeStateProvider!.LocalPlayerId, StringComparison.Ordinal);
        if (!isLocalActor) SynchronizeRemotePlayerWallet(actorId, "intent:" + correlation);
        if (!isLocalActor && !IsActorAwareRemoteAction(action))
            throw new UnauthorizedAccessException("This operation is not yet available for a non-host player identity.");
        object result; var saveStaged = false;
        if (action == "fleet.set-state")
        {
            var assetId = (string?)body["assetId"] ?? ""; if (!Enum.TryParse((string?)body["state"], true, out FleetOperationalState target)) throw new ArgumentException("Invalid fleet state.");
            var record = runtimeStateProvider!.ManageFleetFor("remote-fleet:" + correlation, actorId, assetId, FleetCommandAction.SetOperationalState, runtimeRoleDetector!, target); result = new { action, record.Outcome, record.ResultCode, record.AssetId, record.FleetVersionAfter };
        }
        else if (action == "fleet.rename")
        {
            var assetId = (string?)body["assetId"] ?? ""; var displayName = (string?)body["displayName"] ?? "";
            var record = runtimeStateProvider!.ManageFleetFor("remote-fleet-rename:" + correlation, actorId, assetId, FleetCommandAction.Rename, runtimeRoleDetector!, displayName: displayName); result = new { action, record.Outcome, record.ResultCode, record.AssetId, record.FleetVersionAfter };
        }
        else if (action == "company.create")
        {
            var name = (string?)body["name"] ?? "";
            var record = runtimeStateProvider!.CreateCompanyFor("remote-company-create:" + correlation, actorId, name); result = new { action, record.State, record.ResultCode, record.CompanyId };
        }
        else if (action == "company.apply")
        {
            var companyId = (string?)body["companyId"] ?? ""; var record = runtimeStateProvider!.ApplyToCompanyFor("remote-company-apply:" + correlation, actorId, companyId, runtimeRoleDetector!); result = new { action, record.State, record.ResultCode, record.CompanyId };
        }
        else if (action == "company.invite")
        {
            var companyId = (string?)body["companyId"] ?? ""; var targetPlayerId = (string?)body["targetPlayerId"] ?? ""; var record = runtimeStateProvider!.InvitePlayerFor("remote-company-invite:" + correlation, actorId, companyId, targetPlayerId, runtimeRoleDetector!); result = new { action, record.State, record.ResultCode, record.CompanyId };
        }
        else if (action == "company.decide-application")
        {
            var requestId = (string?)body["requestId"] ?? ""; var accept = (bool?)body["accept"] ?? false; var record = runtimeStateProvider!.DecideApplicationFor("remote-company-decision:" + correlation, actorId, requestId, accept, runtimeRoleDetector!); result = new { action, record.State, record.ResultCode, record.CompanyId };
        }
        else if (action == "company.respond-invitation")
        {
            var requestId = (string?)body["requestId"] ?? ""; var accept = (bool?)body["accept"] ?? false; var record = runtimeStateProvider!.RespondToInvitationFor("remote-company-response:" + correlation, actorId, requestId, accept, runtimeRoleDetector!); result = new { action, record.State, record.ResultCode, record.CompanyId };
        }
        else if (action == "company.leave")
        {
            var record = runtimeStateProvider!.LeaveCompanyFor("remote-company-leave:" + correlation, actorId, runtimeRoleDetector!); result = new { action, record.State, record.ResultCode, record.CompanyId };
        }
        else if (action == "company.policy")
        {
            var companyId = (string?)body["companyId"] ?? ""; if (!Enum.TryParse((string?)body["policy"], true, out MembershipPolicy policy)) throw new ArgumentException("Invalid membership policy.");
            var record = runtimeStateProvider!.SetMembershipPolicyFor("remote-company-policy:" + correlation, actorId, companyId, policy, runtimeRoleDetector!); result = new { action, record.State, record.ResultCode, record.CompanyId };
        }
        else if (action == "company.permission")
        {
            var companyId = (string?)body["companyId"] ?? ""; var memberId = (string?)body["memberId"] ?? ""; var enabled = (bool?)body["enabled"] ?? false; if (!Enum.TryParse((string?)body["permission"], true, out CompanyPermission permission)) throw new ArgumentException("Invalid company permission.");
            var record = runtimeStateProvider!.SetPermissionFor("remote-company-permission:" + correlation, actorId, companyId, memberId, permission, enabled, runtimeRoleDetector!); result = new { action, record.State, record.ResultCode, record.CompanyId };
        }
        else if (action == "company.transfer-leadership")
        {
            var companyId = (string?)body["companyId"] ?? ""; var memberId = (string?)body["memberId"] ?? ""; var record = runtimeStateProvider!.TransferLeadershipFor("remote-company-leadership:" + correlation, actorId, companyId, memberId, runtimeRoleDetector!); result = new { action, record.State, record.ResultCode, record.CompanyId };
        }
        else if (action == "wallet.transfer")
        {
            var amount = (long?)body["amount"] ?? 0; var toCompany = (bool?)body["toCompany"] ?? false; if (amount <= 0) throw new ArgumentException("Transfer amount must be a positive whole number.");
            if (!isLocalActor)
            {
                var externalDebited = false; var externalCredited = false; var domainCommitted = false;
                try
                {
                    if (toCompany)
                    {
                        if (!multiplayerWallet!.TryDebit(actorId, "company-transfer:" + correlation, amount, out _))
                            throw new InvalidOperationException("The authenticated player's personal wallet has insufficient funds.");
                        externalDebited = true;
                    }
                    else
                    {
                        multiplayerWallet!.Credit(actorId, "company-transfer:" + correlation, amount);
                        externalCredited = true;
                    }
                    var remoteRecord = runtimeStateProvider!.TransferCompanyFor("remote-company-transfer:" + correlation, actorId, amount, toCompany);
                    if (remoteRecord.State != CommandState.Succeeded) throw new InvalidOperationException(remoteRecord.ResultCode);
                    domainCommitted = true;
                    RequireRemoteWalletMatch(actorId, "company-transfer-result:" + correlation);
                    result = new { action, remoteRecord.State, remoteRecord.ResultCode, amount, toCompany };
                }
                catch
                {
                    if (!domainCommitted && externalDebited) multiplayerWallet!.Credit(actorId, "company-transfer-compensation:" + correlation, amount);
                    if (!domainCommitted && externalCredited && !multiplayerWallet!.TryDebit(actorId, "company-transfer-compensation:" + correlation, amount, out _))
                        throw new InvalidOperationException("Company transfer failed and its individual-wallet credit could not be compensated.");
                    throw;
                }
            }
            else
            {
            TrySynchronizeHostWallet(mod!, "before-remote-company-transfer"); var externalChanged = false;
            try
            {
                if (toCompany) { if (!hostWallet.TryDebit(amount)) throw new InvalidOperationException("The authoritative personal wallet has insufficient funds."); externalChanged = true; }
                var record = runtimeStateProvider!.TransferLocalCompany("remote-company-transfer:" + correlation, amount, toCompany);
                if (record.State != CommandState.Succeeded) throw new InvalidOperationException(record.ResultCode);
                if (!toCompany) { hostWallet.Credit(amount); externalChanged = true; }
                if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance))
                {
                    var compensation = runtimeStateProvider.TransferLocalCompany("remote-company-transfer-compensation:" + correlation, amount, !toCompany);
                    if (compensation.State != CommandState.Succeeded) throw new InvalidOperationException("Save staging and transfer compensation both failed.");
                    if (toCompany) hostWallet.Credit(amount);
                    else if (!hostWallet.TryDebit(amount)) throw new InvalidOperationException("Save staging failed and the vanilla withdrawal could not be reversed.");
                    externalChanged = false;
                    throw new InvalidOperationException("Save staging failed; transfer was compensated.");
                }
                saveStaged = true;
                result = new { action, record.State, record.ResultCode, amount, toCompany };
            }
            catch
            {
                if (externalChanged)
                {
                    if (toCompany) hostWallet.Credit(amount);
                    else if (!hostWallet.TryDebit(amount)) mod?.Logger.Error("[correlation=" + correlation + "] [event=remote-company-transfer-compensation-failed] amount=" + amount);
                }
                TrySynchronizeHostWallet(mod!, "after-remote-company-transfer-refusal"); throw;
            }
            }
        }
        else if (action == "market.configure")
        {
            if (!isLocalActor) throw new UnauthorizedAccessException("Finite catalog configuration is restricted to the host player.");
            var definitionId = ((string?)body["definitionId"] ?? "").Trim();
            var locationId = ((string?)body["locationId"] ?? "").Trim();
            var basePrice = (long?)body["basePrice"] ?? 0;
            var transferFee = (long?)body["transferFee"] ?? -1;
            var buybackRate = (decimal?)body["buybackRate"] ?? 0m;
            var initialStock = (int?)body["initialStock"] ?? -1;
            var definitions = new UnityVehicleDefinitionReader().ReadLoadedDefinitions("management-catalog-configure")
                .Where(value => value.Resolution == ResolutionState.Resolved && string.Equals(value.ExistingDefinitionId, definitionId, StringComparison.Ordinal)).ToArray();
            if (definitions.Length != 1) throw new InvalidOperationException("The selected rolling-stock model must resolve to exactly one loaded definition.");
            var track = LeaseReturnTrackRules(runtimeStateProvider!.Current!).SingleOrDefault(value => string.Equals(value.TrackId, locationId, StringComparison.Ordinal));
            if (track == null) throw new InvalidOperationException("Catalog stock must use a configured depot or service track.");
            if (basePrice <= 0 || transferFee < 0 || buybackRate <= 0m || buybackRate >= 1m || initialStock < 0)
                throw new ArgumentException("Catalog price, transfer fee, buyback rate and stock are invalid.");
            var definition = definitions[0];
            var categoryId = FleetVehicleClassifier.Classify(definition.Type, definition.ExistingDefinitionId).ToString();
            var entry = runtimeStateProvider.ConfigureFiniteMarketDefinition(definitionId, categoryId, basePrice, transferFee, 0.8m, 1.2m, buybackRate, locationId, initialStock);
            result = new { action, entry.DefinitionId, entry.CategoryId, entry.BasePrice, entry.TransferFee, entry.BuybackRate, locationId, initialStock, entry.Version };
        }
        else if (action == "market.generate-order")
        {
            if (!isLocalActor) throw new UnauthorizedAccessException("Finite catalog offer publication is restricted to the host player.");
            var definitionId = ((string?)body["definitionId"] ?? "").Trim();
            var locationId = ((string?)body["locationId"] ?? "").Trim();
            var listing = runtimeStateProvider!.GenerateFiniteMarketOrder("remote-market-order:" + correlation, definitionId, locationId, runtimeRoleDetector!, new UnityExistingVehicleOwnershipAdapter());
            result = new { action, listing.ListingId, listing.DefinitionId, listing.LocationId, listing.Price, state = listing.State.ToString(), listing.Version };
        }
        else if (action == "market.purchase")
        {
            var listingId = (string?)body["listingId"] ?? ""; var forCompany = (bool?)body["forCompany"] ?? false; var commandId = "remote-market-purchase:" + correlation;
            var previous = runtimeStateProvider!.Current!.Market.Purchases.SingleOrDefault(value => value.CommandId == commandId);
            if (previous != null)
            {
                RequireMarketPurchaseReplayMatch(previous, actorId, listingId, forCompany);
                if (!isLocalActor && !forCompany) RequireRemoteWalletMatch(actorId, "market-purchase-result:" + correlation);
                result = new { action, previous.State, previous.ResultCode, previous.ListingId, previous.AssetId, previous.DeliveryOperationId };
            }
            else
            {
                var listing = runtimeStateProvider.Current.Market.Listings.Single(x => x.ListingId == listingId); var externalDebited = false;
                if (isLocalActor && !forCompany && listing.Price > 0) { TrySynchronizeHostWallet(mod!, "before-remote-market-purchase"); if (!hostWallet.TryDebit(listing.Price)) throw new InvalidOperationException("The authoritative personal wallet has insufficient funds."); externalDebited = true; }
                if (!isLocalActor && !forCompany && listing.Price > 0)
                {
                    if (!multiplayerWallet!.TryDebit(actorId, "market-purchase:" + correlation, listing.Price, out _))
                        throw new InvalidOperationException("The authenticated player's personal wallet has insufficient funds.");
                    externalDebited = true;
                }
                MarketPurchaseRecord record; var domainCommitted = false;
                try
                {
                    record = runtimeStateProvider.PurchaseMarketFor(commandId, actorId, listingId, forCompany, runtimeRoleDetector!, new UnityExistingVehicleOwnershipAdapter(), new DisabledMarketDeliveryPort());
                    domainCommitted = record.State == MarketPurchaseState.Succeeded || record.State == MarketPurchaseState.ReconcileRequired;
                    if (record.State == MarketPurchaseState.Rejected || record.State == MarketPurchaseState.Compensated)
                    {
                        if (externalDebited)
                        {
                            if (isLocalActor) hostWallet.Credit(listing.Price);
                            else multiplayerWallet!.Credit(actorId, "market-purchase-compensation:" + correlation, listing.Price);
                            externalDebited = false;
                        }
                    }
                    else if (!isLocalActor && !forCompany) RequireRemoteWalletMatch(actorId, "market-purchase-result:" + correlation);
                }
                catch
                {
                    if (externalDebited && !domainCommitted)
                    {
                        if (isLocalActor) hostWallet.Credit(listing.Price);
                        else multiplayerWallet!.Credit(actorId, "market-purchase-compensation:" + correlation, listing.Price);
                    }
                    throw;
                }
                result = new { action, record.State, record.ResultCode, record.ListingId, record.AssetId, record.DeliveryOperationId };
            }
        }
        else if (action == "initial-delivery.place")
        {
            var grantId = (string?)body["grantId"] ?? ""; var trackId = (string?)body["trackId"] ?? ""; if (!Enum.TryParse((string?)body["targetKind"], true, out InitialDeliveryTargetKind targetKind)) throw new ArgumentException("Invalid initial delivery target kind.");
            var snapshot = runtimeStateProvider!.Current!;
            var record = runtimeStateProvider.PlaceInitialDeliveryFor("remote-initial-delivery:" + correlation, actorId, grantId, trackId, targetKind, runtimeRoleDetector!, new UnityInitialDeliveryAdapter(LeaseReturnTrackRules(snapshot), snapshot: snapshot), new SaveGameInitialDeliveryCheckpointPort(mod!)); result = new { action, record.State, record.ResultCode, record.GrantId, record.AssetIds, record.TargetTrackId };
        }
        else if (action == "assignment.cancel")
        {
            var assignmentId = (string?)body["assignmentId"] ?? ""; var record = runtimeStateProvider!.CancelAssignmentFor("remote-assignment-cancel:" + correlation, actorId, assignmentId, runtimeRoleDetector!, new UnityMissionLifecyclePort()); result = new { action, record.AssignmentId, state = record.State.ToString(), record.ResultCode };
        }
        else if (action == "company.dissolve")
        {
            var player = runtimeStateProvider!.Current!.Economy.Players.Single(x => x.PlayerId == actorId); var companyId = (string?)body["companyId"] ?? player.CompanyId ?? "";
            var debts = runtimeStateProvider.Current.Financing.Contracts.Where(x => x.Debtor.Key == AccountRef.Company(companyId).Key && x.State != FinancingState.Settled && x.State != FinancingState.Cancelled && x.State != FinancingState.WrittenOff).Sum(x => checked(x.OutstandingPrincipal + x.AccruedInterest));
            var guard = new UnityAssetReleaseGuard(); var record = runtimeStateProvider.DissolveCompanyFor("remote-company-dissolve:" + correlation, player.PlayerId, companyId, debts, 0, runtimeRoleDetector!, guard, new UnityExistingVehicleOwnershipAdapter(), new CompositeCompanyContractCancellationPort(new CompanyWorkflowCancellationPort(runtimeStateProvider.Current, runtimeRoleDetector!), new OutboundLeaseCompanyContractCancellationPort(runtimeStateProvider.Current, runtimeRoleDetector!, guard, new DeclaredOffSceneLeaseSimulationPort()), new FinancingCompanyContractCancellationPort(runtimeStateProvider.Current, runtimeRoleDetector!)), new SaveGameLiquidationCheckpointPort(mod!));
            result = new { action, record.CommandId, record.CompanyId, state = record.State.ToString(), record.ResultCode, liabilities = debts };
        }
        else if (action == "fleet.bundle")
        {
            var ids = body["assetIds"]?.Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToArray() ?? Array.Empty<string>(); var bundle = runtimeStateProvider!.CreateBundleFor("remote-fleet-bundle:" + correlation, actorId, ids, runtimeRoleDetector!); result = new { action, bundle.BundleId, bundle.ComponentAssetIds };
        }
        else if (action == "fleet.resale")
        {
            var operation = (string?)body["operation"] ?? "prepare"; var quoteId = (string?)body["quoteId"] ?? "resale-quote:" + correlation;
            if (operation == "prepare") { var assetId = (string?)body["assetId"] ?? ""; var proceeds = (long?)body["proceeds"] ?? 0; var quote = runtimeStateProvider!.PrepareResaleQuoteFor(quoteId, actorId, assetId, proceeds, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter()); result = new { action, operation, quote.QuoteId, quote.AssetId, quote.Proceeds, quote.Version }; }
            else if (operation == "sell")
            {
                var commandId = "remote-fleet-resale:" + correlation;
                var previous = runtimeStateProvider!.Current!.Resales.SingleOrDefault(value => value.CommandId == commandId);
                if (previous != null)
                {
                    RequireResaleReplayMatch(previous, actorId, quoteId);
                    var previousQuote = runtimeStateProvider.Current.ResaleQuotes.Single(value => value.QuoteId == quoteId);
                    if (!isLocalActor && previousQuote.Payee.Kind == AccountKind.Player) RequireRemoteWalletMatch(actorId, "fleet-resale-result:" + correlation);
                    result = new { action, operation, previous.CommandId, previous.QuoteId, state = previous.State.ToString(), previous.ResultCode, previous.Proceeds };
                }
                else
                {
                    var quote = runtimeStateProvider.Current.ResaleQuotes.Single(value => value.QuoteId == quoteId); var externalCredited = false;
                    try
                    {
                        if (isLocalActor && quote.Payee.Kind == AccountKind.Player) TrySynchronizeHostWallet(mod!, "before-remote-fleet-resale");
                        if (!isLocalActor && quote.Payee.Kind == AccountKind.Player && quote.Proceeds > 0)
                        {
                            multiplayerWallet!.Credit(actorId, "fleet-resale:" + correlation, quote.Proceeds);
                            externalCredited = true;
                        }
                        var sale = runtimeStateProvider.SellVehicleFor(commandId, actorId, quoteId, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter());
                        if (sale.State != ResaleState.Succeeded) throw new InvalidOperationException(sale.ResultCode);
                        if (isLocalActor && quote.Payee.Kind == AccountKind.Player && sale.Proceeds > 0) hostWallet.Credit(sale.Proceeds);
                        if (!isLocalActor && quote.Payee.Kind == AccountKind.Player) RequireRemoteWalletMatch(actorId, "fleet-resale-result:" + correlation);
                        result = new { action, operation, sale.CommandId, sale.QuoteId, state = sale.State.ToString(), sale.ResultCode, sale.Proceeds };
                    }
                    catch
                    {
                        var committed = runtimeStateProvider.Current!.Resales.Any(value => value.CommandId == commandId && value.State == ResaleState.Succeeded);
                        if (externalCredited && !committed && !multiplayerWallet!.TryDebit(actorId, "fleet-resale-compensation:" + correlation, quote.Proceeds, out _))
                            throw new InvalidOperationException("Fleet resale failed and its individual-wallet proceeds could not be compensated.");
                        throw;
                    }
                }
            }
            else throw new ArgumentException("Unsupported resale operation.");
        }
        else if (action == "fleet.maintenance")
        {
            var operation = (string?)body["operation"] ?? "begin"; var sessionId = (string?)body["sessionId"] ?? "maintenance:" + correlation;
            if (operation == "begin")
            {
                if (!Enum.TryParse((string?)body["maintenanceAction"], true, out MaintenanceAction maintenance)) throw new ArgumentException("Invalid maintenance action.");
                if (isLocalActor) TrySynchronizeHostWallet(mod!, "before-remote-maintenance");
                var assetId = (string?)body["assetId"] ?? ""; var condition = new UnityVehicleConditionReader(runtimeStateProvider!.Current!).Read(assetId);
                var forCompany = (bool?)body["forCompany"] ?? false; var maximum = (long?)body["maximumCost"] ?? 0; var reservedExternally = false;
                try
                {
                    if (!isLocalActor && !forCompany && maximum > 0)
                    {
                        if (!multiplayerWallet!.TryDebit(actorId, "maintenance-reserve:" + sessionId, maximum, out _))
                            throw new InvalidOperationException("The authenticated player's personal wallet cannot reserve the maximum maintenance cost.");
                        reservedExternally = true;
                    }
                    var mode = isLocalActor ? OperatingCostSettlementMode.PersonalExternalWallet : OperatingCostSettlementMode.SharedExternalWallet;
                    var record = runtimeStateProvider.BeginOperatingCostFor(sessionId, actorId, assetId, maintenance, forCompany, maximum, hostWallet.ReadBalance(), condition, (string?)body["tripId"], runtimeRoleDetector!, mode);
                    if (!isLocalActor && !forCompany) RequireRemoteWalletMatch(actorId, "maintenance-reserve-result:" + sessionId);
                    result = new { action, operation, record.SessionId, state = record.State.ToString(), record.ResultCode, authoritativeCondition = condition, settlementMode = record.SettlementMode.ToString() };
                }
                catch
                {
                    if (reservedExternally && !runtimeStateProvider.Current!.OperatingCosts.Any(value => value.SessionId == sessionId))
                        multiplayerWallet!.Credit(actorId, "maintenance-reserve-compensation:" + sessionId, maximum);
                    throw;
                }
            }
            else if (operation == "complete")
            {
                var open = runtimeStateProvider!.Current!.OperatingCosts.Single(value => value.SessionId == sessionId); var condition = new UnityVehicleConditionReader(runtimeStateProvider.Current).Read(open.AssetId);
                var record = runtimeStateProvider.CompleteOperatingCostFor(actorId, sessionId, hostWallet.ReadBalance(), condition, runtimeRoleDetector!);
                if (record.ExternalSettlement == ExternalSettlementState.Pending)
                {
                    if (hostWallet.ReadBalance() != record.VanillaBalanceAfter) throw new InvalidOperationException("Vanilla wallet changed before operating-cost reimbursement.");
                    hostWallet.Credit(record.ExternalReimbursement);
                    record = runtimeStateProvider.MarkOperatingCostExternalSettlementFor(actorId, sessionId, hostWallet.ReadBalance(), runtimeRoleDetector!);
                }
                if (!isLocalActor && record.Payer.Kind == AccountKind.Player) SettleRemoteWalletToInternal(actorId, "maintenance-complete:" + sessionId);
                result = new { action, operation, record.SessionId, state = record.State.ToString(), record.ResultCode, record.ActualCost, record.SubsidizedExcess, authoritativeCondition = condition, externalSettlement = record.ExternalSettlement.ToString(), settlementMode = record.SettlementMode.ToString() };
            }
            else if (operation == "cancel")
            {
                var open = runtimeStateProvider!.Current!.OperatingCosts.Single(value => value.SessionId == sessionId);
                var record = runtimeStateProvider.CancelOperatingCostFor(actorId, sessionId, hostWallet.ReadBalance(), runtimeRoleDetector!);
                if (!isLocalActor && open.Payer.Kind == AccountKind.Player) SettleRemoteWalletToInternal(actorId, "maintenance-cancel:" + sessionId);
                result = new { action, operation, record.SessionId, state = record.State.ToString(), record.ResultCode };
            }
            else throw new ArgumentException("Unsupported maintenance operation.");
        }
        else if (action == "lease.manage")
        {
            var operation = (string?)body["operation"] ?? ""; var leaseId = (string?)body["leaseId"] ?? "";
            if (operation == "create-existing" || operation == "create-catalog" || operation == "create-catalog-listings")
            {
                if (!isLocalActor) throw new UnauthorizedAccessException("Only the host may publish inbound lease offers.");
                var ids = body[operation == "create-existing" ? "assetIds" : operation == "create-catalog" ? "definitionIds" : "listingIds"]?.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray() ?? Array.Empty<string>();
                var deposit = (long?)body["deposit"] ?? 0; var initialFee = (long?)body["initialFee"] ?? 0; var rent = (long?)body["rent"] ?? 0; var interval = (long?)body["intervalTicks"] ?? 1; var duration = (long?)body["durationTicks"] ?? 1; var option = (long?)body["purchaseOptionPrice"]; var condition = (decimal?)body["conditionAtStart"] ?? 1m; var maximumDamage = (long?)body["maximumDamageCharge"] ?? 0;
                var created = operation == "create-existing"
                    ? runtimeStateProvider!.CreateLocalLeaseOffer(string.IsNullOrWhiteSpace(leaseId) ? "lease:" + correlation : leaseId, ids, deposit, initialFee, rent, interval, duration, option, condition, maximumDamage, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter())
                    : operation == "create-catalog-listings"
                        ? runtimeStateProvider!.CreateLocalCatalogListingLeaseOffer(string.IsNullOrWhiteSpace(leaseId) ? "lease:" + correlation : leaseId, ids, deposit, initialFee, rent, interval, duration, option, condition, maximumDamage, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter())
                        : runtimeStateProvider!.CreateLocalCatalogLeaseOffer(string.IsNullOrWhiteSpace(leaseId) ? "lease:" + correlation : leaseId, ids, deposit, initialFee, rent, interval, duration, option, condition, maximumDamage, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter());
                result = new { action, operation, created.LeaseId, state = created.State.ToString(), created.AssetIds, created.Version };
            }
            else if (operation == "accept")
            {
                var forCompany = (bool?)body["forCompany"] ?? false;
                var commandId = "remote-lease-accept:" + correlation;
                var lease = runtimeStateProvider!.Current!.Leases.Single(value => value.LeaseId == leaseId);
                var previous = runtimeStateProvider.Current.LeaseActions.SingleOrDefault(value => value.CommandId == commandId);
                if (previous != null)
                {
                    var replay = runtimeStateProvider.AcceptLeaseFor(commandId, actorId, leaseId, forCompany, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter());
                    if (replay.State != LeaseActionState.Succeeded) throw new InvalidOperationException(replay.ResultCode);
                    if (!isLocalActor && !forCompany) RequireRemoteWalletMatch(actorId, "lease-accept-result:" + correlation);
                    result = new { action, operation, replay.LeaseId, state = replay.State.ToString(), replay.ResultCode, initialDeliveryGrantIds = runtimeStateProvider.Current.InitialDeliveries.Where(value => value.SourceCommandId == commandId + ":delivery").Select(value => value.GrantId).ToArray() };
                }
                else
                {
                    var due = checked(lease.Deposit + lease.InitialFee);
                    var debited = false;
                    try
                    {
                        if (!forCompany && due > 0)
                        {
                            if (isLocalActor)
                            {
                                TrySynchronizeHostWallet(mod!, "before-remote-lease-accept");
                                if (!hostWallet.TryDebit(due)) throw new InvalidOperationException("The authoritative personal wallet has insufficient funds.");
                            }
                            else if (!multiplayerWallet!.TryDebit(actorId, "lease-accept:" + correlation, due, out _))
                                throw new InvalidOperationException("The authenticated player's personal wallet has insufficient funds.");
                            debited = true;
                        }
                        var record = runtimeStateProvider.AcceptLeaseFor(commandId, actorId, leaseId, forCompany, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter());
                        if (record.State != LeaseActionState.Succeeded) throw new InvalidOperationException(record.ResultCode);
                        if (!isLocalActor && !forCompany) RequireRemoteWalletMatch(actorId, "lease-accept-result:" + correlation);
                        result = new { action, operation, record.LeaseId, state = record.State.ToString(), record.ResultCode, initialDeliveryGrantIds = runtimeStateProvider.Current.InitialDeliveries.Where(value => value.SourceCommandId == commandId + ":delivery").Select(value => value.GrantId).ToArray() };
                    }
                    catch
                    {
                        if (debited && lease.State == LeaseState.Offered)
                        {
                            if (isLocalActor) hostWallet.Credit(due);
                            else multiplayerWallet!.Credit(actorId, "lease-accept-compensation:" + correlation, due);
                        }
                        throw;
                    }
                }
            }
            else if (operation == "return")
            {
                var commandId = "remote-lease-return:" + correlation;
                var lease = runtimeStateProvider!.Current!.Leases.Single(value => value.LeaseId == leaseId); var condition = ReadMinimumVehicleCondition(runtimeStateProvider.Current, lease.AssetIds);
                var previous = runtimeStateProvider.Current.LeaseActions.SingleOrDefault(value => value.CommandId == commandId);
                if (previous != null)
                {
                    var replay = runtimeStateProvider.ReturnLeaseFor(commandId, actorId, leaseId, condition, runtimeRoleDetector!, new UnityLeaseReturnGuard(LeaseReturnTrackRules(runtimeStateProvider.Current)), new UnityExistingVehicleOwnershipAdapter());
                    if (replay.State != LeaseActionState.Succeeded) throw new InvalidOperationException(replay.ResultCode);
                    if (!isLocalActor && lease.Payer?.Kind == AccountKind.Player) RequireRemoteWalletMatch(actorId, "lease-return-result:" + correlation);
                    result = new { action, operation, replay.LeaseId, state = replay.State.ToString(), replay.ResultCode, authoritativeCondition = condition, refund = replay.Amount };
                }
                else
                {
                    var damage = checked(decimal.ToInt64(decimal.Floor(Math.Max(0m, lease.ConditionAtStart - condition) * lease.MaximumDamageCharge)));
                    var expectedRefund = lease.HeldDeposit - Math.Min(lease.HeldDeposit, checked(lease.OutstandingDebt + damage)); var precredited = false;
                    try
                    {
                        if (lease.Payer?.Kind == AccountKind.Player && expectedRefund > 0)
                        {
                            if (isLocalActor) hostWallet.Credit(expectedRefund);
                            else multiplayerWallet!.Credit(actorId, "lease-return:" + correlation, expectedRefund);
                            precredited = true;
                        }
                        var record = runtimeStateProvider.ReturnLeaseFor(commandId, actorId, leaseId, condition, runtimeRoleDetector!, new UnityLeaseReturnGuard(LeaseReturnTrackRules(runtimeStateProvider.Current)), new UnityExistingVehicleOwnershipAdapter());
                        if (record.State != LeaseActionState.Succeeded || record.Amount != expectedRefund) throw new InvalidOperationException(record.ResultCode);
                        if (!isLocalActor && lease.Payer?.Kind == AccountKind.Player) RequireRemoteWalletMatch(actorId, "lease-return-result:" + correlation);
                        result = new { action, operation, record.LeaseId, state = record.State.ToString(), record.ResultCode, authoritativeCondition = condition, refund = record.Amount };
                    }
                    catch
                    {
                        if (precredited && lease.State != LeaseState.Returned)
                        {
                            if (isLocalActor && !hostWallet.TryDebit(expectedRefund)) throw new InvalidOperationException("Lease return failed and its vanilla refund could not be compensated.");
                            if (!isLocalActor && !multiplayerWallet!.TryDebit(actorId, "lease-return-compensation:" + correlation, expectedRefund, out _)) throw new InvalidOperationException("Lease return failed and its individual-wallet refund could not be compensated.");
                        }
                        throw;
                    }
                }
            }
            else if (operation == "purchase")
            {
                var commandId = "remote-lease-purchase:" + correlation;
                var lease = runtimeStateProvider!.Current!.Leases.Single(value => value.LeaseId == leaseId);
                var previous = runtimeStateProvider.Current.LeaseActions.SingleOrDefault(value => value.CommandId == commandId);
                if (previous != null)
                {
                    var replay = runtimeStateProvider.PurchaseLeaseFor(commandId, actorId, leaseId, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter());
                    if (replay.State == LeaseActionState.Rejected) throw new InvalidOperationException(replay.ResultCode);
                    if (!isLocalActor && lease.Payer?.Kind == AccountKind.Player) RequireRemoteWalletMatch(actorId, "lease-purchase-result:" + correlation);
                    result = new { action, operation, replay.LeaseId, state = replay.State.ToString(), replay.ResultCode, amount = replay.Amount };
                }
                else
                {
                    if (isLocalActor) TrySynchronizeHostWallet(mod!, "before-remote-lease-purchase");
                    var externalDebit = CalculateLeasePurchaseDebit(lease);
                    var debited = false;
                    try
                    {
                        if (lease.Payer?.Kind == AccountKind.Player && externalDebit > 0)
                        {
                            if (isLocalActor)
                            {
                                if (!hostWallet.TryDebit(externalDebit)) throw new InvalidOperationException("The authoritative personal wallet has insufficient funds.");
                            }
                            else if (!multiplayerWallet!.TryDebit(actorId, "lease-purchase:" + correlation, externalDebit, out _))
                                throw new InvalidOperationException("The authenticated player's personal wallet has insufficient funds.");
                            debited = true;
                        }
                        var record = runtimeStateProvider.PurchaseLeaseFor(commandId, actorId, leaseId, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter());
                        if (record.State == LeaseActionState.Rejected) throw new InvalidOperationException(record.ResultCode);
                        if (record.Amount != externalDebit && lease.Payer?.Kind == AccountKind.Player) throw new InvalidOperationException("Lease purchase debit changed during the authoritative transaction.");
                        if (!isLocalActor && lease.Payer?.Kind == AccountKind.Player) RequireRemoteWalletMatch(actorId, "lease-purchase-result:" + correlation);
                        result = new { action, operation, record.LeaseId, state = record.State.ToString(), record.ResultCode, amount = record.Amount };
                    }
                    catch
                    {
                        if (debited && lease.State != LeaseState.PurchasePending && lease.State != LeaseState.Purchased)
                        {
                            if (isLocalActor) hostWallet.Credit(externalDebit);
                            else multiplayerWallet!.Credit(actorId, "lease-purchase-compensation:" + correlation, externalDebit);
                        }
                        throw;
                    }
                }
            }
            else if (operation == "publish-outbound") { if (!isLocalActor) throw new UnauthorizedAccessException("Outbound leasing is not yet actor-aware."); var ids = body["assetIds"]?.Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToArray() ?? Array.Empty<string>(); var contract = runtimeStateProvider!.PublishLocalOutboundLease("remote-outbound-publish:" + correlation, (string?)body["contractId"] ?? "outbound:" + correlation, ids, (long?)body["rent"] ?? 0, (long?)body["interval"] ?? 1, (long?)body["duration"] ?? 1, (long?)body["recallFee"] ?? 0, (decimal?)body["condition"] ?? 1m, (string?)body["destination"] ?? "external-market", (string?)body["returnLocation"] ?? "service-track", runtimeRoleDetector!, new UnityAssetReleaseGuard(), new DeclaredOffSceneLeaseSimulationPort()); result = new { action, operation, contract.ContractId, state = contract.State.ToString(), contract.Version }; }
            else if (operation == "activate-outbound") { if (!isLocalActor) throw new UnauthorizedAccessException("Outbound leasing is not yet actor-aware."); var record = runtimeStateProvider!.ActivateLocalOutboundLease("remote-outbound-activate:" + correlation, (string?)body["contractId"] ?? "", runtimeRoleDetector!, new UnityAssetReleaseGuard(), new DeclaredOffSceneLeaseSimulationPort()); result = new { action, operation, record.ContractId, state = record.State.ToString(), record.ResultCode }; }
            else if (operation == "return-outbound" || operation == "recall-outbound") { if (!isLocalActor) throw new UnauthorizedAccessException("Outbound leasing is not yet actor-aware."); var contractId = (string?)body["contractId"] ?? ""; var condition = (decimal?)body["condition"] ?? 1m; var location = (string?)body["returnLocation"] ?? "service-track"; var record = operation == "recall-outbound" ? runtimeStateProvider!.RecallLocalOutboundLease("remote-outbound-recall:" + correlation, contractId, condition, location, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new DeclaredOffSceneLeaseSimulationPort()) : runtimeStateProvider!.ReturnLocalOutboundLease("remote-outbound-return:" + correlation, contractId, condition, location, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new DeclaredOffSceneLeaseSimulationPort()); result = new { action, operation, record.ContractId, state = record.State.ToString(), record.ResultCode }; }
            else throw new ArgumentException("Unsupported lease operation.");
        }
        else if (action == "assignment.manage")
        {
            var operation = (string?)body["operation"] ?? ""; var assignmentId = (string?)body["assignmentId"] ?? "";
            if (operation == "reserve") { var ids = body["assetIds"]?.Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToArray() ?? Array.Empty<string>(); if (!Enum.TryParse((string?)body["kind"], true, out MissionAssignmentKind kind)) throw new ArgumentException("Invalid assignment kind."); var record = runtimeStateProvider!.ReserveAssignmentFor("remote-assignment-reserve:" + correlation, actorId, string.IsNullOrWhiteSpace(assignmentId) ? "assignment:" + correlation : assignmentId, (string?)body["missionId"] ?? "", kind, ids, (bool?)body["forCompany"] ?? false, (long?)body["maximumRevenue"] ?? 0, runtimeRoleDetector!, new UnityMissionLifecyclePort()); result = new { action, operation, record.AssignmentId, state = record.State.ToString(), record.ResultCode }; }
            else if (operation == "start") { if (isLocalActor) TrySynchronizeHostWallet(mod!, "before-remote-assignment-start"); var balance = isLocalActor ? hostWallet.ReadBalance() : ActorWalletBalance(runtimeStateProvider!.Current!, actorId); var record = runtimeStateProvider!.StartAssignmentFor("remote-assignment-start:" + correlation, actorId, assignmentId, balance, runtimeRoleDetector!, new UnityMissionLifecyclePort()); result = new { action, operation, record.AssignmentId, state = record.State.ToString(), record.ResultCode }; }
            else if (operation == "complete") { var assignment = runtimeStateProvider!.Current!.Assignments.Single(x => x.AssignmentId == assignmentId); var arrived = body["assetIds"]?.Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToArray() ?? assignment.AssetIds.ToArray(); var record = CompleteAssignmentAsActor(correlation, actorId, isLocalActor, assignmentId, arrived); if (record.ExternalSettlement == ExternalSettlementState.Pending && record.ActualRevenue > 0) { if (!hostWallet.TryDebit(record.ActualRevenue)) throw new InvalidOperationException("Mission revenue was routed internally but could not be removed from the shared vanilla wallet."); record = runtimeStateProvider.MarkMissionSettlementFor(actorId, record.AssignmentId, hostWallet.ReadBalance(), runtimeRoleDetector!, new UnityMissionLifecyclePort()); if (isLocalActor) runtimeStateProvider.SynchronizeLocalWallet("remote-mission-wallet-settlement:" + correlation, hostWallet.ReadBalance(), "remote-company-mission-settlement"); } result = new { action, operation, record.AssignmentId, state = record.State.ToString(), record.ResultCode, record.ActualRevenue, settlement = record.ExternalSettlement.ToString() }; }
            else if (operation == "cancel") { var record = runtimeStateProvider!.CancelAssignmentFor("remote-assignment-cancel:" + correlation, actorId, assignmentId, runtimeRoleDetector!, new UnityMissionLifecyclePort()); result = new { action, operation, record.AssignmentId, state = record.State.ToString(), record.ResultCode }; }
            else if (operation == "passenger-configure-route") { if (!isLocalActor) throw new UnauthorizedAccessException("Passenger administration is not yet actor-aware."); var originId = ((string?)body["originId"] ?? "").Trim(); var destinationId = ((string?)body["destinationId"] ?? "").Trim(); var routeId = ((string?)body["routeId"] ?? "").Trim(); if (routeId.Length == 0) routeId = "passenger-route:" + originId + ":" + destinationId; var route = runtimeStateProvider!.ConfigureLocalPassengerRoute("remote-passenger-route:" + correlation, routeId, originId, destinationId, (int?)body["initialDemand"] ?? 0, (int?)body["maximumDemand"] ?? 0, (int?)body["growthPerCycle"] ?? 0, (long?)body["frequencyTicks"] ?? 1, (long?)body["baseFare"] ?? 0, (long?)body["latePenalty"] ?? 0, runtimeRoleDetector!, new UnityMissionLifecyclePort()); result = new { action, operation, route.RouteId, route.DemandUnits, route.MaximumDemandUnits, route.Version }; }
            else if (operation == "passenger-refresh-route") { if (!isLocalActor) throw new UnauthorizedAccessException("Only the host may advance global passenger demand."); var route = runtimeStateProvider!.RefreshLocalPassengerDemand("remote-passenger-refresh:" + correlation, (string?)body["routeId"] ?? "", runtimeRoleDetector!, new UnityMissionLifecyclePort()); result = new { action, operation, route.RouteId, route.DemandUnits, route.Version }; }
            else if (operation == "passenger-reserve") { var ids = body["assetIds"]?.Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToArray() ?? Array.Empty<string>(); var now = runtimeStateProvider!.Current!.LeaseClock.ActiveTick; var passengerJobId = (string?)body["passengerJobId"] ?? ""; RequirePassengerJobsJob(passengerJobId); var record = runtimeStateProvider.ReservePassengerServiceFor("remote-passenger-reserve:" + correlation, actorId, (string?)body["contractId"] ?? "passenger:" + correlation, (string?)body["routeId"] ?? "", passengerJobId, ids, (bool?)body["forCompany"] ?? false, (int?)body["capacity"] ?? 0, now, checked(now + ((long?)body["journeyTicks"] ?? 1)), runtimeRoleDetector!, new UnityMissionLifecyclePort()); result = new { action, operation, record.ContractId, state = record.State.ToString(), record.ResultCode }; }
            else if (operation == "passenger-start") { if (isLocalActor) TrySynchronizeHostWallet(mod!, "before-remote-passenger-start"); var balance = isLocalActor ? hostWallet.ReadBalance() : ActorWalletBalance(runtimeStateProvider!.Current!, actorId); var record = runtimeStateProvider!.StartPassengerServiceFor("remote-passenger-start:" + correlation, actorId, (string?)body["contractId"] ?? "", balance, runtimeStateProvider.Current!.LeaseClock.ActiveTick, runtimeRoleDetector!, new UnityMissionLifecyclePort()); result = new { action, operation, record.ContractId, state = record.State.ToString(), record.ResultCode }; }
            else if (operation == "passenger-complete") { var contractId = (string?)body["contractId"] ?? ""; var contract = runtimeStateProvider!.Current!.PassengerContracts.Single(value => value.ContractId == contractId); var arrived = body["assetIds"]?.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray() ?? contract.AssetIds.ToArray(); var record = CompletePassengerServiceAsActor(correlation, actorId, isLocalActor, contractId, arrived); var assignment = runtimeStateProvider.Current.Assignments.Single(value => value.AssignmentId == record.AssignmentId); if (assignment.ExternalSettlement == ExternalSettlementState.Pending) { var delta = hostWallet.ReadBalance() - assignment.ExpectedVanillaBalance; if (delta > 0 && !hostWallet.TryDebit(delta)) throw new InvalidOperationException("Passenger settlement was committed but the vanilla wallet debit failed."); if (delta < 0) hostWallet.Credit(-delta); runtimeStateProvider.MarkMissionSettlementFor(actorId, assignment.AssignmentId, hostWallet.ReadBalance(), runtimeRoleDetector!, new UnityMissionLifecyclePort()); } result = new { action, operation, record.ContractId, state = record.State.ToString(), record.ResultCode, record.PaidRevenue, record.PunctualityPenalty, settlement = assignment.ExternalSettlement.ToString() }; }
            else if (operation == "passenger-cancel") { var record = runtimeStateProvider!.CancelPassengerServiceFor("remote-passenger-cancel:" + correlation, actorId, (string?)body["contractId"] ?? "", runtimeRoleDetector!, new UnityMissionLifecyclePort()); result = new { action, operation, record.ContractId, state = record.State.ToString(), record.ResultCode }; }
            else throw new ArgumentException("Unsupported assignment operation.");
        }
        else if (action == "finance.manage")
        {
            var operation = (string?)body["operation"] ?? ""; var contractId = (string?)body["contractId"] ?? "";
            if (operation == "offer" && string.IsNullOrWhiteSpace(contractId)) contractId = "financing:" + correlation;
            if (operation == "register-pool") { var pool = runtimeStateProvider!.RegisterLocalFinancingPool("remote-finance-pool:" + correlation, (string?)body["poolId"] ?? "", (long?)body["backedCapital"] ?? 0, runtimeRoleDetector!); result = new { action, operation, pool.PoolId, pool.AvailableCapital, pool.Version }; }
            else if (operation == "offer") { if (!Enum.TryParse((string?)body["kind"], true, out FinancingKind kind)) throw new ArgumentException("Invalid financing kind."); var record = runtimeStateProvider!.OfferLocalFinancing("remote-finance-offer:" + correlation, contractId, kind, (bool?)body["forCompany"] ?? false, (string?)body["poolId"] ?? "", (long?)body["principal"] ?? 0, (int?)body["interestBasisPoints"] ?? 0, (long?)body["installment"] ?? 0, (long?)body["intervalTicks"] ?? 1, (long?)body["maturityTicks"] ?? 1, (long?)body["guarantee"] ?? 0, runtimeRoleDetector!); result = new { action, operation, record.ContractId, state = record.State.ToString(), record.Version }; }
            else if (operation == "accept") { var record = runtimeStateProvider!.AcceptLocalFinancing("remote-finance-accept:" + correlation, contractId, runtimeRoleDetector!); result = new { action, operation, record.ContractId, state = record.State.ToString(), record.ResultCode }; }
            else if (operation == "draw") { var record = runtimeStateProvider!.DrawLocalCredit("remote-finance-draw:" + correlation, contractId, (long?)body["amount"] ?? 0, runtimeRoleDetector!); result = new { action, operation, record.ContractId, state = record.State.ToString(), record.ResultCode }; }
            else if (operation == "repay") { var record = runtimeStateProvider!.RepayLocalFinancing("remote-finance-repay:" + correlation, contractId, (long?)body["amount"] ?? 0, runtimeRoleDetector!); result = new { action, operation, record.ContractId, state = record.State.ToString(), record.ResultCode }; }
            else throw new ArgumentException("Unsupported finance operation.");
        }
        else if (action == "yard.manage")
        {
            var operation = (string?)body["operation"] ?? ""; var planId = (string?)body["planId"] ?? "";
            if (operation == "create") { var tracks = body["trackIds"]?.Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToArray() ?? Array.Empty<string>(); var plan = runtimeStateProvider!.CreateTriagePlanFor("remote-yard-create:" + correlation, actorId, planId, (string?)body["assignmentId"] ?? "", tracks, runtimeRoleDetector!); result = new { action, operation, plan.PlanId, state = plan.State.ToString(), plan.ResultCode }; }
            else if (operation == "cancel") { var plan = runtimeStateProvider!.CancelTriagePlanFor("remote-yard-cancel:" + correlation, actorId, planId, runtimeRoleDetector!); result = new { action, operation, plan.PlanId, state = plan.State.ToString(), plan.ResultCode }; }
            else throw new ArgumentException("Unsupported yard operation.");
        }
        else if (action == "industry.manage")
        {
            var operation = (string?)body["operation"] ?? "";
            var contractId = (string?)body["contractId"] ?? "";
            var snapshot = runtimeStateProvider!.Current!;
            var engine = new IndustrialEconomyEngine(snapshot, runtimeRoleDetector!, new UnityIndustrialExecutionPort(snapshot),
                new UnityCargoTransferObservationPort(snapshot), new UnityWagonCompatibilityPort(snapshot));
            var tick = snapshot.LeaseClock.ActiveTick;
            var localPlayer = snapshot.Economy.Players.Single(value => value.PlayerId == actorId);
            var forCompany = (bool?)body["forCompany"] ?? false;
            var beneficiary = forCompany
                ? AccountRef.Company(localPlayer.CompanyId ?? throw new InvalidOperationException("Company membership is required."))
                : AccountRef.Player(localPlayer.PlayerId);
            var operatorRef = forCompany
                ? ResolveIndustrialOperator(snapshot, localPlayer, true)
                : AssetOwnerRef.Player(localPlayer.PlayerId);
            if (!isLocalActor && IsIndustrialAdministrationOperation(operation))
                throw new UnauthorizedAccessException("Only the host may configure or advance the global industrial economy.");

            if (operation == "configure-pilot")
            {
                var ids = body["assetIds"]?.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray() ?? Array.Empty<string>();
                var pilot = UnityIndustrialPilotBootstrap.Configure(snapshot, engine, ids, operatorRef, tick, "remote-industry-pilot:" + correlation);
                result = new { action, operation, pilot.PolicyId, pilot.OriginFacilityId, pilot.DestinationFacilityId, pilot.CargoId, pilot.BatchQuantity, pilot.RecipeId };
            }
            else if (operation == "configure-stock")
            {
                var stock = engine.ConfigureStock("remote-industry-stock:" + correlation, (string?)body["facilityId"] ?? "", (string?)body["cargoId"] ?? "",
                    (decimal?)body["onHand"] ?? 0m, (decimal?)body["capacity"] ?? 0m);
                result = new { action, operation, stock.FacilityId, stock.CargoId, stock.OnHand, stock.Capacity, stock.Version };
            }
            else if (operation == "configure-recipe")
            {
                var recipe = engine.ConfigureRecipe("remote-industry-recipe:" + correlation, (string?)body["recipeId"] ?? "", (string?)body["facilityId"] ?? "",
                    (string?)body["inputCargoId"] ?? "", (decimal?)body["inputQuantity"] ?? 0m, (string?)body["outputCargoId"] ?? "",
                    (decimal?)body["outputQuantity"] ?? 0m, (long?)body["cadenceTicks"] ?? 0, (int?)body["maximumBacklogCycles"] ?? 0);
                result = new { action, operation, recipe.RecipeId, recipe.FacilityId, recipe.PendingCycles, recipe.CompletedCycles, recipe.Version };
            }
            else if (operation == "configure-policy")
            {
                var cargoId = (string?)body["cargoId"] ?? "";
                var allowed = body["allowedDefinitionIds"]?.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).Distinct(StringComparer.Ordinal).ToList() ?? new List<string>();
                var policy = engine.ConfigureTransportPolicy("remote-industry-policy:" + correlation, (string?)body["policyId"] ?? "",
                    (string?)body["originFacilityId"] ?? "", (string?)body["destinationFacilityId"] ?? "", cargoId,
                    (decimal?)body["batchQuantity"] ?? 0m, (decimal?)body["destinationTargetQuantity"] ?? 0m,
                    (long?)body["baseReward"] ?? 0, (long?)body["maximumScarcityBonus"] ?? 0,
                    (long?)body["offerLifetimeTicks"] ?? 0, (long?)body["preparationDurationTicks"] ?? 0,
                    (long?)body["preparationPenalty"] ?? 0,
                    new WagonRequirement { CargoId = cargoId, MinimumWagonCount = (int?)body["minimumWagonCount"] ?? 1, MinimumTotalCapacity = (decimal?)body["minimumTotalCapacity"] ?? 0m, AllowedDefinitionIds = allowed },
                    (bool?)body["enabled"] ?? true, (long?)body["deliveryDurationTicks"] ?? 0);
                result = new { action, operation, policy.PolicyId, policy.Enabled, policy.NextNeedSequence, policy.Version };
            }
            else if (operation == "publish-need")
            {
                var policyId = (string?)body["policyId"] ?? "";
                var need = engine.PublishTransportNeed("remote-industry-need-publish:" + correlation, policyId, tick);
                result = new { action, operation, policyId, needId = need?.NeedId, state = need?.State.ToString() ?? "NotRequired", quantity = need?.Quantity ?? 0m, tick };
            }
            else if (operation == "accept-need")
            {
                var needId = (string?)body["needId"] ?? "";
                var needVersion = (long?)body["needVersion"] ?? 0;
                var need = snapshot.IndustrialTransportNeeds.Single(value => value.NeedId == needId);
                var penaltyPayer = need.PreparationPenalty > 0 ? beneficiary : null;
                var contract = engine.AcceptTransportNeed("remote-industry-need-accept:" + correlation, needId, needVersion, beneficiary, penaltyPayer, tick);
                result = new { action, operation, needId, contract.ContractId, state = contract.State.ToString(), contract.Version };
            }
            else if (operation == "create")
            {
                var allowed = body["allowedDefinitionIds"]?.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).Distinct(StringComparer.Ordinal).ToList() ?? new List<string>();
                var minimumWagons = (int?)body["minimumWagonCount"] ?? 1;
                var minimumCapacity = (decimal?)body["minimumTotalCapacity"] ?? ((decimal?)body["quantity"] ?? 0m);
                var cargoId = (string?)body["cargoId"] ?? "";
                var requirement = new WagonRequirement { CargoId = cargoId, MinimumWagonCount = minimumWagons, MinimumTotalCapacity = minimumCapacity, AllowedDefinitionIds = allowed };
                var contract = engine.CreateTransportOffer(string.IsNullOrWhiteSpace(contractId) ? "industrial:" + correlation : contractId,
                    (string?)body["originFacilityId"] ?? "", (string?)body["destinationFacilityId"] ?? "", cargoId, (decimal?)body["quantity"] ?? 0m,
                    beneficiary, (long?)body["baseReward"] ?? 0, (long?)body["scarcityBonus"] ?? 0, tick, (long?)body["deadlineTick"] ?? 0,
                    requirement, (long?)body["preparationPenalty"] ?? 0);
                result = new { action, operation, contract.ContractId, state = contract.State.ToString(), contract.Version };
            }
            else if (operation == "advance-production")
            {
                var recipeId = (string?)body["recipeId"] ?? "";
                var cycles = engine.AdvanceProduction("remote-industry-production:" + correlation, recipeId, tick);
                result = new { action, operation, recipeId, cycles, tick };
            }
            else
            {
                var contract = snapshot.IndustrialContracts.Single(value => value.ContractId == contractId);
                RequireIndustrialContractControl(snapshot, localPlayer, contract);
                if (operation == "accept") contract = engine.Accept("remote-industry-accept:" + correlation, contractId, contract.Version, tick,
                    (long?)body["preparationDuration"] ?? 1, (bool?)body["applyPreparationPenalty"] == true ? beneficiary : null);
                else if (operation == "assign")
                {
                    var ids = body["assetIds"]?.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray() ?? Array.Empty<string>();
                    contract = engine.AssignWagons("remote-industry-assign:" + correlation, localPlayer.PlayerId, contractId, contract.Version, operatorRef, ids, tick);
                }
                else if (operation == "activate")
                {
                    EnsureSelfShuntIndustrialLifecycle(mod ?? throw new InvalidOperationException("BDVM mod entry is unavailable."));
                    PreflightIndustrialActivation(snapshot, contract, tick);
                    UnityIndustrialJobAdapter.CreateForContract(snapshot, contract, selfShuntIndustrialLifecycle ?? throw new InvalidOperationException("SelfShunt industrial lifecycle is unavailable."));
                    contract = engine.Activate("remote-industry-activate:" + correlation, contractId, tick);
                }
                else if (operation == "observe-loading") contract = engine.RecordLoading("remote-industry-loading:" + correlation, contractId,
                    (string?)body["assetId"] ?? "", (decimal?)body["cumulativeQuantity"] ?? 0m, tick);
                else if (operation == "observe-unloading") contract = engine.RecordUnloading("remote-industry-unloading:" + correlation, contractId,
                    (string?)body["assetId"] ?? "", (decimal?)body["cumulativeQuantity"] ?? 0m, tick);
                else if (operation == "reconcile-delivery")
                {
                    contract = ReconcileIndustrialCargoState(snapshot, contract, "remote-industry-delivery:" + correlation);
                    if (new UnityIndustrialExecutionPort(snapshot).InspectDelivery("remote-industry-delivery:" + correlation, contractId, contract.Quantity) != WorldOwnershipOutcome.Applied)
                        throw new InvalidOperationException("The exact external job and assigned consist are not authoritatively completed.");
                    if (contract.State != IndustrialContractState.Completed) throw new InvalidOperationException("Physical wagon unloading is incomplete.");
                }
                else if (operation == "expire") contract = engine.ExpirePreparation("remote-industry-expire:" + correlation, contractId, tick);
                else if (operation == "cancel") { UnityIndustrialJobAdapter.RequireCancellationObserved(contract); contract = engine.Cancel("remote-industry-cancel:" + correlation, contractId); }
                else throw new ArgumentException("Unsupported industry operation.");
                result = new { action, operation, contract.ContractId, state = contract.State.ToString(), contract.DeliveredQuantity, contract.PaidAmount, contract.Version };
            }
        }
        else throw new ArgumentException("Unsupported BDVM intent.");
        if (!isLocalActor) SettleRemoteWalletToInternal(actorId, "intent-settlement:" + correlation);
        if (!saveStaged && !SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("BDVM intent succeeded in memory but could not be staged in SaveGameData.");
        mod?.Logger.Log("[correlation=" + correlation + "] [event=remote-dispatch-intent] transportIdentity=" + transportIdentity + ", authorityActor=" + actorId + ", action=" + action);
        return Newtonsoft.Json.JsonConvert.SerializeObject(new { status = "succeeded", correlationId = correlation, result });
    }

    private static bool IsActorAwareRemoteAction(string action)
    {
        return action == "fleet.set-state" || action == "fleet.rename" || action == "fleet.bundle" || action == "fleet.resale" || action == "market.purchase" ||
            action == "company.create" || action == "company.apply" || action == "company.invite" ||
            action == "company.decide-application" || action == "company.respond-invitation" ||
            action == "company.leave" || action == "company.policy" || action == "company.permission" ||
            action == "company.transfer-leadership" || action == "company.dissolve" || action == "industry.manage" ||
            action == "lease.manage" || action == "assignment.cancel" || action == "assignment.manage" || action == "initial-delivery.place" || action == "yard.manage" || action == "fleet.maintenance" || action == "wallet.transfer";
    }

    private static string RequestClientAuthoritativeState()
    {
        string response;
        string? requestId = null;
        var hadCachedState = false;
        lock (clientStateGate)
        {
            if (clientStateTransferPending && clientStateRequestId != null && clientRequestTracker != null &&
                clientRequestTracker.ResultOrTimeout(clientStateRequestId, DateTimeOffset.UtcNow).Code == "request-timeout")
            {
                mod?.Logger.Warning("[correlation=" + clientStateRequestId + "] [event=multiplayer-state-timeout] Restarting authoritative state transfer after terminal page timeout.");
                clientStateTransferPending = false;
                clientStateRequestId = null;
                clientStateAssembler.Reset();
            }
            if (clientAuthoritativeState != null)
            {
                hadCachedState = true;
                var state = Newtonsoft.Json.Linq.JObject.Parse(clientAuthoritativeState);
                state["clientResults"] = Newtonsoft.Json.Linq.JArray.FromObject(clientProtocolResults.Values.Where(value => value.Code != "module-state-page").OrderBy(value => value.RequestId, StringComparer.Ordinal).Select(value => new
                {
                    value.RequestId,
                    status = value.Status.ToString(),
                    value.Code,
                    value.Detail,
                    result = value.Payload == null || value.Payload.Length == 0 ? null : System.Text.Encoding.UTF8.GetString(value.Payload)
                }));
                if (!clientStateTransferPending && DateTimeOffset.UtcNow - clientStateReceivedAt >= TimeSpan.FromSeconds(2))
                {
                    requestId = "state-" + Guid.NewGuid().ToString("N");
                    clientStateTransferPending = true;
                    clientStateRequestId = requestId;
                    clientStateAssembler.Reset();
                    state["stateRefresh"] = "pending";
                }
                response = state.ToString(Newtonsoft.Json.Formatting.None);
            }
            else if (clientStateTransferPending)
                response = Newtonsoft.Json.JsonConvert.SerializeObject(new { schema = "bdvm.remote-dispatch", schemaVersion = 2, status = "pending" });
            else
            {
                requestId = "state-" + Guid.NewGuid().ToString("N");
                clientStateTransferPending = true;
                clientStateRequestId = requestId;
                clientStateAssembler.Reset();
                response = Newtonsoft.Json.JsonConvert.SerializeObject(new { schema = "bdvm.remote-dispatch", schemaVersion = 2, status = "pending", correlationId = requestId });
            }
        }
        if (requestId != null)
        {
            try { SendClientModuleIntent(requestId, "state.get", "{}"); }
            catch
            {
                lock (clientStateGate) { if (string.Equals(clientStateRequestId, requestId, StringComparison.Ordinal)) { clientStateTransferPending = false; clientStateRequestId = null; clientStateAssembler.Reset(); } }
                if (!hadCachedState) throw;
            }
        }
        return response;
    }

    private static string SubmitClientModuleIntent(string payload)
    {
        if (payload == null || System.Text.Encoding.UTF8.GetByteCount(payload) > 4096) throw new ArgumentException("BDVM intent payload is too large.");
        var body = Newtonsoft.Json.Linq.JObject.Parse(payload);
        var action = (string?)body["action"] ?? throw new ArgumentException("A BDVM action is required.");
        if (!IsActorAwareRemoteAction(action)) throw new UnauthorizedAccessException("This operation is not available through the Multiplayer client protocol.");
        var requestId = (string?)body["correlationId"] ?? Guid.NewGuid().ToString("N");
        body.Remove("action"); body.Remove("correlationId"); body.Remove("playerId"); body.Remove("requesterId"); body.Remove("authorityActor");
        SendClientModuleIntent(requestId, action, body.ToString(Newtonsoft.Json.Formatting.None));
        return Newtonsoft.Json.JsonConvert.SerializeObject(new { status = "pending", correlationId = requestId, action });
    }

    private static void SendClientModuleIntent(string requestId, string action, string payloadJson)
    {
        if (configuredClient == null || clientProtocol == null || clientRequestTracker == null || !configuredClient.IsConnected)
            throw new InvalidOperationException("The Multiplayer client protocol is unavailable.");
        var playerId = MultiplayerPlayerIdentityAdapter.RequirePersistentLocalPlayerId(configuredClient);
        var envelope = new CompanyProtocolEnvelope
        {
            ProtocolVersion = CompanyProtocolLimits.CurrentVersion,
            MessageType = CompanyMessageType.IntentRequest,
            RequestId = requestId,
            PlayerId = playerId,
            Payload = CompanyIntentCodec.Encode(new CompanyIntent { Type = CompanyIntentType.ModuleOperation, ModuleAction = action, ModulePayloadJson = payloadJson })
        };
        clientRequestTracker.Track(envelope, DateTimeOffset.UtcNow);
        clientProtocol.SendIntent(envelope);
    }

    private sealed class FullModuleIntentExecutor : IAuthoritativeModuleIntentExecutor
    {
        public ProtocolResult Execute(string authenticatedPlayerId, string requestId, string action, string payloadJson)
        {
            if (action == "state.get")
            {
                try
                {
                    var request = Newtonsoft.Json.Linq.JObject.Parse(payloadJson);
                    var token = (string?)request["token"];
                    var offset = (int?)request["offset"] ?? 0;
                    var page = authoritativeStatePager.Read(authenticatedPlayerId, token, offset,
                        () => System.Text.Encoding.UTF8.GetBytes(BuildRemoteDispatchState(authenticatedPlayerId)), DateTimeOffset.UtcNow);
                    return new ProtocolResult { RequestId = requestId, Status = ProtocolResultStatus.Succeeded, Code = "module-state-page", Payload = AuthoritativeStatePageCodec.Encode(page) };
                }
                catch (Newtonsoft.Json.JsonException exception) { return Rejected(requestId, "module-invalid", exception.Message); }
                catch (System.IO.InvalidDataException exception) { return Rejected(requestId, "module-invalid", exception.Message); }
                catch (UnauthorizedAccessException exception) { return Rejected(requestId, "module-unauthorized", exception.Message); }
                catch (InvalidOperationException exception) { return Rejected(requestId, "module-refused", exception.Message); }
            }
            if (!IsActorAwareRemoteAction(action))
                return Rejected(requestId, "module-action-not-actor-aware", action);
            try
            {
                var body = Newtonsoft.Json.Linq.JObject.Parse(payloadJson);
                body["action"] = action;
                body["correlationId"] = requestId;
                body.Remove("playerId");
                body.Remove("requesterId");
                body.Remove("authorityActor");
                var json = HandleRemoteDispatchIntent(authenticatedPlayerId, body.ToString(Newtonsoft.Json.Formatting.None));
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                if (bytes.Length > CompanyProtocolLimits.MaximumPayloadBytes - 1024)
                    return Rejected(requestId, "module-result-too-large", action);
                return new ProtocolResult { RequestId = requestId, Status = ProtocolResultStatus.Succeeded, Code = "module-operation-succeeded", Payload = bytes };
            }
            catch (UnauthorizedAccessException exception) { return Rejected(requestId, "module-unauthorized", exception.Message); }
            catch (ArgumentException exception) { return Rejected(requestId, "module-invalid", exception.Message); }
            catch (InvalidOperationException exception) { return Rejected(requestId, "module-refused", exception.Message); }
        }

        private static ProtocolResult Rejected(string requestId, string code, string detail)
        {
            if (detail.Length > CompanyProtocolLimits.MaximumDetailBytes)
                detail = detail.Substring(0, CompanyProtocolLimits.MaximumDetailBytes);
            return new ProtocolResult { RequestId = requestId, Status = ProtocolResultStatus.Rejected, Code = code, Detail = detail };
        }
    }

    private sealed class UmmTrace : IDiagnosticTrace
    {
        private readonly UnityModManager.ModEntry entry;
        public UmmTrace(UnityModManager.ModEntry entry) => this.entry = entry;
        public void Info(string correlationId, string message) => entry.Logger.Log($"[correlation={correlationId}] {message}");
        public void Error(string correlationId, string message, Exception exception) => entry.Logger.Error($"[correlation={correlationId}] {message}: {exception}");
    }

    private sealed class SaveGameLiquidationCheckpointPort : ICompanyLiquidationCheckpointPort
    {
        private readonly UnityModManager.ModEntry entry;
        public SaveGameLiquidationCheckpointPort(UnityModManager.ModEntry entry) => this.entry = entry;

        public bool TryCheckpoint(CompanyLiquidationRecord record, string phase)
        {
            var saved = SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance);
            entry.Logger.Log("[correlation=" + record.CommandId + "] [event=company-liquidation-checkpoint] company=" + record.CompanyId + ", phase=" + phase + ", saved=" + saved + ", contractsCancelled=" + record.ContractsCancelled + ", ownershipCommitted=" + record.OwnershipCommitted + ", economyCommitted=" + record.EconomyCommitted);
            return saved;
        }
    }

    private sealed class SaveGameInitialDeliveryCheckpointPort : IInitialDeliveryCheckpointPort
    {
        private readonly UnityModManager.ModEntry entry;
        public SaveGameInitialDeliveryCheckpointPort(UnityModManager.ModEntry entry) => this.entry = entry;

        public bool TryCheckpoint(InitialDeliveryGrant grant, string phase)
        {
            var saved = SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance);
            entry.Logger.Log("[correlation=" + (grant.PlacementCommandId ?? grant.SourceCommandId) + "] [event=initial-delivery-checkpoint] grant=" + grant.GrantId + ", phase=" + phase + ", saved=" + saved + ", state=" + grant.State + ", result=" + grant.ResultCode);
            return saved;
        }
    }

    [HarmonyPatch(typeof(DV.TimeAdvance), nameof(DV.TimeAdvance.AdvanceTime))]
    private static class EconomicTimeAdvancePatch
    {
        [HarmonyPostfix]
        private static void Postfix(float amountOfTimeToSkipInSeconds, bool __runOriginal)
        {
            if (__runOriginal) RecordAuthoritativeTimeSkip(amountOfTimeToSkipInSeconds);
        }
    }
}
