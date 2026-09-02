using Content.Server.Botany;

namespace Content.Server._Forge.Botany.HydroponicsConsole;

[RegisterComponent]
public sealed partial class HydroponicsConsoleComponent : Component
{
    public const int MaxJournalEntries = 24;

    [DataField]
    public float UpdateInterval = 1f;

    public TimeSpan NextUpdate;

    [DataField]
    public List<BotanyCultivarStored> Journal = new();
}

[DataDefinition]
public sealed partial class BotanyCultivarStored
{
    [DataField]
    public string LineName = string.Empty;

    [DataField]
    public SeedData? Seed;
}
