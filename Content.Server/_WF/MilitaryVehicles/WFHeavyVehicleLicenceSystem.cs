using Content.Server.Storage.EntitySystems;
using Content.Shared._WF.MilitaryVehicles;
using Content.Shared._WF.VehicleShop;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.MilitaryVehicles;

/// <summary>
/// Heavy vehicle licences: made out to whoever buys one, handed to faction crew at spawn, and checked by the vehicle
/// terminals before they sell anything military.
/// </summary>
public sealed class WFHeavyVehicleLicenceSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private StorageSystem _storage = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFVehicleShopLicenceCheckEvent>(OnLicenceCheck);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);
        SubscribeLocalEvent<WFHeavyVehicleLicenceComponent, WFVehicleShopPurchasedEvent>(OnPurchased);
        SubscribeLocalEvent<WFHeavyVehicleLicenceComponent, ExaminedEvent>(OnExamined);
    }

    private void OnLicenceCheck(ref WFVehicleShopLicenceCheckEvent ev)
    {
        ev.Licensed |= Carries(ev.User);
    }

    private void OnPurchased(Entity<WFHeavyVehicleLicenceComponent> ent, ref WFVehicleShopPurchasedEvent args)
    {
        Issue(ent, Name(args.Buyer));
    }

    private void OnExamined(Entity<WFHeavyVehicleLicenceComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(ent.Comp.Holder is { } holder
            ? Loc.GetString("wf-heavy-licence-examine-holder", ("holder", holder))
            : Loc.GetString("wf-heavy-licence-examine-blank"));
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (ev.JobId is { } job)
            IssueForJob(ev.Mob, job, ev.Profile.Name);
    }

    /// <summary>Gives someone the licence their job starts with, if it starts with one: in their bag, else in hand.</summary>
    public EntityUid? IssueForJob(EntityUid mob, string job, string holder)
    {
        foreach (var issue in _proto.EnumeratePrototypes<WFLicenceIssuePrototype>())
        {
            if (!issue.Jobs.Contains(job))
                continue;

            var licence = Spawn(issue.Licence, Transform(mob).Coordinates);
            if (TryComp<WFHeavyVehicleLicenceComponent>(licence, out var comp))
                Issue((licence, comp), holder);

            if (!_inventory.TryGetSlotEntity(mob, "back", out var bag) || !_storage.Insert(bag.Value, licence, out _, playSound: false))
                _hands.PickupOrDrop(mob, licence);

            return licence;
        }

        return null;
    }

    /// <summary>Makes a licence out to someone, by name.</summary>
    public void Issue(Entity<WFHeavyVehicleLicenceComponent> licence, string holder)
    {
        licence.Comp.Holder = holder;
        _meta.SetEntityName(licence, Loc.GetString("wf-heavy-licence-name", ("holder", holder)));
    }

    /// <summary>Whether someone has a licence on them: in hand, worn, or in anything they carry.</summary>
    public bool Carries(EntityUid user)
    {
        var query = EntityQueryEnumerator<WFHeavyVehicleLicenceComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            var parent = xform.ParentUid;
            while (parent.IsValid())
            {
                if (parent == user)
                    return true;

                if (!TryComp<TransformComponent>(parent, out var parentXform) || parentXform.GridUid == parent || parentXform.MapUid == parent)
                    break;

                parent = parentXform.ParentUid;
            }
        }

        return false;
    }
}
