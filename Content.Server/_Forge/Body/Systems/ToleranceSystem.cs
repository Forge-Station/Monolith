using Robust.Shared.Timing;
using Content.Shared.Body.Systems;
using Content.Shared._Forge.Body.Components;
using Content.Shared._Forge.Body.Syndromes;

namespace Content.Server._Forge.Body.Syndromes;

public sealed partial class ToleranceSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedBodySystem _body = default!;

    public void AddTolerance(EntityUid uid, string reagentId, float amount, Organ target = Organ.Body)
    {
        var targets = OrganResolver.Resolve(uid, target, _body, EntityManager);

        if (targets.Count == 0)
            return;

        var split = amount / targets.Count;

        foreach (var t in targets)
        {
            if (!TryGetHolder(t, out var holder))
                continue;

            AddToleranceToHolder(t, reagentId, split, holder);
        }
    }

    public void RemoveTolerance(EntityUid uid, string reagentId, float amount, Organ target = Organ.Body)
    {
        var targets = OrganResolver.Resolve(uid, target, _body, EntityManager);

        foreach (var t in targets)
        {
            if (!TryGetHolder(t, out var holder))
                continue;

            RemoveToleranceFromHolder(reagentId, amount, holder);
        }
    }

    // Returns tolerance directly from uid without organ resolution
    public float GetTolerance(EntityUid uid, string reagentId)
    {
        if (!TryGetHolder(uid, out var holder))
            return 0f;

        return GetToleranceFromHolder(uid, reagentId, holder);
    }

    // Get average tolerance.. Method name makes sense..
    public float GetAverageTolerance(EntityUid uid, string reagentId, Organ target = Organ.Body)
    {
        var targets = OrganResolver.Resolve(uid, target, _body, EntityManager);
        var total = 0f;
        var counted = 0;

        foreach (var t in targets)
        {
            if (!TryGetHolder(t, out var holder))
                continue;

            var value = GetToleranceFromHolder(t, reagentId, holder);
            if (value <= 0f)
                continue;

            total += value;
            counted++;
        }

        return counted == 0 ? 0f : total / counted;
    }

    // Get highest tolerance from all targets
    public float GetMaxTolerance(EntityUid uid, string reagentId, Organ target = Organ.Body)
    {
        var targets = OrganResolver.Resolve(uid, target, _body, EntityManager);
        var max = 0f;

        foreach (var t in targets)
        {
            if (!TryGetHolder(t, out var holder))
                continue;

            var value = GetToleranceFromHolder(t, reagentId, holder);
            if (value > max)
                max = value;
        }

        return max;
    }

    // Sum of tolerance from all targets, capped at 1
    public float GetTotalTolerance(EntityUid uid, string reagentId, Organ target = Organ.Body)
    {
        var targets = OrganResolver.Resolve(uid, target, _body, EntityManager);
        var total = 0f;

        foreach (var t in targets)
        {
            if (!TryGetHolder(t, out var holder))
                continue;

            total += GetToleranceFromHolder(t, reagentId, holder);
        }

        return Math.Min(total, 1f);
    }

    private float GetToleranceFromHolder(EntityUid uid, string reagentId, IPhysiologyHolder holder)
    {
        if (!holder.Tolerances.TryGetValue(reagentId, out var tolerance))
            return 0f;

        RefreshTolerance(tolerance);

        if (tolerance.Value <= 0f)
        {
            holder.Tolerances.Remove(reagentId);
            return 0f;
        }

        return tolerance.Value;
    }

    private void AddToleranceToHolder(EntityUid uid, string reagentId, float amount, IPhysiologyHolder holder)
    {
        if (!holder.Tolerances.TryGetValue(reagentId, out var tolerance))
        {
            tolerance = new ToleranceData();
            holder.Tolerances[reagentId] = tolerance;
        }

        RefreshTolerance(tolerance);

        if (tolerance.Value <= 0f)
        {
            holder.Tolerances.Remove(reagentId);
            tolerance = new ToleranceData();
            holder.Tolerances[reagentId] = tolerance;
        }

        tolerance.Value = Math.Clamp(tolerance.Value + amount, 0f, 1f);
        tolerance.LastChanged = _timing.CurTime;
    }

    private void RemoveToleranceFromHolder(string reagentId, float amount, IPhysiologyHolder holder)
    {
        if (!holder.Tolerances.TryGetValue(reagentId, out var tolerance))
            return;

        RefreshTolerance(tolerance);

        tolerance.Value = Math.Clamp(tolerance.Value - amount, 0f, 1f);

        if (tolerance.Value <= 0f)
            holder.Tolerances.Remove(reagentId);
    }

    private void RefreshTolerance(ToleranceData tolerance)
    {
        if (tolerance.LastChanged == TimeSpan.Zero)
            return;

        var elapsed = (_timing.CurTime - tolerance.LastChanged).TotalSeconds;
        var decay = (float)(elapsed * tolerance.DecayRate);

        tolerance.Value = Math.Clamp(tolerance.Value - decay, 0f, 1f);
        tolerance.LastChanged = _timing.CurTime;
    }

    private bool TryGetHolder(EntityUid uid, out IPhysiologyHolder holder)
    {
        if (TryComp<BodyPhysiologyComponent>(uid, out var body))
        {
            holder = body;
            return true;
        }

        if (TryComp<OrganPhysiologyComponent>(uid, out var organ))
        {
            holder = organ;
            return true;
        }

        holder = default!;
        return false;
    }
}
