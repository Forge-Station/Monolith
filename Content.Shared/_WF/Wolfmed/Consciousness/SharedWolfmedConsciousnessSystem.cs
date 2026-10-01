using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Robust.Shared.Configuration;

namespace Content.Shared._WF.Wolfmed.Consciousness;

/// <summary>
/// The half of consciousness both sides need: who owns mob state, and the seam other systems push a level
/// through. The evaluation itself is server-side (pain, blood and the brain all are).
/// </summary>
public abstract class SharedWolfmedConsciousnessSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configuration = default!;

    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_configuration, WolfmedCVars.Consciousness, value => _enabled = value, true);
    }

    /// <summary>
    /// True when consciousness, not the damage thresholds, decides this body's Alive/Critical. The one thing
    /// <see cref="Content.Shared.Mobs.Systems.MobThresholdSystem"/> asks before reading damage totals.
    /// </summary>
    public bool OwnsMobState(EntityUid uid)
    {
        return _enabled && HasComp<WoundHostComponent>(uid);
    }

    /// <summary>
    /// Pushes an outside level into the evaluation: 0 nothing, 1 unconscious on its own. Keys are replaced,
    /// not stacked, so a system can keep writing its current level. Zero removes the key.
    /// </summary>
    /// <remarks>
    /// The seam the life systems use: brain oxygenation ("hypoxia"), sedation, arrest, a machine's shutdown, brain
    /// or core injury and core heat. Suffocation reaches the brain as oxygenation; there is no "airloss" key.
    /// </remarks>
    public virtual void SetExternalPressure(EntityUid body, string key, float level)
    {
    }

    /// <summary>Asks for a re-evaluation now. Cheap: it reads the inputs the body already has.</summary>
    public virtual void Refresh(EntityUid body)
    {
    }
}
