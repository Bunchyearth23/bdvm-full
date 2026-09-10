using System;
using System.Collections.Generic;
using System.Linq;
using BDVM.Management;
using BDVM.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BDVM.Adapters;

internal sealed class RuntimeManagementPort : IManagementAuthoritativePort
{
    private readonly Func<string, string> snapshot;
    private readonly Func<string, string, string> execute;

    public RuntimeManagementPort(Func<string, string> snapshot, Func<string, string, string> execute)
    {
        this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    public ManagementWebSnapshot ReadSnapshot(string authenticatedPrincipal, string correlationId)
    {
        var source = JObject.Parse(snapshot(authenticatedPrincipal));
        return new ManagementWebSnapshot
        {
            Version = HighestVersion(source), CorrelationId = correlationId, FeatureFlags = Features(source),
            Companies = Rows(source["companies"]), Wallets = Rows(source["wallets"]), Fleet = Rows(source["fleet"]),
            Market = MarketRows(source), Deliveries = Rows(source["initialDeliveries"]),
            Leases = Rows(source["leases"], source["outboundLeases"]?["contracts"]),
            Assignments = Rows(source["assignments"]),
            Financing = Rows(source["financing"]?["contracts"], source["financing"]?["pools"]),
            YardPlans = Rows(source["triageAssistance"]?["plans"]),
            Industry = Rows(source["industrial"]?["stocks"], source["industrial"]?["recipes"], source["industrial"]?["policies"], source["industrial"]?["needs"], source["industrial"]?["contracts"]),
            Contracts = Rows(source["assignments"], source["industrial"]?["contracts"], source["passengers"]?["contracts"]),
            Passengers = Rows(source["passengers"]?["routes"], source["passengers"]?["contracts"]),
            Maintenance = Rows(source["operatingCosts"]),
            Diagnostics = DiagnosticRows(source),
            Actions = ManagementActions(source)
        };
    }

    public ManagementAuthorityResult Execute(ManagementAuthorityRequest request)
    {
        var payload = JObject.FromObject(request.Payload ?? new Dictionary<string, object>());
        payload["action"] = ResolveAction(request.IntentType, (string?)payload["action"]);
        payload["correlationId"] = request.CorrelationId;
        payload["expectedVersion"] = request.ExpectedVersion;
        var result = JObject.Parse(execute(request.Principal, payload.ToString(Formatting.None)));
        return new ManagementAuthorityResult
        {
            State = string.Equals((string?)result["status"], "succeeded", StringComparison.OrdinalIgnoreCase) ? "Succeeded" : "Reconcile",
            Code = (string?)result["status"] ?? "unknown-result", Version = request.ExpectedVersion + 1,
            Data = result.ToObject<Dictionary<string, object>>() ?? new Dictionary<string, object>()
        };
    }

    private static string ResolveAction(string intent, string? requested)
    {
        if (intent == "bdvm.management.intent.v1" || intent == "bdvm.management.company-governance.v1") return requested ?? "";
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bdvm.management.company-dissolve.v1"] = "company.dissolve", ["bdvm.management.wallet-transfer.v1"] = "wallet.transfer",
            ["bdvm.management.fleet-manage.v1"] = requested ?? "fleet.set-state", ["bdvm.management.fleet.rename.v1"] = "fleet.rename",
            ["bdvm.management.fleet-bundle.v1"] = "fleet.bundle", ["bdvm.management.fleet-resale.v1"] = "fleet.resale",
            ["bdvm.management.fleet-maintenance.v1"] = "fleet.maintenance", ["bdvm.management.market.purchase.v1"] = "market.purchase",
            ["bdvm.management.initial-delivery.v1"] = "initial-delivery.place", ["bdvm.management.lease-manage.v1"] = requested ?? "lease.manage",
            ["bdvm.management.assignment-manage.v1"] = requested ?? "assignment.manage", ["bdvm.management.finance-manage.v1"] = requested ?? "finance.manage",
            ["bdvm.management.yard-manage.v1"] = requested ?? "yard.manage", ["bdvm.management.industry-manage.v1"] = requested ?? "industry.manage"
        };
        return map.TryGetValue(intent, out var action) ? action : "";
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object>> Rows(params JToken?[] sources) => sources
        .Where(x => x is JArray).SelectMany(x => (JArray)x!).OfType<JObject>()
        .Select(x => (IReadOnlyDictionary<string, object>)(x.ToObject<Dictionary<string, object>>() ?? new Dictionary<string, object>())).ToArray();

    private static IReadOnlyList<IReadOnlyDictionary<string, object>> MarketRows(JObject source)
    {
        var rows = new List<IReadOnlyDictionary<string, object>>();
        AddMarketRows(rows, "Offer", source["market"]);
        AddMarketRows(rows, "Catalog model", source["catalog"]);
        AddMarketRows(rows, "Finite stock", source["marketStock"]);
        return rows;
    }

    private static void AddMarketRows(ICollection<IReadOnlyDictionary<string, object>> rows, string recordType, JToken? source)
    {
        foreach (var value in source as JArray ?? new JArray())
        {
            if (!(value is JObject item)) continue;
            var row = item.ToObject<Dictionary<string, object>>() ?? new Dictionary<string, object>();
            row["recordType"] = recordType;
            rows.Add(row);
        }
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object>> ObjectRows(params JToken?[] sources) => sources
        .OfType<JObject>().Select(x => (IReadOnlyDictionary<string, object>)(x.ToObject<Dictionary<string, object>>() ?? new Dictionary<string, object>())).ToArray();

    private static IReadOnlyList<IReadOnlyDictionary<string, object>> DiagnosticRows(JObject source)
    {
        var rows = new List<IReadOnlyDictionary<string, object>>();
        AddDiagnosticRow(rows, "World population", source["worldPopulation"]);
        AddDiagnosticRow(rows, "Asset lifecycle", source["assetLifecycle"]);
        AddDiagnosticRow(rows, "Dynamic economy", source["dynamicEconomy"]);
        return rows;
    }

    private static void AddDiagnosticRow(ICollection<IReadOnlyDictionary<string, object>> rows, string subsystem, JToken? source)
    {
        if (!(source is JObject item)) return;
        var row = item.ToObject<Dictionary<string, object>>() ?? new Dictionary<string, object>();
        row["subsystem"] = subsystem;
        rows.Add(row);
    }

    private static long HighestVersion(JObject source) => source.Descendants().OfType<JProperty>()
        .Where(x => string.Equals(x.Name, "version", StringComparison.OrdinalIgnoreCase) && x.Value.Type == JTokenType.Integer)
        .Select(x => (long)x.Value).DefaultIfEmpty(0).Max();

    private static IReadOnlyDictionary<string, bool> Features(JObject source) => new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        ["industry"] = (bool?)source["industrial"]?["enabled"] ?? false, ["passengers"] = (bool?)source["passengers"]?["enabled"] ?? false,
        ["financing"] = (bool?)source["financing"]?["enabled"] ?? false, ["yardPlans"] = (bool?)source["triageAssistance"]?["enabled"] ?? false,
        ["strictPopulation"] = (bool?)source["worldPopulation"]?["strict"] ?? false
    };

    private static IReadOnlyList<ManagementActionDescriptor> ManagementActions(JObject source)
    {
        var actions = new List<ManagementActionDescriptor>();
        var locationLabels = Labels(source["locationChoices"], "id", "name");
        var cargoLabels = Labels(source["cargoChoices"], "id", "name");
        var fleetLabels = (source["fleet"] as JArray ?? new JArray()).OfType<JObject>()
            .Where(value => !string.IsNullOrWhiteSpace((string?)value["assetId"]))
            .GroupBy(value => (string)value["assetId"]!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => FleetLabel(group.First()), StringComparer.Ordinal);
        var fleetDefinitionLabels = (source["fleet"] as JArray ?? new JArray()).OfType<JObject>()
            .Where(value => !string.IsNullOrWhiteSpace((string?)value["definitionId"]))
            .GroupBy(value => (string)value["definitionId"]!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => ((string?)group.First()["displayName"] ?? group.Key) + " model (ID: " + group.Key + ")", StringComparer.Ordinal);
        var fleetDefinitionIds = fleetDefinitionLabels.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var missionLabels = Labels(source["missionChoices"], "missionId", "displayName");
        var deliveryTracks = (source["deliveryTracks"] as JArray ?? new JArray())
            .Select(value => new
            {
                TrackId = ((string?)value["trackId"] ?? "").Trim(),
                Kind = ((string?)value["kind"] ?? "").Trim()
            })
            .Where(value => value.TrackId.Length > 0 && (value.Kind == "Depot" || value.Kind == "ServiceTrack"))
            .GroupBy(value => value.TrackId, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(32)
            .ToArray();
        var deliveryTrackLabels = deliveryTracks.ToDictionary(value => value.TrackId,
            value => (locationLabels.TryGetValue(value.TrackId.Split('-')[0], out var station) ? station + " — " : "") + value.Kind + " " + value.TrackId + " (ID: " + value.TrackId + ")",
            StringComparer.Ordinal);
        var missionIds = (source["missionChoices"] as JArray ?? new JArray()).Where(value => string.Equals((string?)value["kind"], "Freight", StringComparison.Ordinal)).Select(value => (string?)value["missionId"] ?? "").Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var passengerMissionIds = (source["missionChoices"] as JArray ?? new JArray()).Where(value => string.Equals((string?)value["kind"], "Passenger", StringComparison.Ordinal)).Select(value => (string?)value["missionId"] ?? "").Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var visibleAssetIds = (source["fleet"] as JArray ?? new JArray()).Select(value => (string?)value["assetId"] ?? "").Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var passengerRouteIds = (source["passengers"]?["routes"] as JArray ?? new JArray()).Select(value => (string?)value["routeId"] ?? "").Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var activeAssignmentIds = (source["assignments"] as JArray ?? new JArray())
            .Where(value => string.Equals((string?)value["state"], "Reserved", StringComparison.Ordinal) || string.Equals((string?)value["state"], "Active", StringComparison.Ordinal))
            .Select(value => (string?)value["assignmentId"] ?? "").Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var grant in source["initialDeliveries"] as JArray ?? new JArray())
        {
            if (!string.Equals((string?)grant["state"], "Available", StringComparison.Ordinal)) continue;
            var grantId = (string?)grant["grantId"] ?? "delivery";
            foreach (var track in deliveryTracks)
                actions.Add(Action("deliveries", "Place " + grantId + " on " + track.Kind + " " + track.TrackId, "bdvm.management.initial-delivery.v1",
                    new Dictionary<string, object> { ["grantId"] = grantId, ["trackId"] = track.TrackId, ["targetKind"] = track.Kind },
                    "Confirm physical placement on this host-approved track."));
        }

        var catalogCandidates = (source["catalogCandidates"] as JArray ?? new JArray())
            .Select(value => ((string?)value["definitionId"] ?? "").Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(256)
            .ToArray();
        if (catalogCandidates.Length > 0 && deliveryTracks.Length > 0)
            actions.Add(Action("market", "Add installed model to finite catalog", "bdvm.management.intent.v1",
                new Dictionary<string, object> { ["action"] = "market.configure" },
                "This creates an explicit host-priced catalog entry and may publish one finite-stock offer.",
                ChoiceField("definitionId", "Installed rolling-stock model", catalogCandidates, Labels(source["catalogCandidates"], "definitionId", "displayName")),
                ChoiceField("locationId", "Delivery depot or service track", deliveryTracks.Select(value => value.TrackId).ToArray(), deliveryTrackLabels),
                Field("basePrice", "Base purchase price", "number", "100000", true),
                Field("transferFee", "Transfer fee", "number", "0", true),
                Field("buybackRate", "Perfect-condition buyback rate", "number", "0.5", true),
                Field("initialStock", "Initial finite stock", "number", "1", true)));

        foreach (var stock in source["marketStock"] as JArray ?? new JArray())
        {
            var available = (int?)stock["available"] ?? 0;
            var definitionId = (string?)stock["definitionId"] ?? "";
            var locationId = (string?)stock["locationId"] ?? "";
            if (available <= 0 || definitionId.Length == 0 || locationId.Length == 0) continue;
            actions.Add(Action("market", "Publish offer for " + definitionId + " at " + locationId, "bdvm.management.intent.v1",
                new Dictionary<string, object> { ["action"] = "market.generate-order", ["definitionId"] = definitionId, ["locationId"] = locationId },
                "This consumes one unit of finite stock and publishes a time-limited purchase offer."));
        }

        foreach (var listing in source["market"] as JArray ?? new JArray())
        {
            if (!string.Equals((string?)listing["state"], "Available", StringComparison.Ordinal)) continue;
            var listingId = (string?)listing["listingId"] ?? "";
            actions.Add(Action("market", "Purchase " + ((string?)listing["definitionId"] ?? "listing"), "bdvm.management.market.purchase.v1",
                new Dictionary<string, object> { ["listingId"] = listingId }, "Confirm this purchase.", Field("forCompany", "Company pays", "checkbox")));
            actions.Add(Action("leases", "Offer lease for " + ((string?)listing["definitionId"] ?? "listing"), "bdvm.management.lease-manage.v1",
                new Dictionary<string, object> { ["action"] = "lease.manage", ["operation"] = string.Equals((string?)listing["kind"], "NewOrder", StringComparison.Ordinal) ? "create-catalog-listings" : "create-existing", [string.Equals((string?)listing["kind"], "NewOrder", StringComparison.Ordinal) ? "listingIds" : "assetIds"] = new[] { string.Equals((string?)listing["kind"], "NewOrder", StringComparison.Ordinal) ? listingId : ((string?)listing["assetId"] ?? "") } }, "",
                LeaseTermFields()));
        }

        foreach (var lease in source["leases"] as JArray ?? new JArray())
        {
            var id = (string?)lease["leaseId"] ?? ""; var state = (string?)lease["state"] ?? "";
            if (state == "Offered") actions.Add(Action("leases", "Accept " + id, "bdvm.management.lease-manage.v1", new Dictionary<string, object> { ["action"] = "lease.manage", ["operation"] = "accept", ["leaseId"] = id }, "Confirm this lease commitment.", Field("forCompany", "Company pays and operates", "checkbox")));
            if (state == "Active" || state == "Delinquent" || state == "ReturnDue")
            {
                actions.Add(Action("leases", "Return " + id, "bdvm.management.lease-manage.v1", new Dictionary<string, object> { ["action"] = "lease.manage", ["operation"] = "return", ["leaseId"] = id }, "The whole consist must be safe on a configured depot or service track."));
                actions.Add(Action("leases", "Purchase " + id, "bdvm.management.lease-manage.v1", new Dictionary<string, object> { ["action"] = "lease.manage", ["operation"] = "purchase", ["leaseId"] = id }, "Confirm the explicit purchase option."));
            }
        }

        foreach (var fleet in source["fleet"] as JArray ?? new JArray())
            actions.Add(Action("maintenance", "Observe maintenance for " + ((string?)fleet["displayName"] ?? (string?)fleet["assetId"] ?? "vehicle"), "bdvm.management.fleet-maintenance.v1",
                new Dictionary<string, object> { ["action"] = "fleet.maintenance", ["operation"] = "begin", ["assetId"] = (string?)fleet["assetId"] ?? "" }, "",
                Field("maintenanceAction", "Action", "select", "Inspect", true, new[] { "Inspect", "Service", "Repair", "Refuel" }), Field("maximumCost", "Maximum authorized cost", "number", "1000", true), Field("forCompany", "Company pays", "checkbox"), Field("tripId", "Optional trip ID")));
        foreach (var session in source["operatingCosts"] as JArray ?? new JArray())
            if (string.Equals((string?)session["state"], "Open", StringComparison.Ordinal))
            {
                var id = (string?)session["sessionId"] ?? "";
                actions.Add(Action("maintenance", "Complete " + id, "bdvm.management.fleet-maintenance.v1", new Dictionary<string, object> { ["action"] = "fleet.maintenance", ["operation"] = "complete", ["sessionId"] = id }, "Confirm after completing the vanilla service manually."));
                actions.Add(Action("maintenance", "Cancel " + id, "bdvm.management.fleet-maintenance.v1", new Dictionary<string, object> { ["action"] = "fleet.maintenance", ["operation"] = "cancel", ["sessionId"] = id }));
            }

        actions.Add(Action("assignments", "Reserve an existing freight job", "bdvm.management.assignment-manage.v1", new Dictionary<string, object> { ["action"] = "assignment.manage", ["operation"] = "reserve", ["kind"] = "Freight" }, "",
            ChoiceField("missionId", "Loaded freight job", missionIds, missionLabels), ChoiceField("assetIds", "Operator rolling stock", visibleAssetIds, fleetLabels, true), Field("forCompany", "Company operation", "checkbox"), Field("maximumRevenue", "Maximum expected revenue", "number", "0", true)));
        foreach (var assignment in source["assignments"] as JArray ?? new JArray())
        {
            var id = (string?)assignment["assignmentId"] ?? ""; var state = (string?)assignment["state"] ?? "";
            if (state == "Reserved") actions.Add(Action("assignments", "Start " + id, "bdvm.management.assignment-manage.v1", new Dictionary<string, object> { ["action"] = "assignment.manage", ["operation"] = "start", ["assignmentId"] = id }));
            if (state == "Active" || state == "CompletionPending") actions.Add(Action("assignments", "Complete " + id, "bdvm.management.assignment-manage.v1", new Dictionary<string, object> { ["action"] = "assignment.manage", ["operation"] = "complete", ["assignmentId"] = id }));
            if (state == "Reserved" || state == "Active" || state == "CompletionPending") actions.Add(Action("assignments", "Cancel " + id, "bdvm.management.assignment-manage.v1", new Dictionary<string, object> { ["action"] = "assignment.manage", ["operation"] = "cancel", ["assignmentId"] = id }, "Confirm cancellation of the external job first."));
        }

        var passengerLocations = locationLabels.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        actions.Add(Action("passengers", "Create a passenger route", "bdvm.management.assignment-manage.v1", new Dictionary<string, object> { ["action"] = "assignment.manage", ["operation"] = "passenger-configure-route" }, "",
            ChoiceField("originId", "Departure station", passengerLocations, locationLabels), ChoiceField("destinationId", "Arrival station", passengerLocations, locationLabels), Field("initialDemand", "Passengers waiting initially", "number", "20", true), Field("maximumDemand", "Maximum waiting passengers", "number", "100", true), Field("growthPerCycle", "New passengers per demand cycle", "number", "5", true), Field("frequencyTicks", "Target service interval", "number", "100", true), Field("baseFare", "Fare per passenger", "number", "100", true), Field("latePenalty", "Late penalty per time unit", "number", "1", true)));
        actions.Add(Action("passengers", "Reserve passenger service", "bdvm.management.assignment-manage.v1", new Dictionary<string, object> { ["action"] = "assignment.manage", ["operation"] = "passenger-reserve" }, "",
            ChoiceField("routeId", "Passenger route", passengerRouteIds, PassengerRouteLabels(source, locationLabels)), ChoiceField("passengerJobId", "Loaded Passenger Jobs service", passengerMissionIds, missionLabels), ChoiceField("assetIds", "Passenger rolling stock", visibleAssetIds, fleetLabels, true), Field("capacity", "Available passenger seats", "number", "1", true), Field("journeyTicks", "Planned journey duration", "number", "100", true), Field("forCompany", "Company operation", "checkbox")));
        foreach (var route in source["passengers"]?["routes"] as JArray ?? new JArray())
            actions.Add(Action("passengers", "Refresh demand for " + ((string?)route["routeId"] ?? "route"), "bdvm.management.assignment-manage.v1", new Dictionary<string, object> { ["action"] = "assignment.manage", ["operation"] = "passenger-refresh-route", ["routeId"] = (string?)route["routeId"] ?? "" }));
        foreach (var passenger in source["passengers"]?["contracts"] as JArray ?? new JArray())
        {
            var id = (string?)passenger["contractId"] ?? ""; var state = (string?)passenger["state"] ?? "";
            if (state == "Reserved") actions.Add(Action("passengers", "Start " + id, "bdvm.management.assignment-manage.v1", new Dictionary<string, object> { ["action"] = "assignment.manage", ["operation"] = "passenger-start", ["contractId"] = id }));
            if (state == "Active" || state == "CompletionPending") actions.Add(Action("passengers", "Complete " + id, "bdvm.management.assignment-manage.v1", new Dictionary<string, object> { ["action"] = "assignment.manage", ["operation"] = "passenger-complete", ["contractId"] = id }));
            if (state == "Reserved" || state == "Active" || state == "CompletionPending") actions.Add(Action("passengers", "Cancel " + id, "bdvm.management.assignment-manage.v1", new Dictionary<string, object> { ["action"] = "assignment.manage", ["operation"] = "passenger-cancel", ["contractId"] = id }, "Confirm cancellation of the Passenger Jobs service first."));
        }

        var pilotPersonalWagons = source["industrial"]?["pilotPersonalWagons"]?.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray() ?? Array.Empty<string>();
        var pilotCompanyWagons = source["industrial"]?["pilotCompanyWagons"]?.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray() ?? Array.Empty<string>();
        if (pilotPersonalWagons.Length > 0)
            actions.Add(Action("industry", "Discover stations and configure a personal pilot chain", "bdvm.management.industry-manage.v1",
                new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "configure-pilot", ["forCompany"] = false },
                "Choose your wagons. BDVM automatically finds compatible cargo and two loaded stations, creates stock and production, then publishes the first transport offer.", ChoiceField("assetIds", "Freight wagons to use", pilotPersonalWagons, fleetLabels, true)));
        if (pilotCompanyWagons.Length > 0)
            actions.Add(Action("industry", "Discover stations and configure a company pilot chain", "bdvm.management.industry-manage.v1",
                new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "configure-pilot", ["forCompany"] = true },
                "Choose company wagons. BDVM automatically finds compatible cargo and two loaded stations, creates stock and production, then publishes the first transport offer.", ChoiceField("assetIds", "Company freight wagons to use", pilotCompanyWagons, fleetLabels, true)));
        actions.Add(Action("industry", "Configure industrial stock", "bdvm.management.industry-manage.v1", new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "configure-stock" }, "", ChoiceField("facilityId", "Station", passengerLocations, locationLabels), ChoiceField("cargoId", "Cargo", cargoLabels.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray(), cargoLabels), Field("onHand", "Quantity currently available", "number", "0", true), Field("capacity", "Maximum storage capacity", "number", "100", true)));
        actions.Add(Action("industry", "Configure production recipe", "bdvm.management.industry-manage.v1", new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "configure-recipe" }, "", Field("recipeId", "Internal recipe name", "text", "production-1", true), ChoiceField("facilityId", "Producing station", passengerLocations, locationLabels), ChoiceField("inputCargoId", "Cargo consumed", cargoLabels.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray(), cargoLabels), Field("inputQuantity", "Quantity consumed", "number", "10", true), ChoiceField("outputCargoId", "Cargo produced", cargoLabels.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray(), cargoLabels), Field("outputQuantity", "Quantity produced", "number", "10", true), Field("cadenceTicks", "Production interval", "number", "60", true), Field("maximumBacklogCycles", "Maximum stored production cycles", "number", "4", true)));
        actions.Add(Action("industry", "Configure shortage-driven transport policy", "bdvm.management.industry-manage.v1", new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "configure-policy" }, "", Field("policyId", "Internal policy name", "text", "transport-policy-1", true), ChoiceField("originFacilityId", "Cargo pickup station", passengerLocations, locationLabels), ChoiceField("destinationFacilityId", "Cargo delivery station", passengerLocations, locationLabels), ChoiceField("cargoId", "Cargo", cargoLabels.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray(), cargoLabels), Field("batchQuantity", "Maximum cargo per offer", "number", "30", true), Field("destinationTargetQuantity", "Target stock at destination", "number", "60", true), Field("baseReward", "Base payment", "number", "15000", true), Field("maximumScarcityBonus", "Maximum shortage bonus", "number", "5000", true), Field("offerLifetimeTicks", "Time before an unaccepted offer expires", "number", "600", true), Field("preparationDurationTicks", "Time allowed to prepare wagons", "number", "600", true), Field("deliveryDurationTicks", "Time allowed for delivery", "number", "3600", true), Field("preparationPenalty", "Penalty if preparation expires", "number", "0"), Field("minimumWagonCount", "Minimum number of wagons", "number", "1", true), Field("minimumTotalCapacity", "Minimum cargo capacity", "number", "30", true), ChoiceField("allowedDefinitionIds", "Allowed wagon models", fleetDefinitionIds, fleetDefinitionLabels, true), Field("enabled", "Publish offers from this policy", "checkbox", "true")));
        actions.Add(Action("industry", "Create one manual transport contract", "bdvm.management.industry-manage.v1", new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "create" }, "", ChoiceField("originFacilityId", "Cargo pickup station", passengerLocations, locationLabels), ChoiceField("destinationFacilityId", "Cargo delivery station", passengerLocations, locationLabels), ChoiceField("cargoId", "Cargo", cargoLabels.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray(), cargoLabels), Field("quantity", "Cargo quantity", "number", "30", true), Field("baseReward", "Base payment", "number", "15000", true), Field("scarcityBonus", "Shortage bonus", "number", "0", true), Field("deadlineTick", "Delivery deadline", "number", "3600", true), Field("minimumWagonCount", "Minimum number of wagons", "number", "1", true), Field("minimumTotalCapacity", "Minimum cargo capacity", "number", "30", true), ChoiceField("allowedDefinitionIds", "Allowed wagon models", fleetDefinitionIds, fleetDefinitionLabels, true), Field("preparationPenalty", "Preparation expiry penalty", "number", "0"), Field("forCompany", "Company operation", "checkbox")));
        foreach (var recipe in source["industrial"]?["recipes"] as JArray ?? new JArray())
            actions.Add(Action("industry", "Advance production for " + ((string?)recipe["recipeId"] ?? "recipe"), "bdvm.management.industry-manage.v1", new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "advance-production", ["recipeId"] = (string?)recipe["recipeId"] ?? "" }));
        foreach (var policy in source["industrial"]?["policies"] as JArray ?? new JArray())
            if ((bool?)policy["enabled"] ?? false)
                actions.Add(Action("industry", "Publish need for " + ((string?)policy["policyId"] ?? "policy"), "bdvm.management.industry-manage.v1", new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "publish-need", ["policyId"] = (string?)policy["policyId"] ?? "" }));
        foreach (var need in source["industrial"]?["needs"] as JArray ?? new JArray())
            if (string.Equals((string?)need["state"], "Available", StringComparison.Ordinal))
                actions.Add(Action("industry", "Accept transport need " + ((string?)need["needId"] ?? "need"), "bdvm.management.industry-manage.v1", new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "accept-need", ["needId"] = (string?)need["needId"] ?? "", ["needVersion"] = (long?)need["version"] ?? 0 }, "Confirm this stock and capacity reservation.", Field("forCompany", "Company operation", "checkbox")));
        foreach (var contract in source["industrial"]?["contracts"] as JArray ?? new JArray())
        {
            var id = (string?)contract["contractId"] ?? ""; var state = (string?)contract["state"] ?? "";
            if (state == "Offered") actions.Add(Action("industry", "Accept " + id, "bdvm.management.industry-manage.v1", new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "accept", ["contractId"] = id }, "", Field("preparationDuration", "Preparation duration", "number", "100", true), Field("applyPreparationPenalty", "Apply preparation penalty", "checkbox"), Field("forCompany", "Company operation", "checkbox")));
            if (state == "Reserved")
            {
                var personal = contract["compatiblePersonalWagons"]?.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray() ?? Array.Empty<string>();
                var company = contract["compatibleCompanyWagons"]?.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray() ?? Array.Empty<string>();
                if (personal.Length > 0) actions.Add(Action("industry", "Assign personal wagons to " + id, "bdvm.management.industry-manage.v1", new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "assign", ["contractId"] = id, ["forCompany"] = false }, "", Field("assetIds", "Compatible operator wagons", "multiselect", "", true, personal)));
                if (company.Length > 0) actions.Add(Action("industry", "Assign company wagons to " + id, "bdvm.management.industry-manage.v1", new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "assign", ["contractId"] = id, ["forCompany"] = true }, "", Field("assetIds", "Compatible company wagons", "multiselect", "", true, company)));
            }
            if (state == "Reserved") actions.Add(Action("industry", "Activate " + id, "bdvm.management.industry-manage.v1", new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "activate", ["contractId"] = id }));
            if (state == "Active" || state == "DeliveryPending") actions.Add(Action("industry", "Reconcile physical delivery " + id, "bdvm.management.industry-manage.v1", new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "reconcile-delivery", ["contractId"] = id }));
            if (state == "Offered" || state == "Reserved" || state == "Active" || state == "DeliveryPending") actions.Add(Action("industry", "Cancel " + id, "bdvm.management.industry-manage.v1", new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "cancel", ["contractId"] = id }, "Abandon any external SelfShunt job first, then confirm contract cancellation."));
        }

        var financingPools = (source["financing"]?["pools"] as JArray ?? new JArray())
            .Select(value => (string?)value["poolId"] ?? "").Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        actions.Add(Action("financing", "Register lender pool", "bdvm.management.finance-manage.v1",
            new Dictionary<string, object> { ["action"] = "finance.manage", ["operation"] = "register-pool" }, "",
            Field("poolId", "Lender pool ID", "text", "bdvm-lender", true), Field("backedCapital", "Backed capital", "number", "1000000", true)));
        if (financingPools.Length > 0)
            actions.Add(Action("financing", "Create financing offer", "bdvm.management.finance-manage.v1",
                new Dictionary<string, object> { ["action"] = "finance.manage", ["operation"] = "offer" }, "",
                Field("contractId", "Optional contract ID"), ChoiceField("kind", "Financing type", new[] { "Loan", "CreditLine" }),
                ChoiceField("poolId", "Lender pool", financingPools), Field("principal", "Principal limit", "number", "100000", true),
                Field("interestBasisPoints", "Interest (basis points)", "number", "500", true), Field("installment", "Minimum installment", "number", "1000", true),
                Field("intervalTicks", "Payment interval", "number", "100", true), Field("maturityTicks", "Maturity", "number", "1000", true),
                Field("guarantee", "Guarantee", "number", "0", true), Field("forCompany", "Company financing", "checkbox")));
        foreach (var financing in source["financing"]?["contracts"] as JArray ?? new JArray())
        {
            var id = (string?)financing["contractId"] ?? ""; var state = (string?)financing["state"] ?? ""; var kind = (string?)financing["kind"] ?? "";
            if (state == "Offered") actions.Add(Action("financing", "Accept financing " + id, "bdvm.management.finance-manage.v1", new Dictionary<string, object> { ["action"] = "finance.manage", ["operation"] = "accept", ["contractId"] = id }, "Confirm the disclosed financing commitment."));
            if (state == "Active" && kind == "CreditLine") actions.Add(Action("financing", "Draw from " + id, "bdvm.management.finance-manage.v1", new Dictionary<string, object> { ["action"] = "finance.manage", ["operation"] = "draw", ["contractId"] = id }, "", Field("amount", "Amount", "number", "0", true)));
            if (state == "Active" || state == "Defaulted") actions.Add(Action("financing", "Repay " + id, "bdvm.management.finance-manage.v1", new Dictionary<string, object> { ["action"] = "finance.manage", ["operation"] = "repay", ["contractId"] = id }, "", Field("amount", "Amount", "number", "0", true)));
        }

        actions.Add(Action("yardPlans", "Create planning-only yard sequence", "bdvm.management.yard-manage.v1", new Dictionary<string, object> { ["action"] = "yard.manage", ["operation"] = "create" }, "", Field("planId", "Plan ID", "text", "", true), ChoiceField("assignmentId", "Active assignment", activeAssignmentIds), Field("trackIds", "Ordered track IDs (comma separated)", "csv", "", true)));
        foreach (var plan in source["triageAssistance"]?["plans"] as JArray ?? new JArray())
            if (string.Equals((string?)plan["state"], "Planned", StringComparison.Ordinal) || string.Equals((string?)plan["state"], "ExecutionPending", StringComparison.Ordinal))
                actions.Add(Action("yardPlans", "Cancel " + ((string?)plan["planId"] ?? "plan"), "bdvm.management.yard-manage.v1", new Dictionary<string, object> { ["action"] = "yard.manage", ["operation"] = "cancel", ["planId"] = (string?)plan["planId"] ?? "" }));

        actions.Add(Action("companies", "Dissolve company", "bdvm.management.company-dissolve.v1", new Dictionary<string, object>(), "This dissolution is permanent."));
        return actions;
    }

    private static ManagementActionDescriptor Action(string area, string label, string intent, IReadOnlyDictionary<string, object> payload, string confirmation = "", params ManagementActionField[] fields) =>
        new ManagementActionDescriptor { Area = area, Label = label, IntentType = intent, Payload = payload, Confirmation = confirmation, Fields = fields };

    private static ManagementActionField Field(string name, string label, string kind = "text", string value = "", bool required = false, IReadOnlyList<string>? options = null) =>
        new ManagementActionField { Name = name, Label = label, Kind = kind, Value = value, Required = required, Options = options ?? Array.Empty<string>() };

    private static ManagementActionField ChoiceField(string name, string label, IReadOnlyList<string> options, bool multiple = false) =>
        Field(name, label, options.Count == 0 ? (multiple ? "csv" : "text") : (multiple ? "multiselect" : "select"), options.FirstOrDefault() ?? "", true, options);

    private static ManagementActionField ChoiceField(string name, string label, IReadOnlyList<string> options, IReadOnlyDictionary<string, string> optionLabels, bool multiple = false) =>
        new ManagementActionField { Name = name, Label = label, Kind = options.Count == 0 ? (multiple ? "csv" : "text") : (multiple ? "multiselect" : "select"), Value = options.FirstOrDefault() ?? "", Required = true, Options = options, OptionLabels = optionLabels };

    private static Dictionary<string, string> Labels(JToken? source, string idProperty, string nameProperty) =>
        (source as JArray ?? new JArray()).OfType<JObject>()
            .Where(value => !string.IsNullOrWhiteSpace((string?)value[idProperty]))
            .GroupBy(value => (string)value[idProperty]!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group =>
            {
                var name = ((string?)group.First()[nameProperty] ?? group.Key).Trim();
                return name + " (ID: " + group.Key + ")";
            }, StringComparer.Ordinal);

    private static string FleetLabel(JObject value)
    {
        var id = (string?)value["assetId"] ?? "unknown";
        var name = (string?)value["displayName"] ?? (string?)value["definitionId"] ?? "Rolling stock";
        var location = (string?)value["lastKnownLocation"] ?? "location unknown";
        return name + " — " + location + " (ID: " + id + ")";
    }

    private static Dictionary<string, string> PassengerRouteLabels(JObject source, IReadOnlyDictionary<string, string> locations) =>
        (source["passengers"]?["routes"] as JArray ?? new JArray()).OfType<JObject>()
            .Where(value => !string.IsNullOrWhiteSpace((string?)value["routeId"]))
            .GroupBy(value => (string)value["routeId"]!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group =>
            {
                var route = group.First();
                var origin = (string?)route["originId"] ?? "?";
                var destination = (string?)route["destinationId"] ?? "?";
                var originName = locations.TryGetValue(origin, out var from) ? from : origin;
                var destinationName = locations.TryGetValue(destination, out var to) ? to : destination;
                return originName + " → " + destinationName + " (ID: " + group.Key + ")";
            }, StringComparer.Ordinal);

    private static ManagementActionField[] LeaseTermFields() => new[]
    {
        Field("leaseId", "Lease ID"), Field("deposit", "Deposit", "number", "0", true), Field("initialFee", "Initial fee", "number", "0", true),
        Field("rent", "Rent per installment", "number", "0", true), Field("intervalTicks", "Rent interval", "number", "10", true),
        Field("durationTicks", "Duration", "number", "100", true), Field("purchaseOptionPrice", "Purchase option", "number"),
        Field("conditionAtStart", "Initial condition", "number", "1", true), Field("maximumDamageCharge", "Maximum damage charge", "number", "0", true)
    };
}

internal sealed class ManagementAuthoritativeWebIntentExecutor : IAuthoritativeWebIntentExecutor
{
    private readonly ManagementAuthorityGateway gateway;
    public ManagementAuthoritativeWebIntentExecutor(IManagementAuthoritativePort port) => gateway = new ManagementAuthorityGateway(port);
    public WebIntentResult Execute(string authenticatedPrincipal, WebIntentEnvelope envelope)
    {
        var payload = JsonConvert.DeserializeObject<Dictionary<string, object>>(envelope.PayloadJson) ?? new Dictionary<string, object>();
        var result = gateway.Execute(new ManagementAuthorityRequest { Principal = authenticatedPrincipal, IntentType = envelope.IntentType, CorrelationId = envelope.CorrelationId, IdempotencyKey = envelope.IdempotencyKey, ExpectedVersion = envelope.ExpectedVersion, Payload = payload });
        if (!Enum.TryParse(result.State, true, out WebIntentState state)) state = WebIntentState.Refused;
        return new WebIntentResult { State = state, Code = result.Code, CorrelationId = envelope.CorrelationId, ResultJson = JsonConvert.SerializeObject(result.Data) };
    }
}
