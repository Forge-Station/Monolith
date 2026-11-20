using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Elites;
using Content.Server.Beam;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Remade.Abilities;

/// <summary>The helpers the elite abilities (rams, slams, blinks, grabs, latches, taunts) use.</summary>
public sealed partial class WFAbilitySystem
{
    [Dependency] private WFEliteSystem _elite = default!;
    [Dependency] private BeamSystem _beam = default!;
    [Dependency] private WFTauntSystem _taunt = default!;

    public WFEliteSystem Elite => _elite;

    public WFTauntSystem Taunt => _taunt;

    /// <summary>The living Remade within range of the mob, not counting itself.</summary>
    public List<EntityUid> Allies(EntityUid around, float range)
    {
        var result = new List<EntityUid>();
        _mobs.Clear();
        _lookup.GetEntitiesInRange(Transform(around).Coordinates, range, _mobs);
        foreach (var mob in _mobs)
        {
            if (mob.Owner != around && HasComp<WFRemadeComponent>(mob) && !IsDead(mob))
                result.Add(mob);
        }

        return result;
    }

    /// <summary>Draws a beam between two entities, such as a tongue.</summary>
    public void DrawBeam(EntityUid from, EntityUid to, EntProtoId beam)
    {
        _beam.TryCreateBeam(from, to, beam.Id);
    }
}
