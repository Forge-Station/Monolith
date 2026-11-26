using Content.Client._WF.UserInterface.Controls;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.BloodColour;

/// <summary>Body card toggle with a colour picker for the character's blood and flesh colour.</summary>
public sealed class BloodColourPicker : BoxContainer
{
    /// <summary>Colour the picker starts on when the toggle is first turned on: plain red blood.</summary>
    private static readonly Color DefaultColour = Color.FromHex("#800000");

    /// <summary>A colour was picked; null is the species' own.</summary>
    public event Action<Color?>? OnColourChanged;

    private readonly CheckBox _toggle;
    private readonly WolfgateColorPicker _picker;

    public BloodColourPicker()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 6;
        HorizontalExpand = true;

        _toggle = new CheckBox
        {
            Text = Loc.GetString("wf-blood-colour-toggle"),
            ToolTip = Loc.GetString("wf-blood-colour-tooltip"),
        };
        AddChild(_toggle);

        _picker = new WolfgateColorPicker { Visible = false, Color = DefaultColour };
        AddChild(_picker);

        _toggle.OnToggled += args =>
        {
            _picker.Visible = args.Pressed;
            OnColourChanged?.Invoke(args.Pressed ? _picker.Color : null);
        };
        _picker.OnColorChanged += colour => OnColourChanged?.Invoke(colour);
    }

    /// <summary>Shows a profile's colour without raising <see cref="OnColourChanged"/>.</summary>
    public void SetColour(Color? colour)
    {
        _toggle.Pressed = colour != null;
        _picker.Visible = colour != null;
        _picker.Color = colour ?? DefaultColour;
    }
}
