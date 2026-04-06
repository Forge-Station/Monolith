// Forge-Change - BioScan

using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Content.Shared.Shuttles.Components;

namespace Content.Server.Shuttles.Components;

[RegisterComponent]
public sealed partial class ShuttleBioThreatMarkerComponent : Component
{
    public bool Marked;
    public string OriginalName = string.Empty;
    public Color OriginalIFFColor = IFFComponent.IFFColor;
}
