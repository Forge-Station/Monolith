using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Bench;

[Serializable, NetSerializable]
public enum WFBenchTerminalUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum WFBenchTicketStatus : byte
{
    Paid,
    Owing,

    /// <summary>Still owing past its deadline or over the debt cap; the offender is Wanted.</summary>
    Overdue,

    /// <summary>An Outlawry ticket; the offender is Wanted with a bounty.</summary>
    Outlawed,
    Pardoned,
}

/// <summary>Someone close enough to the judge to be ticketed.</summary>
[Serializable, NetSerializable]
public sealed class WFBenchPersonView
{
    public NetEntity Entity;
    public string Name = string.Empty;
}

/// <summary>An ordinance the Bench posted this round.</summary>
[Serializable, NetSerializable]
public sealed class WFBenchOrdinanceView
{
    public int Id;
    public string Title = string.Empty;
    public string Text = string.Empty;
    public int Fine;
    public WFPenaltyLevel Level;
    public string PostedBy = string.Empty;
}

[Serializable, NetSerializable]
public sealed class WFBenchChargeView
{
    public string Title = string.Empty;
    public int Fine;
    public WFPenaltyLevel Level;
}

[Serializable, NetSerializable]
public sealed class WFBenchTicketView
{
    public int Number;
    public string Offender = string.Empty;
    public string Judge = string.Empty;
    public string Ship = string.Empty;
    public string Notes = string.Empty;
    public List<WFBenchChargeView> Charges = new();
    public int Total;
    public int Paid;
    public int Owed;
    public WFPenaltyLevel Level;
    public WFBenchTicketStatus Status;
    public TimeSpan Issued;
    public TimeSpan Deadline;
}

/// <summary>Everything the Bench app and terminal show.</summary>
[Serializable, NetSerializable]
public sealed class WFBenchUiState : BoundUserInterfaceState
{
    /// <summary>Whether the viewer may issue, pardon and post. Anyone else only reads the laws.</summary>
    public bool Judge;
    public List<WFBenchPersonView> Nearby = new();
    public List<WFBenchOrdinanceView> Ordinances = new();

    /// <summary>This round's tickets, newest first. Empty for a viewer who is not a judge.</summary>
    public List<WFBenchTicketView> Tickets = new();
}

/// <summary>One charge on a ticket being written: a law, a posted ordinance or a custom charge.</summary>
[Serializable, NetSerializable]
public sealed class WFBenchChargeRequest
{
    /// <summary>The law, or null for an ordinance or custom charge.</summary>
    public string? Law;

    /// <summary>The posted ordinance, or null.</summary>
    public int? Ordinance;

    /// <summary>A custom charge's title; ignored for laws and ordinances.</summary>
    public string Title = string.Empty;
    public int Fine;

    /// <summary>A custom charge's level; laws and ordinances keep their own.</summary>
    public WFPenaltyLevel Level;
}

[Serializable, NetSerializable]
public abstract class WFBenchAction;

[Serializable, NetSerializable]
public sealed class WFBenchRefreshAction : WFBenchAction;

[Serializable, NetSerializable]
public sealed class WFBenchIssueAction : WFBenchAction
{
    public NetEntity Offender;
    public string Ship = string.Empty;
    public string Notes = string.Empty;
    public List<WFBenchChargeRequest> Charges = new();
}

[Serializable, NetSerializable]
public sealed class WFBenchPardonAction(int number) : WFBenchAction
{
    public int Number = number;
}

[Serializable, NetSerializable]
public sealed class WFBenchPostOrdinanceAction : WFBenchAction
{
    public string Title = string.Empty;
    public string Text = string.Empty;
    public int Fine;
    public WFPenaltyLevel Level;
}

[Serializable, NetSerializable]
public sealed class WFBenchRemoveOrdinanceAction(int id) : WFBenchAction
{
    public int Id = id;
}

/// <summary>A Bench app button press.</summary>
[Serializable, NetSerializable]
public sealed class WFBenchCartridgeMessage(WFBenchAction action) : CartridgeMessageEvent
{
    public WFBenchAction Action = action;
}

/// <summary>A Bench terminal button press.</summary>
[Serializable, NetSerializable]
public sealed class WFBenchTerminalMessage(WFBenchAction action) : BoundUserInterfaceMessage
{
    public WFBenchAction Action = action;
}

/// <summary>The terminal's state, sent to each viewer alone since judges see more than others.</summary>
[Serializable, NetSerializable]
public sealed class WFBenchTerminalStateMessage(WFBenchUiState state) : BoundUserInterfaceMessage
{
    public WFBenchUiState State = state;
}
