namespace Content.Shared.Damage;

public sealed partial class DamageSpecifier
{
    /// <summary>Onyx-compatible deep copy; Wolfgate only has a copy constructor.</summary>
    public DamageSpecifier Clone() => new(this);

    /// <summary>Copy containing only entries with a value above zero.</summary>
    public static DamageSpecifier GetPositive(DamageSpecifier damageSpec)
    {
        DamageSpecifier newDamage = new(damageSpec);
        newDamage.DamageDict.Clear();

        foreach (var (key, value) in damageSpec.DamageDict)
        {
            if (value > 0)
                newDamage.DamageDict[key] = value;
        }

        return newDamage;
    }

    /// <summary>Copy containing only entries with a value below zero.</summary>
    public static DamageSpecifier GetNegative(DamageSpecifier damageSpec)
    {
        DamageSpecifier newDamage = new(damageSpec);
        newDamage.DamageDict.Clear();

        foreach (var (key, value) in damageSpec.DamageDict)
        {
            if (value < 0)
                newDamage.DamageDict[key] = value;
        }

        return newDamage;
    }
}
