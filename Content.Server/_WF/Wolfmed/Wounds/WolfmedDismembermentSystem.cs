using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Server.Audio;
using Robust.Shared.Configuration;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>
/// A limb coming off under damage (V2): a wet tear and a spreading puddle for flesh, a shear and sparks
/// for a chassis. Both sides are <see cref="WolfmedSfxProfilePrototype"/> data.
/// </summary>
/// <remarks>
/// Driven by <see cref="WolfmedPartAmputatedEvent"/>, which only the damage path raises, so a surgical
/// removal stays quiet. The puddle is the body's own blood reagent read off its bloodstream, which is how
/// an IPC leaks oil without this file knowing what an IPC is. Shares
/// <see cref="WolfmedWoundSfxComponent"/>'s throttle with the wound sounds: the hit that takes an arm off
/// also creates the stump's wounds, and that is one noise.
/// </remarks>
public sealed class WolfmedDismembermentSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WolfmedWoundSfxSystem _sfx = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedPartAmputatedEvent>(OnAmputated);
    }

    private void OnAmputated(ref WolfmedPartAmputatedEvent args)
    {
        Play(args.Body, args.Part);
    }

    /// <summary>
    /// The sound, the spill and the effect for one limb coming off. Public so a test and any future
    /// dismemberment source can reach it without an amputation.
    /// </summary>
    public bool Play(EntityUid body, EntityUid part)
    {
        if (TerminatingOrDeleted(body) || _sfx.Profile is not { } profile)
            return false;

        var organic = _traits.IsOrganic(part);
        var spec = organic ? profile.OrganicDismemberment : profile.MechanicalDismemberment;
        var state = EnsureComp<WolfmedWoundSfxComponent>(body);

        if (spec.Sound != null && _config.GetCVar(WolfmedCVars.WoundSfx))
        {
            state.NextSound = _sfx.Now + profile.SoundInterval;
            _audio.PlayPvs(spec.Sound, body);
        }

        if (!_config.GetCVar(WolfmedCVars.HitDebris))
            return true;

        if (spec.Effect is { } effect)
            Spawn(effect, _transform.GetMapCoordinates(body));

        TrySpill(body, spec.SpillVolume);
        return true;
    }

    /// <summary>
    /// Puts the body's own blood on the floor where the limb was. Returns the puddle, or null when the
    /// body has no bloodstream to take it from.
    /// </summary>
    public EntityUid? TrySpill(EntityUid body, FixedPoint2 volume)
    {
        if (volume <= FixedPoint2.Zero || !TryComp(body, out BloodstreamComponent? bloodstream))
            return null;

        var solution = new Solution();
        solution.AddReagent(new ReagentId(bloodstream.BloodReagent, _bloodstream.GetEntityBloodData(body)), volume);
        return _puddle.TrySpillAt(body, solution, out var puddle, sound: false) ? puddle : null;
    }
}
