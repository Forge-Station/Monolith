using Content.Server._NF.Bank;
using Content.Server._NF.CryoSleep;
using Content.Server.Mind;
using Content.Server.Roles.Jobs;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.StationRoles;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Server.Popups;
using Robust.Shared.Prototypes;
using Robust.Server.Player;
using Content.Shared.Roles;
using Robust.Shared.Timing;

namespace Content.Server._Forge.AutoSalarySystem;

public sealed partial class AutoSalarySystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private JobSystem _jobs = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MindSystem _mindSystem = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<BankAccountComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!TryGetPay(uid, out var jobId, out var wage, out var interval, out var account))
            {
                if (HasComp<AutoSalaryComponent>(uid))
                    RemCompDeferred<AutoSalaryComponent>(uid);

                continue;
            }

            if (!TryComp<AutoSalaryComponent>(uid, out var comp))
            {
                comp = EnsureComp<AutoSalaryComponent>(uid);
                comp.LastSalaryAt = _timing.CurTime;
                comp.JobPrototype = jobId;
                Dirty(uid, comp);
                continue;
            }

            if (comp.JobPrototype != jobId)
            {
                comp.LastSalaryAt = _timing.CurTime;
                comp.JobPrototype = jobId;
                Dirty(uid, comp);
                continue;
            }

            if (comp.LastSalaryAt + interval > _timing.CurTime)
                continue;

            if (!ShouldSkipEntity(uid))
                TryPaySalary(uid, wage, account);

            comp.LastSalaryAt = _timing.CurTime;
            Dirty(uid, comp);
        }
    }

    private bool HasActivePlayer(EntityUid body)
    {
        if (!_mindSystem.TryGetMind(body, out _, out var mind))
            return false;
        if (!_playerManager.TryGetSessionByEntity(body, out var session) && session == null)
            return false;
        if (mind.IsVisitingEntity)
            return false;
        return true;
    }

    private bool ShouldSkipEntity(EntityUid body)
    {
        if (IsEntityDead(body))
            return true;
        if (!HasActivePlayer(body))
            return true;
        return false;
    }

    private bool IsEntityDead(EntityUid body)
    {
        return !TryComp<MobStateComponent>(body, out var mobState) || _mobState.IsDead(body, mobState);
    }

    private void TryPaySalary(EntityUid body, int salary, SectorBankAccount? account)
    {
        if (account is { } sector
            && !_bank.TrySectorWithdraw(sector, salary, LedgerEntryType.StationWithdrawalPayroll))
            return;

        if (_bank.TryBankDeposit(body, salary))
        {
            _popup.PopupEntity(Loc.GetString("auto-salary-popup", ("salary", salary)), body, body);
        }
    }

    /// <summary>
    /// A <see cref="WfStationSalaryPrototype"/> keyed by the job id pays hourly and, when the account
    /// name is a sector account, draws that wage first. Otherwise the job's own salary is used.
    /// </summary>
    private bool TryGetPay(EntityUid body, out string jobId, out int wage, out TimeSpan interval, out SectorBankAccount? account)
    {
        jobId = string.Empty;
        wage = 0;
        interval = TimeSpan.FromHours(1);
        account = null;

        if (!TryGetCurrentJob(body, out var job))
            return false;

        jobId = job.ID;
        if (_proto.TryIndex<WfStationSalaryPrototype>(job.ID, out var salary))
        {
            wage = salary.Wage;
            if (!string.IsNullOrWhiteSpace(salary.Account)
                && Enum.TryParse(salary.Account, out SectorBankAccount parsed)
                && parsed != SectorBankAccount.Invalid)
                account = parsed;

            return wage > 0;
        }

        wage = job.Salary;
        interval = job.SalaryInterval;
        return wage > 0;
    }

    private bool TryGetCurrentJob(EntityUid body, out JobPrototype job)
    {
        job = default!;
        ProtoId<JobPrototype>? jobId = null;

        if (_mindSystem.TryGetMind(body, out var mindId, out _)
            && _jobs.MindTryGetJobId(mindId, out var currentMindJobId)
            && !string.IsNullOrWhiteSpace(currentMindJobId))
        {
            jobId = currentMindJobId;
        }
        else if (TryComp<PlayerJobComponent>(body, out var playerJob)
                 && !string.IsNullOrWhiteSpace(playerJob.JobPrototype))
        {
            jobId = playerJob.JobPrototype;
        }

        if (jobId is not { } resolvedJobId
            || !_proto.TryIndex(resolvedJobId, out JobPrototype? resolvedJob))
        {
            return false;
        }

        job = resolvedJob;
        return true;
    }
}
