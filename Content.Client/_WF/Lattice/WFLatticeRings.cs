using Content.Shared._WF.Lattice;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Lattice;

/// <summary>The Lattice's band edges as radar rings, in place of the drone rings they line up with.</summary>
public static class WFLatticeRings
{
    /// <summary>Strength of the static garbling on a garbled band's ring label.</summary>
    private const float LabelGarble = 0.35f;

    /// <summary>Whether lists built earlier still match the field's profile and edges.</summary>
    public static bool IsCurrent(WFLatticeFieldComponent field, string? builtFor, List<float> radii)
    {
        if (builtFor != field.Profile.Id)
            return false;

        if (field.Edges.Count != radii.Count + 1)
            return field.Edges.Count == 0;

        for (var i = 0; i < radii.Count; i++)
        {
            if (!MathHelper.CloseTo(field.Edges[i + 1], radii[i]))
                return false;
        }

        return true;
    }

    /// <summary>Fills the ring lists for a map's Lattice; false if it has none.</summary>
    public static bool TryBuild(IPrototypeManager proto, WFLatticeFieldComponent field, List<float> radii, List<string> labels, List<Color> colors)
    {
        radii.Clear();
        labels.Clear();
        colors.Clear();
        if (!proto.TryIndex(field.Profile, out var profile))
            return false;

        // The server's edges follow the map's drone rings; the profile's are the fallback.
        var useField = field.Edges.Count == profile.Bands.Count;
        for (var i = 1; i < profile.Bands.Count; i++)
        {
            var band = profile.Bands[i];
            var name = Loc.GetString(band.Name);
            radii.Add(useField ? field.Edges[i] : band.Edge);
            labels.Add(band.Garbled ? WFLatticeMath.Garble(name, LabelGarble, i) : name);
            colors.Add(band.Color);
        }

        return radii.Count > 0;
    }
}
