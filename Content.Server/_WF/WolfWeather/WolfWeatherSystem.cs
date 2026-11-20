using Content.Shared._WF.WolfWeather;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.WolfWeather;

/// <summary>Starts weather with a random seed and clears it when its time runs out.</summary>
public sealed partial class WolfWeatherSystem : SharedWolfWeatherSystem
{
    [Dependency] private IRobustRandom _random = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextCheck;

    public void SetWeather(EntityUid map, ProtoId<WolfWeatherPrototype>? weather, TimeSpan? duration)
    {
        SetWeather(map, weather, duration, _random.Next(1, 1000));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = Timing.CurTime;
        if (now < _nextCheck)
            return;

        _nextCheck = now + CheckInterval;

        var query = EntityQueryEnumerator<WolfWeatherMapComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            ClearExpired((uid, comp), now);
        }
    }
}
