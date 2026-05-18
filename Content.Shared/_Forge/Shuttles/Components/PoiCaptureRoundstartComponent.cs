using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Shuttles.Components;

/// <summary>
/// Marks a POI grid as owned by a faction/company from round start.
/// Applied on <see cref="MapInitEvent"/>; shows the IFF-colored capture zone on radar.
/// </summary>
[RegisterComponent]
public sealed partial class PoiCaptureRoundstartComponent : Component
{
    /// <summary>Company that owns this POI at round start (also used for zone color).</summary>
    [DataField(required: true)]
    public ProtoId<CompanyPrototype> OwnerCompanyId;

    /// <summary>Optional shuttle faction id for capture logic; "None" to skip.</summary>
    [DataField]
    public string OwnerFactionId = "None";

    /// <summary>Capture zone radius in tiles. Clamped to 1–2048. Zero uses the global CVar default.</summary>
    [DataField]
    public float ZoneRadius;

    /// <summary>When true, IFF grid color is synced from <see cref="OwnerCompanyId"/>.</summary>
    [DataField]
    public bool SyncIffColor = true;
}
