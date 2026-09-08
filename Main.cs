using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BDVM.Adapters;
using BDVM.Domain;
using BDVM.PassengerJobsBridge;
using DV.Logic.Job;
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
    private static string maintenanceCondition = "1";
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
    private static IServer? configuredServer;
    private static IClient? configuredClient;
    private static MultiplayerServerProtocolAdapter? serverProtocol;
    private static MultiplayerClientProtocolAdapter? clientProtocol;
    private static InGameCompanyWindow? inGameWindow;

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
        RemoteDispatchBridge.Configure(BuildRemoteDispatchState, HandleRemoteDispatchIntent);
        WorldStreamingInit.LoadingFinished += OnWorldLoadingFinished;
        runtimeSettings = RuntimeSaveSettings.Load(
            Path.Combine(modEntry.Path, "runtime-settings.json"),
            message => modEntry.Logger.Warning("[correlation=save-settings] " + message));
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

    private static void ConfigureInGameWindow(UnityModManager.ModEntry entry)
    {
        var host = GameObject.Find("BDVM.InGameWindow") ?? new GameObject("BDVM.InGameWindow");
        UnityEngine.Object.DontDestroyOnLoad(host);
        inGameWindow = host.GetComponent<InGameCompanyWindow>() ?? host.AddComponent<InGameCompanyWindow>();
        inGameWindow.Configure(() => DrawCompanyPanel(entry), message => entry.Logger.Log(message));
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
            message => mod?.Logger.Log(message), runtimeSettings.StarterBundleDefinitionIds);
        serverProtocol = new MultiplayerServerProtocolAdapter(server, new PersistentMultiplayerPeerIdentityResolver(), new CompanyProtocolHost(executor), message => mod?.Logger.Log(message));
        server.RegisterSerializablePacket<BDVMSerializablePacket>(serverProtocol.Receive);
        configuredServer = server;
        mod?.Logger.Log("[correlation=multiplayer-bootstrap] [event=protocol-registered] side=server, protocol=" + CompanyProtocolLimits.CurrentVersion + ", api=" + MultiplayerAPI.LoadedApiVersion);
    }

    private static void ConfigureMultiplayerClient(IClient client)
    {
        if (client == null || ReferenceEquals(configuredClient, client)) return;
        clientProtocol = new MultiplayerClientProtocolAdapter(client);
        client.RegisterSerializablePacket<BDVMSerializablePacket>(packet =>
        {
            try
            {
                var result = clientProtocol.Receive(packet);
                mod?.Logger.Log("[correlation=" + result.RequestId + "] [event=multiplayer-result] status=" + result.Status + ", code=" + result.Code + ", version=" + result.AuthoritativeVersion + ", detail=" + result.Detail);
            }
            catch (Exception exception)
            {
                mod?.Logger.Error("[correlation=multiplayer-result] Invalid host result refused: " + exception);
            }
        });
        configuredClient = client;
        mod?.Logger.Log("[correlation=multiplayer-bootstrap] [event=protocol-registered] side=client, protocol=" + CompanyProtocolLimits.CurrentVersion + ", api=" + MultiplayerAPI.LoadedApiVersion);
    }

    private static void ClearMultiplayerServer() { configuredServer = null; serverProtocol = null; }
    private static void ClearMultiplayerClient() { configuredClient = null; clientProtocol = null; }

    private static void OnWorldLoadingFinished()
    {
        automaticExportPending = true;
        automaticExportDelayFrames = 120;
        mod?.Logger.Log("[correlation=runtime-validation] World loading finished; read-only diagnostic export scheduled.");
    }

    private static void OnUpdate(UnityModManager.ModEntry entry, float deltaTime)
    {
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
            entry.Logger.Log("[correlation=runtime-bootstrap] [event=starter-bundle] player=" + player.PlayerId + ", grant=" + starter.GrantId + ", components=" + string.Join(",", starter.DefinitionIds) + ", state=" + starter.State);
        }
        if (runtimeSettings.EnableWalletBridge)
            TrySynchronizeHostWallet(entry, "world-load");
        SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance);
        status = "BDVM 2.0.0 ready for " + player.PlayerId + ".";
        entry.Logger.Log("[correlation=runtime-bootstrap] Runtime state ready; player=" + player.PlayerId + ", legacyBalancePolicy=host-keeps-existing-balance, walletBridge=" + runtimeSettings.EnableWalletBridge + ", transfers=" + runtimeSettings.EnableCompanyTransfers + ", acquisition=" + runtimeSettings.EnableVehicleAcquisition + ".");
        entry.Logger.Log("[correlation=wallet-migration] [event=wallet-migration-policy] policy=host-keeps-existing-balance-v1, player=" + player.PlayerId + ", observedVanillaBalance=" + legacyBalance + ", remotePlayerInitialBalance=0");
        if (runtimeSettings.VerboseLogging)
            entry.Logger.Log("[correlation=runtime-bootstrap] [event=state-summary] players=" + runtimeStateProvider.Current!.Economy.Players.Count + ", companies=" + runtimeStateProvider.Current.Economy.Companies.Count + ", wallets=" + runtimeStateProvider.Current.Economy.Wallets.Count + ", assets=" + runtimeStateProvider.Current.Assets.Assets.Count + ", offers=" + runtimeStateProvider.Current.Offers.Count);
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
        GUILayout.Label("BDVM 2.0.0 — modular economy build; catalog ownership and one-shot depot delivery enabled");
        GUILayout.Label("SaveGameData hook: " + (SaveGameRuntimeHook.Enabled ? "enabled" : "disabled"));
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

    private static void PlaceInitialDelivery(UnityModManager.ModEntry entry, InitialDeliveryGrant grant, InitialDeliveryTrackRule rule)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            var adapter = new UnityInitialDeliveryAdapter(runtimeSettings.InitialDeliveryTracks);
            var result = runtimeStateProvider!.PlaceLocalInitialDelivery("initial-delivery:" + correlation, grant.GrantId, rule.TrackId, rule.Kind, runtimeRoleDetector!, adapter);
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Initial delivery state could not be staged in SaveGameData.");
            status = "Initial delivery: " + result.State + " / " + result.ResultCode + ".";
            entry.Logger.Log("[correlation=" + correlation + "] [event=initial-delivery] grant=" + result.GrantId + ", owner=" + result.Owner.Key + ", track=" + result.TargetTrackId + ", kind=" + result.TargetKind + ", components=" + string.Join(",", result.AssetIds) + ", state=" + result.State + ", result=" + result.ResultCode);
        }
        catch (Exception exception) { status = "Initial delivery refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=initial-delivery-refused] grant=" + grant.GrantId + ", track=" + rule.TrackId + ", error=" + exception); }
    }

    private static void ReconcileInitialDeliveries(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority();
            var results = runtimeStateProvider!.ReconcilePendingInitialDeliveries(runtimeRoleDetector!, new UnityInitialDeliveryAdapter(runtimeSettings.InitialDeliveryTracks));
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Reconciled initial delivery state could not be staged in SaveGameData.");
            status = "Initial delivery reconciliation: " + results.Count + " record(s).";
            foreach (var result in results) entry.Logger.Log("[correlation=" + correlation + "] [event=initial-delivery-reconcile] grant=" + result.GrantId + ", state=" + result.State + ", result=" + result.ResultCode);
        }
        catch (Exception exception) { status = "Initial delivery reconciliation refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=initial-delivery-reconcile-failed] " + exception); }
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
        if (GUILayout.Button("Create lease offer from selected existing market listing")) CreateInboundLeaseOffer(entry);
        foreach (var lease in snapshot.Leases.Where(x => x.State != LeaseState.Returned && x.State != LeaseState.Purchased && x.State != LeaseState.Cancelled).Take(50).ToArray())
        {
            var marker = lease.LeaseId == selectedLeaseId ? "> " : "  ";
            if (GUILayout.Button(marker + lease.LeaseId + " | " + lease.State + " | assets=" + lease.AssetIds.Count + " | deposit=" + lease.Deposit + " | rent=" + lease.RentAmount + " | debt=" + lease.OutstandingDebt)) selectedLeaseId = lease.LeaseId;
        }
        if (string.IsNullOrWhiteSpace(selectedLeaseId)) return;
        var selected = snapshot.Leases.Single(x => x.LeaseId == selectedLeaseId);
        leaseForCompany = hasCompany && GUILayout.Toggle(leaseForCompany, "Lessee and payer: company (off = personal)");
        if (selected.State == LeaseState.Offered && GUILayout.Button("Accept selected lease")) AcceptInboundLease(entry, leaseForCompany && hasCompany);
        if (selected.State == LeaseState.Active || selected.State == LeaseState.Delinquent)
        {
            GUILayout.Label("Active economic ticks (session-open simulation; pause/closed time is excluded):"); leaseAdvanceTicks = GUILayout.TextField(leaseAdvanceTicks, 10);
            if (GUILayout.Button("Advance active lease clock")) AdvanceInboundLeaseClock(entry);
            if (GUILayout.Button("Return selected lease at entered condition")) ReturnInboundLease(entry);
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
            RequireHostAuthority(); var listing = runtimeStateProvider!.Current!.Market.Listings.Single(x => x.ListingId == selectedMarketListingId && x.Kind == MarketListingKind.ExistingAsset);
            if (!long.TryParse(leaseDeposit, out var deposit) || !long.TryParse(leaseInitialFee, out var fee) || !long.TryParse(leaseRent, out var rent) || !long.TryParse(leaseInterval, out var interval) || !long.TryParse(leaseDuration, out var duration) || !long.TryParse(leaseDamageMaximum, out var damage) || !decimal.TryParse(leaseCondition, out var condition)) throw new InvalidOperationException("Lease terms are invalid.");
            long? option = string.IsNullOrWhiteSpace(leasePurchaseOption) ? null : long.Parse(leasePurchaseOption);
            var lease = runtimeStateProvider.CreateLocalLeaseOffer("lease:" + correlation, new[] { listing.AssetId! }, deposit, fee, rent, interval, duration, option, condition, damage, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter()); selectedLeaseId = lease.LeaseId;
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Lease offer could not be staged in SaveGameData.");
            status = "Lease offer created: " + lease.LeaseId + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=lease-offer] lease=" + lease.LeaseId + ", assets=" + string.Join(",", lease.AssetIds) + ", deposit=" + lease.Deposit + ", fee=" + lease.InitialFee + ", rent=" + lease.RentAmount + ", interval=" + lease.RentIntervalTicks + ", duration=" + lease.DurationTicks + ", option=" + lease.PurchaseOptionPrice);
        }
        catch (Exception exception) { status = "Lease offer refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=lease-offer-refused] " + exception); }
    }

    private static void AcceptInboundLease(UnityModManager.ModEntry entry, bool forCompany)
    {
        var correlation = Guid.NewGuid().ToString("N"); var externalDebited = false; long due = 0;
        try
        {
            RequireHostAuthority(); TrySynchronizeHostWallet(entry, "before-lease-accept"); var lease = runtimeStateProvider!.Current!.Leases.Single(x => x.LeaseId == selectedLeaseId); due = checked(lease.Deposit + lease.InitialFee);
            if (!forCompany && due > 0) { if (!hostWallet.TryDebit(due)) throw new InvalidOperationException("The authoritative personal wallet has insufficient funds."); externalDebited = true; }
            var result = runtimeStateProvider.AcceptLocalLease("lease-accept:" + correlation, lease.LeaseId, forCompany, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter());
            if (result.State != LeaseActionState.Succeeded) { if (externalDebited) hostWallet.Credit(due); throw new InvalidOperationException(result.ResultCode); }
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Accepted lease could not be staged in SaveGameData.");
            status = "Lease accepted; deposit held separately."; entry.Logger.Log("[correlation=" + correlation + "] [event=lease-accept] lease=" + lease.LeaseId + ", payer=" + lease.Payer?.Key + ", lessee=" + lease.Lessee?.Key + ", amount=" + result.Amount + ", externalDebited=" + externalDebited);
        }
        catch (Exception exception) { status = "Lease acceptance refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=lease-accept-refused] externalDebited=" + externalDebited + ", error=" + exception); }
    }

    private static void AdvanceInboundLeaseClock(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority(); if (!long.TryParse(leaseAdvanceTicks, out var ticks) || ticks < 0) throw new InvalidOperationException("Active ticks must be non-negative."); TrySynchronizeHostWallet(entry, "before-lease-clock");
            var playerId = runtimeStateProvider!.LocalPlayerId!; var wallet = runtimeStateProvider.Current!.Economy.Wallets.Single(x => x.Account.Key == "Player:" + playerId); var before = wallet.Balance;
            var tick = runtimeStateProvider.AdvanceLocalLeaseClock(new LeaseClockAdvance { CommandId = "lease-clock:" + correlation, SessionOpen = true, ActiveGameplayTicks = ticks }, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter());
            var delta = wallet.Balance - before; var debit = Math.Max(0, -delta); var credit = Math.Max(0, delta);
            if (debit > 0 && !hostWallet.TryDebit(debit)) throw new InvalidOperationException("Lease rent was committed but vanilla wallet settlement failed; preserve logs and retry recovery before continuing.");
            if (credit > 0) hostWallet.Credit(credit);
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Lease clock could not be staged in SaveGameData.");
            status = "Lease clock: " + tick + "; personal debit=" + debit + "; outbound credit=" + credit + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=lease-clock] tick=" + tick + ", activeDelta=" + ticks + ", personalDebit=" + debit + ", outboundCredit=" + credit);
        }
        catch (Exception exception) { status = "Lease clock refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=lease-clock-refused] " + exception); }
    }

    private static void ReturnInboundLease(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try { RequireHostAuthority(); if (!decimal.TryParse(leaseCondition, out var condition)) throw new InvalidOperationException("Return condition is invalid."); var lease = runtimeStateProvider!.Current!.Leases.Single(x => x.LeaseId == selectedLeaseId); var result = runtimeStateProvider.ReturnLocalLease("lease-return:" + correlation, lease.LeaseId, condition, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter()); if (result.State != LeaseActionState.Succeeded) throw new InvalidOperationException(result.ResultCode); if (lease.Payer?.Kind == AccountKind.Player && result.Amount > 0) hostWallet.Credit(result.Amount); if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Lease return could not be staged in SaveGameData."); status = "Lease returned; deposit refund=" + result.Amount + ", debt=" + lease.OutstandingDebt + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=lease-return] lease=" + lease.LeaseId + ", refund=" + result.Amount + ", debt=" + lease.OutstandingDebt + ", condition=" + condition); }
        catch (Exception exception) { status = "Lease return refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=lease-return-refused] " + exception); }
    }

    private static void PurchaseInboundLease(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try { RequireHostAuthority(); TrySynchronizeHostWallet(entry, "before-lease-purchase"); var lease = runtimeStateProvider!.Current!.Leases.Single(x => x.LeaseId == selectedLeaseId); var result = runtimeStateProvider.PurchaseLocalLease("lease-purchase:" + correlation, lease.LeaseId, runtimeRoleDetector!, new UnityAssetReleaseGuard(), new UnityExistingVehicleOwnershipAdapter()); if (result.State != LeaseActionState.Rejected && lease.Payer?.Kind == AccountKind.Player && result.Amount > 0 && !hostWallet.TryDebit(result.Amount)) throw new InvalidOperationException("Lease purchase was reserved but vanilla wallet settlement failed."); SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance); status = "Lease purchase: " + result.State + " / " + result.ResultCode + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=lease-purchase] lease=" + lease.LeaseId + ", amount=" + result.Amount + ", state=" + result.State + ", result=" + result.ResultCode); }
        catch (Exception exception) { status = "Lease purchase refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=lease-purchase-refused] " + exception); }
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
            var assignment = runtimeStateProvider.ReserveLocalAssignment("assignment-reserve:" + correlation, "assignment:" + correlation, missionId, missionKind == 0 ? MissionAssignmentKind.Freight : MissionAssignmentKind.Passenger, ids, forCompany, maximum, runtimeRoleDetector!, new ManualMissionCompletionPort()); selectedAssignmentId = assignment.AssignmentId;
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Assignment could not be staged in SaveGameData."); status = "Mission consist reserved: " + assignment.AssignmentId + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=assignment-reserved] assignment=" + assignment.AssignmentId + ", mission=" + assignment.MissionId + ", kind=" + assignment.Kind + ", operator=" + assignment.Operator.Key + ", assets=" + string.Join(",", assignment.AssetIds) + ", maximum=" + assignment.MaximumExpectedRevenue);
        }
        catch (Exception exception) { status = "Mission reservation refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=assignment-reserve-refused] " + exception); }
    }

    private static void StartMissionAssignment(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); TrySynchronizeHostWallet(entry, "before-assignment-start"); var assignment = runtimeStateProvider!.StartLocalAssignment("assignment-start:" + correlation, selectedAssignmentId!, hostWallet.ReadBalance(), runtimeRoleDetector!, new ManualMissionCompletionPort()); if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Assignment start could not be staged."); status = "Assignment active; complete the vanilla mission, then observe it here."; entry.Logger.Log("[correlation=" + correlation + "] [event=assignment-started] assignment=" + assignment.AssignmentId + ", vanillaBefore=" + assignment.VanillaBalanceBefore); } catch (Exception exception) { status = "Assignment start refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=assignment-start-refused] " + exception); }
    }

    private static void CompleteMissionAssignment(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            RequireHostAuthority(); var assignment = runtimeStateProvider!.Current!.Assignments.Single(x => x.AssignmentId == selectedAssignmentId); var assignmentResult = runtimeStateProvider.CompleteLocalAssignment("assignment-complete:" + correlation, assignment.AssignmentId, hostWallet.ReadBalance(), assignment.AssetIds, runtimeRoleDetector!, new ManualMissionCompletionPort());
            if (assignmentResult.ExternalSettlement == ExternalSettlementState.Pending && assignmentResult.ActualRevenue > 0) { if (!hostWallet.TryDebit(assignmentResult.ActualRevenue)) throw new InvalidOperationException("Company revenue was routed internally but could not be removed from the vanilla host wallet."); runtimeStateProvider.MarkLocalMissionSettlement(assignmentResult.AssignmentId, hostWallet.ReadBalance(), runtimeRoleDetector!, new ManualMissionCompletionPort()); runtimeStateProvider.SynchronizeLocalWallet("mission-wallet-settlement:" + correlation, hostWallet.ReadBalance(), "company-mission-settlement"); }
            if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Mission completion could not be staged in SaveGameData."); status = "Mission completed: revenue " + assignmentResult.ActualRevenue + " -> " + assignmentResult.Operator.Key + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=assignment-completed] assignment=" + assignmentResult.AssignmentId + ", mission=" + assignmentResult.MissionId + ", operator=" + assignmentResult.Operator.Key + ", revenue=" + assignmentResult.ActualRevenue + ", externalSettlement=" + assignmentResult.ExternalSettlement);
        }
        catch (Exception exception) { status = "Mission completion refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=assignment-complete-refused] " + exception); }
    }

    private static void RecoverMissionSettlement(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); var assignment = runtimeStateProvider!.Current!.Assignments.Single(x => x.AssignmentId == selectedAssignmentId); var observed = hostWallet.ReadBalance(); if (observed == assignment.VanillaBalanceAfter && assignment.ActualRevenue > 0 && !hostWallet.TryDebit(assignment.ActualRevenue)) throw new InvalidOperationException("Vanilla mission revenue cannot be removed."); var result = runtimeStateProvider.MarkLocalMissionSettlement(assignment.AssignmentId, hostWallet.ReadBalance(), runtimeRoleDetector!, new ManualMissionCompletionPort()); if (result.ExternalSettlement == ExternalSettlementState.Applied) runtimeStateProvider.SynchronizeLocalWallet("mission-wallet-recovery:" + correlation, hostWallet.ReadBalance(), "company-mission-recovery"); SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance); status = "Mission settlement recovery: " + result.ExternalSettlement + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=assignment-settlement-recovery] assignment=" + result.AssignmentId + ", state=" + result.ExternalSettlement + ", observed=" + hostWallet.ReadBalance()); } catch (Exception exception) { status = "Mission settlement recovery refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=assignment-settlement-recovery-refused] " + exception); }
    }

    private static void CancelMissionAssignment(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); var result = runtimeStateProvider!.CancelLocalAssignment("assignment-cancel:" + correlation, selectedAssignmentId!, runtimeRoleDetector!, new ManualMissionCompletionPort()); if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Assignment cancellation could not be staged."); status = "Assignment cancelled; consist released."; entry.Logger.Log("[correlation=" + correlation + "] [event=assignment-cancelled] assignment=" + result.AssignmentId + ", assets=" + string.Join(",", result.AssetIds)); } catch (Exception exception) { status = "Assignment cancellation refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=assignment-cancel-refused] " + exception); }
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
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); if (!int.TryParse(passengerDemand, out var demand) || !int.TryParse(passengerMaximumDemand, out var maximum) || !int.TryParse(passengerDemandGrowth, out var growth) || !long.TryParse(passengerFrequency, out var frequency) || !long.TryParse(passengerFare, out var fare) || !long.TryParse(passengerLatePenalty, out var penalty)) throw new InvalidOperationException("Passenger route parameters are invalid."); var route = runtimeStateProvider!.ConfigureLocalPassengerRoute("passenger-route:" + correlation, passengerRouteId, passengerOrigin, passengerDestination, demand, maximum, growth, frequency, fare, penalty, runtimeRoleDetector!, new ManualMissionCompletionPort()); if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Passenger route could not be staged in SaveGameData."); status = "Passenger route configured: " + route.RouteId + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=passenger-route-configured] route=" + route.RouteId + ", origin=" + route.OriginId + ", destination=" + route.DestinationId + ", demand=" + route.DemandUnits + ", maximum=" + route.MaximumDemandUnits + ", frequency=" + route.DesiredFrequencyTicks + ", fare=" + route.BaseFarePerPassenger + ", latePenalty=" + route.LatePenaltyPerTick); } catch (Exception exception) { status = "Passenger route refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=passenger-route-refused] " + exception); }
    }

    private static void RefreshPassengerDemand(UnityModManager.ModEntry entry, string routeId)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); var route = runtimeStateProvider!.RefreshLocalPassengerDemand("passenger-demand:" + correlation, routeId, runtimeRoleDetector!, new ManualMissionCompletionPort()); SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance); status = "Passenger demand refreshed: " + route.DemandUnits + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=passenger-demand-refreshed] route=" + route.RouteId + ", demand=" + route.DemandUnits + ", tick=" + runtimeStateProvider.Current!.LeaseClock.ActiveTick); } catch (Exception exception) { status = "Passenger demand refresh refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=passenger-demand-refused] " + exception); }
    }

    private static void ReservePassengerService(UnityModManager.ModEntry entry, bool forCompany)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); RequirePassengerJobsJob(passengerJobId); if (!int.TryParse(passengerCapacity, out var capacity) || !long.TryParse(passengerJourneyTicks, out var journey)) throw new InvalidOperationException("Passenger capacity or journey duration is invalid."); var now = runtimeStateProvider!.Current!.LeaseClock.ActiveTick; var contract = runtimeStateProvider.ReserveLocalPassengerService("passenger-reserve:" + correlation, "passenger:" + correlation, passengerRouteId, passengerJobId, SelectedOutboundAssetIds(runtimeStateProvider.Current), forCompany, capacity, now, checked(now + journey), runtimeRoleDetector!, new ManualMissionCompletionPort()); selectedPassengerContractId = contract.ContractId; if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Passenger service could not be staged in SaveGameData."); status = "Passenger service reserved: " + contract.BookedPassengers + " passenger(s)."; entry.Logger.Log("[correlation=" + correlation + "] [event=passenger-service-reserved] contract=" + contract.ContractId + ", job=" + contract.PassengerJobId + ", route=" + contract.RouteId + ", operator=" + contract.Operator.Key + ", assets=" + string.Join(",", contract.AssetIds) + ", booked=" + contract.BookedPassengers + ", capacity=" + contract.Capacity + ", quoteMax=" + contract.MaximumQuotedRevenue); } catch (Exception exception) { status = "Passenger service reservation refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=passenger-service-reserve-refused] " + exception); }
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
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); TrySynchronizeHostWallet(entry, "before-passenger-start"); var observed = hostWallet.ReadBalance(); var contract = runtimeStateProvider!.StartLocalPassengerService("passenger-start:" + correlation, selectedPassengerContractId!, observed, runtimeStateProvider.Current!.LeaseClock.ActiveTick, runtimeRoleDetector!, new ManualMissionCompletionPort()); SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance); status = "Passenger service observation active."; entry.Logger.Log("[correlation=" + correlation + "] [event=passenger-service-started] contract=" + contract.ContractId + ", vanillaBefore=" + observed + ", departureTick=" + contract.ActualDepartureTick); } catch (Exception exception) { status = "Passenger service start refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=passenger-service-start-refused] " + exception); }
    }

    private static void CompletePassengerService(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); var contract = runtimeStateProvider!.Current!.PassengerContracts.Single(x => x.ContractId == selectedPassengerContractId); var result = runtimeStateProvider.CompleteLocalPassengerService("passenger-complete:" + correlation, contract.ContractId, hostWallet.ReadBalance(), runtimeStateProvider.Current.LeaseClock.ActiveTick, contract.AssetIds, runtimeRoleDetector!, new ManualMissionCompletionPort()); var assignment = runtimeStateProvider.Current.Assignments.Single(x => x.AssignmentId == result.AssignmentId); if (assignment.ExternalSettlement == ExternalSettlementState.Pending) { var delta = hostWallet.ReadBalance() - assignment.ExpectedVanillaBalance; if (delta > 0 && !hostWallet.TryDebit(delta)) throw new InvalidOperationException("Passenger settlement was committed but the vanilla wallet debit failed."); if (delta < 0) hostWallet.Credit(-delta); runtimeStateProvider.MarkLocalMissionSettlement(assignment.AssignmentId, hostWallet.ReadBalance(), runtimeRoleDetector!, new ManualMissionCompletionPort()); } if (!SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("Passenger completion could not be staged in SaveGameData."); status = "Passenger service: " + result.State + "; paid=" + result.PaidRevenue + "; punctuality penalty=" + result.PunctualityPenalty + "."; entry.Logger.Log("[correlation=" + correlation + "] [event=passenger-service-completed] contract=" + result.ContractId + ", observedVanilla=" + result.ObservedVanillaRevenue + ", paid=" + result.PaidRevenue + ", penalty=" + result.PunctualityPenalty + ", arrivalTick=" + result.ActualArrivalTick + ", settlement=" + assignment.ExternalSettlement); } catch (Exception exception) { status = "Passenger completion refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=passenger-service-complete-refused] " + exception); }
    }

    private static void CancelPassengerService(UnityModManager.ModEntry entry)
    {
        var correlation = Guid.NewGuid().ToString("N"); try { RequireHostAuthority(); var result = runtimeStateProvider!.CancelLocalPassengerService("passenger-cancel:" + correlation, selectedPassengerContractId!, runtimeRoleDetector!, new ManualMissionCompletionPort()); SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance); status = "Passenger service cancelled; demand restored."; entry.Logger.Log("[correlation=" + correlation + "] [event=passenger-service-cancelled] contract=" + result.ContractId + ", booked=" + result.BookedPassengers + ", demandRestored=" + result.DemandRestored); } catch (Exception exception) { status = "Passenger cancellation refused: " + exception.Message; entry.Logger.Error("[correlation=" + correlation + "] [event=passenger-service-cancel-refused] " + exception); }
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
            GUILayout.Label("Condition after (0..1):");
            maintenanceCondition = GUILayout.TextField(maintenanceCondition ?? "1", 12);
            if (GUILayout.Button("Complete cost observation from current vanilla wallet")) CompleteOperatingCost(entry, open.SessionId);
            if (GUILayout.Button("Cancel observation (only if vanilla wallet is unchanged)")) CancelOperatingCost(entry, open.SessionId);
            return;
        }
        maintenanceActionIndex = GUILayout.Toolbar(maintenanceActionIndex, new[] { "Inspect", "Service", "Repair", "Refuel" });
        GUILayout.Label("Maximum authorized cost:");
        maintenanceMaximumCost = GUILayout.TextField(maintenanceMaximumCost ?? "1000", 20);
        GUILayout.Label("Condition before (0..1) and optional trip ID:");
        GUILayout.BeginHorizontal();
        maintenanceCondition = GUILayout.TextField(maintenanceCondition ?? "1", 12);
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
            if (!decimal.TryParse(maintenanceCondition, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var condition) || condition < 0m || condition > 1m) throw new InvalidOperationException("Condition must be between 0 and 1 using a decimal point.");
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
            if (!decimal.TryParse(maintenanceCondition, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var condition) || condition < 0m || condition > 1m) throw new InvalidOperationException("Condition must be between 0 and 1 using a decimal point.");
            var observedAfter = hostWallet.ReadBalance();
            var record = runtimeStateProvider!.CompleteLocalOperatingCost(sessionId, observedAfter, condition, runtimeRoleDetector!);
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

    private static void RequireHostAuthority()
    {
        if (runtimeRoleDetector == null) throw new InvalidOperationException("Host authority is unavailable.");
        if (!NetworkAuthorityPolicy.CanExecuteEconomy(runtimeRoleDetector.Detect(), out var reason))
            throw new InvalidOperationException(reason);
    }

    private static string BuildRemoteDispatchState(string transportIdentity)
    {
        RequireHostAuthority(); var snapshot = runtimeStateProvider?.Current ?? throw new InvalidOperationException("BDVM career state is unavailable."); var playerId = runtimeStateProvider!.LocalPlayerId!;
        var payload = new
        {
            schema = "bdvm.remote-dispatch", schemaVersion = 2, release = "2.0.0", transportIdentity, authorityActor = playerId,
            supportedIntents = new[] { "fleet.set-state", "fleet.rename", "company.create", "company.apply", "company.invite", "company.decide-application", "company.respond-invitation", "company.leave", "company.policy", "company.permission", "company.transfer-leadership", "wallet.transfer", "market.purchase", "initial-delivery.place", "assignment.cancel" },
            wallets = snapshot.Economy.Wallets.Select(x => new { account = x.Account.Key, x.Balance, x.Version }),
            companies = snapshot.Economy.Companies.Select(x => new { x.CompanyId, x.Name, x.LeaderId, members = x.Members.ToArray(), delegatedPermissions = x.DelegatedPermissions.ToDictionary(p => p.Key, p => p.Value.Select(v => v.ToString()).ToArray()), x.MembershipPolicy, x.Liquidating, x.Version }),
            membershipRequests = snapshot.Economy.MembershipRequests.Select(x => new { x.RequestId, kind = x.Kind.ToString(), state = x.State.ToString(), x.PlayerId, x.CompanyId, x.Version }),
            fleet = snapshot.Fleet.Select(x => new { x.AssetId, x.DisplayName, kind = x.Kind.ToString(), state = x.OperationalState.ToString(), owner = snapshot.Ownership.Single(o => o.AssetId == x.AssetId).Owner.Key, operatorRef = x.Operator?.Key, x.LastKnownLocation, x.Version }),
            market = snapshot.Market.Listings.Select(x => new { x.ListingId, kind = x.Kind.ToString(), state = x.State.ToString(), x.DefinitionId, x.LocationId, x.Price, x.ExpiresTick, x.Version }),
            catalog = snapshot.Market.Catalog.Select(x => new { x.DefinitionId, x.CategoryId, x.BasePrice, x.TransferFee, x.BuybackRate, x.Version }),
            marketStock = snapshot.Market.Stock.Select(x => new { x.LocationId, x.DefinitionId, x.Available, x.Capacity, x.Version }),
            initialDeliveries = snapshot.InitialDeliveries.Select(x => new { x.GrantId, owner = x.Owner.Key, x.AssetIds, x.DefinitionIds, state = x.State.ToString(), x.FreePlacement, x.TargetTrackId, targetKind = x.TargetKind?.ToString(), x.ResultCode, x.Version }),
            deliveryTracks = (runtimeSettings.InitialDeliveryTracks ?? new List<InitialDeliveryTrackRule>()).Select(x => new { x.TrackId, kind = x.Kind.ToString() }),
            operatingCosts = snapshot.OperatingCosts.Select(x => new { x.SessionId, x.AssetId, action = x.Action.ToString(), payer = x.Payer.Key, state = x.State.ToString(), x.MaximumAuthorizedCost, x.ReservedAmount, x.ActualCost, settlement = x.ExternalSettlement.ToString(), x.ResultCode }),
            leases = snapshot.Leases.Select(x => new { x.LeaseId, state = x.State.ToString(), x.AssetIds, lessee = x.Lessee?.Key, payer = x.Payer?.Key, x.HeldDeposit, x.RentAmount, x.NextDueTick, x.OutstandingDebt, x.Version }),
            assignments = snapshot.Assignments.Select(x => new { x.AssignmentId, x.MissionId, kind = x.Kind.ToString(), state = x.State.ToString(), x.AssetIds, operatorRef = x.Operator.Key, x.ActualRevenue, settlement = x.ExternalSettlement.ToString(), x.Version }),
            industrial = new { enabled = runtimeSettings.EnableIndustrialPilot, stocks = snapshot.IndustrialStocks.Select(x => new { x.FacilityId, x.CargoId, x.OnHand, x.Capacity, x.ReservedOutbound, x.ReservedInbound, x.Version }), contracts = snapshot.IndustrialContracts.Select(x => new { x.ContractId, x.OriginFacilityId, x.DestinationFacilityId, x.CargoId, x.Quantity, x.DeliveredQuantity, x.PaidAmount, state = x.State.ToString(), x.Version }) },
            outboundLeases = new { enabled = runtimeSettings.EnableOutboundLeasing, simulation = "declared-off-scene", contracts = snapshot.OutboundLeases.Select(x => new { x.ContractId, x.AssetIds, owner = x.Owner.Key, beneficiary = x.Beneficiary.Key, x.RentAmount, x.RentIntervalTicks, x.DurationTicks, x.StartTick, x.EndTick, state = x.State.ToString(), x.ConditionAtStart, x.ConditionAtReturn, x.ReturnLocation, x.Version }) },
            passengers = new { enabled = runtimeSettings.EnablePassengerEconomy, routes = snapshot.PassengerRoutes.Select(x => new { x.RouteId, x.OriginId, x.DestinationId, x.DemandUnits, x.MaximumDemandUnits, x.DesiredFrequencyTicks, x.PunctualityBasisPoints, x.Version }), contracts = snapshot.PassengerContracts.Select(x => new { x.ContractId, x.RouteId, x.PassengerJobId, x.AssetIds, operatorRef = x.Operator.Key, x.Capacity, x.BookedPassengers, x.MaximumQuotedRevenue, x.ObservedVanillaRevenue, x.PunctualityPenalty, x.PaidRevenue, state = x.State.ToString(), x.Version }) },
            dynamicEconomy = new { enabled = runtimeSettings.EnableDynamicEconomy, metrics = snapshot.DynamicEconomy.Metrics.Select(x => new { x.CategoryId, x.SupplyRatio, x.DemandRatio, x.UtilizationRatio, x.LessorAvailabilityRatio, x.RawFactor, x.SmoothedFactor, x.CalculatedTick, x.Version }), profitability = snapshot.DynamicEconomy.Profitability.Select(x => new { x.AssetId, x.OperatingRevenue, x.OperatingCosts, x.NetOperatingResult, x.AcquisitionCash, x.CompletedServices, x.Version }) },
            assetLifecycle = new { enabled = runtimeSettings.EnableAssetLifecycle, cleanupProtectionAdapter = "CarVisitChecker.IsRecentlyVisited-exact-CarGUID-host-only", records = snapshot.AssetLifecycle.Records.Select(x => new { x.AssetId, status = x.Status.ToString(), protection = x.ProtectionStatus.ToString(), x.LastKnownMapRevision, x.LastKnownTrackId, x.Detail, x.Version }) },
            financing = new { enabled = runtimeSettings.EnableFinancing, pools = snapshot.Financing.Pools.Select(x => new { x.PoolId, x.AvailableCapital, x.InitialCapital, x.ReceivedPayments, x.WrittenOff, x.Version }), contracts = snapshot.Financing.Contracts.Select(x => new { x.ContractId, kind = x.Kind.ToString(), debtor = x.Debtor.Key, x.PrincipalLimit, x.ReservedCapital, x.OutstandingPrincipal, x.AccruedInterest, x.InterestBasisPoints, x.MinimumInstallment, x.IntervalTicks, x.NextDueTick, x.MaturityTick, x.GuaranteeAmount, x.HeldGuarantee, state = x.State.ToString(), x.Terms, x.Version }) },
            triageAssistance = new { enabled = runtimeSettings.EnableTriageAssistance, executionAdapter = "disabled-until-public-selfshunt-hook-is-proven", plans = snapshot.TriageAssistance.Plans.Select(x => new { x.PlanId, x.AssignmentId, level = x.Level.ToString(), x.AssetIds, x.OrderedTrackIds, state = x.State.ToString(), x.ResultCode, x.Version }) }
        };
        return Newtonsoft.Json.JsonConvert.SerializeObject(payload);
    }

    private static string HandleRemoteDispatchIntent(string transportIdentity, string payload)
    {
        RequireHostAuthority(); if (payload.Length > 4096) throw new ArgumentException("BDVM intent payload is too large."); var body = Newtonsoft.Json.Linq.JObject.Parse(payload); var action = (string?)body["action"] ?? ""; var correlation = (string?)body["correlationId"] ?? Guid.NewGuid().ToString("N"); if (correlation.Length > 96) throw new ArgumentException("Correlation ID is too long.");
        object result; var saveStaged = false;
        if (action == "fleet.set-state")
        {
            var assetId = (string?)body["assetId"] ?? ""; if (!Enum.TryParse((string?)body["state"], true, out FleetOperationalState target)) throw new ArgumentException("Invalid fleet state.");
            var record = runtimeStateProvider!.ManageLocalFleet("remote-fleet:" + correlation, assetId, FleetCommandAction.SetOperationalState, runtimeRoleDetector!, target); result = new { action, record.Outcome, record.ResultCode, record.AssetId, record.FleetVersionAfter };
        }
        else if (action == "fleet.rename")
        {
            var assetId = (string?)body["assetId"] ?? ""; var displayName = (string?)body["displayName"] ?? "";
            var record = runtimeStateProvider!.ManageLocalFleet("remote-fleet-rename:" + correlation, assetId, FleetCommandAction.Rename, runtimeRoleDetector!, displayName: displayName); result = new { action, record.Outcome, record.ResultCode, record.AssetId, record.FleetVersionAfter };
        }
        else if (action == "company.create")
        {
            var name = (string?)body["name"] ?? "";
            var record = runtimeStateProvider!.CreateCompanyFor("remote-company-create:" + correlation, runtimeStateProvider.LocalPlayerId!, name); result = new { action, record.State, record.ResultCode, record.CompanyId };
        }
        else if (action == "company.apply")
        {
            var companyId = (string?)body["companyId"] ?? ""; var record = runtimeStateProvider!.ApplyToCompanyFor("remote-company-apply:" + correlation, runtimeStateProvider.LocalPlayerId!, companyId, runtimeRoleDetector!); result = new { action, record.State, record.ResultCode, record.CompanyId };
        }
        else if (action == "company.invite")
        {
            var companyId = (string?)body["companyId"] ?? ""; var targetPlayerId = (string?)body["targetPlayerId"] ?? ""; var record = runtimeStateProvider!.InvitePlayerFor("remote-company-invite:" + correlation, runtimeStateProvider.LocalPlayerId!, companyId, targetPlayerId, runtimeRoleDetector!); result = new { action, record.State, record.ResultCode, record.CompanyId };
        }
        else if (action == "company.decide-application")
        {
            var requestId = (string?)body["requestId"] ?? ""; var accept = (bool?)body["accept"] ?? false; var record = runtimeStateProvider!.DecideApplicationFor("remote-company-decision:" + correlation, runtimeStateProvider.LocalPlayerId!, requestId, accept, runtimeRoleDetector!); result = new { action, record.State, record.ResultCode, record.CompanyId };
        }
        else if (action == "company.respond-invitation")
        {
            var requestId = (string?)body["requestId"] ?? ""; var accept = (bool?)body["accept"] ?? false; var record = runtimeStateProvider!.RespondToInvitationFor("remote-company-response:" + correlation, runtimeStateProvider.LocalPlayerId!, requestId, accept, runtimeRoleDetector!); result = new { action, record.State, record.ResultCode, record.CompanyId };
        }
        else if (action == "company.leave")
        {
            var record = runtimeStateProvider!.LeaveCompanyFor("remote-company-leave:" + correlation, runtimeStateProvider.LocalPlayerId!, runtimeRoleDetector!); result = new { action, record.State, record.ResultCode, record.CompanyId };
        }
        else if (action == "company.policy")
        {
            var companyId = (string?)body["companyId"] ?? ""; if (!Enum.TryParse((string?)body["policy"], true, out MembershipPolicy policy)) throw new ArgumentException("Invalid membership policy.");
            var record = runtimeStateProvider!.SetMembershipPolicyFor("remote-company-policy:" + correlation, runtimeStateProvider.LocalPlayerId!, companyId, policy, runtimeRoleDetector!); result = new { action, record.State, record.ResultCode, record.CompanyId };
        }
        else if (action == "company.permission")
        {
            var companyId = (string?)body["companyId"] ?? ""; var memberId = (string?)body["memberId"] ?? ""; var enabled = (bool?)body["enabled"] ?? false; if (!Enum.TryParse((string?)body["permission"], true, out CompanyPermission permission)) throw new ArgumentException("Invalid company permission.");
            var record = runtimeStateProvider!.SetPermissionFor("remote-company-permission:" + correlation, runtimeStateProvider.LocalPlayerId!, companyId, memberId, permission, enabled, runtimeRoleDetector!); result = new { action, record.State, record.ResultCode, record.CompanyId };
        }
        else if (action == "company.transfer-leadership")
        {
            var companyId = (string?)body["companyId"] ?? ""; var memberId = (string?)body["memberId"] ?? ""; var record = runtimeStateProvider!.TransferLeadershipFor("remote-company-leadership:" + correlation, runtimeStateProvider.LocalPlayerId!, companyId, memberId, runtimeRoleDetector!); result = new { action, record.State, record.ResultCode, record.CompanyId };
        }
        else if (action == "wallet.transfer")
        {
            var amount = (long?)body["amount"] ?? 0; var toCompany = (bool?)body["toCompany"] ?? false; if (amount <= 0) throw new ArgumentException("Transfer amount must be a positive whole number.");
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
        else if (action == "market.purchase")
        {
            var listingId = (string?)body["listingId"] ?? ""; var forCompany = (bool?)body["forCompany"] ?? false; var listing = runtimeStateProvider!.Current!.Market.Listings.Single(x => x.ListingId == listingId); var externalDebited = false;
            if (!forCompany && listing.Price > 0) { if (!hostWallet.TryDebit(listing.Price)) throw new InvalidOperationException("The authoritative personal wallet has insufficient funds."); externalDebited = true; }
            var record = runtimeStateProvider.PurchaseLocalMarket("remote-market-purchase:" + correlation, listingId, forCompany, runtimeRoleDetector!, new UnityExistingVehicleOwnershipAdapter(), new DisabledMarketDeliveryPort());
            if (externalDebited && (record.State == MarketPurchaseState.Rejected || record.State == MarketPurchaseState.Compensated)) hostWallet.Credit(listing.Price);
            result = new { action, record.State, record.ResultCode, record.ListingId, record.AssetId, record.DeliveryOperationId };
        }
        else if (action == "initial-delivery.place")
        {
            var grantId = (string?)body["grantId"] ?? ""; var trackId = (string?)body["trackId"] ?? ""; if (!Enum.TryParse((string?)body["targetKind"], true, out InitialDeliveryTargetKind targetKind)) throw new ArgumentException("Invalid initial delivery target kind.");
            var record = runtimeStateProvider!.PlaceLocalInitialDelivery("remote-initial-delivery:" + correlation, grantId, trackId, targetKind, runtimeRoleDetector!, new UnityInitialDeliveryAdapter(runtimeSettings.InitialDeliveryTracks)); result = new { action, record.State, record.ResultCode, record.GrantId, record.AssetIds, record.TargetTrackId };
        }
        else if (action == "assignment.cancel")
        {
            var assignmentId = (string?)body["assignmentId"] ?? ""; var record = runtimeStateProvider!.CancelLocalAssignment("remote-assignment-cancel:" + correlation, assignmentId, runtimeRoleDetector!, new ManualMissionCompletionPort()); result = new { action, record.AssignmentId, state = record.State.ToString(), record.ResultCode };
        }
        else throw new ArgumentException("Unsupported BDVM intent.");
        if (!saveStaged && !SaveGameRuntimeHook.TryOnUpdateInternalData(SaveGameManager.Instance)) throw new InvalidOperationException("BDVM intent succeeded in memory but could not be staged in SaveGameData.");
        mod?.Logger.Log("[correlation=" + correlation + "] [event=remote-dispatch-intent] transportIdentity=" + transportIdentity + ", authorityActor=" + runtimeStateProvider!.LocalPlayerId + ", action=" + action);
        return Newtonsoft.Json.JsonConvert.SerializeObject(new { status = "succeeded", correlationId = correlation, result });
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
}
