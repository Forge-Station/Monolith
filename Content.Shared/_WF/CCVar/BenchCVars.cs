using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>Settings for Bench tickets, fines and debt.</summary>
[CVarDefs]
public sealed class BenchCVars
{
    /// <summary>Tiles a judge may stand from the person they ticket.</summary>
    public static readonly CVarDef<float> Range =
        CVarDef.Create("wf.bench.range", 3f, CVar.SERVERONLY);

    /// <summary>Most one ticket may fine, however many charges it carries.</summary>
    public static readonly CVarDef<int> TicketCap =
        CVarDef.Create("wf.bench.ticket_cap", 200000, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Most one person may be fined across all their tickets in a round; pardoned tickets don't count.</summary>
    public static readonly CVarDef<int> RoundCap =
        CVarDef.Create("wf.bench.round_cap", 400000, CVar.SERVERONLY);

    /// <summary>Minutes a ticket's debt may stand before the offender is Wanted.</summary>
    public static readonly CVarDef<int> DeadlineMinutes =
        CVarDef.Create("wf.bench.deadline_minutes", 30, CVar.SERVERONLY);

    /// <summary>Bench debt above which the offender is Wanted at once.</summary>
    public static readonly CVarDef<int> DebtCap =
        CVarDef.Create("wf.bench.debt_cap", 50000, CVar.SERVERONLY);

    /// <summary>Share of each later gain to a debtor's bank that goes to their Bench debt.</summary>
    public static readonly CVarDef<float> Garnish =
        CVarDef.Create("wf.bench.garnish", 0.5f, CVar.SERVERONLY);

    /// <summary>Bounty posted on an outlaw's warrant.</summary>
    public static readonly CVarDef<int> OutlawBounty =
        CVarDef.Create("wf.bench.outlaw_bounty", 50000, CVar.SERVERONLY);
}
