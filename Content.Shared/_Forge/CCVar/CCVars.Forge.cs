using Robust.Shared.Configuration;

namespace Content.Shared._Forge.CCVar;

/// <summary>
/// Contains CVars used by Forge.
/// </summary>
[CVarDefs]
public sealed partial class ForgeCVars
{
    /// <summary>
    ///     Duration of a POI capture operation, in minutes.
    /// </summary>
    public static readonly CVarDef<float> PoiCaptureDurationMinutes =
        CVarDef.Create("forge.poi_capture.duration_minutes", 10f, CVar.SERVERONLY);
}
