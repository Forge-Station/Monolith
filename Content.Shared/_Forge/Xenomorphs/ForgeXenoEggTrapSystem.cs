using Content.Shared.Mobs.Components;
using Content.Shared.Stunnable;
using Robust.Shared.Physics.Events;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Xenomorphs;

/// <summary>
/// Facehugger eggs stun the first non-hive walker that bumps them, then hatch open.
/// </summary>
public sealed class ForgeXenoEggTrapSystem : EntitySystem
{
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ForgeXenoEggTrapComponent, StartCollideEvent>(OnCollide);
    }

    private void OnCollide(Entity<ForgeXenoEggTrapComponent> ent, ref StartCollideEvent args)
    {
        if (ent.Comp.StunSeconds <= 0f)
            return;

        if (HasComp<ForgeXenoComponent>(args.OtherEntity))
            return;

        if (!HasComp<MobStateComponent>(args.OtherEntity))
            return;

        _stun.TryParalyze(args.OtherEntity, TimeSpan.FromSeconds(ent.Comp.StunSeconds), true);

        var coords = Transform(ent).Coordinates;
        var opened = ent.Comp.OpenedPrototype;
        QueueDel(ent.Owner);

        if (_prototypes.HasIndex(opened))
            Spawn(opened, coords);
    }
}
