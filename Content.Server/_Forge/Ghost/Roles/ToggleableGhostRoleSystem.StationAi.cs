using Content.Shared._Forge.Silicons.StationAi;

namespace Content.Server.Ghost.Roles;

public sealed partial class ToggleableGhostRoleSystem
{
    private bool IsStationAi(EntityUid uid)
    {
        return HasComp<StationAiPersonalityComponent>(uid);
    }
}
