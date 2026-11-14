using Content.Server._WF.Remade.Roster;

namespace Content.Server._WF.Remade.Abilities;

/// <summary>The systems and helpers the roster's abilities (leaps, spits, bursts, shells) use.</summary>
public sealed partial class WFAbilitySystem
{
    [Dependency] private WFExploderSystem _exploder = default!;
    [Dependency] private WFRosterSystem _roster = default!;
    [Dependency] private WFShellSystem _shell = default!;

    public WFExploderSystem Exploder => _exploder;

    public WFRosterSystem Roster => _roster;

    public WFShellSystem Shell => _shell;

    /// <summary>Shakes the mob itself for a while, as if something is about to burst.</summary>
    public void Swell(EntityUid uid, float seconds)
    {
        _jitter.DoJitter(uid, TimeSpan.FromSeconds(seconds), true, 12f, 14f, true);
    }

    /// <summary>A random number between two values.</summary>
    public float RandomRange(float min, float max)
    {
        return _random.NextFloat(min, max);
    }

    /// <summary>The direction from the mob to another entity, in the world.</summary>
    public System.Numerics.Vector2 Toward(EntityUid from, EntityUid to)
    {
        return _transform.GetWorldPosition(to) - _transform.GetWorldPosition(from);
    }
}
