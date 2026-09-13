using System;
using System.Collections.Generic;
using System.Globalization;
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
        var view = new ManagementWebSnapshot
        {
            Version = HighestVersion(source), CorrelationId = correlationId, FeatureFlags = Features(source),
            Companies = Rows(source["companies"]), Wallets = Rows(source["wallets"]), Fleet = Rows(source["fleet"]),
            Market = MarketRows(source), Deliveries = Rows(source["initialDeliveries"]),
            Leases = Array.Empty<IReadOnlyDictionary<string, object>>(),
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
        PresentPlayerRows(view, source);
        return view;
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

    private static string ModelName(JObject source, string id) =>
        (string?)(source["catalogCandidates"] as JArray)?.FirstOrDefault(value => (string?)value["definitionId"] == id)?["displayName"] ??
        System.Text.RegularExpressions.Regex.Replace(id, "(?<=[a-z])(?=[A-Z])", " ");

    private static string TransportName(JObject source, JToken row)
    {
        var locations = Labels(source["locationChoices"], "id", "name");
        var cargos = Labels(source["cargoChoices"], "id", "name");
        string Label(string key, IReadOnlyDictionary<string, string> labels) { var id = (string?)row[key] ?? ""; return labels.TryGetValue(id, out var name) ? name : id; }
        return Label("originFacilityId", locations) + " → " + Label("destinationFacilityId", locations) + " · " +
            Label("cargoId", cargos) + " · " + ((decimal?)row["quantity"] ?? 0) + " units · $" +
            (((long?)row["baseReward"] ?? 0) + ((long?)row["scarcityBonus"] ?? 0)).ToString("N0");
    }

    private static string Money(long value) => value < 0 ? "−$" + Math.Abs(value).ToString("N0", CultureInfo.InvariantCulture) : "$" + value.ToString("N0", CultureInfo.InvariantCulture);
    private static string Percent(decimal ratio) => (ratio * 100m).ToString("0") + "%";

    private static void PresentPlayerRows(ManagementWebSnapshot view, JObject source)
    {
        var companies = (source["companies"] as JArray ?? new JArray()).ToDictionary(value => (string)value["companyId"]!, value => (string)value["name"]!);
        string Person(string id) => id == (string?)source["authorityActor"] ? "You" : "Player (" + id.Substring(0, Math.Min(8, id.Length)) + ")";
        string Account(string id) => id.StartsWith("Company:", StringComparison.Ordinal) && companies.TryGetValue(id.Substring(8), out var name) ? name : id.StartsWith("Player:", StringComparison.Ordinal) ? Person(id.Substring(7)) : id;
        view.Companies = (source["companies"] as JArray ?? new JArray()).Select(row => (IReadOnlyDictionary<string, object>)new Dictionary<string, object> {
            ["name"] = (string?)row["name"] ?? "Company", ["leader"] = Person((string?)row["leaderId"] ?? ""),
            ["members"] = string.Join(", ", (row["members"] as JArray ?? new JArray()).Select(value => Person((string?)value ?? ""))),
            ["membership"] = (string?)row["membershipPolicy"] == "ApplicationWithApproval" ? "Application with approval" : (string?)row["membershipPolicy"] ?? "",
            ["technicalDetails"] = row.ToObject<Dictionary<string, object>>()!
        }).ToArray();
        view.Wallets = (source["wallets"] as JArray ?? new JArray()).Select(row => (IReadOnlyDictionary<string, object>)new Dictionary<string, object> {
            ["account"] = Account((string?)row["account"] ?? ""), ["balance"] = "$" + ((long?)row["balance"] ?? 0).ToString("N0"),
            ["technicalDetails"] = row.ToObject<Dictionary<string, object>>()!
        }).ToArray();
        view.Fleet = (source["fleet"] as JArray ?? new JArray()).Select(row => (IReadOnlyDictionary<string, object>)new Dictionary<string, object> {
            ["name"] = (string?)row["displayName"] == (string?)row["definitionId"] ? ModelName(source, (string?)row["definitionId"] ?? "") : (string?)row["displayName"] ?? "Rolling stock",
            ["model"] = ModelName(source, (string?)row["definitionId"] ?? ""), ["kind"] = (string?)row["kind"] ?? "Unknown",
            ["availability"] = (string?)row["state"] == "Stored" ? "Stored — make available in Fleet to use for work" : (string?)row["state"] ?? "Unknown",
            ["owner"] = Account((string?)row["owner"] ?? ""), ["track"] = (string?)row["lastKnownLocation"] ?? "Not currently located",
            ["technicalDetails"] = row.ToObject<Dictionary<string, object>>()!
        }).ToArray();
        view.Market = (source["market"] as JArray ?? new JArray()).Where(row => (string?)row["state"] == "Available")
            .Select(row => (IReadOnlyDictionary<string, object>)new Dictionary<string, object> {
                ["model"] = ModelName(source, (string?)row["definitionId"] ?? ""),
                ["price"] = "$" + ((long?)row["price"] ?? 0).ToString("N0"),
                ["delivery"] = (string?)row["locationName"] ?? (string?)row["locationId"] ?? "Depot",
                ["state"] = "Available to buy", ["technicalDetails"] = row.ToObject<Dictionary<string, object>>()!
            }).ToArray();
        view.Deliveries = (source["initialDeliveries"] as JArray ?? new JArray()).Select(row => (IReadOnlyDictionary<string, object>)new Dictionary<string, object> {
            ["rollingStock"] = string.Join(", ", (row["definitionIds"] as JArray ?? new JArray()).Select(value => ModelName(source, (string?)value ?? ""))),
            ["state"] = (string?)row["state"] ?? "", ["owner"] = Account((string?)row["owner"] ?? ""),
            ["placement"] = (string?)row["state"] == "Available" ? "Use the BDVM delivery radio: Start, choose vehicle, aim, choose direction, confirm." : (string?)row["targetTrackId"] ?? "Pending",
            ["technicalDetails"] = row.ToObject<Dictionary<string, object>>()!
        }).ToArray();
        view.Industry = IndustrySiteRows(source);
        var assets = (source["fleet"] as JArray ?? new JArray()).ToDictionary(row => (string)row["assetId"]!, row => (string?)row["displayName"] ?? ModelName(source, (string?)row["definitionId"] ?? ""));
        string Vehicles(JToken? ids) => string.Join(", ", (ids as JArray ?? new JArray()).Select(id => assets.TryGetValue((string?)id ?? "", out var name) ? name : "Rolling stock"));
        view.Leases = (source["leases"] as JArray ?? new JArray()).Select(row => (IReadOnlyDictionary<string, object>)new Dictionary<string, object> {
            ["vehicles"] = Vehicles(row["assetIds"]), ["state"] = (string?)row["state"] ?? "", ["deposit"] = row["deposit"]?.ToString() ?? "0",
            ["rentPerInstallment"] = row["rentAmount"]?.ToString() ?? "0", ["technicalDetails"] = row.ToObject<Dictionary<string, object>>()!
        }).Concat(view.Leases.Where(row => !row.ContainsKey("leaseId"))).ToArray();
        view.Maintenance = (source["operatingCosts"] as JArray ?? new JArray()).Select(row => (IReadOnlyDictionary<string, object>)new Dictionary<string, object> {
            ["vehicle"] = assets.TryGetValue((string?)row["assetId"] ?? "", out var name) ? name : "Rolling stock",
            ["work"] = (string?)row["action"] ?? "Service", ["state"] = (string?)row["state"] ?? "",
            ["payer"] = Account((string?)row["payer"] ?? ""), ["authorizedBudget"] = row["maximumAuthorizedCost"]?.ToString() ?? "0",
            ["actualCost"] = row["actualCost"]?.ToString() ?? "0", ["technicalDetails"] = row.ToObject<Dictionary<string, object>>()!
        }).ToArray();
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object>> IndustrySiteRows(JObject source)
    {
        var industrial = source["industrial"] as JObject ?? new JObject();
        var stocks = industrial["stocks"] as JArray ?? new JArray();
        var recipes = industrial["recipes"] as JArray ?? new JArray();
        var policies = industrial["policies"] as JArray ?? new JArray();
        var sites = industrial["sites"] as JArray ?? new JArray();
        var facilityIds = sites.Select(row => (string?)row["facilityId"] ?? "")
            .Concat(stocks.Concat(recipes).Select(row => (string?)row["facilityId"] ?? ""))
            .Concat(policies.SelectMany(row => new[] { (string?)row["originFacilityId"] ?? "", (string?)row["destinationFacilityId"] ?? "" }))
            .Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal);
        return facilityIds.Select(facilityId =>
        {
            var siteStocks = stocks.Where(row => (string?)row["facilityId"] == facilityId).OrderBy(row => (string?)row["cargoId"], StringComparer.Ordinal).ToArray();
            var siteRecipes = recipes.Where(row => (string?)row["facilityId"] == facilityId).ToArray();
            var supportedCargoIds = sites.Where(row => (string?)row["facilityId"] == facilityId).SelectMany(row => row["supportedCargoIds"] as JArray ?? new JArray())
                .Select(value => (string?)value ?? "").Where(value => value.Length > 0).ToHashSet(StringComparer.Ordinal);
            var supportedCargo = sites.Where(row => (string?)row["facilityId"] == facilityId)
                .SelectMany(row => row["supportedCargoIds"] as JArray ?? new JArray()).Select(value => Cargo((string?)value))
                .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            string Cargo(string? id) => FriendlyCargo(source, id ?? "");
            string Stock(JToken row)
            {
                var role = (string?)row["role"] ?? "Storage";
                var production = (string?)row["productionState"] ?? "";
                var suffix = role == "Storage" && production == "NotProducedHere" ? "" : " · " + role + (string.IsNullOrWhiteSpace(production) ? "" : " · " + production);
                return Cargo((string?)row["cargoId"]) + " — " + ((decimal?)row["onHand"] ?? 0m).ToString("0.##") + " / " + ((decimal?)row["capacity"] ?? 0m).ToString("0.##") + suffix;
            }
            // A recipe cadence is an internal simulation detail.  Management
            // lists the cargo at unit level so it never looks like a player is
            // required to haul a fixed batch.
            string Flow(JToken row, string cargoField) => Cargo((string?)row[cargoField]);
            string PolicyFlow(JToken row, bool outgoing)
            {
                var otherFacility = (string?)row[outgoing ? "destinationFacilityId" : "originFacilityId"] ?? "";
                return Cargo((string?)row["cargoId"]) + (outgoing ? " → " : " ← ") + FriendlyLocation(source, otherFacility);
            }
            return (IReadOnlyDictionary<string, object>)new Dictionary<string, object>
            {
                ["site"] = FriendlyLocation(source, facilityId),
                ["role"] = CanonicalIndustryFlows.Roles.TryGetValue(facilityId, out var role) ? role : "Warehouse · local transfer and storage",
                ["inputs"] = siteRecipes.Where(row => !string.IsNullOrWhiteSpace((string?)row["inputCargoId"])).Select(row => Flow(row, "inputCargoId"))
                    .Concat(policies.Where(row => (string?)row["destinationFacilityId"] == facilityId).Select(row => PolicyFlow(row, false)))
                    .Concat(CanonicalIndustryFlows.Inputs.TryGetValue(facilityId, out var inputs) ? inputs.Where(supportedCargoIds.Contains).Select(Cargo) : Array.Empty<string>())
                    .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                ["outputs"] = siteRecipes.Where(row => !string.IsNullOrWhiteSpace((string?)row["outputCargoId"])).Select(row => Flow(row, "outputCargoId"))
                    .Concat(policies.Where(row => (string?)row["originFacilityId"] == facilityId).Select(row => PolicyFlow(row, true)))
                    .Concat(CanonicalIndustryFlows.Outputs.TryGetValue(facilityId, out var outputs) ? outputs.Where(supportedCargoIds.Contains).Select(Cargo) : Array.Empty<string>())
                    .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                ["supportedCargo"] = supportedCargo,
                ["stocks"] = siteStocks.Length == 0 ? new[] { "Not tracked — no BDVM stock or capacity is configured." } : siteStocks.Select(Stock).ToArray()
            };
        }).ToArray();
    }

    private static string FriendlyLocation(JObject source, string id) => Labels(source["locationChoices"], "id", "name").TryGetValue(id, out var value) ? value : id;
    private static string FriendlyCargo(JObject source, string id) => Labels(source["cargoChoices"], "id", "name").TryGetValue(id, out var value) ? value : id;

    private static string ResolveAction(string intent, string? requested)
    {
        if (intent == "bdvm.management.intent.v1" || intent == "bdvm.management.company-governance.v1") return requested ?? "";
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bdvm.management.company-dissolve.v1"] = "company.dissolve", ["bdvm.management.wallet-transfer.v1"] = "wallet.transfer",
            ["bdvm.management.fleet-manage.v1"] = requested ?? "fleet.set-state", ["bdvm.management.fleet.rename.v1"] = "fleet.rename",
            ["bdvm.management.fleet-bundle.v1"] = "fleet.bundle", ["bdvm.management.fleet-resale.v1"] = "fleet.resale",
            ["bdvm.management.fleet-maintenance.v1"] = "fleet.maintenance", ["bdvm.management.market.purchase.v1"] = "market.purchase",
            ["bdvm.management.initial-delivery.v1"] = requested ?? "initial-delivery.place",
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
        var actorId = (string?)source["authorityActor"] ?? "";
        var companyRows = (source["companies"] as JArray ?? new JArray()).OfType<JObject>().ToArray();
        var membershipRequests = (source["membershipRequests"] as JArray ?? new JArray()).OfType<JObject>().ToArray();
        var currentCompany = companyRows.SingleOrDefault(company =>
            (company["members"] as JArray ?? new JArray()).Any(member => string.Equals((string?)member, actorId, StringComparison.Ordinal)));
        var locationLabels = Labels(source["locationChoices"], "id", "name");
        var cargoLabels = Labels(source["cargoChoices"], "id", "name");
        var fleetLabels = (source["fleet"] as JArray ?? new JArray()).OfType<JObject>()
            .Where(value => !string.IsNullOrWhiteSpace((string?)value["assetId"]))
            .GroupBy(value => (string)value["assetId"]!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => FleetLabel(group.First()), StringComparer.Ordinal);
        var fleetDefinitionLabels = (source["fleet"] as JArray ?? new JArray()).OfType<JObject>()
            .Where(value => string.Equals((string?)value["kind"], "FreightWagon", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace((string?)value["definitionId"]))
            .GroupBy(value => (string)value["definitionId"]!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => ModelName(source, group.Key), StringComparer.Ordinal);
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
            var state = (string?)grant["state"] ?? "";
            var grantId = (string?)grant["grantId"] ?? "delivery";
            var rollingStock = string.Join(", ", (grant["definitionIds"] as JArray ?? new JArray()).Select(value => ModelName(source, (string?)value ?? "")));
            if (state == "Available")
                foreach (var track in deliveryTracks)
                    actions.Add(Action("deliveries", "Deliver " + rollingStock + " to " + track.TrackId, "bdvm.management.initial-delivery.v1",
                        new Dictionary<string, object> { ["action"] = "initial-delivery.place", ["grantId"] = grantId, ["trackId"] = track.TrackId, ["targetKind"] = track.Kind },
                        "Confirm physical placement on this host-approved track."));
            if (state == "PlacementPending" || state == "ReconcileRequired")
                actions.Add(Action("deliveries", "Reconcile physical delivery — " + rollingStock, "bdvm.management.initial-delivery.v1",
                    new Dictionary<string, object> { ["action"] = "initial-delivery.reconcile", ["grantId"] = grantId },
                    "BDVM will inspect the original target track and match the exact physical vehicle before changing state."));
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
                Field("basePrice", "Base purchase price", "number", "10", true),
                Field("transferFee", "Transfer fee", "number", "0", true),
                Field("buybackRate", "Perfect-condition buyback rate", "number", "0.5", true),
                Field("initialStock", "Initial finite stock", "number", "10", true)));

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
            actions.Add(Action("market", "Buy " + ModelName(source, (string?)listing["definitionId"] ?? "") + " — $" + ((long?)listing["price"] ?? 0).ToString("N0"), "bdvm.management.market.purchase.v1",
                new Dictionary<string, object> { ["listingId"] = listingId }, "Confirm this purchase.", Field("forCompany", "Company pays", "checkbox")));
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
        actions.Add(Action("passengers", "Open a passenger route", "bdvm.management.assignment-manage.v1",
            new Dictionary<string, object> { ["action"] = "assignment.manage", ["operation"] = "passenger-configure-route",
                ["initialDemand"] = 20, ["maximumDemand"] = 100, ["growthPerCycle"] = 5, ["frequencyTicks"] = 100, ["baseFare"] = 100, ["latePenalty"] = 1 },
            "Open this route using the host's standard demand and fare settings.",
            ChoiceField("originId", "Departure station", passengerLocations, locationLabels),
            ChoiceField("destinationId", "Arrival station", passengerLocations, locationLabels)));
        actions.Add(Action("passengers", "Configure passenger route", "bdvm.management.assignment-manage.v1", new Dictionary<string, object> { ["action"] = "assignment.manage", ["operation"] = "passenger-configure-route" }, "",
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

        actions.Add(Action("industry", "Configure industrial stock", "bdvm.management.industry-manage.v1", new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "configure-stock" }, "", ChoiceField("facilityId", "Station", passengerLocations, locationLabels), ChoiceField("cargoId", "Cargo", cargoLabels.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray(), cargoLabels), Field("onHand", "Quantity currently available", "number", "0", true), Field("capacity", "Maximum storage capacity", "number", "100", true)));
        actions.Add(Action("industry", "Configure production recipe", "bdvm.management.industry-manage.v1", new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "configure-recipe" }, "", Field("recipeId", "Internal recipe name", "text", "production-1", true), ChoiceField("facilityId", "Producing station", passengerLocations, locationLabels), ChoiceField("inputCargoId", "Cargo consumed", cargoLabels.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray(), cargoLabels), Field("inputQuantity", "Quantity consumed", "number", "10", true), ChoiceField("outputCargoId", "Cargo produced", cargoLabels.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray(), cargoLabels), Field("outputQuantity", "Quantity produced", "number", "10", true), Field("cadenceTicks", "Production interval", "number", "60", true), Field("maximumBacklogCycles", "Maximum stored production cycles", "number", "4", true)));
        actions.Add(Action("industry", "Configure stock flow", "bdvm.management.industry-manage.v1", new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "configure-policy", ["offerLifetimeTicks"] = 600, ["preparationDurationTicks"] = 600, ["preparationPenalty"] = 0 }, "This defines a permanent stock relationship, not an offer or contract.", Field("policyId", "Internal flow name", "text", "stock-flow-1", true), ChoiceField("originFacilityId", "Cargo pickup company", passengerLocations, locationLabels), ChoiceField("destinationFacilityId", "Cargo receiving company", passengerLocations, locationLabels), ChoiceField("cargoId", "Cargo", cargoLabels.Keys.OrderBy(value => value, StringComparer.Ordinal).ToArray(), cargoLabels), Field("batchQuantity", "Maximum quantity per movement", "number", "30", true), Field("destinationTargetQuantity", "Desired destination stock", "number", "60", true), Field("baseReward", "Reference payment before stock adjustment", "number", "15000", true), Field("maximumScarcityBonus", "Maximum shortage bonus", "number", "5000", true), Field("estimatedOperatingCost", "Expected fuel, consumables and wear", "number", "8000", true), Field("deliveryDurationTicks", "Expected delivery duration", "number", "3600", true), Field("minimumWagonCount", "Minimum number of wagons", "number", "1", true), Field("minimumTotalCapacity", "Minimum cargo capacity", "number", "30", true), ChoiceField("allowedDefinitionIds", "Allowed wagon models", fleetDefinitionIds, fleetDefinitionLabels, true), Field("enabled", "Expose this live stock flow", "checkbox", "true")));
        foreach (var recipe in source["industrial"]?["recipes"] as JArray ?? new JArray())
            actions.Add(Action("industry", "Advance production for " + ((string?)recipe["recipeId"] ?? "recipe"), "bdvm.management.industry-manage.v1", new Dictionary<string, object> { ["action"] = "industry.manage", ["operation"] = "advance-production", ["recipeId"] = (string?)recipe["recipeId"] ?? "" }));

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

        if (currentCompany == null)
        {
            actions.Add(Action("companies", "Create a company", "bdvm.management.company-governance.v1",
                new Dictionary<string, object> { ["action"] = "company.create" }, "Create this company with you as its leader.",
                Field("name", "Company name", "text", "", true)));

            foreach (var company in companyRows.Where(company => !(bool?)company["liquidating"] ?? false).OrderBy(company => (string?)company["name"], StringComparer.OrdinalIgnoreCase))
            {
                var companyId = (string?)company["companyId"] ?? "";
                var companyName = (string?)company["name"] ?? companyId;
                var policy = (string?)company["membershipPolicy"] ?? "";
                var pending = membershipRequests.Any(request => string.Equals((string?)request["playerId"], actorId, StringComparison.Ordinal) &&
                    string.Equals((string?)request["companyId"], companyId, StringComparison.Ordinal) &&
                    string.Equals((string?)request["kind"], "Application", StringComparison.Ordinal) &&
                    string.Equals((string?)request["state"], "Pending", StringComparison.Ordinal));
                if (policy != "InvitationOnly" && !pending)
                    actions.Add(Action("companies", "Request to join " + companyName, "bdvm.management.company-governance.v1",
                        new Dictionary<string, object> { ["action"] = "company.apply", ["companyId"] = companyId },
                        "Submit an application to this company."));
            }

            foreach (var request in membershipRequests.Where(request =>
                string.Equals((string?)request["playerId"], actorId, StringComparison.Ordinal) &&
                string.Equals((string?)request["kind"], "Invitation", StringComparison.Ordinal) &&
                string.Equals((string?)request["state"], "Pending", StringComparison.Ordinal)))
            {
                var requestId = (string?)request["requestId"] ?? "";
                var company = companyRows.SingleOrDefault(row => string.Equals((string?)row["companyId"], (string?)request["companyId"], StringComparison.Ordinal));
                var companyName = (string?)company?["name"] ?? (string?)request["companyId"] ?? "company";
                actions.Add(Action("companies", "Accept invitation from " + companyName, "bdvm.management.company-governance.v1",
                    new Dictionary<string, object> { ["action"] = "company.respond-invitation", ["requestId"] = requestId, ["accept"] = true }));
                actions.Add(Action("companies", "Decline invitation from " + companyName, "bdvm.management.company-governance.v1",
                    new Dictionary<string, object> { ["action"] = "company.respond-invitation", ["requestId"] = requestId, ["accept"] = false },
                    "Decline this invitation."));
            }
        }
        else
        {
            var companyId = (string?)currentCompany["companyId"] ?? "";
            var companyName = (string?)currentCompany["name"] ?? companyId;
            var members = (currentCompany["members"] as JArray ?? new JArray()).Values<string>()
                .Where(member => !string.IsNullOrWhiteSpace(member) && !string.Equals(member, actorId, StringComparison.Ordinal))
                .Select(member => member!).Distinct(StringComparer.Ordinal).ToArray();
            var isLeader = string.Equals((string?)currentCompany["leaderId"], actorId, StringComparison.Ordinal);
            actions.Add(Action("companies", "Change membership policy — " + companyName, "bdvm.management.company-governance.v1",
                new Dictionary<string, object> { ["action"] = "company.policy", ["companyId"] = companyId }, "",
                ChoiceField("policy", "Membership policy", new[] { "ApplicationWithApproval", "InvitationOnly", "Open" })));
            actions.Add(Action("companies", "Invite a player — " + companyName, "bdvm.management.company-governance.v1",
                new Dictionary<string, object> { ["action"] = "company.invite", ["companyId"] = companyId }, "",
                Field("targetPlayerId", "Target player ID", "text", "", true)));

            foreach (var request in membershipRequests.Where(request =>
                string.Equals((string?)request["companyId"], companyId, StringComparison.Ordinal) &&
                string.Equals((string?)request["kind"], "Application", StringComparison.Ordinal) &&
                string.Equals((string?)request["state"], "Pending", StringComparison.Ordinal)))
            {
                var requestId = (string?)request["requestId"] ?? "";
                var applicant = (string?)request["playerId"] ?? "player";
                actions.Add(Action("companies", "Accept application from " + applicant, "bdvm.management.company-governance.v1",
                    new Dictionary<string, object> { ["action"] = "company.decide-application", ["requestId"] = requestId, ["accept"] = true }));
                actions.Add(Action("companies", "Refuse application from " + applicant, "bdvm.management.company-governance.v1",
                    new Dictionary<string, object> { ["action"] = "company.decide-application", ["requestId"] = requestId, ["accept"] = false },
                    "Refuse this membership application."));
            }

            if (members.Length > 0)
            {
                if (isLeader)
                    actions.Add(Action("companies", "Transfer leadership — " + companyName, "bdvm.management.company-governance.v1",
                        new Dictionary<string, object> { ["action"] = "company.transfer-leadership", ["companyId"] = companyId }, "Transfer leadership to this member.",
                        ChoiceField("memberId", "New leader", members)));
                actions.Add(Action("companies", "Manage member permissions — " + companyName, "bdvm.management.company-governance.v1",
                    new Dictionary<string, object> { ["action"] = "company.permission", ["companyId"] = companyId }, "",
                    ChoiceField("memberId", "Member", members), ChoiceField("permission", "Permission", new[] { "ManageMembers", "ManagePermissions", "ManageFunds", "Dissolve", "ManageFleet" }),
                    Field("enabled", "Granted", "checkbox", "true")));
            }
            if (!isLeader)
                actions.Add(Action("companies", "Leave company — " + companyName, "bdvm.management.company-governance.v1",
                    new Dictionary<string, object> { ["action"] = "company.leave" }, "Leave this company."));
            actions.Add(Action("companies", "Dissolve company", "bdvm.management.company-dissolve.v1", new Dictionary<string, object>(), "This dissolution is permanent."));
        }
        foreach (var vehicle in source["fleet"] as JArray ?? new JArray())
        {
            var id = (string?)vehicle["assetId"] ?? "";
            var label = FleetLabel((JObject)vehicle);
            if ((string?)vehicle["state"] == "Stored")
                actions.Add(Action("fleet", "Make available for work — " + label, "bdvm.management.fleet-manage.v1",
                    new Dictionary<string, object> { ["action"] = "fleet.set-state", ["assetId"] = id, ["state"] = "Available" }));
            actions.Add(Action("fleet", "Rename — " + label, "bdvm.management.fleet.rename.v1",
                new Dictionary<string, object> { ["assetId"] = id }, "", Field("displayName", "Vehicle name", "text", (string?)vehicle["displayName"] ?? "", true)));
            if (currentCompany != null && string.Equals((string?)vehicle["owner"], "Player:" + actorId, StringComparison.Ordinal))
                actions.Add(Action("fleet", "Transfer to company — " + label, "bdvm.management.fleet-manage.v1",
                    new Dictionary<string, object> { ["action"] = "fleet.transfer", ["assetId"] = id },
                    "Transfer this personal vehicle to your company. The company becomes its owner."));
        }
        return actions;
    }

    private static ManagementActionDescriptor Action(string area, string label, string intent, IReadOnlyDictionary<string, object> payload, string confirmation = "", params ManagementActionField[] fields) =>
        new ManagementActionDescriptor { Area = area, Label = label, IntentType = intent, Payload = payload, Confirmation = confirmation, Fields = fields };

    private static ManagementActionField Field(string name, string label, string kind = "text", string value = "", bool required = false, IReadOnlyList<string>? options = null) =>
        new ManagementActionField { Name = name, Label = label, Kind = kind, Value = value, Required = required, Options = options ?? Array.Empty<string>() };

    private static ManagementActionField ChoiceField(string name, string label, IReadOnlyList<string> options, bool multiple = false) =>
        Field(name, label, multiple ? "multiselect" : "select", options.FirstOrDefault() ?? "", true, options);

    private static ManagementActionField ChoiceField(string name, string label, IReadOnlyList<string> options, IReadOnlyDictionary<string, string> optionLabels, bool multiple = false) =>
        new ManagementActionField { Name = name, Label = label, Kind = multiple ? "multiselect" : "select", Value = options.FirstOrDefault() ?? "", Required = true, Options = options, OptionLabels = optionLabels };

    private static Dictionary<string, string> Labels(JToken? source, string idProperty, string nameProperty) =>
        (source as JArray ?? new JArray()).OfType<JObject>()
            .Where(value => !string.IsNullOrWhiteSpace((string?)value[idProperty]))
            .GroupBy(value => (string)value[idProperty]!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group =>
            {
                var name = ((string?)group.First()[nameProperty] ?? group.Key).Trim();
                return name;
            }, StringComparer.Ordinal);

    private static string FleetLabel(JObject value)
    {
        var id = (string?)value["assetId"] ?? "unknown";
        var name = (string?)value["displayName"] ?? (string?)value["definitionId"] ?? "Rolling stock";
        var location = (string?)value["lastKnownLocation"] ?? "location unknown";
        return name + " — " + location;
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
