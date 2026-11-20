using Content.Server._WF.Remade.Monolith;
using Content.Shared._WF.Remade;

namespace Content.Server._WF.Remade.Director;

/// <summary>The trap behind an echo: a Remade playing dead beside the body whose headset called for help.</summary>
public sealed partial class WFRemadeDirectorSystem
{
    private const float LureSpot = 1.6f;

    /// <summary>Lays a fake body beside a corpse for the nearest stone at Growth or above that reaches it.</summary>
    public bool TryLureAt(EntityUid corpse)
    {
        var here = _transform.GetMapCoordinates(corpse);
        Entity<WFRemadeDirectorComponent, WFMonolithComponent>? best = null;
        var bestDistance = float.MaxValue;
        var query = EntityQueryEnumerator<WFRemadeDirectorComponent, WFMonolithComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var director, out var monolith, out var xform))
        {
            if (monolith.Stage < WFMonolithStage.Growth || xform.MapID != here.MapId)
                continue;

            var distance = (_transform.GetWorldPosition(xform) - here.Position).Length();
            if (distance <= director.AwakeRadius * 2f && distance < bestDistance)
            {
                best = (uid, director, monolith);
                bestDistance = distance;
            }
        }

        if (best is not { } ent
            || ent.Comp1.Horde.Count >= HordeCap(ent.Comp2.Stage)
            || CountDead(ent.Comp1) >= MaxDead(ent.Comp2.Stage)
            || Buy(ent, corpse, false) is not { } proto)
        {
            return false;
        }

        var remade = Spawn(proto, OpenSpotNear(Transform(corpse).Coordinates, LureSpot));
        Enlist(remade, ent.Owner);
        PlayDead(remade);
        return true;
    }
}
