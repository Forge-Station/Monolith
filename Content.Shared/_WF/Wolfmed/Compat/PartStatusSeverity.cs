namespace Content.Shared._WF.Wolfmed.Compat;

/// <summary>Severity classification from Onyx's Targeting PartStatusSystem, without the Targeting-doll types D10 excludes.</summary>
public static class PartStatusSystem
{
    /// <summary>Buckets a part's total damage the way Onyx's part-status readout does.</summary>
    public static PartDamageSeverity GetSeverity(float damage) => damage switch
    {
        <= 0f => PartDamageSeverity.None,
        < 15f => PartDamageSeverity.Minor,
        < 40f => PartDamageSeverity.Moderate,
        < 70f => PartDamageSeverity.Severe,
        _ => PartDamageSeverity.Critical,
    };
}

/// <summary>How badly a body part is damaged, for examine text and colour.</summary>
public enum PartDamageSeverity : byte
{
    None,
    Minor,
    Moderate,
    Severe,
    Critical,
}
