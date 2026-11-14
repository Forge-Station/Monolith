using System.Numerics;
using Content.Shared._WF.TypingBubble;
using Content.Shared.Chat.TypingIndicator;
using Content.Shared.Humanoid.Prototypes;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI;

/// <summary>The Typing bubble picker in the Body card: one box per bubble, the species' own first.</summary>
public sealed partial class HumanoidProfileEditor
{
    private readonly ButtonGroup _wfTypingBubbleGroup = new();
    private readonly Dictionary<string, ContainerButton> _wfTypingBubbleButtons = new();
    private ContainerButton _wfTypingBubbleSpecies = default!;
    private AnimatedTextureRect _wfTypingBubbleSpeciesIcon = default!;

    private void InitializeTypingBubble()
    {
        _wfTypingBubbleSpeciesIcon = new AnimatedTextureRect();
        _wfTypingBubbleSpecies = TypingBubbleBox(_wfTypingBubbleSpeciesIcon, Loc.GetString("wf-typing-bubble-species"));
        _wfTypingBubbleSpecies.Name = "TypingBubbleSpecies";
        _wfTypingBubbleSpecies.OnPressed += _ => SetTypingBubble(null);
        TypingBubbleGrid.AddChild(_wfTypingBubbleSpecies);

        foreach (var indicator in WFTypingBubbleRules.Offered(_prototypeManager))
        {
            var icon = new AnimatedTextureRect();
            icon.SetFromSpriteSpecifier(new SpriteSpecifier.Rsi(indicator.SpritePath, indicator.TypingState));

            var box = TypingBubbleBox(icon, TypingBubbleName(indicator.ID));
            box.Name = $"TypingBubble{indicator.ID}";
            var chosen = new ProtoId<TypingIndicatorPrototype>(indicator.ID);
            box.OnPressed += _ => SetTypingBubble(chosen);

            _wfTypingBubbleButtons[indicator.ID] = box;
            TypingBubbleGrid.AddChild(box);
        }
    }

    private ContainerButton TypingBubbleBox(AnimatedTextureRect icon, string tooltip)
    {
        icon.HorizontalAlignment = HAlignment.Center;
        icon.VerticalAlignment = VAlignment.Center;
        icon.DisplayRect.Stretch = TextureRect.StretchMode.KeepCentered;

        return new ContainerButton
        {
            ToggleMode = true,
            Group = _wfTypingBubbleGroup,
            MinSize = new Vector2(40, 40),
            ToolTip = tooltip,
            StyleClasses = { ContainerButton.StyleClassButton },
            Children = { icon },
        };
    }

    /// <summary>The bubble's name in the picker, its id where no name is written.</summary>
    private static string TypingBubbleName(string id)
    {
        return Loc.TryGetString($"wf-typing-bubble-{id.ToLowerInvariant()}", out var name) ? name : id;
    }

    /// <summary>Lights the profile's bubble, and shows the species' own on the first box.</summary>
    private void UpdateTypingBubble()
    {
        if (Profile == null)
            return;

        var own = SpeciesTypingBubble(Profile.Species);
        if (_prototypeManager.TryIndex(own, out var ownProto))
            _wfTypingBubbleSpeciesIcon.SetFromSpriteSpecifier(new SpriteSpecifier.Rsi(ownProto.SpritePath, ownProto.TypingState));

        _wfTypingBubbleSpecies.ToolTip = Loc.GetString("wf-typing-bubble-species-named",
            ("name", TypingBubbleName(own.Id)));

        if (Profile.TypingIndicator is { } picked && _wfTypingBubbleButtons.TryGetValue(picked.Id, out var box))
            box.Pressed = true; // the group unpresses the rest
        else
            _wfTypingBubbleSpecies.Pressed = true;
    }

    /// <summary>The bubble the species' mob is drawn with.</summary>
    private ProtoId<TypingIndicatorPrototype> SpeciesTypingBubble(string species)
    {
        if (_prototypeManager.TryIndex<SpeciesPrototype>(species, out var speciesProto)
            && _prototypeManager.TryIndex(speciesProto.Prototype, out var mob)
            && mob.TryGetComponent<TypingIndicatorComponent>(out var indicator, _entManager.ComponentFactory))
        {
            return indicator.TypingIndicatorPrototype;
        }

        return SharedTypingIndicatorSystem.InitialIndicatorId;
    }

    private void SetTypingBubble(ProtoId<TypingIndicatorPrototype>? indicator)
    {
        if (Profile == null || Profile.TypingIndicator == indicator)
            return;

        Profile = Profile.WithTypingIndicator(indicator);
        SetDirty();
    }
}
