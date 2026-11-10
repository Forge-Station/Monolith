using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._WF.Prototypes;
using Microsoft.EntityFrameworkCore;

namespace Content.Server._WF.Prototypes;

/// <summary>Rewrites renamed Wolfgate prototype ids in loaded DB rows, so the load that finds them also saves them.</summary>
public static class WFLegacyDbRows
{
    private static readonly string[] ShapedOrgans = { "penis", "vagina", "breasts" };

    /// <summary>Moves a profile row's species, loadouts and anatomy shapes onto current ids; true if anything changed.</summary>
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

        if (UpdateGenitals(profile.Genitals) is { } genitals)
        {
            profile.Genitals = genitals;
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

    /// <summary>The anatomy JSON with legacy shape ids replaced, or null when nothing needs changing or it cannot be read.</summary>
    /// <remarks>Only the shape values are touched, so an unreadable or unmigrated column stays exactly as it is.</remarks>
    private static string? UpdateGenitals(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
                return null;

            var changed = false;
            foreach (var organ in ShapedOrgans)
            {
                if (root[organ] is not JsonObject part
                    || part["shape"] is not JsonValue value
                    || !value.TryGetValue<string>(out var shape))
                {
                    continue;
                }

                var current = WFLegacyPrototypeIds.Resolve(WFLegacyPrototypeIds.GenitalShapes, shape);
                if (current == shape)
                    continue;

                part["shape"] = current;
                changed = true;
            }

            return changed ? root.ToJsonString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
