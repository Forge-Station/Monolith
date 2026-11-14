using System.Linq;
using System.Numerics;
using Content.Client._WF.Stylesheets;
using Content.Shared.Humanoid;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Humanoid;

/// <summary>
/// Segmented sex picker: one icon box per sex the species allows, in a fixed male/female/none order.
/// Species vary in how many sexes they offer, so the control shows only what is legal and hides itself
/// entirely when there is no choice to make.
/// </summary>
public sealed class WolfgateSexSelector : BoxContainer
{
    /// <summary>Display order, so the boxes do not reshuffle between species that list their sexes differently.</summary>
    private static readonly Sex[] Order = { Sex.Male, Sex.Female, Sex.Unsexed };

    /// <summary>Raised when the player picks a sex. Not raised by <see cref="SetSelected"/>.</summary>
    public Action<Sex>? OnSexSelected;

    private readonly ButtonGroup _group = new();
    private readonly Dictionary<Sex, TextureRect> _icons = new();
    private readonly Dictionary<Sex, ContainerButton> _segments = new();

    public WolfgateSexSelector()
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 4;
    }

    /// <summary>Rebuilds the boxes for the sexes a species allows. Hidden when fewer than two are offered.</summary>
    public void SetSexes(IReadOnlyList<Sex> sexes)
    {
        // Disposing rather than removing matters: a ButtonGroup only drops a button when it is disposed,
        // so plain removal would leave every superseded segment in the group forever.
        DisposeAllChildren();
        _icons.Clear();
        _segments.Clear();

        foreach (var sex in Order)
        {
            if (!sexes.Contains(sex))
                continue;

            var icon = new TextureRect
            {
                StyleClasses = { IconClass(sex), StyleWolfgate.StyleClassSexIconOff },
                Stretch = TextureRect.StretchMode.KeepCentered,
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center,
            };

            var segment = new ContainerButton
            {
                ToggleMode = true,
                Group = _group,
                StyleClasses = { StyleWolfgate.StyleClassCreatorToggle },
                MinSize = new Vector2(38, 30),
                ToolTip = Loc.GetString($"humanoid-profile-editor-sex-{sex.ToString().ToLowerInvariant()}-text"),
                Children = { icon },
            };

            var chosen = sex;
            segment.OnPressed += _ =>
            {
                UpdateTints();
                OnSexSelected?.Invoke(chosen);
            };

            _icons[sex] = icon;
            _segments[sex] = segment;
            AddChild(segment);
        }

        // A species with one legal sex still shows its box, greyed out, so the character's sex stays
        // legible instead of the control vanishing.
        Visible = _segments.Count > 0;
        if (_segments.Count == 1)
        {
            foreach (var segment in _segments.Values)
                segment.Disabled = true;
        }

        UpdateTints();
    }

    /// <summary>Lights the box for a sex without raising <see cref="OnSexSelected"/>.</summary>
    public void SetSelected(Sex sex)
    {
        if (_segments.TryGetValue(sex, out var segment))
            segment.Pressed = true; // the group unpresses the others

        UpdateTints();
    }

    private void UpdateTints()
    {
        foreach (var (sex, icon) in _icons)
        {
            var on = _segments[sex].Pressed;
            icon.RemoveStyleClass(on ? StyleWolfgate.StyleClassSexIconOff : StyleWolfgate.StyleClassSexIconOn);
            icon.AddStyleClass(on ? StyleWolfgate.StyleClassSexIconOn : StyleWolfgate.StyleClassSexIconOff);
        }
    }

    private static string IconClass(Sex sex) => sex switch
    {
        Sex.Male => StyleWolfgate.StyleClassSexIconMale,
        Sex.Female => StyleWolfgate.StyleClassSexIconFemale,
        _ => StyleWolfgate.StyleClassSexIconNone,
    };
}
