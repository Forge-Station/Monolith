using Content.Shared._WF.Strangers;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.ShipSquatters;

/// <summary>A squatter's mood: he sits about insisting, or he has been forced and is fighting.</summary>
public enum WFSquatterMood : byte
{
    Sitting,
    Angry,
}

/// <summary>
/// Someone who has moved into a landed ship: he sets up a bed, wanders the rooms making a mess, insists the ship is his
/// and turns violent only on those who force him.
/// </summary>
[RegisterComponent]
public sealed partial class WFShipSquatterComponent : Component
{
    /// <summary>The ship he squats in.</summary>
    [ViewVariables]
    public EntityUid Ship;

    /// <summary>His bedroll, where he sits when he has nowhere else to be.</summary>
    [ViewVariables]
    public EntityCoordinates Bed;

    [ViewVariables]
    public WFSquatterMood Mood;

    /// <summary>Those who forced him and are still at it, and when they last did.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Forcers = new();

    /// <summary>When an angry squatter with nobody left to fight calms down.</summary>
    [ViewVariables]
    public TimeSpan? CalmAt;

    /// <summary>Everything he has dropped about the ship: items and puddles.</summary>
    [ViewVariables]
    public List<EntityUid> Mess = new();

    /// <summary>All the mess he has made, graffiti included. He stops at <see cref="MessCap"/>.</summary>
    [ViewVariables]
    public int MessCount;

    [ViewVariables]
    public TimeSpan NextMess;

    [ViewVariables]
    public TimeSpan NextWander;

    [ViewVariables]
    public TimeSpan NextBark;

    [ViewVariables]
    public TimeSpan NextRoomScan;

    /// <summary>When he gives up on the leg he is walking.</summary>
    [ViewVariables]
    public TimeSpan? WanderUntil;

    /// <summary>Who he has barked at lately, and until when he leaves them be.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Barked = new();

    /// <summary>Who was close on the last look, so a newcomer gets a different line.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Near = new();

    /// <summary>The player he is talking with, and the node they are on.</summary>
    [ViewVariables]
    public EntityUid? Talking;

    [ViewVariables]
    public string? Node;

    /// <summary>His rooms: tiles joined to his bed with no wall or shut door between.</summary>
    [ViewVariables]
    public HashSet<Vector2i> Rooms = new();

    /// <summary>Tiles of doors in or next to his rooms, kept clear of mess.</summary>
    [ViewVariables]
    public HashSet<Vector2i> Doors = new();

    [DataField]
    public ProtoId<WFStrangerDialoguePrototype> Dialogue = "WFShipSquatterDialogue";

    /// <summary>Most mess he makes aboard one ship.</summary>
    [DataField]
    public int MessCap = 24;

    /// <summary>Seconds between messes, picked within the range.</summary>
    [DataField]
    public float MessMin = 45f;

    [DataField]
    public float MessMax = 120f;

    /// <summary>Tiles from his bed that his rooms reach.</summary>
    [DataField]
    public int RoomRadius = 12;

    /// <summary>Seconds he stands about between legs, picked within the range.</summary>
    [DataField]
    public float LingerMin = 25f;

    [DataField]
    public float LingerMax = 80f;

    /// <summary>Tiles round him that count as close for a bark.</summary>
    [DataField]
    public float BarkRange = 6f;

    /// <summary>Seconds he leaves the same person be after a bark.</summary>
    [DataField]
    public float BarkPerPerson = 45f;

    /// <summary>Seconds between any two barks.</summary>
    [DataField]
    public float BarkGap = 6f;

    /// <summary>Seconds with nobody forcing him before an angry squatter calms down.</summary>
    [DataField]
    public float CalmSeconds = 180f;

    /// <summary>Tiles beyond which a forcer is gone and he lets them be.</summary>
    [DataField]
    public float LeashRange = 35f;

    [DataField]
    public List<EntProtoId> BedItems = new() { "BedsheetBrown", "ClothingBackpackDuffel" };

    /// <summary>Litter he leaves about.</summary>
    [DataField]
    public List<EntProtoId> Litter = new()
    {
        "DrinkColaCanEmpty", "FoodPacketChipsTrash", "FoodPacketPistachioTrash", "FoodPacketRaisinsTrash",
        "FoodPacketCupRamenTrash", "FoodTinBeansTrash", "FoodTinMRETrash", "FoodPacketMRETrash", "FoodPlateTrash",
        "TrashBananaPeel", "FoodPacketSusTrash",
    };

    /// <summary>Puddles he leaves, as reagents and how much.</summary>
    [DataField]
    public List<ProtoId<ReagentPrototype>> Puddles = new() { "Vomit", "Vomit", "Beer" };

    [DataField]
    public float PuddleVolume = 5f;

    /// <summary>The spawner that sprays a graffiti decal.</summary>
    [DataField]
    public EntProtoId Graffiti = "DecalSpawnerGraffiti";

    [DataField]
    public List<LocId> BoardBarks = new();

    [DataField]
    public List<LocId> NearBarks = new();

    [DataField]
    public List<LocId> TalkedBarks = new();

    [DataField]
    public List<LocId> GiftBarks = new();

    [DataField]
    public List<LocId> AngryBarks = new();

    [DataField]
    public List<LocId> CalmBarks = new();
}

/// <summary>On a ship that has had a squatter, so it never gets a second.</summary>
[RegisterComponent]
public sealed partial class WFSquattedShipComponent : Component
{
    [ViewVariables]
    public EntityUid? Squatter;
}
