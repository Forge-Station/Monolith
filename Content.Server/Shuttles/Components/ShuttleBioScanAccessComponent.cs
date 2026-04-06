using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.Shuttles.Components;

[RegisterComponent]
public sealed partial class ShuttleBioScanAccessComponent : Component
{
    [DataField]
    public List<VesselClass> AllowedClasses = new();

    [DataField]
    public List<string> AllowedCompanies = new();

    [DataField]
    public List<ProtoId<SpeciesPrototype>> AllowedSpecies = new();

    [DataField]
    public List<string> AllowedTags = new();

    [DataField]
    public List<string> ThreatTags = new();

    [DataField]
    public List<ProtoId<NpcFactionPrototype>> ThreatFactions = new();

    [DataField]
    public List<ProtoId<SpeciesPrototype>> ThreatSpecies = new();

    [DataField]
    public List<EntProtoId> ThreatEntityPrototypes = new();
}
