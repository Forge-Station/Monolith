using System.Linq;
using System.Numerics;
using Content.Client._WF.Stylesheets;
using Content.Client._WF.UserInterface.Controls;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.Gags;

/// <summary>
/// One swatch per colour of a recolourable item, under a heading with a reset button. A swatch opens the colour popup; changes are
/// reported once the player stops dragging for a moment, as each one re-dresses the character preview.
/// </summary>
public sealed class WFGagsColorRow : BoxContainer
{
    /// <summary>Swatches on one line before the next starts.</summary>
    private const int SwatchColumns = 8;

    private static readonly SpriteSpecifier Palette =
        new SpriteSpecifier.Rsi(new ResPath("/Textures/_WF/Gags/palette.rsi"), "palette");

    /// <summary>How long the colours must hold still before they are reported, in seconds.</summary>
    private const float SettleTime = 0.2f;

    private readonly GridContainer _swatches;
    private readonly Button _reset;
    private readonly List<PanelContainer> _chips = new();
    private WolfgateColorPopup? _popup;

    private List<Color> _colors = new();
    private List<Color> _defaults = new();
    private string? _subject;
    private int _editing = -1;
    private float _pending = -1;

    /// <summary>Raised with the new colours, or null when they were reset to the item's own.</summary>
    public event Action<List<Color>?>? OnColorsChanged;

    /// <summary>The current colours, including changes still settling.</summary>
    public List<Color> CurrentColors => _colors.ToList();

    public WFGagsColorRow()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 2;

        _swatches = new GridContainer { Columns = SwatchColumns, HSeparationOverride = 2, VSeparationOverride = 2 };
        _reset = new Button
        {
            Text = Loc.GetString("wf-gags-reset"),
            ToolTip = Loc.GetString("wf-gags-reset-tooltip"),
            VerticalAlignment = VAlignment.Center,
        };
        _reset.OnPressed += _ =>
        {
            _colors = _defaults.ToList();
            RefreshChips();
            _pending = -1;
            OnColorsChanged?.Invoke(null);
        };
        AddChild(new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            Children =
            {
                new TextureRect { Texture = PaletteIcon(), VerticalAlignment = VAlignment.Center },
                new Label
                {
                    Text = Loc.GetString("wf-gags-colors"),
                    HorizontalExpand = true,
                    VerticalAlignment = VAlignment.Center,
                    StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel },
                },
                _reset,
            },
        });
        AddChild(_swatches);
    }

    /// <summary>The palette that marks a recolourable item.</summary>
    public static Texture PaletteIcon()
    {
        return IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<SpriteSystem>().Frame0(Palette);
    }

    /// <summary>Shows an item's colours. The swatches are only remade when the item or its colour count changed.</summary>
    public void Set(string subject, List<Color> colors, List<Color> defaults)
    {
        // A change still settling is ours; the loadout hasn't caught up with it yet.
        if (_pending >= 0 && subject == _subject)
            return;

        var rebuild = subject != _subject || colors.Count != _chips.Count;
        _subject = subject;
        _colors = colors.ToList();
        _defaults = defaults.ToList();

        if (rebuild)
        {
            _popup?.Close();
            _swatches.RemoveAllChildren();
            _chips.Clear();
            for (var i = 0; i < _colors.Count; i++)
            {
                var index = i;
                var chip = new PanelContainer { MinSize = new Vector2(20, 20) };
                var button = new ContainerButton
                {
                    StyleClasses = { WolfgateColorPicker.StyleClassSwatch },
                    ToolTip = Loc.GetString("wf-gags-color-tooltip", ("index", i + 1)),
                    Children = { new PanelContainer { Margin = new Thickness(2), Children = { chip } } },
                };
                button.OnPressed += _ => OpenPopup(index, button);
                _chips.Add(chip);
                _swatches.AddChild(button);
            }
        }

        RefreshChips();
    }

    private void RefreshChips()
    {
        for (var i = 0; i < _chips.Count && i < _colors.Count; i++)
        {
            _chips[i].PanelOverride = new StyleBoxFlat(_colors[i]);
        }

        _reset.Disabled = _colors.SequenceEqual(_defaults);
    }

    private void OpenPopup(int index, Control anchor)
    {
        if (_popup == null)
        {
            _popup = new WolfgateColorPopup();
            _popup.OnColorChanged += OnPopupColor;
            UserInterfaceManager.ModalRoot.AddChild(_popup);
        }

        _editing = index;
        _popup.Color = _colors[index];
        var origin = anchor.GlobalPosition + new Vector2(0, anchor.Height + 2);
        _popup.Open(UIBox2.FromDimensions(origin, new Vector2(260, 334)));
    }

    private void OnPopupColor(Color color)
    {
        if (_editing < 0 || _editing >= _colors.Count)
            return;

        _colors[_editing] = color;
        RefreshChips();
        _pending = 0;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_pending < 0)
            return;

        _pending += args.DeltaSeconds;
        if (_pending < SettleTime)
            return;

        _pending = -1;
        OnColorsChanged?.Invoke(_colors.ToList());
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        if (_popup == null)
            return;

        _popup.Close();
        _popup.Orphan();
        _popup = null;
    }
}
