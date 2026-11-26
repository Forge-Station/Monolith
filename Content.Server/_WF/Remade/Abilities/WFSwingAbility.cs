using System.Numerics;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Damage;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WF.Remade.Abilities;

/// <summary>
/// A wind-up, an optional lunge, then one blow across an arc: damage, knockdown, and optionally pulling people in or
/// pushing them off, and breaking what stands in the way. The brute's slam, the hunter's hookblade, the divider's swipe,
/// the tripod's arm swing and the Undying's lunge are all this with different numbers.
/// </summary>
public sealed partial class WFSwingAbility : WFAbility
{
    /// <summary>Seconds it stands and rears back before it strikes.</summary>
    [DataField]
    public float WindUp = 0.6f;

    /// <summary>The width of the arc in degrees. 360 hits all round it.</summary>
    [DataField]
    public float Arc = 130f;

    /// <summary>How far the blow reaches.</summary>
    [DataField]
    public float Reach = 3f;

    /// <summary>Tiles it springs towards the target between the wind-up and the blow. 0 stays put.</summary>
    [DataField]
    public float Lunge;

    [DataField]
    public float LungeSpeed = 8f;

    /// <summary>Tiles it drags whoever it hits in towards itself.</summary>
    [DataField]
    public float Pull;

    /// <summary>Tiles it knocks whoever it hits away.</summary>
    [DataField]
    public float Push;

    [DataField]
    public float KnockdownTime;

    /// <summary>Structural damage to doors, windows and machines in the arc. 0 leaves them be.</summary>
    [DataField]
    public float StructureDamage;

    [DataField]
    public DamageSpecifier Damage = new();

    [DataField]
    public SoundSpecifier? HitSound;

    /// <summary>The sound of the blow landing, played even if it hits nothing.</summary>
    [DataField]
    public SoundSpecifier? StrikeSound;

    private enum Phase : byte
    {
        Idle,
        WindUp,
        Lunge,
    }

    /// <summary>Blows it has struck.</summary>
    [ViewVariables]
    public int Swings;

    /// <summary>Living things the last blow hit.</summary>
    [ViewVariables]
    public int Hits;

    /// <summary>Structures the last blow hit.</summary>
    [ViewVariables]
    public int Smashed;

    private Phase _phase;
    private TimeSpan _phaseEnd;
    private Vector2 _aim;

    public WFSwingAbility()
    {
        Cooldown = 3.5f;
        Range = 3f;
        Needs.Add(WFRemadeLimbStat.Melee);
    }

    public override float SpeedModifier => _phase == Phase.WindUp ? 0f : 1f;

    public override WFAbilityResult TryStart(in WFAbilityContext ctx)
    {
        if (!InRange(ctx, out var target))
            return WFAbilityResult.None;

        _aim = ctx.System.Toward(ctx.Owner, target);
        _phase = Phase.WindUp;
        _phaseEnd = ctx.Now + TimeSpan.FromSeconds(WindUp);
        Hits = 0;
        Smashed = 0;
        ctx.System.Play(ctx.Owner, Sound);
        return WFAbilityResult.Running;
    }

    public override bool Update(in WFAbilityContext ctx, float frameTime)
    {
        if (_phase == Phase.WindUp)
        {
            if (ctx.Now < _phaseEnd)
                return true;

            if (Lunge > 0f)
            {
                if (ctx.Target is { } live && ctx.System.IsLive(live))
                    _aim = ctx.System.Toward(ctx.Owner, live);

                var distance = Math.Min(Lunge, _aim.Length());
                ctx.System.Roster.Leap(ctx.Owner, _aim.Normalized() * distance, LungeSpeed);
                _phase = Phase.Lunge;
                _phaseEnd = ctx.Now + TimeSpan.FromSeconds(distance / LungeSpeed + 0.3f);
                return true;
            }

            Strike(ctx);
            return false;
        }

        if (_phase == Phase.Lunge && ctx.System.Roster.Airborne(ctx.Owner) && ctx.Now < _phaseEnd)
            return true;

        ctx.System.Roster.Land(ctx.Owner);
        Strike(ctx);
        return false;
    }

    public override void Stop(in WFAbilityContext ctx)
    {
        if (_phase == Phase.Lunge)
            ctx.System.Roster.Land(ctx.Owner);

        _phase = Phase.Idle;
    }

    private void Strike(in WFAbilityContext ctx)
    {
        if (ctx.Target is { } live && ctx.System.IsLive(live))
            _aim = ctx.System.Toward(ctx.Owner, live);

        Swings++;
        ctx.System.Play(ctx.Owner, StrikeSound);
        var all = Arc >= 360f;
        var half = Angle.FromDegrees(Arc / 2f);
        var aim = new Angle(_aim);
        foreach (var prey in ctx.System.Prey(ctx.Owner, Reach))
        {
            var offset = ctx.System.Toward(ctx.Owner, prey);
            if (!all && offset.LengthSquared() > 0.04f && Math.Abs(Angle.ShortestDistance(aim, new Angle(offset)).Theta) > half.Theta)
                continue;

            Hits++;
            ctx.System.Roster.Strike(ctx.Owner, prey, Damage);
            if (KnockdownTime > 0f)
                ctx.System.Knockdown(prey, KnockdownTime);

            var distance = offset.Length();
            if (Pull > 0f && distance > 1.2f)
                ctx.System.Elite.Throw(prey, -offset, Math.Min(Pull, distance - 0.9f));
            else if (Push > 0f)
                ctx.System.Elite.Throw(prey, offset.LengthSquared() > 0.01f ? offset : _aim, Push);

            ctx.System.Play(prey, HitSound);
            ctx.System.Popup(Loc.GetString("wf-remade-elite-swing-hit", ("mob", ctx.Owner), ("target", prey)), prey, PopupType.LargeCaution);
        }

        if (StructureDamage > 0f)
            Smashed = ctx.System.Elite.Smash(ctx.Owner, _aim, Reach, all ? 180f : Arc / 2f, StructureDamage);
    }
}
