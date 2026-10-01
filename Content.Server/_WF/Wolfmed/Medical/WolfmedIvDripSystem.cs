using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Medical.Components;
using Content.Server.Popups;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Medical;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chat;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Medical;

/// <summary>
/// The IV drip, as tgstation's (Nova's) works: one hung container, a needle put in over a second, inject or take mode
/// at a flow in units a second, and a needle ripped out of an arm when the patient walks off.
/// </summary>
public sealed partial class WolfmedIvDripSystem : EntitySystem
{
    /// <summary>The stack a hung Bloodpack belongs to; the pod's reservoir and the fault prototype read it too.</summary>
    public static readonly EntProtoId PackPrototype = "Bloodpack";

    /// <summary>tg's fill overlay thresholds, in percent.</summary>
    private static readonly int[] FillThresholds = { 0, 10, 25, 50, 75, 80, 90 };

    private static readonly ProtoId<DamageTypePrototype> Piercing = "Piercing";
    private static readonly ProtoId<ReagentPrototype> PackColourReagent = "Blood";

    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private ReactiveSystem _reactive = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedStackSystem _stacks = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private WoundSystem _wounds = default!;

    private float _rateDefault = 5f;
    private float _rateMin;
    private float _rateMax = 15f;
    private float _rateStep = 0.01f;
    private float _unitsPerPack = 30f;
    private float _attachSeconds = 1f;
    private float _ripDamage = 3f;
    private float _ripSeverity = 10f;
    private float _beepBelow = 0.85f;
    private float _beepChance = 0.025f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WolfmedIvDripComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<WolfmedIvDripComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WolfmedIvDripComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WolfmedIvDripComponent, GetVerbsEvent<AlternativeVerb>>(OnAlternativeVerbs);
        SubscribeLocalEvent<WolfmedIvDripComponent, GetVerbsEvent<Verb>>(OnVerbs);
        SubscribeLocalEvent<WolfmedIvDripComponent, DragDropDraggedEvent>(OnDragDropped);
        SubscribeLocalEvent<WolfmedIvDripComponent, WolfmedIvAttachDoAfterEvent>(OnAttachDoAfter);
        SubscribeLocalEvent<WolfmedIvDripComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WolfmedIvDripComponent, EntInsertedIntoContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<WolfmedIvDripComponent, EntRemovedFromContainerMessage>(OnContainerRemoved);

        Subs.CVar(_cfg, WolfmedCVars.IvRateDefault, value => _rateDefault = value, true);
        Subs.CVar(_cfg, WolfmedCVars.IvRateMin, value => _rateMin = MathF.Max(0f, value), true);
        Subs.CVar(_cfg, WolfmedCVars.IvRateMax, value => _rateMax = value, true);
        Subs.CVar(_cfg, WolfmedCVars.IvRateStep, value => _rateStep = value, true);
        Subs.CVar(_cfg, WolfmedCVars.IvUnitsPerPack, value => _unitsPerPack = MathF.Max(0.1f, value), true);
        Subs.CVar(_cfg, WolfmedCVars.IvAttachSeconds, value => _attachSeconds = MathF.Max(0f, value), true);
        Subs.CVar(_cfg, WolfmedCVars.IvRipDamage, value => _ripDamage = MathF.Max(0f, value), true);
        Subs.CVar(_cfg, WolfmedCVars.IvRipWoundSeverity, value => _ripSeverity = MathF.Max(0f, value), true);
        Subs.CVar(_cfg, WolfmedCVars.IvBeepBelow, value => _beepBelow = value, true);
        Subs.CVar(_cfg, WolfmedCVars.IvBeepChance, value => _beepChance = Math.Clamp(value, 0f, 1f), true);
    }

    private void OnInit(Entity<WolfmedIvDripComponent> ent, ref ComponentInit args)
    {
        _containers.EnsureContainer<ContainerSlot>(ent, WolfmedIvDripComponent.ContainerId);
        if (ent.Comp.Rate < 0f)
            ent.Comp.Rate = ClampRate(_rateDefault);
    }

    private void OnMapInit(Entity<WolfmedIvDripComponent> ent, ref MapInitEvent args)
    {
        UpdateAppearance(ent);
    }

    #region Flow

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WolfmedIvDripComponent>();
        while (query.MoveNext(out var uid, out var drip))
        {
            if (drip.Patient is not { } patient)
                continue;

            var ent = (uid, drip);
            if (!CheckNeedle(ent, patient) || now < drip.NextUpdate)
                continue;

            drip.NextUpdate = now + TimeSpan.FromSeconds(MathF.Max(0.1f, drip.Interval));
            Transfer(ent, patient, drip.Rate * MathF.Max(0.1f, drip.Interval));
        }
    }

    /// <summary>
    /// False when the needle came out: cleanly for a patient no longer on the floor (a pod, a locker) or gone, ripped
    /// out of an arm for one who walked more than a tile from the stand.
    /// </summary>
    private bool CheckNeedle(Entity<WolfmedIvDripComponent> ent, EntityUid patient)
    {
        if (TerminatingOrDeleted(patient) || _containers.IsEntityInContainer(patient) ||
            _containers.IsEntityInContainer(ent.Owner))
        {
            Detach(ent, announce: !TerminatingOrDeleted(patient));
            return false;
        }

        var from = _xform.GetMapCoordinates(ent.Owner);
        var to = _xform.GetMapCoordinates(patient);
        if (from.MapId == to.MapId && (from.Position - to.Position).Length() <= ent.Comp.Range)
            return true;

        RipOut(ent, patient);
        return false;
    }

    /// <summary>
    /// tg's rip: a little Piercing into a random arm (the chest without one) through the routing, and, as tg adds a
    /// moderate pierce wound on top of its 3 brute, the wound that opens raised to a severity that bleeds.
    /// </summary>
    private void RipOut(Entity<WolfmedIvDripComponent> ent, EntityUid patient)
    {
        var arms = new List<(EntityUid Part, TargetBodyPart Target)>();
        foreach (var (id, part) in _body.GetBodyChildrenOfType(patient, BodyPartType.Arm))
        {
            if (_body.GetTargetBodyPart(part.PartType, part.Symmetry) is { } target)
                arms.Add((id, target));
        }

        if (arms.Count == 0)
        {
            foreach (var (torso, _) in _body.GetBodyChildrenOfType(patient, BodyPartType.Torso))
            {
                arms.Add((torso, TargetBodyPart.Torso));
                break;
            }
        }

        var (hitPart, where) = arms.Count > 0 ? _random.Pick(arms) : (EntityUid.Invalid, TargetBodyPart.Torso);
        if (_ripDamage > 0f)
        {
            var before = hitPart.IsValid()
                ? _wounds.GetWounds(hitPart).ToDictionary(wound => wound.Owner, wound => wound.Comp.Severity)
                : new Dictionary<EntityUid, FixedPoint2>();

            var damage = new DamageSpecifier { DamageDict = { [Piercing] = FixedPoint2.New(_ripDamage) } };
            _damageable.TryChangeDamage(patient, damage, origin: ent.Owner, targetPart: where, canSever: false);

            // The wound the needle tore: new on the part, or grown by the hit.
            if (hitPart.IsValid() && _ripSeverity > 0f)
            {
                foreach (var wound in _wounds.GetWounds(hitPart).ToList())
                {
                    if (before.TryGetValue(wound.Owner, out var was) && wound.Comp.Severity <= was)
                        continue;

                    var missing = FixedPoint2.New(_ripSeverity) - wound.Comp.Severity;
                    if (missing > FixedPoint2.Zero)
                        _wounds.ChangeSeverity(wound.Owner, missing);
                    break;
                }
            }
        }

        _popup.PopupEntity(Loc.GetString("wolfmed-iv-ripped-patient"), patient, patient, PopupType.LargeCaution);
        _popup.PopupEntity(Loc.GetString("wolfmed-iv-ripped-others", ("target", Identity.Entity(patient, EntityManager))),
            patient, Filter.PvsExcept(patient), true, PopupType.MediumCaution);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(patient):target} had the needle of {ToPrettyString(ent.Owner):drip} ripped out into {where}");

        Detach(ent, announce: false);
    }

    /// <summary>One transfer of up to <paramref name="units"/>, in the drip's mode.</summary>
    private void Transfer(Entity<WolfmedIvDripComponent> ent, EntityUid patient, float units)
    {
        if (units <= 0f)
            return;

        var container = GetContainer(ent);
        if (ent.Comp.Mode == WolfmedIvMode.Inject)
        {
            // Playtest 5: the opened pack keeps giving with nothing hung; only a fresh one needs the stack.
            var pack = container is { } hung && IsPack(ent, hung) ? hung : (EntityUid?) null;
            if (container == null || pack != null)
            {
                if ((pack != null || ent.Comp.PackOpened > 0f) && PacksCanTreat(patient, pack))
                    TransfuseFromPack(pack, ref ent.Comp.PackOpened, patient, units);
            }
            else
            {
                InjectSolution(ent, container.Value, patient, units);
            }
        }
        else if (container is { } beaker && !IsPack(ent, beaker))
        {
            TakeBlood(ent, beaker, patient, units);
        }

        UpdateAppearance(ent);
    }

    /// <summary>
    /// Gives up to <paramref name="units"/> of the body's own blood reagent out of the opened pack, then out of a
    /// Bloodpack stack a pack at a time. Playtest 5: a pack is spent from the stack the moment it is opened, and
    /// <paramref name="opened"/> carries what is left of it, so a stack taken away and hung again gives nothing
    /// back. Nothing is spent on a full bloodstream. Returns the units given. Shared with the pod's blood reservoir.
    /// </summary>
    public float TransfuseFromPack(EntityUid? pack, ref float opened, EntityUid body, float units)
    {
        if (!TryComp(body, out BloodstreamComponent? bloodstream) ||
            !_solutions.ResolveSolution(body, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var blood))
            return 0f;

        var room = (blood.MaxVolume - blood.Volume).Float();
        units = MathF.Min(units, room);
        var given = 0f;

        while (units > 0.005f)
        {
            if (opened <= 0.005f)
            {
                if (pack is not { } stackUid || TerminatingOrDeleted(stackUid) ||
                    !TryComp(stackUid, out StackComponent? stack) || stack.Count <= 0 ||
                    !_stacks.Use(stackUid, 1, stack))
                    break;

                opened = _unitsPerPack;
            }

            var take = MathF.Min(units, opened);
            if (!_bloodstream.TryModifyBloodLevel(body, FixedPoint2.New(take), bloodstream))
                break;

            opened -= take;
            given += take;
            units -= take;
        }

        return given;
    }

    /// <summary>Units of blood left: the opened pack plus every unopened pack of the stack, if there is one.</summary>
    public float PackUnitsLeft(EntityUid? pack, float opened)
    {
        var stacked = pack is { } stackUid && TryComp(stackUid, out StackComponent? stack) && stack.Count > 0
            ? stack.Count * _unitsPerPack
            : 0f;
        return MathF.Max(0f, stacked + opened);
    }

    /// <summary>Units of blood one pack is worth.</summary>
    public float UnitsPerPack => _unitsPerPack;

    /// <summary>
    /// Whether blood packs work on this body at all: the same damage container rule the pack follows by hand, so a
    /// chassis is never filled with a flesh body's blood. With no pack given, the Bloodpack prototype's rule.
    /// </summary>
    public bool PacksCanTreat(EntityUid body, EntityUid? pack = null)
    {
        HealingComponent? healing = null;
        if (pack is { } held)
            TryComp(held, out healing);
        else if (_protos.TryIndex(PackPrototype, out var proto))
            proto.TryGetComponent(out healing, _factory);

        if (healing?.DamageContainers is not { } containers)
            return HasComp<BloodstreamComponent>(body);

        return HasComp<BloodstreamComponent>(body) &&
               TryComp(body, out DamageableComponent? damageable) &&
               damageable.DamageContainerID is { } id &&
               containers.Contains(id.Id);
    }

    /// <summary>
    /// Hypospray-style injection of a beaker or jug. The patient's own blood reagent in it goes back into their blood;
    /// everything else goes into the chemical stream. What does not fit stays in the container.
    /// </summary>
    private void InjectSolution(Entity<WolfmedIvDripComponent> ent, EntityUid container, EntityUid patient, float units)
    {
        if (!TryGetSolution(container, out var soln, out var solution) || solution.Volume <= FixedPoint2.Zero ||
            !TryComp(patient, out BloodstreamComponent? bloodstream))
            return;

        var taken = _solutions.SplitSolution(soln.Value, FixedPoint2.Min(FixedPoint2.New(units), solution.Volume));
        var own = taken.SplitSolutionWithOnly(taken.Volume, bloodstream.BloodReagent.Id);
        var leftover = new Solution();

        if (own.Volume > FixedPoint2.Zero)
        {
            var room = _solutions.ResolveSolution(patient, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var blood)
                ? blood.AvailableVolume
                : FixedPoint2.Zero;
            var fits = FixedPoint2.Min(room, own.Volume);
            if (fits > FixedPoint2.Zero && _bloodstream.TryModifyBloodLevel(patient, fits, bloodstream))
                own.SplitSolution(fits);
            leftover.AddSolution(own, _protos);
        }

        if (taken.Volume > FixedPoint2.Zero)
        {
            if (_solutions.TryGetInjectableSolution(patient, out var chem, out var chemSolution))
            {
                var fits = FixedPoint2.Min(chemSolution.AvailableVolume, taken.Volume);
                var injected = taken.SplitSolution(fits);
                if (injected.Volume > FixedPoint2.Zero)
                {
                    _reactive.DoEntityReaction(patient, injected, ReactionMethod.Injection);
                    _solutions.TryAddSolution(chem.Value, injected);
                }
            }

            leftover.AddSolution(taken, _protos);
        }

        if (leftover.Volume > FixedPoint2.Zero)
            _solutions.TryAddSolution(soln.Value, leftover);
    }

    /// <summary>
    /// The patient's blood into the container. A full container stops the flow with a ping, as tg's does; a patient
    /// under the safe line makes the drip beep loudly now and then.
    /// </summary>
    private void TakeBlood(Entity<WolfmedIvDripComponent> ent, EntityUid container, EntityUid patient, float units)
    {
        if (!TryGetSolution(container, out var soln, out var solution) ||
            !TryComp(patient, out BloodstreamComponent? bloodstream) ||
            !_solutions.ResolveSolution(patient, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var blood))
            return;

        var amount = FixedPoint2.Min(FixedPoint2.New(units), solution.AvailableVolume);
        if (amount <= FixedPoint2.Zero)
        {
            ent.Comp.Rate = ClampRate(_rateMin);
            ent.Comp.Pings++;
            _chat.TrySendInGameICMessage(ent.Owner, Loc.GetString("wolfmed-iv-pings"), InGameICChatType.Emote,
                ChatTransmitRange.Normal, ignoreActionBlocker: true);
            _audio.PlayPvs(ent.Comp.PingSound, ent.Owner);
            return;
        }

        if (blood.FillFraction < _beepBelow && _random.Prob(Math.Clamp(_beepChance * ent.Comp.Interval, 0f, 1f)))
        {
            ent.Comp.Beeps++;
            _chat.TrySendInGameICMessage(ent.Owner, Loc.GetString("wolfmed-iv-beeps"), InGameICChatType.Emote,
                ChatTransmitRange.Normal, ignoreActionBlocker: true);
            _audio.PlayPvs(ent.Comp.BeepSound, ent.Owner);
        }

        var drawn = blood.SplitSolutionWithOnly(amount, bloodstream.BloodReagent.Id);
        _solutions.UpdateChemicals(bloodstream.BloodSolution!.Value);
        _solutions.TryAddSolution(soln.Value, drawn);
    }

    #endregion

    #region Attaching

    /// <summary>Starts putting the needle into <paramref name="patient"/>; false with a popup when it cannot.</summary>
    public bool TryStartAttach(Entity<WolfmedIvDripComponent> ent, EntityUid patient, EntityUid user)
    {
        if (!CanAttach(ent, patient, user))
            return false;

        _popup.PopupEntity(Loc.GetString("wolfmed-iv-attach-begin-user", ("drip", ent.Owner),
            ("target", Identity.Entity(patient, EntityManager))), user, user);
        _popup.PopupEntity(Loc.GetString("wolfmed-iv-attach-begin-others", ("user", Identity.Entity(user, EntityManager)),
            ("drip", ent.Owner), ("target", Identity.Entity(patient, EntityManager))), user, Filter.PvsExcept(user), true,
            PopupType.SmallCaution);

        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, _attachSeconds,
            new WolfmedIvAttachDoAfterEvent(), ent.Owner, target: patient, used: ent.Owner)
        {
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    private bool CanAttach(Entity<WolfmedIvDripComponent> ent, EntityUid patient, EntityUid user)
    {
        if (GetContainer(ent) == null)
        {
            _popup.PopupEntity(Loc.GetString("wolfmed-iv-no-container", ("drip", ent.Owner)), user, user);
            return false;
        }

        if (!HasComp<BloodstreamComponent>(patient) || _containers.IsEntityInContainer(patient))
        {
            _popup.PopupEntity(Loc.GetString("wolfmed-iv-cannot-inject",
                ("target", Identity.Entity(patient, EntityManager))), user, user);
            return false;
        }

        if (!InReach(ent, patient))
        {
            _popup.PopupEntity(Loc.GetString("wolfmed-iv-too-far", ("drip", ent.Owner),
                ("target", Identity.Entity(patient, EntityManager))), user, user);
            return false;
        }

        return true;
    }

    private void OnAttachDoAfter(Entity<WolfmedIvDripComponent> ent, ref WolfmedIvAttachDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } patient || !CanAttach(ent, patient, args.User))
            return;

        args.Handled = true;
        Attach(ent, patient, args.User);
    }

    /// <summary>The needle goes in: any other patient is detached first. Public so a test can skip the do-after.</summary>
    public void Attach(Entity<WolfmedIvDripComponent> ent, EntityUid patient, EntityUid? user = null)
    {
        if (ent.Comp.Patient is { } previous && previous != patient)
            Detach(ent);

        ent.Comp.Patient = patient;
        ent.Comp.NextUpdate = _timing.CurTime + TimeSpan.FromSeconds(MathF.Max(0.1f, ent.Comp.Interval));

        if (user is { } attacher)
        {
            _popup.PopupEntity(Loc.GetString("wolfmed-iv-attach-done-user", ("drip", ent.Owner),
                ("target", Identity.Entity(patient, EntityManager))), attacher, attacher);
            _popup.PopupEntity(Loc.GetString("wolfmed-iv-attach-done-others", ("user", Identity.Entity(attacher, EntityManager)),
                ("drip", ent.Owner), ("target", Identity.Entity(patient, EntityManager))), attacher,
                Filter.PvsExcept(attacher), true);
        }

        _adminLog.Add(LogType.ForceFeed, LogImpact.Medium,
            $"{ToPrettyString(user):user} attached {ToPrettyString(ent.Owner):drip} to {ToPrettyString(patient):target} in {ent.Comp.Mode} mode holding {ToPrettyString(GetContainer(ent))}");
        UpdateAppearance(ent);
    }

    /// <summary>Takes the needle out cleanly.</summary>
    public void Detach(Entity<WolfmedIvDripComponent> ent, bool announce = true)
    {
        if (ent.Comp.Patient is not { } patient)
            return;

        ent.Comp.Patient = null;
        if (announce && !TerminatingOrDeleted(patient))
        {
            _popup.PopupEntity(Loc.GetString("wolfmed-iv-detached", ("drip", ent.Owner),
                ("target", Identity.Entity(patient, EntityManager))), ent.Owner);
        }

        UpdateAppearance(ent);
    }

    private bool InReach(Entity<WolfmedIvDripComponent> ent, EntityUid patient)
    {
        var from = _xform.GetMapCoordinates(ent.Owner);
        var to = _xform.GetMapCoordinates(patient);
        return from.MapId == to.MapId && (from.Position - to.Position).Length() <= ent.Comp.Range;
    }

    private void OnDragDropped(Entity<WolfmedIvDripComponent> ent, ref DragDropDraggedEvent args)
    {
        if (args.Handled || !HasComp<BloodstreamComponent>(args.Target))
            return;

        args.Handled = true;
        TryStartAttach(ent, args.Target, args.User);
    }

    #endregion

    #region Container

    private void OnInteractUsing(Entity<WolfmedIvDripComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !Fits(ent, args.Used))
            return;

        args.Handled = true;
        if (GetContainer(ent) != null)
        {
            _popup.PopupEntity(Loc.GetString("wolfmed-iv-not-empty", ("drip", ent.Owner)), args.User, args.User);
            return;
        }

        if (!_containers.TryGetContainer(ent.Owner, WolfmedIvDripComponent.ContainerId, out var slot) ||
            !_hands.TryDropIntoContainer(args.User, args.Used, slot))
            return;

        _popup.PopupEntity(Loc.GetString("wolfmed-iv-loaded", ("drip", ent.Owner), ("container", args.Used)),
            args.User, args.User);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(args.User):user} hung {ToPrettyString(args.Used)} on {ToPrettyString(ent.Owner):drip}");
    }

    /// <summary>Whether an item may hang on the stand.</summary>
    public bool Fits(Entity<WolfmedIvDripComponent> ent, EntityUid item) =>
        _whitelist.IsWhitelistPassOrNull(ent.Comp.Whitelist, item);

    /// <summary>Takes the container down into the user's hands, detaching the patient first as tg's eject does.</summary>
    public void Eject(Entity<WolfmedIvDripComponent> ent, EntityUid? user)
    {
        if (GetContainer(ent) is not { } container ||
            !_containers.TryGetContainer(ent.Owner, WolfmedIvDripComponent.ContainerId, out var slot))
            return;

        Detach(ent);
        _containers.Remove(container, slot);
        if (user != null)
            _hands.PickupOrDrop(user, container);
    }

    private void OnContainerChanged(Entity<WolfmedIvDripComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != WolfmedIvDripComponent.ContainerId)
            return;

        UpdateAppearance(ent);
    }

    private void OnContainerRemoved(Entity<WolfmedIvDripComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != WolfmedIvDripComponent.ContainerId || TerminatingOrDeleted(ent))
            return;

        UpdateAppearance(ent);
    }

    /// <summary>Whatever hangs on the stand.</summary>
    public EntityUid? GetContainer(Entity<WolfmedIvDripComponent> ent)
    {
        return _containers.TryGetContainer(ent.Owner, WolfmedIvDripComponent.ContainerId, out var slot) &&
               slot.ContainedEntities.Count > 0 &&
               !TerminatingOrDeleted(slot.ContainedEntities[0])
            ? slot.ContainedEntities[0]
            : null;
    }

    private bool IsPack(Entity<WolfmedIvDripComponent> ent, EntityUid container) =>
        _tags.HasTag(container, ent.Comp.PackTag) && HasComp<StackComponent>(container);

    /// <summary>The solution a beaker, bottle or jug gives up and takes in.</summary>
    private bool TryGetSolution(EntityUid container,
        [NotNullWhen(true)] out Entity<SolutionComponent>? soln,
        [NotNullWhen(true)] out Solution? solution)
    {
        return _solutions.TryGetFitsInDispenser(container, out soln, out solution) ||
               _solutions.TryGetDrainableSolution(container, out soln, out solution);
    }

    #endregion

    #region Mode and rate

    /// <summary>Clamped to the CVar bounds and rounded to the step.</summary>
    public float ClampRate(float rate)
    {
        var clamped = Math.Clamp(rate, _rateMin, MathF.Max(_rateMin, _rateMax));
        return _rateStep > 0f ? MathF.Round(clamped / _rateStep) * _rateStep : clamped;
    }

    public void SetRate(Entity<WolfmedIvDripComponent> ent, float rate, EntityUid? user = null)
    {
        ent.Comp.Rate = ClampRate(rate);
        if (user is { } setter)
            _popup.PopupEntity(Loc.GetString("wolfmed-iv-rate-set", ("rate", ent.Comp.Rate)), setter, setter);
        UpdateAppearance(ent);
    }

    public void SetMode(Entity<WolfmedIvDripComponent> ent, WolfmedIvMode mode, EntityUid? user = null)
    {
        ent.Comp.Mode = mode;
        if (user is { } setter)
        {
            _popup.PopupEntity(Loc.GetString(mode == WolfmedIvMode.Inject ? "wolfmed-iv-mode-now-inject" : "wolfmed-iv-mode-now-take",
                ("drip", ent.Owner)), setter, setter);
            if (mode == WolfmedIvMode.Take && GetContainer(ent) is { } container && IsPack(ent, container))
                _popup.PopupEntity(Loc.GetString("wolfmed-iv-pack-no-refill"), setter, setter);
        }

        UpdateAppearance(ent);
    }

    private WolfmedIvMode Other(WolfmedIvMode mode) =>
        mode == WolfmedIvMode.Inject ? WolfmedIvMode.Take : WolfmedIvMode.Inject;

    #endregion

    #region Verbs

    /// <summary>
    /// tg's quick toggle, highest priority first so alt-click does what tg's right-click did: take the needle out, else
    /// take the container down, else switch the mode.
    /// </summary>
    private void OnAlternativeVerbs(Entity<WolfmedIvDripComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        if (ent.Comp.Patient != null)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wolfmed-iv-verb-detach"),
                Act = () => Detach(ent),
                Priority = 3,
            });
        }

        if (GetContainer(ent) is { } container)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wolfmed-iv-verb-eject", ("container", container)),
                Act = () => Eject(ent, user),
                Priority = 2,
            });
        }

        var next = Other(ent.Comp.Mode);
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(next == WolfmedIvMode.Take ? "wolfmed-iv-verb-mode-take" : "wolfmed-iv-verb-mode-inject"),
            Act = () => SetMode(ent, next, user),
            Priority = 1,
        });
    }

    /// <summary>The needle into each patient in reach, and the flow choices.</summary>
    private void OnVerbs(Entity<WolfmedIvDripComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        foreach (var candidate in PatientsInReach(ent))
        {
            if (candidate == ent.Comp.Patient)
                continue;

            var patient = candidate;
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("wolfmed-iv-verb-attach", ("target", Identity.Entity(patient, EntityManager))),
                Act = () => TryStartAttach(ent, patient, user),
                Priority = 2,
            });
        }

        var category = new VerbCategory("wolfmed-iv-verb-category-rate", null);
        var choices = new (string Key, float Rate)[]
        {
            ("wolfmed-iv-verb-rate-stop", _rateMin),
            ("wolfmed-iv-verb-rate-slow", _rateDefault / 2f),
            ("wolfmed-iv-verb-rate-normal", _rateDefault),
            ("wolfmed-iv-verb-rate-fast", _rateMax),
        };

        var priority = choices.Length;
        foreach (var (key, value) in choices)
        {
            var rate = ClampRate(value);
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString(key, ("rate", rate)),
                Category = category,
                Act = () => SetRate(ent, rate, user),
                Priority = priority--,
                Disabled = MathF.Abs(rate - ent.Comp.Rate) < 0.001f,
            });
        }
    }

    /// <summary>Bodies with blood in reach of the stand, on the floor.</summary>
    private IEnumerable<EntityUid> PatientsInReach(Entity<WolfmedIvDripComponent> ent)
    {
        var coords = Transform(ent.Owner).Coordinates;
        foreach (var found in _lookup.GetEntitiesInRange<BloodstreamComponent>(coords, ent.Comp.Range))
        {
            if (!_containers.IsEntityInContainer(found.Owner))
                yield return found.Owner;
        }
    }

    #endregion

    #region Examine and appearance

    private void OnExamined(Entity<WolfmedIvDripComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(WolfmedIvDripComponent)))
        {
            args.PushMarkup(Loc.GetString(ent.Comp.Mode == WolfmedIvMode.Inject
                ? "wolfmed-iv-examine-mode-inject"
                : "wolfmed-iv-examine-mode-take", ("rate", ent.Comp.Rate)));

            if (GetContainer(ent) is not { } container)
            {
                args.PushMarkup(ent.Comp.PackOpened > 0f
                    ? Loc.GetString("wolfmed-iv-examine-opened", ("units", MathF.Round(ent.Comp.PackOpened)))
                    : Loc.GetString("wolfmed-iv-examine-nothing"));
            }
            else if (IsPack(ent, container))
            {
                args.PushMarkup(Loc.GetString("wolfmed-iv-examine-packs",
                    ("count", _stacks.GetCount(container)),
                    ("units", MathF.Round(PackUnitsLeft(container, ent.Comp.PackOpened)))));
                if (ent.Comp.Mode == WolfmedIvMode.Take)
                    args.PushMarkup(Loc.GetString("wolfmed-iv-examine-pack-take"));
                else if (ent.Comp.Patient is { } treated && !PacksCanTreat(treated, container))
                    args.PushMarkup(Loc.GetString("wolfmed-iv-examine-pack-refused",
                        ("target", Identity.Entity(treated, EntityManager))));
            }
            else if (TryGetSolution(container, out _, out var solution) && solution.Volume > FixedPoint2.Zero)
            {
                args.PushMarkup(Loc.GetString("wolfmed-iv-examine-container", ("container", container),
                    ("units", solution.Volume)));
            }
            else
            {
                args.PushMarkup(Loc.GetString("wolfmed-iv-examine-container-empty", ("container", container)));
            }

            args.PushMarkup(ent.Comp.Patient is { } patient
                ? Loc.GetString("wolfmed-iv-examine-connected", ("target", Identity.Entity(patient, EntityManager)))
                : Loc.GetString("wolfmed-iv-examine-not-connected"));
        }
    }

    /// <summary>Mode, needle, flow, and the hung container with tg's fill overlay and the contents' colour.</summary>
    private void UpdateAppearance(Entity<WolfmedIvDripComponent> ent)
    {
        if (!TryComp(ent.Owner, out AppearanceComponent? appearance))
            return;

        var attached = ent.Comp.Patient != null;
        _appearance.SetData(ent.Owner, WolfmedIvDripVisuals.Mode, ent.Comp.Mode, appearance);
        _appearance.SetData(ent.Owner, WolfmedIvDripVisuals.Attached, attached, appearance);
        _appearance.SetData(ent.Owner, WolfmedIvDripVisuals.Flowing, attached && ent.Comp.Rate > 0f, appearance);

        var container = GetContainer(ent);
        // Playtest 5: an opened pack still hangs on the line after its stack is gone.
        var opened = container == null && ent.Comp.PackOpened > 0f;
        _appearance.SetData(ent.Owner, WolfmedIvDripVisuals.Container, container != null || opened, appearance);
        if (container is not { } hung && !opened)
            return;

        float fraction;
        Color colour;
        if (container == null || IsPack(ent, container.Value))
        {
            var max = (container is { } stack ? _stacks.GetMaxCount(stack) : 1) * _unitsPerPack;
            fraction = max > 0f ? PackUnitsLeft(container, ent.Comp.PackOpened) / max : 0f;
            var reagent = ent.Comp.Patient is { } patient && TryComp(patient, out BloodstreamComponent? stream)
                ? stream.BloodReagent
                : PackColourReagent;
            colour = _protos.TryIndex(reagent, out var proto) ? proto.SubstanceColor : Color.DarkRed;
        }
        else if (TryGetSolution(container.Value, out _, out var solution))
        {
            fraction = solution.MaxVolume > FixedPoint2.Zero ? (solution.Volume / solution.MaxVolume).Float() : 0f;
            colour = solution.GetColor(_protos);
        }
        else
        {
            fraction = 0f;
            colour = Color.White;
        }

        var percent = (int) MathF.Ceiling(100f * fraction);
        var threshold = 0;
        foreach (var step in FillThresholds)
        {
            if (percent >= step)
                threshold = step;
        }

        _appearance.SetData(ent.Owner, WolfmedIvDripVisuals.Fill, threshold, appearance);
        _appearance.SetData(ent.Owner, WolfmedIvDripVisuals.FillColor, colour, appearance);
    }

    #endregion
}
