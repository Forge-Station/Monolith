using Content.Shared._Onyx.Wounds;
using Content.Shared.Decals;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Gore;

/// <summary>
/// The mess a hit makes on an organic body (G1): a spray that travels away from whatever caused it and
/// the splat it leaves at the end. Data on <c>WolfmedSfxProfilePrototype</c>, so a server retunes or
/// disables the whole thing by editing one file.
/// </summary>
[DataDefinition]
public sealed partial class WolfmedHitSplatterSpec
{
    /// <summary>False falls back to V4's blood mist, which is what a body with no bloodstream gets anyway.</summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>The travelling spray.</summary>
    [DataField]
    public EntProtoId Effect = "WFWolfmedHitSplatter";

    /// <summary>
    /// RSI states the spray picks from. Each is one of the sprite sheet's spray variants, and each must be
    /// single-direction: the spray is rotated to the angle of the hit, not pointed at a facing (FIX1).
    /// </summary>
    [DataField]
    public List<string> States = new() { "hitsplatter1_free", "hitsplatter2_free", "hitsplatter3_free" };

    /// <summary>
    /// FIX1: how much a hit has to raise the body's total bleeding before any blood flies. This, and not
    /// the damage type, is the whole trigger: a blunt hit that opens a bleed sprays and a slash that does
    /// not bleed stays dry. Bleeding severity is in wound-severity units, so a graze is well under one.
    /// </summary>
    [DataField]
    public FixedPoint2 MinBleedIncrease = FixedPoint2.New(1);

    /// <summary>FIX1: how long the burst budget counts over.</summary>
    [DataField]
    public TimeSpan BurstWindow = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// FIX1: how many sprays one body may throw inside <see cref="BurstWindow"/>. Automatic fire lands a
    /// bleeding hit per bullet, and one spray per bullet is a wall of sprites.
    /// </summary>
    [DataField]
    public int BurstBudget = 3;

    /// <summary>How long the spray takes to cross its distance. The splat lands when it arrives.</summary>
    [DataField]
    public TimeSpan Travel = TimeSpan.FromSeconds(1.1);

    /// <summary>Travel distance bands, longest first. The first one the hit clears wins.</summary>
    [DataField]
    public List<WolfmedSplatterRange> Distances = new();

    /// <summary>Blood on a wall. One of these per state in <see cref="WallStates"/>.</summary>
    [DataField]
    public EntProtoId WallSplat = "WFWolfmedBloodSplatWall";

    /// <summary>RSI states a wall splatter picks from.</summary>
    [DataField]
    public List<string> WallStates = new();

    /// <summary>Cleanable decals a floor splat picks from.</summary>
    [DataField]
    public List<ProtoId<DecalPrototype>> FloorDecals = new();

    /// <summary>How many splats one tile may hold before the oldest is thrown away.</summary>
    [DataField]
    public int MaxPerTile = 3;

    /// <summary>The travel distance this hit earns, in tiles.</summary>
    public float GetDistance(FixedPoint2 severity)
    {
        foreach (var band in Distances)
        {
            if (severity >= band.MinSeverity)
                return band.Tiles;
        }

        return Distances.Count > 0 ? Distances[^1].Tiles : 1f;
    }
}

/// <summary>One severity band of splatter travel.</summary>
[DataDefinition]
public sealed partial class WolfmedSplatterRange
{
    [DataField]
    public FixedPoint2 MinSeverity = FixedPoint2.Zero;

    /// <summary>Tiles travelled. Also how far ahead the wall check looks.</summary>
    [DataField]
    public float Tiles = 1f;
}

/// <summary>
/// What a body with a cut artery does about it (G2). Arterial bleeds and open stumps throw blood; nothing else
/// does (playtest 5: the <see cref="MajorRate"/> line used to make a heavy ordinary bleed spurt too; it drips now).
/// </summary>
[DataDefinition]
public sealed partial class WolfmedBleedSpurtSpec
{
    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// Bleed rate on one wound that counts as major. Playtest 5: no longer a spurt source on its own; the
    /// evisceration profile is still checked against it.
    /// </summary>
    [DataField]
    public float MajorRate = 1.5f;

    /// <summary>Wounds that spurt while untreated whatever their rate: an open amputation stump.</summary>
    [DataField]
    public HashSet<ProtoId<WoundPrototype>> StumpWounds = new() { "DismembermentWound" };

    /// <summary>Average gap between two spurts on one body.</summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(6);

    /// <summary>Jitter either side of the interval, so two patients never sync up.</summary>
    [DataField]
    public TimeSpan Jitter = TimeSpan.FromSeconds(2.5);

    /// <summary>Blood level below which there is nothing left to throw.</summary>
    [DataField]
    public float MinBloodLevel = 0.35f;

    /// <summary>Severity the spurt is spawned as, which is what picks its travel distance.</summary>
    [DataField]
    public FixedPoint2 Severity = FixedPoint2.New(14);

    /// <summary>Units of the body's own blood put on the deck with the spurt.</summary>
    [DataField]
    public FixedPoint2 SpillVolume = FixedPoint2.New(2);

    /// <summary>A wound bleeding out through the skin.</summary>
    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>An open stump, which is wetter.</summary>
    [DataField]
    public SoundSpecifier? StumpSound;

    /// <summary>What a leaking chassis sounds like instead: pressure escaping, not anything wet.</summary>
    [DataField]
    public SoundSpecifier? MechanicalSound;
}

/// <summary>
/// A damaged or EMP-disabled machine part throws sparks every so often until it is repaired.
/// </summary>
[DataDefinition]
public sealed partial class WolfmedMachineSparkSpec
{
    [DataField]
    public bool Enabled = true;

    /// <summary>Damage on one machine part at which it starts sparking.</summary>
    [DataField]
    public FixedPoint2 MinDamage = FixedPoint2.New(10);

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(5);

    [DataField]
    public TimeSpan Jitter = TimeSpan.FromSeconds(3);

    [DataField]
    public EntProtoId Effect = "WFWolfmedSparkBurstSmall";

    [DataField]
    public SoundSpecifier? Sound;
}

