using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Shared._WF.Vehicles.Compat;

/// <summary>Hard-collision test over every fixture pair; the engine's FixturesComponent overload reads body A's fixtures twice.</summary>
public static class VehicleHardCollision
{
    /// <summary>Same arguments as the engine overload; false when a component is missing.</summary>
    public static bool IsHardCollidable(
        Entity<FixturesComponent?, PhysicsComponent?> bodyA,
        Entity<FixturesComponent?, PhysicsComponent?> bodyB)
    {
        if (bodyA.Comp1 is not { } fixturesA ||
            bodyA.Comp2 is not { } physicsA ||
            bodyB.Comp1 is not { } fixturesB ||
            bodyB.Comp2 is not { } physicsB)
        {
            return false;
        }

        return IsHardCollidable(fixturesA, physicsA, fixturesB, physicsB);
    }

    /// <summary>Whether both bodies are hard and some hard fixture of one collides with a hard fixture of the other.</summary>
    public static bool IsHardCollidable(
        FixturesComponent fixturesA,
        PhysicsComponent physicsA,
        FixturesComponent fixturesB,
        PhysicsComponent physicsB)
    {
        if (!physicsA.Hard ||
            !physicsB.Hard ||
            ((physicsA.CollisionLayer & physicsB.CollisionMask) == 0 &&
             (physicsA.CollisionMask & physicsB.CollisionLayer) == 0))
        {
            return false;
        }

        return IsHardCollidable(fixturesA, fixturesB);
    }

    /// <summary>Whether a hard fixture of A and a hard fixture of B filter each other in.</summary>
    public static bool IsHardCollidable(FixturesComponent fixturesA, FixturesComponent fixturesB)
    {
        foreach (var fixture in fixturesA.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            foreach (var other in fixturesB.Fixtures.Values)
            {
                if (!other.Hard)
                    continue;

                if ((fixture.CollisionLayer & other.CollisionMask) == 0 &&
                    (fixture.CollisionMask & other.CollisionLayer) == 0)
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }
}
