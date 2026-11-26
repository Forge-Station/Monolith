using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.FixedPoint;

namespace Content.Server._WF.Wolfmed.Body;

/// <summary>
/// Shows a body's eye damage (welder flashes, bright light) on its eyes organ, so the analyzer reads it and the eye
/// surgery lists. Eye damage blurs and blinds by itself, so it never takes the organ below one health.
/// </summary>
public sealed class WolfmedOpticsSystem : EntitySystem
{
    [Dependency] private BlindableSystem _blindable = default!;
    [Dependency] private OrganHealthSystem _organs = default!;
    [Dependency] private SharedBodySystem _body = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundHostComponent, EyeDamageChangedEvent>(OnEyeDamageChanged);
    }

    private void OnEyeDamageChanged(Entity<WoundHostComponent> body, ref EyeDamageChangedEvent args)
    {
        if (!TryComp(body, out BlindableComponent? sight) || sight.MaxDamage <= 0 ||
            GetEyes(body) is not { } eyes)
            return;

        var strain = EnsureComp<WolfmedOpticStrainComponent>(eyes);

        // An organ at zero is destroyed on the next organ tick; strain clearing in between must not hand it health back.
        if (eyes.Comp.Health <= FixedPoint2.Zero)
        {
            strain.Applied = FixedPoint2.Zero;
            return;
        }
        var target = FixedPoint2.New(eyes.Comp.MaxHealth.Float() * Math.Clamp(args.Damage, 0, sight.MaxDamage) / sight.MaxDamage);
        var room = FixedPoint2.Max(FixedPoint2.Zero, strain.Applied + eyes.Comp.Health - FixedPoint2.New(1));
        target = FixedPoint2.Min(target, room);

        var delta = target - strain.Applied;
        if (delta == FixedPoint2.Zero)
            return;

        strain.Applied = target;
        _organs.ChangeHealth(eyes, -delta);
    }

    /// <summary>Clears the body's eye damage, which gives the eyes organ back what it took.</summary>
    public void ClearEyeDamage(EntityUid body)
    {
        if (TryComp(body, out BlindableComponent? sight) && sight.EyeDamage > sight.MinDamage)
            _blindable.AdjustEyeDamage((body, sight), sight.MinDamage - sight.EyeDamage);
    }

    private Entity<WolfmedOrganComponent>? GetEyes(EntityUid body)
    {
        foreach (var (organ, comp) in _body.GetBodyOrgans(body))
        {
            if (comp.SlotId == "eyes" && TryComp(organ, out WolfmedOrganComponent? health))
                return (organ, health);
        }

        return null;
    }
}

/// <summary>Organ health an eyes organ has lost to its body's eye damage, given back as that damage heals.</summary>
[RegisterComponent]
public sealed partial class WolfmedOpticStrainComponent : Component
{
    [ViewVariables]
    public FixedPoint2 Applied;
}
