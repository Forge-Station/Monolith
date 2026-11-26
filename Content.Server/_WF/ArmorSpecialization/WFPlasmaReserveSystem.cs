using Content.Server._WF.Wolfmed.Medical;
using Content.Server.Administration.Logs;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._WF.ArmorSpecialization;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.ArmorSpecialization;

/// <summary>
/// Runs a worn <see cref="WFPlasmaReserveComponent"/>: feeds the patient on its line and refills from blood packs used on
/// the suit. It never feeds the wearer: a medic suit treats others, it doesn't keep its own wearer alive.
/// </summary>
public sealed class WFPlasmaReserveSystem : EntitySystem
{
    private static readonly ProtoId<TagPrototype> PackTag = "Bloodpack";
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedStackSystem _stacks = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private WolfmedIvDripSystem _ivDrip = default!;
    [Dependency] private WolfmedIvLineSystem _line = default!;
    [Dependency] private WFArmorSpecializationSystem _specs = default!;

    private TimeSpan _nextTick;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPlasmaReserveComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WFPlasmaReserveComponent, WFPlasmaLineDoAfterEvent>(OnLineDoAfter);
        SubscribeLocalEvent<WFPlasmaReserveComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<BloodstreamComponent, GetVerbsEvent<AlternativeVerb>>(OnPatientVerbs);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextTick)
            return;
        _nextTick = now + TickInterval;

        var seconds = (float) TickInterval.TotalSeconds;
        var query = EntityQueryEnumerator<WFPlasmaReserveComponent>();
        while (query.MoveNext(out var uid, out var reserve))
        {
            var ent = (uid, reserve);
            if (_specs.GetWearer(uid) is not { } wearer)
            {
                Disconnect(ent);
                continue;
            }

            if (reserve.Patient is not { } patient || !LineHolds(ent, wearer, patient))
                continue;

            Give(ent, patient, reserve.LineRate * seconds);
            if (reserve.Reserve > 0.005f)
                continue;

            ClearLine(ent);
            _popup.PopupEntity(Loc.GetString("wf-armor-spec-plasma-ran-dry"), wearer, wearer, PopupType.SmallCaution);
        }
    }

    /// <summary>False, with the line pulled free, when the patient is gone, shut in something or out of reach.</summary>
    private bool LineHolds(Entity<WFPlasmaReserveComponent> ent, EntityUid wearer, EntityUid patient)
    {
        if (TerminatingOrDeleted(patient))
        {
            ClearLine(ent);
            return false;
        }

        if (!_containers.IsEntityInContainer(patient) && InReach(ent, wearer, patient))
            return true;

        _popup.PopupEntity(Loc.GetString("wf-armor-spec-plasma-line-pulled",
            ("target", Identity.Entity(patient, EntityManager))), patient, PopupType.SmallCaution);
        ClearLine(ent);
        return false;
    }

    private bool InReach(Entity<WFPlasmaReserveComponent> ent, EntityUid wearer, EntityUid patient)
    {
        var from = _xform.GetMapCoordinates(wearer);
        var to = _xform.GetMapCoordinates(patient);
        return from.MapId == to.MapId && (from.Position - to.Position).Length() <= ent.Comp.Range;
    }

    /// <summary>
    /// Gives up to <paramref name="units"/> of the body's own blood out of the reserve; the plasma is universal, so
    /// any body blood packs work on takes it. Never the wearer. Returns the units given. Public so a test can skip the
    /// clock.
    /// </summary>
    public float Give(Entity<WFPlasmaReserveComponent> ent, EntityUid body, float units)
    {
        if (ent.Comp.Reserve <= 0f || _specs.GetWearer(ent.Owner) == body || !_ivDrip.PacksCanTreat(body) ||
            !TryComp(body, out BloodstreamComponent? bloodstream) ||
            !_solutions.ResolveSolution(body, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var blood))
            return 0f;

        var amount = MathF.Min(MathF.Min(units, ent.Comp.Reserve), (blood.MaxVolume - blood.Volume).Float());
        if (amount <= 0.005f || !_bloodstream.TryModifyBloodLevel(body, FixedPoint2.New(amount), bloodstream))
            return 0f;

        ent.Comp.Reserve = MathF.Max(ent.Comp.Reserve - amount, 0f);
        Dirty(ent);
        return amount;
    }

    #region Line

    private void OnPatientVerbs(Entity<BloodstreamComponent> patient, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.User == args.Target ||
            !_inventory.TryGetSlotEntity(args.User, "outerClothing", out var suit) ||
            !TryComp(suit, out WFPlasmaReserveComponent? reserve) ||
            !_ivDrip.PacksCanTreat(patient))
            return;

        var ent = (suit.Value, reserve);
        var user = args.User;
        var target = args.Target;
        if (reserve.Patient == target)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wf-armor-spec-plasma-verb-disconnect"),
                Act = () => Disconnect(ent),
            });
            return;
        }

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wf-armor-spec-plasma-verb-connect"),
            Act = () => TryStartConnect(ent, target, user),
        });
    }

    /// <summary>Starts running the suit's line into <paramref name="patient"/>; false with a popup when it cannot.</summary>
    public bool TryStartConnect(Entity<WFPlasmaReserveComponent> ent, EntityUid patient, EntityUid user)
    {
        if (!CanConnect(ent, patient, user))
            return false;

        _popup.PopupEntity(Loc.GetString("wf-armor-spec-plasma-connect-begin-others",
            ("user", Identity.Entity(user, EntityManager)), ("target", Identity.Entity(patient, EntityManager))),
            user, Filter.PvsExcept(user), true, PopupType.SmallCaution);

        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, ent.Comp.ConnectTime,
            new WFPlasmaLineDoAfterEvent(), ent.Owner, target: patient, used: ent.Owner)
        {
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    private bool CanConnect(Entity<WFPlasmaReserveComponent> ent, EntityUid patient, EntityUid user)
    {
        if (_specs.GetWearer(ent.Owner) != user || patient == user || _containers.IsEntityInContainer(patient) ||
            !InReach(ent, user, patient))
        {
            _popup.PopupEntity(Loc.GetString("wf-armor-spec-plasma-cannot",
                ("target", Identity.Entity(patient, EntityManager))), user, user);
            return false;
        }

        if (ent.Comp.Reserve <= 0f)
        {
            _popup.PopupEntity(Loc.GetString("wf-armor-spec-plasma-empty"), user, user);
            return false;
        }

        return true;
    }

    private void OnLineDoAfter(Entity<WFPlasmaReserveComponent> ent, ref WFPlasmaLineDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } patient || !CanConnect(ent, patient, args.User))
            return;

        args.Handled = true;
        Connect(ent, patient, args.User);
    }

    /// <summary>The line goes in; any other patient is disconnected first. Public so a test can skip the do-after.</summary>
    public void Connect(Entity<WFPlasmaReserveComponent> ent, EntityUid patient, EntityUid user)
    {
        if (ent.Comp.Patient is { } previous && previous != patient)
            Disconnect(ent);

        ent.Comp.Patient = patient;
        _line.Show(ent.Owner, patient);
        _popup.PopupEntity(Loc.GetString("wf-armor-spec-plasma-connected",
            ("target", Identity.Entity(patient, EntityManager))), user, user);
        _adminLog.Add(LogType.ForceFeed, LogImpact.Low,
            $"{ToPrettyString(user):user} ran the plasma line of {ToPrettyString(ent.Owner):suit} into {ToPrettyString(patient):target}");
    }

    /// <summary>Takes the line out cleanly.</summary>
    public void Disconnect(Entity<WFPlasmaReserveComponent> ent)
    {
        if (ent.Comp.Patient is not { } patient)
            return;

        ClearLine(ent);
        if (!TerminatingOrDeleted(patient))
            _popup.PopupEntity(Loc.GetString("wf-armor-spec-plasma-disconnected",
                ("target", Identity.Entity(patient, EntityManager))), patient);
    }

    /// <summary>Drops the patient and the visible line, with no popup.</summary>
    private void ClearLine(Entity<WFPlasmaReserveComponent> ent)
    {
        if (ent.Comp.Patient is not { } patient)
            return;

        ent.Comp.Patient = null;
        _line.Hide(ent.Owner, patient);
    }

    #endregion

    /// <summary>A blood pack used on the suit opens into the reserve, one pack a use.</summary>
    private void OnInteractUsing(Entity<WFPlasmaReserveComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_tags.HasTag(args.Used, PackTag) || !TryComp(args.Used, out StackComponent? stack))
            return;

        args.Handled = true;
        if (ent.Comp.Reserve >= ent.Comp.MaxReserve - 0.005f)
        {
            _popup.PopupEntity(Loc.GetString("wf-armor-spec-plasma-full"), args.User, args.User);
            return;
        }

        if (!_stacks.Use(args.Used, 1, stack))
            return;

        ent.Comp.Reserve = MathF.Min(ent.Comp.Reserve + _ivDrip.UnitsPerPack, ent.Comp.MaxReserve);
        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("wf-armor-spec-plasma-refilled",
            ("units", (int) MathF.Round(ent.Comp.Reserve)), ("max", (int) ent.Comp.MaxReserve)), args.User, args.User);
    }

    private void OnExamined(Entity<WFPlasmaReserveComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-armor-spec-plasma-examine",
            ("units", (int) MathF.Round(ent.Comp.Reserve)), ("max", (int) ent.Comp.MaxReserve)));
    }
}
