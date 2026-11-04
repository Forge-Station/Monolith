using Content.Shared._WF.Encounters;
using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Ghost;

/// <summary>
/// Sections of the ghost orbit menu, in display order.
/// </summary>
[Serializable, NetSerializable]
public enum GhostOrbitCategory : byte
{
    Interest,
    Antagonist,
    Critical,
    Alive,
    Xenonid,
    Disconnected,
    Dead,
    Ghost,
    Location,
    PlanetPoi,
    Ship,
    Encounter,
    Npc,
}

/// <summary>
/// One thing a ghost can orbit.
/// </summary>
[Serializable, NetSerializable]
public record struct GhostOrbitTarget
{
    public NetEntity Entity;
    public string Name;
    public GhostOrbitCategory Category;

    /// <summary>Job name, or what kind of point of interest this is.</summary>
    public string? Detail;

    /// <summary>Prototype the client draws an icon from when there is no job icon.</summary>
    public string? Prototype;

    public ProtoId<JobIconPrototype>? JobIcon;

    /// <summary>The company rank whose custom job icon replaces <see cref="JobIcon"/>, or 0.</summary>
    public int CompanyRank;

    /// <summary>Damage as a fraction of the dead threshold, 0 to 1. Null when the target has no health.</summary>
    public float? Damage;

    /// <summary>Ghosts currently following the target, admins excluded for non-admin viewers.</summary>
    public int Followers;

    /// <summary>Player mind with no client attached (SSD or catatonic).</summary>
    public bool Disconnected;

    /// <summary>Only listed because the viewer is an admin (admin warp points, admin ghosts).</summary>
    public bool AdminOnly;

    /// <summary>Sort rank inside the section, lowest first. Only the Xenonids section uses it.</summary>
    public int Order;
}

/// <summary>
/// One ship of a running encounter, for the Encounters section. Orbiting it follows its grid.
/// </summary>
[Serializable, NetSerializable]
public record struct GhostOrbitEncounterShip
{
    public NetEntity Grid;
    public string Ship;
    public string Encounter;

    /// <summary>The encounter's entity, to count encounters rather than ships.</summary>
    public NetEntity EncounterId;

    public string Side;
    public WFEncounterCategory Category;

    /// <summary>The colour the ship has on the sector markers; null draws by category.</summary>
    public Color? Color;

    /// <summary>Kept off the sector markers; only ghosts and admins see it.</summary>
    public bool Hidden;

    /// <summary>Ghosts currently following the ship, admins excluded for non-admin viewers.</summary>
    public int Followers;
}

/// <summary>
/// Client asks for everything it can orbit. Answered with <see cref="GhostOrbitTargetsEvent"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostOrbitRequestEvent : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class GhostOrbitTargetsEvent(List<GhostOrbitTarget> targets, List<GhostOrbitEncounterShip>? encounters = null, int burrowedLarva = 0) : EntityEventArgs
{
    public List<GhostOrbitTarget> Targets = targets;

    /// <summary>Burrowed larva waiting across all hives.</summary>
    public int BurrowedLarva = burrowedLarva;

    /// <summary>The ships of every unresolved encounter, hidden ones included.</summary>
    public List<GhostOrbitEncounterShip> Encounters = encounters ?? new List<GhostOrbitEncounterShip>();
}

/// <summary>
/// Client asks to orbit a target. Anything that moves is followed; fixed warp points are teleported to.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostOrbitWarpEvent(NetEntity target) : EntityEventArgs
{
    public NetEntity Target = target;
}
