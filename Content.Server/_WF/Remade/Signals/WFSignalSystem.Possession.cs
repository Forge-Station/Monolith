using Content.Server._WF.Remade.Abilities;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Elites;
using Content.Server._WF.Remade.Monolith;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._WF.Remade.Signals;
using Content.Shared.Database;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Remade.Signals;

/// <summary>
/// A Signal driving a Remade. The Remade's thinking is switched off and the Director lets it go; the player gets its abilities as
/// actions. The Signal stays where it was with the player's mind still in it, and the player only visits the Remade, so when the
/// Remade is released, dies or is gone the player is back in the Signal.
/// </summary>
public sealed partial class WFSignalSystem
{
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private WFAbilitySystem _abilities = default!;

    private void InitializePossession()
    {
        SubscribeLocalEvent<WFSignalComponent, WFSignalPossessEvent>(OnPossess);
        SubscribeLocalEvent<WFSignalPossessedComponent, WFSignalReleaseEvent>(OnRelease);
        SubscribeLocalEvent<WFSignalPossessedComponent, WFSignalAbilityEvent>(OnAbility);
        SubscribeLocalEvent<WFSignalPossessedComponent, MobStateChangedEvent>(OnPossessedState);
        SubscribeLocalEvent<WFSignalPossessedComponent, PlayerDetachedEvent>(OnPossessedDetached);
        SubscribeLocalEvent<WFSignalPossessedComponent, EntityTerminatingEvent>(OnPossessedTerminating);
    }

    private void OnPossess(EntityUid uid, WFSignalComponent signal, WFSignalPossessEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryPossess((uid, signal), args.Target);
    }

    private void OnRelease(EntityUid uid, WFSignalPossessedComponent possessed, WFSignalReleaseEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        Release(uid);
    }

    private void OnPossessedState(EntityUid uid, WFSignalPossessedComponent possessed, MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            Release(uid);
    }

    /// <summary>The player left the Remade without letting go, such as by disconnecting: the Remade is let go and they are back in the Signal.</summary>
    private void OnPossessedDetached(EntityUid uid, WFSignalPossessedComponent possessed, PlayerDetachedEvent args)
    {
        Release(uid);
    }

    private void OnPossessedTerminating(EntityUid uid, WFSignalPossessedComponent possessed, ref EntityTerminatingEvent args)
    {
        Release(uid);
    }

    /// <summary>Whether a Remade is one a Signal may take: its stone's, up and about, with nobody in it and nothing the Director needs it for.</summary>
    public bool CanPossess(Entity<WFSignalComponent> ent, EntityUid remade)
    {
        if (TerminatingOrDeleted(remade)
            || !TryComp<WFRemadeComponent>(remade, out var comp)
            || comp.Source != ent.Comp.Stone
            || !_mobState.IsAlive(remade)
            || HasComp<ActorComponent>(remade)
            || HasComp<WFSignalPossessedComponent>(remade)
            || HasComp<WFUndyingComponent>(remade)
            || HasComp<WFRemadePlayingDeadComponent>(remade)
            || HasComp<WFRemadeVentTripComponent>(remade))
        {
            return false;
        }

        // An Infector off building is the Director's work in progress.
        return !TryComp<WFRemadeBehaviourComponent>(remade, out var behaviour)
               || behaviour.Mode is not (WFRemadeMode.Hidden or WFRemadeMode.Build);
    }

    /// <summary>Takes over a Remade within reach, for Psi, if the Signal's player is there to drive it.</summary>
    public bool TryPossess(Entity<WFSignalComponent> ent, EntityUid remade)
    {
        var now = _timing.CurTime;
        if (!Bound(ent, out var stone) || TerminatingOrDeleted(remade) || !_mind.TryGetMind(ent, out var mindId, out var mind))
            return false;

        if (ent.Comp.Possessing != null)
            return Fail(ent, "wf-signal-busy");

        if (now < ent.Comp.PossessReady)
            return Fail(ent, "wf-signal-cooldown");

        if (ent.Comp.Psi < WFSignalMath.PossessCost)
            return Fail(ent, "wf-signal-no-psi");

        if (_corruption.IsSanctuaryAt(ent) || _corruption.IsSanctuaryAt(remade))
            return Fail(ent, "wf-signal-sanctuary");

        var here = _transform.GetMapCoordinates(ent);
        var there = _transform.GetMapCoordinates(remade);
        if (here.MapId != there.MapId || (here.Position - there.Position).Length() > WFSignalMath.PossessRange)
            return Fail(ent, "wf-signal-far");

        if (!CanPossess(ent, remade))
            return Fail(ent, "wf-signal-not-yours");

        Take(remade, ent);
        EnsureComp<MindContainerComponent>(remade);
        _mind.Visit(mindId, remade, mind);
        if (mind.VisitingEntity != remade)
        {
            Release(remade);
            return Fail(ent, "wf-signal-nothing");
        }

        Grant(remade, ent);
        ent.Comp.Possessing = remade;
        ent.Comp.Psi -= WFSignalMath.PossessCost;
        ent.Comp.PossessReady = now + TimeSpan.FromSeconds(WFSignalMath.PossessCooldown);
        ent.Comp.Possessions++;
        Dirty(ent);

        _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(ent):player} took over {ToPrettyString(remade):target}");
        return true;
    }

    /// <summary>Switches off a Remade's own thinking and takes it out of the Director's hands.</summary>
    private void Take(EntityUid remade, EntityUid signal)
    {
        EnsureComp<WFSignalPossessedComponent>(remade).Signal = signal;

        // With no behaviour the Director neither steers nor listens for it. A stalker's narrowed sight goes back first.
        if (TryComp<WFRemadeBehaviourComponent>(remade, out var behaviour))
        {
            if (behaviour.Sight is { } sight)
            {
                _npc.SetBlackboard(remade, "VisionRadius", sight.Vision);
                _npc.SetBlackboard(remade, "AggroVisionRadius", sight.Aggro);
            }

            RemComp<WFRemadeBehaviourComponent>(remade);
        }

        if (TryComp<HTNComponent>(remade, out var htn))
            _htn.SetHTNEnabled((remade, htn), false);

        _npc.SleepNPC(remade);
        if (TryComp<NPCSteeringComponent>(remade, out var steering))
            _steering.Unregister(remade, steering);

        if (TryComp<WFRemadeAbilitiesComponent>(remade, out var abilities))
            _abilities.StopActive((remade, abilities));
    }

    /// <summary>Gives the player the Remade's abilities as actions, and a way out.</summary>
    private void Grant(EntityUid remade, Entity<WFSignalComponent> signal)
    {
        var possessed = Comp<WFSignalPossessedComponent>(remade);
        if (TryComp<WFRemadeAbilitiesComponent>(remade, out var abilities))
        {
            var seen = new HashSet<string>();
            foreach (var ability in abilities.Abilities)
            {
                var name = ability.GetType().Name;
                if (!seen.Add(name) || !_proto.HasIndex<EntityPrototype>(ActionFor(name)))
                    continue;

                EntityUid? action = null;
                if (!_actions.AddAction(remade, ref action, ActionFor(name)))
                    continue;

                _actions.SetUseDelay(action, TimeSpan.FromSeconds(ability.Cooldown));
                possessed.Actions.Add(action.Value);
            }
        }

        EntityUid? release = null;
        if (_actions.AddAction(remade, ref release, ReleaseAction))
            possessed.Actions.Add(release.Value);
    }

    /// <summary>The action prototype for an ability class: WFLeapAbility is WFSignalActionLeap.</summary>
    public static string ActionFor(string abilityName)
    {
        var name = abilityName;
        if (name.StartsWith("WF", StringComparison.Ordinal))
            name = name[2..];

        if (name.EndsWith("Ability", StringComparison.Ordinal))
            name = name[..^"Ability".Length];

        return $"WFSignalAction{name}";
    }

    /// <summary>Lets go of a Remade: the player is back in the Signal, and the Remade thinks for itself again and answers to the Director.</summary>
    public void Release(EntityUid remade)
    {
        if (!TryComp<WFSignalPossessedComponent>(remade, out var possessed))
            return;

        var signal = possessed.Signal;
        foreach (var action in possessed.Actions)
        {
            if (TerminatingOrDeleted(action))
                continue;

            _actions.RemoveAction(remade, action);
            QueueDel(action);
        }

        RemComp<WFSignalPossessedComponent>(remade);

        if (!TerminatingOrDeleted(signal) && _mind.TryGetMind(signal, out var mindId, out var mind) && mind.VisitingEntity == remade)
            _mind.UnVisit(mindId, mind);

        if (!TerminatingOrDeleted(signal) && TryComp<WFSignalComponent>(signal, out var comp))
        {
            comp.Possessing = null;
            if (!TerminatingOrDeleted(remade))
                _transform.SetMapCoordinates(signal, _transform.GetMapCoordinates(remade));

            Dirty(signal, comp);
            _adminLog.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(signal):player} let go of {ToPrettyString(remade):target}");
        }

        if (TerminatingOrDeleted(remade))
            return;

        if (TryComp<WFRemadeComponent>(remade, out var remadeComp))
            _director.Enlist(remade, remadeComp.Source);

        if (TryComp<HTNComponent>(remade, out var htn))
            _htn.SetHTNEnabled((remade, htn), true);

        if (_mobState.IsAlive(remade))
            _npc.WakeNPC(remade);
    }

    /// <summary>The ability the action stands for, aimed at the nearest living thing to the point or in reach.</summary>
    private void OnAbility(EntityUid uid, WFSignalPossessedComponent possessed, WFSignalAbilityEvent args)
    {
        if (args.Handled || !TryComp<WFRemadeAbilitiesComponent>(uid, out var abilities) || _mobState.IsDead(uid))
            return;

        WFAbility? ability = null;
        foreach (var candidate in abilities.Abilities)
        {
            if (candidate.GetType().Name == args.Ability)
            {
                ability = candidate;
                break;
            }
        }

        if (ability == null)
            return;

        if (_corruption.IsSanctuaryAt(uid))
        {
            Refuse(uid, "wf-signal-sanctuary");
            return;
        }

        var target = PickTarget(uid, args.Target, ability);
        if (!_abilities.TryUse((uid, abilities), ability, target))
        {
            Refuse(uid, "wf-signal-ability-none");
            return;
        }

        possessed.Target = target;
        args.Handled = true;
        _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(possessed.Signal):player} used {args.Ability} with {ToPrettyString(uid):entity} at {ToPrettyString(target):target}");
    }

    /// <summary>The living thing nearest the point that was clicked, else the nearest in reach of the ability, else the Remade itself.</summary>
    private EntityUid PickTarget(EntityUid remade, EntityCoordinates click, WFAbility ability)
    {
        var point = _transform.ToMapCoordinates(click).Position;
        var here = _transform.GetWorldPosition(remade);
        EntityUid? best = null;
        var bestDistance = 3f;
        EntityUid? nearest = null;
        var nearestDistance = ability.Range;
        foreach (var prey in _abilities.Prey(remade, ability.Range + 3f))
        {
            var position = _transform.GetWorldPosition(prey);
            var fromPoint = (position - point).Length();
            if (fromPoint < bestDistance)
            {
                best = prey;
                bestDistance = fromPoint;
            }

            var fromHere = (position - here).Length();
            if (fromHere < nearestDistance)
            {
                nearest = prey;
                nearestDistance = fromHere;
            }
        }

        return best ?? nearest ?? remade;
    }

    private void UpdateDriven(float frameTime)
    {
        var query = EntityQueryEnumerator<WFSignalPossessedComponent, WFRemadeAbilitiesComponent>();
        while (query.MoveNext(out var uid, out var possessed, out var abilities))
        {
            _abilities.UpdateDriven((uid, abilities), possessed.Target, frameTime);
        }
    }
}
