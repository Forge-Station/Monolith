using Content.Server.Fluids.EntitySystems;
using Content.Server.NPC.HTN;
using Content.Server._WF.Remade.Conversion;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using System.Numerics;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Player;

namespace Content.Server._WF.Remade.Limbs;

/// <summary>Raised on a mob when one of its limbs comes off.</summary>
public sealed class WFRemadeLimbSeveredEvent(string limb, EntityUid? item = null) : EntityEventArgs
{
    public readonly string Limb = limb;

    /// <summary>The severed limb left on the floor, if it left one.</summary>
    public readonly EntityUid? Item = item;
}

/// <summary>
/// Shares hits between a mob's limbs by weight, severs a limb once it has taken enough of the right kind of damage, and
/// charges the mob for the loss. Rules from DS13's attempt_dismemberment.
/// </summary>
public sealed partial class WFRemadeLimbsSystem : SharedWFRemadeLimbsSystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const string BloodReagent = "Blood";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFRemadeLimbsComponent, DamageModifyEvent>(OnModify);
        SubscribeLocalEvent<WFRemadeLimbsComponent, DamageChangedEvent>(OnChanged);
    }

    /// <summary>Picks the limb a hit lands on and scales the damage by how well that limb is armoured.</summary>
    private void OnModify(Entity<WFRemadeLimbsComponent> ent, ref DamageModifyEvent args)
    {
        if (args.Damage.GetTotal() <= FixedPoint2.Zero)
            return;

        var limb = PickLimb(ent, args.TargetPart, args.Origin);
        if (limb == null)
            return;

        ent.Comp.PendingLimb = limb.Id;
        ent.Comp.PendingAt = _timing.CurTime;
        if (limb.Scale != 1f)
            args.Damage = args.Damage * limb.Scale;
    }

    /// <summary>Counts the damage that landed against the limb that took it, and severs the limb if it's had enough.</summary>
    private void OnChanged(Entity<WFRemadeLimbsComponent> ent, ref DamageChangedEvent args)
    {
        var comp = ent.Comp;
        if (comp.PendingLimb is not { } id)
            return;

        comp.PendingLimb = null;
        if (comp.PendingAt != _timing.CurTime || args.DamageDelta == null || !args.DamageIncreased)
            return;

        var limb = comp.Limbs.Find(l => l.Id == id);
        if (limb == null || !limb.Severable || comp.Severed.Contains(id))
            return;

        var dealt = 0f;
        var fraction = float.MaxValue;
        foreach (var (type, amount) in args.DamageDelta.DamageDict)
        {
            if (amount <= FixedPoint2.Zero)
                continue;

            dealt += amount.Float();
            fraction = Math.Min(fraction, comp.SeverFractions.GetValueOrDefault(type, comp.DefaultSeverFraction));
        }

        var total = comp.Damage.GetValueOrDefault(id) + dealt;
        comp.Damage[id] = total;

        if (args.CanSever && total >= limb.Health * fraction)
            Sever(ent, limb, args.Origin);
    }

    /// <summary>Chooses a limb: the one aimed at, if the attacker aims and holds the aim, otherwise by weight.</summary>
    private WFRemadeLimb? PickLimb(Entity<WFRemadeLimbsComponent> ent, TargetBodyPart? explicitPart, EntityUid? origin)
    {
        var comp = ent.Comp;
        var aimed = explicitPart is { } part && SharedTargetingSystem.IsSelectable(part) ? part : (TargetBodyPart?) null;
        var certain = aimed != null;
        if (aimed == null
            && origin is { } attacker
            && HasComp<ActorComponent>(attacker)
            && TryComp(attacker, out TargetingComponent? targeting)
            && SharedTargetingSystem.IsSelectable(targeting.Target))
        {
            aimed = targeting.Target;
        }

        if (aimed is { } zone && (certain || _random.Prob(comp.AimedChance)))
        {
            var hit = comp.Limbs.Find(l => l.Zones.Contains(zone));
            if (hit != null && !comp.Severed.Contains(hit.Id))
                return hit;

            // Aimed at nothing that's left, so it hits the body.
            var body = comp.Limbs.Find(l => !l.Severable);
            if (body != null)
                return body;
        }

        var weight = 0f;
        foreach (var limb in comp.Limbs)
        {
            if (!comp.Severed.Contains(limb.Id))
                weight += limb.Weight;
        }

        var roll = _random.NextFloat(0f, weight);
        WFRemadeLimb? last = null;
        foreach (var limb in comp.Limbs)
        {
            if (comp.Severed.Contains(limb.Id))
                continue;

            last = limb;
            roll -= limb.Weight;
            if (roll <= 0f)
                return limb;
        }

        return last;
    }

    /// <summary>Cuts a limb off by id. Returns false if there's no such limb, it can't come off or it already has.</summary>
    public bool TrySever(EntityUid uid, string limbId, EntityUid? origin = null, WFRemadeLimbsComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return false;

        var limb = comp.Limbs.Find(l => l.Id == limbId);
        if (limb == null || !limb.Severable || comp.Severed.Contains(limbId))
            return false;

        Sever((uid, comp), limb, origin);
        return true;
    }

    private void Sever(Entity<WFRemadeLimbsComponent> ent, WFRemadeLimb limb, EntityUid? origin)
    {
        var comp = ent.Comp;
        comp.Severed.Add(limb.Id);
        Dirty(ent);

        var name = string.IsNullOrEmpty(limb.Name.Id) ? limb.Id : Loc.GetString(limb.Name.Id);
        var coordinates = _transform.GetMoverCoordinates(ent);

        EntityUid? dropped = null;
        if ((limb.Dropped ?? limb.Lying) is { } droppedState)
        {
            var item = Spawn(comp.Item, coordinates.Offset(new Vector2(_random.NextFloat(-0.4f, 0.4f), _random.NextFloat(-0.4f, 0.4f))));
            var severed = EnsureComp<WFSeveredLimbComponent>(item);
            severed.Rsi = comp.Rsi;
            severed.State = droppedState;
            severed.Source = ent;
            severed.Limb = limb.Id;
            Dirty(item, severed);
            _metaData.SetEntityName(item, Loc.GetString("wf-remade-limb-severed-name", ("limb", name)));
            dropped = item;
        }

        if (comp.Blood > 0f)
            _puddle.TrySpillAt(coordinates, new Solution(BloodReagent, FixedPoint2.New(comp.Blood)), out _, sound: false);

        _audio.PlayPvs(comp.SeverSound, ent);
        if (TryComp<WFRemadeVoiceComponent>(ent, out var voice))
            _audio.PlayPvs(voice.Pain, ent);

        _popup.PopupEntity(Loc.GetString("wf-remade-limb-severed", ("mob", ent.Owner), ("limb", name)), ent, PopupType.MediumCaution);

        _movement.RefreshMovementSpeedModifiers(ent);
        RefreshVision(ent);
        RaiseLocalEvent(ent, new WFRemadeLimbSeveredEvent(limb.Id, dropped));

        if (limb.Loss > 0f && _proto.TryIndex(comp.LossType, out var lossType))
        {
            _damageable.TryChangeDamage(ent, new DamageSpecifier(lossType, FixedPoint2.New(limb.Loss)),
                ignoreResistances: true, interruptsDoAfters: false, origin: origin, canSever: false);
        }
    }

    /// <summary>Scales how far the NPC sees targets by what its head and eyes still give.</summary>
    private void RefreshVision(Entity<WFRemadeLimbsComponent> ent)
    {
        if (!TryComp<HTNComponent>(ent, out var htn))
            return;

        var comp = ent.Comp;
        var sight = Stat(ent, WFRemadeLimbStat.Sight, comp);
        comp.BaseVision ??= htn.Blackboard.GetValueOrDefault<float>("VisionRadius", EntityManager);
        comp.BaseAggroVision ??= htn.Blackboard.GetValueOrDefault<float>("AggroVisionRadius", EntityManager);
        htn.Blackboard.SetValue("VisionRadius", comp.BaseVision.Value * sight);
        htn.Blackboard.SetValue("AggroVisionRadius", comp.BaseAggroVision.Value * sight);
    }
}
