using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._WF.Remade.Abilities;

/// <summary>Where a lurker is in its cycle of opening its shell, firing and curling back up.</summary>
public enum WFSpinePhase : byte
{
    /// <summary>Shut: tough, fast, can't shoot.</summary>
    Closed,

    /// <summary>Peeling open. It can't move or fire yet, and it's already soft.</summary>
    Opening,

    /// <summary>Open: slow and soft, ready to fire.</summary>
    Open,

    /// <summary>Rearing back to fire.</summary>
    WindUp,
}

/// <summary>
/// A lurker's whole fight. With a target in sight it peels its shell open, rears back and fires one spine per tentacle it has
/// left, then waits to fire again. Hurt, it curls up shut for a while. With no target it shuts and the ability ends. The shell
/// itself is <see cref="Content.Server._WF.Remade.Roster.WFRemadeShellComponent"/>. Numbers from DS13-1.0's lurker.
/// </summary>
public sealed partial class WFSpineAbility : WFAbility
{
    [DataField]
    public EntProtoId Projectile = "WFRemadeSpine";

    /// <summary>How fast a spine flies, in tiles a second.</summary>
    [DataField]
    public float ProjectileSpeed = 18f;

    /// <summary>Seconds it rears back before it fires.</summary>
    [DataField]
    public float WindUp = 0.85f;

    /// <summary>Seconds it takes to open, soft and still.</summary>
    [DataField]
    public float OpenTime = 1f;

    /// <summary>Seconds between volleys while it's open.</summary>
    [DataField]
    public float VolleyCooldown = 3f;

    /// <summary>Seconds it stays shut after being hurt.</summary>
    [DataField]
    public float CurlTime = 4f;

    /// <summary>Seconds without a target in sight before it gives up and shuts.</summary>
    [DataField]
    public float GiveUp = 5f;

    /// <summary>How far the spines scatter, in degrees. The first one flies true.</summary>
    [DataField]
    public float Spread = 14f;

    /// <summary>Its speed while open, as a multiple of normal.</summary>
    [DataField]
    public float OpenSpeed = 0.35f;

    /// <summary>The limbs it fires from, one spine each.</summary>
    [DataField]
    public List<string> Spines = new();

    [DataField]
    public SoundSpecifier? OpenSound;

    [DataField]
    public SoundSpecifier? FireSound;

    [ViewVariables]
    public WFSpinePhase Phase;

    /// <summary>Volleys it has fired, and spines in all.</summary>
    [ViewVariables]
    public int Volleys;

    [ViewVariables]
    public int SpinesFired;

    /// <summary>Times it has curled up because it was hurt.</summary>
    [ViewVariables]
    public int Curls;

    private TimeSpan _phaseEnd;
    private TimeSpan _shutUntil;
    private TimeSpan _nextVolley;
    private TimeSpan _lastSeen;
    private float _lastDamage;
    private EntityUid _aim;

    public WFSpineAbility()
    {
        Cooldown = 6f;
        MinRange = 3f;
        Range = 13f;
        Needs.Add(WFRemadeLimbStat.Fire);
    }

    public override float SpeedModifier => Phase switch
    {
        WFSpinePhase.Opening => 0f,
        WFSpinePhase.WindUp => 0f,
        WFSpinePhase.Open => OpenSpeed,
        _ => 1f,
    };

    public override WFAbilityResult TryStart(in WFAbilityContext ctx)
    {
        if (!InRange(ctx, out var target) || !ctx.System.CanSee(ctx.Owner, target, Range))
            return WFAbilityResult.None;

        Phase = WFSpinePhase.Closed;
        _lastSeen = ctx.Now;
        _shutUntil = TimeSpan.Zero;
        _nextVolley = ctx.Now;
        _lastDamage = ctx.System.Roster.TotalDamage(ctx.Owner);
        return WFAbilityResult.Running;
    }

    public override bool Update(in WFAbilityContext ctx, float frameTime)
    {
        var system = ctx.System;
        var total = system.Roster.TotalDamage(ctx.Owner);
        var hurt = total > _lastDamage + 0.01f;
        _lastDamage = total;
        if (hurt && Phase != WFSpinePhase.Closed)
        {
            Shut(ctx);
            _shutUntil = ctx.Now + TimeSpan.FromSeconds(CurlTime);
            Curls++;
            return true;
        }

        var seen = false;
        if (ctx.Target is { } target && system.IsLive(target))
        {
            var distance = system.Distance(ctx.Owner, target);
            seen = distance >= MinRange && distance <= Range && system.CanSee(ctx.Owner, target, Range);
            if (seen)
                _aim = target;
        }

        if (seen)
            _lastSeen = ctx.Now;

        if (ctx.Now - _lastSeen > TimeSpan.FromSeconds(GiveUp) || system.Roster.Remaining(ctx.Owner, Spines) == 0)
            return false;

        switch (Phase)
        {
            case WFSpinePhase.Closed:
                if (seen && ctx.Now >= _shutUntil)
                    Open(ctx);

                break;
            case WFSpinePhase.Opening:
                if (ctx.Now >= _phaseEnd)
                    SetPhase(ctx, WFSpinePhase.Open);

                break;
            case WFSpinePhase.Open:
                if (seen && ctx.Now >= _nextVolley)
                {
                    _phaseEnd = ctx.Now + TimeSpan.FromSeconds(WindUp);
                    system.Play(ctx.Owner, Sound);
                    SetPhase(ctx, WFSpinePhase.WindUp);
                }

                break;
            case WFSpinePhase.WindUp:
                if (ctx.Now >= _phaseEnd)
                {
                    Volley(ctx);
                    _nextVolley = ctx.Now + TimeSpan.FromSeconds(VolleyCooldown);
                    SetPhase(ctx, WFSpinePhase.Open);
                }

                break;
        }

        return true;
    }

    public override void Stop(in WFAbilityContext ctx)
    {
        Phase = WFSpinePhase.Closed;
        ctx.System.RefreshSpeed(ctx.Owner);

        // A dead one is drawn from its dead state, so its shell stays out of it.
        if (!ctx.System.IsDead(ctx.Owner))
            ctx.System.Shell.SetOpen(ctx.Owner, false);
    }

    private void Open(in WFAbilityContext ctx)
    {
        _phaseEnd = ctx.Now + TimeSpan.FromSeconds(OpenTime);
        ctx.System.Shell.SetOpen(ctx.Owner, true);
        ctx.System.Play(ctx.Owner, OpenSound);
        ctx.System.Popup(Loc.GetString("wf-remade-shell-open", ("mob", ctx.Owner)), ctx.Owner, PopupType.MediumCaution);
        SetPhase(ctx, WFSpinePhase.Opening);
    }

    private void Shut(in WFAbilityContext ctx)
    {
        ctx.System.Shell.SetOpen(ctx.Owner, false);
        ctx.System.Popup(Loc.GetString("wf-remade-shell-shut", ("mob", ctx.Owner)), ctx.Owner);
        SetPhase(ctx, WFSpinePhase.Closed);
    }

    private void SetPhase(in WFAbilityContext ctx, WFSpinePhase phase)
    {
        Phase = phase;
        ctx.System.RefreshSpeed(ctx.Owner);
    }

    /// <summary>One spine for each tentacle it has left: the first at the target, the rest scattered.</summary>
    private void Volley(in WFAbilityContext ctx)
    {
        var system = ctx.System;
        if (system.IsDead(_aim))
            return;

        var count = system.Roster.Remaining(ctx.Owner, Spines);
        var toward = system.Toward(ctx.Owner, _aim);
        Volleys++;
        system.Play(ctx.Owner, FireSound);
        for (var i = 0; i < count; i++)
        {
            var scatter = i == 0 ? 0f : system.RandomRange(-Spread / 2f, Spread / 2f);
            system.Roster.Fire(ctx.Owner, Projectile, Angle.FromDegrees(scatter).RotateVec(toward), ProjectileSpeed);
            SpinesFired++;
        }
    }
}
