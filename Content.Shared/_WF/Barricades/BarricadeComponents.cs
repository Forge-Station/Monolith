using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Tools;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using DrawDepthTag = Robust.Shared.GameObjects.DrawDepth;

namespace Content.Shared._WF.Barricades;

/// <summary>A directional barricade: one per facing per tile.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BarricadeComponent : Component;

/// <summary>A sandbag barricade an entrenching tool can take apart.</summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(BarricadeSystem))]
public sealed partial class BarricadeSandbagComponent : Component
{
    /// <summary>Full sandbags dropped when dismantled.</summary>
    [DataField]
    public EntProtoId Material = "CMSandbagFull";

    /// <summary>Sandbags returned before damage; 0 uses the full stack's build cost.</summary>
    [DataField]
    public int MaxMaterial;

    /// <summary>One sandbag is lost per this much damage.</summary>
    [DataField]
    public int MaterialLossDamageInterval = 75;
}

/// <summary>Empty sandbags an entrenching tool fills.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(BarricadeSystem))]
public sealed partial class EmptySandbagComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId Filled = "CMSandbagFull";
}

/// <summary>Full sandbags that build a sandbag barricade.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(BarricadeSystem))]
public sealed partial class FullSandbagComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan BuildDelay = TimeSpan.FromSeconds(1);

    [DataField, AutoNetworkedField]
    public int StackRequired = 5;

    [DataField, AutoNetworkedField]
    public EntProtoId Builds = "CMBarricadeSandbag";
}

/// <summary>Digs natural ground to fill empty sandbags and takes sandbag barricades apart.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(BarricadeSystem))]
public sealed partial class EntrenchingToolComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan DigDelay = TimeSpan.FromSeconds(5);

    [DataField, AutoNetworkedField]
    public TimeSpan FillDelay = TimeSpan.FromSeconds(3);

    [DataField, AutoNetworkedField]
    public int LayersPerDig = 5;

    [DataField, AutoNetworkedField]
    public int TotalLayers;

    [AutoNetworkedField]
    public EntityCoordinates LastDigLocation;

    /// <summary>Tile ID prefixes that can be dug.</summary>
    [DataField]
    public List<string> DigTilePrefixes = new();

    [DataField, AutoNetworkedField]
    public SoundSpecifier DigSound = new SoundCollectionSpecifier("CMEntrenchingThud", AudioParams.Default.WithVolume(-3));

    [DataField, AutoNetworkedField]
    public SoundSpecifier FillSound = new SoundCollectionSpecifier("CMEntrenchingRustle", AudioParams.Default.WithVolume(-6));
}

[Serializable, NetSerializable]
public enum EntrenchingToolComponentVisualLayers
{
    Base,
    Folded,
    Dirt,
}

[Serializable, NetSerializable]
public sealed partial class EntrenchingToolDoAfterEvent : DoAfterEvent
{
    [DataField(required: true)]
    public NetCoordinates Coordinates;

    public EntrenchingToolDoAfterEvent(NetCoordinates coordinates)
    {
        Coordinates = coordinates;
    }

    public override DoAfterEvent Clone() => this;
}

[Serializable, NetSerializable]
public sealed partial class SandbagFillDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class SandbagDismantleDoAfterEvent : DoAfterEvent
{
    [DataField(required: true)]
    public NetCoordinates Coordinates;

    public SandbagDismantleDoAfterEvent(NetCoordinates coordinates)
    {
        Coordinates = coordinates;
    }

    public override DoAfterEvent Clone() => this;
}

[Serializable, NetSerializable]
public sealed partial class SandbagBuildDoAfterEvent : SimpleDoAfterEvent
{
    [DataField(required: true)]
    public NetCoordinates Coordinates;

    [DataField(required: true)]
    public Direction Direction;

    public SandbagBuildDoAfterEvent(NetCoordinates coordinates, Direction direction)
    {
        Coordinates = coordinates;
        Direction = direction;
    }
}

/// <summary>A barricade that takes barbed wire, which hurts attackers and stops climbing.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedBarbedSystem))]
public sealed partial class BarbedComponent : Component
{
    [DataField(required: true)]
    public DamageSpecifier ThornsDamage = default!;

    [DataField, AutoNetworkedField]
    public bool IsBarbed;

    /// <summary>Destruction threshold added while wired.</summary>
    [DataField, AutoNetworkedField]
    public int MaxHealthIncrease = 50;

    [DataField]
    public EntProtoId Spawn = "BarbedWire1";

    [DataField]
    public ProtoId<ToolQualityPrototype> RemoveQuality = "Cutting";

    [DataField, AutoNetworkedField]
    public SoundSpecifier? CutSound = new SoundPathSpecifier("/Audio/Items/wirecutter.ogg")
    {
        Params = AudioParams.Default.WithVariation(0.35f),
    };

    [DataField, AutoNetworkedField]
    public SoundSpecifier? BarbSound = new SoundPathSpecifier("/Audio/_WF/Barricades/barbed_wire_movement.ogg")
    {
        Params = AudioParams.Default.WithVariation(0.35f),
    };

    [DataField]
    public TimeSpan WireTime = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan CutTime = TimeSpan.FromSeconds(1);
}

/// <summary>Barbed wire that can be strung on a <see cref="BarbedComponent"/> barricade.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BarbedWireComponent : Component;

[Serializable, NetSerializable]
public enum BarbedWireVisualLayers : byte
{
    Wire,
}

[Serializable, NetSerializable]
public enum BarbedWireVisuals : byte
{
    UnWired,
    WiredClosed,
    WiredOpen,
}

[Serializable, NetSerializable]
public sealed partial class BarbedDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class CutBarbedDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Raised on a barricade when its wire goes on or comes off.</summary>
[ByRefEvent]
public record struct BarbedStateChangedEvent;

/// <summary>Melee attacks from in front of this anchored barricade may hit it instead of what is behind it.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DirectionalAttackBlockerComponent : Component
{
    [DataField, AutoNetworkedField]
    public float MinimumBlockChance = 0.3f;

    /// <summary>Health the block chance scales with; the server fills it from the destruction threshold.</summary>
    [DataField, AutoNetworkedField]
    public int MaxHealth;
}

/// <summary>Takes more damage while unanchored.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DamageReceivedModifierWhenUnanchoredComponent : Component
{
    [DataField, AutoNetworkedField]
    public FixedPoint2 Multiplier = 3;
}

/// <summary>A barricade door that can only close while anchored.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class RMCOpenOnlyWhenUnanchoredComponent : Component;

/// <summary>Draws the sprite at another depth while facing south, so it covers whoever stands behind it.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class RotationDrawDepthComponent : Component
{
    [DataField(customTypeSerializer: typeof(ConstantSerializer<DrawDepthTag>))]
    public int DefaultDrawDepth = (int) DrawDepth.DrawDepth.WallTops;

    [DataField(customTypeSerializer: typeof(ConstantSerializer<DrawDepthTag>))]
    public int SouthDrawDepth = (int) DrawDepth.DrawDepth.OverMobs;
}

[Serializable, NetSerializable]
public enum RMCDamageOverlayVisuals : byte
{
    DamageOverlay,
    AdditionalDamageOverlay,
}

/// <summary>Lets mobs in the whitelist pass only from behind, like a turnstile.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(BarricadeTurnstileSystem))]
public sealed partial class BarricadeTurnstileComponent : Component
{
    [DataField]
    public EntityWhitelist? ProcessWhitelist;

    [DataField, AutoNetworkedField]
    public HashSet<EntityUid> CollideExceptions = new();

    [DataField]
    public SoundSpecifier? TurnSound = new SoundPathSpecifier("/Audio/Items/ratchet.ogg", AudioParams.Default.WithVolume(-6));
}
