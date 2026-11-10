using Content.Server._WF.Remade.Conversion;
using Content.Server.Bed.Components;
using Content.Server.Power.EntitySystems;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Limbs;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Elites;

/// <summary>
/// Gets the hunter and the Undying back up after they die. Burns, a head or too many limbs lost, or dying again too soon
/// keep a hunter down; the Undying always comes back. Rules from DS13-1.0's hunter false death and ubermorph.
/// </summary>
public sealed partial class WFRisesSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WFEliteSystem _elite = default!;
    [Dependency] private WFRemadeDirectorSystem _director = default!;
    [Dependency] private WFRemadeLimbsSystem _limbs = default!;

    private readonly HashSet<Entity<StasisBedComponent>> _beds = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFRisesComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<WFRisesComponent, WFRemadeLimbSeveredEvent>(OnSevered);
    }

    private void OnMobState(Entity<WFRisesComponent> ent, ref MobStateChangedEvent args)
    {
        var comp = ent.Comp;
        if (args.NewMobState != MobState.Dead)
        {
            comp.RiseAt = null;
            return;
        }

        var now = _timing.CurTime;
        comp.HeatAtFall = _elite.Taken(ent, "Heat");
        comp.Held = false;
        comp.Final = IsFinal(ent, now);
        if (comp.Final)
        {
            comp.RiseAt = null;
            _popup.PopupEntity(Loc.GetString("wf-remade-elite-down-for-good", ("mob", ent.Owner)), ent, PopupType.Medium);
            return;
        }

        comp.RiseAt = now + TimeSpan.FromSeconds(comp.RiseDelay);
        _popup.PopupEntity(Loc.GetString("wf-remade-elite-lies-still", ("mob", ent.Owner)), ent, PopupType.Medium);
    }

    /// <summary>An Undying that has lost every limb folds up, to form again.</summary>
    private void OnSevered(Entity<WFRisesComponent> ent, ref WFRemadeLimbSeveredEvent args)
    {
        var comp = ent.Comp;
        if (comp.CollapseSevered <= 0
            || _mobState.IsDead(ent)
            || !TryComp<WFRemadeLimbsComponent>(ent, out var limbs)
            || limbs.Severed.Count < comp.CollapseSevered)
        {
            return;
        }

        _popup.PopupEntity(Loc.GetString("wf-remade-elite-collapses", ("mob", ent.Owner)), ent, PopupType.MediumCaution);
        _elite.Fell(ent);
    }

    /// <summary>Whether this death is the last one.</summary>
    private bool IsFinal(Entity<WFRisesComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        if (SourceLost(ent))
            return true;

        if (comp.BurnKill > 0f
            && _thresholds.TryGetDeadThreshold(ent, out var dead)
            && _elite.Taken(ent, "Heat") >= dead.Value.Float() * comp.BurnKill)
        {
            return true;
        }

        if (TryComp<WFRemadeLimbsComponent>(ent, out var limbs))
        {
            if (comp.HeadKills && limbs.Severed.Contains("head"))
                return true;

            if (comp.SeveredKill > 0 && limbs.Severed.Count >= comp.SeveredKill)
                return true;
        }

        return comp.Cooldown > 0f
               && comp.LastRose is { } last
               && (now - last).TotalSeconds < comp.Cooldown;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFRisesComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.RiseAt is not { } at)
                continue;

            // Its Monolith going, or fire taking it while it lies there, ends it for good.
            var reason = SourceLost(uid) ? "wf-remade-elite-down-for-good" : BurntOut((uid, comp)) ? "wf-remade-elite-burns-away" : null;
            if (reason != null)
            {
                comp.Final = true;
                comp.RiseAt = null;
                _popup.PopupEntity(Loc.GetString(reason, ("mob", uid)), uid, PopupType.MediumCaution);
                continue;
            }

            if (now < at)
                continue;

            var held = OnStasisBed((uid, comp));
            if (held != comp.Held)
            {
                comp.Held = held;
                if (held)
                    _popup.PopupEntity(Loc.GetString("wf-remade-elite-held-down", ("mob", uid)), uid, PopupType.Medium);
            }

            if (held)
                continue;

            var attempt = new WFRiseAttemptEvent();
            RaiseLocalEvent(uid, ref attempt);
            if (!attempt.Cancelled)
                Rise((uid, comp), now);
        }
    }

    /// <summary>Whether the Monolith it answers to is gone. A Remade with no Monolith to begin with never loses it.</summary>
    private bool SourceLost(EntityUid uid)
    {
        return TryComp<WFRemadeComponent>(uid, out var remade) && remade.Source is { } source && TerminatingOrDeleted(source);
    }

    /// <summary>Whether it has taken enough heat damage since it fell to be past getting up.</summary>
    private bool BurntOut(Entity<WFRisesComponent> ent)
    {
        return ent.Comp.BurnDown > 0f && _elite.Taken(ent, "Heat") - ent.Comp.HeatAtFall >= ent.Comp.BurnDown;
    }

    /// <summary>Whether it lies on a powered stasis bed, which keeps it down for as long as the bed has power.</summary>
    private bool OnStasisBed(Entity<WFRisesComponent> ent)
    {
        if (ent.Comp.StasisRange <= 0f)
            return false;

        _beds.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.StasisRange, _beds);
        foreach (var bed in _beds)
        {
            if (this.IsPowered(bed, EntityManager))
                return true;
        }

        return false;
    }

    /// <summary>Gets a downed mob back up: heals it, grows limbs back and wakes it.</summary>
    public void Rise(Entity<WFRisesComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        comp.RiseAt = null;
        if (comp.Final || TerminatingOrDeleted(ent))
            return;

        if (SourceLost(ent))
        {
            comp.Final = true;
            return;
        }

        comp.LastRose = now;
        comp.Rises++;
        _limbs.Regrow(ent, comp.Regrow);
        _elite.RestoreTo(ent, comp.RiseHealth);
        _mobState.ChangeMobState(ent, MobState.Alive);
        _audio.PlayPvs(comp.RiseSound, ent);
        _popup.PopupEntity(Loc.GetString("wf-remade-elite-rises", ("mob", ent.Owner)), ent, PopupType.LargeCaution);

        // The Director forgets a Remade that dies, so count it again now it's back.
        if (TryComp<WFRemadeComponent>(ent, out var remade))
            _director.Enlist(ent, remade.Source);
    }

    /// <summary>Keeps a downed mob down for good, with a popup saying why.</summary>
    public void EndForGood(Entity<WFRisesComponent> ent, string reason)
    {
        if (ent.Comp.Final)
            return;

        ent.Comp.Final = true;
        ent.Comp.RiseAt = null;
        _popup.PopupEntity(Loc.GetString(reason, ("mob", ent.Owner)), ent, PopupType.MediumCaution);
    }

    /// <summary>Whether the mob is down for now and will get back up: the Director should count it as alive.</summary>
    public bool IsRising(EntityUid uid)
    {
        return TryComp<WFRisesComponent>(uid, out var comp) && comp.RiseAt != null;
    }
}
