using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.SectorControl;

/// <summary>A side that can hold sector cells, drawn on the sector map in its colour.</summary>
[Prototype("wfSectorFaction")]
public sealed partial class WFSectorFactionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The faction's name in notices, the legend and the admin command.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>The colour its cells are filled and bordered in.</summary>
    [DataField]
    public Color Color = Color.White;

    /// <summary>The company it fights as: whose standing applies and whose ships are its own.</summary>
    [DataField]
    public ProtoId<CompanyPrototype>? Company;

    /// <summary>Whether a station in a cell it holds is cut off from NPC freight and traders.</summary>
    [DataField]
    public bool Blockades;

    /// <summary>Companies whose ships it leaves alone: never counted as present in a dispute or retake, never told they are in its space.</summary>
    [DataField]
    public List<ProtoId<CompanyPrototype>> NeutralCompanies = new();

    /// <summary>Station ids, as a station grid's BecomesStation id, it leaves alone, along with the ships docked to them.</summary>
    [DataField]
    public List<string> NeutralStations = new();

    /// <summary>The notice a ship gets on entering its space, in place of the default; takes $faction, $callsign and $ship.</summary>
    [DataField]
    public LocId? EnteredNotice;

    /// <summary>The notice a ship gets on leaving its space, in place of the default; takes the same arguments.</summary>
    [DataField]
    public LocId? LeftNotice;
}
