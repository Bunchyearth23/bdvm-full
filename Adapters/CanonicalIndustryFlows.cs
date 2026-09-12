using System;
using System.Collections.Generic;

namespace BDVM.Adapters;

// The map is shared by the runtime economy and Management.  It deliberately
// models open ends: raw-material sites create cargo without consuming cargo,
// while cities consume compatible cargo without producing cargo.
internal static class CanonicalIndustryFlows
{
    // Cities are terminal consumers.  Keeping this aggregate explicit makes a
    // delivered consumer good leave the player economy instead of circulating
    // indefinitely as a fake output.
    private static readonly string[] ConsumerCargo = new[] { "Diesel", "Gasoline", "NewCars", "CityBuses", "Bread", "DairyProducts", "MeatProducts", "CannedFood", "CatFood", "Furniture", "ClothingObco", "ElectronicsIskar", "ToolsIskar", "ChemicalsIskar", "Eggs", "TemperateFruits", "Vegetables", "Fish", "TropicalFruits", "Medicine" };

    internal static readonly IReadOnlyDictionary<string, string[]> Inputs = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["OR"] = new[] { "CrudeOil", "Methane" },
        ["SW"] = new[] { "Logs", "ScrapWood" },
        ["SM"] = new[] { "Coal", "IronOre", "ScrapMetal", "ScrapContainers", "Argon", "CryoOxygen" },
        ["FF"] = new[] { "Pigs", "Cows", "Poultry", "Sheep", "Goats", "Wheat", "Corn", "SunflowerSeeds", "TemperateFruits", "Vegetables", "Milk", "Eggs", "Flour", "Nitrogen" },
        ["GF"] = new[] { "Cotton", "Wool", "Eggs", "TemperateFruits", "Vegetables", "SteelRolls", "SteelBillets", "SteelSlabs", "SteelBentPlates", "Plywood", "Boards", "WoodChips", "Methane", "CryoHydrogen", "Ammonia", "SodiumHydroxide" },
        ["MF"] = new[] { "SteelRolls", "SteelBillets", "SteelSlabs", "SteelBentPlates", "Boards", "ElectronicsIskar", "ToolsIskar", "ChemicalsIskar", "Acetylene", "CryoOxygen", "Diesel", "Gasoline" },
        ["CP"] = new[] { "Coal" },
        ["HB"] = new[] { "SteelRails", "SteelRolls", "SteelBillets", "SteelSlabs", "SteelBentPlates", "Plywood", "Boards", "Sleepers", "Pipes", "NewCars", "CityBuses", "Trams", "SemiTrailers" },
        ["MB"] = new[] { "Ammunition", "Tanks", "MilitaryCars", "MilitaryTrucks", "MilitarySupplies", "AttackHelicopters", "Missiles", "SpentNuclearFuel" },
        ["HMB"] = new[] { "Biohazard", "SpentNuclearFuel" },
        ["CW"] = ConsumerCargo, ["CS"] = ConsumerCargo, ["CSW"] = ConsumerCargo
    };

    internal static readonly IReadOnlyDictionary<string, string[]> Outputs = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["OWC"] = new[] { "CrudeOil", "Methane" }, ["OWN"] = new[] { "CrudeOil", "Methane" },
        ["CME"] = new[] { "Coal" }, ["CMS"] = new[] { "Coal" }, ["IME"] = new[] { "IronOre" }, ["IMW"] = new[] { "IronOre" },
        ["FRC"] = new[] { "Logs" }, ["FRS"] = new[] { "Logs" }, ["FM"] = new[] { "Pigs", "Cows", "Poultry", "Sheep", "Goats", "Wheat", "Corn", "SunflowerSeeds", "TemperateFruits", "Vegetables", "Milk", "Eggs", "Flour" },
        ["OR"] = new[] { "Diesel", "Gasoline" }, ["SW"] = new[] { "Plywood", "Boards", "WoodChips", "Sleepers" },
        ["SM"] = new[] { "SteelRolls", "SteelBillets", "SteelSlabs", "SteelBentPlates", "SteelRails" },
        ["FF"] = new[] { "Bread", "DairyProducts", "MeatProducts", "CannedFood", "CatFood", "Alcohol" },
        ["GF"] = new[] { "ElectronicsIskar", "ToolsIskar", "ChemicalsIskar", "Furniture", "ClothingObco", "Pipes" }, ["MF"] = new[] { "NewCars", "CityBuses", "Trams", "SemiTrailers", "Tractors", "Excavators", "MiningTrucks", "CraneParts" },
        ["HB"] = new[] { "Acetylene", "Diesel", "Gasoline", "ToolsBrohm", "ToolsAAG", "ToolsNovae", "ToolsTraeg", "ElectronicsKrugmann", "ElectronicsAAG", "ElectronicsNovae", "ElectronicsTraeg", "ChemicalsSperex", "ClothingNeoGamma", "ClothingNovae", "ClothingTraeg", "Medicine", "ImportedNewCars", "TropicalFruits", "Fish", "CryoHydrogen", "Ammonia", "SodiumHydroxide", "Argon", "CryoOxygen", "Nitrogen", "AmmoniumNitrate", "ScrapContainers" },
        ["HMB"] = new[] { "Ammunition", "MilitaryTrucks", "MilitarySupplies", "AttackHelicopters", "Missiles" }, ["MFMB"] = new[] { "Ammunition", "Tanks", "MilitaryCars" }
    };

    internal static readonly IReadOnlyDictionary<string, string> Roles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["OWC"] = "Raw extraction · oil and gas", ["OWN"] = "Raw extraction · oil and gas", ["CME"] = "Raw extraction · coal", ["CMS"] = "Raw extraction · coal", ["IME"] = "Raw extraction · iron ore", ["IMW"] = "Raw extraction · iron ore", ["FRC"] = "Raw extraction · timber", ["FRS"] = "Raw extraction · timber", ["FM"] = "Raw extraction · agriculture",
        ["OR"] = "Processor · crude oil to fuel", ["SW"] = "Processor · timber to construction goods", ["SM"] = "Processor · ore and scrap to steel", ["FF"] = "Processor · farm goods to food", ["GF"] = "Processor · materials to consumer and industrial goods", ["MF"] = "Manufacturer · vehicles and heavy equipment",
        ["HB"] = "Port · imports and exports", ["HMB"] = "Port and military logistics", ["MFMB"] = "Military supply depot", ["MB"] = "Military destination", ["CP"] = "Power generation · coal consumer", ["CW"] = "City demand · terminal consumer", ["CS"] = "City demand · terminal consumer", ["CSW"] = "City demand · terminal consumer"
    };
    internal static readonly ISet<string> RawMaterialFacilities = new HashSet<string>(StringComparer.Ordinal) { "OWC", "OWN", "CME", "CMS", "IME", "IMW", "FRC", "FRS", "FM" };
    internal static readonly ISet<string> CityFacilities = new HashSet<string>(StringComparer.Ordinal) { "CS", "CSW", "CW" };
}
