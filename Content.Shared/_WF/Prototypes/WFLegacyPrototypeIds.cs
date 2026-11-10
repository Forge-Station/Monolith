using System.Diagnostics.CodeAnalysis;
using Content.Shared.Preferences;

namespace Content.Shared._WF.Prototypes;

/// <summary>Old ids of renamed Wolfgate prototypes that saved data can still hold, one table per prototype kind.</summary>
/// <remarks>Entity renames go in Resources/migration.yml instead.</remarks>
public static class WFLegacyPrototypeIds
{
    /// <summary>consentToggle ids stored in consent_toggle rows.</summary>
    public static readonly IReadOnlyDictionary<string, string> ConsentToggles = new Dictionary<string, string>
    {
        ["AnatomySurgery"] = "WFAnatomySurgery",
        ["UndergarmentStrip"] = "WFUndergarmentStrip",
    };

    /// <summary>genitalShape ids stored in the profile anatomy JSON and in character exports.</summary>
    public static readonly IReadOnlyDictionary<string, string> GenitalShapes = new Dictionary<string, string>
    {
        ["GenitalShapeBreastsPair"] = "WFGenitalShapeBreastsPair",
        ["GenitalShapeBreastsPairRound"] = "WFGenitalShapeBreastsPairRound",
        ["GenitalShapeBreastsQuad"] = "WFGenitalShapeBreastsQuad",
        ["GenitalShapeBreastsQuadLite"] = "WFGenitalShapeBreastsQuadLite",
        ["GenitalShapeBreastsQuadPlus"] = "WFGenitalShapeBreastsQuadPlus",
        ["GenitalShapeBreastsSextuple"] = "WFGenitalShapeBreastsSextuple",
        ["GenitalShapeBreastsUdders"] = "WFGenitalShapeBreastsUdders",
        ["GenitalShapePenisBarbKnot"] = "WFGenitalShapePenisBarbKnot",
        ["GenitalShapePenisFlared"] = "WFGenitalShapePenisFlared",
        ["GenitalShapePenisHemi"] = "WFGenitalShapePenisHemi",
        ["GenitalShapePenisHemiKnot"] = "WFGenitalShapePenisHemiKnot",
        ["GenitalShapePenisHuman"] = "WFGenitalShapePenisHuman",
        ["GenitalShapePenisKnotted"] = "WFGenitalShapePenisKnotted",
        ["GenitalShapePenisNondescript"] = "WFGenitalShapePenisNondescript",
        ["GenitalShapePenisTapered"] = "WFGenitalShapePenisTapered",
        ["GenitalShapePenisTentacle"] = "WFGenitalShapePenisTentacle",
        ["GenitalShapePenisTipKnotted"] = "WFGenitalShapePenisTipKnotted",
        ["GenitalShapeTesticlesPair"] = "WFGenitalShapeTesticlesPair",
        ["GenitalShapeVaginaDentate"] = "WFGenitalShapeVaginaDentate",
        ["GenitalShapeVaginaFurred"] = "WFGenitalShapeVaginaFurred",
        ["GenitalShapeVaginaHaired"] = "WFGenitalShapeVaginaHaired",
        ["GenitalShapeVaginaHuman"] = "WFGenitalShapeVaginaHuman",
        ["GenitalShapeVaginaOpen"] = "WFGenitalShapeVaginaOpen",
        ["GenitalShapeVaginaSlit"] = "WFGenitalShapeVaginaSlit",
        ["GenitalShapeVaginaSpade"] = "WFGenitalShapeVaginaSpade",
        ["GenitalShapeVaginaTentacle"] = "WFGenitalShapeVaginaTentacle",
    };

    /// <summary>hudTheme and uiTheme ids (they share one) stored in the interface.theme client CVar.</summary>
    public static readonly IReadOnlyDictionary<string, string> HudThemes = new Dictionary<string, string>
    {
        ["WolfgateRetroTheme"] = "WFRetroTheme",
        ["WolfgateTheme"] = "WFTheme",
    };

    /// <summary>loadout ids stored in profile_loadout rows and in character exports.</summary>
    public static readonly IReadOnlyDictionary<string, string> Loadouts = new Dictionary<string, string>
    {
        ["ContractorMosinLoadout"] = "WFContractorMosinLoadout",
    };

    /// <summary>shipAlertCode ids stored in ShipAlertComponent.Code on saved grids.</summary>
    public static readonly IReadOnlyDictionary<string, string> ShipAlertCodes = new Dictionary<string, string>
    {
        ["ShipCodeBlack"] = "WFShipCodeBlack",
        ["ShipCodeGreen"] = "WFShipCodeGreen",
        ["ShipCodeRed"] = "WFShipCodeRed",
        ["ShipCodeYellow"] = "WFShipCodeYellow",
    };

    /// <summary>species ids stored in profile rows and in character exports.</summary>
    public static readonly IReadOnlyDictionary<string, string> Species = new Dictionary<string, string>
    {
        ["Canine"] = "WFCanine",
        ["ProtoArachnid"] = "Protogen",
        ["ProtoAvali"] = "Protogen",
        ["ProtoDawi"] = "Protogen",
        ["ProtoDionae"] = "Protogen",
        ["ProtoFeline"] = "Protogen",
        ["ProtoHumie"] = "Protogen",
        ["ProtoKin"] = "Protogen",
        ["ProtoMoth"] = "Protogen",
        ["ProtoReptile"] = "Protogen",
        ["ProtoResomi"] = "Protogen",
        ["ProtoSlimePerson"] = "Protogen",
        ["ProtoThaven"] = "Protogen",
        ["ProtoVox"] = "Protogen",
        ["ProtoVulp"] = "Protogen",
    };

    /// <summary>Moves a profile's species and loadouts off renamed ids; run before it is validated.</summary>
    public static void ResolveProfile(HumanoidCharacterProfile profile)
    {
        profile.Species = Resolve(Species, profile.Species.Id);
        foreach (var role in profile.Loadouts.Values)
        {
            foreach (var loadouts in role.SelectedLoadouts.Values)
            {
                foreach (var loadout in loadouts)
                    loadout.Prototype = Resolve(Loadouts, loadout.Prototype.Id);
            }
        }
    }

    /// <summary>The current id for an old one from the table; any other id, or null, is returned unchanged.</summary>
    [return: NotNullIfNotNull(nameof(id))]
    public static string? Resolve(IReadOnlyDictionary<string, string> table, string? id)
    {
        return id != null && table.TryGetValue(id, out var current) ? current : id;
    }
}
