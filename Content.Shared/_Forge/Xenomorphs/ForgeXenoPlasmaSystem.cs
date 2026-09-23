using Content.Shared.Alert;
using Content.Shared.Movement.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._Forge.Xenomorphs;

public sealed class ForgeXenoPlasmaSystem : EntitySystem
{
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ForgeXenoPlasmaComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ForgeXenoPlasmaComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ForgeXenoFortifyComponent, RefreshMovementSpeedModifiersEvent>(OnFortifySpeed);
        SubscribeLocalEvent<ForgeXenoPheromonesComponent, RefreshMovementSpeedModifiersEvent>(OnPheromoneSpeed);
    }

    private void OnStartup(Entity<ForgeXenoPlasmaComponent> ent, ref ComponentStartup args)
    {
        UpdateAlert(ent);
    }

    private void OnShutdown(Entity<ForgeXenoPlasmaComponent> ent, ref ComponentShutdown args)
    {
        _alerts.ClearAlert(ent, ent.Comp.Alert);
    }

    public bool TryUse(EntityUid uid, float cost, ForgeXenoPlasmaComponent? plasma = null)
    {
        if (cost <= 0f)
            return true;

        if (!Resolve(uid, ref plasma, false))
            return false;

        if (plasma.Plasma < cost)
            return false;

        plasma.Plasma -= cost;
        Dirty(uid, plasma);
        UpdateAlert((uid, plasma));
        return true;
    }

    public void Add(EntityUid uid, float amount, ForgeXenoPlasmaComponent? plasma = null)
    {
        if (!Resolve(uid, ref plasma, false) || amount == 0f)
            return;

        plasma.Plasma = Math.Clamp(plasma.Plasma + amount, 0f, plasma.MaxPlasma);
        Dirty(uid, plasma);
        UpdateAlert((uid, plasma));
    }

    public void UpdateAlert(Entity<ForgeXenoPlasmaComponent> ent)
    {
        if (ent.Comp.MaxPlasma <= 0f)
            return;

        var severity = (short) Math.Clamp((int) Math.Round(ent.Comp.Plasma / ent.Comp.MaxPlasma * 7f), 0, 7);
        _alerts.ShowAlert(ent, ent.Comp.Alert, severity);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < 1f)
            return;

        var dt = _accumulator;
        _accumulator = 0f;

        var query = EntityQueryEnumerator<ForgeXenoPlasmaComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var plasma, out var xform))
        {
            if (plasma.MaxPlasma <= 0f || plasma.Plasma >= plasma.MaxPlasma)
                continue;

            if (_timing.IsFirstTimePredicted == false)
                continue;

            var regen = OnWeeds(xform) ? plasma.RegenOnWeeds : plasma.Regen;
            if (HasComp<ForgeXenoRestingComponent>(uid))
                regen *= plasma.RestMultiplier;

            if (regen <= 0f)
                continue;

            plasma.Plasma = Math.Min(plasma.MaxPlasma, plasma.Plasma + regen * dt);
            Dirty(uid, plasma);
            UpdateAlert((uid, plasma));
        }
    }

    private bool OnWeeds(TransformComponent xform)
    {
        foreach (var ent in _lookup.GetEntitiesInRange<ForgeXenoWeedsComponent>(xform.Coordinates, 0.5f))
        {
            if (ent.Owner.IsValid())
                return true;
        }

        return false;
    }

    private void OnFortifySpeed(Entity<ForgeXenoFortifyComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!ent.Comp.Active)
            return;

        args.ModifySpeed(ent.Comp.SpeedMultiplier, ent.Comp.SpeedMultiplier);
    }

    private void OnPheromoneSpeed(Entity<ForgeXenoPheromonesComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Active)
            args.ModifySpeed(ent.Comp.SpeedBonus, ent.Comp.SpeedBonus);
    }
}
