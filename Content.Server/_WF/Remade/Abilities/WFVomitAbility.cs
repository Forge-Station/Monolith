using System.Numerics;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Damage;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WF.Remade.Abilities;

/// <summary>
/// A short retch, then a cone of acid in front of it. Everything living in the cone that it can see is burned and slowed, and
/// the floor is coated. It needs its head (<see cref="WFRemadeLimbStat.Fire"/>).
/// </summary>
public sealed partial class WFVomitAbility : WFAbility
{
    /// <summary>Seconds it stands and retches before it lets go.</summary>
    [DataField]
    public float WindUp = 0.8f;

    /// <summary>How wide the cone is, in degrees.</summary>
    [DataField]
    public float Arc = 70f;

    [DataField]
    public DamageSpecifier Damage = new();

    /// <summary>Seconds the acid slows whoever it hits.</summary>
    [DataField]
    public float SlowTime = 4f;

    /// <summary>Their speed while slowed, as a multiple of normal.</summary>
    [DataField]
    public float SlowSpeed = 0.5f;

    /// <summary>The reagent coated on the floor, and how much on each spot.</summary>
    [DataField]
    public string Reagent = "SulfuricAcid";

    [DataField]
    public float Spill = 3f;

    [DataField]
    public SoundSpecifier? VomitSound;

    /// <summary>Things the last vomit hit.</summary>
    [ViewVariables]
    public int Hits;

    /// <summary>Times it has let go.</summary>
    [ViewVariables]
    public int Vomits;

    private bool _winding;
    private TimeSpan _at;
    private EntityUid _target;
    private Vector2 _aim;

    public WFVomitAbility()
    {
        Cooldown = 7f;
        Range = 4.5f;
        Needs.Add(WFRemadeLimbStat.Fire);
    }

    public override float SpeedModifier => _winding ? 0f : 1f;

    public override WFAbilityResult TryStart(in WFAbilityContext ctx)
    {
        if (!InRange(ctx, out var target) || !ctx.System.CanSee(ctx.Owner, target, Range))
            return WFAbilityResult.None;

        _winding = true;
        _target = target;
        _at = ctx.Now + TimeSpan.FromSeconds(WindUp);
        Hits = 0;
        ctx.System.Play(ctx.Owner, Sound);
        ctx.System.Popup(Loc.GetString("wf-remade-vomit-start", ("mob", ctx.Owner)), ctx.Owner, PopupType.MediumCaution);
        return WFAbilityResult.Running;
    }

    public override bool Update(in WFAbilityContext ctx, float frameTime)
    {
        if (ctx.System.IsDead(_target))
            return false;

        // It faces the target until it lets go.
        _aim = ctx.System.Toward(ctx.Owner, _target);
        ctx.System.KeepTarget(ctx.Owner, _target);
        if (ctx.Now < _at)
            return true;

        Vomits++;
        ctx.System.Play(ctx.Owner, VomitSound);
        var half = Angle.FromDegrees(Arc / 2f);
        var aim = new Angle(_aim);
        foreach (var prey in ctx.System.Prey(ctx.Owner, Range))
        {
            var offset = ctx.System.Toward(ctx.Owner, prey);
            if (offset.LengthSquared() > 0.04f && Math.Abs(Angle.ShortestDistance(aim, new Angle(offset)).Theta) > half.Theta)
                continue;

            if (!ctx.System.CanSee(ctx.Owner, prey, Range))
                continue;

            Hits++;
            ctx.System.Roster.Strike(ctx.Owner, prey, Damage);
            ctx.System.Roster.Slow(prey, SlowTime, SlowSpeed);
            ctx.System.Popup(Loc.GetString("wf-remade-vomit-hit", ("mob", ctx.Owner)), prey, PopupType.LargeCaution);
        }

        Coat(ctx);
        return false;
    }

    public override void Stop(in WFAbilityContext ctx)
    {
        _winding = false;
    }

    /// <summary>Puddles of acid down the middle and sides of the cone.</summary>
    private void Coat(in WFAbilityContext ctx)
    {
        if (Spill <= 0f || _aim.LengthSquared() < 0.01f)
            return;

        var origin = ctx.System.Roster.WorldPosition(ctx.Owner, out var map);
        var forward = Vector2.Normalize(_aim);
        var side = new Vector2(-forward.Y, forward.X);
        for (var distance = 1.5f; distance <= Range; distance += 1.5f)
        {
            var width = distance * MathF.Tan(MathF.PI * Arc / 360f) * 0.5f;
            foreach (var along in new[] { -width, 0f, width })
            {
                ctx.System.Roster.Spill(origin + forward * distance + side * along, map, Reagent, Spill);
            }
        }
    }
}
