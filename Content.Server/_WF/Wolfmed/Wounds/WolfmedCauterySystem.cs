using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Targeting;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Systems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Content.Shared.Temperature;
using Content.Shared.Verbs;
using Content.Shared._WF.Wolfmed.CCVar;
using Robust.Server.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>
/// Burning a bleed shut. Heat that lands on a part seals whatever is bleeding there and charges a burn for
/// it, which is why a laser fight leaves fewer bleeders than a knife fight. A deliberate cautery - holding
/// something hot against the wound on purpose - seals what incidental heat cannot, and costs more.
/// </summary>
/// <remarks>
/// Server-side: <see cref="WoundBleedingSystem"/> is. The incidental path answers
/// <see cref="WolfmedPartDamageEvent"/>, W4's broadcast seam; the deliberate one is a
/// <see cref="UtilityVerb"/> because the interaction events on <c>WoundHostComponent</c> are all taken
/// (welder repair, embedded removal) and a verb also reads as intent.
/// </remarks>
public sealed class WolfmedCauterySystem : EntitySystem
{
    /// <summary>The shipped profile. Retuning searing is an edit to that prototype, not to this file.</summary>
    public const string DefaultProfile = "WFWolfmedCauteryDefault";

    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private WoundBleedingSystem _bleeding = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private WoundTargetResolver _targeting = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedPartDamageEvent>(OnPartDamage);
        SubscribeLocalEvent<WoundHostComponent, GetVerbsEvent<UtilityVerb>>(OnGetVerbs);
        SubscribeLocalEvent<WoundHostComponent, WolfmedCauteryDoAfterEvent>(OnDoAfter);
    }

    /// <summary>
    /// Whether the body may be told "You feel your wounds painfully close!" now (playtest 1). Heat that trims a
    /// bleed says so at most once per <see cref="WolfmedCVars.CauteryPopupSeconds"/>: vacuum's barotrauma deals
    /// a little Heat every second, and on a bleeding body each tick used to print the line again.
    /// </summary>
    public bool TryAnnounceWoundsClosing(EntityUid body)
    {
        var announce = EnsureComp<WolfmedCauteryAnnounceComponent>(body);
        var now = _timing.CurTime;
        if (now < announce.Next)
            return false;

        announce.Next = now + TimeSpan.FromSeconds(MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.CauteryPopupSeconds)));
        announce.Count++;
        return true;
    }

    private void OnPartDamage(ref WolfmedPartDamageEvent args)
    {
        if (args.DamageType != "Heat" || args.Amount < GetProfile().MinIncidentalHeat)
            return;

        TryCauterize(args.Part, deliberate: false);
    }

    private void OnGetVerbs(Entity<WoundHostComponent> body, ref GetVerbsEvent<UtilityVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || args.Using is not { } tool || !IsHot(tool))
            return;

        if (!TryComp(args.User, out TargetingComponent? targeting) ||
            !_targeting.TryResolveExact(body, targeting.Target, out var part) ||
            !HasSealableBleed(part))
            return;

        var user = args.User;
        args.Verbs.Add(new UtilityVerb
        {
            Text = Loc.GetString("wolfmed-cauterize-verb"),
            Act = () => TryStart(body, user, tool, part),
        });
    }

    private void TryStart(Entity<WoundHostComponent> body, EntityUid user, EntityUid tool, EntityUid part)
    {
        var profile = GetProfile();
        var self = body.Owner == user;
        var delay = self ? profile.DeliberateDelay * profile.SelfMultiplier : profile.DeliberateDelay;

        _popup.PopupEntity(Loc.GetString(self ? "wolfmed-cauterize-start-self" : "wolfmed-cauterize-start",
            ("user", user), ("target", body.Owner), ("tool", tool)), body, user);
        _audio.PlayPvs(profile.DeliberateBeginSound, body);

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
            user,
            delay,
            new WolfmedCauteryDoAfterEvent(GetNetEntity(part)),
            body,
            body,
            tool)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
        });
    }

    private void OnDoAfter(Entity<WoundHostComponent> body, ref WolfmedCauteryDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Used is not { } tool || !IsHot(tool) ||
            !TryGetEntity(args.Part, out var part))
            return;

        // A limb lost during the do-after would otherwise be sealed and burned on the floor.
        var sealedWounds = _body.BodyHasChild(body, part.Value) ? TryCauterize(part.Value, deliberate: true) : 0;
        args.Handled = sealedWounds > 0;
        if (sealedWounds > 0)
            _audio.PlayPvs(GetProfile().DeliberateEndSound, body);

        _popup.PopupEntity(Loc.GetString(sealedWounds > 0
                ? "wolfmed-cauterize-success"
                : "wolfmed-cauterize-nothing",
            ("target", body.Owner)), body, args.User);
    }

    /// <summary>
    /// Sears every bleed on the part that this kind of heat can reach and charges one burn for the lot.
    /// Returns how many were sealed. Public so a test or a future tool can skip the do-after.
    /// </summary>
    public int TryCauterize(EntityUid part, bool deliberate)
    {
        if (TerminatingOrDeleted(part) || !TryComp(part, out WoundableComponent? woundable))
            return 0;

        var profile = GetProfile();
        var count = 0;
        foreach (var wound in _wounds.GetWounds((part, woundable)).ToArray())
        {
            if (wound.Comp.State != WoundState.Open ||
                !TryComp(wound, out WoundBleedingComponent? bleeding) ||
                bleeding.CurrentRate <= 0f ||
                bleeding.Treatment == BleedingTreatment.Cauterized ||
                !CanSeal(wound, deliberate))
                continue;

            if (_bleeding.SetTreatment(wound.Owner, BleedingTreatment.Cauterized))
                count++;
        }

        if (count == 0)
            return 0;

        var burn = deliberate ? profile.DeliberateBurn : profile.IncidentalBurn;
        if (profile.BurnWound is { } prototype && burn > FixedPoint2.Zero)
            _wounds.CreateOrMergeWound(part, prototype, burn * count);

        if (deliberate && profile.DeliberatePain > FixedPoint2.Zero)
            _pain.ChangePain(part, profile.DeliberatePain);

        return count;
    }

    /// <summary>Whether a deliberate cautery on this part would achieve anything.</summary>
    public bool HasSealableBleed(EntityUid part)
    {
        if (!TryComp(part, out WoundableComponent? woundable))
            return false;

        foreach (var wound in _wounds.GetWounds((part, woundable)))
        {
            if (wound.Comp.State == WoundState.Open &&
                TryComp(wound, out WoundBleedingComponent? bleeding) &&
                bleeding.CurrentRate > 0f &&
                bleeding.Treatment != BleedingTreatment.Cauterized)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether this heat can close this wound. A wound that declares no resistance is sealed by anything;
    /// an arterial bleed past its threshold needs someone holding the tool there on purpose.
    /// </summary>
    private bool CanSeal(Entity<WoundComponent> wound, bool deliberate)
    {
        if (!_traits.TryGetBehavior(wound.AsNullable(), out WolfmedCauteryResistBehavior behavior))
            return true;

        return deliberate || !behavior.DeliberateOnly && wound.Comp.Severity <= behavior.MaxIncidentalSeverity;
    }

    private bool IsHot(EntityUid tool)
    {
        var hot = new IsHotEvent();
        RaiseLocalEvent(tool, hot);
        return hot.IsHot;
    }

    private WolfmedCauteryProfilePrototype GetProfile() =>
        _prototypes.Index<WolfmedCauteryProfilePrototype>(DefaultProfile);
}
