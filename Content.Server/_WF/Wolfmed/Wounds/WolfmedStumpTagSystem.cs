using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>
/// Tags each dismemberment wound with the part it is the stump of (<see cref="WolfmedStumpComponent"/>), off the
/// amputation event, so the artery overlay can tell a neck from a shoulder on the same torso, and closes the stump
/// when that part, or a replacement for it, is attached again.
/// </summary>
public sealed class WolfmedStumpTagSystem : EntitySystem
{
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private SharedBodySystem _body = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedPartAmputatedEvent>(OnAmputated);
    }

    private void OnAmputated(ref WolfmedPartAmputatedEvent args)
    {
        if (!TryComp(args.Part, out BodyPartComponent? part) || !TryComp(args.Body, out WoundHostComponent? host))
            return;

        // The stump wound is a separate instance per amputation and the newest untagged one on the parent is this one.
        EntityUid? newest = null;
        foreach (var wound in _wounds.GetWounds(args.Parent))
        {
            if (wound.Comp.Prototype == host.DismembermentWound && !HasComp<WolfmedStumpComponent>(wound))
                newest = wound;
        }

        if (newest is not { } stump)
            return;

        var tag = EnsureComp<WolfmedStumpComponent>(stump);
        tag.PartType = part.PartType;
        tag.Symmetry = part.Symmetry;
    }

    /// <summary>
    /// Playtest 5: "the tissue rupture won't go away after sorting it". A stump wound never heals on its own (the
    /// bleed can be stopped, the wound stayed for ever), and nothing closed it when the limb went back on. The part
    /// now attached, or a replacement for it, takes the stump it matches off its parent. Returns how many closed.
    /// </summary>
    public int CloseStumps(EntityUid body, EntityUid part)
    {
        if (!TryComp(part, out BodyPartComponent? attached) || _body.GetParentPartOrNull(part) is not { } parent)
            return 0;

        var closed = 0;
        foreach (var wound in _wounds.GetWounds(parent).ToArray())
        {
            if (!TryComp(wound, out WolfmedStumpComponent? stump) ||
                stump.PartType != attached.PartType || stump.Symmetry != attached.Symmetry)
                continue;

            if (_wounds.RemoveWound(wound.Owner))
                closed++;
        }

        return closed;
    }
}
