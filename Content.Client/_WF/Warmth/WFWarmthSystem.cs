using System.Numerics;
using Content.Client._WF.Cockpit;
using Content.Client._WF.Planets;
using Content.Shared._WF.Warmth;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Warmth;

/// <summary>
/// The local body's warmth: the frost or heat screen effect, the slowdown once the server says so, and off a planet a
/// small readout of the air here and how the body feels, shown while either is worth knowing.
/// </summary>
public sealed class WFWarmthSystem : SharedWFWarmthSystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private WFPlanetTimepieceHudSystem _planetHud = default!;

    private const float RefreshInterval = 0.25f;

    /// <summary>Off a planet the readout shows when the air is outside this range, in degrees Celsius.</summary>
    private const int MildLow = 10;
    private const int MildHigh = 35;

    private WFWarmthOverlay _overlay = default!;
    private WFWarmthHud _hud = default!;
    private float _untilRefresh;

    public bool CompactVisible => _hud.Visible;
    public string CompactHere => _hud.HereText;
    public string CompactFeel => _hud.FeelText;

    public override void Initialize()
    {
        UpdatesOutsidePrediction = true;
        base.Initialize();
        SubscribeLocalEvent<WFWarmthComponent, AfterAutoHandleStateEvent>(OnState);

        _overlay = new WFWarmthOverlay();
        _overlays.AddOverlay(_overlay);

        _hud = new WFWarmthHud(_resources) { Visible = false };
        _ui.PopupRoot.AddChild(_hud);
        LayoutContainer.SetAnchorPreset(_hud, LayoutContainer.LayoutPreset.TopLeft);
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay(_overlay);
        _hud.Orphan();
        base.Shutdown();
    }

    private void OnState(Entity<WFWarmthComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Movement.RefreshMovementSpeedModifiers(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _untilRefresh -= frameTime;
        if (_untilRefresh > 0f)
            return;

        _untilRefresh = RefreshInterval;
        if (_player.LocalEntity is not { } player || !TryComp(player, out WFWarmthComponent? warmth))
        {
            _hud.Visible = false;
            return;
        }

        var show = !_planetHud.HudVisible &&
                   !_ui.GetUIController<WFCockpitUIController>().Active &&
                   (warmth.Feel != WFBodyFeel.Comfortable || warmth.NearWarmth ||
                    warmth.HereCelsius is not { } here || here <= MildLow || here >= MildHigh);
        _hud.Visible = show;
        if (!show)
            return;

        _hud.SetReadout(HereText(warmth), Loc.GetString(FeelLoc(warmth.Feel)), FeelColor(warmth.Feel));
        _hud.SetGauge(warmth.Gauge, warmth.Trend, warmth.NearWarmth);
        PositionHud();
    }

    /// <summary>Where the planet panel would sit: above the targeting doll, at the screen's right.</summary>
    private void PositionHud()
    {
        var size = new Vector2(
            MathF.Max(_hud.Width, WFWarmthHud.PanelWidth),
            MathF.Max(_hud.Height, WFWarmthHud.PanelHeight));
        LayoutContainer.SetPosition(_hud, WFHudPlacement.Place(_ui, size));
    }
}
