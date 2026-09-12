using System;
using System.Linq;
using BDVM.Adapters;

internal static class Program
{
    static int Main()
    {
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
        Require(!view.Actions.Any(a => a.Area == "industry" && a.Label.StartsWith("Run stock transport")), "Transport dossier creation belongs to Dispatch, not the Industry information view.");
        Require(!view.Actions.Any(a => a.Area == "industry" && (a.Label.Contains("Reserve transport") || a.Label.StartsWith("Publish need") || a.Label.StartsWith("Accept "))), "Stock-driven industry must expose no offer publication, acceptance or reservation action.");
        Require(view.Actions.Any(a => a.Label.StartsWith("Reconcile physical delivery") && (string)a.Payload["grantId"] == "grant-reconcile" && (string)a.Payload["action"] == "initial-delivery.reconcile"), "A pending physical delivery must expose its dedicated reconciliation action.");
        var steelMill = view.Industry.Single(row => (string)row["site"] == "Steel Mill");
        var harbor = view.Industry.Single(row => (string)row["site"] == "Harbor");
        var factory = view.Industry.Single(row => (string)row["site"] == "FM");
        Require(((string[])steelMill["inputs"]).Contains("Ore") && ((string[])steelMill["outputs"]).Contains("Steel") && ((string[])steelMill["outputs"]).Contains("Steel → Harbor") && ((string[])harbor["inputs"]).Contains("Steel ← Steel Mill"), "Industry must group every configured recipe and transport-flow input/output under its site without imposing recipe batch quantities on the player.");
        Require(((string[])steelMill["supportedCargo"]).Contains("Steel") && ((string[])steelMill["supportedCargo"]).Contains("Ore"), "Every warehouse-compatible cargo must remain visible without being misrepresented as an input or output.");
        Require(((string[])factory["stocks"]).Single().Contains("Not tracked"), "A loaded warehouse without BDVM configuration must still be visible.");
        Require(view.Actions.Where(a => a.Area == "industry").SelectMany(a => a.Fields).Where(f => f.Name == "allowedDefinitionIds").All(f => f.Options.SequenceEqual(new[] { "FlatbedEmpty" })), "Locomotives must never be offered as freight wagons.");
        Console.WriteLine("Management presentation: 11/11 passed"); return 0;
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
