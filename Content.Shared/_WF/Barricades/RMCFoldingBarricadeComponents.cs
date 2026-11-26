using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Barricades;

/// <summary>A deployed MB-6 that folds back into an item, keeping its damage.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(RMCFoldingBarricadeSystem))]
public sealed partial class RMCFoldingBarricadeComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId FoldedPrototype = "CMFoldingBarricade";

    [DataField, AutoNetworkedField]
    public float MaxDamage = 350;

    [DataField, AutoNetworkedField]
    public TimeSpan CollapseDelay = TimeSpan.FromSeconds(3);

    [DataField, AutoNetworkedField]
    public TimeSpan CrowbarCollapseDelay = TimeSpan.FromSeconds(1.5);
}

/// <summary>A stack of folded MB-6 barricades; remembers each one's damage.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(RMCFoldingBarricadeSystem))]
public sealed partial class RMCFoldingBarricadeStackComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId DeployedPrototype = "CMBarricadeFolding";

    [DataField, AutoNetworkedField]
    public float MaxDamage = 350;

    [DataField, AutoNetworkedField]
    public List<float> StoredDamage = new();

    [DataField, AutoNetworkedField]
    public TimeSpan DeployDelay = TimeSpan.FromSeconds(1);

    [DataField, AutoNetworkedField]
    public float RepairAmount = 200;

    [DataField, AutoNetworkedField]
    public float RepairFuel = 2;

    [DataField, AutoNetworkedField]
    public float RepairDelayPerDamaged = 10;

    [DataField, AutoNetworkedField]
    public SoundSpecifier? RepairSound = new SoundPathSpecifier("/Audio/Items/welder.ogg");

    public bool SuppressCountChange;

    public List<float> PendingSplitDamage = new();
}

[Serializable, NetSerializable]
public sealed partial class RMCFoldingBarricadeDeployDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class RMCFoldingBarricadeCollapseDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class RMCFoldingBarricadeRepairDoAfterEvent : SimpleDoAfterEvent;

/// <summary>A folding barricade door that, once linked with a crowbar, opens and closes with its linked row.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(RMCFoldingBarricadeLinkingSystem))]
public sealed partial class RMCFoldingBarricadeLinkingComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Linked;

    [DataField, AutoNetworkedField]
    public bool Linkable = true;

    [DataField]
    public SoundSpecifier? ToggleSound = new SoundPathSpecifier("/Audio/Items/crowbar.ogg");

    /// <summary>Connection sprite states per screen side and door state.</summary>
    [DataField]
    public Dictionary<Direction, Dictionary<RMCFoldingBarricadeLinkingVisualState, string>> ConnectionStates = new();

    [Access(Other = AccessPermissions.ReadExecute)]
    public bool TryGetConnectionState(Direction direction, RMCFoldingBarricadeLinkingVisualState visualState, out string state)
    {
        state = string.Empty;
        if (!ConnectionStates.TryGetValue(direction, out var states) ||
            !states.TryGetValue(visualState, out var found))
        {
            return false;
        }

        state = found;
        return true;
    }
}

[Serializable, NetSerializable]
public enum RMCFoldingBarricadeLinkingVisualLayers : byte
{
    North,
    South,
    East,
    West,
}

[Serializable, NetSerializable]
public enum RMCFoldingBarricadeLinkingVisuals : byte
{
    North,
    South,
    East,
    West,
}

[Serializable, NetSerializable]
public enum RMCFoldingBarricadeLinkingVisualState : byte
{
    None,
    Closed,
    Open,
}
