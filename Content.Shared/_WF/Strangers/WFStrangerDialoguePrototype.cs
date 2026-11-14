using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Strangers;

/// <summary>
/// A stranger's conversation: what he says at each turn and what the player can answer, Fallout style. A node's line is
/// spoken aloud as well as shown; an option leads to another node, or ends the talk.
/// </summary>
[Prototype("wfStrangerDialogue")]
public sealed partial class WFStrangerDialoguePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>What he calls out to get the player's attention, shown in the hail.</summary>
    [DataField]
    public LocId? Hail;

    /// <summary>The node a conversation opens on.</summary>
    [DataField(required: true)]
    public string Start = default!;

    /// <summary>Where a refused hail leads, such as straight to a temper; null lets him walk off.</summary>
    [DataField]
    public string? Ignored;

    [DataField(required: true)]
    public Dictionary<string, WFStrangerNode> Nodes = new();
}

/// <summary>One turn: his line, what happens as he says it, and the player's answers.</summary>
[DataDefinition]
public sealed partial class WFStrangerNode
{
    [DataField(required: true)]
    public LocId Line;

    /// <summary>What he does on reaching this node; a node with no options ends the talk after it.</summary>
    [DataField]
    public WFStrangerAction Action = WFStrangerAction.None;

    [DataField]
    public List<WFStrangerOption> Options = new();
}

/// <summary>An answer the player can give.</summary>
[DataDefinition]
public sealed partial class WFStrangerOption
{
    [DataField(required: true)]
    public LocId Text;

    /// <summary>The node this leads to; null ends the talk.</summary>
    [DataField]
    public string? Next;

    /// <summary>Spesos the player hands over to take this option, from cash in hand or else the bank.</summary>
    [DataField]
    public int Cost;

    /// <summary>Where it leads when the player can't pay.</summary>
    [DataField]
    public string? Broke;

    /// <summary>What he hands back, one picked.</summary>
    [DataField]
    public List<EntProtoId> Items = new();
}

[Serializable, NetSerializable]
public enum WFStrangerAction : byte
{
    None,

    /// <summary>He turns on the player he is talking to.</summary>
    Hostile,

    /// <summary>He's done with the player and wanders off.</summary>
    Leave,
}

/// <summary>Server to client: a stranger wants a word.</summary>
[Serializable, NetSerializable]
public sealed class WFStrangerHailEvent : EntityEventArgs
{
    public NetEntity Stranger;
    public string Name = string.Empty;
    public string Line = string.Empty;
}

/// <summary>Client to server: talk to him, or wave him off.</summary>
[Serializable, NetSerializable]
public sealed class WFStrangerHailReplyEvent : EntityEventArgs
{
    public NetEntity Stranger;
    public bool Accept;
}

/// <summary>Server to client: where the conversation is now; no options and <see cref="Ended"/> closes the window.</summary>
[Serializable, NetSerializable]
public sealed class WFStrangerDialogueEvent : EntityEventArgs
{
    public NetEntity Stranger;
    public string Name = string.Empty;
    public string Line = string.Empty;
    public List<string> Options = new();
    public bool Ended;
}

/// <summary>Client to server: the player picks an answer, by its place in the list.</summary>
[Serializable, NetSerializable]
public sealed class WFStrangerChooseEvent : EntityEventArgs
{
    public NetEntity Stranger;
    public int Option;
}
