using Content.Shared.Antag.Selectors;
using Content.Shared.Destructible.Thresholds;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Whitelist;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Shared.Antag.Components;

[RegisterComponent, Access(typeof(AntagSelectionSystem))]
public sealed partial class AntagSelectionComponent : Component
{
    /// <summary>
    /// Has the primary assignment of antagonists been handled yet?
    /// This is typically set to true at the start of antag assignment for a game rule.
    /// Note that this can be true even before all antags have been assigned.
    /// </summary>
    [DataField]
    public bool AssignmentHandled;

    /// <summary>
    /// Has the antagonists been preselected but yet to be fully assigned?
    /// </summary>
    [DataField]
    public bool PreSelectionsComplete;

    /// <summary>
    /// If true, players that late join into a round have a chance of being converted into antagonists for this game rule.
    /// </summary>
    [DataField]
    public bool LateJoinAdditional;

    /// <summary>
    /// The antag specifiers for the antagonists.
    /// Empty when a local game rule still uses the old definitions list.
    /// </summary>
    [DataField]
    public AntagCountSelector[] Antags = [];

    /// <summary>
    /// Legacy YAML field kept so unfinished local game rules still deserialize.
    /// Runtime selection uses <see cref="Antags"/> after the specifier migration.
    /// </summary>
    [DataField]
    public List<AntagSelectionDefinition> Definitions = new();

    /// <summary>
    /// Cached sessions of antag definitions and selected players.
    /// Players in this dict are not guaranteed to have been assigned the role yet, and may be removed if they fail to initialize as an antag.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<AntagSpecifierPrototype>, HashSet<ICommonSession>> PreSelectedSessions = new();

    /// <summary>
    /// The minds and original names of the players assigned to be antagonists, as well as their assigned antag.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<AntagSpecifierPrototype>, HashSet<(EntityUid uid, string name)>> AssignedMinds = new();

    /// <summary>
    /// When the antag selection will occur.
    /// </summary>
    [DataField]
    public AntagSelectionTime SelectionTime = AntagSelectionTime.RuleStarted;

    /// <summary>
    /// Locale id for the name of the antag.
    /// If this is set then the antag is listed in the round-end summary.
    /// </summary>
    [DataField]
    public LocId? AgentName;

    /// <summary>
    /// If the player is pre-selected but fails to spawn in (e.g. due to only having antag-immune jobs selected),
    /// should they be removed from the pre-selection list?
    /// </summary>
    [DataField]
    public bool RemoveUponFailedSpawn = true;
}

/// <remarks>
///     Regardless of this value, antags are only initialized after the game rule activates.
///     If a game rule does not have a delayed activation, the antag will be initialized at the same time as this enum.
///     Otherwise, it will not be initialized until the game rule becomes active.
/// </remarks>
public enum AntagSelectionTime : byte
{
    /// <summary>
    /// Antag roles are selected at <see cref="RulePlayerSpawningEvent"/>
    /// </summary>
    PrePlayerSpawn,

    /// <summary>
    /// Antag roles are selected at <see cref="RulePlayerJobsAssignedEvent"/>
    /// </summary>
    JobsAssigned,

    /// <summary>
    /// Antag roles are selected at <see cref="GameRuleStartedEvent"/>
    /// or <see cref="RulePlayerJobsAssignedEvent"/> if the game rule was started before spawning.
    /// This is the latest an antag can be selected.
    /// </summary>
    RuleStarted,

    /// <summary>
    /// Antag roles are *never* selected. Instead, this definition only makes ghost roles.
    /// </summary>
    Never,
}

/// <summary>
/// Legacy per-rule antag definition used by local YAML that has not yet migrated to
/// <see cref="AntagSpecifierPrototype"/> / <see cref="AntagCountSelector"/>.
/// </summary>
[DataDefinition]
public partial struct AntagSelectionDefinition()
{
    [DataField]
    public List<ProtoId<AntagPrototype>> PrefRoles = new();

    [DataField]
    public List<ProtoId<AntagPrototype>> FallbackRoles = new();

    [DataField]
    public AntagAcceptability MultiAntagSetting = AntagAcceptability.None;

    [DataField]
    public int Min = 1;

    [DataField]
    public int Max = 1;

    [DataField]
    public MinMax? MinRange;

    [DataField]
    public MinMax? MaxRange;

    [DataField]
    public int PlayerRatio = 10;

    [DataField]
    public bool PickPlayer = true;

    [DataField]
    public bool LateJoinAdditional = false;

    [DataField]
    public bool AllowNonHumans = false;

    [DataField]
    public EntityWhitelist? Whitelist;

    [DataField]
    public EntityWhitelist? Blacklist;

    [DataField]
    public ComponentRegistry Components = new();

    [DataField]
    public ComponentRegistry MindComponents = new();

    [DataField]
    public List<EntProtoId>? MindRoles;

    [DataField]
    public ProtoId<StartingGearPrototype>? StartingGear;

    [DataField]
    public List<ProtoId<RoleLoadoutPrototype>>? RoleLoadout;

    [DataField]
    public BriefingData? Briefing;

    [DataField]
    public EntProtoId? SpawnerPrototype;
}
