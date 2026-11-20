// WOLFGATE(Wolfmed): ported from Onyx for Wolfmed.
using Content.Server.Administration.Logs;
using Content.Shared.Administration.Logs;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Components;
using Content.Shared.Sticky;
using Content.Shared.Sticky.Components;
using Content.Shared.Sticky.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.Medical;

public sealed partial class MedicalPatchSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private ReactiveSystem _reactive = default!;
    [Dependency] private StickySystem _sticky = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ISharedAdminLogManager _adminLog = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MedicalPatchComponent, EntityStuckEvent>(OnStuck);
        SubscribeLocalEvent<MedicalPatchComponent, EntityUnstuckEvent>(OnUnstuck);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<MedicalPatchComponent, StickyComponent>();
        while (query.MoveNext(out var uid, out var patch, out var sticky))
        {
            if (sticky.StuckTo == null || _timing.CurTime < patch.NextUpdate)
                continue;
            patch.NextUpdate = _timing.CurTime + TimeSpan.FromSeconds(patch.UpdateTime);
            if (!TryInject((uid, patch), sticky.StuckTo.Value, patch.TransferAmount))
                _sticky.UnstickFromEntity((uid, sticky), uid);
        }
    }

    private bool TryInject(Entity<MedicalPatchComponent> patch, EntityUid target, FixedPoint2 amount)
    {
        if (!_solutions.TryGetSolution(patch.Owner, patch.Comp.SolutionName, out var source, out var sourceSolution) || sourceSolution.Volume == 0)
            return false;
        if (!_solutions.TryGetInjectableSolution(target, out var injectable, out var targetSolution))
            return false;
        var transfer = FixedPoint2.Min(amount, targetSolution.AvailableVolume);
        if (transfer <= 0)
            return true;
        var removed = _solutions.SplitSolution(source.Value, transfer);
        if (!targetSolution.CanAddSolution(removed))
            return true;
        _reactive.DoEntityReaction(target, removed, ReactionMethod.Injection);
        _solutions.TryAddSolution(injectable.Value, removed);
        return true;
    }

    private void OnStuck(Entity<MedicalPatchComponent> patch, ref EntityStuckEvent args)
    {
        EnsureComp<UnremoveableComponent>(patch);
        if (!_solutions.TryGetSolution(patch.Owner, patch.Comp.SolutionName, out _, out var solution))
            return;
        _adminLog.Add(LogType.ForceFeed,
            $"{ToPrettyString(args.User):user} stuck {ToPrettyString(patch):using} containing {SharedSolutionContainerSystem.ToPrettyString(solution):solution} on {ToPrettyString(args.Target):target}");
        if (patch.Comp.InjectAmmountOnAttatch > 0)
            TryInject(patch, args.Target, patch.Comp.InjectAmmountOnAttatch);
        if (patch.Comp.InjectPercentageOnAttatch > 0)
            TryInject(patch, args.Target, solution.Volume * patch.Comp.InjectPercentageOnAttatch / 100);
    }

    private void OnUnstuck(Entity<MedicalPatchComponent> patch, ref EntityUnstuckEvent args)
    {
        RemComp<UnremoveableComponent>(patch);
        if (!patch.Comp.SingleUse)
            return;
        if (patch.Comp.TrashObject is { } trash)
        {
            var used = Spawn(trash, Transform(patch).Coordinates);
            if (_hands.IsHolding(args.User, patch, out var hand))
                _hands.TryPickup(args.User, used, hand);
        }
        QueueDel(patch);
    }
}
