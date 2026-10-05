using System.Numerics;
using Content.Client._Shitmed.UserInterface.Systems.Targeting.Widgets;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared._WF.Planets;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Player;

namespace Content.Client._WF.Planets;

/// <summary>Shows planetary environment data only while the local player wears a marked timepiece.</summary>
public sealed partial class WFPlanetTimepieceHudSystem : EntitySystem
{
    private const float RefreshInterval = 0.25f;

    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IResourceCache _resources = default!;

    private WFPlanetTimepieceHud _hud = default!;
    private float _untilRefresh;
    private bool _active;

    public bool HudVisible => _hud.Visible;
    public string DisplayedPlanet => _hud.PlanetText;
    public string DisplayedTime => _hud.TimeText;
    public string DisplayedWeather => _hud.WeatherText;
    public float HudWidth => WFPlanetTimepieceHud.PanelWidth;

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

        SubscribeLocalEvent<InventoryComponent, DidEquipEvent>(OnEquipmentChanged);
        SubscribeLocalEvent<InventoryComponent, DidUnequipEvent>(OnEquipmentChanged);
        SubscribeLocalEvent<WFPlanetTimepieceComponent, ComponentStartup>(OnMarkerStartup);
        SubscribeLocalEvent<WFPlanetTimepieceComponent, ComponentRemove>(OnMarkerRemove);
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
        if (!_active)
            return;
        PositionHud();
        if (_player.LocalEntity is not { } player || !IsWorn(player))
        {
            SetActive(false);
            return;
        }

        RefreshReadout();
    }

    private void OnEquipmentChanged(EntityUid uid, InventoryComponent component, DidEquipEvent args)
    {
        RefreshVisibility(uid);
    }

    private void OnEquipmentChanged(EntityUid uid, InventoryComponent component, DidUnequipEvent args)
    {
        RefreshVisibility(uid);
    }

    private void OnMarkerStartup(Entity<WFPlanetTimepieceComponent> ent, ref ComponentStartup args)
    {
        RefreshLocalVisibility();
    }

    private void OnMarkerRemove(Entity<WFPlanetTimepieceComponent> ent, ref ComponentRemove args)
    {
        RefreshLocalVisibility();
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

        SetActive(IsWorn(player));
    }

    private bool IsWorn(EntityUid player)
    {
        var slots = _inventory.GetSlotEnumerator(player, SlotFlags.ARMBANDLEFT | SlotFlags.ARMBANDRIGHT);
        while (slots.NextItem(out var item))
        {
            if (HasComp<WFPlanetTimepieceComponent>(item))
                return true;
        }

        return false;
    }

    private void SetActive(bool active)
    {
        if (_active == active)
            return;
        _active = active;
        _hud.Visible = active;
        _untilRefresh = 0f;

        if (active)
            RefreshReadout();
    }

    private void PositionHud()
    {
        var target = _ui.GetActiveUIWidgetOrNull<TargetingControl>();
        var right = _ui.PopupRoot.Width - 12;
        var bottom = _ui.PopupRoot.Height - 150;
        if (target is { Visible: true } && target.Width > 0)
        {
            var position = target.GlobalPosition - _ui.PopupRoot.GlobalPosition;
            right = position.X + target.Width;
            bottom = position.Y - 8;
        }
        LayoutContainer.SetPosition(_hud, new Vector2(
            MathF.Max(0, right - WFPlanetTimepieceHud.PanelWidth),
            MathF.Max(0, bottom - MathF.Max(72, _hud.Height))));
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
            return;
        }

        var hours = environment.MinuteOfDay / 60;
        var minutes = environment.MinuteOfDay % 60;
        _hud.SetReadout(environment.PlanetName, $"{hours:00}:{minutes:00}", environment.Weather);
    }
}
