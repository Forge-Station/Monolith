using System.Linq;
using Content.Server._NF.Bank;
using Content.Server.Afk;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.Companies;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Preferences;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._WF.Companies;

public sealed partial class WFCompanySystem
{
    [Dependency] private IAfkManager _afk = default!;

    /// <summary>How often played time is counted.</summary>
    public static readonly TimeSpan PayrollSample = TimeSpan.FromMinutes(1);

    /// <summary>How often counted time is written to the database between payments.</summary>
    private static readonly TimeSpan PayrollSave = TimeSpan.FromMinutes(5);

    private readonly HashSet<Member> _unsavedTime = new();
    private TimeSpan _nextSample;
    private TimeSpan _nextSave;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!Loaded)
            return;
        SyncCompanyRadios();
        SyncCompanyAccess();
        var now = _timing.CurTime;
        if (now >= _nextSample)
        {
            _nextSample = now + PayrollSample;
            SamplePayroll((int) PayrollSample.TotalSeconds);
        }
        if (now >= _nextSave)
        {
            _nextSave = now + PayrollSave;
            SavePlayedTime();
        }
    }

    /// <summary>Writes counted time that hasn't been saved yet.</summary>
    private void SavePlayedTime()
    {
        foreach (var member in _unsavedTime)
            PersistMember(member);
        _unsavedTime.Clear();
    }

    /// <summary>The member character a session is playing right now: alive or critical, not a ghost, not AFK.</summary>
    private Character? PlayingCharacter(ICommonSession session)
    {
        if (session.Status != SessionStatus.InGame || !_spawnedSlots.TryGetValue(session.UserId, out var slot) ||
            session.AttachedEntity is not { } body || HasComp<GhostComponent>(body) ||
            !TryComp<MobStateComponent>(body, out var state) || state.CurrentState == MobState.Dead || _afk.IsAfk(session) ||
            _preferences.GetPreferencesOrNull(session.UserId)?.Characters.GetValueOrDefault(slot) is not HumanoidCharacterProfile profile ||
            !_profileIds.TryGetValue(session.UserId, out var ids) || !ids.TryGetValue(slot, out var id))
            return null;
        return new Character(session.UserId, slot, profile, id);
    }

    /// <summary>Counts played time and pays every wage that has come due.</summary>
    public void SamplePayroll(int seconds)
    {
        var due = new Dictionary<Company, List<(Member Member, ICommonSession Session, Character Character, int Wage)>>();
        foreach (var session in _players.Sessions)
        {
            if (PlayingCharacter(session) is not { } character || CompanyOf(character.ProfileId) is not { } company)
                continue;
            var member = company.Members[character.ProfileId];
            member.UnpaidSeconds += seconds;
            CountPlayed(company, member, seconds);
            _unsavedTime.Add(member);
            if (member.UnpaidSeconds < WFCompanyPolicy.PayrollHourSeconds)
                continue;
            var hours = member.UnpaidSeconds / WFCompanyPolicy.PayrollHourSeconds;
            member.UnpaidSeconds -= hours * WFCompanyPolicy.PayrollHourSeconds;
            var wage = (RankOf(company, member)?.Wage ?? 0) * hours;
            if (wage <= 0)
                continue;
            if (!due.TryGetValue(company, out var list))
                due[company] = list = new();
            list.Add((member, session, character, wage));
        }
        foreach (var (company, wages) in due)
            PayWages(company, wages);
    }

    /// <summary>Pays wages in full when the account can, otherwise everyone the same share of what's there.</summary>
    private void PayWages(Company company, List<(Member Member, ICommonSession Session, Character Character, int Wage)> wages)
    {
        if (company.Frozen)
            return;
        var total = wages.Sum(entry => (long) entry.Wage);
        var percent = WFCompanyPolicy.PayrollShare(company.Balance, total);
        foreach (var (member, session, character, wage) in wages)
        {
            var amount = (int) (wage * (long) percent / 100);
            var note = percent < 100 ? Loc.GetString("wf-company-payroll-partial-note", ("percent", percent)) : string.Empty;
            if (amount <= 0 || !Transact(company, WFCompanyLedgerKind.Wage, -amount, member.Profile, member.Name, note))
                continue;
            if (!PayCharacter(session, character, amount))
            {
                // The bank refused it, so the money goes back.
                Transact(company, WFCompanyLedgerKind.Wage, amount, member.Profile, member.Name,
                    Loc.GetString("wf-company-payroll-returned-note"), ignoreFrozen: true);
                continue;
            }
            PersistMember(member);
            _unsavedTime.Remove(member);
            _chat.DispatchServerMessage(session, Loc.GetString("wf-company-payroll-paid", ("amount", amount), ("company", company.Name)));
        }
        if (percent < 100 && company.Members.TryGetValue(company.Owner, out var owner) &&
            _players.TryGetSessionById(owner.User, out var ownerSession))
            _chat.DispatchServerMessage(ownerSession, Loc.GetString("wf-company-payroll-short", ("company", company.Name), ("percent", percent)));
        Touch(company);
    }

    /// <summary>Deposits into the played character's bank, keeping the body's balance in step when it is the selected one.</summary>
    private bool PayCharacter(ICommonSession session, Character character, int amount)
    {
        if (_preferences.GetPreferencesOrNull(session.UserId) is not { } prefs)
            return false;
        if (prefs.SelectedCharacterIndex == character.Slot && session.AttachedEntity is { } body && HasComp<BankAccountComponent>(body))
            return _bank.TryBankDeposit(body, amount, false);
        return _bank.TryBankDeposit(session, prefs, character.Profile, amount, out _);
    }

    /// <summary>The company's share of a member's earnings, without taking it; 0 for non-members.</summary>
    public int CutFor(ICommonSession? session, int amount)
    {
        if (session == null || amount <= 0 || CurrentCharacter(session) is not { } character ||
            CompanyOf(character.ProfileId) is not { Frozen: false } company)
            return 0;
        return WFCompanyPolicy.CutOf(amount, company.Cut);
    }

    /// <summary>Pays a cut worked out by <see cref="CutFor"/> into the member's company.</summary>
    public void PayCut(ICommonSession? session, int cut, string source)
    {
        if (session == null || cut <= 0 || CurrentCharacter(session) is not { } character || CompanyOf(character.ProfileId) is not { } company)
            return;
        var member = company.Members[character.ProfileId];
        if (Transact(company, WFCompanyLedgerKind.Cut, cut, member.Profile, member.Name, Loc.GetString("wf-company-cut-source-" + source)))
            Touch(company);
    }

    /// <summary>Takes the cut from a sale paid out in cash and returns what the member keeps.</summary>
    public int TakeCut(EntityUid? actor, int amount, string source)
    {
        if (actor == null || !_players.TryGetSessionByEntity(actor.Value, out var session))
            return amount;
        var cut = CutFor(session, amount);
        PayCut(session, cut, source);
        return amount - cut;
    }
}
