using Content.Server.Light.EntitySystems;
using Content.Shared._Starlight.Shadekin;
using Content.Shared.Light.Components;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Shadekin;

public sealed partial class ShadegenSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PoweredLightSystem _light = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly HandheldLightSystem _handheldLight = default!;
    [Dependency] private readonly ContainerSystem _container = default!;
    private readonly HashSet<EntityUid> _updateQueue = new();

    public override void Initialize()
    {
        base.Initialize();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ShadegenComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (_timing.CurTime < component.NextUpdate)
                continue;

            component.NextUpdate = _timing.CurTime + component.UpdateCooldown;

            foreach (var toUpdate in _updateQueue)
            {
                if (Deleted(toUpdate))
                    continue;

                RemComp<ShadegenAffectedComponent>(toUpdate);
            }

            _updateQueue.Clear();

            var lightQuery = _lookup.GetEntitiesInRange<PointLightComponent>(Transform(uid).Coordinates, component.Range);

            var containerQuery = _lookup.GetEntitiesInRange<ContainerManagerComponent>(Transform(uid).Coordinates, component.Range);
            foreach (var containerent in containerQuery)
                foreach (var container in _container.GetAllContainers(containerent.Owner, containerent.Comp))
                    foreach (var contained in container.ContainedEntities)
                        if (TryComp<PointLightComponent>(contained, out var pointLight))
                            lightQuery.Add((contained, pointLight));

            foreach (var light in lightQuery)
            {
                if (HasComp<DarkLightComponent>(light.Owner))
                    continue;

                EnsureComp<ShadegenAffectedComponent>(light.Owner);
                _updateQueue.Add(light.Owner);

                if (TryComp<HandheldLightComponent>(light.Owner, out var handheldcomp) && handheldcomp.Activated)
                    _handheldLight.TurnOff((light.Owner, handheldcomp), makeNoise: false);

                if (component.DestroyLights && TryComp<PoweredLightComponent>(light.Owner, out var poweredcomp) && poweredcomp.On)
                    _light.TryDestroyBulb(light.Owner, poweredcomp);
            }
        }
    }
}
