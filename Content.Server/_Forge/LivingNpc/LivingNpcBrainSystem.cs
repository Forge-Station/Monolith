using Content.Server.Speech.Components;
using Content.Shared._Forge.CCVar;
using Content.Shared._Forge.LivingNpc;
using Content.Shared._Forge.LivingNpc.Components;
using Content.Shared._Forge.LivingNpc.Prototypes;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.LivingNpc;

public sealed partial class LivingNpcBrainSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private LivingNpcCombatSystem _combat = default!;
    [Dependency] private LivingNpcMemorySystem _memory = default!;
    [Dependency] private LivingNpcMovementSystem _movement = default!;
    [Dependency] private LivingNpcSocialSystem _social = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private bool _enabled = true;
    private int _maxUpdates = 64;
    private float _pauseDistance = 28f;
    private float _pauseTimer;
    private int _updateCursor;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, ForgeCVars.LivingNpcEnabled, v => _enabled = v, true);
        Subs.CVar(_cfg, ForgeCVars.LivingNpcMaxUpdates, v => _maxUpdates = v, true);
        Subs.CVar(_cfg, ForgeCVars.LivingNpcPauseDistance, v => _pauseDistance = v, true);

        SubscribeLocalEvent<LivingNpcComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<LivingNpcComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<LivingNpcComponent, PlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<LivingNpcComponent, PlayerDetachedEvent>(OnDetached);
    }

    private void OnMapInit(Entity<LivingNpcComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.PersonalityId is { } protoId && _prototypes.TryIndex(protoId, out LivingNpcPersonalityPrototype? proto))
            ent.Comp.Personality = proto.Traits.Clone();

        ent.Comp.Personality.Clamp();
        ent.Comp.Mood.Clamp();

        var xform = Transform(ent);
        ent.Comp.Home = xform.Coordinates;
        ent.Comp.HomeSet = true;

        var anchorQuery = EntityQueryEnumerator<LivingNpcAnchorComponent, TransformComponent>();
        while (anchorQuery.MoveNext(out _, out var anchor, out var anchorXform))
        {
            if (anchorXform.Coordinates.TryDistance(EntityManager, xform.Coordinates, out var dist) &&
                dist <= anchor.Radius &&
                (anchor.Group == null || anchor.Group == ent.Comp.Group))
            {
                ent.Comp.Home = anchorXform.Coordinates;
                ent.Comp.HomeRadius = Math.Max(ent.Comp.HomeRadius, anchor.Radius);
                break;
            }
        }

        EnsureComp<LivingNpcMemoryComponent>(ent.Owner);
        EnsureComp<LivingNpcSocialComponent>(ent.Owner);
        var listener = EnsureComp<ActiveListenerComponent>(ent.Owner);
        listener.Range = ent.Comp.HearRange;

        ent.Comp.NextThink = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(0.1f, 1.2f));
        if (TryComp<LivingNpcSocialComponent>(ent, out var social))
            social.NextIdleChat = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(4f, 18f));

        if (ent.Comp.Enabled)
            EnsureComp<ActiveLivingNpcComponent>(ent.Owner);
    }

    private void OnShutdown(Entity<LivingNpcComponent> ent, ref ComponentShutdown args)
    {
        _movement.Stop(ent);
        RemComp<ActiveLivingNpcComponent>(ent.Owner);
    }

    private void OnAttached(Entity<LivingNpcComponent> ent, ref PlayerAttachedEvent args)
    {
        Sleep(ent);
    }

    private void OnDetached(Entity<LivingNpcComponent> ent, ref PlayerDetachedEvent args)
    {
        if (_mobState.IsIncapacitated(ent) || TerminatingOrDeleted(ent))
            return;

        if (TryComp<MindContainerComponent>(ent, out var mind) && mind.HasMind)
            return;

        Wake(ent);
    }

    public void Wake(Entity<LivingNpcComponent> ent)
    {
        if (!ent.Comp.Enabled)
            return;

        EnsureComp<ActiveLivingNpcComponent>(ent.Owner);
        ent.Comp.Asleep = false;
    }

    public void Sleep(Entity<LivingNpcComponent> ent)
    {
        _movement.Stop(ent);
        _combat.LeaveCombat(ent);
        RemComp<ActiveLivingNpcComponent>(ent.Owner);
        ent.Comp.Asleep = true;
    }

    public override void Update(float frameTime)
    {
        if (!_enabled)
            return;

        _pauseTimer += frameTime;
        if (_pauseTimer >= 2f)
        {
            _pauseTimer = 0f;
            RefreshSleep();
        }

        var now = _timing.CurTime;
        var processed = 0;
        var query = EntityQueryEnumerator<ActiveLivingNpcComponent, LivingNpcComponent>();
        var seen = 0;

        while (query.MoveNext(out var uid, out _, out var npc))
        {
            seen++;
            if (seen <= _updateCursor)
                continue;

            if (processed >= _maxUpdates)
            {
                _updateCursor = seen;
                return;
            }

            if (now < npc.NextThink)
                continue;

            Think(uid, npc, now);
            processed++;
        }

        _updateCursor = 0;
    }

    private void RefreshSleep()
    {
        var query = EntityQueryEnumerator<LivingNpcComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var npc, out var xform))
        {
            if (!npc.Enabled || HasComp<ActorComponent>(uid))
                continue;

            if (TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind)
            {
                Sleep((uid, npc));
                continue;
            }

            if (_mobState.IsIncapacitated(uid))
            {
                Sleep((uid, npc));
                continue;
            }

            var nearby = AnyPlayerNearby(uid, xform, npc);
            if (nearby && npc.Asleep)
                Wake((uid, npc));
            else if (!nearby && !npc.Asleep)
                Sleep((uid, npc));
        }
    }

    private bool AnyPlayerNearby(EntityUid uid, TransformComponent xform, LivingNpcComponent npc)
    {
        // Keep brains awake on empty servers, tests, and mapping sessions.
        if (_players.PlayerCount == 0)
            return true;

        var origin = _transform.GetWorldPosition(xform);
        var range = Math.Max(_pauseDistance, npc.VisionRange + 4f);
        var rangeSq = range * range;

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } player)
                continue;
            if (HasComp<GhostComponent>(player))
                continue;
            if (!TryComp(player, out TransformComponent? playerXform) || playerXform.MapID != xform.MapID)
                continue;
            if ((_transform.GetWorldPosition(playerXform) - origin).LengthSquared() <= rangeSq)
                return true;
        }

        return false;
    }

    private void Think(EntityUid uid, LivingNpcComponent npc, TimeSpan now)
    {
        npc.NextThink = now + TimeSpan.FromSeconds(npc.ThinkInterval + _random.NextFloat(0f, npc.ThinkJitter));

        if (!_mobState.IsAlive(uid) || TerminatingOrDeleted(uid))
            return;

        TickMood(uid, npc);
        var world = Perceive(uid, npc, now);
        var intent = PickIntent(uid, npc, world, now);

        if (intent != npc.CurrentIntent)
        {
            OnIntentChanged(uid, npc, npc.CurrentIntent, intent);
            npc.CurrentIntent = intent;
            npc.IntentStartedAt = now;
            npc.FailedMoves = 0;
        }

        ExecuteIntent(uid, npc, world, now);
    }

    private void TickMood(EntityUid uid, LivingNpcComponent npc)
    {
        var p = npc.Personality;
        var m = npc.Mood;

        // Drift toward a personality baseline.
        m.Fear = Approach(m.Fear, 0.05f * (1f - p.Bravery), 0.04f);
        m.Anger = Approach(m.Anger, 0.08f * p.Temper, 0.03f);
        m.Happiness = Approach(m.Happiness, 0.45f + p.Humor * 0.2f, 0.02f);
        m.Energy = Approach(m.Energy, 0.55f + p.Discipline * 0.15f, 0.015f);
        m.SocialNeed = Approach(m.SocialNeed, 0.25f + p.Extraversion * 0.4f, 0.02f);

        if (TryComp<Content.Shared.Nutrition.Components.HungerComponent>(uid, out var hunger) &&
            hunger.CurrentThreshold <= Content.Shared.Nutrition.Components.HungerThreshold.Peckish)
        {
            m.Happiness = Math.Clamp(m.Happiness - 0.01f, 0f, 1f);
            m.Energy = Math.Clamp(m.Energy - 0.01f, 0f, 1f);
        }

        if (TryComp<Content.Shared.Nutrition.Components.ThirstComponent>(uid, out var thirst) &&
            thirst.CurrentThirstThreshold <= Content.Shared.Nutrition.Components.ThirstThreshold.Thirsty)
        {
            m.Happiness = Math.Clamp(m.Happiness - 0.01f, 0f, 1f);
        }

        m.Clamp();
    }

    private static float Approach(float value, float target, float delta)
    {
        if (value < target)
            return Math.Min(target, value + delta);
        if (value > target)
            return Math.Max(target, value - delta);
        return value;
    }

    private void OnIntentChanged(EntityUid uid, LivingNpcComponent npc, LivingNpcIntent from, LivingNpcIntent to)
    {
        if (from == LivingNpcIntent.Fight && to != LivingNpcIntent.Fight)
            _combat.LeaveCombat(uid);

        if (to is not (LivingNpcIntent.Wander or LivingNpcIntent.Work or LivingNpcIntent.Rest
            or LivingNpcIntent.Socialize or LivingNpcIntent.Investigate or LivingNpcIntent.Help
            or LivingNpcIntent.Flee or LivingNpcIntent.Fight or LivingNpcIntent.Eat))
        {
            _movement.Stop(uid);
            npc.Destination = null;
        }
    }
}
