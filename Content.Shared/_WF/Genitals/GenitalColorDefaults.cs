using Content.Shared._WF.Genitals.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Genitals;

/// <summary>Default colour policy for new anatomy. Mucosal tissue gets a tissue colour on species whose skin is not human-toned.</summary>
/// <remarks>Used by card seeding, the preset menu and the legacy migration of untinted markings.</remarks>
public static class GenitalColorDefaults
{
    /// <summary>Tissue colour used when no preset provides one.</summary>
    public static readonly Color DefaultTissueColor = Color.FromHex("#C28A8A");

    /// <summary>The preset listing the species, else the fallback preset; null if neither exists.</summary>
    public static GenitalPresetPrototype? FindPreset(string species, IPrototypeManager proto)
    {
        GenitalPresetPrototype? fallback = null;
        foreach (var preset in proto.EnumeratePrototypes<GenitalPresetPrototype>())
        {
            foreach (var listed in preset.Species)
            {
                if (listed.Id == species)
                    return preset;
            }

            if (preset.Fallback)
                fallback ??= preset;
        }

        return fallback;
    }

    /// <summary>Tissue colour for species whose skin colouration is not HumanToned; null means match skin.</summary>
    public static Color? TissueColorFor(string species, IPrototypeManager proto)
    {
        if (string.IsNullOrEmpty(species)
            || !proto.TryIndex<SpeciesPrototype>(species, out var speciesProto)
            || speciesProto.SkinColoration == HumanoidSkinColor.HumanToned)
            return null;

        return FindPreset(species, proto)?.TissueColor ?? DefaultTissueColor;
    }

    /// <summary>Applies the colour policy to every organ of a profile, e.g. a preset entry.</summary>
    public static GenitalProfile Apply(GenitalProfile profile, string species, IPrototypeManager proto)
    {
        var tissue = TissueColorFor(species, proto);
        var result = profile;

        if (result.Penis is { } penis)
            result = result.WithPenis(ApplyPenis(penis, tissue));

        if (result.Testicles is { } testicles)
            result = result.WithTesticles(ApplyTesticles(testicles));

        if (result.Vagina is { } vagina)
            result = result.WithVagina(ApplyVagina(vagina, tissue));

        if (result.Breasts is { } breasts)
            result = result.WithBreasts(ApplyBreasts(breasts));

        return result;
    }

    /// <summary>Shaft (and sheath inner): tissue colour or match skin. Sheath outer: match skin.</summary>
    public static PenisProfile ApplyPenis(PenisProfile penis, Color? tissue)
    {
        return penis.With(matchSkin: tissue == null, color: tissue ?? Color.White, sheathMatchSkin: true, sheathColor: Color.White);
    }

    public static TesticlesProfile ApplyTesticles(TesticlesProfile testicles)
    {
        return testicles.With(matchSkin: true, color: Color.White);
    }

    /// <summary>Vulva: tissue colour or match skin.</summary>
    public static VaginaProfile ApplyVagina(VaginaProfile vagina, Color? tissue)
    {
        return vagina.With(matchSkin: tissue == null, color: tissue ?? Color.White);
    }

    public static BreastsProfile ApplyBreasts(BreastsProfile breasts)
    {
        return breasts.With(matchSkin: true, color: Color.White);
    }
}
