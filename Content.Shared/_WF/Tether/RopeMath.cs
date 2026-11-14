using System.Numerics;

namespace Content.Shared._WF.Tether;

/// <summary>
/// Pure maths for the one-sided rope spring. A rope is slack below its rest length and only ever
/// pulls its ends together; nothing here may produce a pushing (negative) impulse.
/// </summary>
public static class RopeMath
{
    /// <summary>Shortest rest length any rope may be reeled to, in metres.</summary>
    public const float MinLength = 0.5f;

    /// <summary>Metres of extension past the rest length. Negative or zero means slack.</summary>
    public static float Extension(float distance, float length)
    {
        if (!float.IsFinite(distance) || !float.IsFinite(length))
            return 0f;

        return distance - MathF.Max(length, MinLength);
    }

    /// <summary>
    /// Extension as a fraction of the hard limit's travel, clamped to [0, 2]. 1 means the rope is
    /// on its inextensible limit.
    /// </summary>
    public static float Strain(float distance, float length, float maxStretch)
    {
        var travel = MathF.Max(length, MinLength) * MathF.Max(maxStretch, 0.001f);
        if (!float.IsFinite(travel) || travel <= 0f)
            return 0f;

        var strain = Extension(distance, length) / travel;
        return float.IsFinite(strain) ? Math.Clamp(strain, 0f, 2f) : 0f;
    }

    /// <summary>
    /// Effective mass of the pair along the rope, including each body's rotation about its anchor
    /// arm. Inverse masses and inertias are the physics component's; the arms are world-space
    /// offsets from each centre of mass to its anchor.
    /// </summary>
    public static float EffectiveMass(
        Vector2 direction,
        Vector2 armA,
        Vector2 armB,
        float invMassA,
        float invMassB,
        float invInertiaA,
        float invInertiaB)
    {
        var crossA = armA.X * direction.Y - armA.Y * direction.X;
        var crossB = armB.X * direction.Y - armB.Y * direction.X;
        var inverse = invMassA + invMassB + invInertiaA * crossA * crossA + invInertiaB * crossB * crossB;
        if (!float.IsFinite(inverse) || inverse <= 0f)
            return 0f;

        return 1f / inverse;
    }

    /// <summary>
    /// Impulse magnitude, in N*s, pulling the two anchors together over one substep. Positive
    /// separating speed means the ends are moving apart. The result is clamped so the impulse can
    /// never reverse the relative motion past the rest length, which keeps a light body on a stiff
    /// rope stable at any step size.
    /// </summary>
    public static float SpringImpulse(
        float extension,
        float length,
        float separatingSpeed,
        float effectiveMass,
        float stiffnessPerMetre,
        float dampingRatio,
        float maxStretch,
        float frameTime)
    {
        if (!float.IsFinite(extension) || extension <= 0f ||
            !float.IsFinite(effectiveMass) || effectiveMass <= 0f ||
            !float.IsFinite(separatingSpeed) ||
            !float.IsFinite(stiffnessPerMetre) || stiffnessPerMetre <= 0f ||
            !float.IsFinite(dampingRatio) || dampingRatio < 0f ||
            !float.IsFinite(frameTime) || frameTime <= 0f)
            return 0f;

        var rest = MathF.Max(length, MinLength);
        // Past the hard limit the joint takes over, so the spring never grows without bound.
        var travel = rest * MathF.Max(maxStretch, 0.001f);
        var springExtension = MathF.Min(extension, travel);
        var stiffness = stiffnessPerMetre / rest;
        var damping = 2f * dampingRatio * MathF.Sqrt(stiffness * effectiveMass);
        // Damping resists separation only. A closing rope must never be pulled shut.
        var force = stiffness * springExtension + damping * MathF.Max(0f, separatingSpeed);
        if (!float.IsFinite(force) || force <= 0f)
            return 0f;

        var impulse = force * frameTime;
        // Motion already closing the gap counts against the budget, so the spring never adds closing energy.
        var ceiling = effectiveMass * (separatingSpeed + extension / frameTime);
        if (!float.IsFinite(ceiling) || ceiling <= 0f)
            return 0f;

        return MathF.Min(impulse, ceiling);
    }
}
