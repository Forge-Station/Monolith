using System.Numerics;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Damage;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WF.Remade.Abilities;

/// <summary>
/// A roar, then a run that builds speed the longer it goes. It breaks doors, windows and machines in its way, which slows
/// it, and throws aside, hurts and knocks down whoever it runs into without stopping. Needs its legs
/// (<see cref="WFRemadeLimbStat.Leap"/>). Numbers from DS13's brute long charge.
/// </summary>
public sealed partial class WFRamAbility : WFAbility
{
    /// <summary>Seconds it stands and roars before it runs.</summary>
    [DataField]
    public float WindUp = 1.2f;

    /// <summary>The longest it runs.</summary>
    [DataField]
    public float Duration = 5f;

    /// <summary>Its speed at the end of the build-up, as a multiple of normal.</summary>
    [DataField]
    public float TopSpeed = 2f;

    /// <summary>Seconds of running to reach top speed.</summary>
    [DataField]
    public float RampTime = 3f;

    /// <summary>It hits anything living this close.</summary>
    [DataField]
    public float HitRange = 1.4f;

    [DataField]
    public float KnockdownTime = 3f;

    /// <summary>Tiles it throws a victim sideways.</summary>
    [DataField]
    public float ThrowDistance = 2.5f;

    /// <summary>How far ahead it breaks things.</summary>
    [DataField]
    public float SmashReach = 1.3f;

    [DataField]
    public float StructureDamage = 600f;

    /// <summary>Seconds of build-up it loses each time it breaks something or flattens someone.</summary>
    [DataField]
    public float Stagger = 0.8f;

    [DataField]
    public DamageSpecifier Damage = new();

    [DataField]
    public SoundSpecifier? HitSound;

    [DataField]
    public SoundSpecifier? SmashSound;

    private enum Phase : byte
    {
        Idle,
        WindUp,
        Run,
    }

    /// <summary>Runs it has made.</summary>
    [ViewVariables]
    public int Charges;

    /// <summary>Structures it has broken or damaged on its runs.</summary>
    [ViewVariables]
    public int Smashed;

    /// <summary>Living things it has run into on its runs.</summary>
    [ViewVariables]
    public int Hits;

    private Phase _phase;
    private TimeSpan _phaseEnd;
    private TimeSpan _nextRefresh;
    private float _ramp;
    private EntityUid _target;
    private readonly HashSet<EntityUid> _struck = new();

    public WFRamAbility()
    {
        Cooldown = 18f;
        MinRange = 5f;
        Range = 14f;
        Needs.Add(WFRemadeLimbStat.Leap);
    }

    public override float SpeedModifier => _phase switch
    {
        Phase.WindUp => 0f,
        Phase.Run => 1f + (TopSpeed - 1f) * Math.Clamp(_ramp / RampTime, 0f, 1f),
        _ => 1f,
    };

    public override WFAbilityResult TryStart(in WFAbilityContext ctx)
    {
        if (!InRange(ctx, out var target))
            return WFAbilityResult.None;

        Charges++;
        _phase = Phase.WindUp;
        _target = target;
        _ramp = 0f;
        _struck.Clear();
        _phaseEnd = ctx.Now + TimeSpan.FromSeconds(WindUp);
        ctx.System.Play(ctx.Owner, Sound);
        return WFAbilityResult.Running;
    }

    public override bool Update(in WFAbilityContext ctx, float frameTime)
    {
        if (ctx.System.IsDead(_target))
            return false;

        ctx.System.KeepTarget(ctx.Owner, _target);
        if (_phase == Phase.WindUp)
        {
            if (ctx.Now < _phaseEnd)
                return true;

            _phase = Phase.Run;
            _phaseEnd = ctx.Now + TimeSpan.FromSeconds(Duration);
            ctx.System.RefreshSpeed(ctx.Owner);
            return true;
        }

        _ramp += frameTime;
        var heading = ctx.System.Elite.Heading(ctx.Owner, ctx.System.Toward(ctx.Owner, _target));
        var smashed = ctx.System.Elite.Smash(ctx.Owner, heading, SmashReach, 60f, StructureDamage);
        if (smashed > 0)
        {
            Smashed += smashed;
            _ramp = Math.Max(0f, _ramp - Stagger);
            ctx.System.Play(ctx.Owner, SmashSound);
        }

        foreach (var prey in ctx.System.Prey(ctx.Owner, HitRange))
        {
            if (!_struck.Add(prey))
                continue;

            Hits++;
            _ramp = Math.Max(0f, _ramp - Stagger);
            ctx.System.Roster.Strike(ctx.Owner, prey, Damage);
            ctx.System.Knockdown(prey, KnockdownTime);
            var side = new Vector2(-heading.Y, heading.X) * (ctx.System.RandomRange(0f, 1f) < 0.5f ? 1f : -1f);
            ctx.System.Elite.Throw(prey, side + heading * 0.3f, ThrowDistance);
            ctx.System.Play(prey, HitSound);
            ctx.System.Popup(Loc.GetString("wf-remade-elite-ram-hit", ("mob", ctx.Owner), ("target", prey)), prey, PopupType.LargeCaution);
        }

        if (ctx.Now >= _nextRefresh)
        {
            _nextRefresh = ctx.Now + TimeSpan.FromSeconds(0.2);
            ctx.System.RefreshSpeed(ctx.Owner);
        }

        return ctx.Now < _phaseEnd;
    }

    public override void Stop(in WFAbilityContext ctx)
    {
        _phase = Phase.Idle;
        _ramp = 0f;
    }
}
