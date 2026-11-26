using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Remade.Structures;
using Content.Server.Storage.Components;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Structures;
using Content.Shared.Morgue.Components;
using Content.Shared.Popups;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Remade.Director;

/// <summary>
/// Fake bodies. Some of the Director's Remade are placed lying dead in a morgue tray, beside a tray, or by a nest, as well as among
/// the corpses on corruption, so that no body in the cold store is safe to open. A fake in a tray bursts it open when a crewman
/// comes close.
/// </summary>
public sealed partial class WFRemadeDirectorSystem
{
    [Dependency] private SharedEntityStorageSystem _fakeStorage = default!;

    private static readonly ProtoId<WFRemadeGrowthPrototype> NestKind = "WFRemadeNest";
    private static readonly SoundSpecifier TrayBurst = new SoundCollectionSpecifier("WFEnvBurst");

    /// <summary>A place to lay a fake body, and the tray if it is to go inside one.</summary>
    private readonly record struct FakeSite(EntityCoordinates At, EntityUid? Tray);

    /// <summary>The share of a wave's Remade that are laid out as fake bodies instead of coming for the crew, by stage.</summary>
    public static float FakeShare(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Growth => 0.2f,
            WFMonolithStage.Harvest or WFMonolithStage.Rapture => 0.25f,
            WFMonolithStage.Crescendo => 0.2f,
            _ => 0f,
        };
    }

    /// <summary>Morgue trays (empty and shut, or the floor beside them) and nests near a crewman, between the two ranges.</summary>
    private List<FakeSite> FakeSites(EntityUid near, EntityUid? grid, float min, float max)
    {
        var here = WorldPos(near);
        var sites = new List<FakeSite>();
        var morgues = EntityQueryEnumerator<MorgueComponent, EntityStorageComponent, TransformComponent>();
        while (morgues.MoveNext(out var uid, out _, out var storage, out var xform))
        {
            var distance = (_transform.GetWorldPosition(xform) - here).Length();
            if (xform.GridUid != grid || distance < min || distance > max)
                continue;

            sites.Add(!storage.Open && !HasContents(storage)
                ? new FakeSite(xform.Coordinates, uid)
                : new FakeSite(OpenSpotNear(xform.Coordinates, 1.6f), null));
        }

        var growths = EntityQueryEnumerator<WFGrowthComponent, TransformComponent>();
        while (growths.MoveNext(out _, out var growth, out var xform))
        {
            var distance = (_transform.GetWorldPosition(xform) - here).Length();
            if (growth.Kind == NestKind && xform.GridUid == grid && distance >= min && distance <= max)
                sites.Add(new FakeSite(OpenSpotNear(xform.Coordinates, 1.8f), null));
        }

        return sites;
    }

    private static bool HasContents(EntityStorageComponent storage)
    {
        foreach (var _ in storage.Contents.ContainedEntities)
        {
            return true;
        }

        return false;
    }

    /// <summary>Lays one of the wave's Remade out as a fake body near the prey. False if no such place is near or the stage has enough.</summary>
    private bool TryPlaceFake(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, EntProtoId proto, EntityUid prey)
    {
        if (CountDead(ent.Comp1) >= MaxDead(ent.Comp2.Stage))
            return false;

        var sites = FakeSites(prey, Transform(prey).GridUid, AmbushMin, AmbushMax * 1.6f);
        if (sites.Count == 0)
            return false;

        var site = _random.Pick(sites);
        var remade = Spawn(proto, site.At);
        Enlist(remade, ent.Owner);
        PlayDead(remade);
        if (site.Tray is { } tray)
            HideInTray(remade, tray);

        return true;
    }

    /// <summary>Shuts a Remade that is playing dead into a morgue tray.</summary>
    public bool HideInTray(EntityUid remade, EntityUid tray)
    {
        return TryComp<EntityStorageComponent>(tray, out var storage) && !storage.Open && _container.Insert(remade, storage.Contents);
    }

    /// <summary>A Remade that was lying in a tray bursts it open as it rises.</summary>
    private void Unseal(EntityUid remade)
    {
        if (!_container.TryGetContainingContainer(remade, out var container) || container.ID == HoldId)
            return;

        var tray = container.Owner;
        if (HasComp<EntityStorageComponent>(tray))
        {
            _fakeStorage.OpenStorage(tray);
            _audio.PlayPvs(TrayBurst, tray);
            _popup.PopupEntity(Loc.GetString("wf-remade-morgue-burst", ("tray", tray)), tray, PopupType.LargeCaution);
        }

        if (_container.IsEntityInContainer(remade))
            _container.TryRemoveFromContainer(remade, true);
    }
}
