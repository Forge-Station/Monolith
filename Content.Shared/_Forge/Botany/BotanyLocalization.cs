using System.Linq;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Botany;

/// <summary>
/// Localizes botany UI strings for reagents, gases, and mutation names.
/// </summary>
public static class BotanyLocalization
{
    private static readonly Dictionary<string, string> MutationLocKeys = new()
    {
        ["Sentient"] = "plant-analyzer-mutation-sentient",
        ["Slippery"] = "plant-analyzer-mutation-slip",
        ["Unviable"] = "plant-analyzer-mutation-unviable",
        ["ChangeSeedless"] = "plant-analyzer-mutation-seedless",
        ["ChangeLigneous"] = "plant-analyzer-mutation-ligneous",
        ["ChangeTurnIntoKudzu"] = "plant-analyzer-mutation-turnintokudzu",
        ["ChangeScreaming"] = "plant-analyzer-mutation-canscream",
        ["Radioactive"] = "plant-analyzer-mutation-radioactive",
        ["CarnivorousGrab"] = "plant-analyzer-mutation-grabber",
        ["PestEater"] = "plant-analyzer-mutation-pest-eater",
        ["ChangeWaterConsumption"] = "plant-mutation-name-change-water-consumption",
        ["ChangeNutrientConsumption"] = "plant-mutation-name-change-nutrient-consumption",
        ["ChangeIdealHeat"] = "plant-mutation-name-change-ideal-heat",
        ["ChangeHeatTolerance"] = "plant-mutation-name-change-heat-tolerance",
        ["ChangeToxinsTolerance"] = "plant-mutation-name-change-toxins-tolerance",
        ["ChangeLowPressureTolerance"] = "plant-mutation-name-change-low-pressure-tolerance",
        ["ChangeHighPressureTolerance"] = "plant-mutation-name-change-high-pressure-tolerance",
        ["ChangePestTolerance"] = "plant-mutation-name-change-pest-tolerance",
        ["ChangeWeedTolerance"] = "plant-mutation-name-change-weed-tolerance",
        ["ChangeEndurance"] = "plant-mutation-name-change-endurance",
        ["ChangeYield"] = "plant-mutation-name-change-yield",
        ["ChangeLifespan"] = "plant-mutation-name-change-lifespan",
        ["ChangeMaturation"] = "plant-mutation-name-change-maturation",
        ["ChangeProduction"] = "plant-mutation-name-change-production",
        ["ChangePotency"] = "plant-mutation-name-change-potency",
        ["ChangeIdealLight"] = "plant-mutation-name-change-ideal-light",
        ["ChangeChemicals"] = "plant-mutation-name-change-chemicals",
        ["ChangeExudeGasses"] = "plant-mutation-name-change-exude-gasses",
        ["ChangeConsumeGasses"] = "plant-mutation-name-change-consume-gasses",
        ["ChangeHarvest"] = "plant-mutation-name-change-harvest",
        ["ChangeSpecies"] = "plant-mutation-name-change-species",
        ["CarbonFilter"] = "plant-mutation-name-carbon-filter",
        ["AntiradFruit"] = "plant-mutation-name-antirad-fruit",
        ["DroughtTolerant"] = "plant-mutation-name-drought-tolerant",
        ["NitrogenFixer"] = "plant-mutation-name-nitrogen-fixer",
        ["ShadeAdapted"] = "plant-mutation-name-shade-adapted",
        ["SunLover"] = "plant-mutation-name-sun-lover",
        ["Perennial"] = "plant-mutation-name-perennial",
        ["PestWard"] = "plant-mutation-name-pest-ward",
        ["ThinAir"] = "plant-mutation-name-thin-air",
        ["OxygenBloom"] = "plant-mutation-name-oxygen-bloom",
        ["AloeSap"] = "plant-mutation-name-aloe-sap",
        ["BitterAntidote"] = "plant-mutation-name-bitter-antidote",
        // Boolean trait ids used for gene pinning
        ["TurnIntoKudzu"] = "plant-analyzer-mutation-turnintokudzu",
        ["Seedless"] = "plant-analyzer-mutation-seedless",
        ["Ligneous"] = "plant-analyzer-mutation-ligneous",
        ["CanScream"] = "plant-analyzer-mutation-canscream",
        ["CarnivorousPestEater"] = "plant-analyzer-mutation-pest-eater",
    };

    public static string GetReagentName(IPrototypeManager prototypes, string reagentId)
    {
        if (prototypes.TryIndex<ReagentPrototype>(reagentId, out var proto))
            return proto.LocalizedName;

        return reagentId;
    }

    public static string GetMutationName(string mutationName)
    {
        if (MutationLocKeys.TryGetValue(mutationName, out var key) && Loc.TryGetString(key, out var localized))
            return localized;

        var fallbackKey = $"plant-mutation-name-{ToKebabCase(mutationName)}";
        if (Loc.TryGetString(fallbackKey, out localized))
            return localized;

        return mutationName;
    }

    public static string FormatGas(Gas gas, float amount)
    {
        var key = gas switch
        {
            Gas.Nitrogen => "gases-nitrogen",
            Gas.Oxygen => "gases-oxygen",
            Gas.CarbonDioxide => "gases-co2",
            Gas.Plasma => "gases-plasma",
            Gas.Tritium => "gases-tritium",
            Gas.WaterVapor => "gases-water-vapor",
            Gas.Ammonia => "gases-ammonia",
            Gas.NitrousOxide => "gases-n2o",
            Gas.Frezon => "gases-frezon",
            Gas.BZ => "gases-bz",
            Gas.Healium => "gases-healium",
            Gas.Nitrium => "gases-nitrium",
            Gas.Pluoxium => "gases-pluoxium",
            _ => null
        };

        var name = key != null && Loc.TryGetString(key, out var localized) ? localized : gas.ToString();
        return $"{name} ({amount:0.##})";
    }

    public static string FormatGases(IEnumerable<KeyValuePair<Gas, float>> gases)
    {
        var entries = gases.Select(g => FormatGas(g.Key, g.Value)).ToArray();
        return entries.Length == 0 ? string.Empty : string.Join("\n", entries);
    }

    public static string FormatChemical(IPrototypeManager prototypes, string id, float min, float max)
    {
        return $"{GetReagentName(prototypes, id)} ({min:0.#}-{max:0.#})";
    }

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var chars = new List<char>(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                    chars.Add('-');
                chars.Add(char.ToLowerInvariant(c));
            }
            else
            {
                chars.Add(c);
            }
        }

        return new string(chars.ToArray());
    }
}
