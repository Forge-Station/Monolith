using Robust.Shared.Serialization;

namespace Content.Shared._WF.Hailing;

/// <summary>A ship the console can hail.</summary>
[Serializable, NetSerializable]
public struct HailTarget
{
    public NetEntity Ship;
    public string Name;

    /// <summary>Metres from this ship.</summary>
    public float Distance;

    /// <summary>Every console aboard is already on a hail.</summary>
    public bool Busy;
}

/// <summary>The console wants the list of ships it can hail.</summary>
[Serializable, NetSerializable]
public sealed class HailTargetsRequestMessage : BoundUserInterfaceMessage;

/// <summary>The ships in hailing range, nearest first.</summary>
[Serializable, NetSerializable]
public sealed class HailTargetsMessage : BoundUserInterfaceMessage
{
    public List<HailTarget> Targets;

    public HailTargetsMessage(List<HailTarget> targets)
    {
        Targets = targets;
    }
}

/// <summary>The pilot hailed a ship.</summary>
[Serializable, NetSerializable]
public sealed class HailRequestMessage : BoundUserInterfaceMessage
{
    public NetEntity Ship;

    public HailRequestMessage(NetEntity ship)
    {
        Ship = ship;
    }
}

/// <summary>The pilot answered or declined an incoming hail.</summary>
[Serializable, NetSerializable]
public sealed class HailAnswerMessage : BoundUserInterfaceMessage
{
    public bool Accept;

    public HailAnswerMessage(bool accept)
    {
        Accept = accept;
    }
}

/// <summary>The pilot cancelled their hail or closed the channel.</summary>
[Serializable, NetSerializable]
public sealed class HailEndMessage : BoundUserInterfaceMessage;

/// <summary>The pilot sent a line down the open channel.</summary>
[Serializable, NetSerializable]
public sealed class HailSayMessage : BoundUserInterfaceMessage
{
    public string Text;

    public HailSayMessage(string text)
    {
        Text = text;
    }
}

/// <summary>Tells a console why its hail or call stopped.</summary>
[Serializable, NetSerializable]
public sealed class HailEndedMessage : BoundUserInterfaceMessage
{
    public HailEndReason Reason;
    public string Ship;

    public HailEndedMessage(HailEndReason reason, string ship)
    {
        Reason = reason;
        Ship = ship;
    }
}
