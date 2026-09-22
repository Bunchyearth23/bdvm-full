using System;
using System.Collections.Generic;
using System.Linq;
using BDVM.Adapters;
using BDVM.Management;
using Newtonsoft.Json.Linq;

internal static class InGameManagementChecks
{
    public static void Run()
    {
        var source = JObject.Parse(@"{
          'authorityActor':'me', 'supportedIntents':['fleet.set-tag'],
          'wallets':[{'account':'Player:me','balance':125000}],
          'companies':[{'companyId':'co','name':'Valley Rail','leaderId':'me','members':['me','friend']}],
          'playerChoices':[{'id':'friend','name':'Alice'}],
          'fleet':[{'assetId':'w','displayName':'Wagon 01','carGuid':'real-wagon','kind':'FreightWagon','state':'Available','owner':'Player:me','version':7,'definitionId':'FlatbedEmpty'}],
          'market':[{'listingId':'buy','state':'Available','definitionId':'LocoDE2','price':40000}],
          'rollingStockTags':[{'assetId':'w','sourceFacilityId':'SM','cargoId':'Steel','physicallyPresent':true,'loadedCargoId':'Steel','loadedCargoAmount':10,'capacity':30,'compatibleCargoIds':['Steel']}],
          'locationChoices':[{'id':'SM','name':'Steel Mill'},{'id':'GF','name':'Goods Factory'}],
          'cargoChoices':[{'id':'Steel','name':'Steel'}],
          'industrial':{'enabled':true,'pilotPersonalWagons':['w'],'pilotCompanyWagons':[],
            'sites':[{'facilityId':'SM','providedCargoIds':['Steel'],'supportedCargoIds':['Steel']}],
            'routes':[{'originFacilityId':'SM','destinationFacilityId':'GF','cargoIds':['Steel']}],
            'contracts':[{'contractId':'c','displayName':'Steel delivery','state':'Active'}]},
          'financing':{'enabled':false}, 'passengers':{'enabled':false}
        }");
        var view = InGameManagementModel.Build(source, true);
        Check(view.WalletSummary.Contains("125") && !view.WalletSummary.Contains("technicalDetails"), "wallet presentation hides technical records");
        Check(!view.Areas.Contains("diagnostics") && !view.Areas.Contains("passengers") && !view.Areas.Contains("financing"), "disabled and debug areas are absent");
        Check(!view.Actions.Any(a => a.Label.StartsWith("Configure") || a.Label.StartsWith("Advance") || a.Label.StartsWith("Add installed")), "administration actions are excluded");
        Check(view.Actions.Any(a => a.Label.StartsWith("Buy")), "buying is available");
        Check(view.Actions.Count(a => a.Area == "wallets") == 2, "personal and company transfers exist");
        var invitation = view.Actions.Single(a => a.Label.StartsWith("Invite"));
        Check(invitation.Fields.Single().OptionLabels["friend"] == "Alice", "invites use player names");
        var tag = view.Actions.Single(a => a.Label.StartsWith("Cargo tag"));
        var values = new Dictionary<string,string> { ["cargoId"]="Steel", ["tagLifetime"]="UntilEmpty" };
        var selections = new Dictionary<string,HashSet<string>>();
        var tagCommand = InGameManagementModel.BuildCommand(tag, values, selections);
        Check((long)tagCommand["expectedVersion"]! == 7 && (string)tagCommand["assetId"]! == "w", "tags retain exact identity and revision");
        Check(InGameManagementModel.EligibleWagons(source, "SM", "Steel", false).SequenceEqual(new[]{"w"}), "matching preloaded wagon remains eligible");
        Check(InGameManagementModel.EligibleWagons(source, "SM", "Steel", true).Length == 0, "company cannot adopt a personal wagon");
        var dossier = view.Actions.Single(a => a.Label.StartsWith("Personal transport"));
        var command = InGameManagementModel.BuildCommand(dossier, new Dictionary<string,string>{{"quantity","10"}},
            new Dictionary<string,HashSet<string>>{{"assetIds",new HashSet<string>{"w"}}});
        Check((string)command["operation"]! == "start-manual" && (string)command["assetIds"]![0]! == "w" && (decimal)command["quantity"]! == 10,
            "dossier submits the supported host command with exact wagons");
        Throws(() => InGameManagementModel.BuildCommand(dossier, new Dictionary<string,string>{{"quantity","10"}},
            new Dictionary<string,HashSet<string>>{{"assetIds",new HashSet<string>{"foreign"}}}), "unoffered wagon rejected");
        Throws(() => InGameManagementModel.BuildCommand(dossier, new Dictionary<string,string>{{"quantity","1.5"}},
            new Dictionary<string,HashSet<string>>{{"assetIds",new HashSet<string>{"w"}}}), "fractional dossier quantity rejected");
        source["rollingStockTags"]![0]!["loadedCargoId"] = "Coal";
        Check(InGameManagementModel.EligibleWagons(source, "SM", "Steel", false).Length == 0, "wrong onboard cargo rejected");
        source["rollingStockTags"]![0]!["loadedCargoId"] = "Steel";
        source["rollingStockTags"]![0]!["dossierId"] = "other";
        Check(InGameManagementModel.EligibleWagons(source, "SM", "Steel", false).Length == 0, "already assigned wagon rejected");
        Check(dossier.Fields.Single(f=>f.Name=="assetIds").Options.Contains("w"), "published view owns its choices independently");
        Check(view.Actions.Any(a => a.Label.StartsWith("Check unloading")) && view.Actions.Any(a => a.Label.StartsWith("Cancel - Steel")), "dossiers can be reconciled and cancelled");
        Throws(() => InGameManagementModel.BuildCommand(new ManagementActionDescriptor { IntentType="bdvm.management.intent.v1",
            Payload=new Dictionary<string,object>{{"action","market.configure"}} }, values, selections), "debug command rejected by presentation model");
        Console.WriteLine("PASS in-game Management: player actions, forms, exact identities, permissions, cargo tags, preloaded dossiers and detached data.");
    }
    private static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    private static void Throws(Action action,string message) { try {action();} catch(ArgumentException){return;} catch(InvalidOperationException){return;} throw new Exception(message); }
}