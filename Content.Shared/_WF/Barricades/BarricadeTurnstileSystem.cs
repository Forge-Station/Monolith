using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Events;

namespace Content.Shared._WF.Barricades;

/// <summary>Lets whitelisted mobs through a turnstile only from behind it, along with whatever they pull.</summary>
public sealed partial class BarricadeTurnstileSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BarricadeTurnstileComponent, PreventCollideEvent>(OnPreventCollide);
        SubscribeLocalEvent<BarricadeTurnstileComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<BarricadeTurnstileComponent, EndCollideEvent>(OnEndCollide);
    }

    private void OnPreventCollide(Entity<BarricadeTurnstileComponent> ent, ref PreventCollideEvent args)
    {
        if (args.Cancelled || !args.OurFixture.Hard || !args.OtherFixture.Hard)
            return;

        if (ent.Comp.CollideExceptions.Contains(args.OtherEntity))
        {
            args.Cancelled = true;
            return;
        }

        if (TryComp(args.OtherEntity, out PullableComponent? pullable) && pullable.Puller is { } puller && ent.Comp.CollideExceptions.Contains(puller))
        {
            ent.Comp.CollideExceptions.Add(args.OtherEntity);
            Dirty(ent);
            args.Cancelled = true;
            return;
        }

        if (_whitelist.IsWhitelistFail(ent.Comp.ProcessWhitelist, args.OtherEntity))
        {
            args.Cancelled = true;
            return;
        }

        if (!CanPassDirection(ent, args.OtherEntity))
            return;

        ent.Comp.CollideExceptions.Add(args.OtherEntity);
        if (TryComp(args.OtherEntity, out PullerComponent? pullerComp) && pullerComp.Pulling is { } pulled)
            ent.Comp.CollideExceptions.Add(pulled);

        args.Cancelled = true;
        Dirty(ent);
    }

    private void OnStartCollide(Entity<BarricadeTurnstileComponent> ent, ref StartCollideEvent args)
    {
        if (ent.Comp.CollideExceptions.Contains(args.OtherEntity))
            _audio.PlayPredicted(ent.Comp.TurnSound, ent, args.OtherEntity);
    }

    private void OnEndCollide(Entity<BarricadeTurnstileComponent> ent, ref EndCollideEvent args)
    {
        if (args.OurFixture.Hard)
            return;

        ent.Comp.CollideExceptions.Remove(args.OtherEntity);
        Dirty(ent);
    }

    /// <summary>Whether the other entity approaches from behind, within 45 degrees.</summary>
    private bool CanPassDirection(EntityUid turnstile, EntityUid other)
    {
        var (pos, rot) = _transform.GetWorldPositionRotation(turnstile);
        var otherPos = _transform.GetWorldPosition(other);
        var approachAngle = (pos - otherPos).ToAngle();
        var rotateAngle = rot.ToWorldVec().ToAngle();

        var diff = Math.Abs(approachAngle - rotateAngle) % MathHelper.TwoPi;
        if (diff > Math.PI)
            diff = MathHelper.TwoPi - diff;

        return diff < Math.PI / 4;
    }
}
