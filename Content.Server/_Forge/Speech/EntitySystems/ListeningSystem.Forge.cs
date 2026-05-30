using Content.Shared.Silicons.StationAi;

namespace Content.Server.Speech.EntitySystems;

public sealed partial class ListeningSystem
{
    [Dependency] private SharedStationAiSystem _stationAi = default!;

    private EntityUid ResolveAudioSource(EntityUid source)
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
