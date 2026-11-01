using System.Linq;
using Content.Server.GameTicking;
using Content.Shared._WF.Companies;
using Robust.Shared.Utility;

namespace Content.Server._WF.Companies;

public sealed partial class WFCompanySystem
{
    /// <summary>What a company did this round, for the round end summary.</summary>
    private sealed class RoundStats
    {
        public readonly HashSet<int> Played = new();
        public int Seconds;
        public int Joined;
        public long Wages;
        public long Cuts;
        public long Deposits;
        public long Withdrawals;
        public long Payments;
        public long StartBalance;
    }

    private readonly Dictionary<int, RoundStats> _roundStats = new();

    private void InitializeReport()
    {
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEndText);
    }

    /// <summary>This round's stats for a company, started from its balance before the first change this round.</summary>
    private RoundStats Stats(Company company)
    {
        if (!_roundStats.TryGetValue(company.Id, out var stats))
            _roundStats[company.Id] = stats = new RoundStats { StartBalance = company.Balance };
        return stats;
    }

    /// <summary>Counts a ledger movement towards this round's stats; call before the balance changes.</summary>
    private void CountTransaction(Company company, WFCompanyLedgerKind kind, long amount)
    {
        var stats = Stats(company);
        switch (kind)
        {
            case WFCompanyLedgerKind.Wage:
                stats.Wages -= amount;
                break;
            case WFCompanyLedgerKind.Cut:
                stats.Cuts += amount;
                break;
            case WFCompanyLedgerKind.Deposit:
                stats.Deposits += amount;
                break;
            case WFCompanyLedgerKind.Withdraw:
                stats.Withdrawals -= amount;
                break;
            case WFCompanyLedgerKind.Payment:
                stats.Payments -= amount;
                break;
        }
    }

    private void CountPlayed(Company company, Member member, int seconds)
    {
        var stats = Stats(company);
        stats.Played.Add(member.Profile);
        stats.Seconds += seconds;
    }

    private void OnRoundEndText(RoundEndTextAppendEvent args)
    {
        var active = _roundStats.Where(pair => _companies.ContainsKey(pair.Key) &&
                (pair.Value.Played.Count > 0 || pair.Value.Joined > 0 || pair.Value.Deposits + pair.Value.Withdrawals + pair.Value.Wages + pair.Value.Cuts + pair.Value.Payments != 0))
            .OrderByDescending(pair => pair.Value.Seconds).ToArray();
        if (active.Length == 0)
            return;
        args.AddLine(string.Empty);
        args.AddLine(Loc.GetString("wf-company-round-end-header"));
        foreach (var (id, stats) in active)
        {
            var company = _companies[id];
            args.AddLine(Loc.GetString("wf-company-round-end-company",
                ("colour", company.Colour.ToHex()), ("tag", FormattedMessage.EscapeText(company.Tag)), ("name", FormattedMessage.EscapeText(company.Name)),
                ("members", stats.Played.Count), ("hours", Math.Round(stats.Seconds / 3600.0, 1)), ("joined", stats.Joined),
                ("crews", _roundSetup.CompanyCrewCount(id))));
            args.AddLine(Loc.GetString("wf-company-round-end-money",
                ("wages", stats.Wages), ("payments", stats.Payments), ("cuts", stats.Cuts), ("deposits", stats.Deposits), ("withdrawals", stats.Withdrawals),
                ("start", stats.StartBalance), ("end", company.Balance), ("change", company.Balance - stats.StartBalance)));
        }
    }
}
