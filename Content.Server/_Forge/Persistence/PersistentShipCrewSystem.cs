using System.Collections.Generic;
using System.Linq;
using Content.Server.Preferences.Managers;
using Content.Shared._Forge.Persistence;
using Robust.Server.Player;

namespace Content.Server._Forge.Persistence;

public sealed class PersistentShipCrewSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IServerPreferencesManager _preferences = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PersistentShipCrewComponent, PersistentShipCrewAccessEvent>(OnAccessCheck);
    }

    private void OnAccessCheck(
        Entity<PersistentShipCrewComponent> entity,
        ref PersistentShipCrewAccessEvent args)
    {
        if (!_players.TryGetSessionByEntity(args.User, out var session))
            return;

        var preferences = _preferences.GetPreferencesOrNull(session.UserId);
        if (preferences == null)
            return;

        args.Allowed = entity.Comp.Members.Contains((
            session.UserId.UserId,
            preferences.SelectedCharacterIndex));
    }

    public void SetCrew(EntityUid shuttle, IEnumerable<HangarVesselCrewRecord> crew)
    {
        var component = EnsureComp<PersistentShipCrewComponent>(shuttle);
        component.Members = crew
            .Select(member => (member.PlayerUserId, member.CharacterSlot))
            .ToHashSet();
    }
}
