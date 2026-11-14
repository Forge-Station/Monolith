using System.Numerics;
using Content.Shared._WF.Planets;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Planets;

/// <summary>
/// Networks built ahead of their sector body, in the lobby, so a bounded world's preload is done before the round
/// starts; <see cref="TryBuildNetwork"/> adopts one for the body that spawns where it was built.
/// </summary>
public sealed partial class WFPlanetNetworkSystem
{
    /// <summary>How close a body must spawn to a prebuilt centre, in metres, to take that network.</summary>
    private const float AdoptTolerance = 0.5f;

    private readonly List<Prebuilt> _prebuilt = new();

    /// <summary>Builds a surface's network at a centre with no body yet; false if it could not be built.</summary>
    public bool Prebuild(WFPlanetSurfacePrototype surface, Vector2 centre, string displayName)
    {
        if (BuildNetwork(surface, centre, displayName, null) is not { } network)
            return false;

        _prebuilt.Add(new Prebuilt(surface.ID, centre, displayName, network));
        return true;
    }

    /// <summary>The ground of every prebuilt network no body has taken yet.</summary>
    public IEnumerable<EntityUid> PrebuiltGrounds()
    {
        foreach (var entry in _prebuilt)
        {
            if (TryComp<WFPlanetNetworkComponent>(entry.Network, out var comp))
                yield return comp.GroundMap;
        }
    }

    /// <summary>Deletes every prebuilt network no body has taken.</summary>
    public int ClearPrebuilt()
    {
        var cleared = 0;

        foreach (var entry in _prebuilt)
        {
            if (TerminatingOrDeleted(entry.Network))
                continue;

            DeleteNetwork(entry.Network);
            cleared++;
        }

        _prebuilt.Clear();
        return cleared;
    }

    /// <summary>
    /// Hands a body the network prebuilt for its surface at its spot, if there is one; a prebuilt network under
    /// another name is deleted instead, as its names and weather were set up for that name.
    /// </summary>
    private bool TryAdopt(EntityUid body, WFPlanetSurfacePrototype surface, Vector2 centre, string displayName, out EntityUid network)
    {
        network = EntityUid.Invalid;

        for (var i = 0; i < _prebuilt.Count; i++)
        {
            var entry = _prebuilt[i];

            if (entry.Surface != surface.ID || Vector2.DistanceSquared(entry.Centre, centre) > AdoptTolerance * AdoptTolerance)
                continue;

            _prebuilt.RemoveAt(i);

            if (TerminatingOrDeleted(entry.Network) || !TryComp<WFPlanetNetworkComponent>(entry.Network, out var comp))
                return false;

            if (entry.DisplayName != displayName)
            {
                Log.Warning($"The network prebuilt for \"{surface.ID}\" as \"{entry.DisplayName}\" is not adopted by \"{displayName}\"; building afresh.");
                DeleteNetwork(entry.Network);
                return false;
            }

            comp.Planet = body;

            if (TryComp<WFOrbitLayerComponent>(comp.OrbitMap, out var orbit))
            {
                orbit.Planet = GetNetEntity(body);
                Dirty(comp.OrbitMap, orbit);
            }

            network = entry.Network;
            return true;
        }

        return false;
    }

    private readonly record struct Prebuilt(ProtoId<WFPlanetSurfacePrototype> Surface, Vector2 Centre, string DisplayName, EntityUid Network);
}
