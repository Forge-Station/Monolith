using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets;

namespace Content.Server._WF.Planets;

/// <summary>Names the layers of a planet network that come unnamed, so the shuttle map can tell them apart.</summary>
public sealed class WFPlanetLayerNameSystem : EntitySystem
{
    [Dependency] private MetaDataSystem _meta = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPlanetNetworkBuiltEvent>(OnNetworkBuilt);
    }

    /// <summary>Gives the air, cloud and unnamed lower layers of a new network a name after their world.</summary>
    private void OnNetworkBuilt(ref WFPlanetNetworkBuiltEvent args)
    {
        if (!TryComp<WFPlanetNetworkComponent>(args.Network, out var network))
            return;

        var world = MetaData(args.Ground).EntityName;
        var air = 0;

        foreach (var layer in network.Layers)
        {
            if (layer == network.GroundMap || layer == network.OrbitMap)
                continue;

            air++;

            var cloud = HasComp<CEZCloudLayerComponent>(layer);
            _meta.SetEntityName(layer, cloud
                ? Loc.GetString("wf-planet-cloud-map-name", ("planet", world))
                : Loc.GetString("wf-planet-air-map-name", ("planet", world), ("level", air)));
        }

        var depth = 0;

        foreach (var layer in network.LowerLayers)
        {
            depth++;

            if (!Unnamed(layer))
                continue;

            _meta.SetEntityName(layer, Loc.GetString("wf-planet-lower-map-name", ("planet", world), ("level", depth)));
        }
    }

    /// <summary>Whether a layer still has the name its prototype gave it.</summary>
    private bool Unnamed(EntityUid layer)
    {
        var meta = MetaData(layer);
        return string.IsNullOrEmpty(meta.EntityName) || meta.EntityName == meta.EntityPrototype?.Name;
    }
}
