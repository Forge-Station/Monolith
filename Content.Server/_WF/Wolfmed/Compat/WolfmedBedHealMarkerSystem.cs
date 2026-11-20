using Content.Server.Bed.Components;
using Content.Shared._WF.Wolfmed.Compat;

namespace Content.Server._WF.Wolfmed.Compat;

/// <summary>Keeps the shared bed-heal marker in step with the server HealOnBuckleComponent.</summary>
public sealed class WolfmedBedHealMarkerSystem : EntitySystem
{
    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        // WP9: was ComponentStartup, which made every healing bed gain a component the moment it spawned and
        // failed PrototypeSaveTest.UninitializedSaveTest ("gains a component on spawn"). MapInit never runs on
        // that test's uninitialised map, and for a live bed it runs in the same spawn call, so nothing changes
        // in game. The pair is free (BedSystem only takes Strapped/Unstrapped on this component).
        SubscribeLocalEvent<HealOnBuckleComponent, MapInitEvent>(OnMapInit);
    }

    /// <summary>Adds the shared marker when a healing bed is map-initialised.</summary>
    private void OnMapInit(Entity<HealOnBuckleComponent> ent, ref MapInitEvent args)
        => EnsureComp<WolfmedBedHealMarkerComponent>(ent);
}
