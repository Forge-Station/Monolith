using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Server.Decals;
using Content.Shared.Decals;
using Content.Shared.Gravity;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.BloodTrail;

/// <summary>
/// Leaves decals behind a stained mob: footprints while it walks, drag marks while it crawls, lies down or is
/// hauled around incapacitated. Ported from Colonial Marines Universe.
/// </summary>
public sealed partial class FootPrintsSystem : EntitySystem
{
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private StandingStateSystem _standing = default!;

    /// <summary>Every decal this module leaves starts with this, which is how a wash tells them from paint.</summary>
    public const string DecalPrefix = "Footprint";

    /// <summary>Prints one spot holds before new ones are skipped, so a busy corridor can't pile decals up.</summary>
    public const int MaxPrintsNearby = 8;

    private const float MinAlpha = 0.01f;
    private const float NearbyRange = 0.5f;
    private const string ShoesSlot = "shoes";
    private const string SuitSlot = "outerClothing";

    // Decals are placed by their corner.
    private static readonly Vector2 DecalCenterOffset = new(-0.5f, -0.5f);
    private static readonly Angle DragRotationOffset = Angle.FromDegrees(-90);
    private static readonly Angle StepRotationOffset = Angle.FromDegrees(180);

    private EntityQuery<HumanoidAppearanceComponent> _humanoidQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<PressureProtectionComponent> _pressureQuery;

    public override void Initialize()
    {
        base.Initialize();

        _humanoidQuery = GetEntityQuery<HumanoidAppearanceComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _pressureQuery = GetEntityQuery<PressureProtectionComponent>();

        SubscribeLocalEvent<FootPrintsComponent, MoveEvent>(OnMove);
    }

    /// <summary>Stains a mob, so what it does next on the floor leaves prints of this color.</summary>
    public void Stain(EntityUid uid, Color color)
    {
        if (color.A <= 0f)
            return;

        var comp = EnsureComp<FootPrintsComponent>(uid);
        if (comp.PrintsColor.A <= 0f)
        {
            // The first print lands a step away from the stain, not on top of it.
            comp.PrintsColor = color;
            comp.StepPos = Transform(uid).LocalPosition;
            return;
        }

        var alpha = MathF.Max(comp.PrintsColor.A, color.A);
        comp.PrintsColor = Color.InterpolateBetween(comp.PrintsColor, color, comp.ColorInterpolationFactor)
            .WithAlpha(alpha);
    }

    /// <summary>The mob is on the floor: not floating and not in the air.</summary>
    public bool IsGrounded(EntityUid uid)
    {
        if (_gravity.IsWeightless(uid))
            return false;

        return !_physicsQuery.TryComp(uid, out var body) || body.BodyStatus == BodyStatus.OnGround;
    }

    /// <summary>The mob's body is on the floor: crawling, lying, critical or dead.</summary>
    public bool IsDragging(EntityUid uid)
    {
        return _standing.IsDown(uid) || _mobState.IsIncapacitated(uid);
    }

    private void OnMove(Entity<FootPrintsComponent> ent, ref MoveEvent args)
    {
        var comp = ent.Comp;
        if (comp.PrintsColor.A <= 0f)
            return;

        // Prints are placed in grid space, so a mob inside or on top of something else leaves none.
        var xform = args.Component;
        if (xform.GridUid is not { } grid || xform.ParentUid != grid)
            return;

        var dragging = IsDragging(ent);
        var stepSize = dragging ? comp.DragSize : comp.StepSize;
        var position = xform.LocalPosition;
        var delta = position - comp.StepPos;
        if (delta.LengthSquared() <= stepSize * stepSize)
            return;

        comp.StepPos = position;
        if (!IsGrounded(ent))
            return;

        comp.RightStep = !comp.RightStep;

        if (dragging)
        {
            if (comp.DraggingDecals.Count > 0)
                Place(comp, grid, _random.Pick(comp.DraggingDecals), position, delta.ToAngle() + DragRotationOffset);
        }
        else if (PickStep(ent) is { } step)
        {
            var rotation = xform.LocalRotation;
            var side = comp.RightStep ? rotation + StepRotationOffset : rotation;
            Place(comp, grid, step, position + side.RotateVec(comp.OffsetPrint), rotation + StepRotationOffset);
        }

        // Rounding can leave a sliver of alpha, which would place one more print nobody can see.
        var alpha = comp.PrintsColor.A - comp.ColorReduceAlpha;
        comp.PrintsColor = comp.PrintsColor.WithAlpha(alpha < MinAlpha ? 0f : alpha);
    }

    /// <summary>The footprint a walking mob leaves, or null for one with no feet to speak of.</summary>
    private ProtoId<DecalPrototype>? PickStep(Entity<FootPrintsComponent> ent)
    {
        if (!_humanoidQuery.HasComp(ent))
            return null;

        if (_inventory.TryGetSlotEntity(ent, SuitSlot, out var suit) && _pressureQuery.HasComp(suit))
            return ent.Comp.SuitDecal;

        if (_inventory.TryGetSlotEntity(ent, ShoesSlot, out _))
            return ent.Comp.ShoesDecal;

        return ent.Comp.RightStep ? ent.Comp.RightBareDecal : ent.Comp.LeftBareDecal;
    }

    private void Place(FootPrintsComponent comp, EntityUid grid, ProtoId<DecalPrototype> decal, Vector2 position, Angle rotation)
    {
        if (_decals.GetDecalsInRange(grid, position, NearbyRange, placed => IsPrint(comp, placed.Id)).Count >= MaxPrintsNearby)
            return;

        var coords = new EntityCoordinates(grid, position + DecalCenterOffset);
        _decals.TryAddDecal(decal, coords, out _, comp.PrintsColor, rotation, cleanable: true);
    }

    private static bool IsPrint(FootPrintsComponent comp, string decal)
    {
        return decal == comp.LeftBareDecal.Id
               || decal == comp.RightBareDecal.Id
               || decal == comp.ShoesDecal.Id
               || decal == comp.SuitDecal.Id
               || comp.DraggingDecals.Contains(decal);
    }
}
