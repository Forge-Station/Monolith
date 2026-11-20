using Content.Server.Body.Components;
using Content.Shared._WF.RMCCore.Gibbing;

namespace Content.Server._WF.RMCCore.Gibbing;

/// <summary>Spawns <see cref="RMCSpawnEntitiesOnGibComponent.Entities"/> where a body is gibbed.</summary>
public sealed partial class RMCGibSpawnSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<RMCSpawnEntitiesOnGibComponent, BeingGibbedEvent>(OnGibbed);
    }

    private void OnGibbed(Entity<RMCSpawnEntitiesOnGibComponent> ent, ref BeingGibbedEvent args)
    {
        foreach (var protoId in ent.Comp.Entities)
        {
            var newEntity = Spawn(protoId, _transform.GetMoverCoordinates(ent));
            _transform.AttachToGridOrMap(newEntity);
        }
    }
}
