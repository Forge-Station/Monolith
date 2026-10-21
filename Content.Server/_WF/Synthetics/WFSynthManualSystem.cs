using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Synthetics;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Synthetics;

/// <summary>
/// Every synthetic spawns with "Synths and You" in its backpack: into the pack if it has one with room, else a hand,
/// else at its feet. Nothing prints or sells the book; this is the only way it gets into the round.
/// </summary>
public sealed class WFSynthManualSystem : SharedWFSyntheticsSystem
{
    public static readonly EntProtoId Book = "WFBookSynthsAndYou";

    private const string BackSlot = "back";

    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedStorageSystem _storage = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        // Directed at the mob: the ticker raises this both directed and broadcast.
        SubscribeLocalEvent<WoundHostComponent, PlayerSpawnCompleteEvent>(OnSpawnComplete);
    }

    private void OnSpawnComplete(EntityUid uid, WoundHostComponent comp, PlayerSpawnCompleteEvent args)
    {
        GiveManual(uid);
    }

    /// <summary>Gives a synthetic body its manual, once. False when the body is not synthetic or already has one.</summary>
    public bool GiveManual(EntityUid body)
    {
        if (!IsSynthetic(body) || HasComp<WFSynthManualGivenComponent>(body))
            return false;

        EnsureComp<WFSynthManualGivenComponent>(body);
        var book = Spawn(Book, Transform(body).Coordinates);

        if (_inventory.TryGetSlotEntity(body, BackSlot, out var pack) &&
            TryComp(pack, out StorageComponent? storage) &&
            _storage.Insert(pack.Value, book, out _, storageComp: storage, playSound: false))
            return true;

        // No pack, or no room in it: in a hand, and failing that it stays at the feet where it spawned.
        _hands.TryPickupAnyHand(body, book);
        return true;
    }
}

/// <summary>Marks a body that has been given its manual, so a repeat spawn event can't hand out a second.</summary>
[RegisterComponent]
public sealed partial class WFSynthManualGivenComponent : Component;
