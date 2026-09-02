/// Forge-Chane-Start
using Content.Shared.Decals;
using Content.Shared.SprayPainter;
using Content.Shared.SprayPainter.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client.SprayPainter.UI;

/// <summary>
/// A BUI for a spray painter. Allows selecting pipe colours, decals, and paintable object types sorted by category.
/// </summary>
public sealed class SprayPainterBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private SprayPainterWindow? _window;

    /// <summary>
    /// True while applying networked component state to the window.
    /// Prevents sync writes from emitting BUI messages (which flood "not subscribed" logs).
    /// </summary>
    private bool _syncing;

    private Color? _lastSentDecalColor;
    private int? _lastSentAngle;

    protected override void Open()
    {
        base.Open();

        if (_window == null)
        {
            _window = this.CreateWindow<SprayPainterWindow>();

            _window.OnSpritePicked += OnSpritePicked;
            _window.OnSetPipeColor += OnSetPipeColor;
            _window.OnTabChanged += OnTabChanged;
            _window.OnDecalChanged += OnDecalChanged;
            _window.OnDecalColorChanged += OnDecalColorChanged;
            _window.OnDecalAngleChanged += OnDecalAngleChanged;
            _window.OnDecalSnapChanged += OnDecalSnapChanged;
            _window.OnDecalColorPickerToggled += OnDecalColorPickerToggled;
        }

        var sprayPainter = EntMan.System<SprayPainterSystem>();
        _window.PopulateCategories(sprayPainter.PaintableStylesByGroup, sprayPainter.PaintableGroupsByCategory, sprayPainter.Decals);
        Update();

        if (EntMan.TryGetComponent(Owner, out SprayPainterComponent? sprayPainterComp))
        {
            _syncing = true;
            try
            {
                _window.SetSelectedTab(sprayPainterComp.SelectedTab);
            }
            finally
            {
                _syncing = false;
            }
        }
    }

    public override void Update()
    {
        if (_window == null)
            return;

        if (!EntMan.TryGetComponent(Owner, out SprayPainterComponent? sprayPainter))
            return;

        _syncing = true;
        try
        {
            _window.PopulateColors(sprayPainter.ColorPalette);
            if (sprayPainter.PickedColor != null)
                _window.SelectColor(sprayPainter.PickedColor);
            _window.SetSelectedStyles(sprayPainter.StylesByGroup);
            _window.SetSelectedDecal(sprayPainter.SelectedDecal);
            _window.SetDecalAngle(sprayPainter.SelectedDecalAngle);
            _window.SetDecalColor(sprayPainter.SelectedDecalColor);
            _window.SetDecalSnap(sprayPainter.SnapDecals);
            _window.SetDecalColorPicker(sprayPainter.ColorPickerEnabled);

            _lastSentDecalColor = sprayPainter.SelectedDecalColor;
            _lastSentAngle = sprayPainter.SelectedDecalAngle;
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// SendMessage (not predicted) so unsubscribed clients silently drop instead of spamming the server log.
    /// </summary>
    private void TrySend(BoundUserInterfaceMessage message)
    {
        if (_syncing || !IsOpened)
            return;

        SendMessage(message);
    }

    private void OnDecalSnapChanged(bool snap)
    {
        TrySend(new SprayPainterSetDecalSnapMessage(snap));
    }

    private void OnDecalAngleChanged(int angle)
    {
        if (_lastSentAngle == angle)
            return;

        _lastSentAngle = angle;
        TrySend(new SprayPainterSetDecalAngleMessage(angle));
    }

    private void OnDecalColorChanged(Color? color)
    {
        if (_lastSentDecalColor == color)
            return;

        _lastSentDecalColor = color;
        TrySend(new SprayPainterSetDecalColorMessage(color));
    }

    private void OnDecalChanged(ProtoId<DecalPrototype> protoId)
    {
        TrySend(new SprayPainterSetDecalMessage(protoId));
    }

    private void OnTabChanged(int index, bool isSelectedTabWithDecals)
    {
        TrySend(new SprayPainterTabChangedMessage(index, isSelectedTabWithDecals));
    }

    private void OnSpritePicked(string group, string style)
    {
        TrySend(new SprayPainterSetPaintableStyleMessage(group, style));
    }

    private void OnSetPipeColor(ItemList.ItemListSelectedEventArgs args)
    {
        var key = _window?.IndexToColorKey(args.ItemIndex);
        TrySend(new SprayPainterSetPipeColorMessage(key));
    }

    private void OnDecalColorPickerToggled(bool toggle)
    {
        TrySend(new SprayPainterSetDecalColorPickerMessage(toggle));
    }
}
/// Forge-Chane-End
///
