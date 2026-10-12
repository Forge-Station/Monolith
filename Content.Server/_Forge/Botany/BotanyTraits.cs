using Content.Server.Botany;

namespace Content.Server._Forge.Botany;

/// <summary>
/// Collects plant trait identifiers for gene pinning and display.
/// </summary>
public static class BotanyTraits
{
    public static List<string> CollectTraitIds(SeedData seed)
    {
        var traits = new List<string>();

        if (seed.TurnIntoKudzu)
            traits.Add("TurnIntoKudzu");
        if (seed.Seedless || seed.PermanentlySeedless)
            traits.Add("Seedless");
        if (seed.Ligneous)
            traits.Add("Ligneous");
        if (seed.CanScream)
            traits.Add("CanScream");
        if (!seed.Viable)
            traits.Add("Unviable");
        if (seed.Radioactive)
            traits.Add("Radioactive");
        if (seed.CarnivorousGrab)
            traits.Add("CarnivorousGrab");
        if (seed.CarnivorousPestEater)
            traits.Add("PestEater");

        foreach (var mutation in seed.Mutations)
        {
            if (!traits.Contains(mutation.Name))
                traits.Add(mutation.Name);
        }

        return traits;
    }
}
