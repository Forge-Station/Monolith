using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._WF.Prototypes;
using Microsoft.EntityFrameworkCore;

namespace Content.Server._WF.Prototypes;

/// <summary>Rewrites renamed Wolfgate prototype ids in loaded DB rows, so the load that finds them also saves them.</summary>
public static class WFLegacyDbRows
{
    /// <summary>Moves a profile row's species and loadouts onto current ids; true if anything changed.</summary>
    public static bool Update(Profile profile)
    {
        var changed = false;

        var species = WFLegacyPrototypeIds.Resolve(WFLegacyPrototypeIds.Species, profile.Species);
        if (species != profile.Species)
        {
            profile.Species = species;
            changed = true;
        }

        foreach (var loadout in profile.Loadouts.SelectMany(r => r.Groups).SelectMany(g => g.Loadouts))
        {
            var name = WFLegacyPrototypeIds.Resolve(WFLegacyPrototypeIds.Loadouts, loadout.LoadoutName);
            if (name == loadout.LoadoutName)
                continue;

            loadout.LoadoutName = name;
            changed = true;
        }

        return changed;
    }

    /// <summary>Moves consent toggle rows onto current ids, dropping an old row whose new id is already stored.</summary>
    public static bool Update(ConsentSettings settings)
    {
        var changed = false;
        foreach (var toggle in settings.ConsentToggles.ToList())
        {
            var id = WFLegacyPrototypeIds.Resolve(WFLegacyPrototypeIds.ConsentToggles, toggle.ToggleProtoId);
            if (id == toggle.ToggleProtoId)
                continue;

            if (settings.ConsentToggles.Any(t => t.ToggleProtoId == id))
                settings.ConsentToggles.Remove(toggle);
            else
                toggle.ToggleProtoId = id;

            changed = true;
        }

        return changed;
    }

    /// <summary>Saves rows changed by Update. A failure only logs: the read-time remap still covers those rows.</summary>
    public static async Task Save(DbContext db, ISawmill log, CancellationToken cancel = default)
    {
        try
        {
            await db.SaveChangesAsync(cancel);
        }
        catch (DbUpdateException e)
        {
            log.Warning($"Could not save rows moved off renamed prototype ids; they are remapped on each load instead. {e.Message}");
        }
    }
}
