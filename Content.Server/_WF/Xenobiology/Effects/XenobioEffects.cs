using System.Numerics;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._WF.Xenobiology;
using Content.Shared._WF.Xenobiology.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Tag;
using Content.Shared.Whitelist;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Xenobiology.Effects;

/// <summary>
/// An extract reaction: runs a shared effect set and its own effects on the extract, once, at strength one.
/// </summary>
/// <remarks>
/// Wolfgate's reactive effects scale with the injected amount; Goob's extracts don't, so the effects here see a
/// quantity of one whatever was injected.
/// </remarks>
public sealed partial class XenobioReaction : EntityEffect
{
    [DataField("base")]
    public ProtoId<XenobioEffectSetPrototype>? Base;

    [DataField]
    public List<EntityEffect> Effects = new();

    /// <summary>
    /// Skip a spent extract. Off for sets nested inside another reaction, which run after it spends the extract.
    /// </summary>
    [DataField]
    public bool Once = true;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var entMan = args.EntityManager;
        if (Once && entMan.TryGetComponent<SlimeExtractComponent>(args.TargetEntity, out var extract) && extract.Spent)
            return;

        var unit = args is EntityEffectReagentArgs reagentArgs
            ? new EntityEffectReagentArgs(args.TargetEntity, entMan, null, null, FixedPoint2.New(1), reagentArgs.Reagent,
                reagentArgs.Method, FixedPoint2.New(1))
            : new EntityEffectReagentArgs(args.TargetEntity, entMan, null, null, FixedPoint2.New(1), null, null, FixedPoint2.New(1));

        if (Base is { } baseId)
        {
            foreach (var effect in IoCManager.Resolve<IPrototypeManager>().Index(baseId).Effects)
            {
                XenobioEffectHelpers.Apply(effect, unit);
            }
        }

        foreach (var effect in Effects)
        {
            XenobioEffectHelpers.Apply(effect, unit);
        }
    }
}

public static class XenobioEffectHelpers
{
    public static void Apply(EntityEffect effect, EntityEffectBaseArgs args)
    {
        if (effect.ShouldApply(args))
            effect.Effect(args);
    }

    public static EntityCoordinates RandomNear(IEntityManager entMan, EntityUid target, float offset)
    {
        var random = IoCManager.Resolve<IRobustRandom>();
        var coords = entMan.System<SharedTransformSystem>().GetMoverCoordinates(target);
        return coords.Offset(new Vector2(random.NextFloat(-offset, offset), random.NextFloat(-offset, offset)));
    }
}

/// <summary>
/// Marks a slime extract spent and stops it reacting again.
/// </summary>
public sealed partial class XenobioSpendExtract : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var entMan = args.EntityManager;
        if (entMan.TryGetComponent<SlimeExtractComponent>(args.TargetEntity, out var extract))
        {
            extract.Spent = true;
            entMan.Dirty(args.TargetEntity, extract);
        }

        entMan.RemoveComponent<ReactiveComponent>(args.TargetEntity);
    }
}

/// <summary>
/// Deletes the target.
/// </summary>
public sealed partial class XenobioDelete : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.QueueDeleteEntity(args.TargetEntity);
    }
}

/// <summary>
/// Plays a sound at the target.
/// </summary>
public sealed partial class XenobioPlaySound : EntityEffect
{
    [DataField(required: true)]
    public SoundSpecifier Sound = default!;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.System<AudioSystem>().PlayPvs(Sound, args.TargetEntity);
    }
}

/// <summary>
/// Spawns <see cref="Number"/> of <see cref="Entity"/> around the target.
/// </summary>
public sealed partial class XenobioSpawnEntity : EntityEffect
{
    [DataField(required: true)]
    public EntProtoId Entity;

    [DataField]
    public int Number = 1;

    /// <summary>
    /// How far from the target each one may land.
    /// </summary>
    [DataField]
    public float Offset = 0.3f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        for (var i = 0; i < Number; i++)
        {
            args.EntityManager.SpawnAtPosition(Entity, XenobioEffectHelpers.RandomNear(args.EntityManager, args.TargetEntity, Offset));
        }
    }
}

/// <summary>
/// Adds <see cref="Amount"/> of <see cref="Reagent"/> to the target's own solution.
/// </summary>
public sealed partial class XenobioAdjustSolution : EntityEffect
{
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    [DataField(required: true)]
    public FixedPoint2 Amount;

    [DataField]
    public string Solution = "extract";

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var solutions = args.EntityManager.System<SharedSolutionContainerSystem>();
        if (!solutions.TryGetSolution(args.TargetEntity, Solution, out var solution))
            return;

        if (Amount > 0)
            solutions.TryAddReagent(solution.Value, Reagent, Amount);
        else
            solutions.RemoveReagent(solution.Value, Reagent, -Amount);
    }
}

/// <summary>
/// Adds a tag to the target.
/// </summary>
public sealed partial class XenobioAddTag : EntityEffect
{
    [DataField(required: true)]
    public ProtoId<TagPrototype> Tag;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.System<TagSystem>().AddTag(args.TargetEntity, Tag);
    }
}

/// <summary>
/// Applies <see cref="Relayed"/> to every entity in range that has <see cref="CompName"/>.
/// </summary>
public sealed partial class XenobioRelayNearby : EntityEffect
{
    [DataField(required: true)]
    public string CompName = string.Empty;

    [DataField]
    public float Range = 5f;

    [DataField]
    public EntityWhitelist? Blacklist;

    [DataField("effect", required: true)]
    public EntityEffect Relayed = default!;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var entMan = args.EntityManager;
        var factory = IoCManager.Resolve<IComponentFactory>();
        if (!factory.TryGetRegistration(CompName, out var registration))
            return;

        var whitelist = entMan.System<EntityWhitelistSystem>();
        var lookup = entMan.System<EntityLookupSystem>();
        var coords = entMan.GetComponent<TransformComponent>(args.TargetEntity).Coordinates;
        foreach (var entity in lookup.GetEntitiesInRange(coords, Range))
        {
            if (entity == args.TargetEntity
                || !entMan.HasComponent(entity, registration.Type)
                || whitelist.IsBlacklistPass(Blacklist, entity))
                continue;

            XenobioEffectHelpers.Apply(Relayed, new EntityEffectReagentArgs(entity, entMan, null, null,
                FixedPoint2.New(1), null, null, FixedPoint2.New(1)));
        }
    }
}

/// <summary>
/// Releases smoke carrying <see cref="Solution"/> at the target.
/// </summary>
public sealed partial class XenobioSmoke : EntityEffect
{
    [DataField]
    public EntProtoId SmokePrototype = "Smoke";

    [DataField]
    public float Duration = 10f;

    [DataField]
    public int SpreadAmount = 20;

    [DataField]
    public Solution Solution = new();

    [DataField]
    public SoundSpecifier Sound = new SoundPathSpecifier("/Audio/Effects/smoke.ogg");

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var entMan = args.EntityManager;
        var xform = entMan.System<SharedTransformSystem>();
        var mapCoords = xform.GetMapCoordinates(args.TargetEntity);
        var maps = entMan.System<SharedMapSystem>();
        if (!maps.TryFindGridAt(mapCoords, out var gridUid, out _))
            return;

        var coords = maps.MapToGrid(gridUid, mapCoords).SnapToGrid(entMan);
        var smoke = entMan.SpawnEntity(SmokePrototype, coords);
        entMan.System<SmokeSystem>().StartSmoke(smoke, Solution.Clone(), Duration, SpreadAmount);
        entMan.System<AudioSystem>().PlayPvs(Sound, args.TargetEntity);
    }
}

/// <summary>
/// Changes a slime's breeding stats.
/// </summary>
public sealed partial class XenobioModifySlime : EntityEffect
{
    [DataField]
    public float ChanceModifier;

    [DataField]
    public int ExtractBonus;

    [DataField]
    public int OffspringBonus;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-xenobio-modify-slime", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent<SlimeComponent>(args.TargetEntity, out var slime))
            return;

        slime.MutationChance = Math.Clamp(slime.MutationChance + ChanceModifier, 0f, 1f);
        slime.ExtractsProduced = Math.Max(1, slime.ExtractsProduced + ExtractBonus);
        slime.MaxOffspring = Math.Max(1, slime.MaxOffspring + OffspringBonus);
        args.EntityManager.Dirty(args.TargetEntity, slime);
    }
}

/// <summary>
/// Teleports the target to a random open tile on its grid within <see cref="Radius"/>.
/// </summary>
public sealed partial class XenobioRandomTeleport : EntityEffect
{
    [DataField]
    public float Radius = 7f;

    [DataField]
    public int Attempts = 20;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var entMan = args.EntityManager;
        var xform = entMan.GetComponent<TransformComponent>(args.TargetEntity);
        if (xform.GridUid is not { } gridUid || !entMan.TryGetComponent<MapGridComponent>(gridUid, out var grid))
            return;

        var random = IoCManager.Resolve<IRobustRandom>();
        var maps = entMan.System<SharedMapSystem>();
        var turfs = entMan.System<TurfSystem>();
        var transform = entMan.System<SharedTransformSystem>();
        var origin = xform.Coordinates;
        for (var i = 0; i < Attempts; i++)
        {
            var offset = random.NextVector2(Radius);
            var target = origin.Offset(offset);
            var tile = maps.GetTileRef(gridUid, grid, target);
            if (turfs.IsSpace(tile) || turfs.IsTileBlocked(tile, CollisionGroup.Impassable))
                continue;

            transform.SetCoordinates(args.TargetEntity, maps.GridTileToLocal(gridUid, grid, tile.GridIndices));
            return;
        }
    }
}
