using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BDVM.Adapters;
using BDVM.Domain;

internal static class Program
{
    private static int failures;
    private static int tests;

    private static void TestIndustrialWalletRecovery()
    {
        var account = AccountRef.Player("p");
        var economy = new CompanyEconomySnapshot { CheckpointId = "recovery" };
        economy.Players.Add(new PlayerEconomicState { PlayerId = "p" });
        economy.Wallets.Add(new Wallet { Account = account, Balance = 2000, Version = 1 });
        economy.Ledger.Add(new LedgerEntry { EntryId = "delivery", Kind = LedgerEntryKind.IndustrialRevenue, Credit = account, Amount = 337 });
        economy.Ledger.Add(new LedgerEntry { EntryId = "sync", Kind = LedgerEntryKind.ExternalWalletSync, Debit = account, Amount = 337, Detail = "source=before-economic-clock;previous=2337;current=2000" });
        var mirror = new ExternalWalletMirrorEngine(economy);
        mirror.Complete("p", 2000, "baseline");
        Check(IndustrialWalletRecovery.RestoreErasedCredits(economy, "p") == 337 && economy.Wallets[0].Balance == 2337, "proven erased industrial payment recovered");
        Check(IndustrialWalletRecovery.RestoreErasedCredits(economy, "p") == 0, "recovery receipt prevents duplicate payment");
        var restored = CompanyEconomyPersistence.Deserialize(CompanyEconomyPersistence.Serialize(economy), "recovery");
        Check(IndustrialWalletRecovery.RestoreErasedCredits(restored, "p") == 0 && restored.Wallets[0].Balance == 2337, "recovery receipt survives save and reload");
        Check(mirror.Plan("p", 2000, "credit").Action == ExternalWalletMirrorAction.CreditExternal, "industrial gain exports to native wallet");
        Check(mirror.Plan("p", 2337, "retry").Action == ExternalWalletMirrorAction.None, "retry after native credit never pays twice");
        mirror.Complete("p", 2337, "receipt");
        economy.Ledger.Add(new LedgerEntry { EntryId = "other-delivery", Kind = LedgerEntryKind.IndustrialRevenue, Credit = account, Amount = 337 });
        economy.Ledger.Add(new LedgerEntry { EntryId = "ambiguous", Kind = LedgerEntryKind.ExternalWalletSync, Debit = account, Amount = 100, Detail = "source=before-economic-clock;previous=2437;current=2337" });
        Check(IndustrialWalletRecovery.RestoreErasedCredits(economy, "p") == 0, "ambiguous history is not reimbursed");
        economy.Wallets[0].Balance += 100;
        Check(mirror.Plan("p", 2300, "conflict").Action == ExternalWalletMirrorAction.Conflict, "concurrent external spending is never overwritten");
    }

    private static void TestGameplayPricing()
    {
        var near = IndustrialHaulPricing.PerLoad(1000, 40000, 0, 38000, 0, 1);
        var far = IndustrialHaulPricing.PerLoad(20000, 40000, 0, 38000, 0, 1);
        Check(near >= 1000 && far > near && far == IndustrialHaulPricing.PerLoad(20000, 40000, 0, 38000, 0, 1), "haul service floor and deterministic distance pricing");
        Check(GameplayCatalogPrices.BasePrice("LocoDE2", FleetVehicleKind.Locomotive) < GameplayCatalogPrices.BasePrice("LocoDH4", FleetVehicleKind.Locomotive) && GameplayCatalogPrices.BasePrice("LocoDH4", FleetVehicleKind.Locomotive) < GameplayCatalogPrices.BasePrice("LocoDE6", FleetVehicleKind.Locomotive), "locomotive price tiers");
        var market = new FiniteMarketState();
        var entry = new MarketCatalogEntry { DefinitionId = "LocoDH4", BasePrice = 150000 };
        var available = new MarketListing { DefinitionId = entry.DefinitionId, Kind = MarketListingKind.NewOrder, State = MarketListingState.Available, Condition = 1, MarketFactor = 1.2m };
        var reserved = new MarketListing { DefinitionId = entry.DefinitionId, Kind = MarketListingKind.NewOrder, State = MarketListingState.Reserved, Price = 180000 };
        market.Listings.Add(available); market.Listings.Add(reserved);
        GameplayCatalogPrices.UpgradeLegacyDefault(market, entry, FleetVehicleKind.Locomotive);
        Check(entry.BasePrice == 90000 && available.Price == 99000 && reserved.Price == 180000, "migration updates only available new orders and preserves reserved quotes");
        var version = entry.Version; GameplayCatalogPrices.UpgradeLegacyDefault(market, entry, FleetVehicleKind.Locomotive);
        Check(entry.Version == version, "catalog migration is idempotent");
        var custom = new MarketCatalogEntry { DefinitionId = "LocoDH4", BasePrice = 12345 };
        GameplayCatalogPrices.UpgradeLegacyDefault(market, custom, FleetVehicleKind.Locomotive);
        Check(custom.BasePrice == 12345, "custom prices preserved");
    }

    private static void Main()
    {
        Run(CompanyRollingStockAccessChecks.Run);
        Run(TestIndustrialWalletRecovery);
        Run(TestGameplayPricing);
        Run(TestNetworkRoleMatrix);
        Run(PersistentJournalChecks.Run);
        Run(TestPersistentSystemDocuments);
        Run(TestPeriodicEconomicProjection);
        Run(TestPersistentRuntimeJournal);
        Run(TestDetachedRuntimeState);
        Run(TestIncrementalRuntimeState);
        Run(TestAutomaticProductionSkipsIdleCommands);
        Run(TestPersistentMultiplayerPlayerIdentity);
        Run(TestNetworkRoleAdapterUsesInjectedApiState);
        Run(TestEconomyRefusedForClientAndIndeterminateRole);
        Run(TestAuthoritativeExportKeepsDefinitionsSeparateFromInstances);
        Run(TestClientAuthoritativeExportIsRefusedBeforeReadersAndWriter);
        Run(TestClientVisibilityObservationIsExplicitlyNonAuthoritative);
        Run(TestCorrelationIsSharedByReadersWriterAndTrace);
        Run(TestAssetIdentitySurvivesReconciliation);
        Run(TestFleetLocationProjectionUsesExactPhysicalIdentity);
        Run(TestInvalidPersistenceDataIsRejected);
        Run(TestDuplicatePersistentIdentityIsAmbiguous);
        Run(TestTemporarilyAbsentAssetRetainsIdentity);
        Run(TestDefinitionConflictIsAmbiguous);
        Run(TestMissingLinkAndDuplicateObservationAreExplicit);
        Run(TestCompanyCreationMembershipAndDelegation);
        Run(TestExplicitAccountsIsolationAndMissionRouting);
        Run(TestConcurrentRetryDebitsExactlyOnce);
        Run(TestCheckpointPersistenceAndSaveIsolation);
        Run(TestLiquidationPricingDebtAndFrozenDistribution);
        Run(TestAntiAbuseHistoryAndLicenseEconomicPolicyPersist);
        Run(TestControlledLeaveAndCircularTransferGuard);
        Run(TestFailedCommandRollsBackAndRetriesSameResult);
        Run(TestExplicitResolvedSelectionAndPlayerPurchase);
        Run(TestCompanyPurchaseRequiresMembershipPermissionAndVersions);
        Run(TestConcurrentPlayersAndCompaniesProduceOneWinner);
        Run(TestRetryReturnsOneDebitAndOneOwnershipTransfer);
        Run(TestWorldFailureCompensatesOrRemainsReconcileable);
        Run(TestInterruptedPurchasePersistsAndReconciles);
        Run(TestAcquisitionAuditAndDiagnosticAreComplete);
        Run(TestAcquisitionRefusesClientBeforeReservation);
        Run(TestEveryInjectedFailureIsCompensatedOrReconciled);
        Run(TestIncrementCheckpointRestoresCompletedTransactionExactlyOnce);
        Run(TestIncrementCheckpointReconcilesDebitBeforeOwnership);
        Run(TestCheckpointRejectsOtherBranchAndCorruption);
        Run(TestMaintenanceRequiresManualConfirmationAndExplicitPayer);
        Run(TestLicenseStableIdsAndCombinedPersonalQuote);
        Run(TestLicenseConcurrentRetryDebitsOnce);
        Run(TestLicenseCompanyPayerRequiresPermission);
        Run(TestLicenseDepositRefundsOnce);
        Run(TestLicenseCheckpointRoundTrip);
        Run(TestLicenseQuoteSurvivesRuleModification);
        Run(TestLicenseMissingInvalidAndUnknownCategoryAreExplicitlyUnblocked);
        Run(TestProtocolCodecAndAllIntentTypes);
        Run(TestProtocolVersionLimitsAndNonFiniteValuesFailClosed);
        Run(TestProtocolAuthenticatesReadyPeerAndSeparatesMessageKinds);
        Run(TestAuthenticatedTransportActorRoutingFailsClosed);
        Run(TestRuntimeActorAwareOperationsCannotCrossPlayerBoundary);
        Run(TestProtocolDuplicateConcurrencyAndLostResultAreExactlyOnce);
        Run(TestProtocolRetryTimeoutReconnectAndOrdering);
        Run(TestAuthoritativeStateTransferIsBoundedFrozenAndActorScoped);
        Run(TestSaveGameFeatureFlagDefaultsOff);
        Run(TestSaveGameAutomaticUpdateGate);
        Run(TestSaveGameCreateLoadMigrationAndRecovery);
        Run(TestSaveGameCorruptionInterruptionRollbackAndIsolation);
        Run(TestRuntimeStateProviderBootstrapsRestoresAndRejectsOtherBranch);
        Run(TestRuntimeStatePayloadCommitRejectsStaleWorkers);
        Run(TestCareerIdentityFallsBackToCurrentSessionMetadata);
        Run(TestSavePayloadFactoryReceivesResolvedCheckpointAndExistingPayload);
        Run(TestRuntimeSettingsFailClosed);
        Run(TestStartingCapital);
        Run(TestVerticalSliceCreatesOnePersistentZeroBalanceCompany);
        Run(TestRuntimeWalletMigrationSynchronizationAndCompanyTransfers);
        Run(TestExternalWalletMirrorReconcilesOneSidedChangesAndRefusesConflicts);
        Run(TestRuntimeVisibleOfferAndAcquisitionFlow);
        Run(TestFleetClassificationAndPersistentManagement);
        Run(TestFleetOwnershipTransferAndAuthority);
        Run(TestHostPhysicalRemovalLeavesAuditButRemovesFleetEntry);
        Run(TestFleetSnapshotV1MigratesToV2);
        Run(TestVehicleResaleCreditsOnceAndPersistsAudit);
        Run(TestVehicleResaleRefusesUnsafeOrClientSale);
        Run(TestBundleResaleTransfersEveryComponentAndCreditsOnce);
        Run(TestBundleResalePreflightIsAtomicAndRecoveryIsIdempotent);
        Run(TestVehicleResaleRecoversAfterCreditWithoutDoublePayment);
        Run(TestConcurrentVehicleResalesProduceOneCredit);
        Run(TestGovernanceCommandsAreVersionedIdempotentAndPermissionChecked);
        Run(TestGovernanceRuntimeRequiresHostAuthority);
        Run(TestGovernanceClosesStaleMembershipRequestsAndPersists);
        Run(TestGovernanceCommandIdsRejectChangedPayloadAfterReload);
        Run(TestGovernanceNoOpsPreserveVersionsAndPermissions);
        Run(TestIndependentLeaveIsHostOnlyIdempotentAndPersistent);
        Run(TestDissolutionClosesPendingMembershipRequests);
        Run(TestCompanyLiquidationSellsAssetsAndDistributesAfterLiabilities);
        Run(TestCompanyLiquidationPreflightAndRecoveryAreAtomic);
        Run(TestCompanyLiquidationWriteAheadCheckpointSurvivesCrashWindow);
        Run(TestProtocolV1IntentRemainsCompatible);
        Run(TestProtocolV2IntentRemainsCompatible);
        Run(TestConcurrentGovernanceVersionsAllowOnePermissionMutation);
        Run(TestRuntimeGovernanceIntentIsHostExecutedAndStaged);
        Run(TestOperatingCostPersonalAndCompanyPayersDoNotDoubleCharge);
        Run(TestOperatingCostLimitsAndPersistenceFailClosed);
        Run(TestOperatingCostInterruptionAuthorityAndFleetGuards);
        Run(TestFiniteMarketPricesStockExpiryAndReloadAreDeterministic);
        Run(TestFiniteMarketConcurrentPurchaseDebitsAndTransfersOnce);
        Run(TestFiniteMarketDeliveryRecoveryCreatesOneAsset);
        Run(TestFiniteMarketWriteAheadCheckpointAndVirtualRecovery);
        Run(TestStarterBundleGrantAndAtomicPlacement);
        Run(TestInitialDeliveryWriteAheadCheckpointPreventsDuplicateSpawn);
        Run(TestFiniteMarketBuybackUsesConfiguredMargin);
        Run(TestLeaseDepositClockReloadAndReturn);
        Run(TestLeaseDelinquencyAndUnsafeReturn);
        Run(TestLeasePurchaseOptionAndClientRefusal);
        Run(TestCatalogLeaseConsumesFiniteListingAndGrantsPlacement);
        Run(TestMissionAssignmentPaysIndependentExactlyOnce);
        Run(TestMissionAssignmentRoutesFrozenCompanyRevenue);
        Run(TestMissionSettlementUsesExactJobRevenue);
        Run(TestMissionAssignmentPartialBlockedCancelAndClient);
        Run(TestMissionAssignmentRequiresAuthoritativeLifecycle);
        Run(TestMissionCompletionPendingRetriesSameCommand);
        Run(TestMissionAssignmentRefusesExpiredLeaseBeforeReserveOrStart);
        Run(TestIndustrialReservationPartialDeliveryAndReload);
        Run(TestIndustrialCancellationAndDuplicateUnload);
        Run(TestIndustrialShortageAndRuntimeGate);
        Run(TestOutboundLeaseCreditsExactlyOnceAndReturnsAfterReload);
        Run(TestOutboundLeaseGuardsBundlesAndAuthority);
        Run(TestOutboundLeaseRecallRecoveryAndCompanyLiquidationCancellation);
        Run(TestPassengerDemandCapacityPunctualityAndSinglePayment);
        Run(TestPassengerCompletionPendingRetriesSameCommand);
        Run(TestPassengerExactSettlementPreservesUnrelatedWalletChanges);
        Run(TestPassengerCancellationReloadAndPartialArrival);
        Run(TestPassengerCompanyRoutingAndClientAuthority);
        Run(TestDynamicMarketUsesSupplyDemandUtilizationAndFreezesOffers);
        Run(TestDynamicMarketIsBoundedSmoothedAndCannotReroll);
        Run(TestAssetProfitabilitySupportsMultipleFleetStrategies);
        Run(TestDedicatedAuthorityRestartsAndAdvancesWhileEmpty);
        Run(TestDedicatedAuthorityAuthenticatesLateJoinAndMultipleCompanies);
        Run(TestDedicatedAuthorityRollsBackFailedCheckpointAndRefusesClient);
        Run(TestLifecycleCoversVanillaCclFreightPassengerAndBundles);
        Run(TestLifecycleProtectionPolicyScopesOwnedAndContractedCars);
        Run(TestLifecycleSuspendsMissingContentAndStaleLocationsWithoutRespawn);
        Run(TestLifecycleProtectsOwnedAssetsButLeavesExternalTrafficAlone);
        Run(TestWorldPopulationPolicyAllowsOnlyAuthorizedSources);
        Run(TestOfflineRollingStockPopulationMatrix);
        Run(TestValidationRuntimeSettingsLoads);
        Run(TestDomainBoundaryHasNoGameOrUiAssemblyDependency);
        Run(TestFinancingUsesBackedPoolAndRepaysExactlyOnce);
        Run(TestFinancingReloadDefaultAndCircularRefinancingGuard);
        Run(TestFinancingCompanyLiquidationWritesOffWithoutDeadlock);
        Run(TestTriagePlanningNeverCreatesDriverOrRevenue);
        Run(TestTriageBlockedIncompleteClientAndReloadRecovery);
        Run(TestCompanyWorkflowCancellationCoversEveryContractBeforeDistribution);
        Run(TestCompanyWorkflowCancellationWaitsForUnknownPhysicalTransitions);
        if (failures != 0) Environment.Exit(1);
        Console.WriteLine($"BDVM tests passed: {tests} tests.");
    }

    private static void TestIncrementCheckpointRestoresCompletedTransactionExactlyOnce()
    {
        var fixture = Acquisition("w008-complete", 100, WorldOwnershipOutcome.Applied);
        var state = fixture.Snapshot;
        var world = new FakeWorld(WorldOwnershipOutcome.Applied);
        var store = new MemoryCheckpointStore();
        var service = new IncrementCheckpointService(store);
        var engine = new VehicleAcquisitionEngine(state, world, RoleDetector(NetworkRole.MultiplayerHost, true), service);
        var command = Buy("w008-command", "p", fixture, AssetOwnerRef.Player("p"), AccountRef.Player("p"));
        var first = engine.Acquire(command);
        var restored = service.Restore("w008-complete");
        var retryWorld = new FakeWorld(WorldOwnershipOutcome.Unknown);
        var retry = new VehicleAcquisitionEngine(restored, retryWorld, RoleDetector(NetworkRole.MultiplayerHost, true), service).Acquire(command);
        Check(first.State == AcquisitionState.Succeeded && retry.State == AcquisitionState.Succeeded, "completed acquisition restores with the same result");
        Check(restored.Economy.Wallets.Single(x => x.Account.Key == "Player:p").Balance == 60 &&
            restored.Economy.Ledger.Count(x => x.EntryId == "w008-command:debit") == 1 && retryWorld.ApplyCalls == 0,
            "restore and retry neither recreate, redibit nor reapply ownership");
    }

    private static void TestIncrementCheckpointReconcilesDebitBeforeOwnership()
    {
        var fixture = Acquisition("w008-interrupted", 100, WorldOwnershipOutcome.Applied);
        var state = fixture.Snapshot;
        var store = new MemoryCheckpointStore();
        var service = new IncrementCheckpointService(store);
        var interrupted = new VehicleAcquisitionEngine(state, new FakeWorld(WorldOwnershipOutcome.Applied), RoleDetector(NetworkRole.MultiplayerHost, true), service)
            { FailurePoint = AcquisitionFailurePoint.BeforeWorldTransfer };
        interrupted.Acquire(Buy("w008-interrupted-command", "p", fixture, AssetOwnerRef.Player("p"), AccountRef.Player("p")));
        var restored = service.Restore("w008-interrupted");
        var actualWorld = new FakeWorld(WorldOwnershipOutcome.Applied);
        var resumed = new VehicleAcquisitionEngine(restored, actualWorld, RoleDetector(NetworkRole.MultiplayerHost, true), service);
        var results = service.ReconcilePending(resumed);
        Check(results.Single().State == AcquisitionState.Succeeded && actualWorld.ApplyCalls == 0, "debit-to-ownership interruption reconciles from observed owner");
        Check(restored.Economy.Wallets.Single(x => x.Account.Key == "Player:p").Balance == 60 &&
            restored.Economy.Ledger.Count(x => x.EntryId == "w008-interrupted-command:debit") == 1,
            "reconciliation preserves one debit and one ledger entry");
    }

    private static void TestCheckpointRejectsOtherBranchAndCorruption()
    {
        var store = new MemoryCheckpointStore(); var service = new IncrementCheckpointService(store);
        service.Save("branch-a", Acquisition("branch-a", 100, WorldOwnershipOutcome.Applied).Snapshot, AcquisitionState.Succeeded);
        var mismatch = false; try { service.Restore("branch-b"); } catch (System.IO.InvalidDataException) { mismatch = true; }
        store.Envelope!.PayloadSha256 = "corrupted";
        var corrupt = false; try { service.Restore("branch-a"); } catch (System.IO.InvalidDataException) { corrupt = true; }
        Check(mismatch && corrupt, "cross-branch and corrupted checkpoint payloads fail closed");
    }

    private static void TestDomainBoundaryHasNoGameOrUiAssemblyDependency()
    {
        var forbidden = new[] { "UnityEngine", "UnityModManager", "Harmony", "MPAPI", "RemoteDispatch", "SelfShunt", "PassengerJobs", "DLE" };
        var references = typeof(VehicleAcquisitionSnapshot).Assembly.GetReferencedAssemblies().Select(x => x.Name ?? "").ToArray();
        Check(!references.Any(reference => forbidden.Any(prefix => reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))),
            "domain test boundary has no game, UI, transport or optional-mod assembly dependency");
    }

    private static void TestFinancingUsesBackedPoolAndRepaysExactlyOnce()
    {
        var fixture = Acquisition("finance-backed", 50, WorldOwnershipOutcome.Applied); var engine = new FinancingEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true));
        engine.RegisterPool("pool-create", "bank", 1000);
        var offer = engine.OfferCredit("loan-offer", "p", "loan-1", FinancingKind.Loan, AccountRef.Player("p"), "bank", 100, 1000, 20, 10, 40, 10);
        var results = new FinancingCommandRecord[12]; Parallel.For(0, results.Length, i => results[i] = engine.Accept("loan-accept", "p", offer.ContractId)); var accepted = results[0]; var retry = engine.Accept("loan-accept", "p", offer.ContractId);
        Check(accepted.ResultCode == "financing-accepted" && results.All(x => object.ReferenceEquals(x, accepted)) && object.ReferenceEquals(accepted, retry) && fixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Player:p").Balance == 140, "concurrent loan acceptance reserves a guarantee and credits one backed principal exactly once");
        Check(fixture.Snapshot.Financing.Pools.Single().AvailableCapital == 900 && offer.OutstandingPrincipal == 100 && offer.HeldGuarantee == 10, "loan principal leaves the explicit lender pool and remains a separate liability");
        engine.ProcessClock("finance-clock-10", 10);
        Check(offer.OutstandingPrincipal == 80 && offer.AccruedInterest == 0 && fixture.Snapshot.Financing.Pools.Single().AvailableCapital == 930, "interest and installment are paid to the pool without a second economic authority");
    }

    private static void TestFinancingReloadDefaultAndCircularRefinancingGuard()
    {
        var fixture = Acquisition("finance-reload", 0, WorldOwnershipOutcome.Applied); var host = RoleDetector(NetworkRole.MultiplayerHost, true); var engine = new FinancingEngine(fixture.Snapshot, host);
        engine.RegisterPool("pool-create-reload", "bank", 200); engine.OfferCredit("line-offer", "p", "line-1", FinancingKind.CreditLine, AccountRef.Player("p"), "bank", 100, 500, 25, 10, 20, 0); engine.Accept("line-accept", "p", "line-1"); engine.Draw("line-draw", "p", "line-1", 60);
        var circularRefused = false; try { engine.OfferCredit("second-offer", "p", "line-2", FinancingKind.Loan, AccountRef.Player("p"), "bank", 20, 0, 10, 10, 20, 0); } catch (InvalidOperationException) { circularRefused = true; }
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), "finance-reload"); var restoredEngine = new FinancingEngine(restored, host); restored.Economy.Wallets.Single(x => x.Account.Key == "Player:p").Balance = 0; restoredEngine.ProcessClock("line-clock", 10);
        var clientRefused = false; try { new FinancingEngine(restored, RoleDetector(NetworkRole.MultiplayerClient, false)).Repay("client-repay", "p", "line-1", 1); } catch (InvalidOperationException) { clientRefused = true; }
        Check(circularRefused && clientRefused && restored.Financing.Contracts.Single().State == FinancingState.Defaulted, "circular refinancing, client mutation and unpaid due date fail closed after reload");
    }

    private static void TestFinancingCompanyLiquidationWritesOffWithoutDeadlock()
    {
        var fixture = Acquisition("finance-liquidation", 0, WorldOwnershipOutcome.Applied); var economy = new CompanyEconomyEngine(fixture.Snapshot.Economy); economy.CreateCompany(Command("finance-company", "p", "co"), "Finance Co"); var host = RoleDetector(NetworkRole.MultiplayerHost, true); var engine = new FinancingEngine(fixture.Snapshot, host);
        engine.RegisterPool("liquidation-pool", "bank", 500); engine.OfferCredit("company-loan-offer", "p", "company-loan", FinancingKind.Loan, AccountRef.Company("co"), "bank", 100, 0, 100, 10, 20, 0); engine.Accept("company-loan-accept", "p", "company-loan");
        fixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Company:co").Balance = 30;
        var writtenOff = engine.SettleCompanyForLiquidation("company-finance-liquidation", "co"); var retry = engine.SettleCompanyForLiquidation("company-finance-liquidation", "co");
        Check(writtenOff == 70 && retry == 70 && fixture.Snapshot.Financing.Contracts.Single().State == FinancingState.WrittenOff, "liquidation pays available company cash then writes off residual debt exactly once without deadlock");
        Check(fixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Company:co").Balance == 0 && fixture.Snapshot.Financing.Pools.Single().WrittenOff == 70, "residual financing debt is not transferred to members");
    }

    private static void TestTriagePlanningNeverCreatesDriverOrRevenue()
    {
        var fixture = OwnedFleet("triage-plan", 100, "Locomotive", "Yard Loco"); var assignment = new MissionAssignmentEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeMissionCompletion(WorldOwnershipOutcome.Applied)).Reserve("triage-assignment", "p", "assignment-triage", "job-triage", MissionAssignmentKind.Freight, new[] { fixture.Asset.AssetId }, AssetOwnerRef.Player("p"), 50); var ledgerBefore = fixture.Snapshot.Economy.Ledger.Count;
        var engine = new TriageAssistanceEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new DisabledSelfShuntTriagePort()); var plan = engine.CreatePlan("triage-plan-create", "p", "plan-1", assignment.AssignmentId, TriageAssistanceLevel.PlanningOnly, new[] { "A1", "A2" }); var autonomous = engine.CreatePlan("triage-ai-create", "p", "plan-ai", assignment.AssignmentId, TriageAssistanceLevel.AutonomousDriving, new[] { "A1" }); var cancelled = engine.Cancel("triage-cancel", "p", plan.PlanId);
        Check(plan.Level == TriageAssistanceLevel.PlanningOnly && cancelled.State == TriagePlanState.Cancelled && autonomous.ResultCode == "autonomous-driving-forbidden", "triage assistance distinguishes planning and permanently refuses autonomous driving");
        Check(fixture.Snapshot.Economy.Ledger.Count == ledgerBefore && fixture.Snapshot.Assignments.Count == 1, "triage planning reuses the existing assignment and creates neither contract nor revenue");
    }

    private static void TestTriageBlockedIncompleteClientAndReloadRecovery()
    {
        var fixture = OwnedFleet("triage-guards", 100, "Locomotive", "Yard Loco"); var assignment = new MissionAssignmentEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeMissionCompletion(WorldOwnershipOutcome.Applied)).Reserve("triage-guard-assignment", "p", "assignment-guard", "job-guard", MissionAssignmentKind.Freight, new[] { fixture.Asset.AssetId }, AssetOwnerRef.Player("p"), 50);
        var blockedPort = new FakeTriageLogistics(new AssetReleaseInspection { Status = AssetReleaseStatus.Blocked, Detail = "occupied" }, WorldOwnershipOutcome.Applied, WorldOwnershipOutcome.Applied); var blockedEngine = new TriageAssistanceEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), blockedPort); var blockedPlan = blockedEngine.CreatePlan("blocked-plan", "p", "plan-blocked", assignment.AssignmentId, TriageAssistanceLevel.LogisticsCommand, new[] { "B1" }); var blocked = blockedEngine.Execute("blocked-execute", "p", blockedPlan.PlanId);
        var pendingPort = new FakeTriageLogistics(new AssetReleaseInspection { Status = AssetReleaseStatus.Releasable, Detail = "clear" }, WorldOwnershipOutcome.Unknown, WorldOwnershipOutcome.Applied); var pendingEngine = new TriageAssistanceEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), pendingPort); var pendingPlan = pendingEngine.CreatePlan("pending-plan", "p", "plan-pending", assignment.AssignmentId, TriageAssistanceLevel.LogisticsCommand, new[] { "C1" }); pendingEngine.Execute("pending-execute", "p", pendingPlan.PlanId); var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), "triage-guards"); var recovered = new TriageAssistanceEngine(restored, RoleDetector(NetworkRole.MultiplayerHost, true), pendingPort).Reconcile("pending-reconcile", pendingPlan.PlanId);
        var clientRefused = false; try { new TriageAssistanceEngine(restored, RoleDetector(NetworkRole.MultiplayerClient, false), pendingPort).CreatePlan("client-plan", "p", "plan-client", assignment.AssignmentId, TriageAssistanceLevel.PlanningOnly, new[] { "D1" }); } catch (InvalidOperationException) { clientRefused = true; }
        var incomplete = OwnedFleet("triage-incomplete", 100, "Locomotive", "Incomplete"); var incompleteAssignment = new MissionAssignmentEngine(incomplete.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeMissionCompletion(WorldOwnershipOutcome.Applied)).Reserve("incomplete-assignment", "p", "assignment-incomplete", "job-incomplete", MissionAssignmentKind.Freight, new[] { incomplete.Asset.AssetId }, AssetOwnerRef.Player("p"), 10); incomplete.Snapshot.Fleet.Clear(); var incompleteRefused = false; try { new TriageAssistanceEngine(incomplete.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), pendingPort).CreatePlan("incomplete-plan", "p", "plan-incomplete", incompleteAssignment.AssignmentId, TriageAssistanceLevel.PlanningOnly, new[] { "E1" }); } catch (InvalidOperationException) { incompleteRefused = true; }
        Check(blocked.ResultCode == "triage-track-blocked" && pendingPlan.State == TriagePlanState.ExecutionPending && recovered.State == TriagePlanState.Completed && clientRefused && incompleteRefused, "blocked track, incomplete consist, pending result, reload recovery and client authority are explicit");
    }

    private static void TestCompanyWorkflowCancellationCoversEveryContractBeforeDistribution()
    {
        var fixture = CompanyOwnedFleet("workflow-liquidation", true); var host = RoleDetector(NetworkRole.MultiplayerHost, true); var company = fixture.Snapshot.Economy.Companies.Single(); var companyWallet = fixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Company:" + company.CompanyId); companyWallet.Balance = 500; var companyAssets = fixture.Snapshot.Ownership.Where(x => x.Owner.Key == "Company:" + company.CompanyId).Select(x => x.AssetId).ToArray(); fixture.Snapshot.Fleet.Single(x => x.AssetId == companyAssets[1]).Kind = FleetVehicleKind.PassengerCar;
        var assignment = new MissionAssignmentEngine(fixture.Snapshot, host, new FakeMissionCompletion(WorldOwnershipOutcome.Applied)).Reserve("workflow-assignment", "p", "workflow-assignment", "freight-job", MissionAssignmentKind.Freight, new[] { companyAssets[0] }, AssetOwnerRef.Company(company.CompanyId), 100);
        var passengerEngine = new PassengerEconomyEngine(fixture.Snapshot, host, new FakeMissionCompletion(WorldOwnershipOutcome.Applied)); passengerEngine.ConfigureRoute("workflow-route", "route", "A", "B", 10, 100, 1, 10, 5, 1); var passenger = passengerEngine.OfferAndReserve("workflow-passenger", "p", "passenger", "route", "passenger-job", new[] { companyAssets[1] }, AssetOwnerRef.Company(company.CompanyId), 10, 0, 10);
        var leasedAsset = AddOwnedFleetAsset(fixture, "Freight", "Leased Wagon"); fixture.Snapshot.Ownership.Single(x => x.AssetId == leasedAsset.AssetId).Owner = AssetOwnerRef.Merchant("lessor"); var leaseEngine = new LeaseEngine(fixture.Snapshot, host, new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new FakeWorld(WorldOwnershipOutcome.Applied)); var lease = leaseEngine.CreateOffer("workflow-lease", new[] { leasedAsset.AssetId }, 20, 0, 5, 10, 100, null, 1m, 100); leaseEngine.Accept("workflow-lease-accept", "p", lease.LeaseId, AssetOwnerRef.Company(company.CompanyId), AccountRef.Company(company.CompanyId), lease.Version, companyWallet.Version);
        ConfigureIndustrial(fixture.Snapshot, 20m, 0m, 10m); var industrial = new IndustrialEconomyEngine(fixture.Snapshot, host, new FakeIndustrialExecution(WorldOwnershipOutcome.Applied)); var industrialContract = industrial.CreateOffer("workflow-industrial", "ORIGIN", "DEST", "Logs", 5m, AccountRef.Company(company.CompanyId), 20, 0); industrial.Accept("workflow-industrial-accept", industrialContract.ContractId, industrialContract.Version);
        var triage = new TriageAssistanceEngine(fixture.Snapshot, host, new DisabledSelfShuntTriagePort()).CreatePlan("workflow-triage", "p", "workflow-plan", assignment.AssignmentId, TriageAssistanceLevel.PlanningOnly, new[] { "Y1" });
        var port = new CompanyWorkflowCancellationPort(fixture.Snapshot, host); var first = port.Cancel("workflow-cancel", company.CompanyId); var balanceAfter = companyWallet.Balance; var retry = port.Cancel("workflow-cancel", company.CompanyId);
        Check(first == WorldOwnershipOutcome.Applied && retry == WorldOwnershipOutcome.Applied && passenger.State == PassengerContractState.Cancelled && assignment.State == MissionAssignmentState.Cancelled && industrialContract.State == IndustrialContractState.Cancelled && lease.State == LeaseState.Cancelled && triage.State == TriagePlanState.Cancelled, "company liquidation cancellation covers passenger, mission, industry, inbound lease and triage workflows");
        Check(companyWallet.Balance == balanceAfter && fixture.Snapshot.Economy.History.Count(x => x.EventId == "workflow-cancel") == 1 && port.Inspect(company.CompanyId) == WorldOwnershipOutcome.Applied, "company workflow cancellation is idempotent before final distribution");
    }

    private static void TestCompanyWorkflowCancellationWaitsForUnknownPhysicalTransitions()
    {
        var fixture = CompanyOwnedFleet("workflow-pending", true); var company = fixture.Snapshot.Economy.Companies.Single(); var assignment = new MissionAssignmentEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeMissionCompletion(WorldOwnershipOutcome.Applied)).Reserve("workflow-pending-assignment", "p", "workflow-pending-assignment", "job", MissionAssignmentKind.Freight, new[] { fixture.Snapshot.Ownership.First(x => x.Owner.Key == "Company:" + company.CompanyId).AssetId }, AssetOwnerRef.Company(company.CompanyId), 10);
        var plan = new TriageAssistanceEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new DisabledSelfShuntTriagePort()).CreatePlan("workflow-pending-plan", "p", "workflow-pending-plan", assignment.AssignmentId, TriageAssistanceLevel.PlanningOnly, new[] { "Y1" }); plan.State = TriagePlanState.ExecutionPending; plan.OperationId = "unknown-world-operation";
        var port = new CompanyWorkflowCancellationPort(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true)); var outcome = port.Cancel("workflow-pending-cancel", company.CompanyId);
        Check(outcome == WorldOwnershipOutcome.Unknown && plan.State == TriagePlanState.ExecutionPending && !fixture.Snapshot.Economy.History.Any(x => x.EventId == "workflow-pending-cancel"), "company liquidation waits without mutation while a physical workflow outcome is unknown");
    }

    private static void TestMaintenanceRequiresManualConfirmationAndExplicitPayer()
    {
        var request = new ManualMaintenanceRequest { CommandId = "maint-1", RequesterId = "buyer", AssetId = "asset", Action = MaintenanceAction.Repair };
        Check(!ManualMaintenancePolicy.Validate(request, out var automatic) && automatic == "automatic-maintenance-forbidden", "maintenance cannot start automatically");
        request.ExplicitUserConfirmation = true;
        Check(!ManualMaintenancePolicy.Validate(request, out var payer) && payer == "explicit-payer-required", "maintenance requires an explicitly selected account");
        request.Payer = AccountRef.Company("rail-co"); request.MaximumAuthorizedCost = 500;
        Check(ManualMaintenancePolicy.Validate(request, out _), "manual maintenance accepts only the chosen account and explicit confirmation");
    }

    private static void TestNetworkRoleMatrix()
    {
        Role(new NetworkApiState(), NetworkRole.Local, true);
        Role(new NetworkApiState { ApiAvailable = true }, NetworkRole.Local, true);
        Role(new NetworkApiState { ApiAvailable = true, IsConnected = true, IsHost = true, IsSinglePlayer = true }, NetworkRole.SoloHost, true);
        Role(new NetworkApiState { ApiAvailable = true, IsConnected = true, IsHost = true }, NetworkRole.MultiplayerHost, true);
        Role(new NetworkApiState { ApiAvailable = true, IsConnected = true }, NetworkRole.MultiplayerClient, false);
        Role(new NetworkApiState { ApiAvailable = true, IsConnected = true, IsSinglePlayer = true }, NetworkRole.Indeterminate, false);
        Role(new NetworkApiState { ApiAvailable = true, IsHost = true }, NetworkRole.Indeterminate, false);
    }

    private static void TestPersistentMultiplayerPlayerIdentity()
    {
        var guid = Guid.Parse("12345678-1234-5678-9abc-def012345678");
        var first = PlayerIdentity.FromMultiplayerGuid(guid);
        var second = PlayerIdentity.FromMultiplayerGuid(Guid.Parse("12345678123456789abcdef012345678"));
        var emptyRefused = false;
        try { PlayerIdentity.FromMultiplayerGuid(Guid.Empty); } catch (System.IO.InvalidDataException) { emptyRefused = true; }
        Check(first == "mp-12345678123456789abcdef012345678" && second == first && first.Length == 35 && emptyRefused,
            "authenticated Multiplayer GUIDs map to one stable bounded economic player identity and empty GUIDs fail closed");
    }

    private static void TestSaveGameFeatureFlagDefaultsOff() => Check(!SaveGameFeatureFlags.SafeDefaults().EnableSaveGameDataHook, "SaveGameData runtime hook is explicitly disabled by default");

    private static void TestSaveGameAutomaticUpdateGate()
    {
        var gate = new SaveGameAutomaticUpdateGate();
        var manager = new object();
        var data = new object();
        var otherManager = new object();
        var otherData = new object();
        Check(!gate.ShouldSkip(manager, data), "automatic save gate writes the first update");
        gate.MarkStaged(manager, data);
        Check(gate.ShouldSkip(manager, data), "automatic save gate skips an unchanged manager and data pair");
        Check(!gate.ShouldSkip(otherManager, data) && !gate.ShouldSkip(manager, otherData), "automatic save gate writes when the save manager or data changes");
        gate.Reset();
        Check(!gate.ShouldSkip(manager, data), "automatic save gate resets for a new career configuration");
    }

    private static void TestSaveGameCreateLoadMigrationAndRecovery()
    {
        var node = new FakeAtomicSaveNode(); var identity = SyntheticCareer("Career", "Standard", "2080-01-01T08:00:00Z");
        var service = new SaveGameDataPersistenceService(node, () => "11111111-1111-1111-1111-111111111111");
        var created = service.Write(identity, "payload-v1");
        Check(service.Load(identity).Payload == "payload-v1" && created.SchemaVersion == 2, "synthetic save creates and loads with real identity material");
        var legacy = SaveGameIntegrationCodec.Deserialize(node.Value!); legacy.SchemaVersion = 1; node.Value = SaveGameIntegrationCodec.Serialize(legacy);
        var migrated = service.Write(identity, "payload-v2");
        Check(migrated.SchemaVersion == 2 && migrated.RecoveryCopies.Count == 1 && migrated.RecoveryCopies[0].SourceVersion == 1, "old schema migrates with a checksummed recoverable copy");
        service.RestoreRecoveryCopy(migrated, 0);
        Check(SaveGameIntegrationCodec.Deserialize(node.Value!).SchemaVersion == 1 && service.Load(identity).Payload == "payload-v1", "recoverable copy restores the complete pre-migration synthetic envelope");
    }

    private static void TestSaveGameCorruptionInterruptionRollbackAndIsolation()
    {
        var node = new FakeAtomicSaveNode(); var identity = SyntheticCareer("Career", "Standard", "2080-01-01T08:00:00Z");
        var service = new SaveGameDataPersistenceService(node, () => "22222222-2222-2222-2222-222222222222");
        service.Write(identity, "good"); var original = node.Value;
        service.FailureInjection = _ => throw new System.IO.IOException("synthetic interruption");
        try { service.Write(identity, "never-committed"); } catch (System.IO.IOException) { }
        Check(node.Value == original && service.Load(identity).Payload == "good", "interruption before atomic replace preserves the recoverable state");
        node.FailNextReplaceAfterMutation = true; service.FailureInjection = null;
        try { service.Write(identity, "rolled-back"); } catch (System.IO.IOException) { }
        Check(node.Value == original, "failed atomic replace rolls back to the previous synthetic save node");
        var incompatible = false; try { service.Load(SyntheticCareer("FreeRoam", "Standard", "2080-01-01T08:00:00Z")); } catch (System.IO.InvalidDataException) { incompatible = true; }
        var envelope = SaveGameIntegrationCodec.Deserialize(node.Value!); envelope.PayloadSha256 = "corrupt"; node.Value = SaveGameIntegrationCodec.Serialize(envelope);
        var corrupt = false; try { service.Load(identity); } catch (System.IO.InvalidDataException) { corrupt = true; }
        Check(incompatible && corrupt, "incompatible saves and corrupted payloads fail closed");
    }

    private static void TestRuntimeStateProviderBootstrapsRestoresAndRejectsOtherBranch()
    {
        var provider = new AcquisitionRuntimeStateProvider();
        var payload = provider.Provide("runtime-checkpoint", null);
        var initial = VehicleAcquisitionPersistence.Deserialize(payload, "runtime-checkpoint");
        initial.Economy.Players.Add(new PlayerEconomicState { PlayerId = "runtime-player", Version = 1 });
        var persisted = VehicleAcquisitionPersistence.Serialize(initial);
        var restoredProvider = new AcquisitionRuntimeStateProvider();
        var restored = restoredProvider.Provide("runtime-checkpoint", persisted);
        var repeated = restoredProvider.Provide("runtime-checkpoint", null);
        var wrongBranch = false;
        try { restoredProvider.Provide("other-checkpoint", null); } catch (System.IO.InvalidDataException) { wrongBranch = true; }
        var corrupt = false;
        try { new AcquisitionRuntimeStateProvider().Provide("runtime-checkpoint", "not-json"); } catch (Exception) { corrupt = true; }
        Check(initial.Assets.Assets.Count == 0 && VehicleAcquisitionPersistence.Deserialize(restored, "runtime-checkpoint").Economy.Players.Single().PlayerId == "runtime-player" && restored == repeated && wrongBranch && corrupt,
            "runtime state bootstraps empty, restores existing data, repeats deterministically and rejects branch drift or corruption");
    }

    private static void TestDetachedRuntimeState()
    {
        var provider = new AcquisitionRuntimeStateProvider();
        provider.Provide("detached-runtime", null);
        provider.EnsureLocalPlayer(200);
        var captured = provider.CaptureDetachedState();
        var before = VehicleAcquisitionPersistence.SerializePrepared(provider.Current!);
        Parallel.For(0, 32, _ => {
            var isolated = VehicleAcquisitionPersistence.Deserialize(before, "detached-runtime");
            if (VehicleAcquisitionPersistence.Serialize(isolated) != before) throw new Exception("Concurrent persistence changed the payload.");
        });
        Check(before == VehicleAcquisitionPersistence.SerializePrepared(captured.Snapshot), "detached capture preserves the persisted schema and values");
        provider.Current!.Economy.Wallets[0].Balance = 900;
        provider.Current.Economy.Players.Clear();
        provider.MarkStateChanged();
        var worker = new AcquisitionRuntimeStateProvider();
        Task.Run(() => worker.InitializeFromDetachedState(captured)).GetAwaiter().GetResult();
        Check(VehicleAcquisitionPersistence.SerializePrepared(worker.Current!) == before, "worker owns nested records and lists after live state changes");
        Check(!provider.TryCommitPreparedSnapshot(captured.Revision, captured.Snapshot), "a detached worker cannot overwrite a newer live revision");
        var schema = new VehicleAcquisitionSnapshot();
        SeedSnapshotRecords(schema, 0);
        schema.Economy.Companies[0].DelegatedPermissions = new Dictionary<string, List<CompanyPermission>>(StringComparer.OrdinalIgnoreCase)
            { ["Member"] = new List<CompanyPermission> { CompanyPermission.ManageFleet } };
        var copy = RuntimeStateCopy.Capture(schema);
        Check(VehicleAcquisitionPersistence.SerializePrepared(schema) == VehicleAcquisitionPersistence.SerializePrepared(copy),
            "populated persisted collections and record types survive detached capture");
        schema.Economy.Companies[0].DelegatedPermissions["member"].Clear();
        Check(copy.Economy.Companies[0].DelegatedPermissions["MEMBER"].Count == 1,
            "nested permission lists detach and dictionary key comparison is preserved");
    }

    private static void TestIncrementalRuntimeState()
    {
        var provider = new AcquisitionRuntimeStateProvider();
        provider.Provide("sliced-runtime", null);
        provider.EnsureLocalPlayer(200);
        for (var i = 0; i < 20000; i++) provider.Current!.IndustrialCommands.Add(new MissionAssignmentCommand
            { CommandId = "history-" + i, Fingerprint = "advance|recipe", AssignmentId = "recipe", ResultCode = "0" });
        var expected = VehicleAcquisitionPersistence.SerializePrepared(provider.Current!);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var synchronous = provider.CaptureDetachedState();
        var synchronousMs = timer.Elapsed.TotalMilliseconds;
        using (var capture = provider.BeginDetachedStateCapture())
        {
            Check(!capture.Advance(1000, 1), "a large capture does not complete in one bounded step");
            var workerRefused = Task.Run(() => { try { capture.Advance(); return false; } catch (InvalidOperationException) { return true; } }).Result;
            Check(workerRefused, "partial capture refuses reads from a worker thread");
            var slices = 0; double maximumSliceMs = 0;
            bool done;
            do { timer.Restart(); done = capture.Advance(); maximumSliceMs = Math.Max(maximumSliceMs, timer.Elapsed.TotalMilliseconds); slices++; } while (!done);
            var detached = capture.Result;
            Check(slices > 1 && VehicleAcquisitionPersistence.SerializePrepared(detached.Snapshot) == expected,
                "large incremental capture spans slices without changing persisted data");
            provider.Current!.IndustrialCommands[0].ResultCode = "changed";
            Check(detached.Snapshot.IndustrialCommands[0].ResultCode == "0", "incremental capture owns its history records");
            Console.WriteLine("CAPTURE BENCHMARK net48: records=20000; synchronousMs=" + synchronousMs.ToString("F2") +
                "; slices=" + slices + "; maximumSliceMs=" + maximumSliceMs.ToString("F2") + "; payloadChars=" + expected.Length);
        }
        using (var capture = provider.BeginDetachedStateCapture())
        {
            capture.Advance(1000, 1); provider.MarkStateChanged();
            var cancelled = false; try { capture.Advance(); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "revision changes cancel partial capture before any more live reads");
        }
        var current = true;
        using (var capture = provider.BeginDetachedStateCapture(() => current))
        {
            capture.Advance(1000, 1); current = false;
            var cancelled = false; try { capture.Advance(); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "world or direct-mutation invalidation cancels partial capture");
        }
        var schema = new VehicleAcquisitionSnapshot(); SeedSnapshotRecords(schema, 0);
        schema.Economy.Companies[0].DelegatedPermissions = new Dictionary<string, List<CompanyPermission>>(StringComparer.OrdinalIgnoreCase)
            { ["Member"] = new List<CompanyPermission> { CompanyPermission.ManageFleet } };
        using (var capture = new RuntimeStateCapture(schema, 1, () => true))
        {
            while (!capture.Advance(1000, 1)) { }
            var copy = capture.Result.Snapshot;
            Check(VehicleAcquisitionPersistence.SerializePrepared(copy) == VehicleAcquisitionPersistence.SerializePrepared(schema), "all persisted record shapes survive incremental capture");
            schema.Economy.Companies[0].DelegatedPermissions["member"].Clear();
            Check(copy.Economy.Companies[0].DelegatedPermissions["MEMBER"].Count == 1, "incremental dictionary copies preserve comparer and detach nested lists");
        }
        var stopped = provider.BeginDetachedStateCapture(); stopped.Dispose();
        var disposed = false; try { stopped.Advance(); } catch (ObjectDisposedException) { disposed = true; }
        Check(disposed, "disposed captures cannot resume on the next frame");
    }

    private static void TestAutomaticProductionSkipsIdleCommands()
    {
        var fixture = Acquisition("scheduled-production", 0, WorldOwnershipOutcome.Applied);
        ConfigureIndustrial(fixture.Snapshot, 0m, 10m, 20m);
        fixture.Snapshot.IndustrialStocks.Add(new IndustrialStock { FacilityId = "DEST", CargoId = "Lumber", Capacity = 20m });
        fixture.Snapshot.IndustrialRecipes.Add(new IndustrialRecipe { RecipeId = "sawmill", FacilityId = "DEST", InputCargoId = "Logs", InputQuantity = 2m,
            OutputCargoId = "Lumber", OutputQuantity = 1m, CadenceTicks = 60, MaximumBacklogCycles = 10 });
        var engine = new IndustrialEconomyEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeIndustrialExecution(WorldOwnershipOutcome.Applied));
        for (var tick = 1; tick < 60; tick++) engine.AdvanceDueProduction("idle-" + tick, tick);
        Check(fixture.Snapshot.IndustrialCommands.Count == 0, "idle automatic production creates no per-second history");
        engine.AdvanceDueProduction("due", 60); engine.AdvanceDueProduction("due", 60);
        Check(engine.AdvanceProduction("due:production:sawmill", "sawmill", 60) == 1 && fixture.Snapshot.IndustrialCommands.Count == 1,
            "due production runs once and keeps explicit command replay");
        engine.AdvanceDueProduction("catch-up", 180);
        Check(fixture.Snapshot.IndustrialStocks.Single(value => value.CargoId == "Lumber").OnHand == 3m,
            "batched clock advances preserve due production cycles");
    }

    private static void SeedSnapshotRecords(object value, int depth)
    {
        if (depth > 8) return;
        foreach (var property in value.GetType().GetProperties().Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0))
        {
            var child = property.GetValue(value);
            if (child is System.Collections.IList list && property.PropertyType.IsGenericType)
            {
                var element = property.PropertyType.GetGenericArguments()[0];
                var item = element == typeof(string) ? "probe" : Activator.CreateInstance(element);
                list.Add(item);
                if (item != null && element.Namespace == "BDVM.Domain") SeedSnapshotRecords(item, depth + 1);
            }
            else if (child != null && child.GetType().Namespace == "BDVM.Domain" && !child.GetType().IsValueType)
                SeedSnapshotRecords(child, depth + 1);
        }
    }

    private static void TestPersistentSystemDocuments()
    {
        var schema = new VehicleAcquisitionSnapshot { CheckpointId = "all-system-migrations" };
        SeedSnapshotRecords(schema, 0);
        schema.Economy.Wallets[0].Balance = 987654321;
        schema.Economy.Companies[0].DelegatedPermissions["member"] = new List<CompanyPermission> { CompanyPermission.ManageFleet, CompanyPermission.ManageFunds };
        var before = PersistentSnapshotDocuments.Export(schema);
        var restored = PersistentSnapshotDocuments.Restore(before, schema.CheckpointId, false);
        Check(VehicleAcquisitionPersistence.SerializePrepared(schema) == VehicleAcquisitionPersistence.SerializePrepared(restored),
            "every persisted system, nested list, phase and permission survives journal document migration");
        restored.Economy.Wallets[0].Balance++;
        var delta = PersistentSnapshotDocuments.Difference(before, PersistentSnapshotDocuments.Export(restored));
        Check(delta.Count == 1 && delta[0].System == "economy" && delta[0].Key.StartsWith("$/wallets/"),
            "one wallet change does not journal the full economy, its history or other systems");
        restored.IndustrialCommands.Add(new MissionAssignmentCommand { CommandId = "new-history", AssignmentId = "contract" });
        delta = PersistentSnapshotDocuments.Difference(before, PersistentSnapshotDocuments.Export(restored));
        Check(delta.Count == 3, "appending a command emits its new record and count without rewriting older records");
        foreach (var migration in PersistentSystemMigrations.Systems)
        {
            var selected = PersistentSnapshotDocuments.Export(schema, migration.Value);
            Check(selected.All(document => migration.Value.Contains(document.System)) && migration.Value.All(root => selected.Any(document => document.System == root)),
                "migration covers all of its systems: " + migration.Key);
        }
        var missing = before.Skip(1).ToArray();
        var refused = false; try { PersistentSnapshotDocuments.Restore(missing, schema.CheckpointId, false); } catch (System.IO.InvalidDataException) { refused = true; }
        Check(refused, "incomplete system migration is refused instead of restoring defaults");
        var duplicate = before.Concat(before.Take(1));
        refused = false; try { PersistentSnapshotDocuments.Restore(duplicate, schema.CheckpointId, false); } catch (System.IO.InvalidDataException) { refused = true; }
        Check(refused, "duplicate checkpoint documents are refused");
        restored.IndustrialCommands.RemoveAt(0);
        restored.Economy.Companies.Clear();
        Check(VehicleAcquisitionPersistence.SerializePrepared(restored) == VehicleAcquisitionPersistence.SerializePrepared(
            PersistentSnapshotDocuments.Restore(PersistentSnapshotDocuments.Export(restored), schema.CheckpointId, false)), "deletions and collection ordering round-trip exactly");
        Console.WriteLine("PASS M03-M07 document coverage: all 35 business roots, populated nested records, initial balances, phases, permissions, targeted wallet/history deltas and deleted records.");
    }

    private static void TestPeriodicEconomicProjection()
    {
        var fixture = Acquisition("periodic-projection", 500, WorldOwnershipOutcome.Applied);
        ConfigureIndustrial(fixture.Snapshot, 0m, 10m, 20m);
        fixture.Snapshot.IndustrialStocks.Add(new IndustrialStock { FacilityId = "DEST", CargoId = "Lumber", Capacity = 20m });
        fixture.Snapshot.IndustrialRecipes.Add(new IndustrialRecipe { RecipeId = "sawmill", FacilityId = "DEST", InputCargoId = "Logs", InputQuantity = 2m,
            OutputCargoId = "Lumber", OutputQuantity = 1m, CadenceTicks = 60, MaximumBacklogCycles = 10 });
        var original = VehicleAcquisitionPersistence.Serialize(fixture.Snapshot);
        var provider = new AcquisitionRuntimeStateProvider(); provider.InitializeFromCapturedPayload(fixture.Snapshot.CheckpointId, original);
        var baseline = new AcquisitionRuntimeStateProvider(); baseline.InitializeFromCapturedPayload(fixture.Snapshot.CheckpointId, original);
        var roles = RoleDetector(NetworkRole.MultiplayerHost, true);
        PeriodicEconomicProjection projection;
        using (var capture = provider.BeginPeriodicEconomicCapture())
        {
            while (!capture.Advance(1000, 1)) { }
            Check(capture.Result.Snapshot.Assets.Assets.Count == 0 && capture.Result.Snapshot.Economy.Players.Count == 0,
                "periodic capture excludes assets, governance and unrelated systems");
            projection = new PeriodicEconomicProjection(capture.Result);
        }
        PeriodicEconomicDelta last = null!;
        for (var tick = 1; tick <= 180; tick++)
        {
            var advance = new LeaseClockAdvance { CommandId = "tick-" + tick, ActiveGameplayTicks = 1, SessionOpen = true };
            baseline.AdvanceEconomicClockWithoutLeasing(advance, roles);
            new IndustrialEconomyEngine(baseline.Current!, roles, new DisabledIndustrialExecutionPort()).AdvanceDueProduction(advance.CommandId, tick);
            last = projection.Advance(advance, roles, true, tick - 1);
            Check(provider.TryApplyPeriodicEconomicDelta(provider.Revision, last), "periodic row delta applies at the expected revision");
        }
        Check(VehicleAcquisitionPersistence.Serialize(provider.Current!) == VehicleAcquisitionPersistence.Serialize(baseline.Current!),
            "180 retained-worker ticks preserve exact legacy clock, production, stocks and command results without global copies");
        var beforeReplay = VehicleAcquisitionPersistence.Serialize(provider.Current!);
        Check(!provider.TryApplyPeriodicEconomicDelta(provider.Revision, last) && beforeReplay == VehicleAcquisitionPersistence.Serialize(provider.Current!),
            "a repeated economic delta is rejected atomically by its clock fence");
        var revision = provider.Revision;
        var next = projection.Advance(new LeaseClockAdvance { CommandId = "next", ActiveGameplayTicks = 60, SessionOpen = true }, roles, true, 180);
        provider.MarkStateChanged();
        Check(!provider.TryApplyPeriodicEconomicDelta(revision, next), "a competing command invalidates the entire speculative economic delta");
        // A row conflict must leave the clock and every other stock unchanged.
        provider.Current!.IndustrialStocks.Single(value => value.CargoId == "Lumber").Version++;
        beforeReplay = VehicleAcquisitionPersistence.Serialize(provider.Current!);
        Check(!provider.TryApplyPeriodicEconomicDelta(provider.Revision, next) && beforeReplay == VehicleAcquisitionPersistence.Serialize(provider.Current!),
            "a late row conflict cannot partially apply clock or earlier rows");
        for (var index = 0; index < 20000; index++) provider.Current!.LeaseActions.Add(new LeaseActionRecord { CommandId = "historical-" + index });
        using (var capture = provider.BeginPeriodicEconomicCapture())
        {
            var steps = 0; while (!capture.Advance(1000, 1)) steps++;
            Check(capture.Result.Snapshot.LeaseActions.Count == 0 && capture.Result.Snapshot.IndustrialCommands.Count == 0 && steps < 500,
                "historical command growth does not increase the periodic capture");
            Console.WriteLine("PASS M03/M04 periodic projection: 180 equivalent ticks, atomic replay/conflict fences; captureSteps=" + steps + " with 20000 historical records excluded.");
        }
    }

    private static void TestPersistentRuntimeJournal()
    {
        var provider = new AcquisitionRuntimeStateProvider();
        var payload = provider.Provide("persistent-runtime", null);
        provider.EnsureLocalPlayer(100);
        payload = provider.Provide("persistent-runtime", null);
        var temporaryRoot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(temporaryRoot, "bdvm-runtime-journal-" + Guid.NewGuid().ToString("N")));
        Check(directory.StartsWith(temporaryRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "journal test cleanup stays in its temp directory");
        try
        {
            var runtime = new PersistentRuntimeJournal(directory, "persistent-runtime", payload);
            PeriodicEconomicDelta delta;
            using (var capture = provider.BeginPeriodicEconomicCapture())
            {
                while (!capture.Advance(1000)) { }
                delta = new PeriodicEconomicProjection(capture.Result).Advance(new LeaseClockAdvance { CommandId = "saved-clock", ActiveGameplayTicks = 15, SessionOpen = true },
                    RoleDetector(NetworkRole.MultiplayerHost, true), false, 0);
            }
            var serializedEvent = PersistentJournalCodec.Serialize(delta);
            Check(provider.TryApplyPeriodicEconomicDelta(provider.Revision, delta), "confirmed tick applies before journal observation");
            runtime.ObservePeriodicAsync(serializedEvent).GetAwaiter().GetResult();
            var checkpoint = runtime.ReadCheckpointAsync().GetAwaiter().GetResult();
            Check(runtime.ObservePeriodicAsync(serializedEvent).GetAwaiter().GetResult() == checkpoint.Sequence,
                "repeated confirmed event returns its original receipt without applying its clock or ledger twice");
            var savedPayload = VehicleAcquisitionPersistence.Serialize(provider.Current!);
            Check(savedPayload == VehicleAcquisitionPersistence.Serialize(PersistentSnapshotDocuments.Restore(checkpoint.Documents, "persistent-runtime")),
                "runtime journal replays confirmed periodic events into the same materialized state");
            var ticket = Guid.NewGuid().ToString("N");
            runtime.CheckpointSaveAsync(ticket, "test/manual-save", savedPayload).GetAwaiter().GetResult();
            provider.Current!.Economy.Wallets[0].Balance = 999;
            runtime.ObserveSnapshotAsync("future-command", VehicleAcquisitionPersistence.Serialize(provider.Current!)).GetAwaiter().GetResult();
            runtime.CheckpointSaveAsync(Guid.NewGuid().ToString("N"), "test/stashed-save", savedPayload).GetAwaiter().GetResult();
            Check(PersistentSnapshotDocuments.Restore(runtime.ReadCheckpointAsync().Result.Documents, "persistent-runtime").Economy.Wallets[0].Balance == 999,
                "saving an older staged image must not rewind the live journal projection");
            runtime.StopAsync().GetAwaiter().GetResult();
            var loaded = new PersistentRuntimeJournal(directory, "persistent-runtime", savedPayload, ticket);
            var loadedCheckpoint = loaded.ReadCheckpointAsync().GetAwaiter().GetResult();
            var restored = PersistentSnapshotDocuments.Restore(loadedCheckpoint.Documents, "persistent-runtime");
            Check(restored.Economy.Wallets[0].Balance == 100 && restored.LeaseClock.ActiveTick == 15 && loadedCheckpoint.BranchId != checkpoint.BranchId,
                "loading an older game save forks only its exact ticket checkpoint, never later transactions");
            loaded.StopAsync().GetAwaiter().GetResult();
            var portable = new PersistentRuntimeJournal(directory, "persistent-runtime", savedPayload, Guid.NewGuid().ToString("N"));
            Check(VehicleAcquisitionPersistence.Serialize(PersistentSnapshotDocuments.Restore(portable.ReadCheckpointAsync().Result.Documents, "persistent-runtime")) == savedPayload,
                "portable saves and shutdown before async checkpoint completion recover from their complete embedded payload");
            portable.StopAsync().GetAwaiter().GetResult();
            provider.ResetForLoad(); provider.Provide("persistent-runtime", savedPayload);
            Check(provider.Current!.Economy.Wallets[0].Balance == 100 && provider.Current.LeaseClock.ActiveTick == 15,
                "same-career reload restores its saved state instead of retaining the current session's future balance");
            Console.WriteLine("PASS runtime journal: confirmed delta replay, durable save ticket, old-save isolation, fork and missing-sidecar recovery.");
        }
        finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
    }

    private static void TestRuntimeStatePayloadCommitRejectsStaleWorkers()
    {
        var provider = new AcquisitionRuntimeStateProvider();
        var initial = provider.Provide("async-runtime", null);
        var captured = provider.CaptureStatePayload();
        var worker = new AcquisitionRuntimeStateProvider();
        worker.InitializeFromCapturedPayload(captured.CheckpointId, captured.Payload, captured.Revision);
        var authority = new FakeRoleDetector(new NetworkRoleReport { Role = NetworkRole.Local, HasAuthority = true, Detail = "test" });
        worker.AdvanceEconomicClockWithoutLeasing(new LeaseClockAdvance
        {
            CommandId = "async-tick",
            ActiveGameplayTicks = 1,
            SessionOpen = true,
            Paused = false
        }, authority);
        var replacement = worker.Current!;
        var replacementPayload = VehicleAcquisitionPersistence.SerializePrepared(replacement);
        var committed = provider.TryCommitPreparedSnapshot(captured.Revision, replacement, replacementPayload);
        var staleRejected = !provider.TryCommitPreparedSnapshot(captured.Revision, replacement, replacementPayload);
        var tick = VehicleAcquisitionPersistence.Deserialize(provider.CapturePayload(), "async-runtime").LeaseClock.ActiveTick;
        Check(initial != replacementPayload && committed && staleRejected && tick == 1,
            "prepared runtime workers commit one matching revision and stale results cannot overwrite a newer state");
    }

    private static void TestSavePayloadFactoryReceivesResolvedCheckpointAndExistingPayload()
    {
        var node = new FakeAtomicSaveNode();
        var identity = SyntheticCareer("Career", "Standard", "2080-01-01T08:00:00Z");
        var service = new SaveGameDataPersistenceService(node, () => "33333333-3333-3333-3333-333333333333");
        string? firstCheckpoint = null; string? firstExisting = "unexpected";
        var first = service.Write(identity, (checkpoint, existing) => { firstCheckpoint = checkpoint; firstExisting = existing; return "runtime-one"; });
        string? secondCheckpoint = null; string? secondExisting = null;
        var second = service.Write(identity, (checkpoint, existing) => { secondCheckpoint = checkpoint; secondExisting = existing; return "runtime-two"; });
        Check(firstCheckpoint == first.CheckpointId && firstExisting == null && secondCheckpoint == first.CheckpointId && secondExisting == "runtime-one" && second.Payload == "runtime-two",
            "payload factory receives the resolved checkpoint and preserves explicit access to existing state before replacement");
    }

    private static void TestStartingCapital()
    {
        var settings = RuntimeSaveSettings.SafeDefaults();
        Check(settings.StartingPersonalBalance == 125000 && settings.StarterBundleDefinitionIds.Count == 0, "new defaults grant cash without rolling stock");
        var path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(path, "{\"startingPersonalBalance\":2000,\"starterBundleDefinitionIds\":[\"LocoDE2\",\"FlatbedEmpty\",\"FlatbedEmpty\",\"FlatbedEmpty\"]}");
            var legacy = RuntimeSaveSettings.Load(path);
            Check(legacy.StartingPersonalBalance == 125000 && legacy.StarterBundleDefinitionIds.Count == 0, "installed legacy starter settings upgrade to cash only");
            System.IO.File.WriteAllText(path, "{\"startingPersonalBalance\":90000}");
            Check(RuntimeSaveSettings.Load(path).StartingPersonalBalance == 90000, "custom starting balance remains configurable");
        }
        finally { System.IO.File.Delete(path); }
        var provider = new AcquisitionRuntimeStateProvider();
        provider.Provide("capital", null);
        var id = provider.LocalPlayerId!;
        provider.EnsureStartingPlayer(id, 125000, 2000);
        var plan = provider.PlanExternalWalletMirror(id, 2000, "initial");
        Check(plan.Action == ExternalWalletMirrorAction.CreditExternal && plan.Amount == 123000, "native wallet receives the difference to 125000, not an extra grant");
        Check(provider.PlanExternalWalletMirror(id, 125000, "retry").Action == ExternalWalletMirrorAction.None, "retry after native credit does not pay twice");
        provider.CompleteExternalWalletMirror(id, 125000, "paid");
        provider.SynchronizeWalletFor("spend", id, 100000, "test");
        provider.EnsureStartingPlayer(id, 125000, 100000);
        provider.EnsureStartingPlayer("remote", 125000);
        provider.EnsureStartingPlayer("remote", 125000);
        provider.CreateCompanyFor("company", id, "Capital Rail");
        var restored = new AcquisitionRuntimeStateProvider();
        restored.Provide("capital", provider.CapturePayload());
        restored.EnsureStartingPlayer(id, 125000, 100000);
        restored.EnsureStartingPlayer("remote", 125000);
        var state = restored.Current!;
        Check(state.Economy.Wallets.Single(w => w.Account.Key == AccountRef.Player(id).Key).Balance == 100000 &&
              state.Economy.Wallets.Single(w => w.Account.Key == AccountRef.Player("remote").Key).Balance == 125000 &&
              state.Economy.Wallets.Single(w => w.Account.Kind == AccountKind.Company).Balance == 0,
              "reload and repeated player initialization preserve spent capital, remote capital and zero company balance");
        Check(state.Assets.Assets.Count == 0 && state.InitialDeliveries.Count == 0 && state.Economy.History.Count(h => h.Kind == "starting-capital-granted") == 2,
              "one persistent receipt per player and no starter vehicles or deliveries");
        restored.EnsurePersistentPlayer("legacy", 2300);
        restored.EnsureStartingPlayer("legacy", 125000);
        Check(state.Economy.Wallets.Single(w => w.Account.Key == AccountRef.Player("legacy").Key).Balance == 2300, "existing players do not receive retroactive capital");
        var newCareer = new AcquisitionRuntimeStateProvider();
        newCareer.Provide("other-career", null);
        newCareer.EnsureStartingPlayer("remote", 125000);
        Check(newCareer.Current!.Economy.Wallets.Single().Balance == 125000, "another career grants its own initial capital");
    }
    private static void TestRuntimeSettingsFailClosed()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bdvm-runtime-settings-" + Guid.NewGuid().ToString("N") + ".json");
        var warnings = 0;
        Check(!RuntimeSaveSettings.Load(path).EnableSaveGameDataHook, "missing runtime settings keep the save hook disabled");
        System.IO.File.WriteAllText(path, "{");
        Check(!RuntimeSaveSettings.Load(path, _ => warnings++).EnableSaveGameDataHook && warnings == 1, "invalid runtime settings fail closed with one warning");
        System.IO.File.WriteAllText(path, "{\"enableSaveGameDataHook\":true}");
        Check(RuntimeSaveSettings.Load(path).EnableSaveGameDataHook && RuntimeSaveSettings.Load(path).StartingPersonalBalance == 125000,
            "older runtime settings without a balance use the new starting capital");
        System.IO.File.WriteAllText(path, "{\"enableSaveGameDataHook\":true,\"startingPersonalBalance\":-1}");
        Check(!RuntimeSaveSettings.Load(path, _ => warnings++).EnableSaveGameDataHook && warnings == 2,
            "an invalid configured starting balance fails closed");
        System.IO.File.Delete(path);
    }

    private static void TestVerticalSliceCreatesOnePersistentZeroBalanceCompany()
    {
        var provider = new AcquisitionRuntimeStateProvider();
        provider.Provide("vertical-checkpoint", null);
        var player = provider.EnsureLocalPlayer();
        var first = provider.CreateCompany("ui-create:stable", "Valley Rail");
        var retry = provider.CreateCompany("ui-create:stable", "Valley Rail");
        var payload = provider.Provide("vertical-checkpoint", null);
        var restored = new AcquisitionRuntimeStateProvider();
        restored.Provide("vertical-checkpoint", payload);
        var restoredPlayer = restored.EnsureLocalPlayer();
        var state = restored.Current!;
        Check(player.PlayerId == restoredPlayer.PlayerId && first.State == CommandState.Succeeded && retry.ResultCode == first.ResultCode && state.Economy.Companies.Count == 1 && state.Economy.Wallets.Single(x => x.Account.Kind == AccountKind.Company).Balance == 0,
            "0.1.0 creates one free zero-balance company and restores the same local identity without duplicate effects");
    }

    private static void TestRuntimeWalletMigrationSynchronizationAndCompanyTransfers()
    {
        var provider = new AcquisitionRuntimeStateProvider();
        provider.Provide("runtime-wallet", null);
        var player = provider.EnsureLocalPlayer(100);
        provider.CreateCompany("create-wallet-company", "Wallet Rail");
        var contribution = provider.TransferLocalCompany("contribute-40", 40, true);
        var retry = provider.TransferLocalCompany("contribute-40", 40, true);
        var withdrawal = provider.TransferLocalCompany("withdraw-10", 10, false);
        var sync = provider.SynchronizeLocalWallet("external-sync-75", 75, "synthetic-vanilla");
        var state = provider.Current!;
        var personal = state.Economy.Wallets.Single(x => x.Account.Key == AccountRef.Player(player.PlayerId).Key);
        var company = state.Economy.Wallets.Single(x => x.Account.Kind == AccountKind.Company);
        var persisted = provider.Provide("runtime-wallet", null);
        var restored = new AcquisitionRuntimeStateProvider(); restored.Provide("runtime-wallet", persisted);
        Check(contribution.State == CommandState.Succeeded && retry.CommandId == contribution.CommandId && withdrawal.State == CommandState.Succeeded && sync.State == CommandState.Succeeded && personal.Balance == 75 && company.Balance == 30,
            "host legacy balance migrates once, contribution and withdrawal succeed, retry is idempotent, and authoritative external balance synchronizes explicitly");
        Check(state.Economy.History.Count(x => x.Kind == "wallet-migration") == 1 && state.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.Contribution) == 1 && state.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.Withdrawal) == 1 && state.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.ExternalWalletSync) == 1 && restored.Current!.Economy.Wallets.Any(x => x.Balance == 30),
            "wallet migration, transfer and external sync remain auditable and survive persistence");
    }

    private static void TestCareerIdentityFallsBackToCurrentSessionMetadata()
    {
        var anchor = CareerCheckpointIdentity.SessionAnchor("Career", "World1", 0, "profile-signature");
        var identity = new CareerIdentityMaterial { GameMode = "Career", StartingDifficulty = "Standard", StartingTimeAndDate = anchor, Scenario = "World1", IdentitySource = "current-session-metadata-v1" };
        var fingerprint = CareerCheckpointIdentity.Fingerprint(identity);
        Check(anchor.StartsWith("session-v1:") && anchor == CareerCheckpointIdentity.SessionAnchor("Career", "World1", 0, "profile-signature") && anchor != CareerCheckpointIdentity.SessionAnchor("Career", "World1", 1, "profile-signature") && CareerCheckpointIdentity.IsHash(fingerprint),
            "career identity falls back to stable current-session metadata when Multiplayer SaveGameData omits startup keys");
    }

    private static void TestRuntimeVisibleOfferAndAcquisitionFlow()
    {
        var provider = new AcquisitionRuntimeStateProvider(); provider.Provide("runtime-offer", null);
        var player = provider.EnsureLocalPlayer(500);
        var carGuid = Guid.NewGuid().ToString("D");
        var vehicle = new VehicleInstanceRecord { ExistingPersistentId = carGuid, ExistingVisibleId = "L-001", DefinitionId = "loco.runtime", Resolution = ResolutionState.Resolved, Origin = new OriginRecord { Kind = "game" } };
        var firstOffer = provider.PrepareVisibleVehicleOffer(vehicle, 120);
        var sameOffer = provider.PrepareVisibleVehicleOffer(vehicle, 120);
        var sink = new CountingCheckpointSink();
        var acquired = provider.AcquireLocal("runtime-buy", firstOffer.OfferId, false, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeWorld(WorldOwnershipOutcome.Applied), sink);
        var retry = provider.AcquireLocal("runtime-buy", firstOffer.OfferId, false, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeWorld(WorldOwnershipOutcome.Applied), sink);
        Check(firstOffer.OfferId == sameOffer.OfferId && provider.Current!.Assets.Assets.Count == 1 && provider.Current.Ownership.Single().Owner.Key == AssetOwnerRef.Player(player.PlayerId).Key,
            "one explicitly resolved visible vehicle creates one stable runtime offer and ownership record");
        Check(acquired.State == AcquisitionState.Succeeded && retry.State == AcquisitionState.Succeeded && provider.Current!.Economy.Wallets.Single(x => x.Account.Key == AccountRef.Player(player.PlayerId).Key).Balance == 380 && sink.Calls >= 4,
            "runtime acquisition debits once, replays idempotently and stages every state transition");
    }

    private static void TestFleetClassificationAndPersistentManagement()
    {
        Check(FleetVehicleClassifier.Classify("LocoDiesel", "loco.de6") == FleetVehicleKind.Locomotive &&
            FleetVehicleClassifier.Classify("Passenger", "car.coach") == FleetVehicleKind.PassengerCar &&
            FleetVehicleClassifier.Classify("Boxcar", "freight.box") == FleetVehicleKind.FreightWagon &&
            FleetVehicleClassifier.Classify(null, null) == FleetVehicleKind.Unknown,
            "fleet classifier distinguishes locomotive, freight wagon, passenger car and unknown definitions");

        var fixture = Acquisition("fleet-management", 100, WorldOwnershipOutcome.Applied);
        Check(FleetVehicleClassifier.Classify(null, "FlatbedEmpty") == FleetVehicleKind.FreightWagon,
            "runtime flatbed identity is recognized without legacy Car prefix");
        var legacyFleet = new FleetAssetState { AssetId = "legacy-flatbed", Kind = FleetVehicleKind.Unknown, DisplayName = "My wagon", OperationalState = FleetOperationalState.Stored };
        fixture.Snapshot.Fleet.Add(legacyFleet);
        FleetManagementEngine.EnsureAsset(fixture.Snapshot, legacyFleet.AssetId, null, "FlatbedEmpty", "FlatbedEmpty");
        Check(legacyFleet.Kind == FleetVehicleKind.FreightWagon && legacyFleet.DisplayName == "My wagon" && legacyFleet.OperationalState == FleetOperationalState.Stored,
            "legacy classification repair preserves user name and deliberate operational state");
        fixture.Snapshot.Fleet.Remove(legacyFleet);
        fixture.Engine.Acquire(Buy("fleet-buy", "p", fixture, AssetOwnerRef.Player("p"), AccountRef.Player("p")));
        var fleet = FleetManagementEngine.EnsureAsset(fixture.Snapshot, fixture.Asset.AssetId, "LocoDiesel", fixture.Asset.DefinitionId, "DE6-001");
        var engine = new FleetManagementEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true));
        var rename = FleetCommand("fleet-rename", fixture, FleetCommandAction.Rename); rename.DisplayName = "Mainline One";
        var renamed = engine.Execute(rename);
        var retry = engine.Execute(rename);
        var service = FleetCommand("fleet-service", fixture, FleetCommandAction.SetOperationalState); service.OperationalState = FleetOperationalState.InService;
        var inService = engine.Execute(service);
        var illegal = FleetCommand("fleet-illegal", fixture, FleetCommandAction.SetOperationalState); illegal.OperationalState = FleetOperationalState.Maintenance;
        var rejected = engine.Execute(illegal);
        var longNameAsset = FleetAsset.Create("BDVM.ExternalVehicleDefinition.With.A.Long.Identifier", Guid.NewGuid().ToString("D"));
        fixture.Snapshot.Assets.Definitions.Add(new AssetDefinition { DefinitionId = longNameAsset.DefinitionId, Origin = "test" });
        fixture.Snapshot.Assets.Assets.Add(longNameAsset);
        fixture.Snapshot.Ownership.Add(new AssetOwnership { AssetId = longNameAsset.AssetId, Owner = AssetOwnerRef.Player("p") });
        var boundedName = FleetManagementEngine.EnsureAsset(fixture.Snapshot, longNameAsset.AssetId, "Freight", longNameAsset.DefinitionId, longNameAsset.DefinitionId);
        Check(boundedName.DisplayName.Length == 48 && boundedName.AssetId == longNameAsset.AssetId,
            "fleet display names derived from external definition IDs are bounded without changing persistent identity");
        var persisted = VehicleAcquisitionPersistence.Serialize(fixture.Snapshot);
        var restored = VehicleAcquisitionPersistence.Deserialize(persisted, "fleet-management");
        Check(renamed.Outcome == FleetCommandOutcome.Succeeded && object.ReferenceEquals(renamed, retry) && inService.Outcome == FleetCommandOutcome.Succeeded && rejected.ResultCode == "invalid-state-transition",
            "fleet rename and operational state commands are versioned, idempotent and transition-checked");
        Check(restored.Fleet.Single(x => x.AssetId == fixture.Asset.AssetId).DisplayName == "Mainline One" && restored.Fleet.Single(x => x.AssetId == fixture.Asset.AssetId).OperationalState == FleetOperationalState.InService && restored.FleetCommands.Count == 3,
            "fleet name, kind, state and command journal survive checkpoint serialization");
    }

    private static void TestFleetOwnershipTransferAndAuthority()
    {
        var fixture = Acquisition("fleet-transfer", 100, WorldOwnershipOutcome.Applied);
        fixture.Engine.Acquire(Buy("fleet-transfer-buy", "p", fixture, AssetOwnerRef.Player("p"), AccountRef.Player("p")));
        FleetManagementEngine.EnsureAsset(fixture.Snapshot, fixture.Asset.AssetId, "Freight", fixture.Asset.DefinitionId, "W-001");
        new CompanyEconomyEngine(fixture.Snapshot.Economy).CreateCompany(Command("fleet-company", "p"), "rail");
        var companyId = fixture.Snapshot.Economy.Players.Single(x => x.PlayerId == "p").CompanyId!;
        var transfer = FleetCommand("fleet-to-company", fixture, FleetCommandAction.TransferOwnership);
        transfer.Target = AssetOwnerRef.Company(companyId);
        var moved = new FleetManagementEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true)).Execute(transfer);
        var clientRename = FleetCommand("fleet-client-rename", fixture, FleetCommandAction.Rename); clientRename.DisplayName = "Spoofed";
        var refused = new FleetManagementEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerClient, false)).Execute(clientRename);
        var returnTransfer = FleetCommand("fleet-to-player", fixture, FleetCommandAction.TransferOwnership); returnTransfer.Target = AssetOwnerRef.Player("p");
        var returned = new FleetManagementEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true)).Execute(returnTransfer);
        Check(moved.Outcome == FleetCommandOutcome.Succeeded && returned.Outcome == FleetCommandOutcome.Succeeded && fixture.Snapshot.Ownership.Single().Owner.Key == "Player:p",
            "available fleet ownership transfers personal to company and back under leader authority");
        Check(refused.ResultCode == "host-authority-required" && fixture.Snapshot.Fleet.Single().DisplayName == "W-001",
            "client fleet mutation is rejected without changing authoritative state");
    }

    private static void TestHostPhysicalRemovalLeavesAuditButRemovesFleetEntry()
    {
        var fixture = OwnedFleet("fleet-radio-clear", 100, "LocoDiesel", "DE2-001");
        var engine = new FleetManagementEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true));
        var record = engine.ConfirmPhysicalRemoval("host-radio-clear:" + fixture.Asset.GameLink.Value, fixture.Asset.GameLink.Value!, "CarSpawner.CarAboutToBeDeleted");
        var replay = engine.ConfirmPhysicalRemoval("host-radio-clear:" + fixture.Asset.GameLink.Value, fixture.Asset.GameLink.Value!, "CarSpawner.CarAboutToBeDeleted");
        VehicleAcquisitionPersistence.Validate(fixture.Snapshot);
        var clientRefused = false;
        var other = OwnedFleet("fleet-radio-clear-client", 100, "LocoDiesel", "DE2-002");
        try { new FleetManagementEngine(other.Snapshot, RoleDetector(NetworkRole.MultiplayerClient, false)).ConfirmPhysicalRemoval("client-clear", other.Asset.GameLink.Value!, "radio"); }
        catch (InvalidOperationException) { clientRefused = true; }
        Check(record.ResultCode == "physical-removal-confirmed" && object.ReferenceEquals(record, replay) && fixture.Snapshot.Fleet.Count == 0 && fixture.Snapshot.Assets.Assets.Single().AssetId == record.AssetId && fixture.Snapshot.Ownership.Single().AssetId == record.AssetId && clientRefused,
            "host physical deletion removes the active Fleet entry exactly once, retains ownership/audit identity and refuses clients");
    }

    private static void TestFleetSnapshotV1MigratesToV2()
    {
        var fixture = Acquisition("fleet-migration", 100, WorldOwnershipOutcome.Applied);
        var currentJson = VehicleAcquisitionPersistence.Serialize(fixture.Snapshot);
        var currentMarker = "\"schemaVersion\":" + VehicleAcquisitionSnapshot.CurrentVersion;
        var markerIndex = currentJson.IndexOf(currentMarker, StringComparison.Ordinal);
        var legacyJson = currentJson.Remove(markerIndex, currentMarker.Length).Insert(markerIndex, "\"schemaVersion\":1");
        var migrated = VehicleAcquisitionPersistence.Deserialize(legacyJson, "fleet-migration");
        Check(migrated.SchemaVersion == VehicleAcquisitionSnapshot.CurrentVersion && migrated.Fleet != null && migrated.FleetCommands != null && migrated.ResaleQuotes != null && migrated.Resales != null,
            "vehicle acquisition snapshot v1 migrates through fleet and resale schema revisions");
    }

    private static void TestVehicleResaleCreditsOnceAndPersistsAudit()
    {
        var fixture = OwnedFleet("resale-success", 100, "LocoDiesel", "DE6-001");
        var guard = new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe");
        var engine = new VehicleResaleEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), guard, new FakeWorld(WorldOwnershipOutcome.Applied));
        var quote = engine.PrepareQuote("quote-one", "p", fixture.Asset.AssetId, 30, 50, ReferenceValueSource.ConfiguredModel, 0.8m, 0, 10);
        var command = Sell("sell-one", fixture, quote);
        var first = engine.Sell(command);
        var retry = engine.Sell(command);
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), "resale-success");
        Check(first.State == ResaleState.Succeeded && object.ReferenceEquals(first, retry) && fixture.Snapshot.Ownership.Single().Owner.Kind == AssetOwnerKind.Merchant,
            "resale transfers ownership to the merchant and an identical retry returns the durable result");
        Check(fixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Player:p").Balance == 90 && fixture.Snapshot.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.VehicleSale) == 1,
            "resale credits the explicit seller wallet exactly once");
        Check(restored.ResaleQuotes.Single().ReferenceValue == 50 && restored.Resales.Single().ObservedCondition == 0.8m && restored.Resales.Single().TransferFee == 10,
            "resale freezes and persists reference, condition, rate, fee and result audit data");
    }

    private static void TestVehicleResaleRefusesUnsafeOrClientSale()
    {
        var assigned = OwnedFleet("resale-assigned", 100, "Freight", "W-001");
        assigned.Snapshot.Fleet.Single().Operator = AssetOwnerRef.Player("p");
        var assignedEngine = new VehicleResaleEngine(assigned.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new FakeWorld(WorldOwnershipOutcome.Applied));
        var assignedQuote = assignedEngine.PrepareQuote("assigned-quote", "p", assigned.Asset.AssetId, 20, 40, ReferenceValueSource.ConfiguredModel, 1m, 0, 0);
        var assignedResult = assignedEngine.Sell(Sell("assigned-sell", assigned, assignedQuote));

        var coupled = OwnedFleet("resale-coupled", 100, "Freight", "W-002");
        var coupledWorld = new FakeWorld(WorldOwnershipOutcome.Applied);
        var coupledEngine = new VehicleResaleEngine(coupled.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Blocked, "vehicle-coupled"), coupledWorld);
        var coupledQuote = coupledEngine.PrepareQuote("coupled-quote", "p", coupled.Asset.AssetId, 20, 40, ReferenceValueSource.ConfiguredModel, 1m, 0, 0);
        var coupledResult = coupledEngine.Sell(Sell("coupled-sell", coupled, coupledQuote));

        var client = OwnedFleet("resale-client", 100, "Freight", "W-003");
        var hostQuoteEngine = new VehicleResaleEngine(client.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new FakeWorld(WorldOwnershipOutcome.Applied));
        var clientQuote = hostQuoteEngine.PrepareQuote("client-quote", "p", client.Asset.AssetId, 20, 40, ReferenceValueSource.ConfiguredModel, 1m, 0, 0);
        var clientResult = new VehicleResaleEngine(client.Snapshot, RoleDetector(NetworkRole.MultiplayerClient, false), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new FakeWorld(WorldOwnershipOutcome.Applied)).Sell(Sell("client-sell", client, clientQuote));
        Check(assignedResult.ResultCode == "operator-assignment-active" && coupledResult.ResultCode == "asset-not-releasable" && coupledWorld.ApplyCalls == 0,
            "resale refuses an assigned or physically blocked vehicle before world transfer and credit");
        Check(clientResult.ResultCode == "host-authority-required" && client.Snapshot.Ownership.Single().Owner.Kind == AssetOwnerKind.Player,
            "client resale is rejected without changing ownership or wallet");
    }

    private static void TestBundleResaleTransfersEveryComponentAndCreditsOnce()
    {
        var fixture = OwnedFleet("resale-bundle", 100, "LocoSteam", "Steam-001");
        var tender = AddOwnedFleetAsset(fixture, "Freight", "Tender-001");
        var bundleId = Guid.NewGuid().ToString("N");
        fixture.Snapshot.Assets.Bundles.Add(new AssetBundle { BundleId = bundleId, ComponentAssetIds = new List<string> { fixture.Asset.AssetId, tender.AssetId } });
        var engine = new VehicleResaleEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new FakeWorld(WorldOwnershipOutcome.Applied));
        var quote = engine.PrepareBundleQuote("bundle-quote", "p", bundleId, 70, 100, ReferenceValueSource.ConfiguredModel, 0.9m, 0, 0);
        var result = engine.Sell(Sell("bundle-sale", fixture, quote));
        Check(result.State == ResaleState.Succeeded && result.AssetIds.Count == 2 && fixture.Snapshot.Ownership.All(x => x.Owner.Kind == AssetOwnerKind.Merchant),
            "bundle resale transfers every physical component to the merchant as one commercial operation");
        Check(fixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Player:p").Balance == 130 && fixture.Snapshot.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.VehicleSale) == 1,
            "bundle resale produces one aggregate credit and one ledger entry");
    }

    private static void TestBundleResalePreflightIsAtomicAndRecoveryIsIdempotent()
    {
        var blocked = OwnedFleet("resale-bundle-blocked", 100, "LocoSteam", "Steam-002");
        var blockedTender = AddOwnedFleetAsset(blocked, "Freight", "Tender-002");
        var blockedBundleId = Guid.NewGuid().ToString("N");
        blocked.Snapshot.Assets.Bundles.Add(new AssetBundle { BundleId = blockedBundleId, ComponentAssetIds = new List<string> { blocked.Asset.AssetId, blockedTender.AssetId } });
        var blockedGuard = new PerVehicleReleaseGuard(new Dictionary<string, AssetReleaseStatus>
        {
            [blocked.Asset.GameLink.Value!] = AssetReleaseStatus.Releasable,
            [blockedTender.GameLink.Value!] = AssetReleaseStatus.Blocked
        });
        var blockedWorld = new FakeWorld(WorldOwnershipOutcome.Applied);
        var blockedEngine = new VehicleResaleEngine(blocked.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), blockedGuard, blockedWorld);
        var blockedQuote = blockedEngine.PrepareBundleQuote("bundle-blocked-quote", "p", blockedBundleId, 30, 100, ReferenceValueSource.ConfiguredModel, 0.8m, 0, 0);
        var blockedResult = blockedEngine.Sell(Sell("bundle-blocked-sale", blocked, blockedQuote));
        Check(blockedResult.State == ResaleState.Rejected && blockedWorld.ApplyCalls == 0 && blocked.Snapshot.Ownership.All(x => x.Owner.Kind == AssetOwnerKind.Player),
            "bundle preflight inspects every component before applying any world ownership change");

        var interrupted = OwnedFleet("resale-bundle-recovery", 100, "LocoSteam", "Steam-003");
        var interruptedTender = AddOwnedFleetAsset(interrupted, "Freight", "Tender-003");
        var interruptedBundleId = Guid.NewGuid().ToString("N");
        interrupted.Snapshot.Assets.Bundles.Add(new AssetBundle { BundleId = interruptedBundleId, ComponentAssetIds = new List<string> { interrupted.Asset.AssetId, interruptedTender.AssetId } });
        var firstWorld = new SequenceWorld(WorldOwnershipOutcome.Applied, WorldOwnershipOutcome.Unknown);
        var firstEngine = new VehicleResaleEngine(interrupted.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), firstWorld);
        var interruptedQuote = firstEngine.PrepareBundleQuote("bundle-recovery-quote", "p", interruptedBundleId, 30, 100, ReferenceValueSource.ConfiguredModel, 0.8m, 0, 0);
        var pending = firstEngine.Sell(Sell("bundle-recovery-sale", interrupted, interruptedQuote));
        Check(pending.State == ResaleState.ReconcileRequired && interrupted.Snapshot.Ownership.All(x => x.Owner.Kind == AssetOwnerKind.Player),
            "partial bundle world transfer remains explicitly recoverable without committing economic ownership");
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(interrupted.Snapshot), "resale-bundle-recovery");
        var recoveryEngine = new VehicleResaleEngine(restored, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new FakeWorld(WorldOwnershipOutcome.Applied));
        var recovered = recoveryEngine.Reconcile("bundle-recovery-sale");
        var retry = recoveryEngine.Reconcile("bundle-recovery-sale");
        Check(recovered.State == ResaleState.Succeeded && object.ReferenceEquals(recovered, retry) && restored.Ownership.All(x => x.Owner.Kind == AssetOwnerKind.Merchant),
            "bundle recovery after reload commits every component once when the world confirms the transfer");
        Check(restored.Economy.Wallets.Single(x => x.Account.Key == "Player:p").Balance == 90 && restored.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.VehicleSale) == 1,
            "bundle recovery credits exactly once across reload and retry");
    }

    private static void TestVehicleResaleRecoversAfterCreditWithoutDoublePayment()
    {
        var fixture = OwnedFleet("resale-after-credit", 100, "Freight", "W-004");
        var engine = new VehicleResaleEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new FakeWorld(WorldOwnershipOutcome.Applied))
            { FailurePoint = ResaleFailurePoint.AfterCredit };
        var quote = engine.PrepareQuote("after-credit-quote", "p", fixture.Asset.AssetId, 30, 50, ReferenceValueSource.ConfiguredModel, 0.8m, 0, 0);
        var pending = engine.Sell(Sell("after-credit-sale", fixture, quote));
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), "resale-after-credit");
        var recovery = new VehicleResaleEngine(restored, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new FakeWorld(WorldOwnershipOutcome.Applied));
        var completed = recovery.Reconcile("after-credit-sale");
        Check(pending.State == ResaleState.ReconcileRequired && completed.State == ResaleState.Succeeded && restored.Economy.Wallets.Single(x => x.Account.Key == "Player:p").Balance == 90,
            "recovery after a persisted credit completes the resale without paying twice");
        Check(restored.Economy.Ledger.Count(x => x.EntryId == "after-credit-sale:credit") == 1,
            "recovery after credit retains exactly one vehicle-sale ledger entry");
    }

    private static void TestConcurrentVehicleResalesProduceOneCredit()
    {
        var fixture = OwnedFleet("resale-concurrent", 100, "Freight", "W-005");
        var engine = new VehicleResaleEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new FakeWorld(WorldOwnershipOutcome.Applied));
        var quote = engine.PrepareQuote("concurrent-quote", "p", fixture.Asset.AssetId, 30, 50, ReferenceValueSource.ConfiguredModel, 0.8m, 0, 0);
        var first = Sell("concurrent-sale-a", fixture, quote);
        var second = Sell("concurrent-sale-b", fixture, quote);
        var results = Task.WhenAll(Task.Run(() => engine.Sell(first)), Task.Run(() => engine.Sell(second))).GetAwaiter().GetResult();
        Check(results.Count(x => x.State == ResaleState.Succeeded) == 1 && results.Count(x => x.State == ResaleState.Rejected) == 1,
            "two concurrent resale commands for one quote produce exactly one success");
        Check(fixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Player:p").Balance == 90 && fixture.Snapshot.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.VehicleSale) == 1,
            "concurrent resale credits the seller exactly once");
    }

    private static void TestGovernanceCommandsAreVersionedIdempotentAndPermissionChecked()
    {
        var engine = Economy("governance");
        engine.EnsurePlayer("leader", 0); engine.EnsurePlayer("member", 0); engine.EnsurePlayer("open-player", 0);
        engine.CreateCompany(Command("governance-create", "leader", "rail"), "Rail Company");
        var company = engine.State.Companies.Single();
        var policy = new EconomyCommand { CommandId = "governance-policy", RequesterId = "leader", CompanyId = company.CompanyId, ExpectedVersions = new Dictionary<string, long> { ["company:" + company.CompanyId] = company.Version } };
        var policyResult = engine.ChangeMembershipPolicy(policy, MembershipPolicy.Open);
        var openPlayer = engine.State.Players.Single(x => x.PlayerId == "open-player");
        var application = new EconomyCommand { CommandId = "governance-application", RequesterId = openPlayer.PlayerId, CompanyId = company.CompanyId, ExpectedVersions = new Dictionary<string, long> { ["player:" + openPlayer.PlayerId] = openPlayer.Version, ["company:" + company.CompanyId] = company.Version } };
        var applicationResult = engine.SubmitApplication(application, company.CompanyId);
        var member = engine.State.Players.Single(x => x.PlayerId == "member");
        var invitation = new EconomyCommand { CommandId = "governance-invitation", RequesterId = "leader", CompanyId = company.CompanyId, ExpectedVersions = new Dictionary<string, long> { ["company:" + company.CompanyId] = company.Version, ["player:" + member.PlayerId] = member.Version } };
        var invitationResult = engine.SendInvitation(invitation, member.PlayerId);
        var request = engine.State.MembershipRequests.Single(x => x.RequestId == invitation.CommandId);
        var response = new EconomyCommand { CommandId = "governance-invitation-response", RequesterId = member.PlayerId, CompanyId = company.CompanyId, ExpectedVersions = new Dictionary<string, long> { ["company:" + company.CompanyId] = company.Version, ["player:" + member.PlayerId] = member.Version, ["membership:" + request.RequestId] = request.Version } };
        var responseResult = engine.RespondToInvitation(response, request.RequestId, true);
        var delegation = new EconomyCommand { CommandId = "governance-delegation", RequesterId = "leader", CompanyId = company.CompanyId, ExpectedVersions = new Dictionary<string, long> { ["company:" + company.CompanyId] = company.Version } };
        var delegated = engine.ChangeDelegation(delegation, member.PlayerId, CompanyPermission.ManageFleet, true);
        var retry = engine.ChangeDelegation(delegation, member.PlayerId, CompanyPermission.ManageFleet, true);
        Check(policyResult.State == CommandState.Succeeded && applicationResult.ResultCode == "membership-accepted" && invitationResult.ResultCode == "invitation-pending" && responseResult.ResultCode == "invitation-accepted",
            "governance commands cover open applications and target-accepted invitations");
        Check(delegated.State == CommandState.Succeeded && object.ReferenceEquals(delegated, retry) && company.DelegatedPermissions[member.PlayerId].Count(x => x == CompanyPermission.ManageFleet) == 1,
            "governance delegation is permission-checked and idempotent");
        var transfer = new EconomyCommand { CommandId = "governance-leadership", RequesterId = "leader", CompanyId = company.CompanyId, ExpectedVersions = new Dictionary<string, long> { ["company:" + company.CompanyId] = company.Version } };
        Check(engine.TransferLeadershipCommand(transfer, member.PlayerId).State == CommandState.Succeeded && company.LeaderId == member.PlayerId,
            "leadership transfers only to a current member through a versioned command");
    }

    private static void TestGovernanceRuntimeRequiresHostAuthority()
    {
        var provider = new AcquisitionRuntimeStateProvider();
        provider.Provide("governance-runtime", null); var local = provider.EnsureLocalPlayer();
        provider.CreateCompany("governance-runtime-create", "Runtime Rail");
        var company = provider.Current!.Economy.Companies.Single();
        var refused = false;
        try { provider.SetMembershipPolicyFor("governance-client-policy", local.PlayerId, company.CompanyId, MembershipPolicy.Open, RoleDetector(NetworkRole.MultiplayerClient, false)); }
        catch (InvalidOperationException) { refused = true; }
        Check(refused && company.MembershipPolicy == MembershipPolicy.ApplicationWithApproval,
            "runtime governance refuses client-side mutation before changing authoritative company state");
    }

    private static void TestGovernanceClosesStaleMembershipRequestsAndPersists()
    {
        var engine = Economy("governance-stale-requests");
        engine.EnsurePlayer("leader-a", 0); engine.EnsurePlayer("leader-b", 0); engine.EnsurePlayer("candidate", 0);
        engine.CreateCompany(Command("governance-create-a", "leader-a", "company-a"), "Company A");
        engine.CreateCompany(Command("governance-create-b", "leader-b", "company-b"), "Company B");
        var companyA = engine.State.Companies.Single(x => x.CompanyId == "company-a");
        var companyB = engine.State.Companies.Single(x => x.CompanyId == "company-b");
        var candidate = engine.State.Players.Single(x => x.PlayerId == "candidate");
        EconomyCommand Apply(string id, CompanyState company) => new EconomyCommand
        {
            CommandId = id, RequesterId = candidate.PlayerId, CompanyId = company.CompanyId,
            ExpectedVersions = new Dictionary<string, long> { ["player:" + candidate.PlayerId] = candidate.Version, ["company:" + company.CompanyId] = company.Version }
        };
        var first = engine.SubmitApplication(Apply("application-a", companyA), companyA.CompanyId);
        var duplicate = engine.SubmitApplication(Apply("application-a-duplicate", companyA), companyA.CompanyId);
        engine.SubmitApplication(Apply("application-b", companyB), companyB.CompanyId);
        var requestA = engine.State.MembershipRequests.Single(x => x.RequestId == "application-a");
        var decision = new EconomyCommand
        {
            CommandId = "accept-a", RequesterId = "leader-a", CompanyId = companyA.CompanyId,
            ExpectedVersions = new Dictionary<string, long> { ["company:" + companyA.CompanyId] = companyA.Version, ["player:candidate"] = candidate.Version, ["membership:" + requestA.RequestId] = requestA.Version }
        };
        engine.DecideApplication(decision, requestA.RequestId, true);
        var restored = CompanyEconomyPersistence.Deserialize(CompanyEconomyPersistence.Serialize(engine.State), "governance-stale-requests");
        Check(first.ResultCode == "membership-pending" && duplicate.ResultCode == "membership-pending" && restored.MembershipRequests.Count(x => x.PlayerId == "candidate" && x.CompanyId == "company-a") == 1,
            "repeated membership intent reuses the existing durable pending request");
        Check(restored.Players.Single(x => x.PlayerId == "candidate").CompanyId == "company-a" && restored.MembershipRequests.Single(x => x.RequestId == "application-b").State == MembershipRequestState.Rejected,
            "joining one company closes stale requests for other companies and survives persistence");
    }

    private static void TestGovernanceNoOpsPreserveVersionsAndPermissions()
    {
        var engine = Economy("governance-no-op"); engine.EnsurePlayer("leader", 0); engine.EnsurePlayer("member", 0);
        engine.CreateCompany(Command("no-op-create", "leader", "no-op-company"), "No-op Company");
        engine.RequestMembership("no-op-invite", "member", "no-op-company", MembershipRequestKind.Invitation);
        engine.DecideMembership("leader", "no-op-invite", true);
        var company = engine.State.Companies.Single(); var version = company.Version;
        EconomyCommand Change(string id, string actor) => new EconomyCommand { CommandId = id, RequesterId = actor, CompanyId = company.CompanyId, ExpectedVersions = new Dictionary<string, long> { ["company:" + company.CompanyId] = version } };
        var currentPermission = engine.ChangeDelegation(Change("permission-current", "leader"), "member", CompanyPermission.ManageFunds, false);
        var unauthorized = engine.ChangeDelegation(Change("permission-unauthorized", "member"), "member", CompanyPermission.ManageFleet, false);
        var currentLeader = engine.TransferLeadershipCommand(Change("leadership-current", "leader"), "leader");
        var currentCompany = engine.State.Companies.Single();
        Check(currentPermission.State == CommandState.Succeeded && currentPermission.ResultCode == "permission-current" && currentLeader.State == CommandState.Succeeded && currentLeader.ResultCode == "leadership-current" && currentCompany.Version == version,
            "authorized governance no-ops are idempotent and do not create artificial version conflicts");
        Check(unauthorized.State == CommandState.Rejected && !currentCompany.DelegatedPermissions.ContainsKey("member"),
            "a no-op permission request still requires ManagePermissions authority");
    }

    private static void TestGovernanceCommandIdsRejectChangedPayloadAfterReload()
    {
        var engine = Economy("governance-command-fingerprint");
        engine.EnsurePlayer("leader", 0); engine.EnsurePlayer("first-target", 0); engine.EnsurePlayer("second-target", 0);
        engine.CreateCompany(Command("fingerprint-create", "leader", "fingerprint-company"), "Fingerprint Company");
        var company = engine.State.Companies.Single();
        EconomyCommand Invite(string target) => new EconomyCommand
        {
            CommandId = "stable-invitation-id", RequesterId = "leader", CompanyId = company.CompanyId,
            ExpectedVersions = new Dictionary<string, long> { ["company:" + company.CompanyId] = company.Version, ["player:" + target] = engine.State.Players.Single(x => x.PlayerId == target).Version }
        };
        engine.SendInvitation(Invite("first-target"), "first-target");
        var restored = CompanyEconomyPersistence.Deserialize(CompanyEconomyPersistence.Serialize(engine.State), "governance-command-fingerprint");
        var restoredEngine = new CompanyEconomyEngine(restored); var restoredCompany = restored.Companies.Single();
        var reused = restoredEngine.SendInvitation(new EconomyCommand
        {
            CommandId = "stable-invitation-id", RequesterId = "leader", CompanyId = restoredCompany.CompanyId,
            ExpectedVersions = new Dictionary<string, long> { ["company:" + restoredCompany.CompanyId] = restoredCompany.Version, ["player:second-target"] = restored.Players.Single(x => x.PlayerId == "second-target").Version }
        }, "second-target");
        Check(reused.State == CommandState.Rejected && reused.ResultCode == "command-id-reused" && restored.MembershipRequests.Count == 1 && restored.MembershipRequests.Single().PlayerId == "first-target",
            "a durable governance command ID cannot be rebound to a changed payload after reload");
    }

    private static void TestIndependentLeaveIsHostOnlyIdempotentAndPersistent()
    {
        var provider = new AcquisitionRuntimeStateProvider(); provider.Provide("governance-independent-leave", null); provider.EnsurePersistentPlayer("independent", 0);
        var host = RoleDetector(NetworkRole.MultiplayerHost, true);
        var first = provider.LeaveCompanyFor("independent-leave", "independent", host);
        var retry = provider.LeaveCompanyFor("independent-leave", "independent", host);
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(provider.Current!), "governance-independent-leave");
        var refused = false;
        try { provider.LeaveCompanyFor("independent-client-leave", "independent", RoleDetector(NetworkRole.MultiplayerClient, false)); }
        catch (InvalidOperationException) { refused = true; }
        Check(first.State == CommandState.Succeeded && first.ResultCode == "already-independent" && object.ReferenceEquals(first, retry) && restored.Economy.Commands.Count(x => x.CommandId == "independent-leave") == 1,
            "independent leave is journalled once and survives SaveGameData persistence");
        Check(refused && provider.Current!.Economy.Commands.All(x => x.CommandId != "independent-client-leave"),
            "client-side leave is refused before an authoritative command is written");
    }

    private static void TestDissolutionClosesPendingMembershipRequests()
    {
        var engine = Economy("governance-dissolution-requests"); engine.EnsurePlayer("leader", 0); engine.EnsurePlayer("candidate", 0);
        engine.CreateCompany(Command("dissolution-request-create", "leader", "closing-company"), "Closing Company");
        var company = engine.State.Companies.Single(); var candidate = engine.State.Players.Single(x => x.PlayerId == "candidate");
        engine.SubmitApplication(new EconomyCommand { CommandId = "pending-before-dissolution", RequesterId = candidate.PlayerId, CompanyId = company.CompanyId, ExpectedVersions = new Dictionary<string, long> { ["player:candidate"] = candidate.Version, ["company:" + company.CompanyId] = company.Version } }, company.CompanyId);
        engine.Dissolve(new EconomyCommand { CommandId = "dissolve-with-request", RequesterId = "leader", CompanyId = company.CompanyId, ExpectedVersions = new Dictionary<string, long> { ["company:" + company.CompanyId] = company.Version } }, Array.Empty<LiquidationAsset>(), 0, 0);
        var restored = CompanyEconomyPersistence.Deserialize(CompanyEconomyPersistence.Serialize(engine.State), "governance-dissolution-requests");
        Check(!restored.Companies.Any() && restored.MembershipRequests.Single().State == MembershipRequestState.Rejected && restored.MembershipRequests.Single().DecidedBy == "leader",
            "dissolution terminally closes company membership requests before removing the company");
    }

    private static void TestCompanyLiquidationSellsAssetsAndDistributesAfterLiabilities()
    {
        var fixture = CompanyOwnedFleet("company-liquidation", false);
        var company = fixture.Snapshot.Economy.Companies.Single();
        fixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Company:" + company.CompanyId).Balance = 100;
        var contracts = new FakeContractCancellationPort(WorldOwnershipOutcome.Applied);
        var engine = new CompanyLiquidationEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new FakeWorld(WorldOwnershipOutcome.Applied), contracts);
        var result = engine.Dissolve("liquidation-command", "p", company.CompanyId, 5, 1);
        Check(result.State == CompanyLiquidationState.Succeeded && result.ContractsCancelled && result.OwnershipCommitted && result.EconomyCommitted && contracts.CancelCalls == 1,
            "company liquidation cancels contracts and commits physical and economic ownership stages");
        Check(!fixture.Snapshot.Economy.Companies.Any() && fixture.Snapshot.Ownership.All(x => x.Owner.Kind == AssetOwnerKind.Merchant) && result.BeneficiaryIds.SequenceEqual(new[] { "m", "p" }),
            "company liquidation freezes beneficiaries and returns every company asset to the merchant");
        var distributed = fixture.Snapshot.Economy.Ledger.Where(x => x.Kind == LedgerEntryKind.Distribution).Sum(x => x.Amount);
        Check(distributed == 115 && fixture.Snapshot.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.DebtPayment) == 1,
            "liquidation deducts debts and penalties before equal distribution of the remaining balance");
    }

    private static void TestCompanyLiquidationPreflightAndRecoveryAreAtomic()
    {
        var blocked = CompanyOwnedFleet("company-liquidation-blocked", false);
        var blockedCompany = blocked.Snapshot.Economy.Companies.Single();
        var blockedWorld = new FakeWorld(WorldOwnershipOutcome.Applied);
        var blockedContracts = new FakeContractCancellationPort(WorldOwnershipOutcome.Applied);
        var blockedResult = new CompanyLiquidationEngine(blocked.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Blocked, "loaded"), blockedWorld, blockedContracts)
            .Dissolve("liquidation-blocked", "p", blockedCompany.CompanyId, 0, 0);
        Check(blockedResult.State == CompanyLiquidationState.Rejected && blockedWorld.ApplyCalls == 0 && blockedContracts.CancelCalls == 0 && blocked.Snapshot.Economy.Companies.Count == 1,
            "liquidation preflight refuses unsafe assets before cancelling contracts or mutating the world");

        var interrupted = CompanyOwnedFleet("company-liquidation-recovery", true);
        var interruptedCompany = interrupted.Snapshot.Economy.Companies.Single();
        var first = new CompanyLiquidationEngine(interrupted.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new SequenceWorld(WorldOwnershipOutcome.Applied, WorldOwnershipOutcome.Unknown), new FakeContractCancellationPort(WorldOwnershipOutcome.Applied));
        var pending = first.Dissolve("liquidation-recovery", "p", interruptedCompany.CompanyId, 0, 0);
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(interrupted.Snapshot), "company-liquidation-recovery");
        var recovery = new CompanyLiquidationEngine(restored, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new FakeWorld(WorldOwnershipOutcome.Applied), new FakeContractCancellationPort(WorldOwnershipOutcome.Applied));
        var completed = recovery.Reconcile("liquidation-recovery");
        var retry = recovery.Reconcile("liquidation-recovery");
        Check(pending.State == CompanyLiquidationState.ReconcileRequired && completed.State == CompanyLiquidationState.Succeeded && object.ReferenceEquals(completed, retry),
            "partial liquidation survives reload and completes idempotently after world reconciliation");
        Check(!restored.Economy.Companies.Any() && restored.Ownership.All(x => x.Owner.Kind == AssetOwnerKind.Merchant) && restored.Economy.Commands.Count(x => x.CommandId == "liquidation-recovery") == 1,
            "recovered liquidation removes the company and records its financial commit exactly once");
    }

    private static void TestCompanyLiquidationWriteAheadCheckpointSurvivesCrashWindow()
    {
        var fixture = CompanyOwnedFleet("company-liquidation-wal", false);
        var company = fixture.Snapshot.Economy.Companies.Single();
        var checkpoint = new RecordingLiquidationCheckpoint(() => VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), "contracts-cancelled");
        var pending = new CompanyLiquidationEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new FakeWorld(WorldOwnershipOutcome.Applied), new FakeContractCancellationPort(WorldOwnershipOutcome.Applied), checkpoint)
            .Dissolve("liquidation-wal", "p", company.CompanyId, 0, 0);
        Check(pending.State == CompanyLiquidationState.ReconcileRequired && pending.ResultCode == "checkpoint-contracts-failed" && checkpoint.SuccessfulPhases.SequenceEqual(new[] { "prepared" }),
            "liquidation stops after an external effect when the next durable checkpoint fails");

        var restored = VehicleAcquisitionPersistence.Deserialize(checkpoint.DurablePayload!, "company-liquidation-wal");
        var restoredRecord = restored.CompanyLiquidations.Single();
        Check(!restoredRecord.ContractsCancelled && restoredRecord.State == CompanyLiquidationState.ReconcileRequired,
            "the durable write-ahead record exists before contract cancellation and can drive crash recovery");
        var completed = new CompanyLiquidationEngine(restored, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new FakeWorld(WorldOwnershipOutcome.Applied), new FakeContractCancellationPort(WorldOwnershipOutcome.Applied))
            .Reconcile("liquidation-wal");
        Check(completed.State == CompanyLiquidationState.Succeeded && !restored.Economy.Companies.Any(),
            "write-ahead recovery inspects idempotent external effects and completes liquidation once");
    }

    private static void TestProtocolV1IntentRemainsCompatible()
    {
        var intent = new CompanyIntent { Type = CompanyIntentType.ApplyToCompany };
        var envelope = ProtocolEnvelope("protocol-v1", intent);
        envelope.ProtocolVersion = 1;
        envelope.Payload = CompanyIntentCodec.Encode(intent, 1);
        var decoded = CompanyProtocolCodec.Decode(CompanyProtocolCodec.Encode(envelope));
        var result = new CompanyProtocolHost(new FakeProtocolExecutor()).Receive(ReadyPeer(), decoded);
        Check(decoded.ProtocolVersion == 1 && result.Status == ProtocolResultStatus.Succeeded,
            "protocol v3 host retains bounded compatibility with the previous v1 intent payload");
    }

    private static void TestProtocolV2IntentRemainsCompatible()
    {
        var intent = new CompanyIntent { Type = CompanyIntentType.DissolveCompany, DebtAmount = 4, PenaltyAmount = 1 };
        var envelope = ProtocolEnvelope("protocol-v2", intent);
        envelope.ProtocolVersion = 2;
        envelope.Payload = CompanyIntentCodec.Encode(intent, 2);
        var decoded = CompanyProtocolCodec.Decode(CompanyProtocolCodec.Encode(envelope));
        var decodedIntent = CompanyIntentCodec.Decode(decoded.Payload, decoded.ProtocolVersion);
        var result = new CompanyProtocolHost(new FakeProtocolExecutor()).Receive(ReadyPeer(), decoded);
        Check(decoded.ProtocolVersion == 2 && decodedIntent.Type == CompanyIntentType.DissolveCompany && decodedIntent.DebtAmount == 4 && result.Status == ProtocolResultStatus.Succeeded,
            "protocol v3 host retains bounded compatibility with the previous v2 intent payload");
    }

    private static void TestConcurrentGovernanceVersionsAllowOnePermissionMutation()
    {
        var engine = Economy("governance-concurrent"); engine.EnsurePlayer("leader", 0); engine.EnsurePlayer("member", 0);
        engine.CreateCompany(Command("governance-concurrent-create", "leader", "co"), "Concurrent Governance");
        engine.RequestMembership("governance-concurrent-member", "member", "co", MembershipRequestKind.Invitation); engine.DecideMembership("leader", "governance-concurrent-member", true);
        var company = engine.State.Companies.Single();
        EconomyCommand Change(string id) => new EconomyCommand { CommandId = id, RequesterId = "leader", CompanyId = company.CompanyId, ExpectedVersions = new Dictionary<string, long> { ["company:" + company.CompanyId] = company.Version } };
        var first = Change("governance-concurrent-a"); var second = Change("governance-concurrent-b");
        var results = Task.WhenAll(Task.Run(() => engine.ChangeDelegation(first, "member", CompanyPermission.ManageFunds, true)), Task.Run(() => engine.ChangeDelegation(second, "member", CompanyPermission.ManageFleet, true))).GetAwaiter().GetResult();
        Check(results.Count(x => x.State == CommandState.Succeeded) == 1 && results.Count(x => x.State == CommandState.Rejected) == 1,
            "concurrent governance commands based on one company version permit exactly one mutation");
    }

    private static void TestRuntimeGovernanceIntentIsHostExecutedAndStaged()
    {
        var provider = new AcquisitionRuntimeStateProvider(); provider.Provide("governance-protocol-runtime", null);
        provider.EnsurePersistentPlayer("p1", 0); provider.EnsurePersistentPlayer("p2", 0);
        provider.CreateCompanyFor("governance-protocol-create", "p1", "Protocol Rail");
        var company = provider.Current!.Economy.Companies.Single();
        var staged = 0;
        var executor = new TestGovernanceIntentExecutor(provider, RoleDetector(NetworkRole.MultiplayerHost, true), () => staged++);
        var host = new CompanyProtocolHost(executor);
        var invite = ProtocolEnvelope("governance-protocol-invite", new CompanyIntent { Type = CompanyIntentType.InvitePlayer, TargetPlayerId = "p2" }); invite.CompanyId = company.CompanyId;
        var inviteResult = host.Receive(ReadyPeer(), invite);
        var request = provider.Current.Economy.MembershipRequests.Single(x => x.RequestId == invite.RequestId);
        var response = ProtocolEnvelope("governance-protocol-response", new CompanyIntent { Type = CompanyIntentType.RespondToInvitation, MembershipRequestId = request.RequestId, Enabled = true }, "p2"); response.CompanyId = company.CompanyId;
        var peer2 = new PeerContext { PeerKey = "peer-2", AuthenticatedPlayerId = "p2", IsKnown = true, IsReady = true };
        var responseResult = host.Receive(peer2, response);
        Check(inviteResult.Status == ProtocolResultStatus.Succeeded && responseResult.Status == ProtocolResultStatus.Succeeded && provider.Current.Economy.Players.Single(x => x.PlayerId == "p2").CompanyId == company.CompanyId,
            "authenticated Multiplayer governance intents are executed only by the host authority");
        Check(staged == 2, "each successful runtime governance intent stages the authoritative SaveGameData payload");
    }

    private static void TestOperatingCostPersonalAndCompanyPayersDoNotDoubleCharge()
    {
        var personal = OwnedFleet("operating-personal", 100, "LocoDiesel", "Personal Service");
        var personalEngine = new OperatingCostEngine(personal.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true));
        var personalSession = personalEngine.Begin(new ManualMaintenanceRequest { CommandId = "personal-service", RequesterId = "p", AssetId = personal.Asset.AssetId, Action = MaintenanceAction.Service, Payer = AccountRef.Player("p"), MaximumAuthorizedCost = 20, ExplicitUserConfirmation = true }, 60, 0.7m, "trip-1");
        var personalResult = personalEngine.Complete(personalSession.SessionId, 55, 0.9m);
        var personalRetry = personalEngine.Complete(personalSession.SessionId, 55, 0.9m);
        Check(personalResult.State == OperatingCostState.Settled && object.ReferenceEquals(personalResult, personalRetry) && personal.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Player:p").Balance == 55,
            "personal operating cost mirrors the observed vanilla debit exactly once without a second charge");

        var companyFixture = CompanyOwnedFleet("operating-company", false);
        var company = companyFixture.Snapshot.Economy.Companies.Single();
        var companyWallet = companyFixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Company:" + company.CompanyId); companyWallet.Balance = 50;
        var companyEngine = new OperatingCostEngine(companyFixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true));
        var companySession = companyEngine.Begin(new ManualMaintenanceRequest { CommandId = "company-service", RequesterId = "p", AssetId = companyFixture.Asset.AssetId, Action = MaintenanceAction.Refuel, Payer = AccountRef.Company(company.CompanyId), MaximumAuthorizedCost = 20, ExplicitUserConfirmation = true }, 60, 0.8m, "trip-2");
        var companyResult = companyEngine.Complete(companySession.SessionId, 50, 0.8m);
        var settled = companyEngine.MarkExternalSettlement(companySession.SessionId, 60);
        Check(companyResult.ActualCost == 10 && companyWallet.Balance == 40 && companyFixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Player:p").Balance == 60,
            "company-paid operating cost debits only the company while preserving the player's economic balance");
        Check(settled.ExternalSettlement == ExternalSettlementState.Applied && companyFixture.Snapshot.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.OperatingCost) == 1,
            "company reimbursement is explicitly acknowledged and one operating-cost ledger entry is retained");
    }

    private static void TestOperatingCostLimitsAndPersistenceFailClosed()
    {
        var fixture = OwnedFleet("operating-guards", 100, "Freight", "Guarded Service");
        var engine = new OperatingCostEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true));
        var session = engine.Begin(new ManualMaintenanceRequest { CommandId = "guarded-service", RequesterId = "p", AssetId = fixture.Asset.AssetId, Action = MaintenanceAction.Repair, Payer = AccountRef.Player("p"), MaximumAuthorizedCost = 4, ExplicitUserConfirmation = true }, 60, 0.5m);
        var result = engine.Complete(session.SessionId, 55, 0.8m);
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), "operating-guards");
        Check(result.State == OperatingCostState.Rejected && result.ResultCode == "authorized-cost-exceeded" && fixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Player:p").Balance == 55,
            "an observed cost above the manual authorization is rejected while mirroring, not repeating, the vanilla debit");
        Check(restored.OperatingCosts.Single().State == OperatingCostState.Rejected && restored.SchemaVersion == VehicleAcquisitionSnapshot.CurrentVersion,
            "operating cost records and their terminal refusal survive the versioned checkpoint");
    }

    private static void TestOperatingCostInterruptionAuthorityAndFleetGuards()
    {
        var fixture = OwnedFleet("operating-interruption", 100, "LocoDiesel", "Interrupted Service");
        var engine = new OperatingCostEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true));
        var open = engine.Begin(new ManualMaintenanceRequest { CommandId = "interrupted-service", RequesterId = "p", AssetId = fixture.Asset.AssetId, Action = MaintenanceAction.Service, Payer = AccountRef.Player("p"), MaximumAuthorizedCost = 20, ExplicitUserConfirmation = true }, 60, 0.6m);
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), "operating-interruption");
        var world = new FakeWorld(WorldOwnershipOutcome.Applied);
        var resale = new VehicleResaleEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), world);
        var quote = resale.PrepareQuote("interrupted-resale-quote", "p", fixture.Asset.AssetId, 20, 50, ReferenceValueSource.ConfiguredModel, 0.8m, 0, 0);
        var sale = resale.Sell(Sell("interrupted-resale", fixture, quote));
        Check(open.State == OperatingCostState.Open && restored.OperatingCosts.Single().State == OperatingCostState.Open && sale.ResultCode == "operating-cost-session-active" && world.ApplyCalls == 0,
            "an interrupted manual cost session survives reload and blocks vehicle sale before world mutation");
        var clientRefused = false;
        try { new OperatingCostEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerClient, false)).Begin(new ManualMaintenanceRequest { CommandId = "client-service", RequesterId = "p", AssetId = fixture.Asset.AssetId, Action = MaintenanceAction.Service, Payer = AccountRef.Player("p"), MaximumAuthorizedCost = 1, ExplicitUserConfirmation = true }, 60, 0.6m); }
        catch (InvalidOperationException) { clientRefused = true; }
        Check(clientRefused, "a client cannot open or settle an authoritative operating-cost session");

        var companyFixture = CompanyOwnedFleet("operating-insufficient", false);
        var company = companyFixture.Snapshot.Economy.Companies.Single();
        var companyWallet = companyFixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Company:" + company.CompanyId); companyWallet.Balance = 5;
        var companyEngine = new OperatingCostEngine(companyFixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true));
        var insufficientRefused = false;
        try { companyEngine.Begin(new ManualMaintenanceRequest { CommandId = "insufficient-company-service", RequesterId = "p", AssetId = companyFixture.Asset.AssetId, Action = MaintenanceAction.Repair, Payer = AccountRef.Company(company.CompanyId), MaximumAuthorizedCost = 20, ExplicitUserConfirmation = true }, 60, 0.5m); }
        catch (InvalidOperationException) { insufficientRefused = true; }
        Check(insufficientRefused && companyWallet.Balance == 5 && companyFixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Player:p").Balance == 60,
            "insufficient company funds refuse the session before vanilla maintenance can charge the player");
    }

    private static void TestFiniteMarketPricesStockExpiryAndReloadAreDeterministic()
    {
        var fixture = Acquisition("finite-market-state", 200, WorldOwnershipOutcome.Applied);
        ConfigureMarket(fixture.Snapshot, "loco.test", "Locomotive", 100, 1);
        FleetManagementEngine.EnsureAsset(fixture.Snapshot, fixture.Asset.AssetId, "LocoDiesel", fixture.Asset.DefinitionId, "Market Loco");
        var engine = new FiniteMarketEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeWorld(WorldOwnershipOutcome.Applied), new DisabledMarketDeliveryPort());
        var existing = engine.PublishExisting("market-existing", fixture.Asset.AssetId, "Harbor", 0.5m, 9m);
        var randomBefore = fixture.Snapshot.Market.RandomState;
        var order = engine.GenerateNewOrder("market-order", "loco.test", "Harbor");
        var saturatedRefused = false;
        try { engine.GenerateNewOrder("market-order-saturated", "loco.test", "Harbor"); } catch (InvalidOperationException) { saturatedRefused = true; }
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), "finite-market-state");
        Check(existing.MarketFactor == 1.2m && existing.ReferenceValue == 75 && existing.Price == 100,
            "finite market clamps factor and calculates price from configured model, condition and transfer fee");
        Check(order.Price == restored.Market.Listings.Single(x => x.ListingId == order.ListingId).Price && fixture.Snapshot.Market.RandomState != randomBefore && restored.Market.RandomState == fixture.Snapshot.Market.RandomState,
            "new-order quote and pseudo-random generator state survive reload unchanged");
        Check(saturatedRefused, "finite stock refuses another new-order listing while the location is saturated");
        new FiniteMarketEngine(restored, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeWorld(WorldOwnershipOutcome.Applied), new DisabledMarketDeliveryPort()).AdvanceTo(order.ExpiresTick);
        Check(restored.Market.Listings.Single(x => x.ListingId == order.ListingId).State == MarketListingState.Expired && restored.Market.Stock.Single().Available == 1,
            "expired unsold order returns its unit to the finite location stock without generating a replacement");
        var unknownRefused = false;
        try { engine.GenerateNewOrder("unknown-order", "unknown.definition", "Harbor"); } catch (InvalidOperationException) { unknownRefused = true; }
        Check(unknownRefused, "an unknown vehicle definition is never assigned a zero or invented price");
    }

    private static void TestFiniteMarketConcurrentPurchaseDebitsAndTransfersOnce()
    {
        var fixture = Acquisition("finite-market-concurrent", 100, WorldOwnershipOutcome.Applied);
        ConfigureMarket(fixture.Snapshot, "loco.test", "Locomotive", 80, 0);
        FleetManagementEngine.EnsureAsset(fixture.Snapshot, fixture.Asset.AssetId, "LocoDiesel", fixture.Asset.DefinitionId, "Concurrent Loco");
        var world = new FakeWorld(WorldOwnershipOutcome.Applied);
        var engine = new FiniteMarketEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), world, new DisabledMarketDeliveryPort());
        var listing = engine.PublishExisting("concurrent-listing", fixture.Asset.AssetId, "Harbor", 1m, 1m);
        MarketPurchaseCommand BuyMarket(string id) => new MarketPurchaseCommand { CommandId = id, RequesterId = "p", ListingId = listing.ListingId, Buyer = AssetOwnerRef.Player("p"), Payer = AccountRef.Player("p"), ExpectedListingVersion = listing.Version, ExpectedWalletVersion = 0, ExpectedPlayerVersion = 0 };
        var results = Task.WhenAll(Task.Run(() => engine.Purchase(BuyMarket("market-buy-a"))), Task.Run(() => engine.Purchase(BuyMarket("market-buy-b")))).GetAwaiter().GetResult();
        Check(results.Count(x => x.State == MarketPurchaseState.Succeeded) == 1 && results.Count(x => x.State == MarketPurchaseState.Rejected) == 1 && world.ApplyCalls == 1,
            "two buyers racing for one finite listing produce one world ownership transition");
        Check(fixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Player:p").Balance == 10 && fixture.Snapshot.Economy.Ledger.Count(x => x.EntryId.EndsWith(":market-debit", StringComparison.Ordinal)) == 1,
            "finite market purchase debits the exact frozen price once");
    }

    private static void TestFiniteMarketDeliveryRecoveryCreatesOneAsset()
    {
        var fixture = Acquisition("finite-market-delivery", 200, WorldOwnershipOutcome.Applied);
        ConfigureMarket(fixture.Snapshot, "new.loco", "Locomotive", 100, 1);
        var deliveredGuid = Guid.NewGuid().ToString("D");
        var engine = new FiniteMarketEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeWorld(WorldOwnershipOutcome.Applied), new DisabledMarketDeliveryPort());
        var listing = engine.GenerateNewOrder("delivery-listing", "new.loco", "Harbor");
        var wallet = fixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Player:p");
        var command = new MarketPurchaseCommand { CommandId = "delivery-purchase", RequesterId = "p", ListingId = listing.ListingId, Buyer = AssetOwnerRef.Player("p"), Payer = AccountRef.Player("p"), ExpectedListingVersion = listing.Version, ExpectedWalletVersion = wallet.Version, ExpectedPlayerVersion = 0 };
        var purchase = engine.Purchase(command);
        var grant = fixture.Snapshot.InitialDeliveries.Single();
        Check(purchase.State == MarketPurchaseState.Succeeded && grant.State == InitialDeliveryState.Available && fixture.Snapshot.Assets.Assets.Single(x => x.AssetId == purchase.AssetId).GameLink.State == PersistentLinkState.TemporarilyAbsent,
            "catalog purchase completes ownership without spawning and creates one free placement grant");
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), "finite-market-delivery");
        var restoredGrant = restored.InitialDeliveries.Single();
        var deliveryPort = new FakeInitialDelivery(
            new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Applied },
            new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Unknown, Detail = "spawn-result-lost" },
            new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Applied, PersistentCarGuids = new[] { deliveredGuid }, Detail = "found" });
        var delivery = new InitialDeliveryEngine(restored, RoleDetector(NetworkRole.MultiplayerHost, true), deliveryPort);
        var pending = delivery.Place(new InitialDeliveryCommand { CommandId = "place-delivery", RequesterId = "p", GrantId = restoredGrant.GrantId, TargetTrackId = "Harbor-Service-1", TargetKind = InitialDeliveryTargetKind.ServiceTrack, ExpectedGrantVersion = restoredGrant.Version });
        var pendingState = pending.State;
        var completed = delivery.Reconcile(restoredGrant.GrantId); var retry = delivery.Reconcile(restoredGrant.GrantId);
        Check(pendingState == InitialDeliveryState.ReconcileRequired && completed.State == InitialDeliveryState.Delivered && object.ReferenceEquals(completed, retry),
            "unknown initial placement survives reconciliation and the one-shot grant stays consumed");
        Check(restored.Assets.Assets.Count(x => string.Equals(x.GameLink.Value, deliveredGuid, StringComparison.OrdinalIgnoreCase)) == 1 && restored.Ownership.Single(x => x.AssetId == completed.AssetIds.Single()).Owner.Key == "Player:p",
            "delivery recovery registers one persistent asset with the intended owner");
        Check(restored.Economy.Ledger.Count(x => x.EntryId == "delivery-purchase:market-debit") == 1,
            "delivery recovery does not repeat the market debit");
    }

    private static void TestFiniteMarketWriteAheadCheckpointAndVirtualRecovery()
    {
        var blocked = Acquisition("finite-market-checkpoint-blocked", 200, WorldOwnershipOutcome.Applied);
        ConfigureMarket(blocked.Snapshot, "loco.test", "Locomotive", 100, 1);
        FleetManagementEngine.EnsureAsset(blocked.Snapshot, blocked.Asset.AssetId, "LocoDiesel", blocked.Asset.DefinitionId, "Checkpoint Loco");
        var blockedWorld = new FakeWorld(WorldOwnershipOutcome.Applied);
        var blockedEngine = new FiniteMarketEngine(blocked.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), blockedWorld, new DisabledMarketDeliveryPort(), new SequenceMarketCheckpoint(false));
        var existing = blockedEngine.PublishExisting("checkpoint-existing", blocked.Asset.AssetId, "Harbor", 1m, 1m);
        var wallet = blocked.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Player:p");
        var command = new MarketPurchaseCommand { CommandId = "checkpoint-buy", RequesterId = "p", ListingId = existing.ListingId, Buyer = AssetOwnerRef.Player("p"), Payer = AccountRef.Player("p"), ExpectedListingVersion = existing.Version, ExpectedWalletVersion = wallet.Version, ExpectedPlayerVersion = 0 };
        var compensated = blockedEngine.Purchase(command); var replay = blockedEngine.Purchase(command);
        Check(compensated.State == MarketPurchaseState.Compensated && object.ReferenceEquals(compensated, replay) && blockedWorld.ApplyCalls == 0 && wallet.Balance == 200,
            "a failed write-ahead checkpoint compensates once and never starts the world operation");
        Check(blocked.Snapshot.Economy.Ledger.Count(x => x.CommandId == command.CommandId) == 2,
            "the debit and its compensation remain auditable without changing the final balance");

        var recoverable = Acquisition("finite-market-result-recovery", 200, WorldOwnershipOutcome.Applied);
        ConfigureMarket(recoverable.Snapshot, "new.loco", "Locomotive", 100, 1);
        var recoveryEngine = new FiniteMarketEngine(recoverable.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeWorld(WorldOwnershipOutcome.Applied), new DisabledMarketDeliveryPort(), new SequenceMarketCheckpoint(true, false, true));
        var order = recoveryEngine.GenerateNewOrder("checkpoint-order", "new.loco", "Harbor");
        var recoveryWallet = recoverable.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Player:p");
        var pending = recoveryEngine.Purchase(new MarketPurchaseCommand { CommandId = "checkpoint-order-buy", RequesterId = "p", ListingId = order.ListingId, Buyer = AssetOwnerRef.Player("p"), Payer = AccountRef.Player("p"), ExpectedListingVersion = order.Version, ExpectedWalletVersion = recoveryWallet.Version, ExpectedPlayerVersion = 0 });
        var pendingState = pending.State;
        var assetCount = recoverable.Snapshot.Assets.Assets.Count;
        var completed = recoveryEngine.Reconcile(pending.CommandId); var retry = recoveryEngine.Reconcile(pending.CommandId);
        Check(pendingState == MarketPurchaseState.ReconcileRequired && completed.State == MarketPurchaseState.Succeeded && object.ReferenceEquals(completed, retry) && recoverable.Snapshot.Assets.Assets.Count == assetCount,
            "a lost result checkpoint reconciles a virtual purchase without creating a second asset or delivery grant");
    }

    private static void TestStarterBundleGrantAndAtomicPlacement()
    {
        var fixture = Acquisition("starter-bundle", 100, WorldOwnershipOutcome.Applied);
        var engine = new InitialDeliveryEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeInitialDelivery(
            new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Applied },
            new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Applied, PersistentCarGuids = new[] { Guid.NewGuid().ToString("D") } },
            new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Unknown }));
        var definitions = new[] { "LocoDE2", "CarFlatcar", "CarFlatcar", "CarFlatcar" };
        var grant = engine.GrantStarterBundle("starter-grant:p", "p", definitions);
        var replay = engine.GrantStarterBundle("starter-grant:p", "p", definitions);
        var grants = fixture.Snapshot.InitialDeliveries.Where(x => x.SourceCommandId == "starter-grant:p").OrderBy(x => x.GrantId).ToArray();
        Check(object.ReferenceEquals(grant, replay) && grants.Length == 4 && grants.All(x => x.AssetIds.Count == 1) &&
              fixture.Snapshot.Assets.Bundles.Single().ComponentAssetIds.SequenceEqual(grants.SelectMany(x => x.AssetIds)),
            "starter DE2 and three-wagon bundle is granted once with one persistent radio delivery per vehicle");
        foreach (var item in grants)
        {
            var placed = engine.Place(new InitialDeliveryCommand { CommandId = "starter-place:" + item.GrantId, RequesterId = "p", GrantId = item.GrantId, TargetTrackId = "Depot-1", TargetKind = InitialDeliveryTargetKind.Depot, ExpectedGrantVersion = item.Version });
            Check(placed.State == InitialDeliveryState.Delivered && placed.AssetIds.All(id => fixture.Snapshot.Assets.Assets.Single(x => x.AssetId == id).GameLink.State == PersistentLinkState.Resolved) && fixture.Snapshot.Fleet.Where(x => placed.AssetIds.Contains(x.AssetId)).All(x => x.LastKnownLocation == "Depot-1"),
                "each starter vehicle placement commits only after its physical component is confirmed");
        }
        var duplicateRefused = false;
        try { engine.GrantStarterBundle("starter-grant:p:again", "p", definitions); } catch (InvalidOperationException) { duplicateRefused = true; }
        Check(duplicateRefused, "a persistent player identity cannot receive the starter bundle twice");
    }

    private static void TestInitialDeliveryWriteAheadCheckpointPreventsDuplicateSpawn()
    {
        var fixture = Acquisition("initial-delivery-checkpoint", 100, WorldOwnershipOutcome.Applied);
        var definition = new[] { "LocoDE2" };
        var grant = new InitialDeliveryEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new DisabledInitialDeliveryPort())
            .GrantStarterBundle("checkpoint-starter", "p", definition);
        var blockedPort = new FakeInitialDelivery(
            new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Applied },
            new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Applied, PersistentCarGuids = new[] { Guid.NewGuid().ToString("D") } },
            new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.NotApplied });
        var blocked = new InitialDeliveryEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), blockedPort,
            new SequenceInitialDeliveryCheckpoint(false)).Place(new InitialDeliveryCommand
            {
                CommandId = "checkpoint-place-blocked", RequesterId = "p", GrantId = grant.GrantId,
                TargetTrackId = "Depot-1", TargetKind = InitialDeliveryTargetKind.Depot, ExpectedGrantVersion = grant.Version
            });
        Check(blocked.State == InitialDeliveryState.ReconcileRequired && blockedPort.PlaceCalls == 0,
            "initial delivery never spawns when its write-ahead pending checkpoint cannot be staged");

        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), "initial-delivery-checkpoint");
        var restoredGrant = restored.InitialDeliveries.Single();
        var released = new InitialDeliveryEngine(restored, RoleDetector(NetworkRole.MultiplayerHost, true), blockedPort).Reconcile(restoredGrant.GrantId);
        Check(released.State == InitialDeliveryState.Available && released.PlacementCommandId == null && released.TargetTrackId == null && !released.TargetKind.HasValue &&
              released.ResultCode.StartsWith("placement-released-for-radio-retry:", StringComparison.Ordinal),
            "an unobserved delivery attempt releases its unresolved entitlement back to radio placement");
        var deliveredGuid = Guid.NewGuid().ToString("D");
        var appliedPort = new FakeInitialDelivery(
            new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Applied },
            new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Applied, PersistentCarGuids = new[] { deliveredGuid } },
            new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Applied, PersistentCarGuids = new[] { deliveredGuid } });
        var pendingAfterLostCheckpoint = new InitialDeliveryEngine(restored, RoleDetector(NetworkRole.MultiplayerHost, true), appliedPort,
            new SequenceInitialDeliveryCheckpoint(true, false)).Place(new InitialDeliveryCommand
            {
                CommandId = "checkpoint-place-applied", RequesterId = "p", GrantId = restoredGrant.GrantId,
                TargetTrackId = "Depot-1", TargetKind = InitialDeliveryTargetKind.Depot, ExpectedGrantVersion = restoredGrant.Version
            });
        Check(pendingAfterLostCheckpoint.State == InitialDeliveryState.ReconcileRequired && appliedPort.PlaceCalls == 1,
            "a confirmed spawn with a lost result checkpoint remains reconcile-only instead of reopening its entitlement");
        var final = new InitialDeliveryEngine(restored, RoleDetector(NetworkRole.MultiplayerHost, true), appliedPort).Reconcile(restoredGrant.GrantId);
        Check(final.State == InitialDeliveryState.Delivered && appliedPort.PlaceCalls == 1 && appliedPort.InspectCalls == 1,
            "reconciliation confirms the original operation without issuing a second physical spawn");
    }

    private static void TestFiniteMarketBuybackUsesConfiguredMargin()
    {
        var fixture = OwnedFleet("finite-market-buyback", 100, "Locomotive", "Buyback Loco");
        ConfigureMarket(fixture.Snapshot, fixture.Asset.DefinitionId, "Locomotive", 100, 0);
        var market = new FiniteMarketEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeWorld(WorldOwnershipOutcome.Applied), new DisabledMarketDeliveryPort());
        var quote = market.PrepareBuybackQuote("market-buyback", "p", fixture.Asset.AssetId, 1m, 9m, 5, new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"));
        Check(quote.ReferenceValue == 100 && quote.Proceeds == 55 && quote.AppliedRate == 0.619048m && quote.TransferFee == 10,
            "market buyback clamps the dynamic factor and applies the configured margin, fuel value and transfer fee");
        Check(quote.Proceeds < 130, "market buyback cannot exceed the equivalent purchase price and create immediate arbitrage");
    }

    private static void TestLeaseDepositClockReloadAndReturn()
    {
        var fixture = Acquisition("lease-lifecycle", 200, WorldOwnershipOutcome.Applied);
        FleetManagementEngine.EnsureAsset(fixture.Snapshot, fixture.Asset.AssetId, "Locomotive", fixture.Asset.DefinitionId, "Lease Loco");
        var engine = new LeaseEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new FakeWorld(WorldOwnershipOutcome.Applied));
        var lease = engine.CreateOffer("lease-a", new[] { fixture.Asset.AssetId }, 50, 10, 20, 10, 30, 100, 1m, 100);
        var accepted = engine.Accept("lease-accept", "p", lease.LeaseId, AssetOwnerRef.Player("p"), AccountRef.Player("p"), lease.Version, 0);
        var acceptedReplay = engine.Accept("lease-accept", "p", lease.LeaseId, AssetOwnerRef.Player("p"), AccountRef.Player("p"), 999, 999);
        var leasedFleet = fixture.Snapshot.Fleet.Single(); var leasedOwnership = fixture.Snapshot.Ownership.Single();
        var leaseRename = new FleetManagementEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true)).Execute(new FleetCommand { CommandId = "lease-rename", RequesterId = "p", AssetId = fixture.Asset.AssetId, Action = FleetCommandAction.Rename, DisplayName = "Rented Loco", ExpectedFleetVersion = leasedFleet.Version, ExpectedOwnershipVersion = leasedOwnership.Version });
        var leaseTransfer = new FleetManagementEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true)).Execute(new FleetCommand { CommandId = "lease-transfer", RequesterId = "p", AssetId = fixture.Asset.AssetId, Action = FleetCommandAction.TransferOwnership, Target = AssetOwnerRef.Player("p"), ExpectedFleetVersion = leasedFleet.Version, ExpectedOwnershipVersion = leasedOwnership.Version });
        engine.Advance(new LeaseClockAdvance { CommandId = "clock-closed", SessionOpen = false, ActiveGameplayTicks = 10 });
        engine.Advance(new LeaseClockAdvance { CommandId = "clock-paused", SessionOpen = true, Paused = true, ActiveGameplayTicks = 10 });
        var tick = engine.Advance(new LeaseClockAdvance { CommandId = "clock-active", SessionOpen = true, ActiveGameplayTicks = 4, SleepTicks = 3, FastTravelTicks = 3 });
        var retryTick = engine.Advance(new LeaseClockAdvance { CommandId = "clock-active", SessionOpen = true, ActiveGameplayTicks = 4, SleepTicks = 3, FastTravelTicks = 3 });
        var wallet = fixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Player:p");
        Check(object.ReferenceEquals(accepted, acceptedReplay) && accepted.State == LeaseActionState.Succeeded && lease.HeldDeposit == 50 && wallet.Balance == 120 && tick == 10 && retryTick == 10 && lease.Installments.Count == 1,
            "lease holds deposit separately, ignores closed/paused time and charges one due installment across retry");
        Check(leaseRename.Outcome == FleetCommandOutcome.Succeeded && leaseTransfer.ResultCode == "leased-asset-operation-only" && fixture.Snapshot.Ownership.Single().Owner.Kind == AssetOwnerKind.Merchant,
            "lessee may operate and rename rented equipment but cannot transfer its ownership");
        Check(fixture.Snapshot.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.LeaseDeposit) == 1 && !fixture.Snapshot.Economy.Ledger.Any(x => x.Kind == LedgerEntryKind.LeaseDeposit && x.Credit != null),
            "held lease deposit is not recorded as merchant or company income");
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), "lease-lifecycle");
        var recovered = new LeaseEngine(restored, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new FakeWorld(WorldOwnershipOutcome.Applied));
        var dueTick = recovered.Advance(new LeaseClockAdvance { CommandId = "clock-to-return-due", SessionOpen = true, ActiveGameplayTicks = 20 });
        var dueLease = restored.Leases.Single();
        Check(dueTick == 30 && dueLease.State == LeaseState.ReturnDue && dueLease.Installments.Count == 3,
            "lease duration expiry charges the final due installment once and enters an explicit return-due state");
        var returned = recovered.Return("lease-return", "p", "lease-a", 0.8m); var returnedReplay = recovered.Return("lease-return", "p", "lease-a", 0.1m); var restoredLease = restored.Leases.Single();
        Check(object.ReferenceEquals(returned, returnedReplay) && returned.State == LeaseActionState.Succeeded && returned.Amount == 30 && restoredLease.State == LeaseState.Returned && restoredLease.HeldDeposit == 0 && restoredLease.OutstandingDebt == 0,
            "safe return after reload applies damage to held deposit and refunds only the remainder");
    }

    private static void TestLeaseDelinquencyAndUnsafeReturn()
    {
        var fixture = Acquisition("lease-delinquent", 10, WorldOwnershipOutcome.Applied);
        FleetManagementEngine.EnsureAsset(fixture.Snapshot, fixture.Asset.AssetId, "Freight", fixture.Asset.DefinitionId, "Lease Wagon");
        var blocked = new LeaseEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Blocked, "active-job"), new FakeWorld(WorldOwnershipOutcome.Applied));
        var lease = blocked.CreateOffer("lease-b", new[] { fixture.Asset.AssetId }, 0, 0, 20, 5, 20, null, 1m, 100);
        blocked.Accept("lease-b-accept", "p", lease.LeaseId, AssetOwnerRef.Player("p"), AccountRef.Player("p"), lease.Version, 0);
        blocked.Advance(new LeaseClockAdvance { CommandId = "lease-b-clock", SessionOpen = true, ActiveGameplayTicks = 5 });
        var saleRefused = false;
        try { new VehicleResaleEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new FakeWorld(WorldOwnershipOutcome.Applied)).PrepareQuote("leased-sale", "p", fixture.Asset.AssetId, 1, 10, ReferenceValueSource.ConfiguredModel, 1m, 0, 0); }
        catch (InvalidOperationException) { saleRefused = true; }
        var refused = blocked.Return("lease-b-return", "p", lease.LeaseId, 0.9m);
        Check(lease.State == LeaseState.Delinquent && lease.OutstandingDebt == 20 && saleRefused && refused.State == LeaseActionState.Rejected && fixture.Snapshot.Fleet.Single().Operator?.Key == "Player:p",
            "late rent becomes explicit debt, sale stays forbidden and an active-job return leaves the wagon and operator untouched");
    }

    private static void TestLeasePurchaseOptionAndClientRefusal()
    {
        var fixture = Acquisition("lease-purchase", 200, WorldOwnershipOutcome.Applied);
        FleetManagementEngine.EnsureAsset(fixture.Snapshot, fixture.Asset.AssetId, "Locomotive", fixture.Asset.DefinitionId, "Lease Purchase Loco");
        var second = FleetAsset.Create(fixture.Asset.DefinitionId, Guid.NewGuid().ToString("D")); second.GameLink.State = PersistentLinkState.Resolved;
        fixture.Snapshot.Assets.Assets.Add(second); fixture.Snapshot.Ownership.Add(new AssetOwnership { AssetId = second.AssetId, Owner = AssetOwnerRef.Merchant("m") }); FleetManagementEngine.EnsureAsset(fixture.Snapshot, second.AssetId, "Freight", second.DefinitionId, "Lease Purchase Wagon");
        fixture.Snapshot.Assets.Bundles.Add(new AssetBundle { BundleId = Guid.NewGuid().ToString("N"), ComponentAssetIds = new List<string> { fixture.Asset.AssetId, second.AssetId } });
        var world = new FakeWorld(WorldOwnershipOutcome.Applied);
        var host = new LeaseEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), world);
        var incompleteBundleRefused = false;
        try { host.CreateOffer("lease-incomplete-bundle", new[] { fixture.Asset.AssetId }, 0, 0, 0, 10, 30, null, 1m, 0); }
        catch (InvalidOperationException) { incompleteBundleRefused = true; }
        var lease = host.CreateOffer("lease-c", new[] { fixture.Asset.AssetId, second.AssetId }, 50, 0, 0, 10, 30, 100, 1m, 0);
        host.Accept("lease-c-accept", "p", lease.LeaseId, AssetOwnerRef.Player("p"), AccountRef.Player("p"), lease.Version, 0);
        var purchased = host.ExercisePurchaseOption("lease-c-purchase", "p", lease.LeaseId);
        Check(incompleteBundleRefused && purchased.State == LeaseActionState.Succeeded && lease.State == LeaseState.Purchased && fixture.Snapshot.Ownership.All(x => x.Owner.Key == "Player:p") && fixture.Snapshot.Economy.Wallets.Single().Balance == 100 && world.ApplyCalls == 2,
            "bundle purchase option consumes held deposit, debits only the remainder and transfers every component once");
        var clientRefused = false;
        try { new LeaseEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerClient, false), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), world).CreateOffer("client-lease", new[] { fixture.Asset.AssetId }, 0, 0, 0, 1, 1, null, 1m, 0); }
        catch (InvalidOperationException) { clientRefused = true; }
        Check(clientRefused, "multiplayer client cannot create or mutate an authoritative lease");
    }

    private static void TestCatalogLeaseConsumesFiniteListingAndGrantsPlacement()
    {
        var fixture = Acquisition("lease-catalog", 500, WorldOwnershipOutcome.Applied);
        ConfigureMarket(fixture.Snapshot, "CarBox", "FreightWagon", 100, 2);
        var authority = RoleDetector(NetworkRole.MultiplayerHost, true);
        var market = new FiniteMarketEngine(fixture.Snapshot, authority, new FakeWorld(WorldOwnershipOutcome.Applied), new DisabledMarketDeliveryPort());
        var first = market.GenerateNewOrder("lease-listing-a", "CarBox", "Harbor");
        var second = market.GenerateNewOrder("lease-listing-b", "CarBox", "Harbor");
        var leaseEngine = new LeaseEngine(fixture.Snapshot, authority, new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new FakeWorld(WorldOwnershipOutcome.Applied));
        var lease = leaseEngine.CreateCatalogListingOffer("lease-catalog-a", new[] { first.ListingId, second.ListingId }, 50, 10, 5, 10, 30, 150, 1m, 100);
        Check(fixture.Snapshot.Market.Stock.Single(x => x.DefinitionId == "CarBox").Available == 0 &&
              fixture.Snapshot.Market.Listings.Where(x => x.ReservedBy == "lease:" + lease.LeaseId).All(x => x.State == MarketListingState.Sold) &&
              lease.AssetIds.Count == 2 && lease.AssetIds.All(id => fixture.Snapshot.Ownership.Single(x => x.AssetId == id).Owner.Key == "Merchant:market") &&
              lease.AssetIds.All(id => fixture.Snapshot.Assets.Assets.Single(x => x.AssetId == id).GameLink.State == PersistentLinkState.TemporarilyAbsent),
            "catalog lease consumes exact finite listings and creates merchant-owned virtual rolling stock once");
        var accepted = leaseEngine.Accept("lease-catalog-accept", "p", lease.LeaseId, AssetOwnerRef.Player("p"), AccountRef.Player("p"), lease.Version, fixture.Snapshot.Economy.Wallets.Single().Version);
        var grants = new InitialDeliveryEngine(fixture.Snapshot, authority, new DisabledInitialDeliveryPort()).GrantLeaseDelivery("lease-catalog-accept:delivery", lease.LeaseId);
        new CompanyEconomyEngine(fixture.Snapshot.Economy).EnsurePlayer("intruder", 0);
        var unauthorized = false;
        try
        {
            new InitialDeliveryEngine(fixture.Snapshot, authority, new DisabledInitialDeliveryPort()).Place(new InitialDeliveryCommand
            {
                CommandId = "lease-catalog-intruder", RequesterId = "intruder", GrantId = grants[0].GrantId,
                TargetTrackId = "Harbor-Service", TargetKind = InitialDeliveryTargetKind.ServiceTrack, ExpectedGrantVersion = grants[0].Version
            });
        }
        catch (UnauthorizedAccessException) { unauthorized = true; }
        foreach (var grant in grants)
        {
            var guid = Guid.NewGuid().ToString("D");
            var delivery = new FakeInitialDelivery(
                new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Applied },
                new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Applied, PersistentCarGuids = new[] { guid } },
                new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Applied, PersistentCarGuids = new[] { guid } });
            new InitialDeliveryEngine(fixture.Snapshot, authority, delivery).Place(new InitialDeliveryCommand
            {
                CommandId = "lease-catalog-place:" + grant.GrantId, RequesterId = "p", GrantId = grant.GrantId,
                TargetTrackId = "Harbor-Service", TargetKind = InitialDeliveryTargetKind.ServiceTrack, ExpectedGrantVersion = grant.Version
            });
        }
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), "lease-catalog");
        Check(accepted.State == LeaseActionState.Succeeded && unauthorized && grants.All(x => x.AuthorizedOperator?.Key == "Player:p" && x.State == InitialDeliveryState.Delivered) &&
              restored.InitialDeliveries.All(x => x.AuthorizedOperator?.Key == "Player:p") && restored.Ownership.Where(x => lease.AssetIds.Contains(x.AssetId)).All(x => x.Owner.Key == "Merchant:market") &&
              restored.Fleet.Where(x => lease.AssetIds.Contains(x.AssetId)).All(x => x.Operator?.Key == "Player:p"),
            "accepted catalog lease grants placement only to the lessee while preserving merchant ownership across save and reload");
    }

    private static void TestMissionAssignmentPaysIndependentExactlyOnce()
    {
        var fixture = OwnedFleet("assignment-personal", 100, "Locomotive", "Mission Loco");
        var engine = new MissionAssignmentEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeMissionCompletion(WorldOwnershipOutcome.Applied));
        var assignment = engine.Reserve("assignment-reserve", "p", "assignment-a", "job-a", MissionAssignmentKind.Freight, new[] { fixture.Asset.AssetId }, AssetOwnerRef.Player("p"), 100);
        engine.Start("assignment-start", "p", assignment.AssignmentId, 100);
        var completed = engine.Complete("assignment-complete", "p", assignment.AssignmentId, 150, assignment.AssetIds);
        var retry = engine.Complete("assignment-complete", "p", assignment.AssignmentId, 999, assignment.AssetIds);
        Check(object.ReferenceEquals(completed, retry) && completed.State == MissionAssignmentState.Completed && fixture.Snapshot.Economy.Wallets.Single().Balance == 150 && fixture.Snapshot.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.MissionRevenue) == 1,
            "independent mission mirrors the observed vanilla payout exactly once across retry");
        Check(fixture.Snapshot.Fleet.Single().OperationalState == FleetOperationalState.Available,
            "completed mission keeps the consist in the fleet and releases its reservation");
    }

    private static void TestMissionAssignmentRoutesFrozenCompanyRevenue()
    {
        var fixture = CompanyOwnedFleet("assignment-company", false); var company = fixture.Snapshot.Economy.Companies.Single();
        fixture.Snapshot.Fleet.Single(value => value.AssetId == fixture.Asset.AssetId).Kind = FleetVehicleKind.PassengerCar;
        company.LeaderId = "m"; company.DelegatedPermissions["m"] = new List<CompanyPermission> { CompanyPermission.ManageFleet, CompanyPermission.ManageFunds }; company.Members.Remove("p"); fixture.Snapshot.Economy.Players.Single(x => x.PlayerId == "p").CompanyId = null;
        var engine = new MissionAssignmentEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeMissionCompletion(WorldOwnershipOutcome.Applied));
        var assignment = engine.Reserve("company-assignment-reserve", "m", "assignment-company", "passenger-a", MissionAssignmentKind.Passenger, new[] { fixture.Asset.AssetId }, AssetOwnerRef.Company(company.CompanyId), 100);
        engine.Start("company-assignment-start", "m", assignment.AssignmentId, 100);
        var completed = engine.Complete("company-assignment-complete", "m", assignment.AssignmentId, 140, assignment.AssetIds);
        Check(completed.Operator.Key == "Company:" + company.CompanyId && completed.ExternalSettlement == ExternalSettlementState.Pending && completed.ExpectedVanillaBalance == 100 && fixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Company:" + company.CompanyId).Balance == 40,
            "company mission routes frozen operator revenue to company and stages removal from host vanilla wallet");
        var settled = engine.MarkExternalSettlement(assignment.AssignmentId, 100);
        Check(settled.ExternalSettlement == ExternalSettlementState.Applied, "company mission external settlement is explicit and recoverable");
    }

    private static void TestMissionSettlementUsesExactJobRevenue()
    {
        var fixture = OwnedFleet("assignment-exact-settlement", 100, "Locomotive", "Exact Settlement Loco");
        var settlement = new FakeMissionSettlement(40);
        var engine = new MissionAssignmentEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), settlement);
        var assignment = engine.Reserve("exact-reserve", "p", "assignment-exact", "job-exact", MissionAssignmentKind.Freight, new[] { fixture.Asset.AssetId }, AssetOwnerRef.Player("p"), 100);
        engine.Start("exact-start", "p", assignment.AssignmentId, 100);
        var completed = engine.Complete("exact-complete", "p", assignment.AssignmentId, 115, assignment.AssetIds);
        var ledger = fixture.Snapshot.Economy.Ledger.Single(value => value.Kind == LedgerEntryKind.MissionRevenue);
        Check(completed.ActualRevenue == 40 && fixture.Snapshot.Economy.Wallets.Single().Balance == 115 && ledger.Amount == 40 && ledger.Detail.Contains("source=exact-job-settlement"),
            "exact job settlement isolates mission revenue from unrelated vanilla wallet changes");
    }

    private static void TestMissionAssignmentPartialBlockedCancelAndClient()
    {
        var fixture = OwnedFleet("assignment-blocked", 100, "Locomotive", "Lead"); var second = AddOwnedFleetAsset(fixture, "Freight", "Wagon");
        var blocked = new MissionAssignmentEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeMissionCompletion(WorldOwnershipOutcome.NotApplied));
        var assignment = blocked.Reserve("blocked-reserve", "p", "assignment-blocked", "job-blocked", MissionAssignmentKind.Freight, new[] { fixture.Asset.AssetId, second.AssetId }, AssetOwnerRef.Player("p"), 100);
        blocked.Start("blocked-start", "p", assignment.AssignmentId, 100);
        var partialRefused = false; try { blocked.Complete("partial-complete", "p", assignment.AssignmentId, 120, new[] { fixture.Asset.AssetId }); } catch (InvalidOperationException) { partialRefused = true; }
        var pending = blocked.Complete("blocked-complete", "p", assignment.AssignmentId, 120, assignment.AssetIds);
        var pendingCode = pending.ResultCode;
        var cancelled = blocked.Cancel("blocked-cancel", "p", assignment.AssignmentId);
        var clientRefused = false; try { new MissionAssignmentEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerClient, false), new FakeMissionCompletion(WorldOwnershipOutcome.Applied)).Reserve("client-assignment", "p", "client", "job", MissionAssignmentKind.Freight, new[] { fixture.Asset.AssetId }, AssetOwnerRef.Player("p"), 1); } catch (InvalidOperationException) { clientRefused = true; }
        Check(partialRefused && pendingCode == "destination-blocked" && cancelled.State == MissionAssignmentState.Cancelled && fixture.Snapshot.Fleet.All(x => x.OperationalState == FleetOperationalState.Available) && clientRefused,
            "partial or blocked arrival never pays; cancellation releases the whole consist and client mutation is refused");

        var passengerInFreight = OwnedFleet("assignment-wrong-freight", 100, "PassengerCoach", "Wrong Coach");
        var freightInPassenger = OwnedFleet("assignment-wrong-passenger", 100, "FreightWagon", "Wrong Wagon");
        var freightRefused = false; var passengerRefused = false;
        try { new MissionAssignmentEngine(passengerInFreight.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeMissionCompletion(WorldOwnershipOutcome.Applied)).Reserve("wrong-freight", "p", "wrong-freight", "job", MissionAssignmentKind.Freight, new[] { passengerInFreight.Asset.AssetId }, AssetOwnerRef.Player("p"), 1); } catch (InvalidOperationException) { freightRefused = true; }
        try { new MissionAssignmentEngine(freightInPassenger.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeMissionCompletion(WorldOwnershipOutcome.Applied)).Reserve("wrong-passenger", "p", "wrong-passenger", "job", MissionAssignmentKind.Passenger, new[] { freightInPassenger.Asset.AssetId }, AssetOwnerRef.Player("p"), 1); } catch (InvalidOperationException) { passengerRefused = true; }
        Check(freightRefused && passengerRefused, "mission kind rejects an incompatible passenger or freight consist before reservation");
    }

    private static void TestMissionAssignmentRequiresAuthoritativeLifecycle()
    {
        var rejectedFixture = OwnedFleet("assignment-lifecycle-rejected", 100, "Locomotive", "Rejected");
        var rejectedPort = new FakeMissionLifecycle { Reservation = WorldOwnershipOutcome.NotApplied };
        var reservationRejected = false;
        try
        {
            new MissionAssignmentEngine(rejectedFixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), rejectedPort)
                .Reserve("lifecycle-rejected", "p", "assignment-rejected", "job-rejected", MissionAssignmentKind.Freight,
                    new[] { rejectedFixture.Asset.AssetId }, AssetOwnerRef.Player("p"), 100);
        }
        catch (InvalidOperationException) { reservationRejected = true; }
        Check(reservationRejected && rejectedFixture.Snapshot.Assignments.Count == 0 && rejectedFixture.Snapshot.Fleet.Single().OperationalState == FleetOperationalState.Available,
            "mission reservation refuses an external job or consist mismatch without reserving fleet state");

        var fixture = OwnedFleet("assignment-lifecycle", 100, "Locomotive", "Lifecycle");
        var port = new FakeMissionLifecycle
        {
            Reservation = WorldOwnershipOutcome.Applied,
            Start = WorldOwnershipOutcome.NotApplied,
            Completion = WorldOwnershipOutcome.Unknown,
            Cancellation = WorldOwnershipOutcome.NotApplied
        };
        var engine = new MissionAssignmentEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), port);
        var assignment = engine.Reserve("lifecycle-reserve", "p", "assignment-lifecycle", "job-lifecycle", MissionAssignmentKind.Freight,
            new[] { fixture.Asset.AssetId }, AssetOwnerRef.Player("p"), 100);
        var startRejected = false;
        try { engine.Start("lifecycle-start-rejected", "p", assignment.AssignmentId, 100); }
        catch (InvalidOperationException) { startRejected = true; }
        port.Start = WorldOwnershipOutcome.Applied;
        engine.Start("lifecycle-start", "p", assignment.AssignmentId, 100);
        var pending = engine.Complete("lifecycle-complete-pending", "p", assignment.AssignmentId, 120, assignment.AssetIds);
        var completionWasPending = pending.State == MissionAssignmentState.CompletionPending;
        var cancelRejected = false;
        try { engine.Cancel("lifecycle-cancel-rejected", "p", assignment.AssignmentId); }
        catch (InvalidOperationException) { cancelRejected = true; }
        port.Cancellation = WorldOwnershipOutcome.Applied;
        var cancelled = engine.Cancel("lifecycle-cancel", "p", assignment.AssignmentId);
        var expectedGuid = fixture.Snapshot.Assets.Assets.Single(x => x.AssetId == fixture.Asset.AssetId).GameLink.Value;
        Check(startRejected && completionWasPending && cancelRejected && cancelled.State == MissionAssignmentState.Cancelled &&
              port.LastMissionId == "job-lifecycle" && port.LastPersistentCarGuids.SequenceEqual(new[] { expectedGuid }, StringComparer.OrdinalIgnoreCase),
            "mission reserve, start, completion and cancellation require authoritative external lifecycle evidence for the exact persistent consist");
    }

    private static void TestMissionCompletionPendingRetriesSameCommand()
    {
        var fixture = OwnedFleet("assignment-pending-retry", 100, "Locomotive", "Retry Loco");
        var port = new FakeMissionLifecycle { Completion = WorldOwnershipOutcome.Unknown };
        var engine = new MissionAssignmentEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), port);
        var assignment = engine.Reserve("pending-retry-reserve", "p", "assignment-pending-retry", "job-pending-retry", MissionAssignmentKind.Freight,
            new[] { fixture.Asset.AssetId }, AssetOwnerRef.Player("p"), 100);
        engine.Start("pending-retry-start", "p", assignment.AssignmentId, 100);
        var pending = engine.Complete("pending-retry-complete", "p", assignment.AssignmentId, 120, assignment.AssetIds);
        var pendingObserved = pending.State == MissionAssignmentState.CompletionPending;
        port.Completion = WorldOwnershipOutcome.Applied;
        var completed = engine.Complete("pending-retry-complete", "p", assignment.AssignmentId, 120, assignment.AssetIds);
        var replay = engine.Complete("pending-retry-complete", "p", assignment.AssignmentId, 999, assignment.AssetIds);
        Check(pendingObserved && object.ReferenceEquals(completed, replay) && completed.State == MissionAssignmentState.Completed &&
              fixture.Snapshot.Economy.Wallets.Single().Balance == 120 && fixture.Snapshot.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.MissionRevenue) == 1 &&
              fixture.Snapshot.AssignmentCommands.Count(x => x.CommandId == "pending-retry-complete") == 1,
            "a pending authoritative destination observation can complete through the same command exactly once");
    }

    private static void TestMissionAssignmentRefusesExpiredLeaseBeforeReserveOrStart()
    {
        static (AcquisitionFixture Fixture, LeaseEngine Leases) Leased(string checkpoint)
        {
            var fixture = Acquisition(checkpoint, 100, WorldOwnershipOutcome.Applied);
            FleetManagementEngine.EnsureAsset(fixture.Snapshot, fixture.Asset.AssetId, "FreightWagon", fixture.Asset.DefinitionId, "Leased Wagon");
            var leases = new LeaseEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true),
                new FakeReleaseGuard(AssetReleaseStatus.Releasable, "safe"), new FakeWorld(WorldOwnershipOutcome.Applied));
            var offer = leases.CreateOffer(checkpoint + ":lease", new[] { fixture.Asset.AssetId }, 0, 0, 0, 5, 5, null, 1m, 0);
            leases.Accept(checkpoint + ":accept", "p", offer.LeaseId, AssetOwnerRef.Player("p"), AccountRef.Player("p"), offer.Version, 0);
            return (fixture, leases);
        }

        var expiredBeforeReserve = Leased("assignment-expired-before-reserve");
        expiredBeforeReserve.Leases.Advance(new LeaseClockAdvance { CommandId = "expire-before-reserve", SessionOpen = true, ActiveGameplayTicks = 5 });
        var reserveRefused = false;
        try
        {
            new MissionAssignmentEngine(expiredBeforeReserve.Fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeMissionCompletion(WorldOwnershipOutcome.Applied))
                .Reserve("expired-reserve", "p", "expired-assignment", "job-expired", MissionAssignmentKind.Freight,
                    new[] { expiredBeforeReserve.Fixture.Asset.AssetId }, AssetOwnerRef.Player("p"), 10);
        }
        catch (InvalidOperationException) { reserveRefused = true; }

        var expiredBeforeStart = Leased("assignment-expired-before-start");
        var engine = new MissionAssignmentEngine(expiredBeforeStart.Fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeMissionCompletion(WorldOwnershipOutcome.Applied));
        var reserved = engine.Reserve("valid-reserve", "p", "reserved-before-expiry", "job-reserved", MissionAssignmentKind.Freight,
            new[] { expiredBeforeStart.Fixture.Asset.AssetId }, AssetOwnerRef.Player("p"), 10);
        expiredBeforeStart.Leases.Advance(new LeaseClockAdvance { CommandId = "expire-before-start", SessionOpen = true, ActiveGameplayTicks = 5 });
        var startRefused = false;
        try { engine.Start("expired-start", "p", reserved.AssignmentId, 100); }
        catch (InvalidOperationException) { startRefused = true; }

        Check(reserveRefused && startRefused && expiredBeforeReserve.Fixture.Snapshot.Assignments.Count == 0 &&
              reserved.State == MissionAssignmentState.Reserved && expiredBeforeStart.Fixture.Snapshot.Fleet.Single().OperationalState == FleetOperationalState.Reserved,
            "an expired inbound lease cannot authorize a new mission reservation or start, while an already reserved consist remains intact for explicit cancellation");
    }

    private static void TestIndustrialReservationPartialDeliveryAndReload()
    {
        var fixture = Acquisition("industrial-partial", 0, WorldOwnershipOutcome.Applied); ConfigureIndustrial(fixture.Snapshot, 20m, 0m, 10m);
        var engine = new IndustrialEconomyEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeIndustrialExecution(WorldOwnershipOutcome.Applied));
        var contract = engine.CreateOffer("industrial-a", "ORIGIN", "DEST", "Logs", 10m, AccountRef.Player("p"), 100, 20); engine.Accept("industrial-accept", contract.ContractId, contract.Version); engine.Activate("industrial-activate", contract.ContractId);
        engine.RecognizeDelivery("unload-a", contract.ContractId, 4m); var replay = engine.RecognizeDelivery("unload-a", contract.ContractId, 4m);
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), "industrial-partial"); var recovery = new IndustrialEconomyEngine(restored, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeIndustrialExecution(WorldOwnershipOutcome.Applied)); var completed = recovery.RecognizeDelivery("unload-b", contract.ContractId, 10m);
        Check(replay.DeliveredQuantity == 4m && completed.State == IndustrialContractState.Completed && completed.PaidAmount == 120 && restored.Economy.Wallets.Single().Balance == 120 && restored.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.IndustrialRevenue) == 2,
            "partial industrial delivery pays proportionally and reload completes without replaying payment");
        Check(restored.IndustrialStocks.Single(x => x.FacilityId == "ORIGIN").OnHand == 10m && restored.IndustrialStocks.Single(x => x.FacilityId == "DEST" && x.CargoId == "Logs").OnHand == 10m,
            "recognized cargo moves exactly once between reserved source and destination stock");
    }

    private static void TestIndustrialCancellationAndDuplicateUnload()
    {
        var fixture = Acquisition("industrial-cancel", 0, WorldOwnershipOutcome.Applied); ConfigureIndustrial(fixture.Snapshot, 10m, 0m, 20m);
        var engine = new IndustrialEconomyEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeIndustrialExecution(WorldOwnershipOutcome.Applied)); var contract = engine.CreateOffer("industrial-b", "ORIGIN", "DEST", "Logs", 5m, AccountRef.Player("p"), 50, 0);
        var accepted = engine.Accept("industrial-b-accept", contract.ContractId, contract.Version); var acceptedReplay = engine.Accept("industrial-b-accept", contract.ContractId, contract.Version - 1); var cancelled = engine.Cancel("industrial-b-cancel", contract.ContractId); var cancelReplay = engine.Cancel("industrial-b-cancel", contract.ContractId);
        Check(object.ReferenceEquals(accepted, acceptedReplay) && object.ReferenceEquals(cancelled, cancelReplay) && cancelled.State == IndustrialContractState.Cancelled && fixture.Snapshot.IndustrialStocks.All(x => x.ReservedInbound == 0m && x.ReservedOutbound == 0m),
            "accept/cancel retries preserve one reservation and cancellation releases all remaining capacity");
        var blocked = engine.CreateOffer("industrial-saturated", "ORIGIN", "DEST", "Logs", 25m, AccountRef.Player("p"), 1, 0); var refused = false; try { engine.Accept("industrial-saturated-accept", blocked.ContractId, blocked.Version); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "industrial contract cannot reserve unavailable cargo or exceed destination capacity");
    }

    private static void TestIndustrialShortageAndRuntimeGate()
    {
        var fixture = Acquisition("industrial-recipe", 0, WorldOwnershipOutcome.Applied); ConfigureIndustrial(fixture.Snapshot, 4m, 0m, 10m); fixture.Snapshot.IndustrialStocks.Add(new IndustrialStock { FacilityId = "DEST", CargoId = "Lumber", Capacity = 10m }); fixture.Snapshot.IndustrialRecipes.Add(new IndustrialRecipe { RecipeId = "sawmill", FacilityId = "DEST", InputCargoId = "Logs", InputQuantity = 2m, OutputCargoId = "Lumber", OutputQuantity = 1m });
        var engine = new IndustrialEconomyEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeIndustrialExecution(WorldOwnershipOutcome.Applied)); var before = engine.RunRecipe("recipe-before", "sawmill", 10);
        var contract = engine.CreateOffer("industrial-recipe-contract", "ORIGIN", "DEST", "Logs", 4m, AccountRef.Player("p"), 0, 0); engine.Accept("recipe-accept", contract.ContractId, contract.Version); engine.Activate("recipe-activate", contract.ContractId); engine.RecognizeDelivery("recipe-unload", contract.ContractId, 4m); var after = engine.RunRecipe("recipe-after", "sawmill", 10);
        Check(before == 0 && after == 2 && fixture.Snapshot.IndustrialStocks.Single(x => x.CargoId == "Logs" && x.FacilityId == "DEST").OnHand == 0m && fixture.Snapshot.IndustrialStocks.Single(x => x.CargoId == "Lumber").OnHand == 2m,
            "production stops on shortage and resumes only after recognized delivery");
        Check(!IndustrialRuntimeGate.TryEnable("pilot-disabled", new DisabledIndustrialExecutionPort(), new FakeGeneratorControl(true, true)) && !IndustrialRuntimeGate.TryEnable("pilot-no-control", new FakeIndustrialExecution(WorldOwnershipOutcome.Applied), new FakeGeneratorControl(false, true)) && IndustrialRuntimeGate.TryEnable("pilot-ok", new FakeIndustrialExecution(WorldOwnershipOutcome.Applied), new FakeGeneratorControl(true, true)),
            "industrial runtime stays fail-closed until execution and competing-generator suspension are both available");
    }

    private static void TestOutboundLeaseCreditsExactlyOnceAndReturnsAfterReload()
    {
        var fixture = OwnedFleet("outbound-income", 100, "LocoDiesel", "Lease Loco");
        fixture.Snapshot.Fleet.Single().LastKnownLocation = "Harbor";
        var engine = new OutboundLeaseEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "idle"), new DeclaredOffSceneLeaseSimulationPort());
        var contract = engine.Publish("outbound-publish", "p", "outbound-a", new[] { fixture.Asset.AssetId }, 50, 10, 30, 20, 1m, "external-market", "Depot");
        engine.Activate("outbound-activate", "p", contract.ContractId);
        fixture.Snapshot.LeaseClock.ActiveTick = 25;
        var first = engine.ProcessClock("outbound-clock-25"); var replay = engine.ProcessClock("outbound-clock-25");
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), "outbound-income");
        restored.LeaseClock.ActiveTick = 30;
        var recovery = new OutboundLeaseEngine(restored, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Unknown, "physically absent"), new DeclaredOffSceneLeaseSimulationPort());
        var final = recovery.ProcessClock("outbound-clock-30"); var returned = recovery.Return("outbound-return", contract.ContractId, 0.7m, "Depot");
        Check(first == 100 && replay == 100 && final == 50 && restored.Economy.Wallets.Single(x => x.Account.Key == "Player:p").Balance == 210 && restored.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.OutboundLeaseRent) == 3,
            "outbound rent is credited once per due tick across retry and reload");
        Check(returned.State == OutboundLeaseActionState.Succeeded && restored.OutboundLeases.Single().ConditionAtReturn == 0.7m && restored.Fleet.Single().OperationalState == FleetOperationalState.Available && restored.Fleet.Single().LastKnownLocation == "Depot",
            "off-scene outbound lease returns deterministically even while its physical representation is absent");
    }

    private static void TestOutboundLeaseGuardsBundlesAndAuthority()
    {
        var fixture = OwnedFleet("outbound-guards", 100, "SteamLoco", "Lead"); var tender = AddOwnedFleetAsset(fixture, "Tender", "Tender");
        fixture.Snapshot.Assets.Bundles.Add(new AssetBundle { BundleId = Guid.NewGuid().ToString("N"), ComponentAssetIds = new List<string> { fixture.Asset.AssetId, tender.AssetId } });
        var engine = new OutboundLeaseEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "idle"), new DeclaredOffSceneLeaseSimulationPort());
        var incomplete = false; try { engine.Publish("outbound-incomplete", "p", "outbound-incomplete", new[] { fixture.Asset.AssetId }, 1, 1, 2, 0, 1m, "market", "Yard"); } catch (InvalidOperationException) { incomplete = true; }
        var contract = engine.Publish("outbound-bundle", "p", "outbound-bundle", new[] { fixture.Asset.AssetId, tender.AssetId }, 10, 5, 10, 0, 1m, "market", "Yard");
        var duplicate = false; try { engine.Publish("outbound-duplicate", "p", "outbound-duplicate", contract.AssetIds, 10, 5, 10, 0, 1m, "market", "Yard"); } catch (InvalidOperationException) { duplicate = true; }
        var missionBlocked = false; try { new MissionAssignmentEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeMissionCompletion(WorldOwnershipOutcome.Applied)).Reserve("outbound-mission", "p", "outbound-mission", "job", MissionAssignmentKind.Freight, contract.AssetIds, AssetOwnerRef.Player("p"), 10); } catch (InvalidOperationException) { missionBlocked = true; }
        var clientBlocked = false; try { new OutboundLeaseEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerClient, false), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "idle"), new DeclaredOffSceneLeaseSimulationPort()).Publish("outbound-client", "p", "outbound-client", contract.AssetIds, 1, 1, 1, 0, 1m, "market", "Yard"); } catch (InvalidOperationException) { clientBlocked = true; }
        Check(incomplete && duplicate && missionBlocked && clientBlocked && fixture.Snapshot.Fleet.All(x => x.OperationalState == FleetOperationalState.Reserved),
            "complete bundles are reserved atomically and cannot be assigned, double-leased or mutated by a client");
    }

    private static void TestOutboundLeaseRecallRecoveryAndCompanyLiquidationCancellation()
    {
        var fixture = OwnedFleet("outbound-recall", 100, "LocoDiesel", "Recall Loco");
        var port = new SequenceOutboundSimulation(WorldOwnershipOutcome.Applied, WorldOwnershipOutcome.Unknown, WorldOwnershipOutcome.Applied);
        var engine = new OutboundLeaseEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "idle"), port);
        var contract = engine.Publish("recall-publish", "p", "recall", new[] { fixture.Asset.AssetId }, 10, 5, 20, 15, 1m, "market", "Yard"); engine.Activate("recall-activate", "p", contract.ContractId);
        var pending = engine.Recall("recall-command", "p", contract.ContractId, 0.9m, "Yard"); var pendingCode = pending.ResultCode; var recovered = engine.Reconcile("recall-command"); var recoveredAgain = engine.Reconcile("recall-command");
        Check(pendingCode == "outbound-lease-recall-pending" && recovered.State == OutboundLeaseActionState.Succeeded && recoveredAgain.State == OutboundLeaseActionState.Succeeded && fixture.Snapshot.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.OutboundLeaseRecallFee) == 1 && fixture.Snapshot.Economy.Wallets.Single().Balance == 45,
            "early recall recovers an unknown return and charges its frozen fee exactly once");

        var companyFixture = CompanyOwnedFleet("outbound-liquidation", false); var company = companyFixture.Snapshot.Economy.Companies.Single();
        var companyFleet = companyFixture.Snapshot.Fleet.Single(); var companyOwnership = companyFixture.Snapshot.Ownership.Single();
        new FleetManagementEngine(companyFixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true)).Execute(new FleetCommand { CommandId = "company-outbound-clear-operator", RequesterId = "p", AssetId = companyFleet.AssetId, Action = FleetCommandAction.ClearOperator, ExpectedFleetVersion = companyFleet.Version, ExpectedOwnershipVersion = companyOwnership.Version });
        var companyEngine = new OutboundLeaseEngine(companyFixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "idle"), new DeclaredOffSceneLeaseSimulationPort());
        var companyContract = companyEngine.Publish("company-outbound-publish", "p", "company-outbound", new[] { companyFixture.Asset.AssetId }, 10, 5, 20, 0, 1m, "market", "Yard"); companyEngine.Activate("company-outbound-activate", "p", companyContract.ContractId);
        var guard = new FakeReleaseGuard(AssetReleaseStatus.Releasable, "idle"); var cancellation = new OutboundLeaseCompanyContractCancellationPort(companyFixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), guard, new DeclaredOffSceneLeaseSimulationPort());
        var liquidation = new CompanyLiquidationEngine(companyFixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), guard, companyFixture.World, cancellation).Dissolve("outbound-liquidate", "p", company.CompanyId, 0, 0);
        Check(liquidation.State == CompanyLiquidationState.Succeeded && companyFixture.Snapshot.OutboundLeases.Single().State == OutboundLeaseState.Cancelled,
            "company dissolution cancels an outbound lease before selling and distributing the frozen company estate");
    }

    private static void TestPassengerDemandCapacityPunctualityAndSinglePayment()
    {
        var fixture = OwnedFleet("passenger-economy", 100, "PassengerCoach", "Coach");
        var engine = new PassengerEconomyEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeMissionCompletion(WorldOwnershipOutcome.Applied));
        var route = engine.ConfigureRoute("passenger-route", "A-B", "A", "B", 100, 200, 20, 10, 10, 2);
        var contract = engine.OfferAndReserve("passenger-reserve", "p", "service-a", route.RouteId, "passenger-job-a", new[] { fixture.Asset.AssetId }, AssetOwnerRef.Player("p"), 60, 10, 20);
        var reserveReplay = engine.OfferAndReserve("passenger-reserve", "p", "service-a", route.RouteId, "passenger-job-a", new[] { fixture.Asset.AssetId }, AssetOwnerRef.Player("p"), 60, 20, 30);
        engine.Start("passenger-start", "p", contract.ContractId, 60, 10);
        var startReplay = engine.Start("passenger-start", "p", contract.ContractId, 999, 99);
        var completed = engine.Complete("passenger-complete", "p", contract.ContractId, 160, 25, contract.AssetIds); var replay = engine.Complete("passenger-complete", "p", contract.ContractId, 999, 99, contract.AssetIds);
        var assignment = fixture.Snapshot.Assignments.Single(x => x.AssignmentId == contract.AssignmentId); var ledger = fixture.Snapshot.Economy.Ledger.Single(x => x.EntryId == assignment.AssignmentId + ":mission-revenue");
        Check(object.ReferenceEquals(contract, reserveReplay) && object.ReferenceEquals(contract, startReplay) && object.ReferenceEquals(completed, replay) && completed.BookedPassengers == 60 && completed.ObservedVanillaRevenue == 100 && completed.PunctualityPenalty == 10 && completed.PaidRevenue == 90 && ledger.Amount == 90 && fixture.Snapshot.Economy.Wallets.Single().Balance == 150,
            "passenger capacity and punctuality adjust the observed PassengerJobs payout without creating a second payment");
        Check(route.DemandUnits == 40 && route.TransportedPassengers == 60 && route.PunctualityBasisPoints == 5000 && assignment.ExternalSettlement == ExternalSettlementState.Pending && assignment.ExpectedVanillaBalance == 150,
            "passenger demand, frequency metrics and required vanilla settlement remain explicit and persistent");
    }

    private static void TestPassengerCompletionPendingRetriesSameCommand()
    {
        var fixture = OwnedFleet("passenger-pending-retry", 100, "PassengerCoach", "Retry Coach");
        var port = new FakeMissionLifecycle { Completion = WorldOwnershipOutcome.Unknown };
        var engine = new PassengerEconomyEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), port);
        var route = engine.ConfigureRoute("passenger-pending-route", "Retry route", "A", "B", 10, 20, 1, 10, 5, 1);
        var contract = engine.OfferAndReserve("passenger-pending-reserve", "p", "passenger-pending", route.RouteId, "passenger-pending-job",
            new[] { fixture.Asset.AssetId }, AssetOwnerRef.Player("p"), 10, 0, 10);
        engine.Start("passenger-pending-start", "p", contract.ContractId, 100, 0);
        var pending = engine.Complete("passenger-pending-complete", "p", contract.ContractId, 150, 12, contract.AssetIds);
        var pendingObserved = pending.State == PassengerContractState.CompletionPending;
        port.Completion = WorldOwnershipOutcome.Applied;
        var completed = engine.Complete("passenger-pending-complete", "p", contract.ContractId, 150, 12, contract.AssetIds);
        var replay = engine.Complete("passenger-pending-complete", "p", contract.ContractId, 999, 99, contract.AssetIds);
        Check(pendingObserved && object.ReferenceEquals(completed, replay) && completed.State == PassengerContractState.Completed && completed.PaidRevenue == 48 &&
              fixture.Snapshot.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.MissionRevenue) == 1 &&
              fixture.Snapshot.PassengerCommands.Count(x => x.CommandId == "passenger-pending-complete") == 1,
            "a pending passenger arrival can complete through the same command without duplicate revenue or demand updates");
    }

    private static void TestPassengerExactSettlementPreservesUnrelatedWalletChanges()
    {
        var fixture = OwnedFleet("passenger-exact-settlement", 100, "PassengerCoach", "Exact Coach");
        var engine = new PassengerEconomyEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeMissionSettlement(40));
        var route = engine.ConfigureRoute("passenger-exact-route", "A-B-exact", "A", "B", 10, 20, 1, 10, 10, 2);
        var contract = engine.OfferAndReserve("passenger-exact-reserve", "p", "passenger-exact", route.RouteId, "passenger-job-exact", new[] { fixture.Asset.AssetId }, AssetOwnerRef.Player("p"), 10, 0, 10);
        engine.Start("passenger-exact-start", "p", contract.ContractId, 100, 0);
        var completed = engine.Complete("passenger-exact-complete", "p", contract.ContractId, 125, 15, contract.AssetIds);
        var assignment = fixture.Snapshot.Assignments.Single(value => value.AssignmentId == contract.AssignmentId);
        Check(completed.ObservedVanillaRevenue == 40 && completed.PunctualityPenalty == 10 && completed.PaidRevenue == 30 && assignment.ExpectedVanillaBalance == 115 && fixture.Snapshot.Economy.Wallets.Single().Balance == 115,
            "passenger penalty removes only the policy adjustment while preserving unrelated vanilla wallet changes");
    }

    private static void TestPassengerCancellationReloadAndPartialArrival()
    {
        var fixture = OwnedFleet("passenger-cancel", 100, "PassengerCoach", "Coach"); var second = AddOwnedFleetAsset(fixture, "LocoDiesel", "Loco");
        var engine = new PassengerEconomyEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeMissionCompletion(WorldOwnershipOutcome.Applied));
        var route = engine.ConfigureRoute("passenger-route-c", "C-D", "C", "D", 20, 100, 5, 10, 10, 1);
        fixture.Snapshot.LeaseClock.ActiveTick = 20; engine.RefreshDemand("passenger-demand-c", route.RouteId, 20); engine.RefreshDemand("passenger-demand-c", route.RouteId, 200);
        var contract = engine.OfferAndReserve("passenger-reserve-c", "p", "service-c", route.RouteId, "passenger-job-c", new[] { fixture.Asset.AssetId, second.AssetId }, AssetOwnerRef.Player("p"), 10, 20, 30);
        engine.Start("passenger-start-c", "p", contract.ContractId, 60, 20);
        var partialBlocked = false; try { engine.Complete("passenger-partial-c", "p", contract.ContractId, 70, 30, new[] { fixture.Asset.AssetId }); } catch (InvalidOperationException) { partialBlocked = true; }
        var cancelled = engine.Cancel("passenger-cancel-c", "p", contract.ContractId); var replay = engine.Cancel("passenger-cancel-c", "p", contract.ContractId);
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), "passenger-cancel");
        Check(partialBlocked && object.ReferenceEquals(cancelled, replay) && route.DemandUnits == 30 && restored.PassengerContracts.Single().State == PassengerContractState.Cancelled && restored.Fleet.All(x => x.OperationalState == FleetOperationalState.Available),
            "partial passenger arrival cannot pay; cancellation restores demand and survives reload exactly once");
    }

    private static void TestPassengerCompanyRoutingAndClientAuthority()
    {
        var fixture = OwnedFleet("passenger-company", 100, "PassengerCoach", "Coach"); var economy = new CompanyEconomyEngine(fixture.Snapshot.Economy); economy.CreateCompany(Command("passenger-company-create", "p", "co"), "Passenger Co"); var company = fixture.Snapshot.Economy.Companies.Single();
        var fleet = fixture.Snapshot.Fleet.Single(); var ownership = fixture.Snapshot.Ownership.Single(); new FleetManagementEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true)).Execute(new FleetCommand { CommandId = "passenger-company-transfer", RequesterId = "p", AssetId = fleet.AssetId, Action = FleetCommandAction.TransferOwnership, Target = AssetOwnerRef.Company(company.CompanyId), ExpectedFleetVersion = fleet.Version, ExpectedOwnershipVersion = ownership.Version });
        var engine = new PassengerEconomyEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeMissionCompletion(WorldOwnershipOutcome.Applied)); var route = engine.ConfigureRoute("passenger-company-route", "E-F", "E", "F", 10, 20, 1, 10, 10, 0);
        var contract = engine.OfferAndReserve("passenger-company-reserve", "p", "service-company", route.RouteId, "passenger-job-company", new[] { fixture.Asset.AssetId }, AssetOwnerRef.Company(company.CompanyId), 10, 0, 10); engine.Start("passenger-company-start", "p", contract.ContractId, 60, 0); engine.Complete("passenger-company-complete", "p", contract.ContractId, 160, 10, contract.AssetIds);
        var assignment = fixture.Snapshot.Assignments.Single(x => x.AssignmentId == contract.AssignmentId); var clientBlocked = false; try { new PassengerEconomyEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerClient, false), new FakeMissionCompletion(WorldOwnershipOutcome.Applied)).ConfigureRoute("passenger-client", "X-Y", "X", "Y", 1, 1, 0, 1, 1, 0); } catch (InvalidOperationException) { clientBlocked = true; }
        Check(fixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Company:" + company.CompanyId).Balance == 100 && assignment.ExternalSettlement == ExternalSettlementState.Pending && assignment.ExpectedVanillaBalance == 60 && clientBlocked,
            "company passenger revenue is routed once to the company while client-side economy remains refused");
    }

    private static void TestDynamicMarketUsesSupplyDemandUtilizationAndFreezesOffers()
    {
        var fixture = Acquisition("dynamic-market", 1000, WorldOwnershipOutcome.Applied); ConfigureMarket(fixture.Snapshot, fixture.Asset.DefinitionId, "Passenger", 100, 10);
        fixture.Snapshot.Market.Stock.Single().Available = 1;
        fixture.Snapshot.PassengerRoutes.Add(new PassengerRouteDemand { RouteId = "A-B", OriginId = "A", DestinationId = "B", DemandUnits = 90, MaximumDemandUnits = 100, DemandPerInterval = 1, DesiredFrequencyTicks = 10, BaseFarePerPassenger = 1, Version = 1 });
        var engine = new DynamicEconomyEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true)); engine.ConfigurePolicy("dynamic-policy", "Passenger", 0.8m, 1.2m, 0.5m, 0.05m, 0.4m, 0.5m, 0.2m);
        var metric = engine.Recalculate("dynamic-first", 1).Single(); var initialSupply = metric.SupplyRatio; var initialDemand = metric.DemandRatio;
        var market = new FiniteMarketEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), fixture.World, new FakeMarketDelivery(new MarketDeliveryResult { Outcome = WorldOwnershipOutcome.Applied, PersistentCarGuid = Guid.NewGuid().ToString("D") }, new MarketDeliveryResult { Outcome = WorldOwnershipOutcome.Applied }));
        var listing = market.GenerateNewOrder("dynamic-listing", fixture.Asset.DefinitionId, "Harbor"); var frozenPrice = listing.Price; var frozenFactor = listing.MarketFactor;
        fixture.Snapshot.PassengerRoutes.Single().DemandUnits = 0; fixture.Snapshot.Market.Stock.Single().Available = 9; for (var tick = 2; tick <= 20; tick++) engine.Recalculate("dynamic-" + tick, tick);
        Check(initialSupply == 0.1m && initialDemand == 0.9m && frozenFactor > 1m && listing.Price == frozenPrice && listing.MarketFactor == frozenFactor,
            "new offers consume derived supply/demand factors while accepted or existing offers retain frozen terms");
        Check(engine.FactorForDefinition(fixture.Asset.DefinitionId) < frozenFactor, "higher supply and lower passenger demand progressively reduce only future market factors");
    }

    private static void TestDynamicMarketIsBoundedSmoothedAndCannotReroll()
    {
        var fixture = Acquisition("dynamic-bounds", 100, WorldOwnershipOutcome.Applied); ConfigureMarket(fixture.Snapshot, fixture.Asset.DefinitionId, "Locomotive", 100, 1); fixture.Snapshot.Market.Stock.Single().Available = 0;
        var engine = new DynamicEconomyEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true)); engine.ConfigurePolicy("bounds-policy", "Locomotive", 0.9m, 1.1m, 1m, 0.02m, 10m, 10m, 10m);
        var factors = new List<decimal>(); for (var tick = 1; tick <= 100; tick++) factors.Add(engine.Recalculate("bounds-" + tick, tick).Single().SmoothedFactor);
        var rerollBlocked = false; try { engine.Recalculate("bounds-reroll", 100); } catch (InvalidOperationException) { rerollBlocked = true; }
        var clientBlocked = false; try { new DynamicEconomyEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerClient, false)).Recalculate("bounds-client", 101); } catch (InvalidOperationException) { clientBlocked = true; }
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), "dynamic-bounds");
        Check(factors.All(x => x >= 0.9m && x <= 1.1m) && factors.Zip(factors.Skip(1), (a, b) => Math.Abs(b - a)).All(x => x <= 0.02m) && rerollBlocked && clientBlocked,
            "dynamic factors remain bounded and step-limited while same-tick rerolls and client mutations are refused");
        Check(restored.DynamicEconomy.LastCalculatedTick == 100 && restored.DynamicEconomy.Metrics.Single().SmoothedFactor == factors.Last(), "dynamic state and deterministic anti-reroll clock survive reload");
    }

    private static void TestAssetProfitabilitySupportsMultipleFleetStrategies()
    {
        var fixture = OwnedFleet("dynamic-profit", 100, "LocoDiesel", "Service asset"); var lessor = AddOwnedFleetAsset(fixture, "FreightWagon", "Lessor asset");
        fixture.Snapshot.Assignments.Add(new MissionAssignment { AssignmentId = "profit-service", MissionId = "job", Kind = MissionAssignmentKind.Freight, AssetIds = new List<string> { fixture.Asset.AssetId }, Operator = AssetOwnerRef.Player("p"), RequestedBy = "p", MaximumExpectedRevenue = 100, VanillaBalanceBefore = 0, VanillaBalanceAfter = 100, ActualRevenue = 100, ExpectedVanillaBalance = 100, State = MissionAssignmentState.Completed });
        fixture.Snapshot.OutboundLeases.Add(new OutboundLeaseContract { ContractId = "profit-lessor", AssetIds = new List<string> { lessor.AssetId }, Owner = AssetOwnerRef.Player("p"), Beneficiary = AccountRef.Player("p"), RentAmount = 50, RentIntervalTicks = 10, DurationTicks = 10, ConditionAtStart = 1m, State = OutboundLeaseState.Returned, Installments = new List<OutboundLeaseInstallment> { new OutboundLeaseInstallment { DueTick = 10, Amount = 50, Credited = true, LedgerEntryId = "profit-rent" } } });
        fixture.Snapshot.OperatingCosts.Add(new OperatingCostRecord { SessionId = "profit-service-cost", Fingerprint = "service", RequesterId = "p", AssetId = fixture.Asset.AssetId, Payer = AccountRef.Player("p"), MaximumAuthorizedCost = 20, VanillaBalanceBefore = 100, VanillaBalanceAfter = 80, ActualCost = 20, ConditionBefore = 1m, ConditionAfter = 1m, State = OperatingCostState.Settled });
        fixture.Snapshot.OperatingCosts.Add(new OperatingCostRecord { SessionId = "profit-lessor-cost", Fingerprint = "lessor", RequesterId = "p", AssetId = lessor.AssetId, Payer = AccountRef.Player("p"), MaximumAuthorizedCost = 10, VanillaBalanceBefore = 80, VanillaBalanceAfter = 70, ActualCost = 10, ConditionBefore = 1m, ConditionAfter = 1m, State = OperatingCostState.Settled });
        var engine = new DynamicEconomyEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true)); engine.Recalculate("profit-rebuild", 1);
        var service = fixture.Snapshot.DynamicEconomy.Profitability.Single(x => x.AssetId == fixture.Asset.AssetId); var rental = fixture.Snapshot.DynamicEconomy.Profitability.Single(x => x.AssetId == lessor.AssetId);
        Check(service.OperatingRevenue == 100 && service.NetOperatingResult == 80 && rental.OperatingRevenue == 50 && rental.NetOperatingResult == 40 && service.AcquisitionCash > 0,
            "per-asset profitability keeps active service and outbound lessor strategies independently measurable and viable");
    }

    private static void TestDedicatedAuthorityRestartsAndAdvancesWhileEmpty()
    {
        var store = new FakeDedicatedStore(); var auth = new FakeDedicatedAuthenticator(); var host = new DedicatedAuthorityHost(store, auth, RoleDetector(NetworkRole.MultiplayerHost, true));
        host.Start("dedicated-restart", "server-a", null, new DedicatedClockPolicy { RunWhileEmpty = true, MaximumAdvanceTicks = 100 });
        var first = host.Advance("dedicated-empty-clock", 25); var replay = host.Advance("dedicated-empty-clock", 25);
        var restarted = new DedicatedAuthorityHost(store, auth, RoleDetector(NetworkRole.MultiplayerHost, true)); var restored = restarted.Start("dedicated-restart", "server-b", null, new DedicatedClockPolicy { RunWhileEmpty = true, MaximumAdvanceTicks = 100 });
        Check(first == 25 && replay == 25 && restored.LeaseClock.ActiveTick == 25 && restored.DedicatedAuthority.RestartCount == 2 && restored.DedicatedAuthority.ServerInstanceId == "server-b",
            "headless authority advances deterministic clocks without players and restores them after server restart");
        var stopped = restarted.Start("dedicated-stopped", "server-c", null, new DedicatedClockPolicy { RunWhileEmpty = false, MaximumAdvanceTicks = 100 });
        Check(restarted.Advance("dedicated-stopped-clock", 25) == 0 && stopped.Market.ClockTick == 0, "dedicated clock policy explicitly freezes time while empty when configured");
    }

    private static void TestDedicatedAuthorityAuthenticatesLateJoinAndMultipleCompanies()
    {
        var store = new FakeDedicatedStore(); var auth = new FakeDedicatedAuthenticator("p", "m"); var host = new DedicatedAuthorityHost(store, auth, RoleDetector(NetworkRole.MultiplayerHost, true)); host.Start("dedicated-players", "server", null, new DedicatedClockPolicy { RunWhileEmpty = true, MaximumAdvanceTicks = 100 });
        var refused = false; try { host.Connect("bad", "spoof", "invalid"); } catch (InvalidOperationException) { refused = true; }
        host.Connect("peer-p", "p", "credential:p"); host.ExecuteAuthenticated("peer-p", "p", (state, player) => new CompanyEconomyEngine(state.Economy).CreateCompany(Command("dedicated-company-p", player, "co-p"), "Company P")); host.Disconnect("peer-p");
        host.Advance("dedicated-between-joins", 10); host.Connect("peer-m", "m", "credential:m"); host.ExecuteAuthenticated("peer-m", "m", (state, player) => new CompanyEconomyEngine(state.Economy).CreateCompany(Command("dedicated-company-m", player, "co-m"), "Company M"));
        Check(refused && host.Current.Economy.Companies.Count == 2 && host.Current.DedicatedAuthority.KnownPlayerIds.OrderBy(x => x).SequenceEqual(new[] { "m", "p" }) && host.Current.LeaseClock.ActiveTick == 10,
            "dedicated authority authenticates persistent identities, supports late join and keeps multiple company accounts isolated");
    }

    private static void TestDedicatedAuthorityRollsBackFailedCheckpointAndRefusesClient()
    {
        var store = new FakeDedicatedStore(); var auth = new FakeDedicatedAuthenticator("p"); var host = new DedicatedAuthorityHost(store, auth, RoleDetector(NetworkRole.MultiplayerHost, true)); host.Start("dedicated-rollback", "server", null, new DedicatedClockPolicy { RunWhileEmpty = true, MaximumAdvanceTicks = 100 }); host.Connect("peer", "p", "credential:p");
        store.FailNextWrite = true; var failed = false; try { host.ExecuteAuthenticated("peer", "p", (state, player) => new CompanyEconomyEngine(state.Economy).CreateCompany(Command("dedicated-failed", player, "co"), "Must Roll Back")); } catch (InvalidOperationException) { failed = true; }
        var unauthenticated = false; try { host.ExecuteAuthenticated("other", "p", (state, player) => 1); } catch (InvalidOperationException) { unauthenticated = true; }
        var clientRefused = false; try { new DedicatedAuthorityHost(store, auth, RoleDetector(NetworkRole.MultiplayerClient, false)).Start("client", "server", null, new DedicatedClockPolicy()); } catch (InvalidOperationException) { clientRefused = true; }
        Check(failed && host.Current.Economy.Companies.Count == 0 && host.Current.DedicatedAuthority.RollbackCount == 1 && unauthenticated && clientRefused,
            "failed optimistic checkpoint writes roll back all mutations and unauthenticated or client-side authority is refused");
    }

    private static void TestLifecycleCoversVanillaCclFreightPassengerAndBundles()
    {
        var fixture = OwnedFleet("lifecycle-catalog", 100, "LocoDiesel", "Vanilla Loco"); var ccl = AddOwnedFleetAsset(fixture, "CCL.BigBoy", "CCL Loco"); var tender = AddOwnedFleetAsset(fixture, "Tender", "Tender"); var freight = AddOwnedFleetAsset(fixture, "FreightWagon", "Freight"); var passenger = AddOwnedFleetAsset(fixture, "PassengerCoach", "Coach");
        fixture.Snapshot.Assets.Definitions.Single(x => x.DefinitionId == "CCL.BigBoy").Origin = "DVCustomCarLoader"; fixture.Snapshot.Assets.Bundles.Add(new AssetBundle { BundleId = Guid.NewGuid().ToString("N"), ComponentAssetIds = new List<string> { ccl.AssetId, tender.AssetId } });
        var observations = fixture.Snapshot.Assets.Assets.Select((x, i) => new AssetLifecycleObservation { PersistentCarGuid = x.GameLink.Value!, DefinitionId = x.DefinitionId, TrackId = "track-" + i, MapRevision = "map-a", VisibleNumber = "changed-" + i, LiveryId = "skin-" + i }).ToArray(); var tracks = observations.Select(x => x.TrackId!).ToArray(); var definitions = fixture.Snapshot.Assets.Definitions.Select(x => x.DefinitionId).ToArray(); var port = new FakeLifecycleProtection(WorldOwnershipOutcome.Applied);
        var engine = new AssetLifecycleEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), port); var records = engine.Reconcile("lifecycle-all", observations, definitions, "map-a", tracks);
        Check(records.Count == 5 && records.All(x => x.Status == AssetLifecycleStatus.Active) && fixture.Snapshot.Fleet.Any(x => x.Kind == FleetVehicleKind.Locomotive) && fixture.Snapshot.Fleet.Any(x => x.Kind == FleetVehicleKind.FreightWagon) && fixture.Snapshot.Fleet.Any(x => x.Kind == FleetVehicleKind.PassengerCar),
            "lifecycle registry covers vanilla, CCL, freight, passenger and multi-component bundle identities");
        var renamed = observations.Select(x => new AssetLifecycleObservation { PersistentCarGuid = x.PersistentCarGuid, DefinitionId = x.DefinitionId, TrackId = x.TrackId, MapRevision = "map-a", VisibleNumber = "renumbered", LiveryId = "new-skin" }).ToArray(); engine.Reconcile("lifecycle-cosmetics", renamed, definitions, "map-a", tracks);
        Check(fixture.Snapshot.Assets.Bundles.Single().ComponentAssetIds.SequenceEqual(new[] { ccl.AssetId, tender.AssetId }) && fixture.Snapshot.Assets.Assets.All(x => x.GameLink.State == PersistentLinkState.Resolved), "renumbering and livery changes do not alter AssetId, CarGUID or bundle composition");
    }

    private static void TestLifecycleProtectionPolicyScopesOwnedAndContractedCars()
    {
        var fixture = OwnedFleet("lifecycle-policy", 100, "LocoDiesel", "Protected"); var guid = fixture.Asset.GameLink.Value!;
        var owned = AssetLifecycleProtectionPolicy.IsProtected(fixture.Snapshot, guid); fixture.Snapshot.Ownership.Single().Owner = AssetOwnerRef.Merchant("external"); var merchant = AssetLifecycleProtectionPolicy.IsProtected(fixture.Snapshot, guid);
        var assignment = new MissionAssignment { AssignmentId = "lifecycle-contract", MissionId = "job", Kind = MissionAssignmentKind.Freight, AssetIds = new List<string> { fixture.Asset.AssetId }, Operator = AssetOwnerRef.Player("p"), State = MissionAssignmentState.Reserved, Version = 1 }; fixture.Snapshot.Assignments.Add(assignment); var contracted = AssetLifecycleProtectionPolicy.IsProtected(fixture.Snapshot, guid); assignment.State = MissionAssignmentState.Cancelled; var terminal = AssetLifecycleProtectionPolicy.IsProtected(fixture.Snapshot, guid);
        var industrial = new IndustrialContract { ContractId = "industrial-protection", State = IndustrialContractState.Active, AssignedWagons = new List<ContractWagonAssignment> { new ContractWagonAssignment { AssetId = fixture.Asset.AssetId } } }; fixture.Snapshot.IndustrialContracts.Add(industrial); var industrialProtected = AssetLifecycleProtectionPolicy.IsProtected(fixture.Snapshot, guid); industrial.State = IndustrialContractState.Completed;
        var passenger = new PassengerServiceContract { ContractId = "passenger-protection", State = PassengerContractState.Active, AssetIds = new List<string> { fixture.Asset.AssetId } }; fixture.Snapshot.PassengerContracts.Add(passenger); var passengerProtected = AssetLifecycleProtectionPolicy.IsProtected(fixture.Snapshot, guid); passenger.State = PassengerContractState.Cancelled;
        Check(owned && !merchant && contracted && !terminal && industrialProtected && passengerProtected && !AssetLifecycleProtectionPolicy.IsProtected(fixture.Snapshot, guid) && !AssetLifecycleProtectionPolicy.IsProtected(fixture.Snapshot, Guid.NewGuid().ToString("D")), "cleanup policy protects exact owned, lease, assignment, industrial or passenger CarGUIDs and leaves merchant traffic and terminal contracts eligible for cleanup");
    }

    private static void TestLifecycleSuspendsMissingContentAndStaleLocationsWithoutRespawn()
    {
        var fixture = OwnedFleet("lifecycle-suspend", 100, "CCL.Custom", "Custom"); var walletBefore = fixture.Snapshot.Economy.Wallets.Single().Balance; var ownerBefore = fixture.Snapshot.Ownership.Single().Owner.Key; var port = new FakeLifecycleProtection(WorldOwnershipOutcome.Applied); var engine = new AssetLifecycleEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), port);
        var missing = engine.Reconcile("lifecycle-missing", Array.Empty<AssetLifecycleObservation>(), Array.Empty<string>(), "map-a", Array.Empty<string>()).Single(); var missingDetail = missing.Detail;
        var absent = engine.Reconcile("lifecycle-absent", Array.Empty<AssetLifecycleObservation>(), new[] { fixture.Asset.DefinitionId }, "map-a", Array.Empty<string>()).Single(); var absentDetail = absent.Detail;
        var observation = new AssetLifecycleObservation { PersistentCarGuid = fixture.Asset.GameLink.Value!, DefinitionId = fixture.Asset.DefinitionId, TrackId = "obsolete", MapRevision = "map-b" }; var stale = engine.Reconcile("lifecycle-stale", new[] { observation }, new[] { fixture.Asset.DefinitionId }, "map-b", new[] { "valid" }).Single(); var staleDetail = stale.Detail;
        observation.TrackId = "valid"; var recovered = engine.Reconcile("lifecycle-recovered", new[] { observation }, new[] { fixture.Asset.DefinitionId }, "map-b", new[] { "valid" }).Single(); var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), "lifecycle-suspend");
        Check(missingDetail.Contains("no substitution or refund") && absentDetail.Contains("no respawn") && staleDetail.Contains("teleport are forbidden") && recovered.Status == AssetLifecycleStatus.Active && restored.Fleet.Single().OperationalState == FleetOperationalState.Available,
            "missing content, temporary absence and stale map tracks suspend then reconcile without substitution, refund, respawn or teleport");
        Check(restored.Economy.Wallets.Single().Balance == walletBefore && restored.Ownership.Single().Owner.Key == ownerBefore && port.ProtectCalls == 2, "lifecycle recovery preserves owner and wallet and protects only resolved representations");
    }

    private static void TestLifecycleProtectsOwnedAssetsButLeavesExternalTrafficAlone()
    {
        var fixture = OwnedFleet("lifecycle-protection", 100, "LocoDiesel", "Owned"); var traffic = FleetAsset.Create("TrafficLoco", Guid.NewGuid().ToString("D")); traffic.GameLink.State = PersistentLinkState.Resolved; fixture.Snapshot.Assets.Definitions.Add(new AssetDefinition { DefinitionId = traffic.DefinitionId, Origin = "AITraffic" }); fixture.Snapshot.Assets.Assets.Add(traffic); fixture.Snapshot.Ownership.Add(new AssetOwnership { AssetId = traffic.AssetId, Owner = AssetOwnerRef.Merchant("external-traffic") }); FleetManagementEngine.EnsureAsset(fixture.Snapshot, traffic.AssetId, "LocoDiesel", traffic.DefinitionId, "Traffic");
        var observations = fixture.Snapshot.Assets.Assets.Select(x => new AssetLifecycleObservation { PersistentCarGuid = x.GameLink.Value!, DefinitionId = x.DefinitionId, TrackId = "track" }).ToArray(); var port = new FakeLifecycleProtection(WorldOwnershipOutcome.Applied); var engine = new AssetLifecycleEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), port); var records = engine.Reconcile("lifecycle-owned-only", observations, fixture.Snapshot.Assets.Definitions.Select(x => x.DefinitionId).ToArray(), "map", new[] { "track" });
        var clientBlocked = false; try { new AssetLifecycleEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerClient, false), port).Reconcile("lifecycle-client", observations, fixture.Snapshot.Assets.Definitions.Select(x => x.DefinitionId).ToArray(), "map", new[] { "track" }); } catch (InvalidOperationException) { clientBlocked = true; }
        Check(records.Count == 1 && records.Single().AssetId == fixture.Asset.AssetId && !fixture.Snapshot.AssetLifecycle.Records.Any(x => x.AssetId == traffic.AssetId) && port.LastProtectedGuids.SequenceEqual(new[] { fixture.Asset.GameLink.Value! }) && clientBlocked,
            "cleanup protection targets owned/contract assets only, leaving external AI traffic outside BDVM authority");
    }

    private static void TestWorldPopulationPolicyAllowsOnlyAuthorizedSources()
    {
        var policy = WorldPopulationPolicy.StrictDefaults();
        Func<WorldPopulationSource, int, WorldPopulationDecision> decide = (source, count) => WorldPopulationPolicyEngine.Evaluate(policy, new WorldPopulationRequest
        {
            CorrelationId = "population-test-" + source + "-" + count,
            Source = source,
            Origin = "offline-test",
            ExistingPhysicalCount = count
        });
        Check(decide(WorldPopulationSource.PurchasedDelivery, 99).Decision == WorldPopulationDecisionKind.Allow && decide(WorldPopulationSource.StarterDelivery, 99).Decision == WorldPopulationDecisionKind.Allow && decide(WorldPopulationSource.ExternalTraffic, 99).Decision == WorldPopulationDecisionKind.Allow,
            "purchased, starter and external traffic sources remain explicitly allowed");
        Check(decide(WorldPopulationSource.NaturalLocomotive, 0).Decision == WorldPopulationDecisionKind.Deny && decide(WorldPopulationSource.ContractProvidedVehicle, 0).Decision == WorldPopulationDecisionKind.Deny && decide(WorldPopulationSource.UnsupportedTutorial, 0).Decision == WorldPopulationDecisionKind.Deny && decide(WorldPopulationSource.Unknown, 0).Decision == WorldPopulationDecisionKind.Deny,
            "natural, contract-provided, tutorial and unknown spawn sources fail closed");
        Check(decide(WorldPopulationSource.RecoveryRequired, 0).Decision == WorldPopulationDecisionKind.Allow && decide(WorldPopulationSource.RecoveryRequired, 1).ResultCode == "population-maximum-reached",
            "recovery is a separately bounded non-economic exception");
        var serialized = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(WorldPopulationPolicy));
        using (var stream = new System.IO.MemoryStream())
        {
            serialized.WriteObject(stream, policy); stream.Position = 0;
            var restored = (WorldPopulationPolicy)serialized.ReadObject(stream)!;
            WorldPopulationPolicyEngine.Validate(restored);
            Check(restored.SchemaVersion == 1 && restored.Rules.Count == 9, "world population policy is versioned and round-trips without losing source rules");
        }
    }

    private static void TestOfflineRollingStockPopulationMatrix()
    {
        var policy = WorldPopulationPolicy.StrictDefaults();
        WorldPopulationDecision Decide(WorldPopulationSource source, int count = 0) => WorldPopulationPolicyEngine.Evaluate(policy, new WorldPopulationRequest
        {
            CorrelationId = "w037.9:" + source + ":" + count,
            Source = source,
            Origin = "offline-rolling-stock-matrix",
            ExistingPhysicalCount = count
        });

        Check(Decide(WorldPopulationSource.NaturalLocomotive).Decision == WorldPopulationDecisionKind.Deny &&
              Decide(WorldPopulationSource.ContractProvidedVehicle).Decision == WorldPopulationDecisionKind.Deny,
            "zero-wagon policy refuses natural and contract-provided rolling stock");
        Check(Decide(WorldPopulationSource.RecoveryRequired, 0).Decision == WorldPopulationDecisionKind.Allow &&
              Decide(WorldPopulationSource.RecoveryRequired, 1).Decision == WorldPopulationDecisionKind.Deny,
            "minimal recovery reserve allows exactly one non-economic physical exception");
        Check(Decide(WorldPopulationSource.PurchasedDelivery).Decision == WorldPopulationDecisionKind.Allow &&
              Decide(WorldPopulationSource.StarterDelivery).Decision == WorldPopulationDecisionKind.Allow &&
              Decide(WorldPopulationSource.LeasedDelivery).Decision == WorldPopulationDecisionKind.Allow,
            "purchase, starter and lease deliveries are the only economic population sources");

        var tutorial = WorldPopulationCareerPolicy.Evaluate(new WorldPopulationCareerRequest { Kind = WorldPopulationCareerKind.NewCareer, TutorialEnabled = true });
        var cleanCareer = WorldPopulationCareerPolicy.Evaluate(new WorldPopulationCareerRequest { Kind = WorldPopulationCareerKind.NewCareer, TutorialEnabled = false });
        var vanillaSave = WorldPopulationCareerPolicy.Evaluate(new WorldPopulationCareerRequest { Kind = WorldPopulationCareerKind.ExistingSave, HasBdvmCheckpoint = false });
        var bdvmSave = WorldPopulationCareerPolicy.Evaluate(new WorldPopulationCareerRequest { Kind = WorldPopulationCareerKind.ExistingSave, HasBdvmCheckpoint = true });
        Check(!tutorial.AllowActivation && !vanillaSave.AllowActivation && cleanCareer.AllowActivation && bdvmSave.AllowActivation,
            "tutorial and existing non-BDVM saves refuse before strict activation while eligible careers are accepted");

        var fixture = Acquisition("w037.9-population", 500, WorldOwnershipOutcome.Applied);
        ConfigureMarket(fixture.Snapshot, "Caboose", "FreightWagon", 40, 1);
        var market = new FiniteMarketEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeWorld(WorldOwnershipOutcome.Applied), new DisabledMarketDeliveryPort());
        var listing = market.GenerateNewOrder("w037.9-catalog", "Caboose", "Harbor");
        Check(fixture.Snapshot.Assets.Assets.Count == 1 && fixture.Snapshot.InitialDeliveries.Count == 0,
            "virtual catalog publication does not create physical rolling stock or a delivery entitlement");
        var wallet = fixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == "Player:p");
        var purchase = market.Purchase(new MarketPurchaseCommand { CommandId = "w037.9-buy", RequesterId = "p", ListingId = listing.ListingId, Buyer = AssetOwnerRef.Player("p"), Payer = AccountRef.Player("p"), ExpectedListingVersion = listing.Version, ExpectedWalletVersion = wallet.Version, ExpectedPlayerVersion = 0 });
        var purchasedGrant = fixture.Snapshot.InitialDeliveries.Single();
        Check(purchase.State == MarketPurchaseState.Succeeded && purchasedGrant.State == InitialDeliveryState.Available &&
              fixture.Snapshot.Assets.Assets.Single(x => x.AssetId == purchase.AssetId).GameLink.State == PersistentLinkState.TemporarilyAbsent,
            "catalog purchase creates one virtual caboose and one unconsumed delivery entitlement without spawning");
        Check(FleetVehicleClassifier.Classify(null, "Caboose") == FleetVehicleKind.FreightWagon,
            "caboose definitions remain operator freight rolling stock and never a free system vehicle");

        var deliveryGuid = Guid.NewGuid().ToString("D");
        var deliveryPort = new FakeInitialDelivery(
            new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Applied },
            new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Unknown, Detail = "result-lost" },
            new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Applied, PersistentCarGuids = new[] { deliveryGuid } });
        var delivery = new InitialDeliveryEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true), deliveryPort);
        var pending = delivery.Place(new InitialDeliveryCommand { CommandId = "w037.9-place", RequesterId = "p", GrantId = purchasedGrant.GrantId, TargetTrackId = "Harbor-Service", TargetKind = InitialDeliveryTargetKind.ServiceTrack, ExpectedGrantVersion = purchasedGrant.Version });
        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), "w037.9-population");
        var restoredPort = new FakeInitialDelivery(
            new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Applied },
            new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Applied, PersistentCarGuids = new[] { deliveryGuid } },
            new InitialDeliveryPortResult { Outcome = WorldOwnershipOutcome.Applied, PersistentCarGuids = new[] { deliveryGuid } });
        var restoredDelivery = new InitialDeliveryEngine(restored, RoleDetector(NetworkRole.MultiplayerHost, true), restoredPort);
        var completed = restoredDelivery.Reconcile(purchasedGrant.GrantId);
        var replay = restoredDelivery.Reconcile(purchasedGrant.GrantId);
        Check(pending.State == InitialDeliveryState.ReconcileRequired && completed.State == InitialDeliveryState.Delivered && object.ReferenceEquals(completed, replay) &&
              restored.Assets.Assets.Count(x => string.Equals(x.GameLink.Value, deliveryGuid, StringComparison.OrdinalIgnoreCase)) == 1 && restoredPort.PlaceCalls == 0 && restoredPort.InspectCalls == 1,
            "simulated save/reload reconciles a lost delivery result exactly once without a duplicate spawn");

        var starterEngine = new InitialDeliveryEngine(restored, RoleDetector(NetworkRole.MultiplayerHost, true), new DisabledInitialDeliveryPort());
        starterEngine.GrantStarterBundle("w037.9-starter", "p", new[] { "LocoDE2", "FlatbedEmpty", "FlatbedEmpty", "FlatbedEmpty" });
        var starterCount = restored.InitialDeliveries.Count(x => x.SourceCommandId == "w037.9-starter");
        starterEngine.GrantStarterBundle("w037.9-starter", "p", new[] { "LocoDE2", "FlatbedEmpty", "FlatbedEmpty", "FlatbedEmpty" });
        Check(starterCount == 4 && restored.InitialDeliveries.Count(x => x.SourceCommandId == "w037.9-starter") == 4,
            "starter bundle retry preserves one DE2 and three wagon entitlements without duplication");

        var leasedAsset = restored.Assets.Assets.Single(x => x.AssetId == purchase.AssetId);
        var leaseOwner = restored.Ownership.Single(x => x.AssetId == leasedAsset.AssetId);
        leaseOwner.Owner = AssetOwnerRef.Merchant("lessor"); leaseOwner.Version++;
        var lease = new LeaseEngine(restored, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeReleaseGuard(AssetReleaseStatus.Releasable, "ready"), new FakeWorld(WorldOwnershipOutcome.Applied));
        var offer = lease.CreateOffer("w037.9-lease", new[] { leasedAsset.AssetId }, 10, 5, 1, 10, 100, 100, 1m, 10);
        var leaseAction = lease.Accept("w037.9-lease-accept", "p", offer.LeaseId, AssetOwnerRef.Player("p"), AccountRef.Player("p"), offer.Version, restored.Economy.Wallets.Single(x => x.Account.Key == "Player:p").Version);
        Check(leaseAction.State == LeaseActionState.Succeeded && restored.Assets.Assets.Count(x => x.AssetId == leasedAsset.AssetId) == 1,
            "lease delivery changes operator access to existing registered stock without creating a second asset");

        ConfigureIndustrial(restored, 10m, 0m, 10m);
        var assetsBeforeContract = restored.Assets.Assets.Count;
        var contract = new IndustrialEconomyEngine(restored, RoleDetector(NetworkRole.MultiplayerHost, true), new FakeIndustrialExecution(WorldOwnershipOutcome.Applied))
            .CreateOffer("w037.9-contract", "ORIGIN", "DEST", "Logs", 5m, AccountRef.Player("p"), 50, 20);
        Check(contract.AssignedWagons.Count == 0 && contract.Manifests.Count == 0 && restored.Assets.Assets.Count == assetsBeforeContract,
            "transport contract reserves cargo but provides and creates no wagon");
    }

    private static void TestValidationRuntimeSettingsLoads()
    {
        var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime-settings.validation.json");
        var warnings = 0;
        var settings = RuntimeSaveSettings.Load(path, _ => warnings++);
        Check(warnings == 0, "validation runtime settings deserialize without fallback");
        Check(settings.EnableSaveGameDataHook && settings.EnableWalletBridge && settings.StartingPersonalBalance == 125000 && settings.StarterBundleDefinitionIds.Count == 0,
            "validation runtime settings retain enabled runtime hooks and the configured one-time player grant");
        WorldPopulationPolicyEngine.Validate(settings.WorldPopulationPolicy);
        Check(settings.WorldPopulationPolicy.Rules.Any(rule => rule.Source == WorldPopulationSource.PurchasedDelivery && rule.Allow), "validation runtime settings preserve purchased delivery policy");
    }

    private static CareerIdentityMaterial SyntheticCareer(string mode, string difficulty, string started) => new CareerIdentityMaterial { GameMode = mode, StartingDifficulty = difficulty, StartingTimeAndDate = started, Scenario = "synthetic" };

    private static void TestEconomyRefusedForClientAndIndeterminateRole()
    {
        Check(!NetworkAuthorityPolicy.CanExecuteEconomy(new NetworkRoleReport { Role = NetworkRole.MultiplayerClient }, out var clientReason)
            && clientReason.Contains("host-only"), "client economy refusal");
        Check(!NetworkAuthorityPolicy.CanExecuteEconomy(new NetworkRoleReport { Role = NetworkRole.Indeterminate }, out var unknownReason)
            && unknownReason.Contains("indeterminate"), "fail-closed economy refusal");
        Check(NetworkAuthorityPolicy.CanExecuteEconomy(new NetworkRoleReport { Role = NetworkRole.MultiplayerHost, HasAuthority = true }, out _),
            "host economy authorization");
    }

    private static void TestNetworkRoleAdapterUsesInjectedApiState()
    {
        var detector = new NetworkRoleDetector(new FakeApiStateReader(new NetworkApiState
            { ApiAvailable = true, IsConnected = true, IsHost = true }));
        Check(detector.Detect().Role == NetworkRole.MultiplayerHost, "network adapter delegates exact API state to policy");
    }

    private static void TestAuthoritativeExportKeepsDefinitionsSeparateFromInstances()
    {
        var definitions = new FakeDefinitions(); var inventory = new FakeInventory(); var writer = new FakeWriter();
        new DiagnosticService(RoleDetector(NetworkRole.MultiplayerHost, true), definitions, inventory, writer, new FakeTrace()).ExportAuthoritative();
        Check(writer.Snapshot != null && writer.Snapshot.Definitions.Count == 1 && writer.Snapshot.VisibleInventory.Count == 1,
            "definition and instance collections remain separate");
        Check(writer.Snapshot!.MutationPolicy == "read-only" && writer.Snapshot.SchemaVersion == 2 && writer.Snapshot.IsAuthoritative,
            "authoritative schema v2 metadata");
    }

    private static void TestClientAuthoritativeExportIsRefusedBeforeReadersAndWriter()
    {
        var definitions = new FakeDefinitions(); var inventory = new FakeInventory(); var writer = new FakeWriter();
        try { new DiagnosticService(RoleDetector(NetworkRole.MultiplayerClient, false), definitions, inventory, writer, new FakeTrace()).ExportAuthoritative(); }
        catch (InvalidOperationException) { }
        Check(definitions.Calls == 0 && inventory.Calls == 0 && writer.Snapshot == null,
            "client authoritative refusal precedes all reads and writes");
    }

    private static void TestClientVisibilityObservationIsExplicitlyNonAuthoritative()
    {
        var writer = new FakeWriter();
        new DiagnosticService(RoleDetector(NetworkRole.MultiplayerClient, false), new FakeDefinitions(), new FakeInventory(), writer, new FakeTrace())
            .ExportVisibilityObservation();
        Check(writer.Snapshot != null && writer.Snapshot.ReportKind == "visibility-observation" &&
            writer.Snapshot.NetworkRole == "MultiplayerClient" && !writer.Snapshot.IsAuthoritative && writer.Snapshot.Authority == "none",
            "client visibility report cannot claim authority");
    }

    private static void TestCorrelationIsSharedByReadersWriterAndTrace()
    {
        var definitions = new FakeDefinitions(); var inventory = new FakeInventory(); var writer = new FakeWriter(); var trace = new FakeTrace();
        new DiagnosticService(RoleDetector(NetworkRole.SoloHost, true), definitions, inventory, writer, trace).ExportAuthoritative();
        Check(!string.IsNullOrWhiteSpace(writer.Snapshot?.CorrelationId) && definitions.Correlation == writer.Snapshot!.CorrelationId &&
            inventory.Correlation == writer.Snapshot.CorrelationId && trace.LastCorrelation == writer.Snapshot.CorrelationId,
            "one correlation ID spans the export");
    }

    private static void TestAssetIdentitySurvivesReconciliation()
    {
        var carGuid = Guid.NewGuid().ToString("D");
        var asset = FleetAsset.Create("loco.de2", carGuid);
        var originalAssetId = asset.AssetId;
        var reloaded = AssetRegistryJson.Deserialize(AssetRegistryJson.Serialize(Snapshot(asset, "loco.de2")));
        var reloadedAsset = reloaded.Assets[0];
        AssetRegistry.Reconcile(reloaded, new[] { new VisibleVehicleIdentity { CarGuid = carGuid, DefinitionId = "loco.de2" } });
        Check(reloadedAsset.AssetId == originalAssetId && reloadedAsset.GameLink.State == PersistentLinkState.Resolved,
            "AssetId survives exact persistent identity reconciliation");
        Check(reloadedAsset.AssetId != carGuid.Replace("-", "") && AssetRegistry.Validate(reloaded).IsValid,
            "AssetId is independent from CarGUID and snapshot is valid");
    }

    private static void TestInvalidPersistenceDataIsRejected()
    {
        var snapshot = new AssetRegistrySnapshot
        {
            SchemaVersion = 99,
            Definitions = new List<AssetDefinition> { new AssetDefinition { DefinitionId = "loco.de2" } },
            Assets = new List<FleetAsset> { new FleetAsset { AssetId = "visible-number-001", DefinitionId = "missing", GameLink = new PersistentVehicleLink { Value = "not-a-guid" } } }
        };
        var result = AssetRegistry.Validate(snapshot);
        Check(!result.IsValid && result.Errors.Count >= 3, "invalid schema, AssetId, definition and CarGUID are rejected");
    }

    private static void TestDuplicatePersistentIdentityIsAmbiguous()
    {
        var carGuid = Guid.NewGuid().ToString("D");
        var first = FleetAsset.Create("wagon.box", carGuid);
        var second = FleetAsset.Create("wagon.box", carGuid);
        var snapshot = new AssetRegistrySnapshot
        {
            Definitions = new List<AssetDefinition> { new AssetDefinition { DefinitionId = "wagon.box" } },
            Assets = new List<FleetAsset> { first, second }
        };
        Check(!AssetRegistry.Validate(snapshot).IsValid, "duplicate persistent links fail validation");
        AssetRegistry.Reconcile(snapshot, new[] { new VisibleVehicleIdentity { CarGuid = carGuid, DefinitionId = "wagon.box" } });
        Check(first.GameLink.State == PersistentLinkState.Ambiguous && second.GameLink.State == PersistentLinkState.Ambiguous,
            "duplicate economic claims become explicit ambiguity");
    }

    private static void TestTemporarilyAbsentAssetRetainsIdentity()
    {
        var asset = FleetAsset.Create("coach.passenger", Guid.NewGuid().ToString("D"));
        var assetId = asset.AssetId;
        var link = asset.GameLink.Value;
        AssetRegistry.Reconcile(Snapshot(asset, "coach.passenger"), Array.Empty<VisibleVehicleIdentity>());
        Check(asset.GameLink.State == PersistentLinkState.TemporarilyAbsent && asset.AssetId == assetId && asset.GameLink.Value == link,
            "temporarily absent asset retains AssetId and persistent link");
    }

    private static void TestDefinitionConflictIsAmbiguous()
    {
        var carGuid = Guid.NewGuid().ToString("D");
        var asset = FleetAsset.Create("loco.custom", carGuid);
        AssetRegistry.Reconcile(Snapshot(asset, "loco.custom"), new[]
            { new VisibleVehicleIdentity { CarGuid = carGuid, DefinitionId = "loco.other" } });
        Check(asset.GameLink.State == PersistentLinkState.Ambiguous, "definition conflict is not auto-reassociated");
    }

    private static void TestMissingLinkAndDuplicateObservationAreExplicit()
    {
        var missing = new FleetAsset { AssetId = Guid.NewGuid().ToString("N"), DefinitionId = "wagon.flat", GameLink = new PersistentVehicleLink { Value = null } };
        var missingSnapshot = Snapshot(missing, "wagon.flat");
        AssetRegistry.Reconcile(missingSnapshot, Array.Empty<VisibleVehicleIdentity>());
        Check(missing.GameLink.State == PersistentLinkState.Missing, "absent persistent link is explicit");

        var carGuid = Guid.NewGuid().ToString("D");
        var duplicate = FleetAsset.Create("wagon.flat", carGuid);
        AssetRegistry.Reconcile(Snapshot(duplicate, "wagon.flat"), new[]
        {
            new VisibleVehicleIdentity { CarGuid = carGuid, DefinitionId = "wagon.flat" },
            new VisibleVehicleIdentity { CarGuid = carGuid, DefinitionId = "wagon.flat" }
        });
        Check(duplicate.GameLink.State == PersistentLinkState.Ambiguous, "duplicate visible CarGUID is explicit ambiguity");
    }

    private static void TestCompanyCreationMembershipAndDelegation()
    {
        var engine = Economy("membership"); engine.EnsurePlayer("leader", 100); engine.EnsurePlayer("member", 40);
        var created = engine.CreateCompany(Command("create", "leader", "acme"), "Acme Rail");
        var company = engine.State.Companies.Single();
        Check(created.State == CommandState.Succeeded && engine.State.Wallets.Single(w => w.Account.Key == "Company:acme").Balance == 0, "free company creation with zero balance");
        var application = engine.RequestMembership("apply", "member", "acme", MembershipRequestKind.Application);
        Check(application.State == MembershipRequestState.Pending, "default membership uses application approval");
        engine.DecideMembership("leader", "apply", true);
        engine.Delegate("leader", "acme", "member", CompanyPermission.ManageFunds, true);
        Check(engine.State.Players.Single(p => p.PlayerId == "member").CompanyId == "acme" && company.DelegatedPermissions["member"].Contains(CompanyPermission.ManageFunds), "membership and delegated permissions persist");
        engine.TransferLeadership("leader", "acme", "member");
        Check(company.LeaderId == "member", "leadership transfer is controlled");
    }

    private static void TestExplicitAccountsIsolationAndMissionRouting()
    {
        var e = Economy("routing"); e.EnsurePlayer("a", 100); e.EnsurePlayer("b", 100); e.EnsurePlayer("solo", 0);
        e.CreateCompany(Command("ca", "a", "a-co"), "A"); e.CreateCompany(Command("cb", "b", "b-co"), "B");
        var aWallet = e.State.Wallets.Single(w => w.Account.Key == "Player:a"); var cWallet = e.State.Wallets.Single(w => w.Account.Key == "Company:a-co");
        var transfer = Command("fund", "a", "a-co"); transfer.ExpectedVersions[aWallet.Account.Key] = 0; transfer.ExpectedVersions[cWallet.Account.Key] = 0;
        e.Transfer(transfer, aWallet.Account, cWallet.Account, 25, LedgerEntryKind.Contribution);
        e.RouteMissionRevenue(Command("company-income", "a", "a-co"), "a", "a-co", 30);
        e.RouteMissionRevenue(Command("solo-income", "solo"), "solo", null, 12);
        var otherCompanyWallet = e.State.Wallets.Single(w => w.Account.Key == "Company:b-co");
        var crossCompany = Command("cross-company", "a", "b-co");
        crossCompany.ExpectedVersions[aWallet.Account.Key] = aWallet.Version;
        crossCompany.ExpectedVersions[otherCompanyWallet.Account.Key] = otherCompanyWallet.Version;
        var crossResult = e.Transfer(crossCompany, aWallet.Account, otherCompanyWallet.Account, 10, LedgerEntryKind.Contribution);
        Check(aWallet.Balance == 75 && cWallet.Balance == 55 && e.State.Wallets.Single(w => w.Account.Key == "Company:b-co").Balance == 0, "explicit accounts remain isolated");
        Check(e.State.Wallets.Single(w => w.Account.Key == "Player:solo").Balance == 12, "independent mission revenue routes personally");
        Check(crossResult.State == CommandState.Rejected && crossResult.ResultCode == "invalid-contribution-route", "a player cannot contribute to another company's account through an ambiguous route");
    }

    private static void TestConcurrentRetryDebitsExactlyOnce()
    {
        var e = Economy("retry"); e.EnsurePlayer("p", 100); e.CreateCompany(Command("create-r", "p", "co"), "Retry Rail");
        var from = e.State.Wallets.Single(w => w.Account.Key == "Player:p"); var to = e.State.Wallets.Single(w => w.Account.Key == "Company:co");
        var command = Command("same-command", "p", "co"); command.ExpectedVersions[from.Account.Key] = 0; command.ExpectedVersions[to.Account.Key] = 0;
        var results = new CommandRecord[16]; Parallel.For(0, results.Length, i => results[i] = e.Transfer(command, from.Account, to.Account, 10, LedgerEntryKind.Contribution));
        Check(results.All(r => object.ReferenceEquals(r, results[0])) && from.Balance == 90 && to.Balance == 10, "concurrent retry returns one durable result and one debit");
        Check(e.State.Ledger.Count(x => x.CommandId == "same-command") == 1, "idempotent journal contains one ledger entry");
    }

    private static void TestCheckpointPersistenceAndSaveIsolation()
    {
        var e = Economy("save-a"); e.EnsurePlayer("p", 9); e.CreateCompany(Command("persist-create", "p", "persist"), "Persistent");
        var json = CompanyEconomyPersistence.Serialize(e.State);
        var loaded = CompanyEconomyPersistence.Deserialize(json, "save-a");
        Check(loaded.Commands.Single().CommandId == "persist-create" && loaded.Players.Single().CompanyId == "persist", "company, membership and journal survive checkpoint round trip");
        var rejected = false; try { CompanyEconomyPersistence.Deserialize(json, "save-b"); } catch (System.IO.InvalidDataException) { rejected = true; }
        Check(rejected, "checkpoint state cannot leak across saves");
    }

    private static void TestLiquidationPricingDebtAndFrozenDistribution()
    {
        var e = Economy("liquidate"); e.EnsurePlayer("lead", 0); e.EnsurePlayer("m", 0); e.CreateCompany(Command("lc", "lead", "liq"), "Liquidators");
        e.RequestMembership("invite", "m", "liq", MembershipRequestKind.Invitation); e.DecideMembership("lead", "invite", true);
        var company = e.State.Companies.Single(); var cmd = Command("dissolve", "lead", "liq"); cmd.ExpectedVersions["company:liq"] = company.Version;
        var result = e.Dissolve(cmd, new[] {
            new LiquidationAsset { AssetId = "perfect", Condition = 1m, DynamicMarketValue = 100, ConfiguredModelValue = 999 },
            new LiquidationAsset { AssetId = "wreck", Condition = 0m, DynamicMarketValue = null, ConfiguredModelValue = 100 }
        }, 20, 5);
        Check(result.State == CommandState.Succeeded && !e.State.Companies.Any(), "dissolution closes company atomically");
        Check(e.State.LiquidationSales.Single(x => x.AssetId == "perfect").Proceeds == 50 && e.State.LiquidationSales.Single(x => x.AssetId == "wreck").Proceeds == 15, "liquidation rate is linear from 15 to 50 percent and records source");
        Check(e.State.Wallets.Where(w => w.Account.Kind == AccountKind.Player).Sum(w => w.Balance) == 40 && e.State.History.Any(h => h.Kind == "company-dissolved" && h.ActorIds.Count == 2), "debts and penalties precede equal frozen-beneficiary distribution");
    }

    private static void TestAntiAbuseHistoryAndLicenseEconomicPolicyPersist()
    {
        var e = Economy("history"); e.EnsurePlayer("p", 0); e.CreateCompany(Command("hc", "p", "old"), "Old Rail");
        e.State.LicenseRules.Add(new LicenseEconomicRule { StableLicenseId = "license:freight", Mechanism = LicenseEconomicMechanism.Guarantee, Amount = 100, VanillaProjectionRequired = true, BlocksGameplayWhenAbsent = false });
        var c = e.State.Companies.Single(); var d = Command("hd", "p", "old"); d.ExpectedVersions["company:old"] = c.Version; e.Dissolve(d, Array.Empty<LiquidationAsset>(), 50, 0);
        var retryCreation = e.CreateCompany(Command("recreate", "p", "old"), "New Name");
        var roundtrip = CompanyEconomyPersistence.Deserialize(CompanyEconomyPersistence.Serialize(e.State), "history");
        Check(retryCreation.State == CommandState.Rejected && roundtrip.History.Any(h => h.CompanyId == "old"), "durable history prevents company identity recreation abuse");
        Check(roundtrip.LicenseRules.Single().Mechanism == LicenseEconomicMechanism.Guarantee && !roundtrip.LicenseRules.Single().BlocksGameplayWhenAbsent, "license is modeled economically without primary hard gate");
        for (var i = 0; i < roundtrip.Guardrails.RecreationCooldownEvents; i++) roundtrip.History.Add(new EconomicHistoryRecord { EventId = "cooldown-" + i, Kind = "economic-observation" });
        var afterCooldown = new CompanyEconomyEngine(roundtrip).CreateCompany(Command("recreate-after-cooldown", "p", "new"), "New Name");
        Check(afterCooldown.State == CommandState.Succeeded, "insolvent dissolution cooldown is bounded and does not permanently deadlock the player");
    }

    private static void TestControlledLeaveAndCircularTransferGuard()
    {
        var e = Economy("guards"); e.EnsurePlayer("lead", 20); e.EnsurePlayer("member", 20); e.CreateCompany(Command("gc", "lead", "guard"), "Guard");
        e.RequestMembership("gi", "member", "guard", MembershipRequestKind.Invitation); e.DecideMembership("lead", "gi", true);
        var member = e.State.Players.Single(p => p.PlayerId == "member"); member.ActiveOperation = true;
        var refused = false; try { e.LeaveCompany("member"); } catch (InvalidOperationException) { refused = true; }
        member.ActiveOperation = false;
        var playerWallet = e.State.Wallets.Single(w => w.Account.Key == "Player:lead"); var companyWallet = e.State.Wallets.Single(w => w.Account.Key == "Company:guard");
        var outCmd = Command("out", "lead", "guard"); outCmd.ExpectedVersions[playerWallet.Account.Key] = 0; outCmd.ExpectedVersions[companyWallet.Account.Key] = 0;
        e.Transfer(outCmd, playerWallet.Account, companyWallet.Account, 10, LedgerEntryKind.Contribution);
        var back = Command("back", "lead", "guard"); back.ExpectedVersions[playerWallet.Account.Key] = 1; back.ExpectedVersions[companyWallet.Account.Key] = 1;
        var circular = e.Transfer(back, companyWallet.Account, playerWallet.Account, 5, LedgerEntryKind.Reimbursement);
        Check(refused && circular.ResultCode == "circular-transfer-blocked" && companyWallet.Balance == 10, "active-operation leave and circular transfer are blocked");
    }

    private static void TestFailedCommandRollsBackAndRetriesSameResult()
    {
        var e = Economy("rollback"); e.EnsurePlayer("p", 10); e.CreateCompany(Command("rc", "p", "r"), "Rollback");
        var from = e.State.Wallets.Single(w => w.Account.Key == "Player:p"); var to = e.State.Wallets.Single(w => w.Account.Key == "Company:r");
        var bad = Command("bad-version", "p", "r"); bad.ExpectedVersions[from.Account.Key] = 0; bad.ExpectedVersions[to.Account.Key] = 99;
        var first = e.Transfer(bad, from.Account, to.Account, 5, LedgerEntryKind.Contribution);
        var second = e.Transfer(bad, from.Account, to.Account, 5, LedgerEntryKind.Contribution);
        Check(first.State == CommandState.Rejected && second.ResultCode == first.ResultCode && from.Balance == 10 && to.Balance == 0 && !e.State.Ledger.Any(x => x.CommandId == "bad-version"), "failed command rolls back before durable retry result");
    }

    private static void TestExplicitResolvedSelectionAndPlayerPurchase()
    {
        var fixture = Acquisition("player-buy", 100, WorldOwnershipOutcome.Applied);
        Check(fixture.Engine.SelectableOffers().Single().AssetId == fixture.Asset.AssetId, "only an explicitly resolved merchant asset is selectable");
        var command = Buy("buy-player", "p", fixture, AssetOwnerRef.Player("p"), AccountRef.Player("p"));
        var result = fixture.Engine.Acquire(command);
        Check(result.State == AcquisitionState.Succeeded && fixture.Snapshot.Ownership.Single().Owner.Key == "Player:p", "player acquisition transfers explicit ownership");
        Check(fixture.Snapshot.Economy.Wallets.Single(w => w.Account.Key == "Player:p").Balance == 60, "player acquisition debits selected personal account");
    }

    private static void TestCompanyPurchaseRequiresMembershipPermissionAndVersions()
    {
        var fixture = Acquisition("company-buy", 100, WorldOwnershipOutcome.Applied);
        var economy = new CompanyEconomyEngine(fixture.Snapshot.Economy); economy.EnsurePlayer("leader", 50); economy.CreateCompany(Command("make-company", "leader", "co"), "Company");
        var personal = fixture.Snapshot.Economy.Wallets.Single(w => w.Account.Key == "Player:leader"); var company = fixture.Snapshot.Economy.Wallets.Single(w => w.Account.Key == "Company:co");
        var funding = Command("fund-company", "leader", "co"); funding.ExpectedVersions[personal.Account.Key] = personal.Version; funding.ExpectedVersions[company.Account.Key] = company.Version; economy.Transfer(funding, personal.Account, company.Account, 50, LedgerEntryKind.Contribution);
        var invalid = Buy("bad-company", "p", fixture, AssetOwnerRef.Company("co"), AccountRef.Company("co"));
        Check(fixture.Engine.Acquire(invalid).ResultCode == "invalid-company-relation", "non-member cannot buy for a company");
        var cmd = Buy("good-company", "leader", fixture, AssetOwnerRef.Company("co"), AccountRef.Company("co"));
        cmd.ExpectedVersions["player:leader"] = fixture.Snapshot.Economy.Players.Single(x => x.PlayerId == "leader").Version;
        cmd.ExpectedVersions["company:co"] = fixture.Snapshot.Economy.Companies.Single().Version;
        cmd.ExpectedVersions["Company:co"] = company.Version;
        Check(fixture.Engine.Acquire(cmd).State == AcquisitionState.Succeeded && company.Balance == 10, "authorized company purchase debits company account");
    }

    private static void TestConcurrentPlayersAndCompaniesProduceOneWinner()
    {
        var fixture = Acquisition("competition", 100, WorldOwnershipOutcome.Applied);
        var economy = new CompanyEconomyEngine(fixture.Snapshot.Economy); economy.EnsurePlayer("leader", 100); economy.CreateCompany(Command("cc", "leader", "co"), "Concurrent");
        var source = fixture.Snapshot.Economy.Wallets.Single(w => w.Account.Key == "Player:leader"); var target = fixture.Snapshot.Economy.Wallets.Single(w => w.Account.Key == "Company:co");
        var funding = Command("cf", "leader", "co"); funding.ExpectedVersions[source.Account.Key] = 0; funding.ExpectedVersions[target.Account.Key] = 0; economy.Transfer(funding, source.Account, target.Account, 60, LedgerEntryKind.Contribution);
        var player = Buy("race-player", "p", fixture, AssetOwnerRef.Player("p"), AccountRef.Player("p"));
        var company = Buy("race-company", "leader", fixture, AssetOwnerRef.Company("co"), AccountRef.Company("co"));
        company.ExpectedVersions["player:leader"] = fixture.Snapshot.Economy.Players.Single(x => x.PlayerId == "leader").Version; company.ExpectedVersions["company:co"] = fixture.Snapshot.Economy.Companies.Single().Version; company.ExpectedVersions["Company:co"] = target.Version;
        var results = new AcquisitionRecord[2]; Parallel.Invoke(() => results[0] = fixture.Engine.Acquire(player), () => results[1] = fixture.Engine.Acquire(company));
        Check(results.Count(x => x.State == AcquisitionState.Succeeded) == 1 && fixture.Snapshot.Economy.Ledger.Count(x => x.Kind == LedgerEntryKind.VehiclePurchase) == 1, "player/company race has exactly one winner and debit");
        Check(fixture.World.ApplyCalls == 1 && fixture.Snapshot.Ownership.Single().Owner.Kind != AssetOwnerKind.Merchant, "player/company race has one world transfer and one owner");
    }

    private static void TestRetryReturnsOneDebitAndOneOwnershipTransfer()
    {
        var fixture = Acquisition("retry-buy", 100, WorldOwnershipOutcome.Applied); var command = Buy("same-buy", "p", fixture, AssetOwnerRef.Player("p"), AccountRef.Player("p"));
        var results = new AcquisitionRecord[16]; Parallel.For(0, results.Length, i => results[i] = fixture.Engine.Acquire(command));
        Check(results.All(x => object.ReferenceEquals(x, results[0])) && fixture.World.ApplyCalls == 1, "concurrent acquisition retry returns one durable result");
        Check(fixture.Snapshot.Economy.Ledger.Count(x => x.CommandId == "same-buy" && x.Kind == LedgerEntryKind.VehiclePurchase) == 1, "acquisition retry creates one purchase ledger entry");
    }

    private static void TestWorldFailureCompensatesOrRemainsReconcileable()
    {
        var notApplied = Acquisition("compensate", 100, WorldOwnershipOutcome.NotApplied); var compensated = notApplied.Engine.Acquire(Buy("comp", "p", notApplied, AssetOwnerRef.Player("p"), AccountRef.Player("p")));
        Check(compensated.State == AcquisitionState.Compensated && notApplied.Snapshot.Economy.Wallets.Single(w => w.Account.Key == "Player:p").Balance == 100 && notApplied.Snapshot.Offers.Single().State == OfferState.Available, "definite world rejection compensates debit and releases offer");
        var unknown = Acquisition("unknown", 100, WorldOwnershipOutcome.Unknown); var pending = unknown.Engine.Acquire(Buy("uncertain", "p", unknown, AssetOwnerRef.Player("p"), AccountRef.Player("p")));
        Check(pending.State == AcquisitionState.ReconcileRequired && unknown.Snapshot.Economy.Wallets.Single(w => w.Account.Key == "Player:p").Balance == 60 && unknown.Snapshot.Offers.Single().State == OfferState.Reserved, "unknown world outcome preserves debit and reservation for reconciliation");
    }

    private static void TestInterruptedPurchasePersistsAndReconciles()
    {
        var fixture = Acquisition("resume", 100, WorldOwnershipOutcome.Applied); fixture.Engine.FailurePoint = AcquisitionFailurePoint.AfterWorldTransfer;
        var interrupted = fixture.Engine.Acquire(Buy("resume-buy", "p", fixture, AssetOwnerRef.Player("p"), AccountRef.Player("p")));
        var json = VehicleAcquisitionPersistence.Serialize(fixture.Snapshot); var loaded = VehicleAcquisitionPersistence.Deserialize(json, "resume");
        var resumedWorld = new FakeWorld(WorldOwnershipOutcome.Applied); var resumed = new VehicleAcquisitionEngine(loaded, resumedWorld, RoleDetector(NetworkRole.MultiplayerHost, true)).Reconcile(interrupted.CommandId);
        Check(resumed.State == AcquisitionState.Succeeded && loaded.Economy.Wallets.Single(w => w.Account.Key == "Player:p").Balance == 60, "persisted interruption reconciles without second debit");
        Check(loaded.Economy.Ledger.Count(x => x.CommandId == "resume-buy" && x.Kind == LedgerEntryKind.VehiclePurchase) == 1, "recovery keeps one purchase entry");
    }

    private static void TestAcquisitionAuditAndDiagnosticAreComplete()
    {
        var fixture = Acquisition("audit", 100, WorldOwnershipOutcome.Applied); var result = fixture.Engine.Acquire(Buy("audit-buy", "p", fixture, AssetOwnerRef.Player("p"), AccountRef.Player("p")));
        var text = fixture.Engine.Diagnostic();
        Check(result.ReferenceValue == 50 && result.ReferenceSource == ReferenceValueSource.ConfiguredModel && result.ObservedCondition == 0.8m && result.AppliedRate == 0.8m, "transaction freezes reference value, source, condition and rate");
        Check(text.Contains("audit-buy") && text.Contains("source=ConfiguredModel") && text.Contains("buyer=Player:p") && text.Contains("payer=Player:p"), "minimal diagnostic exposes auditable acquisition state");
    }

    private static void TestAcquisitionRefusesClientBeforeReservation()
    {
        var fixture = Acquisition("client-refusal", 100, WorldOwnershipOutcome.Applied);
        var client = new VehicleAcquisitionEngine(fixture.Snapshot, fixture.World, RoleDetector(NetworkRole.MultiplayerClient, false));
        var result = client.Acquire(Buy("client-buy", "p", fixture, AssetOwnerRef.Player("p"), AccountRef.Player("p")));
        Check(result.ResultCode == "host-authority-required" && fixture.Snapshot.Offers.Single().State == OfferState.Available && fixture.World.ApplyCalls == 0, "client acquisition is rejected before reservation, debit and world mutation");
    }

    private static void TestEveryInjectedFailureIsCompensatedOrReconciled()
    {
        foreach (var point in new[] { AcquisitionFailurePoint.AfterReservation, AcquisitionFailurePoint.AfterDebit, AcquisitionFailurePoint.BeforeWorldTransfer })
        {
            var fixture = Acquisition("failure-" + point, 100, WorldOwnershipOutcome.NotApplied); fixture.Engine.FailurePoint = point;
            var record = fixture.Engine.Acquire(Buy("fail-" + point, "p", fixture, AssetOwnerRef.Player("p"), AccountRef.Player("p")));
            Check(record.State == AcquisitionState.ReconcileRequired, point + " is durably marked for reconciliation");
            var recovered = fixture.Engine.Reconcile(record.CommandId);
            Check(recovered.State == AcquisitionState.Compensated && fixture.Snapshot.Economy.Wallets.Single(w => w.Account.Key == "Player:p").Balance == 100, point + " reconciles without lost funds");
        }
        var applied = Acquisition("failure-applied", 100, WorldOwnershipOutcome.Applied); applied.Engine.FailurePoint = AcquisitionFailurePoint.AfterWorldTransfer;
        var pending = applied.Engine.Acquire(Buy("fail-applied", "p", applied, AssetOwnerRef.Player("p"), AccountRef.Player("p")));
        var completed = applied.Engine.Reconcile(pending.CommandId);
        Check(completed.State == AcquisitionState.Succeeded && applied.Snapshot.Economy.Wallets.Single(w => w.Account.Key == "Player:p").Balance == 60, "post-world failure reconciles to ownership without second debit");
    }

    private static void TestLicenseStableIdsAndCombinedPersonalQuote()
    {
        var f = LicenseFixture("license-combined", 100);
        f.State.LicenseEconomy.Rules.Add(Rule("rule:freight:1", "dv:job:freight-haul", "job:freight", 1,
            Charge(LicenseChargeKind.PermanentPurchase, 10), Charge(LicenseChargeKind.PerOperationFee, 3),
            Charge(LicenseChargeKind.InsurancePremium, 2), Charge(LicenseChargeKind.RefundableDeposit, 20)));
        var request = LicenseRequest("license-q1", "p", "dv:job:freight-haul", "job:freight", "job-42", AccountRef.Player("p"), 1, 0);
        var quote = f.Engine.Quote(request); var charged = f.Engine.Commit(request.IdempotencyKey);
        Check(quote.RuleId == "rule:freight:1" && quote.TotalDebit == 35 && quote.RefundableDeposit == 20 && quote.Charges.Count == 4, "stable string IDs support combined configured mechanisms");
        Check(charged.Status == LicenseQuoteStatus.Charged && f.Wallet.Balance == 65 && f.State.LicenseEconomy.Entitlements.Single().StableLicenseId == "dv:job:freight-haul", "combined quote charges its explicit personal payer and records permanent entitlement");
        var later = f.Engine.Quote(LicenseRequest("license-q2", "p", "dv:job:freight-haul", "job:freight", "job-43", AccountRef.Player("p"), 1, 1));
        Check(later.TotalDebit == 25 && later.Charges.All(x => x.Kind != LicenseChargeKind.PermanentPurchase), "permanent purchase is omitted from later operation quotes");
    }

    private static void TestLicenseConcurrentRetryDebitsOnce()
    {
        var f = LicenseFixture("license-retry", 100); f.State.LicenseEconomy.Rules.Add(Rule("rule:op", "dv:general:hazmat", "operation:hazmat", 4, Charge(LicenseChargeKind.PerOperationFee, 7)));
        var request = LicenseRequest("same-license-key", "p", "dv:general:hazmat", "operation:hazmat", "op-a", AccountRef.Player("p"), 4, 0);
        var records = new LicenseEconomicRecord[16]; Parallel.For(0, records.Length, i => { f.Engine.Quote(request); records[i] = f.Engine.Commit(request.IdempotencyKey); });
        Check(records.All(x => object.ReferenceEquals(x, records[0])) && f.Wallet.Balance == 93, "concurrent license retry returns one durable result and one debit");
        Check(f.State.Ledger.Count(x => x.EntryId == "same-license-key:license-debit") == 1, "license retry creates one ledger entry");
        var race = LicenseFixture("license-distinct-race", 100); race.State.LicenseEconomy.Rules.Add(Rule("rule:race", "dv:general:race", "operation:race", 1, Charge(LicenseChargeKind.PerOperationFee, 10)));
        var a = LicenseRequest("race-a", "p", "dv:general:race", "operation:race", "a", race.Wallet.Account, 1, 0); var b = LicenseRequest("race-b", "p", "dv:general:race", "operation:race", "b", race.Wallet.Account, 1, 0);
        race.Engine.Quote(a); race.Engine.Quote(b); LicenseEconomicRecord? ra = null; LicenseEconomicRecord? rb = null; Parallel.Invoke(() => ra = race.Engine.Commit(a.IdempotencyKey), () => rb = race.Engine.Commit(b.IdempotencyKey));
        Check(new[] { ra!, rb! }.Count(x => x.Status == LicenseQuoteStatus.Charged) == 1 && race.Wallet.Balance == 90, "distinct concurrent quotes with the same expected wallet version produce one debit");
    }

    private static void TestLicenseCompanyPayerRequiresPermission()
    {
        var f = LicenseFixture("license-company", 0); var economy = new CompanyEconomyEngine(f.State); economy.EnsurePlayer("leader", 20); economy.EnsurePlayer("member", 0); economy.CreateCompany(Command("make-license-company", "leader", "co"), "License Rail");
        economy.RequestMembership("license-invite", "member", "co", MembershipRequestKind.Invitation); economy.DecideMembership("leader", "license-invite", true);
        var companyWallet = f.State.Wallets.Single(x => x.Account.Key == "Company:co"); companyWallet.Balance = 20;
        f.State.LicenseEconomy.Rules.Add(Rule("rule:company", "dv:general:mu", "equipment:mu", 1, Charge(LicenseChargeKind.InsurancePremium, 5)));
        var denied = f.Engine.Quote(LicenseRequest("company-denied", "member", "dv:general:mu", "equipment:mu", "mu-1", companyWallet.Account, 1, companyWallet.Version));
        economy.Delegate("leader", "co", "member", CompanyPermission.ManageFunds, true);
        var acceptedRequest = LicenseRequest("company-accepted", "member", "dv:general:mu", "equipment:mu", "mu-2", companyWallet.Account, 1, companyWallet.Version);
        var accepted = f.Engine.Quote(acceptedRequest); f.Engine.Commit(acceptedRequest.IdempotencyKey);
        Check(denied.Status == LicenseQuoteStatus.Rejected && denied.ResultCode == "manage-funds-permission-required", "company payer refuses a member without ManageFunds");
        Check(accepted.Status == LicenseQuoteStatus.Charged && companyWallet.Balance == 15, "delegated ManageFunds permits the explicit company payer");
    }

    private static void TestLicenseDepositRefundsOnce()
    {
        var f = LicenseFixture("license-refund", 50); f.State.LicenseEconomy.Rules.Add(Rule("rule:deposit", "dv:job:passenger", "job:passenger", 1, Charge(LicenseChargeKind.PerOperationFee, 4), Charge(LicenseChargeKind.RefundableDeposit, 10)));
        var request = LicenseRequest("deposit-charge", "p", "dv:job:passenger", "job:passenger", "passenger-1", f.Wallet.Account, 1, 0); f.Engine.Quote(request); f.Engine.Commit(request.IdempotencyKey);
        var first = f.Engine.RefundDeposit("refund-a", request.IdempotencyKey, "p"); var second = f.Engine.RefundDeposit("refund-b", request.IdempotencyKey, "p");
        Check(first.Status == LicenseQuoteStatus.Refunded && second.Status == LicenseQuoteStatus.Refunded && f.Wallet.Balance == 46, "refundable deposit returns exactly once while the operation fee remains charged");
        Check(f.State.Ledger.Count(x => x.Kind == LedgerEntryKind.LicenseDepositRefund) == 1, "deposit has one refund ledger entry across distinct retries");
    }

    private static void TestLicenseCheckpointRoundTrip()
    {
        var f = LicenseFixture("license-checkpoint", 30); f.State.LicenseEconomy.Rules.Add(Rule("rule:save", "custom:steam:test", "loco:steam", 2, Charge(LicenseChargeKind.PermanentPurchase, 9)));
        var request = LicenseRequest("saved-license", "p", "custom:steam:test", "loco:steam", "loco-use", f.Wallet.Account, 2, 0); f.Engine.Quote(request); f.Engine.Commit(request.IdempotencyKey);
        var restored = CompanyEconomyPersistence.Deserialize(CompanyEconomyPersistence.Serialize(f.State), "license-checkpoint");
        var retryEngine = new LicenseEconomyEngine(restored, RoleDetector(NetworkRole.MultiplayerHost, true)); var retry = retryEngine.Commit(request.IdempotencyKey);
        Check(retry.Status == LicenseQuoteStatus.Charged && restored.Wallets.Single(x => x.Account.Key == "Player:p").Balance == 21, "license journal and entitlement survive checkpoint and retry without another debit");
    }

    private static void TestLicenseQuoteSurvivesRuleModification()
    {
        var f = LicenseFixture("license-rule-change", 100); var rule = Rule("rule:mutable", "dv:job:shunting", "job:shunting", 3, Charge(LicenseChargeKind.PerOperationFee, 8)); f.State.LicenseEconomy.Rules.Add(rule);
        var request = LicenseRequest("frozen-quote", "p", "dv:job:shunting", "job:shunting", "job-old", f.Wallet.Account, 3, 0); var quote = f.Engine.Quote(request);
        rule.Version = 4; rule.Charges[0].Amount = 80; var committed = f.Engine.Commit(request.IdempotencyKey);
        Check(quote.RuleVersion == 3 && committed.TotalDebit == 8 && f.Wallet.Balance == 92, "a committed quote uses its frozen rule version and amounts after configuration changes");
    }

    private static void TestLicenseMissingInvalidAndUnknownCategoryAreExplicitlyUnblocked()
    {
        var f = LicenseFixture("license-unknown", 20); f.State.LicenseEconomy.Rules.Add(Rule("rule:invalid", "dv:general:invalid", "general:invalid", 1, Charge(LicenseChargeKind.InsurancePremium, null)));
        var invalid = f.Engine.Quote(LicenseRequest("invalid-rule", "p", "dv:general:invalid", "general:invalid", "x", f.Wallet.Account, 1, 0));
        var missing = f.Engine.Quote(LicenseRequest("missing-rule", "p", "unknown:license", "unknown:category", "y", f.Wallet.Account, 0, 0));
        var wrongCategory = f.Engine.Quote(LicenseRequest("unknown-category", "p", "dv:general:invalid", "unknown:category", "z", f.Wallet.Account, 0, 0));
        Check(invalid.Status == LicenseQuoteStatus.RuleInvalid && invalid.ResultCode.Contains("access-remains-economically-unblocked"), "invalid rule is visible and does not silently restore a vanilla hard gate");
        Check(missing.Status == LicenseQuoteStatus.RuleMissing && wrongCategory.Status == LicenseQuoteStatus.RuleMissing && f.Wallet.Balance == 20, "unknown license and category are explicit non-charging outcomes");
    }

    private static (CompanyEconomySnapshot State, Wallet Wallet, LicenseEconomyEngine Engine) LicenseFixture(string checkpoint, long balance)
    {
        var state = new CompanyEconomySnapshot { CheckpointId = checkpoint }; var economy = new CompanyEconomyEngine(state); economy.EnsurePlayer("p", balance);
        return (state, state.Wallets.Single(x => x.Account.Key == "Player:p"), new LicenseEconomyEngine(state, RoleDetector(NetworkRole.MultiplayerHost, true)));
    }
    private static LicenseRuleDefinition Rule(string ruleId, string licenseId, string categoryId, long version, params LicenseChargeRule[] charges) => new LicenseRuleDefinition { RuleId = ruleId, StableLicenseId = licenseId, CategoryId = categoryId, Version = version, Charges = charges.ToList() };
    private static LicenseChargeRule Charge(LicenseChargeKind kind, long? amount) => new LicenseChargeRule { Kind = kind, Amount = amount };
    private static LicenseEconomicRequest LicenseRequest(string key, string requester, string licenseId, string categoryId, string operationId, AccountRef payer, long ruleVersion, long walletVersion) => new LicenseEconomicRequest { IdempotencyKey = key, RequesterId = requester, StableLicenseId = licenseId, CategoryId = categoryId, OperationId = operationId, Payer = payer, ExpectedRuleVersion = ruleVersion, ExpectedWalletVersion = walletVersion };

    private static AcquisitionFixture Acquisition(string checkpoint, long balance, WorldOwnershipOutcome worldOutcome)
    {
        var economy = new CompanyEconomySnapshot { CheckpointId = checkpoint }; var ee = new CompanyEconomyEngine(economy); ee.EnsurePlayer("p", balance);
        var guid = Guid.NewGuid().ToString("D"); var asset = FleetAsset.Create("loco.test", guid); asset.GameLink.State = PersistentLinkState.Resolved;
        var snapshot = new VehicleAcquisitionSnapshot { CheckpointId = checkpoint, Economy = economy, Assets = Snapshot(asset, "loco.test"), Ownership = new List<AssetOwnership> { new AssetOwnership { AssetId = asset.AssetId, Owner = AssetOwnerRef.Merchant("market"), Version = 0 } }, Offers = new List<VehicleOffer> { new VehicleOffer { OfferId = "offer", AssetId = asset.AssetId, Price = 40, ReferenceValue = 50, ReferenceSource = ReferenceValueSource.ConfiguredModel, ObservedCondition = 0.8m, AppliedRate = 0.8m, Version = 0 } } };
        var world = new FakeWorld(worldOutcome); return new AcquisitionFixture(snapshot, asset, world, new VehicleAcquisitionEngine(snapshot, world, RoleDetector(NetworkRole.MultiplayerHost, true)));
    }

    private static AcquireVehicleCommand Buy(string id, string requester, AcquisitionFixture f, AssetOwnerRef buyer, AccountRef payer)
    {
        var player = f.Snapshot.Economy.Players.Single(x => x.PlayerId == requester);
        return new AcquireVehicleCommand { CommandId = id, RequesterId = requester, OfferId = "offer", AssetId = f.Asset.AssetId, Buyer = buyer, Payer = payer, ExpectedVersions = new Dictionary<string, long> { ["player:" + requester] = player.Version, [payer.Key] = f.Snapshot.Economy.Wallets.Single(x => x.Account.Key == payer.Key).Version, ["offer:offer"] = f.Snapshot.Offers.Single().Version, ["asset:" + f.Asset.AssetId] = f.Snapshot.Ownership.Single().Version } };
    }

    private static FleetCommand FleetCommand(string id, AcquisitionFixture fixture, FleetCommandAction action)
    {
        var fleet = fixture.Snapshot.Fleet.Single();
        var ownership = fixture.Snapshot.Ownership.Single(x => x.AssetId == fleet.AssetId);
        return new FleetCommand { CommandId = id, RequesterId = "p", AssetId = fleet.AssetId, Action = action, ExpectedFleetVersion = fleet.Version, ExpectedOwnershipVersion = ownership.Version };
    }

    private static AcquisitionFixture OwnedFleet(string checkpoint, long balance, string type, string name)
    {
        var fixture = Acquisition(checkpoint, balance, WorldOwnershipOutcome.Applied);
        fixture.Engine.Acquire(Buy(checkpoint + "-buy", "p", fixture, AssetOwnerRef.Player("p"), AccountRef.Player("p")));
        FleetManagementEngine.EnsureAsset(fixture.Snapshot, fixture.Asset.AssetId, type, fixture.Asset.DefinitionId, name);
        return fixture;
    }

    private static FleetAsset AddOwnedFleetAsset(AcquisitionFixture fixture, string type, string name)
    {
        var asset = FleetAsset.Create(type, Guid.NewGuid().ToString("D"));
        asset.GameLink.State = PersistentLinkState.Resolved;
        if (!fixture.Snapshot.Assets.Definitions.Any(x => x.DefinitionId == type)) fixture.Snapshot.Assets.Definitions.Add(new AssetDefinition { DefinitionId = type, Origin = "test" });
        fixture.Snapshot.Assets.Assets.Add(asset);
        fixture.Snapshot.Ownership.Add(new AssetOwnership { AssetId = asset.AssetId, Owner = AssetOwnerRef.Player("p"), Version = 0 });
        FleetManagementEngine.EnsureAsset(fixture.Snapshot, asset.AssetId, type, type, name);
        fixture.Snapshot.Acquisitions.Add(new AcquisitionRecord { CommandId = "synthetic-acquisition:" + asset.AssetId, RequesterId = "p", AssetId = asset.AssetId, Buyer = AssetOwnerRef.Player("p"), Payer = AccountRef.Player("p"), State = AcquisitionState.Succeeded, ResultCode = "acquired", ReferenceValue = 50, Price = 40, ReferenceSource = ReferenceValueSource.ConfiguredModel, ObservedCondition = 0.8m, AppliedRate = 0.8m });
        return asset;
    }

    private static AcquisitionFixture CompanyOwnedFleet(string checkpoint, bool addSecondAsset)
    {
        var fixture = OwnedFleet(checkpoint, 100, "LocoDiesel", "Company Loco");
        var economy = new CompanyEconomyEngine(fixture.Snapshot.Economy);
        economy.EnsurePlayer("m", 0); economy.CreateCompany(Command(checkpoint + ":company", "p", "co"), "Company");
        economy.RequestMembership(checkpoint + ":member", "m", "co", MembershipRequestKind.Invitation); economy.DecideMembership("p", checkpoint + ":member", true);
        var company = fixture.Snapshot.Economy.Companies.Single();
        var assets = new List<FleetAsset> { fixture.Asset };
        if (addSecondAsset) assets.Add(AddOwnedFleetAsset(fixture, "Freight", "Company Wagon"));
        foreach (var asset in assets)
        {
            var fleet = fixture.Snapshot.Fleet.Single(x => x.AssetId == asset.AssetId);
            var ownership = fixture.Snapshot.Ownership.Single(x => x.AssetId == asset.AssetId);
            new FleetManagementEngine(fixture.Snapshot, RoleDetector(NetworkRole.MultiplayerHost, true)).Execute(new FleetCommand { CommandId = checkpoint + ":transfer:" + asset.AssetId, RequesterId = "p", AssetId = asset.AssetId, Action = FleetCommandAction.TransferOwnership, Target = AssetOwnerRef.Company(company.CompanyId), ExpectedFleetVersion = fleet.Version, ExpectedOwnershipVersion = ownership.Version });
        }
        return fixture;
    }

    private static void ConfigureMarket(VehicleAcquisitionSnapshot snapshot, string definitionId, string categoryId, long basePrice, int stock)
    {
        snapshot.Market.Catalog.Add(new MarketCatalogEntry { DefinitionId = definitionId, CategoryId = categoryId, BasePrice = basePrice, MinimumMarketFactor = 0.8m, MaximumMarketFactor = 1.2m, MinimumConditionFactor = 0.5m, TransferFee = 10, BuybackRate = 0.5m, QuoteDurationTicks = 10 });
        snapshot.Market.Stock.Add(new MarketStockEntry { LocationId = "Harbor", DefinitionId = definitionId, Available = stock, Capacity = stock });
    }

    private static void ConfigureIndustrial(VehicleAcquisitionSnapshot snapshot, decimal origin, decimal destination, decimal destinationCapacity)
    {
        snapshot.IndustrialStocks.Add(new IndustrialStock { FacilityId = "ORIGIN", CargoId = "Logs", OnHand = origin, Capacity = origin });
        snapshot.IndustrialStocks.Add(new IndustrialStock { FacilityId = "DEST", CargoId = "Logs", OnHand = destination, Capacity = destinationCapacity });
    }

    private static SellVehicleCommand Sell(string id, AcquisitionFixture fixture, VehicleResaleQuote quote)
    {
        var fleet = fixture.Snapshot.Fleet.Single(x => x.AssetId == quote.AssetId);
        var owner = fixture.Snapshot.Ownership.Single(x => x.AssetId == quote.AssetId);
        var wallet = fixture.Snapshot.Economy.Wallets.Single(x => x.Account.Key == quote.Payee.Key);
        return new SellVehicleCommand
        {
            CommandId = id, RequesterId = "p", QuoteId = quote.QuoteId, AssetId = quote.AssetId,
            ExpectedFleetVersion = fleet.Version, ExpectedOwnershipVersion = owner.Version,
            ExpectedQuoteVersion = quote.Version, ExpectedWalletVersion = wallet.Version,
            ExpectedFleetVersions = quote.AssetIds.ToDictionary(x => x, x => fixture.Snapshot.Fleet.Single(f => f.AssetId == x).Version),
            ExpectedOwnershipVersions = quote.AssetIds.ToDictionary(x => x, x => fixture.Snapshot.Ownership.Single(o => o.AssetId == x).Version)
        };
    }

    private sealed class AcquisitionFixture { public VehicleAcquisitionSnapshot Snapshot; public FleetAsset Asset; public FakeWorld World; public VehicleAcquisitionEngine Engine; public AcquisitionFixture(VehicleAcquisitionSnapshot s, FleetAsset a, FakeWorld w, VehicleAcquisitionEngine e) { Snapshot = s; Asset = a; World = w; Engine = e; } }
    private sealed class FakeWorld : IExistingVehicleOwnershipAdapter { private readonly WorldOwnershipOutcome result; public int ApplyCalls; public FakeWorld(WorldOwnershipOutcome result) { this.result = result; } public WorldOwnershipOutcome ApplyOwner(string operationId, string persistentCarGuid, AssetOwnerRef owner) { ApplyCalls++; return result; } public WorldOwnershipOutcome InspectOwner(string persistentCarGuid, AssetOwnerRef owner) => result; }
    private sealed class SequenceWorld : IExistingVehicleOwnershipAdapter
    {
        private readonly Queue<WorldOwnershipOutcome> outcomes;
        public SequenceWorld(params WorldOwnershipOutcome[] outcomes) { this.outcomes = new Queue<WorldOwnershipOutcome>(outcomes); }
        public WorldOwnershipOutcome ApplyOwner(string operationId, string persistentCarGuid, AssetOwnerRef owner) => outcomes.Count == 0 ? WorldOwnershipOutcome.Unknown : outcomes.Dequeue();
        public WorldOwnershipOutcome InspectOwner(string persistentCarGuid, AssetOwnerRef owner) => WorldOwnershipOutcome.Applied;
    }
    private sealed class MemoryCheckpointStore : IWorldCheckpointStore
    {
        public IncrementCheckpointEnvelope? Envelope;
        public void Write(IncrementCheckpointEnvelope envelope) => Envelope = IncrementCheckpointJson.Deserialize(IncrementCheckpointJson.Serialize(envelope));
        public IncrementCheckpointEnvelope? Read() => Envelope;
    }
    private sealed class FakeAtomicSaveNode : IAtomicSaveGameNode
    {
        public string? Value; public bool FailNextReplaceAfterMutation;
        public string? Read() => Value;
        public void Replace(string value) { Value = value; if (FailNextReplaceAfterMutation) { FailNextReplaceAfterMutation = false; throw new System.IO.IOException("synthetic replace failure"); } }
    }
    private sealed class CountingCheckpointSink : IAcquisitionCheckpointSink { public int Calls; public void Save(string checkpointId, VehicleAcquisitionSnapshot snapshot, AcquisitionState stage) => Calls++; }
    private sealed class FakeReleaseGuard : IAssetReleaseGuard
    {
        private readonly AssetReleaseStatus status; private readonly string detail;
        public FakeReleaseGuard(AssetReleaseStatus status, string detail) { this.status = status; this.detail = detail; }
        public AssetReleaseInspection Inspect(string persistentCarGuid) => new AssetReleaseInspection { Status = status, Detail = detail };
    }
    private sealed class PerVehicleReleaseGuard : IAssetReleaseGuard
    {
        private readonly IReadOnlyDictionary<string, AssetReleaseStatus> statuses;
        public PerVehicleReleaseGuard(IReadOnlyDictionary<string, AssetReleaseStatus> statuses) { this.statuses = statuses; }
        public AssetReleaseInspection Inspect(string persistentCarGuid) => new AssetReleaseInspection { Status = statuses[persistentCarGuid], Detail = statuses[persistentCarGuid].ToString() };
    }
    private sealed class FakeContractCancellationPort : ICompanyContractCancellationPort
    {
        private readonly WorldOwnershipOutcome outcome; public int CancelCalls;
        public FakeContractCancellationPort(WorldOwnershipOutcome outcome) { this.outcome = outcome; }
        public WorldOwnershipOutcome Cancel(string operationId, string companyId) { CancelCalls++; return outcome; }
        public WorldOwnershipOutcome Inspect(string companyId) => outcome;
    }
    private sealed class RecordingLiquidationCheckpoint : ICompanyLiquidationCheckpointPort
    {
        private readonly Func<string> serialize;
        private readonly string failPhase;
        public readonly List<string> SuccessfulPhases = new List<string>();
        public string? DurablePayload;
        public RecordingLiquidationCheckpoint(Func<string> serialize, string failPhase) { this.serialize = serialize; this.failPhase = failPhase; }
        public bool TryCheckpoint(CompanyLiquidationRecord record, string phase)
        {
            if (phase == failPhase) return false;
            DurablePayload = serialize(); SuccessfulPhases.Add(phase); return true;
        }
    }
    private sealed class FakeMarketDelivery : IMarketDeliveryPort
    {
        private readonly MarketDeliveryResult delivered; private readonly MarketDeliveryResult inspected;
        public FakeMarketDelivery(MarketDeliveryResult delivered, MarketDeliveryResult inspected) { this.delivered = delivered; this.inspected = inspected; }
        public MarketDeliveryResult Deliver(string operationId, string definitionId, string locationId) => delivered;
        public MarketDeliveryResult Inspect(string operationId, string definitionId, string locationId) => inspected;
    }
    private sealed class SequenceMarketCheckpoint : IMarketPurchaseCheckpointPort
    {
        private readonly Queue<bool> outcomes;
        public SequenceMarketCheckpoint(params bool[] outcomes) => this.outcomes = new Queue<bool>(outcomes);
        public bool TryCheckpoint(MarketPurchaseRecord purchase, string phase) => outcomes.Count == 0 || outcomes.Dequeue();
    }
    private sealed class FakeInitialDelivery : IInitialDeliveryPort
    {
        public int PlaceCalls; public int InspectCalls;
        private readonly InitialDeliveryPortResult preflight; private readonly InitialDeliveryPortResult placed; private readonly InitialDeliveryPortResult inspected;
        public FakeInitialDelivery(InitialDeliveryPortResult preflight, InitialDeliveryPortResult placed, InitialDeliveryPortResult inspected) { this.preflight = preflight; this.placed = placed; this.inspected = inspected; }
        public InitialDeliveryPortResult Preflight(string operationId, string trackId, InitialDeliveryTargetKind targetKind, IReadOnlyList<string> definitionIds) => preflight;
        public InitialDeliveryPortResult Place(string operationId, string trackId, InitialDeliveryTargetKind targetKind, IReadOnlyList<string> definitionIds) { PlaceCalls++; return placed; }
        public InitialDeliveryPortResult Inspect(string operationId, string trackId, InitialDeliveryTargetKind targetKind, IReadOnlyList<string> definitionIds) { InspectCalls++; return inspected; }
    }
    private sealed class SequenceInitialDeliveryCheckpoint : IInitialDeliveryCheckpointPort
    {
        private readonly Queue<bool> outcomes;
        public SequenceInitialDeliveryCheckpoint(params bool[] outcomes) => this.outcomes = new Queue<bool>(outcomes);
        public bool TryCheckpoint(InitialDeliveryGrant grant, string phase) => outcomes.Count == 0 || outcomes.Dequeue();
    }
    private sealed class FakeMissionCompletion : IMissionCompletionPort
    {
        private readonly WorldOwnershipOutcome outcome; public FakeMissionCompletion(WorldOwnershipOutcome outcome) { this.outcome = outcome; }
        public WorldOwnershipOutcome Inspect(string missionId, IReadOnlyList<string> persistentCarGuids) => outcome;
    }
    private sealed class FakeMissionLifecycle : IMissionLifecyclePort
    {
        public WorldOwnershipOutcome Reservation { get; set; } = WorldOwnershipOutcome.Applied;
        public WorldOwnershipOutcome Start { get; set; } = WorldOwnershipOutcome.Applied;
        public WorldOwnershipOutcome Completion { get; set; } = WorldOwnershipOutcome.Applied;
        public WorldOwnershipOutcome Cancellation { get; set; } = WorldOwnershipOutcome.Applied;
        public string LastMissionId { get; private set; } = "";
        public IReadOnlyList<string> LastPersistentCarGuids { get; private set; } = Array.Empty<string>();
        public WorldOwnershipOutcome InspectReservation(string missionId, IReadOnlyList<string> persistentCarGuids) => Capture(missionId, persistentCarGuids, Reservation);
        public WorldOwnershipOutcome InspectStart(string missionId, IReadOnlyList<string> persistentCarGuids) => Capture(missionId, persistentCarGuids, Start);
        public WorldOwnershipOutcome Inspect(string missionId, IReadOnlyList<string> persistentCarGuids) => Capture(missionId, persistentCarGuids, Completion);
        public WorldOwnershipOutcome InspectCancellation(string missionId, IReadOnlyList<string> persistentCarGuids) => Capture(missionId, persistentCarGuids, Cancellation);
        private WorldOwnershipOutcome Capture(string missionId, IReadOnlyList<string> persistentCarGuids, WorldOwnershipOutcome outcome)
        {
            LastMissionId = missionId;
            LastPersistentCarGuids = persistentCarGuids.ToArray();
            return outcome;
        }
    }
    private sealed class FakeMissionSettlement : IMissionSettlementPort
    {
        private readonly long revenue;
        public FakeMissionSettlement(long revenue) => this.revenue = revenue;
        public WorldOwnershipOutcome InspectReservation(string missionId, IReadOnlyList<string> persistentCarGuids) => WorldOwnershipOutcome.Applied;
        public WorldOwnershipOutcome InspectStart(string missionId, IReadOnlyList<string> persistentCarGuids) => WorldOwnershipOutcome.Applied;
        public WorldOwnershipOutcome Inspect(string missionId, IReadOnlyList<string> persistentCarGuids) => WorldOwnershipOutcome.Applied;
        public WorldOwnershipOutcome InspectCancellation(string missionId, IReadOnlyList<string> persistentCarGuids) => WorldOwnershipOutcome.Applied;
        public MissionSettlementObservation InspectSettlement(string missionId, IReadOnlyList<string> persistentCarGuids) => new MissionSettlementObservation { Outcome = WorldOwnershipOutcome.Applied, Revenue = revenue, Detail = "test-exact-job-wage" };
    }
    private sealed class FakeIndustrialExecution : IIndustrialExecutionPort
    {
        private readonly WorldOwnershipOutcome outcome; public FakeIndustrialExecution(WorldOwnershipOutcome outcome) { this.outcome = outcome; }
        public bool Available => true;
        public WorldOwnershipOutcome InspectDelivery(string operationId, string contractId, decimal cumulativeQuantity) => outcome;
    }
    private sealed class SequenceOutboundSimulation : IOutboundLeaseSimulationPort
    {
        private readonly Queue<WorldOwnershipOutcome> outcomes;
        public SequenceOutboundSimulation(params WorldOwnershipOutcome[] outcomes) { this.outcomes = new Queue<WorldOwnershipOutcome>(outcomes); }
        public bool Available => true;
        private WorldOwnershipOutcome Next() => outcomes.Count == 0 ? WorldOwnershipOutcome.Unknown : outcomes.Dequeue();
        public WorldOwnershipOutcome Begin(string operationId, IReadOnlyList<string> persistentCarGuids, string declaredDestination) => Next();
        public WorldOwnershipOutcome InspectBegin(string operationId, IReadOnlyList<string> persistentCarGuids) => Next();
        public WorldOwnershipOutcome Return(string operationId, IReadOnlyList<string> persistentCarGuids, string returnLocation) => Next();
        public WorldOwnershipOutcome InspectReturn(string operationId, IReadOnlyList<string> persistentCarGuids, string returnLocation) => Next();
    }
    private sealed class FakeDedicatedStore : IDedicatedCheckpointStore
    {
        private readonly Dictionary<string, DedicatedCheckpointRecord> records = new Dictionary<string, DedicatedCheckpointRecord>(StringComparer.Ordinal);
        public bool FailNextWrite;
        public DedicatedCheckpointRecord? Read(string checkpointId) => records.TryGetValue(checkpointId, out var value) ? new DedicatedCheckpointRecord { CheckpointId = value.CheckpointId, Revision = value.Revision, Payload = value.Payload } : null;
        public long Write(string checkpointId, long expectedRevision, string payload)
        {
            if (FailNextWrite) { FailNextWrite = false; throw new InvalidOperationException("synthetic dedicated checkpoint failure"); }
            var actual = records.TryGetValue(checkpointId, out var current) ? current.Revision : 0; if (actual != expectedRevision) throw new InvalidOperationException("dedicated checkpoint version conflict");
            var next = checked(actual + 1); records[checkpointId] = new DedicatedCheckpointRecord { CheckpointId = checkpointId, Revision = next, Payload = payload }; return next;
        }
    }
    private sealed class FakeDedicatedAuthenticator : IDedicatedIdentityAuthenticator
    {
        private readonly HashSet<string> players;
        public FakeDedicatedAuthenticator(params string[] players) { this.players = new HashSet<string>(players ?? Array.Empty<string>(), StringComparer.Ordinal); }
        public bool Authenticate(string peerSessionId, string claimedPersistentPlayerId, string credential) => players.Contains(claimedPersistentPlayerId) && credential == "credential:" + claimedPersistentPlayerId;
    }
    private sealed class FakeLifecycleProtection : IAssetLifecycleProtectionPort
    {
        private readonly WorldOwnershipOutcome outcome; public FakeLifecycleProtection(WorldOwnershipOutcome outcome) { this.outcome = outcome; }
        public bool Available => true; public int ProtectCalls; public IReadOnlyList<string> LastProtectedGuids = Array.Empty<string>();
        public WorldOwnershipOutcome Protect(string operationId, IReadOnlyList<string> persistentCarGuids) { ProtectCalls++; LastProtectedGuids = persistentCarGuids.ToArray(); return outcome; }
    }
    private sealed class FakeGeneratorControl : ICompetingGeneratorControl
    {
        private readonly bool result; public FakeGeneratorControl(bool available, bool result) { CanSuspendNewGeneration = available; this.result = result; }
        public bool CanSuspendNewGeneration { get; }
        public bool TrySuspendNewGeneration(string operationId) => result;
    }
    private sealed class FakeTriageLogistics : ITriageLogisticsPort
    {
        private readonly AssetReleaseInspection inspection; private readonly WorldOwnershipOutcome apply; private readonly WorldOwnershipOutcome inspect;
        public FakeTriageLogistics(AssetReleaseInspection inspection, WorldOwnershipOutcome apply, WorldOwnershipOutcome inspect) { this.inspection = inspection; this.apply = apply; this.inspect = inspect; }
        public bool Available => true;
        public AssetReleaseInspection InspectTrack(string trackId) => inspection;
        public WorldOwnershipOutcome Apply(string operationId, IReadOnlyList<string> persistentCarGuids, IReadOnlyList<string> orderedTrackIds) => apply;
        public WorldOwnershipOutcome Inspect(string operationId, IReadOnlyList<string> persistentCarGuids, IReadOnlyList<string> orderedTrackIds) => inspect;
    }
    private sealed class TestGovernanceIntentExecutor : ICompanyIntentExecutor
    {
        private readonly AcquisitionRuntimeStateProvider provider; private readonly INetworkRoleDetector authority; private readonly Action stage;
        public TestGovernanceIntentExecutor(AcquisitionRuntimeStateProvider provider, INetworkRoleDetector authority, Action stage) { this.provider = provider; this.authority = authority; this.stage = stage; }
        public ProtocolResult Execute(PeerContext peer, CompanyProtocolEnvelope envelope, CompanyIntent intent)
        {
            CommandRecord record;
            if (intent.Type == CompanyIntentType.InvitePlayer) record = provider.InvitePlayerFor(envelope.RequestId, peer.AuthenticatedPlayerId, envelope.CompanyId, intent.TargetPlayerId, authority);
            else if (intent.Type == CompanyIntentType.RespondToInvitation) record = provider.RespondToInvitationFor(envelope.RequestId, peer.AuthenticatedPlayerId, intent.MembershipRequestId, intent.Enabled, authority);
            else return new ProtocolResult { RequestId = envelope.RequestId, Status = ProtocolResultStatus.Rejected, Code = "unsupported" };
            stage();
            return new ProtocolResult { RequestId = envelope.RequestId, Status = record.State == CommandState.Succeeded ? ProtocolResultStatus.Succeeded : ProtocolResultStatus.Rejected, Code = record.ResultCode };
        }
    }

    private static CompanyEconomyEngine Economy(string checkpoint) => new CompanyEconomyEngine(new CompanyEconomySnapshot { CheckpointId = checkpoint });
    private static EconomyCommand Command(string id, string requester, string? company = null) => new EconomyCommand { CommandId = id, RequesterId = requester, CompanyId = company };

    private static AssetRegistrySnapshot Snapshot(FleetAsset asset, string definitionId) => new AssetRegistrySnapshot
    {
        Definitions = new List<AssetDefinition> { new AssetDefinition { DefinitionId = definitionId, Origin = "runtime" } },
        Assets = new List<FleetAsset> { asset }
    };

    private static void Role(NetworkApiState state, NetworkRole expectedRole, bool expectedAuthority)
    {
        var result = NetworkAuthorityPolicy.Classify(state);
        Check(result.Role == expectedRole && result.HasAuthority == expectedAuthority, $"role {expectedRole}");
    }

    private static FakeRoleDetector RoleDetector(NetworkRole role, bool authority) => new FakeRoleDetector(new NetworkRoleReport
        { Role = role, HasAuthority = authority, Detail = role.ToString() });

    private static void TestProtocolCodecAndAllIntentTypes()
    {
        var intents = new[]
        {
            new CompanyIntent { Type = CompanyIntentType.CreateCompany, Name = "Rail & Co" },
            new CompanyIntent { Type = CompanyIntentType.ApplyToCompany },
            new CompanyIntent { Type = CompanyIntentType.InvitePlayer, TargetPlayerId = "p2" },
            new CompanyIntent { Type = CompanyIntentType.ChangePermission, TargetPlayerId = "p2", Permission = "ManageFunds", Enabled = true },
            new CompanyIntent { Type = CompanyIntentType.TransferFunds, SourceAccount = "Player:p1", DestinationAccount = "Company:c1", Amount = 25 },
            new CompanyIntent { Type = CompanyIntentType.AcquireVehicle, OfferId = "offer", AssetId = "asset", Acquirer = "Company:c1", Payer = "Company:c1" }
            ,new CompanyIntent { Type = CompanyIntentType.DecideApplication, MembershipRequestId = "membership-1", Enabled = true }
            ,new CompanyIntent { Type = CompanyIntentType.RespondToInvitation, MembershipRequestId = "membership-2", Enabled = false }
            ,new CompanyIntent { Type = CompanyIntentType.ChangeMembershipPolicy, MembershipPolicy = "Open" }
            ,new CompanyIntent { Type = CompanyIntentType.LeaveCompany }
            ,new CompanyIntent { Type = CompanyIntentType.TransferLeadership, TargetPlayerId = "p2" }
            ,new CompanyIntent { Type = CompanyIntentType.DissolveCompany, DebtAmount = 10, PenaltyAmount = 2 }
            ,new CompanyIntent { Type = CompanyIntentType.ModuleOperation, ModuleAction = "industry.manage", ModulePayloadJson = "{\"operation\":\"accept\",\"contractId\":\"contract-1\"}" }
        };
        foreach (var intent in intents)
        {
            var envelope = ProtocolEnvelope("codec-" + (byte)intent.Type, intent);
            var decoded = CompanyProtocolCodec.Decode(CompanyProtocolCodec.Encode(envelope));
            var decodedIntent = CompanyIntentCodec.Decode(decoded.Payload);
            Check(decoded.MessageType == CompanyMessageType.IntentRequest && decoded.RequestId == envelope.RequestId && decoded.ExpectedVersion == 7 && decodedIntent.Type == intent.Type && CompanyIntentValidator.Validate(decodedIntent) == null,
                "protocol round-trip supports intent " + intent.Type);
            if (intent.Type == CompanyIntentType.ModuleOperation)
                Check(decodedIntent.ModuleAction == intent.ModuleAction && decodedIntent.ModulePayloadJson == intent.ModulePayloadJson,
                    "module operation keeps the bounded action and opaque JSON payload intact");
        }

        var missingAction = new CompanyIntent { Type = CompanyIntentType.ModuleOperation, ModulePayloadJson = "{}" };
        var missingPayload = new CompanyIntent { Type = CompanyIntentType.ModuleOperation, ModuleAction = "industry.manage" };
        Check(CompanyIntentValidator.Validate(missingAction) == "invalid-module-action" && CompanyIntentValidator.Validate(missingPayload) == "invalid-module-payload",
            "module operations fail closed unless both bounded fields are present");
    }

    private static void TestProtocolVersionLimitsAndNonFiniteValuesFailClosed()
    {
        var envelope = ProtocolEnvelope("bad-version", new CompanyIntent { Type = CompanyIntentType.ApplyToCompany });
        envelope.ProtocolVersion = 99;
        var versionRefused = false; try { CompanyProtocolCodec.Encode(envelope); } catch (System.IO.InvalidDataException ex) { versionRefused = ex.Message == "unsupported-version"; }
        var tooLarge = ProtocolEnvelope("large", new CompanyIntent { Type = CompanyIntentType.ApplyToCompany });
        tooLarge.Payload = new byte[CompanyProtocolLimits.MaximumPayloadBytes + 1];
        var limitRefused = false; try { CompanyProtocolCodec.Encode(tooLarge); } catch (System.IO.InvalidDataException ex) { limitRefused = ex.Message == "payload-too-large"; }
        var encoded = CompanyProtocolCodec.Encode(ProtocolEnvelope("truncated", new CompanyIntent { Type = CompanyIntentType.ApplyToCompany }));
        var truncatedRefused = false; try { CompanyProtocolCodec.Decode(encoded.Take(encoded.Length - 1).ToArray()); } catch (System.IO.InvalidDataException) { truncatedRefused = true; }
        var invalidAmount = new CompanyIntent { Type = CompanyIntentType.TransferFunds, SourceAccount = "a", DestinationAccount = "b", Amount = double.NaN };
        Check(versionRefused && limitRefused && truncatedRefused && CompanyIntentValidator.Validate(invalidAmount) == "non-finite-value", "unsupported versions, truncated or oversized payloads, and non-finite values fail closed");
    }

    private static void TestProtocolAuthenticatesReadyPeerAndSeparatesMessageKinds()
    {
        var executor = new FakeProtocolExecutor(); var host = new CompanyProtocolHost(executor);
        var valid = ProtocolEnvelope("auth", new CompanyIntent { Type = CompanyIntentType.ApplyToCompany });
        Check(host.Receive(new PeerContext { IsKnown = false }, valid).Code == "unknown-peer", "unknown peers are refused");
        Check(host.Receive(new PeerContext { IsKnown = true, IsReady = false, AuthenticatedPlayerId = "p1" }, valid).Code == "peer-not-ready", "non-ready peers are refused");
        Check(host.Receive(ReadyPeer(), ProtocolEnvelope("spoof", new CompanyIntent { Type = CompanyIntentType.ApplyToCompany }, "p2")).Code == "identity-spoofing", "payload identity cannot impersonate the peer");
        var snapshot = ProtocolEnvelope("snapshot", new CompanyIntent { Type = CompanyIntentType.ApplyToCompany }); snapshot.MessageType = CompanyMessageType.AuthoritativeSnapshot;
        Check(host.Receive(ReadyPeer(), snapshot).Code == "unexpected-message-type", "authoritative snapshots are never accepted as client intents");
        Check(executor.Calls == 0, "peer, readiness, identity and message-kind refusals precede mutation");
    }

    private static void TestAuthenticatedTransportActorRoutingFailsClosed()
    {
        var bindings = new[]
        {
            new AuthenticatedTransportActor { TransportIdentity = "Alice", PlayerId = "mp-alice" },
            new AuthenticatedTransportActor { TransportIdentity = "Bob", PlayerId = "mp-bob" }
        };
        Check(AuthenticatedActorRouting.Resolve("local-save", "local-save", new[] { "local-save", "mp-alice", "mp-bob" }, bindings) == "local-save",
            "the exact authenticated local principal resolves to the host player");
        Check(AuthenticatedActorRouting.Resolve("alice", "local-save", new[] { "local-save", "mp-alice", "mp-bob" }, bindings) == "mp-alice",
            "a connected transport name resolves to its durable Multiplayer principal");
        Check(AuthenticatedActorRouting.Resolve("mp-bob", "local-save", new[] { "local-save", "mp-alice", "mp-bob" }, bindings) == "mp-bob",
            "a Web credential may bind directly to an existing durable principal");
        Check(AuthenticatedActorRouting.Resolve("browser-admin", "local-save", new[] { "local-save" }, Array.Empty<AuthenticatedTransportActor>()) == "local-save",
            "an authorized browser principal resolves to the sole local economic player in a solo career");
        var unknownRefused = false;
        try { AuthenticatedActorRouting.Resolve("Mallory", "local-save", new[] { "local-save", "mp-alice", "mp-bob" }, bindings); }
        catch (UnauthorizedAccessException) { unknownRefused = true; }
        var ambiguousRefused = false;
        try
        {
            AuthenticatedActorRouting.Resolve("alice", "local-save", new[] { "local-save", "mp-alice", "mp-bob" }, new[]
            {
                new AuthenticatedTransportActor { TransportIdentity = "Alice", PlayerId = "mp-alice" },
                new AuthenticatedTransportActor { TransportIdentity = "ALICE", PlayerId = "mp-other" }
            });
        }
        catch (UnauthorizedAccessException) { ambiguousRefused = true; }
        var companyVisibility = new AuthenticatedActorVisibility("mp-alice", "company-a");
        var independentVisibility = new AuthenticatedActorVisibility("mp-bob", null);
        Check(companyVisibility.CanView(AccountRef.Player("mp-alice")) && companyVisibility.CanView(AccountRef.Company("company-a")) &&
              companyVisibility.CanView(AssetOwnerRef.Player("mp-alice")) && companyVisibility.CanView(AssetOwnerRef.Company("company-a")),
            "an authenticated actor can view its personal and current-company private state");
        Check(!companyVisibility.CanView(AccountRef.Player("mp-bob")) && !companyVisibility.CanView(AccountRef.Company("company-b")) &&
              !companyVisibility.CanView(AssetOwnerRef.Player("mp-bob")) && !companyVisibility.CanView(AssetOwnerRef.Company("company-b")) &&
              independentVisibility.CanView(AccountRef.Player("mp-bob")) && !independentVisibility.CanView(AccountRef.Company("company-a")),
            "authenticated visibility fails closed across player and company boundaries");
        Check(unknownRefused && ambiguousRefused, "unknown or ambiguous authenticated transport identities fail closed before mutation");
    }

    private static void TestRuntimeActorAwareOperationsCannotCrossPlayerBoundary()
    {
        var host = RoleDetector(NetworkRole.MultiplayerHost, true);

        var costFixture = OwnedFleet("actor-cost", 100, "LocoDiesel", "Actor Cost");
        var costProvider = new AcquisitionRuntimeStateProvider();
        costProvider.Provide(costFixture.Snapshot.CheckpointId, VehicleAcquisitionPersistence.Serialize(costFixture.Snapshot));
        costProvider.EnsurePersistentPlayer("attacker", 0);
        var ownerBalance = costProvider.Current!.Economy.Wallets.Single(value => value.Account.Key == "Player:p").Balance;
        var cost = costProvider.BeginOperatingCostFor("actor-cost-session", "p", costFixture.Asset.AssetId, MaintenanceAction.Service, false, 10, ownerBalance, 0.7m, null, host);
        var costSpoofRefused = false;
        try { costProvider.CompleteOperatingCostFor("attacker", cost.SessionId, ownerBalance, 0.8m, host); }
        catch (UnauthorizedAccessException) { costSpoofRefused = true; }
        costProvider.CancelOperatingCostFor("p", cost.SessionId, ownerBalance, host);

        var assignmentFixture = OwnedFleet("actor-assignment", 100, "Freight", "Actor Wagon");
        var assignmentProvider = new AcquisitionRuntimeStateProvider();
        assignmentProvider.Provide(assignmentFixture.Snapshot.CheckpointId, VehicleAcquisitionPersistence.Serialize(assignmentFixture.Snapshot));
        assignmentProvider.EnsurePersistentPlayer("attacker", 0);
        var assignment = assignmentProvider.ReserveAssignmentFor("actor-assignment-reserve", "p", "actor-assignment-1", "mission-1", MissionAssignmentKind.Freight,
            new[] { assignmentFixture.Asset.AssetId }, false, 50, host, new ManualMissionCompletionPort());
        var assignmentSpoofRefused = false;
        try { assignmentProvider.CancelAssignmentFor("actor-assignment-cancel-spoof", "attacker", assignment.AssignmentId, host, new ManualMissionCompletionPort()); }
        catch (InvalidOperationException) { assignmentSpoofRefused = true; }
        assignmentProvider.CancelAssignmentFor("actor-assignment-cancel-owner", "p", assignment.AssignmentId, host, new ManualMissionCompletionPort());
        var exactAssignmentPort = new FakeMissionSettlement(20);
        var paidAssignment = assignmentProvider.ReserveAssignmentFor("actor-assignment-paid-reserve", "p", "actor-assignment-2", "mission-2", MissionAssignmentKind.Freight,
            new[] { assignmentFixture.Asset.AssetId }, false, 50, host, exactAssignmentPort);
        assignmentProvider.StartAssignmentFor("actor-assignment-paid-start", "p", paidAssignment.AssignmentId, 60, host, exactAssignmentPort);
        assignmentProvider.CompleteAssignmentFor("actor-assignment-paid-complete", "p", paidAssignment.AssignmentId, 60, paidAssignment.AssetIds, host, exactAssignmentPort, MissionSettlementMode.InternalWalletReceivesRevenue);
        var paidAssignmentWallet = assignmentProvider.Current!.Economy.Wallets.Single(value => value.Account.Key == "Player:p");

        var leaseFixture = OwnedFleet("actor-lease", 100, "Freight", "Lease Wagon");
        leaseFixture.Snapshot.Ownership.Single(value => value.AssetId == leaseFixture.Asset.AssetId).Owner = AssetOwnerRef.Merchant("lessor");
        var leaseProvider = new AcquisitionRuntimeStateProvider();
        leaseProvider.Provide(leaseFixture.Snapshot.CheckpointId, VehicleAcquisitionPersistence.Serialize(leaseFixture.Snapshot));
        leaseProvider.EnsurePersistentPlayer("attacker", 100);
        leaseProvider.CreateLocalLeaseOffer("actor-lease-offer", new[] { leaseFixture.Asset.AssetId }, 5, 0, 1, 10, 100, null, 1m, 10, host,
            new FakeReleaseGuard(AssetReleaseStatus.Releasable, "idle"), new FakeWorld(WorldOwnershipOutcome.Applied));
        leaseProvider.AcceptLeaseFor("actor-lease-accept", "p", "actor-lease-offer", false, host,
            new FakeReleaseGuard(AssetReleaseStatus.Releasable, "idle"), new FakeWorld(WorldOwnershipOutcome.Applied));
        var leaseSpoof = leaseProvider.ReturnLeaseFor("actor-lease-return-spoof", "attacker", "actor-lease-offer", 1m, host,
            new FakeReleaseGuard(AssetReleaseStatus.Releasable, "idle"), new FakeWorld(WorldOwnershipOutcome.Applied));

        var passengerFixture = OwnedFleet("actor-passenger", 100, "PassengerCoach", "Actor Coach");
        var passengerProvider = new AcquisitionRuntimeStateProvider();
        passengerProvider.Provide(passengerFixture.Snapshot.CheckpointId, VehicleAcquisitionPersistence.Serialize(passengerFixture.Snapshot));
        passengerProvider.EnsurePersistentPlayer("attacker", 100);
        var passengerPort = new FakeMissionSettlement(30);
        passengerProvider.ConfigureLocalPassengerRoute("actor-passenger-route", "A-B", "A", "B", 10, 20, 1, 10, 3, 0, host, passengerPort);
        var passenger = passengerProvider.ReservePassengerServiceFor("actor-passenger-reserve", "p", "actor-passenger-1", "A-B", "job-passenger",
            new[] { passengerFixture.Asset.AssetId }, false, 10, 0, 10, host, passengerPort);
        var passengerSpoofRefused = false;
        try { passengerProvider.CancelPassengerServiceFor("actor-passenger-cancel-spoof", "attacker", passenger.ContractId, host, passengerPort); }
        catch (InvalidOperationException) { passengerSpoofRefused = true; }
        passengerProvider.StartPassengerServiceFor("actor-passenger-start", "p", passenger.ContractId, 60, 0, host, passengerPort);
        passengerProvider.CompletePassengerServiceFor("actor-passenger-complete", "p", passenger.ContractId, 60, 10, passenger.AssetIds, host, passengerPort, MissionSettlementMode.InternalWalletReceivesRevenue);
        var passengerAssignment = passengerProvider.Current!.Assignments.Single(value => value.AssignmentId == passenger.AssignmentId);
        var passengerWallet = passengerProvider.Current.Economy.Wallets.Single(value => value.Account.Key == "Player:p");
        var fleetSpoof = costProvider.ManageFleetFor("actor-fleet-spoof", "attacker", costFixture.Asset.AssetId,
            FleetCommandAction.Rename, host, displayName: "Stolen name");
        var fleetOwner = costProvider.ManageFleetFor("actor-fleet-owner", "p", costFixture.Asset.AssetId,
            FleetCommandAction.Rename, host, displayName: "Owner name");

        Check(costSpoofRefused && assignmentSpoofRefused && leaseSpoof.State == LeaseActionState.Rejected && leaseSpoof.ResultCode == "lessee-control-required" && passengerSpoofRefused &&
              fleetSpoof.Outcome == FleetCommandOutcome.Rejected && fleetOwner.Outcome == FleetCommandOutcome.Succeeded,
            "actor-aware fleet, operating-cost, assignment, lease and passenger methods cannot cross a persistent player boundary");
        Check(passengerWallet.Balance == 90 && passengerAssignment.ExternalSettlement == ExternalSettlementState.NotRequired,
            "an authenticated remote passenger completion credits only its internal personal wallet exactly once");
        Check(paidAssignmentWallet.Balance == 80 && paidAssignment.ExternalSettlement == ExternalSettlementState.NotRequired,
            "an authenticated remote freight completion credits only its internal personal wallet exactly once");
    }

    private static void TestProtocolDuplicateConcurrencyAndLostResultAreExactlyOnce()
    {
        var executor = new FakeProtocolExecutor(); var host = new CompanyProtocolHost(executor); var envelope = ProtocolEnvelope("duplicate", new CompanyIntent { Type = CompanyIntentType.TransferFunds, SourceAccount = "Player:p1", DestinationAccount = "Company:c1", Amount = 1 });
        var results = new System.Collections.Concurrent.ConcurrentBag<ProtocolResult>();
        Parallel.For(0, 16, _ => results.Add(host.Receive(ReadyPeer(), envelope)));
        var replayAfterLostResult = host.Receive(ReadyPeer(), CompanyProtocolCodec.Decode(CompanyProtocolCodec.Encode(envelope)));
        var changed = ProtocolEnvelope("duplicate", new CompanyIntent { Type = CompanyIntentType.TransferFunds, SourceAccount = "Player:p1", DestinationAccount = "Company:c1", Amount = 2 });
        Check(executor.Calls == 1 && results.All(x => x.Status == ProtocolResultStatus.Succeeded) && replayAfterLostResult.Code == "ok", "concurrent duplicates and a lost result execute once and replay the terminal result");
        Check(host.Receive(ReadyPeer(), changed).Code == "request-id-reused", "one request ID cannot be rebound to another payload");
    }

    private static void TestProtocolRetryTimeoutReconnectAndOrdering()
    {
        var now = DateTimeOffset.UtcNow; var tracker = new ClientRequestTracker(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));
        var first = ProtocolEnvelope("first", new CompanyIntent { Type = CompanyIntentType.ApplyToCompany });
        var second = ProtocolEnvelope("second", new CompanyIntent { Type = CompanyIntentType.ApplyToCompany });
        tracker.Track(first, now); tracker.Track(second, now.AddMilliseconds(1));
        Check(tracker.DueRetries(now.AddSeconds(3)).Select(x => x.RequestId).SequenceEqual(new[] { "first", "second" }), "retries retain submission order");
        Check(tracker.RequestsToResumeAfterReconnect().Count == 2, "reconnect resumes every unresolved request with its original ID");
        Check(tracker.Accept(new ProtocolResult { RequestId = "second", Status = ProtocolResultStatus.Succeeded, Code = "ok" }) && tracker.ResultOrTimeout("second", now.AddSeconds(4)).Code == "ok", "out-of-order correlated results resolve the correct request");
        Check(tracker.ResultOrTimeout("first", now.AddSeconds(11)).Code == "request-timeout" && tracker.RequestsToResumeAfterReconnect().Single().RequestId == "first", "timeout is local and keeps the request resumable for authoritative replay");
        var clientRebindRefused = false;
        try { tracker.Track(ProtocolEnvelope("first", new CompanyIntent { Type = CompanyIntentType.CreateCompany, Name = "Another Company" }), now.AddSeconds(12)); }
        catch (InvalidOperationException exception) { clientRebindRefused = exception.Message == "client-request-id-reused"; }
        Check(clientRebindRefused && tracker.RequestsToResumeAfterReconnect().Single().RequestId == "first",
            "the client cannot rebind a pending request ID to another payload or reset its authoritative retry state");

        var bounded = new ClientRequestTracker(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));
        for (var index = 0; index < 260; index++)
        {
            var request = ProtocolEnvelope("bounded-" + index, new CompanyIntent { Type = CompanyIntentType.ApplyToCompany });
            bounded.Track(request, now.AddMilliseconds(index));
            bounded.Accept(new ProtocolResult { RequestId = request.RequestId, Status = ProtocolResultStatus.Succeeded, Code = "ok" });
        }
        Check(bounded.ResultOrTimeout("bounded-0", now).Code == "unknown-request" && bounded.ResultOrTimeout("bounded-259", now).Code == "ok",
            "the client tracker evicts old completed results instead of growing for the whole session");

        var saturated = new ClientRequestTracker(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));
        for (var index = 0; index < 256; index++)
            saturated.Track(ProtocolEnvelope("unresolved-" + index, new CompanyIntent { Type = CompanyIntentType.ApplyToCompany }), now);
        var capacityRefused = false;
        try { saturated.Track(ProtocolEnvelope("unresolved-overflow", new CompanyIntent { Type = CompanyIntentType.ApplyToCompany }), now); }
        catch (InvalidOperationException exception) { capacityRefused = exception.Message == "client-request-capacity-exhausted"; }
        Check(capacityRefused && saturated.RequestsToResumeAfterReconnect().Count == 256,
            "the client tracker never evicts unresolved authoritative requests when capacity is exhausted");

        var transientExecutor = new FakeProtocolExecutor();
        var transientHost = new CompanyProtocolHost(transientExecutor);
        for (var index = 0; index < 4097; index++)
            transientHost.Receive(ReadyPeer(), ProtocolEnvelope("state-bounded-" + index,
                new CompanyIntent { Type = CompanyIntentType.ModuleOperation, ModuleAction = "state.get", ModulePayloadJson = "{}" }));
        var afterInitialPages = transientExecutor.Calls;
        transientHost.Receive(ReadyPeer(), ProtocolEnvelope("state-bounded-4096",
            new CompanyIntent { Type = CompanyIntentType.ModuleOperation, ModuleAction = "state.get", ModulePayloadJson = "{}" }));
        var afterRecentReplay = transientExecutor.Calls;
        transientHost.Receive(ReadyPeer(), ProtocolEnvelope("state-bounded-0",
            new CompanyIntent { Type = CompanyIntentType.ModuleOperation, ModuleAction = "state.get", ModulePayloadJson = "{}" }));
        Check(afterInitialPages == 4097 && afterRecentReplay == afterInitialPages && transientExecutor.Calls == afterInitialPages + 1,
            "read-only state page replay is idempotent while retained and old page results are bounded");
    }

    private static void TestAuthoritativeStateTransferIsBoundedFrozenAndActorScoped()
    {
        var now = DateTimeOffset.UtcNow;
        var pager = new AuthoritativeStateSnapshotPager(2, TimeSpan.FromSeconds(5));
        var original = System.Text.Encoding.UTF8.GetBytes(new string('x', AuthoritativeStateSnapshotPager.MaximumPageDataBytes * 2 + 137) + "-é");
        var calls = 0;
        var first = pager.Read("player-a", null, 0, () => { calls++; return original; }, now);
        var encoded = AuthoritativeStatePageCodec.Encode(first);
        var decoded = AuthoritativeStatePageCodec.Decode(encoded);
        var assembler = new AuthoritativeStateSnapshotAssembler();
        Check(encoded.Length <= CompanyProtocolLimits.MaximumPayloadBytes - 1024 && decoded.Data.Length == AuthoritativeStateSnapshotPager.MaximumPageDataBytes,
            "authoritative state pages remain below the protocol result limit");
        Check(assembler.Accept(decoded) == null && assembler.Accept(decoded) == null,
            "an identical repeated state page is idempotent and does not advance the transfer twice");
        var offset = decoded.Data.Length;
        byte[]? complete = null;
        while (complete == null)
        {
            var page = pager.Read("player-a", decoded.SnapshotToken, offset, () => throw new InvalidOperationException("snapshot must remain frozen"), now.AddSeconds(1));
            complete = assembler.Accept(AuthoritativeStatePageCodec.Decode(AuthoritativeStatePageCodec.Encode(page)));
            offset += page.Data.Length;
        }
        Check(calls == 1 && complete.SequenceEqual(original), "paged state transfer reassembles one frozen UTF-8 snapshot exactly");
        var crossActorRefused = false;
        try { pager.Read("player-b", decoded.SnapshotToken, decoded.Data.Length, () => original, now.AddSeconds(1)); }
        catch (UnauthorizedAccessException) { crossActorRefused = true; }
        var expiredRefused = false;
        try { pager.Read("player-a", decoded.SnapshotToken, decoded.Data.Length, () => original, now.AddSeconds(7)); }
        catch (InvalidOperationException) { expiredRefused = true; }
        Check(crossActorRefused && expiredRefused, "state snapshot tokens are actor-scoped and expire fail closed");
        var oversizedTotalRefused = false;
        try { AuthoritativeStatePageCodec.Encode(new AuthoritativeStatePage { SnapshotToken = "oversized", Offset = 0, TotalLength = AuthoritativeStateSnapshotPager.MaximumSnapshotBytes + 1, Data = new byte[] { 1 } }); }
        catch (System.IO.InvalidDataException) { oversizedTotalRefused = true; }
        Check(oversizedTotalRefused, "a hostile state page cannot advertise an allocation beyond the bounded snapshot limit");
    }

    private static void TestFleetLocationProjectionUsesExactPhysicalIdentity()
    {
        var guid = Guid.NewGuid().ToString("D");
        var asset = FleetAsset.Create("LocoDE2", guid);
        var snapshot = new VehicleAcquisitionSnapshot();
        snapshot.Assets.Definitions.Add(new AssetDefinition { DefinitionId = "LocoDE2" });
        snapshot.Assets.Assets.Add(asset);
        snapshot.Fleet.Add(new FleetAssetState { AssetId = asset.AssetId, DisplayName = "BL-01", LastKnownLocation = "SM-A2P" });
        var observation = new AssetLifecycleObservation { PersistentCarGuid = guid.ToUpperInvariant(), DefinitionId = "LocoDE2", TrackId = " T12P " };

        var projected = FleetLocationProjection.Resolve(snapshot, new[] { observation });
        Check(projected[asset.AssetId] == "T12P" && snapshot.Fleet[0].LastKnownLocation == "SM-A2P",
            "Fleet Web projection reports the exact live physical track without mutating the persisted fallback");

        var ambiguous = FleetLocationProjection.Resolve(snapshot, new[] { observation, new AssetLifecycleObservation { PersistentCarGuid = guid, DefinitionId = "LocoDE2", TrackId = "T13P" } });
        var conflicting = FleetLocationProjection.Resolve(snapshot, new[] { new AssetLifecycleObservation { PersistentCarGuid = guid, DefinitionId = "LocoDE6", TrackId = "T12P" } });
        Check(!ambiguous.ContainsKey(asset.AssetId) && !conflicting.ContainsKey(asset.AssetId),
            "Fleet Web projection fails closed for duplicate identities and definition conflicts");
    }

    private static void TestExternalWalletMirrorReconcilesOneSidedChangesAndRefusesConflicts()
    {
        var fixture = Acquisition("wallet-mirror", 100, WorldOwnershipOutcome.Applied);
        var state = fixture.Snapshot.Economy;
        var wallet = state.Wallets.Single(value => value.Account.Key == "Player:p");
        var mirror = new ExternalWalletMirrorEngine(state);

        var initial = mirror.Plan("p", 100, "mirror-initial");
        mirror.Complete("p", 100, "mirror-initial");
        wallet.Balance = 85;
        wallet.Version++;
        var internalAhead = mirror.Plan("p", 100, "mirror-offline-rent");
        var retryAfterExternalDebit = mirror.Plan("p", 85, "mirror-offline-rent");
        mirror.Complete("p", 85, "mirror-offline-rent");

        var externalAhead = mirror.Plan("p", 105, "mirror-external-credit");
        wallet.Balance = 105;
        wallet.Version++;
        mirror.Complete("p", 105, "mirror-external-credit");
        wallet.Balance = 95;
        wallet.Version++;
        var conflict = mirror.Plan("p", 115, "mirror-conflict");

        Check(initial.Action == ExternalWalletMirrorAction.None && internalAhead.Action == ExternalWalletMirrorAction.DebitExternal && internalAhead.Amount == 15,
            "a persisted wallet mirror exports an offline authoritative rent instead of overwriting it on reconnect");
        Check(retryAfterExternalDebit.Action == ExternalWalletMirrorAction.None && externalAhead.Action == ExternalWalletMirrorAction.ImportExternal && externalAhead.Amount == 20,
            "wallet settlement retry is idempotent and an isolated external change can be imported");
        Check(conflict.Action == ExternalWalletMirrorAction.Conflict,
            "independent internal and external wallet changes fail closed instead of silently choosing one balance");

        var restored = VehicleAcquisitionPersistence.Deserialize(VehicleAcquisitionPersistence.Serialize(fixture.Snapshot), fixture.Snapshot.CheckpointId);
        var restoredMirror = restored.Economy.ExternalWalletMirrors.Single(value => value.PlayerId == "p");
        Check(restoredMirror.LastSynchronizedBalance == 105 && restoredMirror.LastOperationId == "mirror-external-credit",
            "wallet mirror generation and recovery metadata survive save and reload");

        var legacy = restored.Economy;
        legacy.SchemaVersion = 2;
        legacy.ExternalWalletMirrors = null!;
        CompanyEconomyPersistence.Migrate(legacy);
        Check(legacy.SchemaVersion == CompanyEconomySnapshot.CurrentVersion && legacy.ExternalWalletMirrors != null && legacy.ExternalWalletMirrors.Count == 0,
            "pre-mirror company economy snapshots migrate without inventing a synchronized balance");
    }

    private static CompanyProtocolEnvelope ProtocolEnvelope(string requestId, CompanyIntent intent, string playerId = "p1") => new CompanyProtocolEnvelope
    {
        MessageType = CompanyMessageType.IntentRequest,
        RequestId = requestId,
        PlayerId = playerId,
        CompanyId = "c1",
        ExpectedVersion = 7,
        Payload = CompanyIntentCodec.Encode(intent)
    };

    private static PeerContext ReadyPeer() => new PeerContext { PeerKey = "7", AuthenticatedPlayerId = "p1", IsKnown = true, IsReady = true };

    private sealed class FakeProtocolExecutor : ICompanyIntentExecutor
    {
        private int calls;
        public int Calls => calls;
        public ProtocolResult Execute(PeerContext peer, CompanyProtocolEnvelope envelope, CompanyIntent intent)
        {
            System.Threading.Interlocked.Increment(ref calls);
            return new ProtocolResult { RequestId = envelope.RequestId, Status = ProtocolResultStatus.Succeeded, Code = "ok", AuthoritativeVersion = envelope.ExpectedVersion + 1 };
        }
    }

    private static void Run(Action test) { tests++; test(); }
    private static void Check(bool condition, string name) { if (!condition) { failures++; Console.Error.WriteLine("FAIL: " + name); } }

    private sealed class FakeRoleDetector : INetworkRoleDetector { private readonly NetworkRoleReport role; public FakeRoleDetector(NetworkRoleReport role) => this.role = role; public NetworkRoleReport Detect() => role; }
    private sealed class FakeApiStateReader : INetworkApiStateReader { private readonly NetworkApiState state; public FakeApiStateReader(NetworkApiState state) => this.state = state; public NetworkApiState Read() => state; }
    private sealed class FakeDefinitions : IVehicleDefinitionReader { public int Calls; public string? Correlation; public IReadOnlyList<VehicleDefinitionRecord> ReadLoadedDefinitions(string id) { Calls++; Correlation = id; return new[] { new VehicleDefinitionRecord { ExistingDefinitionId = "def" } }; } }
    private sealed class FakeInventory : IVisibleVehicleReader { public int Calls; public string? Correlation; public IReadOnlyList<VehicleInstanceRecord> ReadVisibleInventory(string id) { Calls++; Correlation = id; return new[] { new VehicleInstanceRecord { ExistingPersistentId = "instance" } }; } }
    private sealed class FakeWriter : IDiagnosticWriter { public DiagnosticSnapshot? Snapshot; public string Write(DiagnosticSnapshot snapshot) { Snapshot = snapshot; return "snapshot.json"; } }
    private sealed class FakeTrace : IDiagnosticTrace { public string? LastCorrelation; public void Info(string id, string message) => LastCorrelation = id; public void Error(string id, string message, Exception exception) => LastCorrelation = id; }
}
