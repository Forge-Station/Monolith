using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Hud;

/// <summary>
/// The data side of the synthetic HUD: the wound-to-readout table, and the escalation tiers the readout
/// draws at. Both sides of the wire need it, so it lives in shared.
/// </summary>
public sealed class WolfmedSyntheticHudLineSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly Dictionary<string, WolfmedSyntheticHudLinePrototype> _byWound = new();
    private readonly Dictionary<WolfmedSyntheticCondition, WolfmedSyntheticHudLinePrototype> _byCondition = new();
    private bool _built;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => _built = false);
    }

    /// <summary>The readout line for a wound at this severity, or the fallback row for anything unmapped.</summary>
    public bool TryGetWoundLine(
        string wound,
        FixedPoint2 severity,
        bool allowFallback,
        out WolfmedSyntheticHudLinePrototype line,
        out WolfmedSyntheticSeverity tag)
    {
        Build();
        tag = WolfmedSyntheticSeverity.Info;
        line = default!;
        if (!_byWound.TryGetValue(wound, out var found) &&
            (!allowFallback || !_byCondition.TryGetValue(WolfmedSyntheticCondition.Fallback, out found)))
            return false;

        line = found;
        if (severity < found.MinSeverity)
            return false;

        tag = found.EscalateAt > FixedPoint2.Zero && severity >= found.EscalateAt
            ? found.Escalated
            : found.Severity;
        return true;
    }

    /// <summary>The readout line for a body or part condition, if the data names one.</summary>
    public bool TryGetConditionLine(WolfmedSyntheticCondition condition, out WolfmedSyntheticHudLinePrototype line)
    {
        Build();
        if (_byCondition.TryGetValue(condition, out var found))
        {
            line = found;
            return true;
        }

        line = default!;
        return false;
    }

    /// <summary>Every mapped wound id. The coverage test reads it.</summary>
    public IReadOnlyCollection<string> MappedWounds
    {
        get
        {
            Build();
            return _byWound.Keys;
        }
    }

    private void Build()
    {
        if (_built)
            return;

        _built = true;
        _byWound.Clear();
        _byCondition.Clear();
        foreach (var line in _prototypes.EnumeratePrototypes<WolfmedSyntheticHudLinePrototype>())
        {
            if (line.Wound is { } wound)
                _byWound[wound.Id] = line;
            else if (line.Condition != WolfmedSyntheticCondition.None)
                _byCondition[line.Condition] = line;
        }
    }

    /// <summary>
    /// How hard the chassis is being pushed, 0 to 1: the worse of consciousness depth and lost integrity.
    /// </summary>
    public static float Strain(float depth, float integrity) =>
        Math.Max(Math.Clamp(depth, 0f, 1f), 1f - Math.Clamp(integrity, 0f, 1f));

    /// <summary>What the readout shows at this strain. Pure, so the bands are testable on their own.</summary>
    public static WolfmedSyntheticTier Tier(float depth, float integrity)
    {
        var strain = Strain(depth, integrity);
        return strain switch
        {
            < LightAt => WolfmedSyntheticTier.Idle,
            < ModerateAt => WolfmedSyntheticTier.Light,
            < HeavyAt => WolfmedSyntheticTier.Moderate,
            _ => WolfmedSyntheticTier.Heavy,
        };
    }

    /// <summary>The label a fault line names its part with. Anything else is the chassis as a whole.</summary>
    public static string PartKey(TargetBodyPart part) => part switch
    {
        TargetBodyPart.Head => "wolfmed-synthetic-part-head",
        TargetBodyPart.Torso => "wolfmed-synthetic-part-torso",
        TargetBodyPart.Groin => "wolfmed-synthetic-part-groin",
        TargetBodyPart.LeftArm => "wolfmed-synthetic-part-left-arm",
        TargetBodyPart.RightArm => "wolfmed-synthetic-part-right-arm",
        TargetBodyPart.LeftHand => "wolfmed-synthetic-part-left-hand",
        TargetBodyPart.RightHand => "wolfmed-synthetic-part-right-hand",
        TargetBodyPart.LeftLeg => "wolfmed-synthetic-part-left-leg",
        TargetBodyPart.RightLeg => "wolfmed-synthetic-part-right-leg",
        TargetBodyPart.LeftFoot => "wolfmed-synthetic-part-left-foot",
        TargetBodyPart.RightFoot => "wolfmed-synthetic-part-right-foot",
        _ => "wolfmed-synthetic-part-chassis",
    };

    /// <summary>Strain at which the SYSTEM block fades in.</summary>
    public const float LightAt = 0.08f;

    /// <summary>Strain at which the DIAGNOSTICS block and the edge tint appear.</summary>
    public const float ModerateAt = 0.25f;

    /// <summary>Strain at which the tint pulses, the text jitters and the banner runs.</summary>
    public const float HeavyAt = 0.55f;
}

/// <summary>How much of the readout is on screen.</summary>
public enum WolfmedSyntheticTier : byte
{
    /// <summary>A corner glyph and nothing else.</summary>
    Idle,

    /// <summary>The SYSTEM block.</summary>
    Light,

    /// <summary>The DIAGNOSTICS block and a still edge tint.</summary>
    Moderate,

    /// <summary>Pulsing tint, jitter, glitch slices and the integrity banner.</summary>
    Heavy,
}
