using Content.Client._WF.Cockpit;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Shuttles;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;

namespace Content.Client._WF.Shuttles.UI;

public sealed partial class CollisionWarningBanner
{
    [Dependency] private IConfigurationManager _wfTcasConfiguration = default!;

    /// <summary>Shares the banner's assigned grid without depending on its visibility or animation polling.</summary>
    public WFCockpitTcasReading WfCockpitTcasReading()
    {
        var available = _grid is { } grid &&
            _entManager.TryGetComponent<MetaDataComponent>(grid, out var metadata) &&
            metadata.EntityLifeStage < EntityLifeStage.Terminating &&
            (metadata.Flags & MetaDataFlags.Detached) == 0 &&
            _entManager.HasComponent<MapGridComponent>(grid);
        var enabled = _wfTcasConfiguration.GetCVar(CollisionWarningCVars.Enabled) &&
            !_entManager.HasComponent<CollisionWarningDisabledComponent>(_grid);
        _entManager.TryGetComponent<CollisionWarningComponent>(_grid, out var warning);
        return WFCockpitTcasReading.From(available, enabled, warning);
    }
}
