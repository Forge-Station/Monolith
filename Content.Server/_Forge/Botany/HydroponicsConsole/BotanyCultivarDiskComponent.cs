using Content.Server.Botany;

namespace Content.Server._Forge.Botany.HydroponicsConsole;

/// <summary>
/// Portable copy of a named cultivar. Insert into a hydroponics console to import.
/// </summary>
[RegisterComponent]
public sealed partial class BotanyCultivarDiskComponent : Component
{
    [DataField]
    public string LineName = string.Empty;

    [DataField]
    public SeedData? Seed;
}
