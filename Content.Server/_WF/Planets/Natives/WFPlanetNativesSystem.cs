using Content.Server._WF.PlanetNpcGear;
using Content.Server.Body.Components;
using Content.Shared._Shitmed.Body.Components;
using Content.Shared._WF.Xenobiology.Components;
using Content.Shared.EntityTable;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Planets.Natives;

/// <summary>
/// Gives a world's natives <see cref="BreathingImmunityComponent"/> while they are on it, so its air doesn't kill its own
/// life. Runs before the NPC gear system, which hands NPCs that can't breathe the air a mask.
/// </summary>
public sealed class WFPlanetNativesSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(0.5);

    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(WFPlanetNpcGearSystem));
        _proto.PrototypesReloaded += OnPrototypesReloaded;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _proto.PrototypesReloaded -= OnPrototypesReloaded;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        var query = EntityQueryEnumerator<WFPlanetNativesComponent>();
        while (query.MoveNext(out var natives))
        {
            natives.Resolved = null;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _next)
            return;
        _next = now + Interval;

        var query = EntityQueryEnumerator<RespiratorComponent, MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var meta, out var xform))
        {
            if (xform.MapUid is not { } map || !TryComp(map, out WFPlanetNativesComponent? natives) ||
                HasComp<BreathingImmunityComponent>(uid) || !IsNative((uid, meta), natives))
            {
                continue;
            }

            AddComp<BreathingImmunityComponent>(uid);
        }
    }

    /// <summary>Whether an entity is one of a world's natives.</summary>
    public bool IsNative(Entity<MetaDataComponent?> ent, WFPlanetNativesComponent natives)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return false;

        if (ent.Comp.EntityPrototype is { } proto && NativePrototypes(natives).Contains(proto.ID))
            return true;

        return natives.Breeds.Count > 0 && TryComp(ent, out SlimeComponent? slime) && natives.Breeds.Contains(slime.Breed);
    }

    /// <summary>Every prototype a world's tables can spawn, through nested tables, and its listed entities.</summary>
    public HashSet<string> NativePrototypes(WFPlanetNativesComponent natives)
    {
        if (natives.Resolved != null)
            return natives.Resolved;

        var resolved = new HashSet<string>();
        foreach (var id in natives.Entities)
        {
            resolved.Add(id);
        }

        var context = new EntityTableContext();
        foreach (var id in natives.Tables)
        {
            if (!_proto.TryIndex(id, out var table))
                continue;
            foreach (var (spawn, _) in table.Table.ListSpawns(EntityManager, _proto, context))
            {
                resolved.Add(spawn);
            }
        }

        natives.Resolved = resolved;
        return resolved;
    }
}
