using Content.Server._WF.PlanetCargoHubs;
using Content.Shared.Examine;

namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    [Dependency] private WFPlanetCargoHubSystem _wfCargoHubs = default!;

    /// <summary>A trade crate bound for a planetside depot says which world the depot stands on.</summary>
    private void WfExamineCrateHub(EntityUid destination, ExaminedEvent ev)
    {
        if (_wfCargoHubs.CrateNote(destination) is { } note)
            ev.PushMarkup(note);
    }
}
