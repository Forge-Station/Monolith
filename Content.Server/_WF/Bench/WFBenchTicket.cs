using Content.Shared._WF.Bench;
using Robust.Shared.Network;

namespace Content.Server._WF.Bench;

/// <summary>A ticket issued this round, and what has been taken against it.</summary>
public sealed class WFBenchTicket
{
    public int Number;
    public string Offender = string.Empty;
    public NetUserId User;
    public EntityUid Body;
    public string Judge = string.Empty;
    public string Ship = string.Empty;
    public string Notes = string.Empty;
    public List<WFBenchChargeView> Charges = new();
    public int Total;

    /// <summary>The charges' sum before the soft limits.</summary>
    public int Uncapped;
    public int Paid;
    public int Owed;
    public WFPenaltyLevel Level;
    public TimeSpan Issued;
    public TimeSpan Deadline;
    public bool Overdue;
    public bool Pardoned;

    public WFBenchTicketStatus Status => Pardoned ? WFBenchTicketStatus.Pardoned
        : Level == WFPenaltyLevel.Outlawry ? WFBenchTicketStatus.Outlawed
        : Owed <= 0 ? WFBenchTicketStatus.Paid
        : Overdue ? WFBenchTicketStatus.Overdue
        : WFBenchTicketStatus.Owing;

    /// <summary>Whether the offender stands Wanted because of this ticket.</summary>
    public bool Wanting => !Pardoned && (Level == WFPenaltyLevel.Outlawry || Overdue && Owed > 0);
}

/// <summary>An ordinance the Bench posted this round.</summary>
public sealed class WFBenchOrdinance
{
    public int Id;
    public string Title = string.Empty;
    public string Text = string.Empty;
    public int Fine;
    public WFPenaltyLevel Level;
    public string PostedBy = string.Empty;
}

/// <summary>What the Bench tracks about one offender's bank while they owe it.</summary>
public sealed class WFBenchDebtor
{
    /// <summary>Their balance as last seen, to tell a gain from a spend.</summary>
    public int? LastBalance;

    /// <summary>Gains not yet garnished.</summary>
    public int Pending;

    /// <summary>Whether the Bench set their record to Wanted, so it only clears what it set.</summary>
    public bool MarkedWanted;
}
