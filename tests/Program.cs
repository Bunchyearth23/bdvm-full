using System;
using System.Linq;
using BDVM.Adapters;

internal static class Program
{
    static int Main()
    {
        SnapshotChecks.Run(); InGameManagementChecks.Run(); InGamePanelChecks.Run();
        var port = new RuntimeManagementPort(_ => @"{
          'authorityActor':'local-player',
          'companies':[{'companyId':'co','name':'Rail Company','leaderId':'local-player','members':['local-player']}],
          'wallets':[{'account':'Player:local-player','balance':2000},{'account':'Company:co','balance':100}],
          'fleet':[{'assetId':'engine','displayName':'BL-01','definitionId':'LocoDE2','kind':'Locomotive','state':'Stored','owner':'Player:local-player','lastKnownLocation':'SM-T12P'},
                   {'assetId':'wagon','displayName':'FlatbedEmpty','definitionId':'FlatbedEmpty','kind':'FreightWagon','state':'Available','owner':'Player:local-player'}],
          'catalogCandidates':[{'definitionId':'LocoDE2','displayName':'DE2'},{'definitionId':'FlatbedEmpty','displayName':'Empty flatbed'}],
          'locationChoices':[{'id':'SM','name':'Steel Mill'},{'id':'HMB','name':'Harbor'}],
          'cargoChoices':[{'id':'Steel','name':'Steel'}],
          'market':[{'listingId':'offer','definitionId':'LocoDE2','state':'Available','price':40000}],
          'initialDeliveries':[{'grantId':'grant-reconcile','definitionIds':['FlatbedEmpty'],'state':'ReconcileRequired','targetTrackId':'SM-A5S','targetKind':'ServiceTrack'}],
          'industrial':{'enabled':true,'sites':[{'facilityId':'SM','supportedCargoIds':['Ore','Steel']},{'facilityId':'HMB','supportedCargoIds':['Steel']},{'facilityId':'FM','supportedCargoIds':['Steel']}], 'stocks':[{'facilityId':'SM','cargoId':'Steel','role':'Output','onHand':75,'capacity':100,'fillRatio':0.75,'currentUnitValue':220,'previousUnitValue':210,'trend':1,'productionState':'FullRate','version':1}], 'recipes':[{'facilityId':'SM','inputCargoId':'Ore','inputQuantity':2,'outputCargoId':'Steel','outputQuantity':1}], 'policies':[{'policyId':'steel-flow','originFacilityId':'SM','destinationFacilityId':'HMB','cargoId':'Steel'}], 'needs':[{'needId':'need','policyId':'steel-flow','originFacilityId':'SM','destinationFacilityId':'HMB','cargoId':'Steel','quantity':30,'currentUnitValue':220,'sourceFillRatio':0.75,'destinationFillRatio':0.2,'state':'LiveStock','version':1}]}
        }", (_, __) => "{}");
        var view = port.ReadSnapshot("local-player", "test");
        Require((string)view.Companies[0]["leader"] == "You", "Company name must never replace the leader identity.");
        Require((string)view.Wallets[1]["account"] == "Rail Company", "Wallet must display company name.");
        Require((string)view.Fleet[0]["model"] == "DE2", "Vehicle nickname must never replace model name.");
        Require((string)view.Fleet[0]["track"] == "SM-T12P", "Live track must be preserved.");
        Require(view.Fleet[0].ContainsKey("technicalDetails"), "Technical identities must remain available.");
        Require(view.Actions.Any(a => a.Label.StartsWith("Buy DE2") && (string)a.Payload["listingId"] == "offer"), "Readable buy action must retain exact listing identity.");
        Require(view.Actions.Any(a => a.Area == "companies" && a.Label.StartsWith("Change membership policy")), "Company members must receive governance controls in Management.");
        Require(view.Actions.Any(a => a.Area == "companies" && a.Label.StartsWith("Invite a player")), "Company members must be able to invite a player in Management.");
        var independent = new RuntimeManagementPort(_ => @"{
          'authorityActor':'independent',
          'companies':[{'companyId':'open-co','name':'Open Railway','leaderId':'leader','members':['leader'],'membershipPolicy':'Open','liquidating':false}]
        }", (_, __) => "{}").ReadSnapshot("independent", "join");
        Require(independent.Actions.Any(a => a.Label == "Create a company" && a.Fields.Any(f => f.Name == "name")), "Independent players must be able to create a company.");
        Require(independent.Actions.Any(a => a.Label == "Request to join Open Railway" && (string)a.Payload["companyId"] == "open-co"), "Independent players must be able to request company membership.");
        Require(!view.Actions.Any(a => a.Area == "industry" && a.Label.StartsWith("Run stock transport")), "Transport dossier creation belongs to Contracts, not the Industry information view.");
        var dossierSource = Newtonsoft.Json.Linq.JObject.Parse(@"{
          'industrial':{'enabled':true,'routes':[{'originFacilityId':'SM','destinationFacilityId':'GF','cargoIds':['Steel']}],
            'contracts':[{'dossierId':'one','quantity':3,'assignedWagons':[{'assetId':'wagon'}]}],
            'pilotPersonalWagons':['wagon'],'pilotCompanyWagons':[]},
          'fleet':[{'assetId':'wagon','carGuid':'exact-guid','kind':'FreightWagon','state':'Available'}],
          'rollingStockTags':[{'assetId':'wagon','sourceFacilityId':'SM','cargoId':'Steel','loadedCargoAmount':1,'capacity':1}]
        }");
        var projected = RuntimeManagementPort.ProjectSnapshot(dossierSource, "dossiers");
        var workspace = Newtonsoft.Json.Linq.JObject.FromObject(projected.IndustrialWorkspace);
        Require((bool)workspace["enabled"]! && (string)workspace["wagons"]![0]!["carGuid"]! == "exact-guid", "Contracts preserve exact physical wagon identities.");
        Require((decimal)workspace["tags"]![0]!["loadedCargoAmount"]! == 1m && workspace["contracts"]!.Count() == 1, "Contracts expose preloaded cargo and independent dossiers.");
        Require(workspace["personalWagons"]!.Count() == 1 && workspace["companyWagons"]!.Count() == 0, "Operator eligibility remains host-provided.");
        dossierSource["rollingStockTags"]![0]!["loadedCargoAmount"] = 0;
        Require((decimal)Newtonsoft.Json.Linq.JObject.FromObject(projected.IndustrialWorkspace)["tags"]![0]!["loadedCargoAmount"]! == 1m, "Contracts workspace owns its data after projection.");
        Require(!view.Actions.Any(a => a.Area == "industry" && (a.Label.Contains("Reserve transport") || a.Label.StartsWith("Publish need") || a.Label.StartsWith("Accept "))), "Stock-driven industry must expose no offer publication, acceptance or reservation action.");
        Require(view.Actions.Any(a => a.Label.StartsWith("Reconcile physical delivery") && (string)a.Payload["grantId"] == "grant-reconcile" && (string)a.Payload["action"] == "initial-delivery.reconcile"), "A pending physical delivery must expose its dedicated reconciliation action.");
        var steelMill = view.Industry.Single(row => (string)row["site"] == "Steel Mill");
        var harbor = view.Industry.Single(row => (string)row["site"] == "Harbor");
        var factory = view.Industry.Single(row => (string)row["site"] == "FM");
        Require(((string[])steelMill["inputs"]).Contains("Ore") && ((string[])steelMill["outputs"]).Contains("Steel") && ((string[])steelMill["outputs"]).Contains("Steel → Harbor") && ((string[])harbor["inputs"]).Contains("Steel ← Steel Mill"), "Industry must group every configured recipe and transport-flow input/output under its site without imposing recipe batch quantities on the player.");
        Require(((string[])steelMill["supportedCargo"]).Contains("Steel") && ((string[])steelMill["supportedCargo"]).Contains("Ore"), "Every warehouse-compatible cargo must remain visible without being misrepresented as an input or output.");
        Require(((string[])factory["stocks"]).Single().Contains("Not tracked"), "A loaded warehouse without BDVM configuration must still be visible.");
        Require(view.Actions.Where(a => a.Area == "industry").SelectMany(a => a.Fields).Where(f => f.Name == "allowedDefinitionIds").All(f => f.Options.SequenceEqual(new[] { "FlatbedEmpty" })), "Locomotives must never be offered as freight wagons.");
        var transferSource = Newtonsoft.Json.Linq.JObject.Parse(@"{
          'authorityActor':'p','companies':[{'companyId':'c','name':'Rail','leaderId':'p','members':['p'],'delegatedPermissions':{}}],
          'fleet':[{'assetId':'personal','owner':'Player:p','state':'Available'},
                   {'assetId':'company','owner':'Company:c','state':'Stored'},
                   {'assetId':'busy','owner':'Company:c','state':'InService'},
                   {'assetId':'other','owner':'Company:other','state':'Available'}]
        }");
        var transfers = RuntimeManagementPort.ProjectSnapshot(transferSource, "transfers");
        Require((string)transfers.Fleet[0]["ownerType"] == "Player" && (string)transfers.Fleet[1]["ownerType"] == "Company" && (string)transfers.Fleet[1]["owner"] == "Rail", "Fleet must distinguish player and company owners by name.");
        Require(transfers.Actions.Any(a => a.Payload.TryGetValue("action", out var action) && (string)action == "fleet.transfer" && (string)a.Payload["assetId"] == "personal" && (string)a.Payload["targetKind"] == "Company"), "Personal assets can transfer to the current company.");
        Require(transfers.Actions.Any(a => a.Payload.TryGetValue("action", out var action) && (string)action == "fleet.transfer" && (string)a.Payload["assetId"] == "company" && (string)a.Payload["targetKind"] == "Player"), "Company leaders can transfer eligible company assets to themselves.");
        Require(!transfers.Actions.Any(a => a.Payload.TryGetValue("action", out var action) && (string)action == "fleet.transfer" && new[] { "busy", "other" }.Contains((string)a.Payload["assetId"])), "Busy and other-company assets must not expose transfers.");
        transferSource["companies"]![0]!["leaderId"] = "leader";
        transfers = RuntimeManagementPort.ProjectSnapshot(transferSource, "member");
        Require(!transfers.Actions.Any(a => a.Payload.TryGetValue("targetKind", out var kind) && (string)kind == "Player"), "Ordinary membership cannot extract company assets.");
        transferSource["companies"]![0]!["delegatedPermissions"]!["p"] = new Newtonsoft.Json.Linq.JArray("ManageFleet");
        transfers = RuntimeManagementPort.ProjectSnapshot(transferSource, "delegate");
        Require(transfers.Actions.Any(a => a.Payload.TryGetValue("targetKind", out var kind) && (string)kind == "Player"), "ManageFleet delegates can transfer eligible company assets.");
        transferSource["companies"]![0]!["liquidating"] = true;
        transfers = RuntimeManagementPort.ProjectSnapshot(transferSource, "liquidating");
        Require(!transfers.Actions.Any(a => a.Payload.TryGetValue("action", out var action) && (string)action == "fleet.transfer"), "Liquidating companies expose no transfers.");
        Console.WriteLine("Management presentation: 28/28 passed"); return 0;
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
