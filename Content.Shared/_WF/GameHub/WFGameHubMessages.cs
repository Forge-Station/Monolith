using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.GameHub;

/// <summary>Transfer stream carrying game data: a header, then the file.</summary>
public static class WFGameHubProtocol
{
    public const string TransferKey = "WFGameData";
    public const int MaxHeaderBytes = 1024;

    /// <summary>Longest sound lump name a client may report.</summary>
    public const int MaxNoiseName = 8;

    /// <summary>How close a player must be to the cabinet or PDA they play on.</summary>
    public const float PlayRange = 3f;

    /// <summary>How far away other players hear a game.</summary>
    public const float HearRange = 10f;
}

/// <summary>Asks for a game's size and version.</summary>
[Serializable, NetSerializable]
public sealed class WFGameManifestRequestEvent(string game) : EntityEventArgs
{
    public readonly string Game = game;
}

[Serializable, NetSerializable]
public sealed class WFGameManifestEvent(string game, long size, string version) : EntityEventArgs
{
    public readonly string Game = game;
    public readonly long Size = size;
    public readonly string Version = version;
}

/// <summary>Asks the server to stream a game's data to this client.</summary>
[Serializable, NetSerializable]
public sealed class WFGameDownloadRequestEvent(string game) : EntityEventArgs
{
    public readonly string Game = game;
}

[Serializable, NetSerializable]
public sealed class WFGameDownloadFailedEvent(string game) : EntityEventArgs
{
    public readonly string Game = game;
}

/// <summary>The player started or changed their game's music on a cabinet or PDA. Null stops broadcasting.</summary>
[Serializable, NetSerializable]
public sealed class WFGameBroadcastEvent(NetEntity source, string game, string? track, bool loop, bool stopped) : EntityEventArgs
{
    public readonly NetEntity Source = source;
    public readonly string Game = game;
    public readonly string? Track = track;
    public readonly bool Loop = loop;
    public readonly bool Stopped = stopped;
}

/// <summary>A sound the player's game made, for the server to pass on to people nearby.</summary>
[Serializable, NetSerializable]
public sealed class WFGameNoiseEvent(NetEntity source, string game, string sound, byte volume) : EntityEventArgs
{
    public readonly NetEntity Source = source;
    public readonly string Game = game;
    public readonly string Sound = sound;
    public readonly byte Volume = volume;
}

/// <summary>Another player's game made a sound near you.</summary>
[Serializable, NetSerializable]
public sealed class WFGameNoiseHeardEvent(NetEntity source, string game, string sound, byte volume) : EntityEventArgs
{
    public readonly NetEntity Source = source;
    public readonly string Game = game;
    public readonly string Sound = sound;
    public readonly byte Volume = volume;
}

/// <summary>The player finished a level. Client-reported, so the server caps what it can earn.</summary>
[Serializable, NetSerializable]
public sealed class WFGameLevelCompleteEvent(
    NetEntity source,
    string game,
    int level,
    int killPercent,
    int itemPercent,
    int secretPercent,
    int seconds) : EntityEventArgs
{
    public readonly NetEntity Source = source;
    public readonly string Game = game;
    public readonly int Level = level;
    public readonly int KillPercent = killPercent;
    public readonly int ItemPercent = itemPercent;
    public readonly int SecretPercent = secretPercent;
    public readonly int Seconds = seconds;
}

[Serializable, NetSerializable]
public sealed class WFGameLeaderboardRequestEvent(string game) : EntityEventArgs
{
    public readonly string Game = game;
}

[Serializable, NetSerializable]
public sealed class WFGameLeaderboardEvent(string game, List<WFGameLeaderboardEntry> entries) : EntityEventArgs
{
    public readonly string Game = game;
    public readonly List<WFGameLeaderboardEntry> Entries = entries;
}
