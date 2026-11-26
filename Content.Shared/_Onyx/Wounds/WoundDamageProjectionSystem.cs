using System.Linq;
using Content.Shared.Body;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Rejuvenate;
using Content.Shared._Onyx.Chemistry.Circulation;
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE(Wolfmed): D12 damage facade.
using Content.Shared._WF.Wolfmed.Wounds; // WOLFGATE(Wolfmed): W5: the rejuvenate relay.
using Robust.Shared.Network;

namespace Content.Shared._Onyx.Wounds;

public sealed partial class WoundDamageProjectionSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedDamageableSystem _damage = default!; // WOLFGATE(Wolfmed): D12, Onyx-shaped damage API; see _WF/Wolfmed/Compat
    [Dependency] private INetManager _net = default!;
    [Dependency] private PainSystem _pain = default!;
    // WOLFGATE(Wolfmed): D15 drops the CirculatoryStreamSystem dependency. Organic is the primary stream in phase 1, so
    // SynchronizeStreams never attaches a CirculatoryStreamComponent to an organic body.
    [Dependency] private WoundSystem _wounds = default!; // WOLFGATE(Wolfmed): D17, OnRejuvenate now drives WoundSystem.ClearBodyWounds.

    private readonly HashSet<EntityUid> _projecting = new();

    public override void Initialize()
    {
        base.Initialize();
        // WOLFGATE(Wolfmed): InitialBodySystem is Nubody glue that does not exist here; Shitmed's SharedBodySystem builds the body.
        SubscribeLocalEvent<WoundHostComponent, MapInitEvent>(OnMapInit, after: [typeof(SharedBodySystem)]);
        SubscribeLocalEvent<WoundHostComponent, RejuvenateEvent>(OnRejuvenate);
        // WOLFGATE(Wolfmed): D11 - Wolfgate writes damage inline instead of subscribing, so one raise point cannot serve both
        // orderings. Routing keeps the pre-write DamageDealtEvent; the projection re-points to the post-write
        // DamageChangedEvent Wolfgate already raises.
        SubscribeLocalEvent<WoundableComponent, DamageChangedEvent>(OnPartDamageDealt);
    }

    private void OnMapInit(Entity<WoundHostComponent> body, ref MapInitEvent args)
    {
        SetupBody(body);
        // WOLFGATE(Wolfmed): D15 - _circulation.SynchronizeStreams(body) is dead in phase 1 (Organic is the primary stream).
    }

    private void OnRejuvenate(Entity<WoundHostComponent> body, ref RejuvenateEvent args)
    {
        if (!_net.IsServer || !_projecting.Add(body))
            return;

        // WOLFGATE(Wolfmed): D17 - WoundSystem cannot own <BodyComponent, RejuvenateEvent> here, so its handler runs from this one.
        _wounds.ClearBodyWounds(body);

        try
        {
            if (TryComp(body, out SystemicDamageComponent? systemic))
            {
                systemic.Damage = new DamageSpecifier();
                Dirty(body, systemic);
            }

            foreach (var (part, _) in _body.GetBodyChildren(body))
                if (TryComp(part, out DamageableComponent? damageable))
                {
                    _damage.ClearAllDamage((part, damageable));
                    if (TryComp(part, out PainComponent? pain))
                    {
                        _pain.SetPain((part, pain), FixedPoint2.Zero);
                        _pain.ClearPainSuppression((part, pain));
                    }

                    if (TryComp(part, out WoundableComponent? woundable) &&
                        woundable.AmputationOverflow != FixedPoint2.Zero)
                    {
                        woundable.AmputationOverflow = FixedPoint2.Zero;
                        Dirty(part, woundable);
                    }
                }

            if (TryComp(body, out PainComponent? bodyPain))
                _pain.ClearPainSuppression((body, bodyPain));
        }
        finally
        {
            _projecting.Remove(body);
        }

        RefreshBodyDamage(body);

        // WOLFGATE(Wolfmed): W5: sepsis, dead tissue and tourniquets are not wounds, so clearing the wounds leaves
        // them behind. The Wolfmed handler is server-side and both RejuvenateEvent pairs here are taken.
        var rejuvenated = new WolfmedRejuvenateEvent(body);
        RaiseLocalEvent(ref rejuvenated);
    }

    // WOLFGATE(Wolfmed): D11 - was (Entity<WoundableComponent>, ref DamageDealtEvent).
    private void OnPartDamageDealt(Entity<WoundableComponent> part, ref DamageChangedEvent args)
    {
        if (!_net.IsServer || args.DamageDelta is not { } delta ||
            !TryComp(part, out BodyPartComponent? component))
            return;

        _pain.ApplyDamage(part, delta, component); // WOLFGATE(Wolfmed): D11, the post-write event carries the delta.

        if (component.Body is { } body)
            RefreshBodyDamage(body);
        else
            RefreshDetachedDamage(GetDetachedRoot(part));
    }

    public void OnPartInserted(EntityUid part, EntityUid body)
    {
        if (!HasComp<WoundHostComponent>(body))
            return;

        SetupPart(part);
        RefreshBodyPain(body);
        RefreshBodyDamage(body);
    }

    public void OnPartRemoved(EntityUid part, EntityUid body)
    {
        RefreshDetachedDamage(part);
        RefreshBodyPain(body);
        RefreshBodyDamage(body);
    }

    private EntityUid GetDetachedRoot(EntityUid part)
    {
        var root = part;
        // WOLFGATE(Wolfmed): Shitmed's BodyPartComponent has no Parent field; walk the slot containers instead.
        while (_body.GetParentPartOrNull(root) is { } parent)
            root = parent;

        return root;
    }

    private void RefreshDetachedDamage(EntityUid root)
    {
        if (!_net.IsServer || !HasComp<BodyPartComponent>(root))
            return;

        var visual = EnsureComp<PartDamageVisualsComponent>(root);
        visual.Damage.Clear();
        foreach (var (part, _) in _body.GetBodyPartChildren(root))
        {
            if (!TryComp(part, out DamageableComponent? damageable) || !TryGetVisualLayer(part, out var layer))
                continue;

            var damage = _damage.GetPositiveDamage((part, damageable));
            if (!visual.Damage.TryGetValue(layer, out var current))
                visual.Damage[layer] = damage.Clone();
            else
                visual.Damage[layer] = current + damage;
        }

        Dirty(root, visual);
    }

    public void RefreshBodyDamage(EntityUid body)
    {
        if (!_net.IsServer || !HasComp<WoundHostComponent>(body) || !_projecting.Add(body))
            return;

        try
        {
            var total = new DamageSpecifier();
            if (TryComp(body, out SystemicDamageComponent? systemic))
            {
                foreach (var (type, amount) in systemic.Damage.DamageDict.ToArray())
                {
                    if (_damage.CanBeDamagedBy(body, type))
                    {
                        total.DamageDict[type] = amount;
                        continue;
                    }

                    systemic.Damage.DamageDict.Remove(type);
                    Dirty(body, systemic);
                }
            }
            var visual = EnsureComp<PartDamageVisualsComponent>(body);
            visual.Damage.Clear();
            foreach (var (part, _) in _body.GetBodyChildren(body))
            {
                if (TryComp(part, out DamageableComponent? damageable))
                {
                    var damage = _damage.GetPositiveDamage((part, damageable));
                    total += damage;
                    if (TryGetVisualLayer(part, out var layer))
                    {
                        if (!visual.Damage.TryGetValue(layer, out var current))
                            visual.Damage[layer] = damage.Clone();
                        else
                            visual.Damage[layer] = current + damage;
                    }
                }
            }

            _damage.SetDamage(body, total);
            Dirty(body, visual);
        }
        finally
        {
            _projecting.Remove(body);
        }
    }

    private void SetupBody(EntityUid body)
    {
        if (!_net.IsServer)
            return;

        EnsureComp<SystemicDamageComponent>(body);
        EnsureComp<PartDamageVisualsComponent>(body);
        EnsureComp<PainComponent>(body);
        foreach (var (part, _) in _body.GetBodyChildren(body))
            SetupPart(part);
        RefreshBodyPain(body);
        RefreshBodyDamage(body);
    }

private void SetupPart(EntityUid part)
    {
        EnsureComp<WoundableComponent>(part);
        EnsureComp<DamageableComponent>(part);
        if (_pain.CanFeelPain(part))
            EnsureComp<PainComponent>(part);
        else
            RemComp<PainComponent>(part);
        EnsureComp<BodyPartFunctionalityComponent>(part);
        // WOLFGATE(Wolfmed): D19 - InjurableComponent is not ported. Wolfgate parts already carry
        // Damageable(damageContainer: OrganicPart) statically from _Shitmed/Body/Parts/base.yml.
    }

    private void RefreshBodyPain(EntityUid body)
    {
        var value = FixedPoint2.Zero;
        foreach (var (part, _) in _body.GetBodyChildren(body))
            value += _pain.GetRawPain(part);
        _pain.SetPain((body, EnsureComp<PainComponent>(body)), value);
    }

    // WOLFGATE(Wolfmed): V3: public so the degradation overlay maps parts to layers the same way instead of forking it.
    public bool TryGetVisualLayer(EntityUid part, out HumanoidVisualLayers layer)
    {
        layer = default;
        if (!TryComp(part, out BodyPartComponent? component))
            return false;

        switch (component.PartType, component.Symmetry)
        {
            case (BodyPartType.Torso, _): layer = HumanoidVisualLayers.Chest; return true; // WOLFGATE(Wolfmed): D9
            // WOLFGATE(Wolfmed): D9 - no BodyPartType.Groin and no HumanoidVisualLayers.Groin in Wolfgate.
            case (BodyPartType.Head, _): layer = HumanoidVisualLayers.Head; return true;
            case (BodyPartType.Arm, BodyPartSymmetry.Left): layer = HumanoidVisualLayers.LArm; return true;
            case (BodyPartType.Arm, BodyPartSymmetry.Right): layer = HumanoidVisualLayers.RArm; return true;
            case (BodyPartType.Hand, BodyPartSymmetry.Left): layer = HumanoidVisualLayers.LHand; return true;
            case (BodyPartType.Hand, BodyPartSymmetry.Right): layer = HumanoidVisualLayers.RHand; return true;
            case (BodyPartType.Leg, BodyPartSymmetry.Left): layer = HumanoidVisualLayers.LLeg; return true;
            case (BodyPartType.Leg, BodyPartSymmetry.Right): layer = HumanoidVisualLayers.RLeg; return true;
            case (BodyPartType.Foot, BodyPartSymmetry.Left): layer = HumanoidVisualLayers.LFoot; return true;
            case (BodyPartType.Foot, BodyPartSymmetry.Right): layer = HumanoidVisualLayers.RFoot; return true;
            default: return false;
        }
    }
}
