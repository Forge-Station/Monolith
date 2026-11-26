using Content.Shared._WF.LegStyle;

namespace Content.Client.Lobby.UI;

/// <summary>The Digitigrade legs toggle in the Body card, shown for species with both kinds of legs.</summary>
public sealed partial class HumanoidProfileEditor
{
    private void InitializeLegStyle()
    {
        DigitigradeLegsCheckBox.OnToggled += args => SetDigitigradeLegs(args.Pressed);
    }

    private void UpdateLegStyle()
    {
        if (Profile == null)
        {
            DigitigradeLegsCheckBox.Visible = false;
            return;
        }

        var stance = LegStyleRules.Validate(Profile.Species, Profile.LegStance, _prototypeManager);
        if (stance != Profile.LegStance)
            Profile = Profile.WithLegStance(stance);

        DigitigradeLegsCheckBox.Visible = LegStyleRules.TryGetAlternate(Profile.Species, _prototypeManager, out _);
        DigitigradeLegsCheckBox.Pressed = LegStyleRules.IsDigitigrade(Profile.Species, Profile.LegStance, _prototypeManager);
    }

    private void SetDigitigradeLegs(bool digitigrade)
    {
        if (Profile == null)
            return;

        var stance = LegStyleRules.Validate(Profile.Species,
            digitigrade ? LegStance.Digitigrade : LegStance.Plantigrade,
            _prototypeManager);
        if (Profile.LegStance == stance)
            return;

        Profile = Profile.WithLegStance(stance);
        SetDirty();
        ReloadPreview();
    }
}
