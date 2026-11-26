using Content.Server.Light.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Light.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._WF.CavernEncounters;

/// <summary>
/// The Lamplighter stands frozen, unable to strike, while a switched-on handheld light shines on it, within the
/// light's radius and with nothing solid between; and every few seconds it puts out the nearest such light.
/// </summary>
public sealed partial class WFLamplighterSystem : EntitySystem
{
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private HandheldLightSystem _handheld = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPointLightSystem _lights = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.2);

    private TimeSpan _nextCheck;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFLamplighterComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<WFLamplighterComponent, AttackAttemptEvent>(OnAttackAttempt);
    }

    private void OnRefreshSpeed(Entity<WFLamplighterComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Lit)
            args.ModifySpeed(0f);
    }

    private void OnAttackAttempt(Entity<WFLamplighterComponent> ent, ref AttackAttemptEvent args)
    {
        if (ent.Comp.Lit)
            args.Cancel();
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextCheck)
            return;

        _nextCheck = now + CheckInterval;

        var query = EntityQueryEnumerator<WFLamplighterComponent>();
        while (query.MoveNext(out var uid, out var lamplighter))
        {
            if (!_mobState.IsAlive(uid))
            {
                SetLit((uid, lamplighter), false);
                continue;
            }

            SetLit((uid, lamplighter), IsLit(uid));

            if (now >= lamplighter.NextDouse)
                lamplighter.NextDouse = now + (TryDouse((uid, lamplighter)) ? lamplighter.DouseInterval : TimeSpan.FromSeconds(1));
        }
    }

    /// <summary>Freezes or frees it as the light on it comes and goes.</summary>
    private void SetLit(Entity<WFLamplighterComponent> ent, bool lit)
    {
        if (ent.Comp.Lit == lit)
            return;

        ent.Comp.Lit = lit;
        _movement.RefreshMovementSpeedModifiers(ent);
    }

    /// <summary>Whether a switched-on handheld light, not one of its omens, shines on it: in its radius, with nothing solid between.</summary>
    public bool IsLit(EntityUid uid)
    {
        var position = _transform.GetMapCoordinates(uid);

        var lights = EntityQueryEnumerator<HandheldLightComponent>();
        while (lights.MoveNext(out var light, out var handheld))
        {
            if (!handheld.Activated || HasComp<WFCavernOmenLightComponent>(light) || !_lights.TryGetLight(light, out var point) || !point.Enabled)
                continue;

            var source = _transform.GetMapCoordinates(light);
            if (source.MapId != position.MapId || (source.Position - position.Position).Length() > point.Radius)
                continue;

            if (_examine.InRangeUnOccluded(source, position, point.Radius, null))
                return true;
        }

        return false;
    }

    /// <summary>Puts out the nearest switched-on handheld light within reach; false when there is none.</summary>
    private bool TryDouse(Entity<WFLamplighterComponent> ent)
    {
        var position = _transform.GetMapCoordinates(ent);
        Entity<HandheldLightComponent>? nearest = null;
        var best = ent.Comp.DouseRange;

        var lights = EntityQueryEnumerator<HandheldLightComponent>();
        while (lights.MoveNext(out var light, out var handheld))
        {
            // Its own omen lights die for the players, not for it.
            if (!handheld.Activated || HasComp<WFCavernOmenLightComponent>(light))
                continue;

            var source = _transform.GetMapCoordinates(light);
            if (source.MapId != position.MapId)
                continue;

            var distance = (source.Position - position.Position).Length();
            if (distance > best)
                continue;

            best = distance;
            nearest = (light, handheld);
        }

        if (nearest is not { } doused)
            return false;

        _handheld.TurnOff(doused);
        _audio.PlayPvs(ent.Comp.DouseSound, doused);
        return true;
    }
}
