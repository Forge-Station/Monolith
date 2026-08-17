using System.Numerics;
using Content.Client.GameTicking.Managers;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Client.UserInterface.Systems.Bwoink;
using Content.Client.UserInterface.Systems.EscapeMenu;
using Content.Client.UserInterface.Systems.Viewport;
using Content.Client.Voting.UI;
using Content.Shared._Forge.CCVar;
using Content.Shared._Forge.LobbyHub;
using Content.Shared.Input;
using Content.Shared.Preferences;
using Robust.Client.Console;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Graphics;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.Client._Forge.LobbyHub;

/// <summary>
/// Client-only walkable lobby: loads a local YAML grid, a local character dummy, and kiosks.
/// The server is only contacted for character saves, chat, ready/join/observe, and disconnect.
/// </summary>
public sealed partial class LobbyHubSystem : EntitySystem
{
    private const float WalkSpeed = 3.6f;
    private const float InteractRange = 1.55f;
    private static readonly Color HubAmbient = Color.FromSrgb(new Color((byte)210, (byte)205, (byte)190));

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IClientConsoleHost _console = default!;
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IStateManager _state = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IClientPreferencesManager _prefs = default!;
    [Dependency] private ClientGameTicker _ticker = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedEyeSystem _eye = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private MetaDataSystem _meta = default!;

    private readonly HashSet<Vector2i> _blocked = new();
    private readonly HashSet<Vector2i> _floors = new();

    private MapId _mapId = MapId.Nullspace;
    private EntityUid? _mapUid;
    private EntityUid? _gridUid;
    private EntityUid? _avatar;
    private Vector2 _spawnLocal = new(10.5f, 7.5f);
    private Vector2 _avatarLocal = new(10.5f, 7.5f);
    private bool _activateHeld;
    private string? _previousContext;
    private IEye? _previousEye;

    public bool IsActive { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;
    }

    public override void Shutdown()
    {
        Stop();
        base.Shutdown();
    }

    public void Start(LobbyGui lobby)
    {
        if (IsActive || !_cfg.GetCVar(ForgeCVars.LobbyHubEnabled))
            return;

        try
        {
            if (!TryLoadHubMap())
                return;

            ReloadAvatar(keepPosition: false);
            if (_avatar == null)
            {
                Log.Error("Lobby hub loaded the map but failed to spawn the dummy.");
                ClearHub();
                return;
            }

            _previousContext = _input.Contexts.ActiveContext.Name;
            _input.Contexts.SetActiveContext("human");
            _previousEye = _eyeManager.CurrentEye;
            _input.KeyBindStateChanged += OnKeyBindStateChanged;

            IsActive = true;

            lobby.HubViewport.ViewportSize = ViewportUIController.ViewportSize;
            lobby.HubViewport.AlwaysRender = true;
            lobby.HubPrompt.Text = Loc.GetString("lobby-hub-hint");
            lobby.SetHubMode(true);
            SyncEye(lobby);
        }
        catch (Exception e)
        {
            Log.Error($"Lobby hub failed to start, falling back to the classic lobby UI: {e}");
            Stop(lobby);
        }
    }

    public void Stop(LobbyGui? lobby = null)
    {
        _input.KeyBindStateChanged -= OnKeyBindStateChanged;

        if (_previousContext != null)
        {
            try
            {
                _input.Contexts.SetActiveContext(_previousContext);
            }
            catch (Exception e)
            {
                Log.Debug($"Lobby hub input context restore skipped: {e.Message}");
            }
        }

        _previousContext = null;

        if (_previousEye != null)
            _eyeManager.CurrentEye = _previousEye;
        _previousEye = null;

        if (lobby != null)
            lobby.HubViewport.Eye = null;

        ClearHub();
        IsActive = false;
        lobby?.SetHubMode(false);
    }

    public void ReloadAvatar(bool keepPosition = true)
    {
        if (_gridUid == null || !Exists(_gridUid.Value))
            return;

        var pos = keepPosition && _avatar != null && Exists(_avatar.Value)
            ? _xform.GetWorldPosition(_avatar.Value)
            : (Vector2?)null;

        if (_avatar != null)
        {
            Del(_avatar.Value);
            _avatar = null;
        }

        var lobbyUi = _ui.GetUIController<LobbyUIController>();
        var dummy = lobbyUi.LoadProfileEntity(
            _prefs.Preferences?.SelectedCharacter as HumanoidCharacterProfile,
            null,
            true);

        _avatar = dummy;
        DisableSimulation(dummy);
        var local = keepPosition ? _avatarLocal : _spawnLocal;
        _xform.SetCoordinates(dummy, new EntityCoordinates(_gridUid.Value, local));
        _avatarLocal = local;

        if (pos != null)
            _xform.SetWorldPosition(dummy, pos.Value);

        var eye = EnsureComp<EyeComponent>(dummy);
        _eye.SetDrawFov(dummy, false, eye);
        _eye.SetDrawLight((dummy, eye), false);
        _eye.SetZoom(dummy, new Vector2(1.15f, 1.15f), eye);
        _eyeManager.CurrentEye = eye.Eye;

        if (_prefs.Preferences?.SelectedCharacter is HumanoidCharacterProfile profile)
            _meta.SetEntityName(dummy, profile.Name);
    }

    public override void FrameUpdate(float frameTime)
    {
        if (!IsActive || _state.CurrentState is not LobbyState lobbyState)
            return;

        var lobby = lobbyState.Lobby;
        if (lobby == null)
            return;

        SyncEye(lobby);
        UpdatePrompt(lobby, lobbyState);
        TryInteractHeld();

        if (ShouldBlockInput(lobby))
            return;

        MoveAvatar(frameTime);
    }

    private void SyncEye(LobbyGui lobby)
    {
        if (_avatar == null || !TryComp(_avatar.Value, out EyeComponent? eye))
            return;

        _eyeManager.CurrentEye = eye.Eye;
        lobby.HubViewport.Eye = eye.Eye;
    }

    private bool TryLoadHubMap()
    {
        ClearHub();

        var path = new ResPath(_cfg.GetCVar(ForgeCVars.LobbyHubMap));
        _mapUid = _map.CreateMap(out _mapId);
        _map.SetAmbientLight(_mapId, HubAmbient);

        var opts = new DeserializationOptions
        {
            LogOrphanedGrids = false,
        };

        if (!_loader.TryLoadGrid(_mapId, path, out var grid, opts))
        {
            Log.Error($"Failed to load lobby hub map '{path}'. Keep forge.lobby_hub.map pointed at a single-grid YAML.");
            ClearHub();
            return false;
        }

        _gridUid = grid.Value.Owner;
        _xform.SetMapCoordinates(grid.Value, new MapCoordinates(Vector2.Zero, _mapId));
        DisableSimulation(_gridUid.Value);
        RebuildCollision(grid.Value);
        ResolveSpawn();
        return true;
    }

    private void RebuildCollision(Entity<MapGridComponent> grid)
    {
        _blocked.Clear();
        _floors.Clear();

        foreach (var tile in _map.GetAllTiles(grid.Owner, grid.Comp))
            _floors.Add(tile.GridIndices);

        var query = EntityQueryEnumerator<LobbyHubBlockerComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid != _gridUid && xform.ParentUid != _gridUid)
                continue;

            _blocked.Add(_map.CoordinatesToTile(grid.Owner, grid.Comp, xform.Coordinates));
        }
    }

    private void ResolveSpawn()
    {
        var query = EntityQueryEnumerator<LobbyHubSpawnComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid != _gridUid && xform.ParentUid != _gridUid)
                continue;

            _spawnLocal = xform.LocalPosition;
            _avatarLocal = _spawnLocal;
            return;
        }

        if (_floors.Count == 0)
            return;

        var sum = Vector2.Zero;
        foreach (var tile in _floors)
            sum += new Vector2(tile.X + 0.5f, tile.Y + 0.5f);

        _spawnLocal = sum / _floors.Count;
        _avatarLocal = _spawnLocal;
        Log.Warning("Lobby hub map has no LobbyHubSpawn marker; using the floor centroid.");
    }

    /// <summary>
    /// Hub walking is tile-based, not Box2D. Keep the grid and dummy out of the physics islands.
    /// </summary>
    private void DisableSimulation(EntityUid uid)
    {
        if (!TryComp<PhysicsComponent>(uid, out var body))
            return;

        _physics.SetCanCollide(uid, false, body: body);
        _physics.SetAwake((uid, body), false);
        _physics.SetBodyType(uid, BodyType.Static, body: body);
    }

    private void ClearHub()
    {
        _blocked.Clear();
        _floors.Clear();
        _avatar = null;

        try
        {
            if (_mapUid != null && Exists(_mapUid.Value))
                _map.DeleteMap(_mapId);
        }
        catch (Exception e)
        {
            Log.Debug($"Lobby hub map cleanup skipped: {e.Message}");
        }

        _mapUid = null;
        _gridUid = null;
        _mapId = MapId.Nullspace;
        _avatarLocal = _spawnLocal;
    }

    private void MoveAvatar(float frameTime)
    {
        if (_avatar == null || _gridUid == null)
            return;

        var dir = Vector2.Zero;
        if (IsDown(EngineKeyFunctions.MoveUp))
            dir += new Vector2(0, 1);
        if (IsDown(EngineKeyFunctions.MoveDown))
            dir += new Vector2(0, -1);
        if (IsDown(EngineKeyFunctions.MoveLeft))
            dir += new Vector2(-1, 0);
        if (IsDown(EngineKeyFunctions.MoveRight))
            dir += new Vector2(1, 0);

        if (dir == Vector2.Zero)
            return;

        dir = dir.Normalized();
        var delta = dir * WalkSpeed * frameTime;
        var current = _xform.GetWorldPosition(_avatar.Value);
        var desired = current + delta;

        var local = Transform(_avatar.Value).Coordinates.Position + delta;
        var tile = new Vector2i((int)MathF.Floor(local.X), (int)MathF.Floor(local.Y));
        if (_blocked.Contains(tile) || !_floors.Contains(tile))
            return;

        _xform.SetWorldPositionRotation(_avatar.Value, desired, delta.ToWorldAngle());
        _avatarLocal = local;
    }

    private void UpdatePrompt(LobbyGui lobby, LobbyState lobbyState)
    {
        if (TryGetNearby(out _, out var interact) && interact != null)
        {
            lobby.HubPrompt.Text = GetPrompt(interact, lobbyState);
            return;
        }

        lobby.HubPrompt.Text = Loc.GetString("lobby-hub-hint");
    }

    private string GetPrompt(LobbyHubInteractableComponent interact, LobbyState lobbyState)
    {
        if (interact.Action == LobbyHubAction.ReadyOrJoin)
        {
            if (_ticker.IsGameStarted)
                return Loc.GetString("lobby-hub-prompt-join");

            var status = Loc.GetString(_ticker.AreWeReady ? "lobby-hub-ready-yes" : "lobby-hub-ready-no");
            return Loc.GetString("lobby-hub-prompt-ready", ("status", status));
        }

        return Loc.GetString(interact.Prompt);
    }

    private void TryInteractHeld()
    {
        var activate = IsDown(ContentKeyFunctions.ActivateItemInWorld);
        if (activate && !_activateHeld && !ShouldBlockInput(GetLobby()))
            TryInteractNearest();
        _activateHeld = activate;
    }

    private void OnKeyBindStateChanged(ViewportBoundKeyEventArgs args)
    {
        if (!IsActive || args.KeyEventArgs.State != BoundKeyState.Down)
            return;

        if (ShouldBlockInput(GetLobby()))
            return;

        if (args.KeyEventArgs.Function != EngineKeyFunctions.Use)
            return;

        var lobby = GetLobby();
        if (lobby == null)
            return;

        var mouse = _input.MouseScreenPosition;
        if (!mouse.IsValid)
            return;

        var mapPos = lobby.HubViewport.PixelToMap(mouse.Position);
        if (mapPos.MapId != _mapId)
            return;

        TryInteractAt(mapPos.Position);
        args.KeyEventArgs.Handle();
    }

    private void TryInteractNearest()
    {
        if (TryGetNearby(out _, out var interact) && interact != null)
            RunAction(interact.Action);
    }

    private void TryInteractAt(Vector2 worldPos)
    {
        if (_avatar == null)
            return;

        var avatarPos = _xform.GetWorldPosition(_avatar.Value);
        LobbyHubInteractableComponent? bestComp = null;
        var bestDist = InteractRange;

        var query = EntityQueryEnumerator<LobbyHubInteractableComponent, TransformComponent>();
        while (query.MoveNext(out _, out var interact, out var xform))
        {
            if (xform.MapID != _mapId)
                continue;

            var pos = _xform.GetWorldPosition(xform);
            var clickDist = (pos - worldPos).Length();
            if (clickDist > 0.75f)
                continue;

            var walkDist = (pos - avatarPos).Length();
            if (walkDist > InteractRange || clickDist >= bestDist)
                continue;

            bestDist = clickDist;
            bestComp = interact;
        }

        if (bestComp != null)
            RunAction(bestComp.Action);
    }

    private bool TryGetNearby(out EntityUid uid, out LobbyHubInteractableComponent? interact)
    {
        uid = default;
        interact = null;

        if (_avatar == null)
            return false;

        var avatarPos = _xform.GetWorldPosition(_avatar.Value);
        var bestDist = InteractRange;

        var query = EntityQueryEnumerator<LobbyHubInteractableComponent, TransformComponent>();
        while (query.MoveNext(out var ent, out var comp, out var xform))
        {
            if (xform.MapID != _mapId)
                continue;

            var dist = (_xform.GetWorldPosition(xform) - avatarPos).Length();
            if (dist >= bestDist)
                continue;

            bestDist = dist;
            uid = ent;
            interact = comp;
        }

        return interact != null;
    }

    private void RunAction(LobbyHubAction action)
    {
        if (_state.CurrentState is not LobbyState lobby)
            return;

        switch (action)
        {
            case LobbyHubAction.CharacterSetup:
                lobby.OpenCharacterSetup();
                break;
            case LobbyHubAction.ReadyOrJoin:
                lobby.ReadyOrJoinFromHub();
                break;
            case LobbyHubAction.Observe:
                lobby.OpenObserveFromHub();
                break;
            case LobbyHubAction.Options:
                _ui.GetUIController<OptionsUIController>().ToggleWindow();
                break;
            case LobbyHubAction.AHelp:
                _ui.GetUIController<AHelpUIController>().ToggleWindow();
                break;
            case LobbyHubAction.Sponsor:
                lobby.Lobby?.OpenSponsorWindow();
                break;
            case LobbyHubAction.Vote:
                new VoteCallMenu().OpenCentered();
                break;
            case LobbyHubAction.Leave:
                _console.ExecuteCommand("disconnect");
                break;
        }
    }

    private bool ShouldBlockInput(LobbyGui? lobby)
    {
        if (lobby == null)
            return true;

        if (lobby.CharacterSetupState.Visible)
            return true;

        return _ui.KeyboardFocused is LineEdit or TextEdit;
    }

    private LobbyGui? GetLobby()
    {
        return _state.CurrentState is LobbyState state ? state.Lobby : null;
    }

    private bool IsDown(BoundKeyFunction function)
    {
        foreach (var down in _input.DownKeyFunctions)
        {
            if (down == function)
                return true;
        }

        return false;
    }
}
