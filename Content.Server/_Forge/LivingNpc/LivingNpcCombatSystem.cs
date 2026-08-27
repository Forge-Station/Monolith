using Content.Shared._Forge.LivingNpc;
using Content.Shared._Forge.LivingNpc.Components;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Random;

namespace Content.Server._Forge.LivingNpc;

public sealed partial class LivingNpcCombatSystem : EntitySystem
{
    [Dependency] private LivingNpcMemorySystem _memory = default!;
    [Dependency] private LivingNpcMovementSystem _movement = default!;
    [Dependency] private SharedCombatModeSystem _combat = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LivingNpcComponent, DamageChangedEvent>(OnDamage);
        SubscribeLocalEvent<LivingNpcComponent, MobStateChangedEvent>(OnMobState);
    }

    private void OnDamage(Entity<LivingNpcComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.Origin is not { Valid: true } origin || origin == ent.Owner)
            return;

        if (!TryComp<LivingNpcMemoryComponent>(ent, out var memory))
            return;

        _memory.NoteHurt(ent, memory, origin);

        var personality = ent.Comp.Personality;
        ent.Comp.Mood.Fear = Math.Clamp(ent.Comp.Mood.Fear + 0.22f * (1.1f - personality.Bravery), 0f, 1f);
        ent.Comp.Mood.Anger = Math.Clamp(ent.Comp.Mood.Anger + 0.28f * personality.Temper, 0f, 1f);
        ent.Comp.Mood.Happiness = Math.Clamp(ent.Comp.Mood.Happiness - 0.18f, 0f, 1f);

        ent.Comp.CurrentTarget = origin;
        if (ent.Comp.WillFight && personality.Bravery + ent.Comp.Mood.Anger >= 0.85f && ent.Comp.Mood.Fear < 0.7f)
        {
            SwitchIntent(ent.Comp, LivingNpcIntent.Fight);
        }
        else
        {
            SwitchIntent(ent.Comp, LivingNpcIntent.Flee);
        }
    }

    private void OnMobState(Entity<LivingNpcComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState is MobState.Critical or MobState.Dead)
        {
            _movement.Stop(ent);
            RemComp<ActiveLivingNpcComponent>(ent.Owner);
            if (TryComp<CombatModeComponent>(ent, out var combat))
                _combat.SetInCombatMode(ent, false, combat);
        }
        else if (args.NewMobState == MobState.Alive && ent.Comp.Enabled)
        {
            EnsureComp<ActiveLivingNpcComponent>(ent.Owner);
        }
    }

    public void TickMelee(EntityUid uid, LivingNpcComponent npc)
    {
        if (npc.CurrentTarget is not { Valid: true } target || TerminatingOrDeleted(target))
            return;

        if (!TryComp<CombatModeComponent>(uid, out var combat))
            return;

        if (!combat.IsInCombatMode)
            _combat.SetInCombatMode(uid, true, combat);

        if (!_melee.TryGetWeapon(uid, out var weaponUid, out var weapon))
            return;

        if (_movement.Distance(uid, target) > weapon.Range)
        {
            _movement.MoveToEntity(uid, target, Math.Max(0.6f, weapon.Range * 0.6f));
            return;
        }

        if (_random.Prob(0.18f))
            _melee.AttemptLightAttackMiss(uid, weaponUid, weapon, Transform(target).Coordinates);
        else
            _melee.AttemptLightAttack(uid, weaponUid, weapon, target);
    }

    public void LeaveCombat(EntityUid uid)
    {
        if (TryComp<CombatModeComponent>(uid, out var combat) && combat.IsInCombatMode)
            _combat.SetInCombatMode(uid, false, combat);
    }

    private static void SwitchIntent(LivingNpcComponent npc, LivingNpcIntent intent)
    {
        npc.CurrentIntent = intent;
        npc.IntentStartedAt = TimeSpan.Zero;
        npc.Destination = null;
        npc.FailedMoves = 0;
    }

}
