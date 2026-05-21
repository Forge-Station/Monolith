using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Forge.BoardingTeleport;
using Content.Shared._Forge.BoardingTeleport.Components;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Forge.BoardingTeleport;

public sealed class BoardingTeleportWindow : FancyWindow
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private readonly BoardingTeleportBoundUserInterface _owner;
    private readonly BoxContainer _sectorPage;
    private readonly BoxContainer _gridPage;
    private readonly Label _statusHeaderLabel;
    private readonly Label _statusLabel;
    private readonly RichTextLabel _modeLabel;
    private readonly Label _modeStatsLabel;
    private readonly Label _platformStatsLabel;
    private readonly RichTextLabel _flavorLabel;
    private readonly Button _modeStealthButton;
    private readonly Button _modePreciseButton;
    private readonly Button _modeRapidButton;
    private readonly Button _clearTargetButton;
    private readonly Button _syncVolleyButton;
    private readonly Button _sharedLandingButton;
    private readonly BoxContainer _platformList;
    private readonly BoardingTeleportSectorMapControl _sectorMap;
    private readonly BoardingTeleportLandingMapControl _landingMap;

    public BoardingTeleportWindow(BoardingTeleportBoundUserInterface owner)
    {
        IoCManager.InjectDependencies(this);

        _owner = owner;
        Title = Loc.GetString("boarding-teleport-window-title");
        MinSize = new Vector2(900, 720);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(8),
        };

        var statusPanel = new PanelContainer
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 6),
        };
        statusPanel.AddStyleClass("BackgroundDark");

        var statusPanelContent = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8, 6, 8, 6),
        };

        _statusHeaderLabel = new Label
        {
            Text = Loc.GetString("boarding-teleport-window-status-header"),
            HorizontalExpand = true,
        };
        statusPanelContent.AddChild(_statusHeaderLabel);

        _statusLabel = new Label
        {
            Text = Loc.GetString("boarding-teleport-window-status-none"),
            HorizontalExpand = true,
        };
        statusPanelContent.AddChild(_statusLabel);

        _modeLabel = new RichTextLabel
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 2, 0, 0),
        };
        statusPanelContent.AddChild(_modeLabel);

        _modeStatsLabel = new Label
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 2, 0, 0),
            FontColorOverride = Color.FromHex("#C6CEDA"),
        };
        statusPanelContent.AddChild(_modeStatsLabel);

        _platformStatsLabel = new Label
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 2, 0, 0),
            FontColorOverride = Color.FromHex("#A8B4C8"),
        };
        statusPanelContent.AddChild(_platformStatsLabel);

        statusPanel.AddChild(statusPanelContent);
        root.AddChild(statusPanel);

        _sectorPage = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 6,
        };

        _sectorPage.AddChild(new Label
        {
            Text = Loc.GetString("boarding-teleport-window-sector-help"),
            HorizontalExpand = true,
            Margin = new Thickness(6, 0, 6, 4),
        });

        _sectorMap = new BoardingTeleportSectorMapControl
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = true,
        };
        _sectorMap.TargetCoordinatesSelected += coordinates => _owner.SelectGrid(coordinates);
        _sectorMap.TargetGridSelected += grid => _owner.SelectGrid(grid);
        _sectorPage.AddChild(_sectorMap);

        _gridPage = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Visible = false,
            SeparationOverride = 6,
        };

        var gridTopRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(6, 0, 6, 4),
        };

        var backButton = new Button
        {
            Text = Loc.GetString("boarding-teleport-window-back"),
        };
        backButton.OnPressed += _ => _owner.Back();
        gridTopRow.AddChild(backButton);

        gridTopRow.AddChild(new Control { HorizontalExpand = true });

        _clearTargetButton = new Button
        {
            Text = Loc.GetString("boarding-teleport-window-clear-target"),
        };
        _clearTargetButton.OnPressed += _ => _owner.ClearTarget();
        gridTopRow.AddChild(_clearTargetButton);

        _syncVolleyButton = new Button
        {
            Text = Loc.GetString("boarding-teleport-window-sync-volley"),
        };
        _syncVolleyButton.OnPressed += _ => _owner.SyncVolley();
        gridTopRow.AddChild(_syncVolleyButton);

        _sharedLandingButton = new Button();
        _sharedLandingButton.OnPressed += _ => _owner.ToggleSharedLanding();
        gridTopRow.AddChild(_sharedLandingButton);

        _gridPage.AddChild(gridTopRow);

        _platformList = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(8, 0, 8, 4),
        };
        _gridPage.AddChild(_platformList);

        _gridPage.AddChild(new Label
        {
            Text = Loc.GetString("boarding-teleport-window-grid-help"),
            HorizontalExpand = true,
            Margin = new Thickness(6, 0, 6, 4),
        });

        var modeRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
            Margin = new Thickness(8, 4, 8, 4),
        };

        _modeStealthButton = new Button
        {
            Text = Loc.GetString("boarding-teleport-window-mode-button-Stealth"),
            HorizontalExpand = true,
        };
        _modeStealthButton.OnPressed += _ => _owner.SelectMode(BoardingTeleportInsertionMode.Stealth);
        modeRow.AddChild(_modeStealthButton);

        _modePreciseButton = new Button
        {
            Text = Loc.GetString("boarding-teleport-window-mode-button-Precise"),
            HorizontalExpand = true,
        };
        _modePreciseButton.OnPressed += _ => _owner.SelectMode(BoardingTeleportInsertionMode.Precise);
        modeRow.AddChild(_modePreciseButton);

        _modeRapidButton = new Button
        {
            Text = Loc.GetString("boarding-teleport-window-mode-button-Rapid"),
            HorizontalExpand = true,
        };
        _modeRapidButton.OnPressed += _ => _owner.SelectMode(BoardingTeleportInsertionMode.Rapid);
        modeRow.AddChild(_modeRapidButton);

        var modeRowPanel = new PanelContainer
        {
            HorizontalExpand = false,
            VerticalExpand = false,
            MinSize = new Vector2(760, 0),
            MaxSize = new Vector2(880, 80),
        };
        modeRowPanel.AddStyleClass("BackgroundDark");
        modeRowPanel.AddChild(modeRow);

        var modeRowHost = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = false,
        };
        modeRowHost.AddChild(new Control { HorizontalExpand = true });
        modeRowHost.AddChild(modeRowPanel);
        modeRowHost.AddChild(new Control { HorizontalExpand = true });
        _gridPage.AddChild(modeRowHost);

        _landingMap = new BoardingTeleportLandingMapControl
        {
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        _landingMap.TileSelected += (grid, tile) => _owner.SelectTile(grid, tile);

        var gridMapPanel = new PanelContainer
        {
            HorizontalExpand = false,
            VerticalExpand = false,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            MinSize = new Vector2(760, 500),
            MaxSize = new Vector2(880, 620),
            Margin = new Thickness(8, 4, 8, 4),
        };
        gridMapPanel.AddStyleClass("BackgroundDark");
        gridMapPanel.AddChild(_landingMap);

        var gridMapRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        gridMapRow.AddChild(new Control { HorizontalExpand = true });
        gridMapRow.AddChild(gridMapPanel);
        gridMapRow.AddChild(new Control { HorizontalExpand = true });
        _gridPage.AddChild(gridMapRow);

        root.AddChild(_sectorPage);
        root.AddChild(_gridPage);

        var flavorPanel = new PanelContainer
        {
            HorizontalExpand = true,
            Margin = new Thickness(0, 6, 0, 0),
        };
        flavorPanel.AddStyleClass("BackgroundDark");
        _flavorLabel = new RichTextLabel
        {
            Margin = new Thickness(8, 4, 8, 4),
            HorizontalExpand = true,
        };
        _flavorLabel.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString("boarding-teleport-window-flavor-sector")));
        flavorPanel.AddChild(_flavorLabel);
        root.AddChild(flavorPanel);

        ContentsContainer.AddChild(root);
    }

    public void UpdateState(BoardingTeleportBoundUserInterfaceState state)
    {
        _statusLabel.Text = Loc.GetString($"boarding-teleport-status-{state.Status}");
        _statusLabel.FontColorOverride = state.Status switch
        {
            BoardingTeleportStatus.LandingSelected => Color.FromHex("#8DFF99"),
            BoardingTeleportStatus.TargetSelected => Color.FromHex("#FFE07A"),
            BoardingTeleportStatus.None => Color.FromHex("#D0D6E0"),
            _ => Color.FromHex("#FF8888"),
        };
        _modeLabel.SetMessage(FormattedMessage.FromMarkupOrThrow(GetModeMarkup(state)));
        _modeStatsLabel.Text = Loc.GetString("boarding-teleport-window-mode-stats",
            ("delay", $"{state.ModeDelaySeconds:0.0}"),
            ("scatter", $"{state.ModeScatter:0.00}"),
            ("risk", $"{state.ModeRiskPercent:0}"));

        _platformStatsLabel.Text = BuildPlatformStats(state);
        _platformStatsLabel.FontColorOverride = state.EngineRange is null
            ? Color.FromHex("#FF8888")
            : Color.FromHex("#A8B4C8");
        UpdatePlatformList(state);
        UpdateModeButtons(state.Mode);
        UpdateSharedLandingButton(state);

        var coordinates = _entManager.GetCoordinates(state.NavState.Coordinates);
        var shuttle = coordinates?.EntityId;
        _sectorMap.SetShuttle(shuttle);
        _sectorMap.SetConsole(_owner.Owner);
        _sectorMap.SetSelectedTargetName(state.TargetGrid is { } netTarget ? GetTargetName(netTarget) : null);

        _landingMap.ScatterRadius = state.ModeScatter;

        if (state.Page == BoardingTeleportPage.Sector)
        {
            _sectorPage.Visible = true;
            _gridPage.Visible = false;
            _clearTargetButton.Visible = state.TargetGrid != null;
            _flavorLabel.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString("boarding-teleport-window-flavor-sector")));
            _sectorMap.RebuildMapObjects();
            return;
        }

        _sectorPage.Visible = false;
        _gridPage.Visible = true;
        _clearTargetButton.Visible = true;
        _flavorLabel.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString("boarding-teleport-window-flavor-grid")));

        if (state.TargetGrid is not { } targetGrid)
            return;

        var grid = _entManager.GetEntity(targetGrid);
        if (!grid.Valid)
            return;

        _landingMap.MapUid = grid;
        _landingMap.NetGrid = targetGrid;
        _landingMap.ForceNavMapUpdate();

        if (state.SelectedLandingCoordinates is { } selectedLanding)
        {
            var landing = _entManager.GetCoordinates(selectedLanding);
            if (landing.IsValid(_entManager) &&
                _entManager.TryGetComponent<MapGridComponent>(grid, out var mapGrid))
            {
                var mapSys = _entManager.System<SharedMapSystem>();
                _landingMap.SetSelectedTile(mapSys.CoordinatesToTile(grid, mapGrid, landing));
            }
        }
        else if (state.LandingCoordinates is { } netLanding)
        {
            var landing = _entManager.GetCoordinates(netLanding);
            if (landing.IsValid(_entManager) &&
                _entManager.TryGetComponent<MapGridComponent>(grid, out var mapGrid))
            {
                var mapSys = _entManager.System<SharedMapSystem>();
                _landingMap.SetSelectedTile(mapSys.CoordinatesToTile(grid, mapGrid, landing));
            }
        }
    }

    private void UpdateSharedLandingButton(BoardingTeleportBoundUserInterfaceState state)
    {
        _sharedLandingButton.Text = state.UseSharedLandingZone
            ? Loc.GetString("boarding-teleport-window-shared-landing-on")
            : Loc.GetString("boarding-teleport-window-shared-landing-off");
        _sharedLandingButton.Modulate = state.UseSharedLandingZone
            ? Color.FromHex("#8DFF99")
            : Color.White;
    }

    private string BuildPlatformStats(BoardingTeleportBoundUserInterfaceState state)
    {
        var parts = new List<string>();

        if (state.EngineRange is { } range && state.EngineMaxTargetVelocity is { } speedLimit)
        {
            parts.Add(Loc.GetString("boarding-teleport-window-engine-stats",
                ("range", $"{range:0}"),
                ("speed", $"{speedLimit:0}")));
        }
        else
        {
            parts.Add(Loc.GetString("boarding-teleport-window-engine-missing"));
        }

        if (state.PlatformCooldownSeconds is { } cooldown and > 0.05f)
            parts.Add(Loc.GetString("boarding-teleport-window-platform-cooldown", ("seconds", $"{cooldown:0}")));

        parts.Add(Loc.GetString("boarding-teleport-window-return-window",
            ("seconds", $"{state.ReturnWindowSeconds:0}")));

        var returnRemaining = GetLocalReturnRemainingSeconds();
        if (returnRemaining is > 0.05f)
            parts.Add(Loc.GetString("boarding-teleport-window-return-remaining", ("seconds", $"{returnRemaining:0}")));

        if (state.ApcRiskBonusPercent > 0.05f)
            parts.Add(Loc.GetString("boarding-teleport-window-apc-risk", ("percent", $"{state.ApcRiskBonusPercent:0}")));

        if (state.LockAgeSeconds > 0.05f)
            parts.Add(Loc.GetString("boarding-teleport-window-lock-age", ("seconds", $"{state.LockAgeSeconds:0}")));

        if (state.LockScatterPenalty > 0.05f || state.LockRiskPenalty > 0.05f)
            parts.Add(Loc.GetString("boarding-teleport-window-lock-degrade",
                ("scatter", $"{state.LockScatterPenalty:0.00}"),
                ("risk", $"{state.LockRiskPenalty:0}")));

        return string.Join(" | ", parts);
    }

    private void UpdatePlatformList(BoardingTeleportBoundUserInterfaceState state)
    {
        _platformList.RemoveAllChildren();
        if (state.Platforms.Count == 0)
            return;

        _platformList.AddChild(new Label
        {
            Text = Loc.GetString("boarding-teleport-window-platform-list-header"),
            FontColorOverride = Color.FromHex("#C6CEDA"),
        });

        foreach (var entry in state.Platforms)
        {
            var cooldown = entry.CooldownSeconds is { } cd and > 0.05f
                ? Loc.GetString("boarding-teleport-window-platform-entry-cooldown", ("seconds", $"{cd:0}"))
                : Loc.GetString("boarding-teleport-window-platform-entry-ready");

            var landing = entry.HasLanding
                ? Loc.GetString("boarding-teleport-window-platform-entry-landing-yes")
                : Loc.GetString("boarding-teleport-window-platform-entry-landing-no");

            var button = new Button
            {
                Text = Loc.GetString("boarding-teleport-window-platform-entry",
                    ("name", entry.Name),
                    ("slot", entry.SlotIndex + 1),
                    ("cooldown", cooldown),
                    ("landing", landing)),
                HorizontalExpand = true,
                Modulate = entry.IsSelected ? Color.FromHex("#8DFF99") : Color.White,
            };

            var slot = entry.SlotIndex;
            button.OnPressed += _ => _owner.SelectPlatformSlot(slot);
            _platformList.AddChild(button);
        }
    }

    private float? GetLocalReturnRemainingSeconds()
    {
        var player = _playerManager.LocalSession?.AttachedEntity;
        if (player is not { } uid ||
            !_entManager.TryGetComponent<BoardingTeleportAnchorComponent>(uid, out var anchor) ||
            anchor.ExpiresAt is not { } expires)
        {
            return null;
        }

        var remaining = (expires - _timing.CurTime).TotalSeconds;
        return remaining > 0 ? (float) remaining : null;
    }

    private string GetModeMarkup(BoardingTeleportBoundUserInterfaceState state)
    {
        return state.Mode switch
        {
            BoardingTeleportInsertionMode.Precise => Loc.GetString("boarding-teleport-window-mode-summary-Precise"),
            BoardingTeleportInsertionMode.Rapid => Loc.GetString("boarding-teleport-window-mode-summary-Rapid"),
            _ => Loc.GetString("boarding-teleport-window-mode-summary-Stealth"),
        };
    }

    private void UpdateModeButtons(BoardingTeleportInsertionMode mode)
    {
        _modeStealthButton.Modulate = mode == BoardingTeleportInsertionMode.Stealth ? Color.FromHex("#6BB8FF") : Color.White;
        _modePreciseButton.Modulate = mode == BoardingTeleportInsertionMode.Precise ? Color.FromHex("#9BFF96") : Color.White;
        _modeRapidButton.Modulate = mode == BoardingTeleportInsertionMode.Rapid ? Color.FromHex("#FFAE63") : Color.White;
    }

    private string? GetTargetName(NetEntity targetGrid)
    {
        var uid = _entManager.GetEntity(targetGrid);
        if (!uid.Valid || !_entManager.TryGetComponent<MetaDataComponent>(uid, out var meta))
            return null;

        return meta.EntityName;
    }
}
