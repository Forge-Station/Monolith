using Content.Shared.Access.Components;
using Content.Shared.Silicons.StationAi;
using Content.Shared.StationRecords;

namespace Content.Shared._WF.ShipAccess;

/// <summary>
/// A ship's own AI opens its doors. The AI's core carries a record key naming the grid it is anchored on, which the
/// server adds to every keyed reader on that ship, so a locked ship admits the AI it carries and no other.
/// </summary>
public sealed partial class WFShipAiAccessSystem : EntitySystem
{
    /// <summary>The record id of a ship AI's key. Real crew records count up from zero.</summary>
    public const uint AiKeyId = uint.MaxValue;

    [Dependency] private SharedStationAiSystem _stationAi = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationAiHeldComponent, GetAdditionalAccessEvent>(OnGetAdditionalAccess);
    }

    /// <summary>The key a ship's AI carries for its doors.</summary>
    public static StationRecordKey AiKey(EntityUid grid)
    {
        return new StationRecordKey(AiKeyId, grid);
    }

    /// <summary>The AI searches its core for access, where its ship key is kept.</summary>
    private void OnGetAdditionalAccess(Entity<StationAiHeldComponent> ent, ref GetAdditionalAccessEvent args)
    {
        if (_stationAi.TryGetCore(ent.Owner, out var core))
            args.Entities.Add(core.Owner);
    }
}
