using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetSignals;

/// <summary>On a sector body: the vessels, sites and people on its world, so radars in space and on every layer can show them.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFPlanetSignalsComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public List<WFPlanetSignal> Signals = new();

    /// <summary>How many player characters are on the world, living or not; only those with a medical tracker are named.</summary>
    [ViewVariables, AutoNetworkedField]
    public int People;
}

/// <summary>
/// One thing on a world. A site's name is its Fluent id, empty until the site is identified; a vessel's or a tracked
/// person's is their own. <paramref name="Expedition"/> is the grid of the ship running an expedition on a site;
/// <paramref name="Condition"/> is a tracked person's.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct WFPlanetSignal(
    WFPlanetSignalKind Kind,
    string Name,
    Vector2i Position,
    NetEntity Map,
    WFPlanetSignalLayer Layer,
    NetEntity? Expedition,
    WFPlanetSignalCondition Condition = WFPlanetSignalCondition.None);

/// <summary>A vessel, a site, or a person whose medical tracker gives them away.</summary>
public enum WFPlanetSignalKind : byte { Vessel, Site, Person }

/// <summary>Which layer of the world a signal is on.</summary>
public enum WFPlanetSignalLayer : byte { Ground, Air, Orbit, Underground }

/// <summary>How a tracked person is doing.</summary>
public enum WFPlanetSignalCondition : byte { None, Alive, Critical, Dead }
