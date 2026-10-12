using Content.Shared._Forge.Genetics.Components;
using Content.Shared.Damage;
using Content.Shared.Radiation.Events;
using Robust.Shared.Random;

namespace Content.Server._Forge.Genetics;

public sealed partial class GeneticsSystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;

    private void InitializeRadiation()
    {
        SubscribeLocalEvent<GenomeComponent, OnIrradiatedEvent>(OnIrradiated);
    }

    private void OnIrradiated(Entity<GenomeComponent> ent, ref OnIrradiatedEvent args)
    {
        ent.Comp.RadiationAccumulated += args.TotalRads;
        if (ent.Comp.RadiationAccumulated < ent.Comp.RadiationThreshold)
            return;

        ent.Comp.RadiationAccumulated = 0f;
        MutateRandom(ent, _random.Next(8, 22), activateOnComplete: true, ent.Comp);
    }

    public void IrradiateSubject(EntityUid uid, int completionDelta = 25)
    {
        if (!CanMutate(uid))
        {
            _popup.PopupEntity(Loc.GetString("genetics-steel-no-mutate"), uid);
            return;
        }

        if (_mobState.IsCritical(uid))
        {
            _popup.PopupEntity(Loc.GetString("genetics-irradiate-critical"), uid);
            return;
        }

        var damage = new DamageSpecifier();
        damage.DamageDict["Radiation"] = 6;
        damage.DamageDict["Cellular"] = 1;
        _damageable.TryChangeDamage(uid, damage, origin: uid);

        MutateRandom(uid, completionDelta, activateOnComplete: true);
    }
}
