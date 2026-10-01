using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Random;

namespace Content.Shared._WF.Wolfmed.Targeting;

/// <summary>Decides whether a bullet lands on the part its shooter aimed at, from the gun's spread and the range.</summary>
/// <remarks>
/// The gun's current spread cone is projected out to the target: the wider the cone is there compared to the part's
/// size, the likelier the round strays to a neighbouring part. Melee and thrown items are not touched.
/// </remarks>
public sealed class WolfmedAimScatterSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Test seam: replaces the rolls (aim roll, then neighbour pick) with this value.</summary>
    public float? ForcedRoll;

    /// <summary>Returns the part a projectile really hits; <paramref name="aimed"/> unless the shot strays.</summary>
    public EntityUid Scatter(EntityUid body, EntityUid shooter, EntityUid? tool, EntityUid aimed)
    {
        if (tool is not { } projectile ||
            !TryComp(projectile, out ProjectileComponent? projectileComp) ||
            !_cfg.GetCVar(WolfmedCVars.AimScatter) ||
            !TryComp(aimed, out BodyPartComponent? aimedPart))
            return aimed;

        if (Roll() < HitChance(body, shooter, projectileComp.Weapon, aimed, aimedPart))
            return aimed;

        return PickNeighbour(body, aimedPart) ?? aimed;
    }

    /// <summary>Chance that a shot lands on the aimed part.</summary>
    public float HitChance(EntityUid body, EntityUid shooter, EntityUid? weapon, EntityUid aimed, BodyPartComponent aimedPart)
    {
        var best = _cfg.GetCVar(WolfmedCVars.AimBestChance);
        var worst = Math.Min(best, _cfg.GetCVar(WolfmedCVars.AimWorstChance));

        var spread = TryComp(weapon, out GunComponent? gun)
            ? Math.Max(gun.CurrentAngle.Theta, gun.MinAngleModified.Theta)
            : 0d;

        var range = 1f;
        if (!TerminatingOrDeleted(shooter))
        {
            var from = _transform.GetMapCoordinates(shooter);
            var to = _transform.GetMapCoordinates(body);
            if (from.MapId == to.MapId)
                range = Math.Max(0.5f, (to.Position - from.Position).Length());
        }

        // Half-width of the spread cone where it reaches the target, in tiles.
        var stray = range * (float) Math.Tan(Math.Min(spread, Math.PI / 2) / 2);
        if (stray <= 0.0001f)
            return best;

        return Math.Clamp(PartSize(aimed, aimedPart) / stray, worst, best);
    }

    private float PartSize(EntityUid part, BodyPartComponent comp)
    {
        if (TryComp(part, out WolfmedBodyPartComponent? profile) && profile.AimSize > 0f)
            return profile.AimSize;

        return comp.PartType switch
        {
            BodyPartType.Torso => 0.22f,
            BodyPartType.Head => 0.1f,
            BodyPartType.Leg => 0.12f,
            BodyPartType.Arm => 0.1f,
            BodyPartType.Foot => 0.07f,
            BodyPartType.Hand => 0.06f,
            _ => 0.1f,
        };
    }

    /// <summary>A part next to the aimed one, weighted by how much of the miss it would catch.</summary>
    private EntityUid? PickNeighbour(EntityUid body, BodyPartComponent aimed)
    {
        var options = new List<(EntityUid Part, float Weight)>();
        var total = 0f;
        foreach (var (id, part) in _body.GetBodyChildren(body))
        {
            var weight = NeighbourWeight(aimed, part);
            if (weight <= 0f)
                continue;

            options.Add((id, weight));
            total += weight;
        }

        if (options.Count == 0)
            return null;

        var roll = Roll() * total;
        foreach (var (part, weight) in options)
        {
            roll -= weight;
            if (roll <= 0f)
                return part;
        }

        return options[^1].Part;
    }

    private static float NeighbourWeight(BodyPartComponent aimed, BodyPartComponent other)
    {
        var sameSide = aimed.Symmetry == other.Symmetry;
        return (aimed.PartType, other.PartType) switch
        {
            (BodyPartType.Head, BodyPartType.Torso) => 1f,
            (BodyPartType.Torso, BodyPartType.Arm) => 1f,
            (BodyPartType.Torso, BodyPartType.Leg) => 1f,
            (BodyPartType.Torso, BodyPartType.Head) => 0.5f,
            (BodyPartType.Arm, BodyPartType.Torso) => 2f,
            (BodyPartType.Arm, BodyPartType.Hand) => sameSide ? 1f : 0f,
            (BodyPartType.Hand, BodyPartType.Arm) => sameSide ? 2f : 0f,
            (BodyPartType.Hand, BodyPartType.Torso) => 1f,
            (BodyPartType.Leg, BodyPartType.Torso) => 2f,
            (BodyPartType.Leg, BodyPartType.Foot) => sameSide ? 1f : 0f,
            (BodyPartType.Leg, BodyPartType.Leg) => sameSide ? 0f : 1f,
            (BodyPartType.Foot, BodyPartType.Leg) => sameSide ? 2f : 0f,
            (BodyPartType.Foot, BodyPartType.Foot) => sameSide ? 0f : 1f,
            _ => 0f,
        };
    }

    private float Roll() => ForcedRoll ?? _random.NextFloat();
}
