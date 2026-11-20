using Content.Client._WF.BloodColour;

namespace Content.Client.Lobby.UI;

/// <summary>The Blood card: a colour that replaces the species' own blood and flesh colour.</summary>
public sealed partial class HumanoidProfileEditor
{
    private BloodColourPicker _bloodColourPicker = default!;

    private void InitializeBloodColour()
    {
        _bloodColourPicker = new BloodColourPicker();
        _bloodColourPicker.OnColourChanged += SetBloodColour;
        BloodColourContainer.AddChild(_bloodColourPicker);
    }

    private void UpdateBloodColour()
    {
        _bloodColourPicker.SetColour(Profile?.BloodColor);
    }

    private void SetBloodColour(Color? colour)
    {
        if (Profile == null || Profile.BloodColor == colour)
            return;

        Profile = Profile.WithBloodColor(colour);
        SetDirty();
    }
}
