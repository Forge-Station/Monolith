using Content.Shared.Silicons.StationAi;

namespace Content.Server.Radio.EntitySystems;

public sealed partial class RadioDeviceSystem
{
    [Dependency] private SharedStationAiSystem _stationAi = default!;

    private EntityUid ResolveListenSource(EntityUid source)
    {
        if (HasComp<StationAiHeldComponent>(source) &&
            _stationAi.TryGetCore(source, out var core) &&
            core.Comp?.RemoteEntity is { } remoteEntity)
        {
            return remoteEntity;
        }

        return source;
    }
}
