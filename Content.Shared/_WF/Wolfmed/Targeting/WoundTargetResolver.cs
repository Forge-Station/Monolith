using Content.Shared._Shitmed.Targeting;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;

namespace Content.Shared._WF.Wolfmed.Targeting;

/// <summary>Resolves TargetBodyPart values to Wolfgate body-part entities for wound routing.</summary>
/// <remarks>
/// Replaces Onyx's TargetResolverSystem (D10). Built on Shitmed's SharedBodySystem targeting helpers, so
/// TargetBodyPart.Groin folds to the torso exactly as ConvertTargetBodyPart already does (D9). Phase 1
/// resolves the requested part exactly: Wolfgate's combat code rolls its own inaccuracy before calling in,
/// so Onyx's anatomical-odds scatter is deliberately not reproduced.
/// </remarks>
public sealed class WoundTargetResolver : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private IConfigurationManager _configuration = default!;

    /// <summary>Resolves the part the origin entity is currently aiming at.</summary>
    public bool TryResolve(EntityUid body, EntityUid origin, out EntityUid part)
    {
        part = default;
        if (!TryComp(origin, out TargetingComponent? targeting))
            return false;

        return TryResolve(body, targeting.Target, null, out part);
    }

    /// <summary>Resolves a requested target for a shooter; phase 1 resolves exactly, with no scatter.</summary>
    public bool TryResolve(EntityUid body, TargetBodyPart requested, EntityUid? shooter, out EntityUid part)
    {
        part = default;
        if (!_configuration.GetCVar(CCVars.TargetingEnabled) || !SharedTargetingSystem.IsSelectable(requested))
            return false;

        return TryResolveAvailable(body, requested, out part);
    }

    /// <summary>Resolves a single-bit target to a part, falling back hand to arm, foot to leg, else torso.</summary>
    public bool TryResolveAvailable(EntityUid body, TargetBodyPart target, out EntityUid part)
    {
        part = default;
        if (!SharedTargetingSystem.IsSelectable(target))
            return false;

        var (type, symmetry) = _body.ConvertTargetBodyPart(target);
        if (TryFind(body, type, symmetry, out part))
            return true;

        if (type == BodyPartType.Hand && TryFind(body, BodyPartType.Arm, symmetry, out part) ||
            type == BodyPartType.Foot && TryFind(body, BodyPartType.Leg, symmetry, out part))
            return true;

        return TryFind(body, BodyPartType.Torso, BodyPartSymmetry.None, out part);
    }

    /// <summary>Resolves a single-bit target to a part with no fallback.</summary>
    public bool TryResolveExact(EntityUid body, TargetBodyPart target, out EntityUid part)
    {
        part = default;
        if (!SharedTargetingSystem.IsSelectable(target))
            return false;

        var (type, symmetry) = _body.ConvertTargetBodyPart(target);
        return TryFind(body, type, symmetry, out part);
    }

    /// <summary>Whether the body has a part the target resolves to.</summary>
    public bool IsAvailable(EntityUid body, TargetBodyPart target) => TryResolveAvailable(body, target, out _);

    /// <summary>Every attached part matching any bit in the mask.</summary>
    public List<EntityUid> GetMatchingParts(EntityUid body, TargetBodyPart mask)
    {
        var parts = new List<EntityUid>();
        var seen = new HashSet<EntityUid>();
        foreach (var (part, component) in _body.GetBodyChildren(body))
        {
            if (_body.GetTargetBodyPart(component) is not { } target)
                continue;

            if ((mask & target) != 0 && seen.Add(part))
                parts.Add(part);
        }

        return parts;
    }

    /// <summary>Finds an attached part of the given type, ignoring symmetry for the unpaired types.</summary>
    private bool TryFind(EntityUid body, BodyPartType type, BodyPartSymmetry symmetry, out EntityUid part)
    {
        foreach (var candidate in _body.GetBodyChildrenOfType(body, type))
        {
            if (type is not BodyPartType.Torso and not BodyPartType.Head && candidate.Component.Symmetry != symmetry)
                continue;

            part = candidate.Id;
            return true;
        }

        part = default;
        return false;
    }
}
