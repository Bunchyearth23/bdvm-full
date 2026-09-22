using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BDVM.Management;
using Newtonsoft.Json.Linq;

namespace BDVM.Adapters;

// Presentation only: source is a detached, actor-filtered snapshot. No Unity access.
internal sealed class InGameManagementView
{
    public readonly Dictionary<string, string[]> Rows = new Dictionary<string, string[]>();
    public ManagementActionDescriptor[] Actions = Array.Empty<ManagementActionDescriptor>();
    public string WalletSummary = "";
    public bool CanGrantHostMoney;
    public string[] Areas = Array.Empty<string>();
    public string[] AreaTitles = Array.Empty<string>();
    public readonly Dictionary<ManagementActionDescriptor, string> ActionKeys = new Dictionary<ManagementActionDescriptor, string>();
}

internal static class InGameManagementModel
{
    private static readonly string[] BasicAreas = { "wallets", "companies", "market", "fleet", "deliveries", "industry", "contracts", "assignments", "maintenance" };
    public static string Title(string area) => area == "wallets" ? "Finances" : area == "deliveries" ? "Deliveries" : Humanize(area);
    public static string Humanize(string value) => Regex.Replace(value ?? "", "(?<=[a-z])(?=[A-Z])", " ").Replace("_", " ");
    private static JArray Items(JToken? value) => value as JArray ?? new JArray();
    private static string Text(JToken? value) => (string?)value ?? "";
    private static Dictionary<string, string> Labels(JToken? rows, string key, string label) =>
        Items(rows).Where(x => Text(x[key]).Length > 0).GroupBy(x => Text(x[key])).ToDictionary(x => x.Key, x => Text(x.First()[label]));
    private static string Label(Dictionary<string, string> names, string id) => names.TryGetValue(id, out var name) ? name : Humanize(id);
    public static bool IsPlayerAction(ManagementActionDescriptor action)
    {
        var command = RuntimeManagementPort.ResolveAction(action.IntentType, action.Payload.TryGetValue("action", out var raw) ? Convert.ToString(raw) : null);
        var operation = action.Payload.TryGetValue("operation", out var op) ? Convert.ToString(op) : "";
        if (command == "market.configure" || command == "market.generate-order") return false;
        if (command == "industry.manage") return operation == "start-manual" || operation == "activate" || operation == "cancel" || operation == "reconcile-delivery";
        if (command == "finance.manage") return operation == "accept" || operation == "draw" || operation == "repay";
        if (command == "assignment.manage" && (operation == "passenger-configure-route" || operation == "passenger-refresh-route")) return false;
        return command.StartsWith("company.", StringComparison.Ordinal) || command.StartsWith("fleet.", StringComparison.Ordinal) ||
            command == "wallet.transfer" || command == "market.purchase" || command.StartsWith("initial-delivery.", StringComparison.Ordinal) ||
            command == "assignment.manage" || command == "assignment.cancel" || command == "yard.manage";
    }

    public static InGameManagementView Build(JObject source, bool remote)
    {
        var web = RuntimeManagementPort.ProjectSnapshot(source, "in-game");
        var view = new InGameManagementView { CanGrantHostMoney = !remote };
        var areas = BasicAreas.ToList();
        foreach (var area in new[] { "passengers", "financing", "yardPlans" })
            if (web.FeatureFlags.TryGetValue(area, out var enabled) && enabled && !(remote && area == "financing")) areas.Add(area);
        view.Areas = areas.ToArray();
        var names = Labels(source["locationChoices"], "id", "name");
        foreach (var pair in Labels(source["cargoChoices"], "id", "name")) names[pair.Key] = pair.Value;
        foreach (var pair in Labels(source["fleet"], "assetId", "displayName")) names[pair.Key] = pair.Value;
        foreach (var pair in Labels(source["playerChoices"], "id", "name")) names[pair.Key] = pair.Value;
        var collections = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object>>> {
            ["wallets"] = web.Wallets, ["companies"] = web.Companies, ["market"] = web.Market,
            ["fleet"] = web.Fleet, ["deliveries"] = web.Deliveries, ["industry"] = web.Industry,
            ["contracts"] = web.Contracts, ["assignments"] = web.Assignments, ["maintenance"] = web.Maintenance,
            ["financing"] = web.Financing, ["passengers"] = web.Passengers, ["yardPlans"] = web.YardPlans
        };
        foreach (var pair in collections) view.Rows[pair.Key] = pair.Value.Select(row => PresentRow(row, names)).ToArray();
        view.WalletSummary = string.Join("     |     ", view.Rows["wallets"].Select(row => row.Replace("\n", "  ")));
        var actions = web.Actions.Where(IsPlayerAction).Where(x => areas.Contains(x.Area)).ToList();
        var players = Labels(source["playerChoices"], "id", "name");
        foreach (var action in actions)
        {
            action.Fields = action.Fields.Select(field => field.Name == "targetPlayerId" || field.Name == "memberId"
                ? new ManagementActionField { Name = field.Name, Label = field.Name == "memberId" ? "Member" : "Player", Kind = "select",
                    Required = true, Options = field.Options.Count > 0 ? field.Options : players.Keys.ToArray(), OptionLabels = players }
                : field).ToArray();
        }
        var actor = Text(source["authorityActor"]);
        var company = Items(source["companies"]).FirstOrDefault(x => Items(x["members"]).Any(m => Text(m) == actor));
        if (company != null)
        {
            actions.Add(Action("wallets", "Deposit into company", "wallet.transfer", new JObject { ["toCompany"] = true }, Field("amount", "Amount", "number")));
            actions.Add(Action("wallets", "Withdraw from company", "wallet.transfer", new JObject { ["toCompany"] = false }, Field("amount", "Amount", "number")));
        }
        AddCargoActions(source, names, actions);
        AddDossierActions(source, names, actions);
        view.Actions = actions.ToArray();
        view.AreaTitles = view.Areas.Select(Title).ToArray();
        foreach (var action in view.Actions) view.ActionKeys[action] = Key(action);
        return view;
    }

    private static string PresentRow(IReadOnlyDictionary<string, object> row, Dictionary<string, string> names)
    {
        var result = new List<string>();
        foreach (var pair in row)
        {
            var key = pair.Key;
            if (key == "technicalDetails" || key == "version" || key == "resultCode" || key == "fingerprint" ||
                key == "commandId" || key == "requestId" || key == "contractId" || key == "dossierId" || key == "assignmentId" ||
                key == "sessionId" || key == "planId" || key == "policyId" || key == "recipeId") continue;
            var value = pair.Value is JToken token ? token : pair.Value == null ? JValue.CreateNull() : JToken.FromObject(pair.Value);
            if (value.Type == JTokenType.Null) continue;
            string Show(JToken v) => v is JValue ? Label(names, v.ToString()) :
                v is JArray items ? string.Join(", ", items.Select(Show)) :
                v is JObject obj ? string.Join(", ", obj.Properties().Where(p => p.Name != "version").Select(p => Humanize(p.Name) + ": " + Show(p.Value))) : "";
            result.Add(Humanize(key.EndsWith("Id", StringComparison.Ordinal) ? key.Substring(0, key.Length - 2) : key) + ": " + Show(value));
        }
        return string.Join("\n", result);
    }

    private static ManagementActionField Field(string name, string label, string kind = "text") =>
        new ManagementActionField { Name = name, Label = label, Kind = kind, Required = true };
    private static ManagementActionField Choices(string name, string label, IEnumerable<string> ids, Dictionary<string, string> names, bool multi = false) =>
        new ManagementActionField { Name = name, Label = label, Kind = multi ? "multiselect" : "select", Required = true, Options = ids.Distinct().ToArray(), OptionLabels = names };
    private static ManagementActionDescriptor Action(string area, string label, string command, JObject payload, params ManagementActionField[] fields)
    {
        payload["action"] = command;
        return new ManagementActionDescriptor { Area = area, Label = label, IntentType = "bdvm.management.intent.v1",
            Payload = payload.ToObject<Dictionary<string, object>>()!, Fields = fields,
            Confirmation = command == "fleet.set-tag" ? "" : "Confirm: " + label + "?" };
    }

    private static void AddCargoActions(JObject source, Dictionary<string, string> names, List<ManagementActionDescriptor> actions)
    {
        var tags = Items(source["rollingStockTags"]).ToDictionary(x => Text(x["assetId"]));
        foreach (var wagon in Items(source["fleet"]))
        {
            var id = Text(wagon["assetId"]);
            if (!tags.TryGetValue(id, out var tag) || !string.IsNullOrEmpty(Text(tag["blockedReason"]))) continue;
            actions.Add(Action("fleet", "Set availability - " + Label(names, id), "fleet.set-dispatch-state",
                new JObject { ["assetId"] = id, ["expectedVersion"] = wagon["version"]?.DeepClone() ?? new JValue(0) },
                Choices("state", "Availability", new[] { "Available", "Stored", "Maintenance" }, names)));
            if (Text(wagon["kind"]) != "FreightWagon") continue;
            var compatible = Items(tag["compatibleCargoIds"]).Select(Text).ToArray();
            foreach (var site in Items(source["industrial"]?["sites"]))
            {
                var cargos = Items(site["providedCargoIds"]).Select(Text).Intersect(compatible).ToArray();
                if (cargos.Length == 0) continue;
                var origin = Text(site["facilityId"]);
                actions.Add(Action("fleet", "Cargo tag - " + Label(names, id) + " from " + Label(names, origin), "fleet.set-tag",
                    new JObject { ["assetId"] = id, ["expectedVersion"] = wagon["version"]?.DeepClone() ?? new JValue(0), ["sourceFacilityId"] = origin },
                    Choices("cargoId", "Cargo", cargos, names), Choices("tagLifetime", "Keep tag", new[] { "UntilEmpty", "Permanent" }, names)));
            }
        }
    }

    internal static string[] EligibleWagons(JObject source, string origin, string cargo, bool company)
    {
        var allowed = new HashSet<string>(Items(source["industrial"]?[company ? "pilotCompanyWagons" : "pilotPersonalWagons"]).Select(Text));
        var tags = Items(source["rollingStockTags"]).ToDictionary(x => Text(x["assetId"]));
        return Items(source["fleet"]).Where(w => {
            if (!allowed.Contains(Text(w["assetId"])) || Text(w["kind"]) != "FreightWagon" || Text(w["state"]) != "Available" ||
                !tags.TryGetValue(Text(w["assetId"]), out var tag)) return false;
            return string.IsNullOrEmpty(Text(tag["dossierId"])) && (bool?)tag["physicallyPresent"] != false &&
                Text(tag["sourceFacilityId"]) == origin && Text(tag["cargoId"]) == cargo &&
                (string.IsNullOrEmpty(Text(tag["loadedCargoId"])) || Text(tag["loadedCargoId"]) == cargo);
        }).Select(w => Text(w["assetId"])).ToArray();
    }

    private static void AddDossierActions(JObject source, Dictionary<string, string> names, List<ManagementActionDescriptor> actions)
    {
        if ((bool?)source["industrial"]?["enabled"] != true) return;
        foreach (var route in Items(source["industrial"]?["routes"]))
            foreach (var cargo in Items(route["cargoIds"]).Select(Text))
                foreach (var company in new[] { false, true })
                {
                    var origin = Text(route["originFacilityId"]); var destination = Text(route["destinationFacilityId"]);
                    var ids = EligibleWagons(source, origin, cargo, company);
                    if (ids.Length == 0) continue;
                    var labels = new Dictionary<string, string>(names);
                    foreach (var id in ids)
                    {
                        var tag = Items(source["rollingStockTags"]).First(t => Text(t["assetId"]) == id);
                        var wagon = Items(source["fleet"]).First(w => Text(w["assetId"]) == id);
                        labels[id] = Label(names, id) + " | " + Text(wagon["lastKnownLocation"]) + " | aboard " + tag["loadedCargoAmount"] + " / " + tag["capacity"] + " | " + (Text(wagon["carGuid"]).Length > 0 ? Text(wagon["carGuid"]) : id);
                    }
                    actions.Add(Action("contracts", (company ? "Company" : "Personal") + " transport: " + Label(names, cargo) + " | " + Label(names, origin) + " > " + Label(names, destination),
                        "industry.manage", new JObject { ["operation"] = "start-manual", ["originFacilityId"] = origin, ["destinationFacilityId"] = destination, ["cargoId"] = cargo, ["forCompany"] = company },
                        Choices("assetIds", "Tagged wagons (matching loaded cargo is accepted)", ids, labels, true), Field("quantity", "Planned quantity", "number")));
                }
        foreach (var dossier in Items(source["industrial"]?["contracts"]))
        {
            var state = Text(dossier["state"]);
            if (state == "Completed" || state == "Cancelled" || state == "Expired") continue;
            var id = Text(dossier["contractId"]);
            var name = Text(dossier["displayName"]);
            if (name.Length == 0) name = Label(names, Text(dossier["originFacilityId"])) + " > " + Label(names, Text(dossier["destinationFacilityId"])) + " / " + Label(names, Text(dossier["cargoId"]));
            foreach (var operation in new[] { "reconcile-delivery", "cancel" })
                actions.Add(Action("contracts", (operation == "cancel" ? "Cancel - " : "Check unloading and payment - ") + name, "industry.manage",
                    new JObject { ["operation"] = operation, ["contractId"] = id }));
        }
    }

    public static string Key(ManagementActionDescriptor action) => action.IntentType + "|" + JObject.FromObject(action.Payload).ToString(Newtonsoft.Json.Formatting.None) + "|" + action.Label;
    public static JObject BuildCommand(ManagementActionDescriptor action, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, HashSet<string>> selected)
    {
        if (!IsPlayerAction(action)) throw new InvalidOperationException("This action is not available in Management.");
        var body = JObject.FromObject(action.Payload);
        body["action"] = RuntimeManagementPort.ResolveAction(action.IntentType, (string?)body["action"]);
        foreach (var field in action.Fields)
        {
            values.TryGetValue(field.Name, out var value); value = (value ?? field.Value ?? "").Trim();
            if (field.Kind == "multiselect")
            {
                selected.TryGetValue(field.Name, out var ids);
                var chosen = ids?.ToArray() ?? System.Array.Empty<string>();
                if ((field.Required && chosen.Length == 0) || chosen.Any(id => !field.Options.Contains(id))) throw new ArgumentException("Choose eligible " + field.Label.ToLowerInvariant() + ".");
                body[field.Name] = new JArray(chosen);
            }
            else if (field.Kind == "checkbox") body[field.Name] = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            else
            {
                if (field.Required && value.Length == 0) throw new ArgumentException("Enter " + field.Label.ToLowerInvariant() + ".");
                if (field.Kind == "select" && !field.Options.Contains(value)) throw new ArgumentException("Choose " + field.Label.ToLowerInvariant() + ".");
                if (field.Kind == "number")
                {
                    if (value.Length == 0) { body[field.Name] = JValue.CreateNull(); continue; }
                    if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)) throw new ArgumentException("Enter a valid " + field.Label.ToLowerInvariant() + ".");
                    if ((field.Name == "quantity" || field.Name == "amount") && (number <= 0 || decimal.Truncate(number) != number)) throw new ArgumentException("Enter a positive whole " + field.Label.ToLowerInvariant() + ".");
                    body[field.Name] = number;
                }
                else if (field.Kind == "csv") body[field.Name] = new JArray(value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
                else body[field.Name] = value;
            }
        }
        body.Remove("playerId"); body.Remove("requesterId"); body.Remove("authorityActor");
        body["correlationId"] = Guid.NewGuid().ToString("N");
        if (System.Text.Encoding.UTF8.GetByteCount(body.ToString(Newtonsoft.Json.Formatting.None)) > 4096) throw new ArgumentException("Select fewer vehicles for this action.");
        return body;
    }
}