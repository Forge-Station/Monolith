using Content.Shared._WF.Roles;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Access.Systems;
using Content.Shared.Mind;
using Content.Shared.StationRecords;
using Content.Server.StationRecords.Systems;
using Robust.Shared.Player;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFRoundSetupSystem
{
    /// <summary>Applies the chosen crew title to the spawned identity while retaining its real job and access.</summary>
    private void ApplyCrewTitle(Lobby lobby, Member member, ICommonSession session)
    {
        if (lobby.Scenario == WFRoundScenario.FactionShip)
            return;
        var title = member.Title;
        if (title.Length == 0 && member.RoleId is { } role && lobby.Roles.TryGetValue(role, out var selected))
            title = selected.Name;
        if (title.Length == 0 || member.Job == null || session.AttachedEntity is not { } body)
            return;
        if (EntityManager.System<SharedMindSystem>().TryGetMind(body, out var mind, out _))
        {
            var custom = EnsureComp<CustomJobTitleComponent>(mind);
            custom.Job = member.Job;
            custom.Title = title;
        }
        var cards = EntityManager.System<SharedIdCardSystem>();
        if (!cards.TryFindIdCard(body, out var card))
            return;
        cards.TryChangeJobTitle(card, title);
        var records = EntityManager.System<StationRecordsSystem>();
        if (TryComp<StationRecordKeyStorageComponent>(card, out var storage) && storage.Key is { } key &&
            records.TryGetRecord<GeneralStationRecord>(key, out var record))
        {
            record.JobTitle = title;
            records.Synchronize(key);
        }
    }
}
