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
            Market = Rows(source["market"], source["catalog"], source["marketStock"]),
            Leases = Rows(source["leases"], source["outboundLeases"]?["contracts"]),
            Assignments = Rows(source["assignments"], source["passengers"]?["contracts"]),
            Financing = Rows(source["financing"]?["contracts"], source["financing"]?["pools"]),
            YardPlans = Rows(source["triageAssistance"]?["plans"]),
            Industry = Rows(source["industrial"]?["stocks"], source["industrial"]?["contracts"]),
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
        foreach (var grant in source["initialDeliveries"] as JArray ?? new JArray())
            actions.Add(Action("market", "Placer gratuitement", "bdvm.management.initial-delivery.v1", new Dictionary<string, object> { ["grantId"] = (string?)grant["grantId"] ?? "" }));
        foreach (var contract in source["industrial"]?["contracts"] as JArray ?? new JArray())
            actions.Add(Action("industry", "Gérer le contrat", "bdvm.management.industry-manage.v1", new Dictionary<string, object> { ["contractId"] = (string?)contract["contractId"] ?? "" }));
        actions.Add(Action("companies", "Dissoudre la compagnie", "bdvm.management.company-dissolve.v1", new Dictionary<string, object>(), "Cette dissolution est définitive."));
        return actions;
    }

    private static ManagementActionDescriptor Action(string area, string label, string intent, IReadOnlyDictionary<string, object> payload, string confirmation = "") =>
        new ManagementActionDescriptor { Area = area, Label = label, IntentType = intent, Payload = payload, Confirmation = confirmation };
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
