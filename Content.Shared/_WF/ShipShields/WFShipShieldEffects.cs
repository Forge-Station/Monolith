namespace Content.Shared._WF.ShipShields;

/// <summary>Pure sampling rules for shield health, cooling and impact waves.</summary>
public static class WFShipShieldEffects
{
    /// <summary>Duration of residual local impact heat.</summary>
    public const float HeatLifetime = 16f;
    /// <summary>Duration of an expanding impact ripple.</summary>
    public const float WaveLifetime = 4f;
    /// <summary>Ripple travel speed in tiles per second.</summary>
    public const float WaveSpeed = 22f;

    /// <summary>Returns full capacity bounded by overload and power demand limits.</summary>
    public static float EffectiveCapacity(float damageLimit, float maxDraw, float powerModifier, float damageExp)
    {
        if (!float.IsFinite(damageLimit) || damageLimit <= 0f)
            return 0f;
        if (!float.IsFinite(maxDraw) || maxDraw <= 0f || !float.IsFinite(powerModifier) || powerModifier <= 0f ||
            !float.IsFinite(damageExp) || damageExp <= 0f)
            return damageLimit;
        return (float) Math.Min(damageLimit, Math.Pow((double) maxDraw / powerModifier, 1d / damageExp));
    }

    /// <summary>Scales impact energy against full capacity, reaching full intensity at five percent.</summary>
    public static float ImpactStrength(float damage, float capacity)
    {
        if (!float.IsFinite(damage) || damage <= 0f || !float.IsFinite(capacity) || capacity <= 0f)
            return 0f;
        return Math.Clamp((float) Math.Sqrt((double) damage / capacity / 0.05d), 0.08f, 1f);
    }

    /// <summary>Scales impact footprint and ripple reach with impact energy.</summary>
    public static float ImpactRadiusScale(float strength) =>
        0.2f + 0.8f * Math.Clamp(float.IsFinite(strength) ? strength : 0f, 0f, 1f);

    /// <summary>Returns the default health indicator colour.</summary>
    public static Color HealthColor(float health) => HealthColor(health, new Color(0.12f, 0.72f, 1f));

    /// <summary>Blends the generator colour through amber to red as shield health falls.</summary>
    public static Color HealthColor(float health, Color generatorColor)
    {
        health = float.IsFinite(health) ? Math.Clamp(health, 0f, 1f) : 0f;
        return health > 0.45f
            ? Color.InterpolateBetween(new Color(1f, 0.53f, 0.12f), generatorColor.WithAlpha(1f), (health - 0.45f) / 0.55f)
            : Color.InterpolateBetween(new Color(1f, 0.05f, 0.08f), new Color(1f, 0.53f, 0.12f), health / 0.45f);
    }

    /// <summary>Returns local impact heat with seven-second cooling and a sixteen-second lifetime.</summary>
    public static float Heat(float distance, float age, float strength)
    {
        if (age < 0f || age >= HeatLifetime)
            return 0f;
        distance /= ImpactRadiusScale(strength);
        return strength * 0.65f * MathF.Exp(-age / 7f) * MathF.Exp(-distance * distance / 14f) *
            Math.Clamp((HeatLifetime - age) / 4f, 0f, 1f);
    }

    /// <summary>Briefly reveals nearby hexes before the traveling ripple leaves the impact.</summary>
    public static float Flash(float distance, float age, float strength)
    {
        if (age < 0f || age >= 0.65f)
            return 0f;
        distance /= ImpactRadiusScale(strength);
        var fade = 1f - age / 0.65f;
        return strength * fade * fade * MathF.Exp(-distance * distance / 10f);
    }

    /// <summary>Layers local red heat and impact flashes over the generator's health colour.</summary>
    public static (Color Tint, float Surface, float Hexes, Color SurfaceTint) Appearance(Color health, float heat, float wave, float flash, float integrity = 1f, float wake = 0f)
    {
        heat = Math.Clamp(heat, 0f, 1.5f);
        wave = Math.Clamp(wave, 0f, 1.5f);
        flash = Math.Clamp(flash, 0f, 1f);
        wake = Math.Clamp(wake, 0f, 1.5f);
        var damage = 1f - Math.Clamp(float.IsFinite(integrity) ? integrity : 0f, 0f, 1f);
        var hot = Math.Clamp(heat * 1.5f, 0f, 1f);
        var red = new Color(1f, 0.025f, 0.045f);
        var tint = Color.InterpolateBetween(health, red, Math.Clamp(hot + wake * 0.85f, 0f, 1f));
        var surfaceTint = tint;
        tint = Color.InterpolateBetween(tint, Color.White, flash);
        surfaceTint = Color.InterpolateBetween(surfaceTint, Color.White, flash);
        return (Color.FromSrgb(tint),
            0.06f + damage * 0.38f + flash * 1.25f + wave * 1.15f + heat * 0.55f + wake * 0.55f,
            0.38f + damage * 0.62f + flash * 3.2f + wave * 2.8f + heat * 1.2f + wake * 1.4f,
            Color.FromSrgb(surfaceTint));
    }

    /// <summary>Returns a broad brightness crest traveling up to eighty-eight tiles from the impact.</summary>
    public static float Wave(float distance, float age, float strength)
    {
        if (age < 0f || age >= WaveLifetime)
            return 0f;
        distance /= ImpactRadiusScale(strength);
        var ring = distance - age * WaveSpeed;
        return strength * MathF.Sqrt(1f - age / WaveLifetime) * MathF.Exp(-ring * ring / 2.4f);
    }

    /// <summary>Returns the wider red wake following the brightness crest.</summary>
    public static float WaveWake(float distance, float age, float strength)
    {
        if (age < 0f || age >= WaveLifetime)
            return 0f;
        distance /= ImpactRadiusScale(strength);
        var trailing = distance - MathF.Max(0f, age * WaveSpeed - 3f);
        return strength * 0.75f * MathF.Sqrt(1f - age / WaveLifetime) *
            MathF.Exp(-trailing * trailing / 9f);
    }
}