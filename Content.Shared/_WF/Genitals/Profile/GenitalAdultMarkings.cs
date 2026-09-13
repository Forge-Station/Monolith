using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Genitals;

/// <summary>Ordinary markings that require an adult character (the settings' AdultOnlyMarkings, e.g. pregnancy overlays).</summary>
public static class GenitalAdultMarkings
{
    /// <summary>A new appearance without the adult-only markings when age is below AdultAge; the input itself when nothing is removed.</summary>
    /// <remarks>Never edits the input list: With* copies of a profile share their appearance.</remarks>
    public static HumanoidCharacterAppearance StripIfMinor(HumanoidCharacterAppearance appearance, int age, IPrototypeManager proto)
    {
        var settings = GenitalProfileValidator.GetSettings(proto);
        if (age >= settings.AdultAge || settings.AdultOnlyMarkings.Count == 0)
            return appearance;

        List<Marking>? kept = null;
        for (var i = 0; i < appearance.Markings.Count; i++)
        {
            var marking = appearance.Markings[i];
            if (!settings.AdultOnlyMarkings.Contains(marking.MarkingId))
            {
                kept?.Add(marking);
                continue;
            }

            // First removal: copy everything before it.
            if (kept == null)
            {
                kept = new List<Marking>(appearance.Markings.Count);
                for (var j = 0; j < i; j++)
                {
                    kept.Add(appearance.Markings[j]);
                }
            }
        }

        return kept == null ? appearance : appearance.WithMarkings(kept);
    }
}
