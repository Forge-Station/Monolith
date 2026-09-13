using Content.Server.Station.Events;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Utility;

namespace Content.Server._Starlight.Shadekin;

public sealed class StationTheDarkSystem : EntitySystem
{
    [Dependency] private readonly MapLoaderSystem _loader = default!;

    private readonly ResPath _map = new("/Maps/_HL/TheDark.yml");
    private EntityUid? _thedark;

    public override void Initialize()
    {
        SubscribeLocalEvent<StationTheDarkComponent, StationPostInitEvent>(OnStationStartup);
    }

    private void OnStationStartup(EntityUid uid, StationTheDarkComponent component, StationPostInitEvent args)
    {
        if (_thedark is not null)
            return;

        var opts = DeserializationOptions.Default with { InitializeMaps = true };
        if (_loader.TryLoadMap(_map, out var map, out _, opts))
            _thedark = map;
    }
}

[RegisterComponent]
public sealed partial class StationTheDarkComponent : Component { }
