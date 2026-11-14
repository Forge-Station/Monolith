using System.Linq;
using Content.Shared.FixedPoint;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Targeting;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>Tissue death from a forgotten tourniquet, a deep burn or freeze, or a limb reattached late.</summary>
// All three accumulate on WolfmedNecrosisComponent on the part and end the same way: a WFWolfmedNecrosisWound that
// nothing treats, a limb that no longer works properly and a standing source of infection (INFECTION: the part is
// pinned at the top and infects the part it hangs off, towards the torso), until the part is amputated and replaced.
/// <remarks>
/// The patient gets one warning popup before it happens, and the analyzer flags the part from the moment
/// the clock starts. <see cref="Update"/> advances by whatever time has accumulated so a test can hand it
/// the whole ten minutes at once.
/// </remarks>
public sealed class WolfmedNecrosisSystem : EntitySystem
{
    private const float TickSeconds = 5f;

    [Dependency] private Content.Shared._WF.Wolfmed.Examine.WolfmedVisualInspectionSystem _look = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private Damage.WolfmedDegradationVisualsSystem _degradation = default!;
    [Dependency] private WolfmedInfectionSystem _infection = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private WoundBleedingSystem _bleeding = default!;
    [Dependency] private WoundSystem _wounds = default!;

    private float _accumulator;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
        SubscribeLocalEvent<WoundHostComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        if (TerminatingOrDeleted(args.Part))
            return;

        // M6 (P30): the wound's risk multiplier shortens its onset; it used to be read only as "is there a risk".
        var onset = _traits.GetPartNecrosisOnset(args.Part);
        if (onset > TimeSpan.Zero)
            Start(args.Part, WolfmedNecrosisSource.Wound, onset);
        else
            ClearSource(args.Part, WolfmedNecrosisSource.Wound);
    }

    private void OnGetVerbs(Entity<WoundHostComponent> body, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        // One entry per tourniquet, each saying whether the bleed under it has stopped. A single verb picked a
        // limb for the medic and gave no way to tell a tourniquet that was done from one still holding an artery.
        var user = args.User;
        var priority = 0;
        foreach (var (part, component) in _body.GetBodyChildren(body))
        {
            if (!HasComp<WolfmedTourniquetComponent>(part))
                continue;

            var target = part;
            var holding = IsHoldingABleed(part);
            args.Verbs.Add(new Verb
            {
                Category = LoosenCategory,
                Text = Loc.GetString(holding ? "wolfmed-tourniquet-loosen-part-holding" : "wolfmed-tourniquet-loosen-part-safe",
                    ("part", _look.PartName(part, component))),
                Message = Loc.GetString(holding ? "wolfmed-tourniquet-loosen-holding-tip" : "wolfmed-tourniquet-loosen-safe-tip"),
                Priority = priority--,
                Act = () => Loosen(body, target, user),
            });
        }
    }

    private static readonly VerbCategory LoosenCategory = new("wolfmed-tourniquet-loosen-verb", null);

    /// <summary>Whether taking this tourniquet off would let a wound bleed again.</summary>
    public bool IsHoldingABleed(EntityUid part)
    {
        foreach (var tied in TiedParts(part))
        {
            foreach (var wound in _wounds.GetWounds(tied))
            {
                if (TryComp(wound, out WoundBleedingComponent? bleeding) &&
                    bleeding.Treatment == BleedingTreatment.Clamped &&
                    bleeding.BleedingSeverity > FixedPoint2.Zero)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The parts a strap on this part holds: the part and the parts hanging off it (a leg's foot), except one that
    /// carries a strap of its own. The same set TourniquetSystem.Apply clamps.
    /// </summary>
    private List<EntityUid> TiedParts(EntityUid part)
    {
        var parts = new List<EntityUid> { part };
        foreach (var child in _body.GetBodyPartChildren(part))
        {
            if (child.Id != part && !HasComp<WolfmedTourniquetComponent>(child.Id))
                parts.Add(child.Id);
        }

        return parts;
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < TickSeconds)
            return;

        var elapsed = _accumulator;
        _accumulator = 0f;

        if (!_config.GetCVar(WolfmedCVars.NecrosisEnabled))
            return;

        var profile = _infection.Profile;
        var seconds = elapsed * _config.GetCVar(WolfmedCVars.NecrosisRate);

        // A tourniquet stays a tourniquet only while something under it is still clamped shut; sutures or
        // surgery on the wound underneath take the clock off with them.
        // Buffered: both loops add and remove the components they walk.
        var clamped = new List<EntityUid>();
        var tourniquets = EntityQueryEnumerator<WolfmedTourniquetComponent>();
        while (tourniquets.MoveNext(out var tourniquetUid, out _))
            clamped.Add(tourniquetUid);

        foreach (var uid in clamped)
        {
            if (TerminatingOrDeleted(uid) || !HasComp<WolfmedTourniquetComponent>(uid))
                continue;

            if (!IsClamped(uid))
            {
                RemComp<WolfmedTourniquetComponent>(uid);
                ClearSource(uid, WolfmedNecrosisSource.Tourniquet);
                continue;
            }

            Start(uid, WolfmedNecrosisSource.Tourniquet, profile.TourniquetOnset);
        }

        var dyingParts = new List<EntityUid>();
        var dying = EntityQueryEnumerator<WolfmedNecrosisComponent>();
        while (dying.MoveNext(out var dyingUid, out _))
            dyingParts.Add(dyingUid);

        foreach (var uid in dyingParts)
        {
            if (TerminatingOrDeleted(uid) || !TryComp(uid, out WolfmedNecrosisComponent? necrosis))
                continue;

            if (necrosis.Necrotic || necrosis.Onset <= TimeSpan.Zero)
                continue;

            necrosis.Progress += seconds;
            Dirty(uid, necrosis);

            var onset = (float) necrosis.Onset.TotalSeconds;
            if (necrosis.Progress >= onset)
            {
                MakeNecrotic(uid);
                continue;
            }

            if (necrosis.Warned || necrosis.Progress < onset * profile.WarnFraction)
                continue;

            necrosis.Warned = true;
            if (CompOrNull<BodyPartComponent>(uid)?.Body is { } host)
                _popup.PopupEntity(Loc.GetString("wolfmed-necrosis-warning"), host, host, PopupType.MediumCaution);
        }
    }

    /// <summary>
    /// Puts the part on the clock, or shortens the clock if this source is faster. Public so the part
    /// lifecycle and a test can start it without a wound.
    /// </summary>
    public void Start(EntityUid part, WolfmedNecrosisSource source, TimeSpan onset)
    {
        // WOLFGATE (W6): a chassis has no tissue to lose, so no source starts the clock on one - not a
        // tourniquet, not a deep freeze, not a limb left on the floor. Gated here and in MakeNecrotic so
        // every entry point is covered.
        if (TerminatingOrDeleted(part) || onset <= TimeSpan.Zero || !HasComp<WoundableComponent>(part) ||
            !_traits.IsOrganic(part))
            return;

        var necrosis = EnsureComp<WolfmedNecrosisComponent>(part);
        if (necrosis.Necrotic || necrosis.Onset > TimeSpan.Zero && necrosis.Onset <= onset)
            return;

        necrosis.Source = source;
        necrosis.Onset = onset;
        Dirty(part, necrosis);
    }

    /// <summary>Takes one source off the necrosis clock, keeping the progress if the other still applies.</summary>
    // Source and its progress are a single slot; otherwise treating a frostbite would reset a tourniquet's ten minutes
    // to zero and the strap could be left on forever.
    private void ClearSource(EntityUid part, WolfmedNecrosisSource source)
    {
        if (!TryComp(part, out WolfmedNecrosisComponent? necrosis) || necrosis.Necrotic ||
            necrosis.Source != source)
            return;

        if (source != WolfmedNecrosisSource.Tourniquet && HasComp<WolfmedTourniquetComponent>(part))
        {
            necrosis.Source = WolfmedNecrosisSource.Tourniquet;
            necrosis.Onset = _infection.Profile.TourniquetOnset;
            Dirty(part, necrosis);
            return;
        }

        if (source != WolfmedNecrosisSource.Wound &&
            _traits.GetPartNecrosisOnset(part) is var onset && onset > TimeSpan.Zero)
        {
            necrosis.Source = WolfmedNecrosisSource.Wound;
            necrosis.Onset = onset;
            Dirty(part, necrosis);
            return;
        }

        RemComp<WolfmedNecrosisComponent>(part);
    }

    /// <summary>
    /// Kills the part: a wound nothing treats, contaminated from the start. Returns the wound,
    /// or null when the part cannot carry one. Public so a test and the reattachment path can skip the wait.
    /// </summary>
    public EntityUid? MakeNecrotic(EntityUid part)
    {
        if (TerminatingOrDeleted(part) || !HasComp<WoundableComponent>(part) || !_traits.IsOrganic(part))
            return null;

        var necrosis = EnsureComp<WolfmedNecrosisComponent>(part);
        if (necrosis.Necrotic)
            return null;

        necrosis.Necrotic = true;
        necrosis.Progress = 0f;
        necrosis.Onset = TimeSpan.Zero;
        Dirty(part, necrosis);

        // The tourniquet has nothing left to save; take the clock off so it does not keep ticking.
        RemComp<WolfmedTourniquetComponent>(part);

        // V3: dead tissue shows whether or not the part can carry the wound below.
        _degradation.Refresh(CompOrNull<BodyPartComponent>(part)?.Body ?? part);

        var profile = _infection.Profile;
        if (!_wounds.CanCreateWound(part, profile.NecrosisWound))
            return null;

        var wound = _wounds.CreateOrMergeWound(part, profile.NecrosisWound, profile.NecrosisSeverity);
        if (wound is { } created)
            _infection.Contaminate(created);

        if (CompOrNull<BodyPartComponent>(part)?.Body is { } body)
            _popup.PopupEntity(Loc.GetString("wolfmed-necrosis-dead"), body, body, PopupType.LargeCaution);

        return wound;
    }

    /// <summary>
    /// Takes a tourniquet off: the bleeding it was holding starts again, and the limb stops dying. The
    /// trade the verb exists for.
    /// </summary>
    public bool Loosen(EntityUid body, EntityUid part, EntityUid user)
    {
        if (!TryComp(part, out WolfmedTourniquetComponent? tourniquet))
            return false;

        var holding = IsHoldingABleed(part);
        Release(part);

        _audio.PlayPvs(tourniquet.LoosenSound, body);
        _popup.PopupEntity(Loc.GetString(holding ? "wolfmed-tourniquet-loosened" : "wolfmed-tourniquet-loosened-safe"), body, user);
        return true;
    }

    /// <summary>
    /// A makeshift strap knocked loose by a hit (<see cref="WolfmedTourniquetSlipSystem"/>): the same release as
    /// <see cref="Loosen"/>, with nobody's hand on it, so everyone nearby is told.
    /// </summary>
    public bool Slip(EntityUid body, EntityUid part)
    {
        if (!TryComp(part, out WolfmedTourniquetComponent? tourniquet))
            return false;

        Release(part);

        _audio.PlayPvs(tourniquet.LoosenSound, body);
        var name = TryComp(part, out BodyPartComponent? component) ? _look.PartName(part, component) : Name(part);
        _popup.PopupEntity(Loc.GetString("wolfmed-tourniquet-slipped", ("part", name)), body, PopupType.MediumCaution);
        return true;
    }

    /// <summary>Takes the strap's record off the part and unclamps every bleed it was holding, the foot's under a leg's too.</summary>
    private void Release(EntityUid part)
    {
        foreach (var tied in TiedParts(part))
        {
            foreach (var wound in _wounds.GetWounds(tied).ToArray())
            {
                if (TryComp(wound, out WoundBleedingComponent? bleeding) &&
                    bleeding.Treatment == BleedingTreatment.Clamped)
                    _bleeding.SetTreatment(wound.Owner, BleedingTreatment.None);
            }
        }

        RemComp<WolfmedTourniquetComponent>(part);
        ClearSource(part, WolfmedNecrosisSource.Tourniquet);
    }

    /// <summary>Stamps the time a limb came off, for the reattachment grace period.</summary>
    public void OnDetached(EntityUid part)
    {
        // WOLFGATE (W6): a chassis limb keeps indefinitely, so it is not even stamped.
        if (TerminatingOrDeleted(part) || !HasComp<WoundableComponent>(part) || !_traits.IsOrganic(part))
            return;

        var necrosis = EnsureComp<WolfmedNecrosisComponent>(part);
        necrosis.DetachedAt = _timing.CurTime;
    }

    /// <summary>
    /// Checks a reattached limb against that stamp. A limb put back within the grace period is fine; one
    /// left on the floor too long is dead tissue the moment it goes back on.
    /// </summary>
    public bool OnAttached(EntityUid part)
    {
        if (TerminatingOrDeleted(part) || !TryComp(part, out WolfmedNecrosisComponent? necrosis) ||
            necrosis.DetachedAt is not { } detached)
            return false;

        necrosis.DetachedAt = null;
        if (necrosis.Necrotic || !_config.GetCVar(WolfmedCVars.NecrosisEnabled) ||
            _timing.CurTime - detached <= _infection.Profile.ReattachGrace)
            return false;

        necrosis.Source = WolfmedNecrosisSource.Reattachment;
        Dirty(part, necrosis);
        MakeNecrotic(part);
        return true;
    }

    /// <summary>Whether the part, or a part its strap ties off, still has a bleed a tourniquet is holding shut.</summary>
    private bool IsClamped(EntityUid part)
    {
        foreach (var tied in TiedParts(part))
        {
            foreach (var wound in _wounds.GetWounds(tied))
            {
                if (TryComp(wound, out WoundBleedingComponent? bleeding) &&
                    bleeding.Treatment == BleedingTreatment.Clamped)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Whether this part is dead tissue. The analyzer's flag.</summary>
    public bool IsNecrotic(EntityUid part) => CompOrNull<WolfmedNecrosisComponent>(part)?.Necrotic == true;

    /// <summary>Whether this part is on a necrosis clock that has not run out yet.</summary>
    public bool IsAtRisk(EntityUid part) =>
        CompOrNull<WolfmedNecrosisComponent>(part) is { Necrotic: false, Onset.Ticks: > 0 };

    /// <summary>Marks a part as being under a tourniquet. The one call site is TourniquetSystem.Apply.</summary>
    public void OnTourniquetApplied(EntityUid part)
    {
        // WOLFGATE (W6): a tourniquet on a cybernetic limb is bleeding control and nothing more.
        if (!TerminatingOrDeleted(part) && HasComp<WoundableComponent>(part) && _traits.IsOrganic(part))
            EnsureComp<WolfmedTourniquetComponent>(part);
    }
}
