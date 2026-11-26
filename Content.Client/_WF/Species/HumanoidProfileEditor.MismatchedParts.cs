using Content.Shared._WF.Species;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;

namespace Content.Client.Lobby.UI;

/// <summary>The Mismatched parts toggle in the Body card, and the markings and hair styles it opens.</summary>
public sealed partial class HumanoidProfileEditor
{
    private void InitializeMismatchedParts()
    {
        MismatchedPartsCheckBox.OnToggled += args => SetMismatchedParts(args.Pressed);
    }

    private void UpdateMismatchedParts()
    {
        MismatchedPartsCheckBox.Pressed = Profile?.MismatchedParts ?? false;
        Markings.MismatchedParts = Profile?.MismatchedParts ?? false;
    }

    private void SetMismatchedParts(bool enabled)
    {
        if (Profile == null || Profile.MismatchedParts == enabled)
            return;

        Profile = Profile.WithMismatchedParts(enabled);
        // Turning it off drops what only the option allowed, as saving would
        if (!enabled)
        {
            Profile = Profile.WithCharacterAppearance(
                HumanoidCharacterAppearance.EnsureValid(Profile.Appearance, Profile.Species, Profile.Sex, false));
        }

        Markings.MismatchedParts = enabled;
        UpdateSkinColor();
        UpdateHairPickers();
        UpdateCMarkingsHair();
        UpdateCMarkingsFacialHair();
        UpdateMarkings();
        SetDirty();
        ReloadPreview();
    }

    /// <summary>The styles of a hair category the edited character may pick.</summary>
    private IReadOnlyDictionary<string, MarkingPrototype> HairStyleChoices(MarkingCategories category)
    {
        if (Profile == null)
            return new Dictionary<string, MarkingPrototype>();

        return MismatchedPartsRules.Styles(category, Profile.Species, Profile.MismatchedParts, _markingManager, _prototypeManager);
    }
}
