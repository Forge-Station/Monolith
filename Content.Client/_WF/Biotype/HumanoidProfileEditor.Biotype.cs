using System.Linq;
using System.Numerics;
using Content.Shared._WF.Biotype;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Traits;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

/// <summary>The Biotype switch in the Body card: organic or synthetic, whatever the species, one icon box each.</summary>
public sealed partial class HumanoidProfileEditor
{
    private readonly ButtonGroup _wfBiotypeGroup = new();
    private readonly Dictionary<WFBiotype, ContainerButton> _wfBiotypeButtons = new();

    private void InitializeBiotype()
    {
        var sprites = _entManager.System<SpriteSystem>();
        var biotypes = _prototypeManager.EnumeratePrototypes<WFBiotypePrototype>().OrderBy(p => p.Biotype).ToList();
        for (var i = 0; i < biotypes.Count; i++)
        {
            var biotype = biotypes[i];
            var row = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                SeparationOverride = 6,
                HorizontalAlignment = HAlignment.Center,
            };
            if (biotype.Icon != null)
            {
                row.AddChild(new TextureRect
                {
                    Texture = sprites.Frame0(biotype.Icon),
                    Stretch = TextureRect.StretchMode.KeepAspectCentered,
                    SetSize = new Vector2(24, 24),
                    VerticalAlignment = VAlignment.Center,
                });
            }
            row.AddChild(new Label { Text = Loc.GetString(biotype.Name), VAlign = Label.VAlignMode.Center });

            var button = new ContainerButton
            {
                Name = $"Biotype{biotype.Biotype}",
                ToggleMode = true,
                Group = _wfBiotypeGroup,
                HorizontalExpand = true,
                MinSize = new Vector2(0, 30),
                ToolTip = Loc.GetString(biotype.Description),
                StyleClasses = { ContainerButton.StyleClassButton, SegmentClass(i, biotypes.Count) },
                Children = { row },
            };
            var chosen = biotype.Biotype;
            button.OnPressed += _ => SetBiotype(chosen);

            _wfBiotypeButtons[biotype.Biotype] = button;
            BiotypeSwitch.AddChild(button);
        }
    }

    /// <summary>Joins the boxes into one switch: open on the sides that touch a neighbour.</summary>
    private static string SegmentClass(int index, int count)
    {
        if (index == 0)
            return "OpenRight";

        return index == count - 1 ? "OpenLeft" : "OpenBoth";
    }

    /// <summary>Lights the biotype the character will have, the species' own while none is picked.</summary>
    private void UpdateBiotype()
    {
        if (Profile == null || !_prototypeManager.TryIndex<SpeciesPrototype>(Profile.Species, out var species))
            return;

        if (_wfBiotypeButtons.TryGetValue(WFBiotypeRules.Resolve(Profile.Biotype, species), out var button))
            button.Pressed = true; // the group unpresses the other
    }

    private void SetBiotype(WFBiotype biotype)
    {
        if (Profile == null || Profile.Biotype == biotype)
            return;

        Profile = Profile.WithBiotype(biotype);
        SetDirty();

        // Traits and loadouts can list biotypes they refuse.
        RefreshTraits();
        RefreshLoadouts();
    }

    /// <summary>The tooltip line naming the biotypes that can't take the trait, or null when it lists none.</summary>
    private string? BiotypeBlacklistTooltip(TraitPrototype trait)
    {
        if (trait.BiotypeBlacklist.Count == 0)
            return null;

        var names = trait.BiotypeBlacklist
            .OrderBy(b => b)
            .Select(b => $"[color=#087209]{Loc.GetString(WFBiotypeGates.NameKey(b))}[/color]");
        return Loc.GetString("wf-biotype-trait-blocked", ("biotypes", string.Join(", ", names)));
    }
}
