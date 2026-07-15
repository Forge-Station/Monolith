using System.Text.RegularExpressions;
using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Mind;
using Content.Server.Preferences.Managers;
using Content.Server.RandomMetadata;
using Content.Shared._Forge.Silicons.StationAi;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Players;
using Content.Shared.Popups;
using Content.Shared.Preferences;
using Content.Shared.Silicons.StationAi;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Silicons.StationAi;

/// <summary>
/// Owns station AI customization, personality replacement and delayed SSD release.
/// </summary>
public sealed class StationAiPersonalitySystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IServerPreferencesManager _preferences = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly GhostRoleSystem _ghostRoles = default!;
    [Dependency] private readonly RandomMetadataSystem _randomMetadata = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStationAiSystem _stationAi = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private static readonly Regex RestrictedNameRegex = new("[^а-яА-Яa-zA-Z-'0-9\\ ]");
    private static readonly Regex NameCaseRegex = new(@"^(?<word>\w)|\b(?<word>\w)(?=\w*$)");
    private static readonly TimeSpan CustomizationCooldown = TimeSpan.FromSeconds(60);
    private readonly Dictionary<EntityUid, TimeSpan> _ssdDeadlines = new();
    private TimeSpan _nextSsdCheck;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StationAiPersonalityComponent, MindAddedMessage>(OnMindAdded);
        SubscribeLocalEvent<StationAiPersonalityComponent, MindRemovedMessage>(OnMindRemoved);
        SubscribeLocalEvent<StationAiPersonalityComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<StationAiPersonalityComponent, PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<StationAiPersonalityComponent, ComponentShutdown>(OnPersonalityShutdown);
        SubscribeLocalEvent<StationAiHeldComponent, OpenStationAiCustomizationEvent>(OnOpenCustomization);
        SubscribeLocalEvent<StationAiHeldComponent, StationAiCustomizationApplyMessage>(OnApplyCustomization);
        SubscribeLocalEvent<StationAiScreenComponent, EntInsertedIntoContainerMessage>(OnCoreInsert);
        SubscribeLocalEvent<StationAiScreenComponent, EntRemovedFromContainerMessage>(OnCoreRemove);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_ssdDeadlines.Count == 0 || _timing.CurTime < _nextSsdCheck)
            return;

        _nextSsdCheck = _timing.CurTime + TimeSpan.FromSeconds(1);
        foreach (var (brain, deadline) in _ssdDeadlines.ToArray())
        {
            if (_timing.CurTime < deadline)
                continue;

            _ssdDeadlines.Remove(brain);
            ReleaseDisconnectedAi(brain);
        }
    }

    private void OnMindAdded(Entity<StationAiPersonalityComponent> ent, ref MindAddedMessage args)
    {
        _ssdDeadlines.Remove(ent.Owner);
        ent.Comp.Occupant = args.Mind.Comp.UserId;
        ent.Comp.NextCustomization = TimeSpan.Zero;
        ent.Comp.Screen = "StationAiScreenDefault";
        ent.Comp.Color = Color.White;

        var name = GetNewPersonalityName(ent.Owner, args.Mind.Comp.UserId);
        ApplyIdentity(ent.Owner, name, ent.Comp.Screen, ent.Comp.Color, args.Mind);

        if (HasComp<StationAiHeldComponent>(ent.Owner))
        {
            UpdateUi(ent.Owner, ent.Comp);
            _ui.TryOpenUi(ent.Owner, StationAiCustomizationUiKey.Key, ent.Owner);
        }
    }

    private void OnMindRemoved(Entity<StationAiPersonalityComponent> ent, ref MindRemovedMessage args)
    {
        _ssdDeadlines.Remove(ent.Owner);
        ent.Comp.Occupant = null;
        ent.Comp.NextCustomization = TimeSpan.Zero;
        ent.Comp.Screen = "StationAiScreenDefault";
        ent.Comp.Color = Color.White;

        ReopenGhostRole(ent.Owner);

        var name = GetFallbackName(ent.Owner);
        _metadata.SetEntityName(ent.Owner, name);
        if (_stationAi.TryGetCore(ent.Owner, out var core))
        {
            _metadata.SetEntityName(core.Owner, Prototype(core.Owner)?.Name ?? Loc.GetString("station-ai-core-default-name"));
            SetCoreScreen(core.Owner, ent.Comp.Screen, ent.Comp.Color, true);
        }
    }

    private void ReopenGhostRole(EntityUid brain)
    {
        if (!TryComp(brain, out GhostRoleComponent? role))
            return;

        EnsureComp<GhostTakeoverAvailableComponent>(brain);
        _ghostRoles.ReregisterGhostRole((brain, role));
    }

    private void OnPlayerAttached(Entity<StationAiPersonalityComponent> ent, ref PlayerAttachedEvent args)
    {
        _ssdDeadlines.Remove(ent.Owner);
    }

    private void OnPlayerDetached(Entity<StationAiPersonalityComponent> ent, ref PlayerDetachedEvent args)
    {
        if (ent.Comp.Occupant == null || !TryComp(ent.Owner, out MindContainerComponent? mind) || !mind.HasMind)
            return;

        // Use the server's ordinary player inactivity window for disconnected AI release.
        var timeout = Math.Max(1f, _configuration.GetCVar(CCVars.AfkTime));
        _ssdDeadlines[ent.Owner] = _timing.CurTime + TimeSpan.FromSeconds(timeout);
    }

    private void OnPersonalityShutdown(Entity<StationAiPersonalityComponent> ent, ref ComponentShutdown args)
    {
        _ssdDeadlines.Remove(ent.Owner);
    }

    private void OnOpenCustomization(Entity<StationAiHeldComponent> ent, ref OpenStationAiCustomizationEvent args)
    {
        if (args.Handled || !TryComp(ent.Owner, out StationAiPersonalityComponent? personality) || !IsCurrentController(ent.Owner))
            return;

        UpdateUi(ent.Owner, personality);
        args.Handled = _ui.TryOpenUi(ent.Owner, StationAiCustomizationUiKey.Key, ent.Owner);
    }

    private void OnApplyCustomization(Entity<StationAiHeldComponent> ent, ref StationAiCustomizationApplyMessage args)
    {
        if (args.Actor != ent.Owner ||
            !TryComp(ent.Owner, out StationAiPersonalityComponent? personality) ||
            !IsCurrentController(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("station-ai-customization-not-controller"), ent.Owner, args.Actor, PopupType.MediumCaution);
            return;
        }

        if (_timing.CurTime < personality.NextCustomization)
        {
            var seconds = Math.Max(1, (int) Math.Ceiling((personality.NextCustomization - _timing.CurTime).TotalSeconds));
            _popup.PopupEntity(Loc.GetString("station-ai-customization-cooldown", ("seconds", seconds)), ent.Owner, args.Actor, PopupType.MediumCaution);
            UpdateUi(ent.Owner, personality);
            return;
        }

        if (!_prototypes.HasIndex<StationAiScreenPrototype>(args.Screen))
        {
            _popup.PopupEntity(Loc.GetString("station-ai-customization-invalid-screen"), ent.Owner, args.Actor, PopupType.MediumCaution);
            return;
        }

        if (!TryNormalizeName(args.Name, out var name))
        {
            _popup.PopupEntity(Loc.GetString("station-ai-customization-invalid-name"), ent.Owner, args.Actor, PopupType.MediumCaution);
            return;
        }

        if (!TryNormalizeColor(args.Color, out var color))
        {
            _popup.PopupEntity(Loc.GetString("station-ai-customization-invalid-color"), ent.Owner, args.Actor, PopupType.MediumCaution);
            return;
        }

        if (Name(ent.Owner) == name && personality.Screen == args.Screen && personality.Color == color)
            return;

        personality.Screen = args.Screen;
        personality.Color = color;
        personality.NextCustomization = _timing.CurTime + CustomizationCooldown;
        ApplyIdentity(ent.Owner, name, personality.Screen, personality.Color);
        _adminLog.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(args.Actor):player} customized station AI {ToPrettyString(ent.Owner):entity} as '{name}' with screen '{args.Screen}' and color '{color}'.");
        _popup.PopupEntity(Loc.GetString("station-ai-customization-applied"), ent.Owner, args.Actor);
        UpdateUi(ent.Owner, personality);
    }

    private void OnCoreInsert(Entity<StationAiScreenComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != StationAiCoreComponent.Container)
            return;

        var screen = TryComp(args.Entity, out StationAiPersonalityComponent? personality)
            ? personality.Screen
            : (ProtoId<StationAiScreenPrototype>) "StationAiScreenDefault";
        var color = personality?.Color ?? Color.White;
        SetCoreScreen(ent.Owner, screen, color, true);
    }

    private void OnCoreRemove(Entity<StationAiScreenComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == StationAiCoreComponent.Container)
            SetCoreScreen(ent.Owner, "StationAiScreenDefault", Color.White, false);
    }

    private void ApplyIdentity(
        EntityUid brain,
        string name,
        ProtoId<StationAiScreenPrototype> screen,
        Color color,
        Entity<MindComponent>? mind = null)
    {
        _metadata.SetEntityName(brain, name);

        if (mind == null && TryComp(brain, out MindContainerComponent? container) &&
            container.Mind is { } mindUid && TryComp(mindUid, out MindComponent? mindComp))
        {
            mind = (mindUid, mindComp);
        }

        if (mind != null)
        {
            mind.Value.Comp.CharacterName = name;
            Dirty(mind.Value);
        }

        if (_stationAi.TryGetCore(brain, out var core))
        {
            _metadata.SetEntityName(core.Owner, name);
            SetCoreScreen(core.Owner, screen, color, true);
        }
    }

    private void SetCoreScreen(
        EntityUid core,
        ProtoId<StationAiScreenPrototype> screen,
        Color color,
        bool occupied)
    {
        if (!TryComp(core, out StationAiScreenComponent? component))
            return;

        component.Screen = screen;
        component.Color = color;
        component.Occupied = occupied;
        Dirty(core, component);
    }

    private bool IsCurrentController(EntityUid brain)
    {
        if (!HasComp<ActorComponent>(brain) || !_stationAi.TryGetCore(brain, out var core))
            return false;

        return _stationAi.TryGetHeld(core, out var held) && held == brain;
    }

    private void UpdateUi(EntityUid brain, StationAiPersonalityComponent personality)
    {
        var remaining = Math.Max(0, (int) Math.Ceiling((personality.NextCustomization - _timing.CurTime).TotalSeconds));
        _ui.SetUiState(brain,
            StationAiCustomizationUiKey.Key,
            new StationAiCustomizationState(Name(brain), personality.Screen, personality.Color, remaining));
    }

    private string GetNewPersonalityName(EntityUid brain, NetUserId? userId)
    {
        var preferred = _preferences.GetPreferencesOrNull(userId)?.SelectedCharacter.Name;
        if (!string.IsNullOrWhiteSpace(preferred) && TryNormalizeName(preferred, out var valid))
            return valid;

        return GetFallbackName(brain);
    }

    private string GetFallbackName(EntityUid brain)
    {
        if (TryComp(brain, out RandomMetadataComponent? random) && random.NameSegments is { Count: > 0 })
            return _randomMetadata.GetRandomFromSegments(random.NameSegments, random.NameSeparator);

        return Loc.GetString("station-ai-default-name");
    }

    private bool TryNormalizeName(string input, out string name)
    {
        name = input.Trim();
        if (name.Length == 0 || name.Length > HumanoidCharacterProfile.MaxNameLength)
            return false;

        if (_configuration.GetCVar(CCVars.RestrictedNames))
            name = RestrictedNameRegex.Replace(name, string.Empty).Trim();

        if (_configuration.GetCVar(CCVars.ICNameCase))
            name = NameCaseRegex.Replace(name, match => match.Groups["word"].Value.ToUpper());

        return name.Length > 0 && name.Length <= HumanoidCharacterProfile.MaxNameLength;
    }

    private static bool TryNormalizeColor(Color input, out Color color)
    {
        color = Color.White;
        if (!float.IsFinite(input.R) ||
            !float.IsFinite(input.G) ||
            !float.IsFinite(input.B) ||
            !float.IsFinite(input.A))
        {
            return false;
        }

        color = new Color(
            Math.Clamp(input.R, 0f, 1f),
            Math.Clamp(input.G, 0f, 1f),
            Math.Clamp(input.B, 0f, 1f),
            1f);
        return true;
    }

    private void ReleaseDisconnectedAi(EntityUid brain)
    {
        if (Deleted(brain) || HasComp<ActorComponent>(brain) ||
            !TryComp(brain, out StationAiPersonalityComponent? personality) ||
            personality.Occupant is not { } userId ||
            !TryComp(brain, out MindContainerComponent? container) ||
            container.Mind is not { } mindUid)
        {
            return;
        }

        if (_players.TryGetSessionById(userId, out var session) &&
            session.Status is not (SessionStatus.Disconnected or SessionStatus.Zombie))
        {
            return;
        }

        // Mind removal re-registers GhostTakeoverAvailable on the same brain and keeps the physical core intact.
        _mind.TransferTo(mindUid, null, createGhost: false);
    }
}
