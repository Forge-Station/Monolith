using Content.Shared.Damage;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Elites;

/// <summary>Applies and expires the hunter's taunt buff.</summary>
public sealed partial class WFTauntSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFTauntBuffComponent, DamageModifyEvent>(OnModify);
    }

    private void OnModify(Entity<WFTauntBuffComponent> ent, ref DamageModifyEvent args)
    {
        args.Damage = args.Damage * ent.Comp.Multiplier;
    }

    /// <summary>Puts the buff on a mob for a while, or renews it. A stronger buff already there stays.</summary>
    public void Buff(EntityUid uid, float multiplier, float seconds)
    {
        var buff = EnsureComp<WFTauntBuffComponent>(uid);
        buff.Multiplier = buff.Until > _timing.CurTime ? Math.Min(buff.Multiplier, multiplier) : multiplier;
        buff.Until = _timing.CurTime + TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Takes the buff off now.</summary>
    public void Clear(EntityUid uid)
    {
        RemCompDeferred<WFTauntBuffComponent>(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFTauntBuffComponent>();
        while (query.MoveNext(out var uid, out var buff))
        {
            if (now >= buff.Until)
                RemCompDeferred<WFTauntBuffComponent>(uid);
        }
    }
}
