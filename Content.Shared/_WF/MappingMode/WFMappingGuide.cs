using Content.Shared.Guidebook;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.MappingMode;

/// <summary>The mapping server guide in the guidebook.</summary>
public static class WFMappingGuide
{
    /// <summary>The guide's root entry; its children are the other pages.</summary>
    public static readonly ProtoId<GuideEntryPrototype> Root = "WFMappingGuide";
}
