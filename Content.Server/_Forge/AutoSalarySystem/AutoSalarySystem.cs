using Content.Server._NF.Bank;
using Content.Server._NF.CryoSleep;
using Content.Server.Mind;
using Content.Server.Roles.Jobs;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Server.Popups;
using Robust.Shared.Prototypes;
using Robust.Server.Player;
using Content.Shared.Roles;
using Robust.Shared.Timing;

namespace Content.Server._Forge.AutoSalarySystem;

public sealed class AutoSalarySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly JobSystem _jobs = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MindSystem _mindSystem = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<BankAccountComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!TryGetCurrentJob(uid, out var job)
                || job.Salary <= 0)
            {
                if (HasComp<AutoSalaryComponent>(uid))
                    RemCompDeferred<AutoSalaryComponent>(uid);

                continue;
            }

            if (!TryComp<AutoSalaryComponent>(uid, out var comp))
            {
                comp = EnsureComp<AutoSalaryComponent>(uid);
                comp.LastSalaryAt = _timing.CurTime;
                comp.JobPrototype = job.ID;
                Dirty(uid, comp);
                continue;
            }

            if (comp.JobPrototype != job.ID)
            {
                comp.LastSalaryAt = _timing.CurTime;
                comp.JobPrototype = job.ID;
                Dirty(uid, comp);
                continue;
            }

            if (comp.LastSalaryAt + job.SalaryInterval > _timing.CurTime)
                continue;

            if (!ShouldSkipEntity(uid))
                TryPaySalary(uid, job.Salary);

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

    private void TryPaySalary(EntityUid body, int salary)
    {
        if (_bank.TryBankDeposit(body, salary))
        {
            _popup.PopupEntity(Loc.GetString("auto-salary-popup", ("salary", salary)), body, body);
        }
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
