using Content.Client.Shuttles;
using Content.Shared._WF.Planets.Flight;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;

namespace Content.Client._WF.ShipPreview;

/// <summary>
/// Estimates the strongest planet gravity a previewed hull can lift off in, from its thruster prototypes and tile count.
/// The client has no thrust ratings, so they come from prototype YAML; part upgrades and power are left out.
/// </summary>
public static class ShipPreviewLift
{
    // Server values: ShuttleSystem.TileDensityMultiplier, ThrusterComponent.Thrust's default, the atmosphere penalty.
    private const float TileMass = 0.5f;
    private const float DefaultThrust = 150f;
    private const float OrdinaryAtmosphereThrust = 0.5f;
    private const float StandardGravity = 9.81f;

    /// <summary>
    /// Tonnes of cargo a hull carries before losing a quarter of its thrust in space: a third of its tile mass, from the
    /// cargo load factor hull / (hull + load).
    /// </summary>
    public static float RatedLoad(int tileCount)
    {
        return tileCount * TileMass / 3f;
    }

    /// <summary>Gravity, in g, at which lift equals weight; zero when the hull has no linear thrusters.</summary>
    public static float MaxLiftGravity(IEntityManager entMan, IPrototypeManager protoMan, EntityUid grid, int tileCount)
    {
        if (tileCount <= 0)
            return 0f;

        var lift = 0f;
        var children = entMan.GetComponent<TransformComponent>(grid).ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            if (!entMan.HasComponent<ThrusterComponent>(uid)
                || entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype is not { } proto
                || !protoMan.GetPrototypeData(proto).TryGetValue("Thruster", out var data))
            {
                continue;
            }

            if (data.TryGet<ValueDataNode>("thrusterType", out var type) && type.Value != "Linear")
                continue;

            // The server gives a disabled engine no lift.
            if (data.TryGet<ValueDataNode>("enabled", out var enabled) && enabled.Value == "false")
                continue;

            var thrust = DefaultThrust;
            if (data.TryGet<ValueDataNode>("thrust", out var rated) && Parse.TryFloat(rated.Value, out var parsed))
                thrust = parsed;

            var landing = entMan.HasComponent<WFLandingThrusterComponent>(uid);
            lift += thrust * (landing ? 1f : OrdinaryAtmosphereThrust) / StandardGravity;
        }

        return lift / (tileCount * TileMass);
    }
}
