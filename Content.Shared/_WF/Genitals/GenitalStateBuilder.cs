using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Genitals;

/// <summary>The one mapping from configuration to organ state, used by the server organ builder and the creator preview.</summary>
public static class GenitalStateBuilder
{
    /// <summary>Representative erect lengths (cm) for sprite steps 1-5; the inverse of LengthToStep with the default settings.</summary>
    private static readonly int[] StepLengths = { 15, 21, 28, 38, 52 };

    /// <summary>Organ state for one slot, or null when the profile has no organ there. Colours are resolved against the skin colour.</summary>
    public static GenitalOrganState? FromProfile(GenitalProfile profile, GenitalSlot slot, Color skinColor, GenitalSettingsPrototype settings)
    {
        switch (slot)
        {
            case GenitalSlot.Penis when profile.Penis is { } penis:
                return new GenitalOrganState
                {
                    Shape = penis.Shape,
                    Step = LengthToStep(penis.LengthCm, settings),
                    LengthCm = (byte) Math.Clamp(penis.LengthCm, 0, byte.MaxValue),
                    Sheath = penis.Sheath,
                    Color = penis.MatchSkin ? skinColor : penis.Color,
                    SheathColor = penis.SheathMatchSkin ? skinColor : penis.SheathColor,
                    MatchSkin = penis.MatchSkin,
                    SheathMatchSkin = penis.SheathMatchSkin,
                };

            case GenitalSlot.Testicles when profile.Testicles is { } testicles:
                return new GenitalOrganState
                {
                    // Internal testicles have no art.
                    Shape = testicles.Type == TesticleType.External
                        ? settings.TesticlesShape
                        : (ProtoId<GenitalShapePrototype>?) null,
                    Step = (byte) Math.Clamp(testicles.Size, 0, byte.MaxValue),
                    Testicles = testicles.Type,
                    Color = testicles.MatchSkin ? skinColor : testicles.Color,
                    MatchSkin = testicles.MatchSkin,
                };

            case GenitalSlot.Vagina when profile.Vagina is { } vagina:
                return new GenitalOrganState
                {
                    Shape = vagina.Shape,
                    Step = 1,
                    Color = vagina.MatchSkin ? skinColor : vagina.Color,
                    MatchSkin = vagina.MatchSkin,
                };

            case GenitalSlot.Womb when profile.Womb:
                return new GenitalOrganState();

            case GenitalSlot.Breasts when profile.Breasts is { } breasts:
                return new GenitalOrganState
                {
                    Shape = breasts.Shape,
                    Step = (byte) Math.Clamp(breasts.Cup, 0, byte.MaxValue),
                    Lactation = breasts.Lactation,
                    Color = breasts.MatchSkin ? skinColor : breasts.Color,
                    MatchSkin = breasts.MatchSkin,
                };

            default:
                return null;
        }
    }

    /// <summary>Penis sprite step (1 upwards) for an erect length, from the settings' step bounds.</summary>
    public static byte LengthToStep(int lengthCm, GenitalSettingsPrototype settings)
    {
        var step = 1;
        foreach (var max in settings.LengthStepMaxCm)
        {
            if (lengthCm <= max)
                break;

            step++;
        }

        return (byte) Math.Min(step, byte.MaxValue);
    }

    /// <summary>Representative length for a sprite step (1-5), used by the legacy migration.</summary>
    public static int StepToLength(int step)
    {
        return StepLengths[Math.Clamp(step, 1, StepLengths.Length) - 1];
    }

    /// <summary>Runtime visibility defaults from the profile.</summary>
    public static GenitalVisibilitySet VisibilityFromProfile(GenitalProfile profile)
    {
        return new GenitalVisibilitySet
        {
            Penis = profile.Penis?.Visibility ?? GenitalVisibility.Normal,
            Testicles = profile.Testicles?.Visibility ?? GenitalVisibility.Normal,
            Vagina = profile.Vagina?.Visibility ?? GenitalVisibility.Normal,
            Breasts = profile.Breasts?.Visibility ?? GenitalVisibility.Normal,
        };
    }

    /// <summary>Reveal mode and visibility a body starts with: the profile's, or the defaults without one.</summary>
    public static (GenitalRevealMode RevealMode, GenitalVisibilitySet Visibility) RuntimeDefaults(GenitalProfile? profile)
    {
        return profile == null
            ? (GenitalRevealMode.UndergarmentRemoval, default(GenitalVisibilitySet))
            : (profile.RevealMode, VisibilityFromProfile(profile));
    }

    /// <summary>Re-tints the skin-matched parts of an organ state; custom colours are kept.</summary>
    public static GenitalOrganState Retint(GenitalOrganState state, Color skinColor)
    {
        if (state.MatchSkin)
            state.Color = skinColor;

        if (state.SheathMatchSkin)
            state.SheathColor = skinColor;

        return state;
    }
}
