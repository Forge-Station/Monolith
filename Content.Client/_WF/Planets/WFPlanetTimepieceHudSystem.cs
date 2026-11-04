using System.Numerics;
using Content.Client._WF.Cockpit;
using Content.Shared._WF.Warmth;
using Content.Shared.GameTicking;
using Content.Shared._WF.Planets;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Player;

namespace Content.Client._WF.Planets;

/// <summary>Shows the planet's local time and weather to anyone standing on a planet.</summary>
public sealed partial class WFPlanetTimepieceHudSystem : EntitySystem
{
    private const float RefreshInterval = 0.25f;

    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IResourceCache _resources = default!;

    private WFPlanetTimepieceHud _hud = default!;
    private float _untilRefresh;
    private bool _active;

    public bool HudVisible => _hud.Visible;
    public string DisplayedPlanet => _hud.PlanetText;
    public string DisplayedTime => _hud.TimeText;
    public string DisplayedWeather => _hud.WeatherText;
    public string DisplayedClimate => _hud.ClimateText;
    public string DisplayedHere => _hud.HereText;
    public string DisplayedFeel => _hud.FeelText;
    public float HudWidth => MathF.Max(_hud.Width, WFPlanetTimepieceHud.PanelWidth);
    public Control Hud => _hud;

    public override void Initialize()
    {
        UpdatesOutsidePrediction = true;
        base.Initialize();

        _hud = new WFPlanetTimepieceHud(_resources)
        {
            Visible = false,
        };
        _ui.PopupRoot.AddChild(_hud);
        LayoutContainer.SetAnchorPreset(_hud, LayoutContainer.LayoutPreset.TopLeft);

        SubscribeLocalEvent<WFPlanetEnvironmentComponent, AfterAutoHandleStateEvent>(OnEnvironmentState);
        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Shutdown()
    {
        _hud.Orphan();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _untilRefresh -= frameTime;
        if (_untilRefresh > 0f)
            return;

        _untilRefresh = RefreshInterval;
        RefreshLocalVisibility();
        ApplyVisibility();
        if (!_active)
            return;
        PositionHud();
        if (_player.LocalEntity is not { } player || !IsOnPlanet(player))
        {
            SetActive(false);
            return;
        }

        RefreshReadout();
    }

    private void OnEnvironmentState(Entity<WFPlanetEnvironmentComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!_active ||
            _player.LocalEntity is not { } player ||
            !TryComp(player, out TransformComponent? xform) ||
            xform.MapUid != ent.Owner)
        {
            return;
        }

        RefreshReadout();
    }

    private void OnPlayerAttached(LocalPlayerAttachedEvent args)
    {
        RefreshVisibility(args.Entity);
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        SetActive(false);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        SetActive(false);
    }

    private void RefreshLocalVisibility()
    {
        if (_player.LocalEntity is { } player)
            RefreshVisibility(player);
        else
            SetActive(false);
    }

    private void RefreshVisibility(EntityUid player)
    {
        if (player != _player.LocalEntity)
            return;

        SetActive(IsOnPlanet(player));
    }

    private bool IsOnPlanet(EntityUid player)
    {
        return TryComp(player, out TransformComponent? xform) && HasComp<WFPlanetEnvironmentComponent>(xform.MapUid);
    }

    private void SetActive(bool active)
    {
        if (_active == active)
            return;
        _active = active;
        ApplyVisibility();
        _untilRefresh = 0f;

        if (active)
            RefreshReadout();
    }

    /// <summary>Shows the panel on a planet, except under the cockpit, which has no HUD corner for it.</summary>
    private void ApplyVisibility()
    {
        _hud.Visible = _active && !_ui.GetUIController<WFCockpitUIController>().Active;
    }

    private void PositionHud()
    {
        var size = new Vector2(
            MathF.Max(_hud.Width, WFPlanetTimepieceHud.PanelWidth),
            MathF.Max(_hud.Height, WFPlanetTimepieceHud.PanelHeight));
        LayoutContainer.SetPosition(_hud, WFHudPlacement.Place(_ui, size));
    }

    private void RefreshReadout()
    {
        if (!_active ||
            _player.LocalEntity is not { } player ||
            !TryComp(player, out TransformComponent? xform) ||
            xform.MapUid is not { } map ||
            !TryComp<WFPlanetEnvironmentComponent>(map, out var environment))
        {
            _hud.SetReadout(Loc.GetString("wf-planet-timepiece-no-signal"), "--:--", string.Empty);
            _hud.SetThermal(string.Empty, string.Empty, Color.White);
            _hud.SetGauge(false, 50, WFWarmthTrend.Steady, false);
            return;
        }

        if (TryComp(player, out WFWarmthComponent? warmth))
        {
            _hud.SetThermal(SharedWFWarmthSystem.HereText(warmth),
                Loc.GetString(SharedWFWarmthSystem.FeelLoc(warmth.Feel)), SharedWFWarmthSystem.FeelColor(warmth.Feel));
            _hud.SetGauge(true, warmth.Gauge, warmth.Trend, warmth.NearWarmth);
        }
        else
        {
            _hud.SetThermal(string.Empty, string.Empty, Color.White);
            _hud.SetGauge(false, 50, WFWarmthTrend.Steady, false);
        }

        var hours = environment.MinuteOfDay / 60;
        var minutes = environment.MinuteOfDay % 60;
        _hud.SetReadout(environment.PlanetName, $"{hours:00}:{minutes:00}", environment.Weather,
            ClimateText(environment), environment.Season != null ? environment.SeasonColor : null);
    }

    /// <summary>The season and outdoor temperature line; either half is left out when the world has none.</summary>
    public string ClimateText(WFPlanetEnvironmentComponent environment)
    {
        var temperature = environment.TemperatureCelsius is { } celsius
            ? Loc.GetString("wf-planet-temperature-outside", ("celsius", celsius))
            : null;
        var season = environment.Season is { } name ? Loc.GetString(name) : null;
        if (season != null && temperature != null)
            return Loc.GetString("wf-planet-timepiece-climate", ("season", season), ("temperature", temperature));
        return season ?? temperature ?? string.Empty;
    }
}
