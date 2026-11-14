using Content.Server._DV.Execution;
using Content.Server.EUI;
using Content.Shared._Mono.Weapons.Ranged.Components;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.ActionBlocker;
using Content.Shared.Execution;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Player;

namespace Content.Server._WF.Wolfmed.Life;

// "Are you sure?" on both Execute verbs, and the suicide command with a gun in hand.
public sealed partial class WolfmedExecutionSystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private ExecutionSystem _gunExecution = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedExecutionSystem _meleeExecution = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    /// <summary>The Execute each executor has been asked about and has not answered yet.</summary>
    private readonly Dictionary<EntityUid, PendingExecution> _pending = new();

    private readonly record struct PendingExecution(EntityUid Victim, EntityUid Weapon, bool Gun, WolfmedChoiceEui? Eui);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedExecutionAskEvent>(OnAsk);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<GunComponent, SuicideByEnvironmentEvent>(OnGunSuicide);
    }

    #region Are you sure?

    private void OnAsk(ref WolfmedExecutionAskEvent args)
    {
        if (!args.Handled)
            args.Handled = AskFirst(args.Weapon, args.Victim, args.Attacker, false);
    }

    /// <summary>
    /// An Execute verb was used. A player is asked first and the do-after starts on a yes, so true means "do not
    /// start it now"; an executor with nobody behind it is not asked. A weapon that will not kill is turned away.
    /// </summary>
    public bool AskFirst(EntityUid weapon, EntityUid victim, EntityUid attacker, bool gun)
    {
        if (!TryComp(attacker, out ActorComponent? actor))
            return false;

        Withdraw(attacker);
        if (Refuse(weapon, victim, attacker, gun))
            return true;

        var session = actor.PlayerSession;
        var eui = new WolfmedChoiceEui(DialogState(weapon, victim, attacker, gun), accepted =>
        {
            // The window is closing by itself: only the answer is left to act on.
            if (_pending.TryGetValue(attacker, out var asked))
                _pending[attacker] = asked with { Eui = null };

            if (accepted && session.AttachedEntity == attacker)
                Confirm(attacker);
            else
                Decline(attacker);
        });

        _pending[attacker] = new PendingExecution(victim, weapon, gun, eui);
        _eui.OpenEui(eui, session);
        return true;
    }

    /// <summary>The victim and weapon this executor has been asked about and has not answered for. What the tests read.</summary>
    public (EntityUid Victim, EntityUid Weapon)? GetPending(EntityUid executor)
    {
        return _pending.TryGetValue(executor, out var pending) ? (pending.Victim, pending.Weapon) : null;
    }

    /// <summary>
    /// The executor said yes: everything the verb checked is checked again, and only then does the Execute do-after
    /// start. Public so a test can answer.
    /// </summary>
    public bool Confirm(EntityUid executor)
    {
        if (!_pending.Remove(executor, out var pending))
            return false;

        pending.Eui?.Withdraw();
        var (victim, weapon, gun, _) = pending;
        if (TerminatingOrDeleted(executor) || TerminatingOrDeleted(victim) || TerminatingOrDeleted(weapon))
            return false;

        // What the verb menu checked when it offered Execute: that weapon in the active hand, the victim in reach.
        if (!_interaction.TryGetUsedEntity(executor, out var used) || used != weapon ||
            !_interaction.InRangeAndAccessible(executor, victim) || !_actionBlocker.CanInteract(executor, victim))
            return false;

        if (Refuse(weapon, victim, executor, gun))
            return false;

        if (!gun)
            return _meleeExecution.StartConfirmedExecution(weapon, victim, executor);

        return TryComp(weapon, out GunComponent? comp) && _gunExecution.StartConfirmedExecution((weapon, comp), victim, executor);
    }

    /// <summary>The executor said no, or closed the window: nothing happens.</summary>
    public void Decline(EntityUid executor)
    {
        Withdraw(executor);
    }

    /// <summary>Closes the executor's open dialog, if any, without acting on it.</summary>
    private void Withdraw(EntityUid executor)
    {
        if (_pending.Remove(executor, out var pending))
            pending.Eui?.Withdraw();
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        // An executor who died or went out (Unconscious is Critical on a wound host) is no longer asked.
        if (args.NewMobState != MobState.Alive)
            Withdraw(args.Target);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        foreach (var pending in _pending.Values)
        {
            pending.Eui?.Withdraw();
        }

        _pending.Clear();
    }

    /// <summary>A weapon that will not kill a wound host gets a popup instead of a dialog.</summary>
    private bool Refuse(EntityUid weapon, EntityUid victim, EntityUid attacker, bool gun)
    {
        // Elsewhere the old head hit can still kill. A roulette shotgun's next shell is the game: nothing gives it away.
        if (!CanApply(victim) || HasComp<RouletteShotgunComponent>(weapon))
            return false;

        if ((gun ? Measure(weapon, attacker) : MeasureMelee(weapon, attacker)).Lethal)
            return false;

        _popup.PopupEntity(Loc.GetString("wolfmed-execution-refuse", ("weapon", weapon)), attacker, attacker);
        return true;
    }

    private WolfmedChoiceEuiState DialogState(EntityUid weapon, EntityUid victim, EntityUid attacker, bool gun)
    {
        if (attacker != victim)
        {
            return new WolfmedChoiceEuiState(
                Loc.GetString("wolfmed-execution-confirm-title"),
                Loc.GetString("wolfmed-execution-confirm-text", ("victim", Identity.Entity(victim, EntityManager)), ("weapon", weapon)),
                Loc.GetString("wolfmed-execution-confirm-accept"),
                Loc.GetString("wolfmed-execution-confirm-deny"));
        }

        // A blade on yourself always ghosts for good. A gun does where Wolfmed owns the death and the round is live:
        // elsewhere it is the old head hit, and a roulette shotgun's dialog may not say which shell is next.
        var plain = gun && (!CanApply(victim) || HasComp<RouletteShotgunComponent>(weapon));
        var text = plain ? "wolfmed-execution-confirm-self-text-plain" : "wolfmed-execution-confirm-self-text";
        return new WolfmedChoiceEuiState(
            Loc.GetString("wolfmed-execution-confirm-self-title"),
            Loc.GetString(text, ("weapon", weapon)),
            Loc.GetString("wolfmed-execution-confirm-self-accept"),
            Loc.GetString("wolfmed-execution-confirm-self-deny"));
    }

    #endregion

    #region Suicide command

    /// <summary>
    /// The suicide command with a gun in the active hand: on a wound host a lethal round is fired into the user's own
    /// head. Anything else is left unhandled, so the command's default runs.
    /// </summary>
    private void OnGunSuicide(Entity<GunComponent> gun, ref SuicideByEnvironmentEvent args)
    {
        // The command also offers itself to whatever stands within reach: a mounted gun is not in anybody's hand.
        var victim = args.Victim;
        if (args.Handled || _hands.GetActiveItem(victim) != gun.Owner || !CanApply(victim) ||
            !Measure(gun, victim).Lethal || !TryFireOne(gun, victim, out var strength))
            return;

        if (!HasComp<RouletteShotgunComponent>(gun))
        {
            var self = Identity.Entity(victim, EntityManager);
            _popup.PopupEntity(
                Loc.GetString("suicide-popup-gun-complete-external", ("attacker", self), ("victim", self), ("weapon", gun.Owner)),
                victim, Filter.Pvs(victim), true, PopupType.LargeCaution);
        }

        Apply(victim, victim, gun, strength, WolfmedEnding.Suicide);
        args.Handled = true;
    }

    /// <summary>
    /// Takes the next round the way a shot does, spends it and plays the shot. False when the gun will not fire or
    /// what came out of it does not kill.
    /// </summary>
    private bool TryFireOne(Entity<GunComponent> gun, EntityUid user, out WolfmedKillStrength strength)
    {
        strength = WolfmedKillStrength.None;
        if (!_gun.CanShoot(gun.Comp))
            return false;

        var attempt = new ShotAttemptedEvent { User = user, Used = gun };
        RaiseLocalEvent(gun, ref attempt);
        if (attempt.Cancelled)
            return false;

        RaiseLocalEvent(user, ref attempt);
        if (attempt.Cancelled)
            return false;

        var shoot = new AttemptShootEvent(user, null);
        RaiseLocalEvent(gun, ref shoot);
        if (shoot.Cancelled)
            return false;

        var take = new TakeAmmoEvent(1, new List<(EntityUid? Entity, IShootable Shootable)>(), Transform(user).Coordinates, user, true);
        RaiseLocalEvent(gun, take);
        if (take.Ammo.Count == 0)
        {
            _audio.PlayPvs(gun.Comp.SoundEmpty, gun);
            return false;
        }

        var (round, shootable) = take.Ammo[0];
        strength = MeasureRound(gun, round);
        if (!strength.Lethal)
        {
            if (shootable is not CartridgeAmmoComponent)
                Del(round);

            return false;
        }

        var sound = gun.Comp.SoundGunshot;
        if (shootable is CartridgeAmmoComponent cartridge && round is { } casing)
        {
            sound = cartridge.SoundGunshot ?? sound;
            cartridge.Spent = true;
            _appearance.SetData(casing, AmmoVisuals.Spent, true);
            Dirty(casing, cartridge);
        }
        else
        {
            Del(round);
        }

        _audio.PlayPvs(sound, gun);
        RaiseLocalEvent(gun, new AmmoShotEvent { FiredProjectiles = new List<EntityUid>() });
        return true;
    }

    #endregion
}
