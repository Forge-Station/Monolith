using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Leviathans.Components;

/// <summary>
/// Colossal space worm leviathan with warp-burrow, coil smash, brood spawn and a visual segment trail.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VoidWormComponent : Component
{
    [DataField]
    public EntProtoId HeadPrototype = "MobForgeVoidWorm";

    [DataField]
    public EntProtoId SegmentPrototype = "MobForgeVoidWormSegment";

    [DataField]
    public EntProtoId SegmentAltPrototype = "MobForgeVoidWormSegmentAlt";

    [DataField]
    public EntProtoId TailPrototype = "MobForgeVoidWormTail";

    /// <summary>
    /// Trailing body pieces behind the head, including the tail. Rolled on spawn from length.
    /// </summary>
    [DataField]
    public int SegmentCount = 20;

    /// <summary>
    /// World-unit gap between segment centers. ~5.4 with 6-tile sprites keeps them overlapping.
    /// </summary>
    [DataField]
    public float SegmentSpacing = 5.4f;

    [DataField]
    public float MinLength = 100f;

    [DataField]
    public float MaxLength = 300f;

    [DataField]
    public EntProtoId BroodPrototype = "MobForgeVoidWormling";

    [DataField]
    public int BroodCount = 3;

    [DataField]
    public int MaxBrood = 6;

    [DataField]
    public float BroodSpread = 2.2f;

    [DataField]
    public TimeSpan BroodCooldown = TimeSpan.FromSeconds(45);

    [DataField]
    public float WarpRange = 28f;

    [DataField]
    public DamageSpecifier WarpDamage = new()
    {
        DamageDict =
        {
            ["Blunt"] = 20,
            ["Structural"] = 40
        }
    };

    [DataField]
    public float WarpBurstRange = 6f;

    [DataField]
    public TimeSpan WarpCooldown = TimeSpan.FromSeconds(12);

    [DataField]
    public float CoilRange = 8f;

    [DataField]
    public TimeSpan CoilStun = TimeSpan.FromSeconds(3);

    [DataField]
    public DamageSpecifier CoilDamage = new()
    {
        DamageDict =
        {
            ["Blunt"] = 35,
            ["Structural"] = 80
        }
    };

    [DataField]
    public TimeSpan CoilCooldown = TimeSpan.FromSeconds(18);

    /// <summary>
    /// Head ram chew radius in tiles.
    /// </summary>
    [DataField]
    public float RamRadius = 3.4f;

    [DataField]
    public TimeSpan RamInterval = TimeSpan.FromSeconds(0.05);

    /// <summary>
    /// How many segments must hug a grid, and cover all four sides, before crush starts.
    /// </summary>
    [DataField]
    public float WrapMargin = 8f;

    [DataField]
    public float WrapCoverage = 0.28f;

    [DataField]
    public float CrushRadius = 3.2f;

    [DataField]
    public TimeSpan CrushInterval = TimeSpan.FromSeconds(0.55);

    [DataField]
    public DamageSpecifier CrushDamage = new()
    {
        DamageDict =
        {
            ["Blunt"] = 25,
            ["Structural"] = 90
        }
    };

    [DataField]
    public float HuntRange = 2048f;

    [DataField]
    public float HuntSpeed = 110f;

    [DataField]
    public int MinHealth = 100000;

    [DataField]
    public int MaxHealth = 1000000;

    [DataField]
    public int SegmentMinHealth = 50000;

    [DataField]
    public int SegmentMaxHealth = 200000;

    /// <summary>
    /// True when this worm was created by a Terraria-style split and already has a body.
    /// </summary>
    [DataField]
    public bool IsFragment;

    public TimeSpan NextWarp;
    public TimeSpan NextCoil;
    public TimeSpan NextBrood;
    public TimeSpan NextNpcCheck;
    public TimeSpan NextRam;
    public TimeSpan NextCrush;
    public TimeSpan NextCrushPopup;

    public readonly List<EntityUid> Segments = new();
    public readonly List<EntityUid> Brood = new();

    [DataField]
    public SoundSpecifier? SoundRoar = new SoundPathSpecifier("/Audio/Effects/demon_consume.ogg")
    {
        Params = AudioParams.Default.WithVolume(3f)
    };

    [DataField]
    public SoundSpecifier? SoundWarp = new SoundPathSpecifier("/Audio/Magic/blink.ogg");

    [DataField]
    public SoundSpecifier? SoundCoil = new SoundPathSpecifier("/Audio/Weapons/smash.ogg");
}
