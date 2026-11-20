using Robust.Shared.Serialization;

namespace Content.Shared._WF.Planets;

/// <summary>What a forming world is working on; each has a <c>wf-world-forming-stage-*</c> label.</summary>
public enum WFWorldFormingStage : byte
{
    Waiting,
    Caverns,
    Cities,
    Sites,
    Roads,
    Wrecks,
    Terrain,
    Boundary,
    Wildlife,
}

/// <summary>One world still forming: its name, stage and progress from 0 to 1.</summary>
[Serializable, NetSerializable]
public sealed class WFWorldFormingEntry
{
    public NetEntity Ground;

    /// <summary>Every map of the world, orbit and caverns included, for the in-round bar.</summary>
    public List<NetEntity> Maps = new();

    public string Name = string.Empty;
    public WFWorldFormingStage Stage;
    public float Progress;

    /// <summary>Built in the lobby, ahead of the round.</summary>
    public bool RoundStart;
}

/// <summary>Sent to every client a few times a second while worlds form, and once empty when they are done.</summary>
[Serializable, NetSerializable]
public sealed class WFWorldFormingEvent : EntityEventArgs
{
    public List<WFWorldFormingEntry> Worlds = new();

    /// <summary>Whether the round start is being held for them.</summary>
    public bool RoundHeld;

    /// <summary>The overall progress over every world, from 0 to 1.</summary>
    public float Overall;
}
