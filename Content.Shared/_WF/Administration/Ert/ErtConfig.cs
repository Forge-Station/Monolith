using Robust.Shared.Serialization;

namespace Content.Shared._WF.Administration.Ert;

/// <summary>
/// Everything the ERT Builder sends: team flavour, size, outfits, ID setup, allowed species and ship.
/// Prototype ids are plain strings and are validated server-side.
/// </summary>
[Serializable, NetSerializable]
public sealed class ErtConfig
{
    public string TeamName = string.Empty;
    public string Briefing = string.Empty;
    public int Members = 4;
    public bool HasLeader = true;

    /// <summary>
    /// Empty means "{team} Leader" / "{team} Responder".
    /// </summary>
    public string LeaderTitle = string.Empty;
    public string MemberTitle = string.Empty;

    public string MemberOutfit = string.Empty;

    /// <summary>
    /// Empty means the leader wears the member outfit.
    /// </summary>
    public string LeaderOutfit = string.Empty;

    /// <summary>
    /// Allowed species ids. Empty allows any round-start species.
    /// </summary>
    public List<string> Species = new();

    /// <summary>
    /// Everyone becomes the default bald male human with a random human name, ignoring their characters and <see cref="Species"/>.
    /// </summary>
    public bool GenericHumans;

    /// <summary>
    /// Access groups added to every ID.
    /// </summary>
    public List<string> AccessGroups = new();

    /// <summary>
    /// Keep the access the outfit's own ID comes with; otherwise IDs only get <see cref="AccessGroups"/>.
    /// </summary>
    public bool KeepOutfitAccess = true;

    /// <summary>
    /// Vessel prototype to spawn at the admin. Empty spawns the team at the admin with no ship.
    /// </summary>
    public string Vessel = string.Empty;
}

/// <summary>
/// Limits the server enforces on an <see cref="ErtConfig"/>.
/// </summary>
public static class ErtLimits
{
    public const int MaxMembers = 30;
    public const int MaxTextLength = 64;
    public const int MaxBriefingLength = 1000;
}

/// <summary>
/// Admin asks the server to spawn a team. Ignored without the Spawn flag.
/// </summary>
[Serializable, NetSerializable]
public sealed class ErtSpawnRequestEvent : EntityEventArgs
{
    public ErtConfig Config;

    public ErtSpawnRequestEvent(ErtConfig config)
    {
        Config = config;
    }
}

/// <summary>
/// Sent to every ghost when a team is spawned so they can sign up.
/// </summary>
[Serializable, NetSerializable]
public sealed class ErtCalledEvent : EntityEventArgs
{
    public int TeamId;
    public string Team;
    public string Briefing;
    public int Places;
    public bool HasLeader;

    public ErtCalledEvent(int teamId, string team, string briefing, int places, bool hasLeader)
    {
        TeamId = teamId;
        Team = team;
        Briefing = briefing;
        Places = places;
        HasLeader = hasLeader;
    }
}

/// <summary>
/// A ghost signs up for a team, as a member or for the leader place.
/// </summary>
[Serializable, NetSerializable]
public sealed class ErtSignUpEvent : EntityEventArgs
{
    public int TeamId;
    public bool Leader;

    public ErtSignUpEvent(int teamId, bool leader)
    {
        TeamId = teamId;
        Leader = leader;
    }
}

/// <summary>
/// Answer to a sign-up, for the ghost who sent it.
/// </summary>
[Serializable, NetSerializable]
public sealed class ErtSignUpResultEvent : EntityEventArgs
{
    public int TeamId;
    public string Message;
    public bool IsError;

    public ErtSignUpResultEvent(int teamId, string message, bool isError)
    {
        TeamId = teamId;
        Message = message;
        IsError = isError;
    }
}

/// <summary>
/// Every place in a team is filled; clients close its sign-up prompt.
/// </summary>
[Serializable, NetSerializable]
public sealed class ErtClosedEvent : EntityEventArgs
{
    public int TeamId;

    public ErtClosedEvent(int teamId)
    {
        TeamId = teamId;
    }
}

/// <summary>
/// Outcome of a spawn request, for the admin who sent it.
/// </summary>
[Serializable, NetSerializable]
public sealed class ErtSpawnResultEvent : EntityEventArgs
{
    public string Message;
    public bool IsError;

    public ErtSpawnResultEvent(string message, bool isError)
    {
        Message = message;
        IsError = isError;
    }
}
