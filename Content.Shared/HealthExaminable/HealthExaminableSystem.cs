using Content.Shared._Onyx.Wounds; // WOLFGATE(Wolfmed): GUARD F
using Content.Shared._WF.Wolfmed.Examine; // WOLFGATE(Wolfmed): LOOK
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Shared.HealthExaminable;

public sealed partial class HealthExaminableSystem : EntitySystem
{
    [Dependency] private ExamineSystemShared _examineSystem = default!;
    [Dependency] private WolfmedVisualInspectionSystem _look = default!; // WOLFGATE(Wolfmed): LOOK
    [Dependency] private Robust.Shared.Network.INetManager _net = default!; // WOLFGATE(Wolfmed): LOOK

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HealthExaminableComponent, GetVerbsEvent<ExamineVerb>>(OnGetExamineVerbs);
    }

    private void OnGetExamineVerbs(EntityUid uid, HealthExaminableComponent component, GetVerbsEvent<ExamineVerb> args)
    {
        if (!TryComp<DamageableComponent>(uid, out var damage))
            return;

        var detailsRange = _examineSystem.IsInDetailsRange(args.User, uid);
        var look = HasComp<WoundHostComponent>(uid); // WOLFGATE(Wolfmed): LOOK, a body can be looked over from across the room; only the detail changes.

        var verb = new ExamineVerb()
        {
            Act = () =>
            {
                // WOLFGATE(Wolfmed) START: the server builds a wound host's look.
                // The verb is predicted, so the client ran this too, from its own partial copy of the wounds, and that
                // tooltip (missing the bleed, missing wounds) could be the one left on screen.
                if (look && _net.IsClient)
                    return;
                // WOLFGATE END

                var markup = CreateMarkup(uid, args.User, component, damage, detailsRange); // WOLFGATE(Wolfmed): GUARD F, examiner param for self-vs-other pain visibility; LOOK, examine range
                _examineSystem.SendExamineTooltip(args.User, uid, markup, false, false);
            },
            Text = Loc.GetString("health-examinable-verb-text"),
            Category = VerbCategory.Examine,
            Disabled = !detailsRange && !look, // WOLFGATE(Wolfmed): LOOK
            Message = detailsRange || look ? null : Loc.GetString("health-examinable-verb-disabled"), // WOLFGATE(Wolfmed): LOOK
            Icon = new SpriteSpecifier.Texture(new ("/Textures/Interface/VerbIcons/rejuvenate.svg.192dpi.png"))
        };

        args.Verbs.Add(verb);
    }

    public FormattedMessage CreateMarkup(EntityUid uid, EntityUid examiner, HealthExaminableComponent component, DamageableComponent damage, bool detailed = true) // WOLFGATE(Wolfmed): GUARD F; LOOK adds detailed
    {
        var msg = new FormattedMessage();

        var first = true;
        // WOLFGATE(Wolfmed) START: GUARD F, legacy threshold text is for non-wound-hosts only.
        // The body is left un-reindented to keep the upstream diff minimal.
        if (!HasComp<WoundHostComponent>(uid))
        {
        // WOLFGATE END
        foreach (var type in component.ExaminableTypes)
        {
            if (!damage.Damage.DamageDict.TryGetValue(type, out var dmg))
                continue;

            if (dmg == FixedPoint2.Zero)
                continue;

            FixedPoint2 closest = FixedPoint2.Zero;

            string chosenLocStr = string.Empty;
            foreach (var threshold in component.Thresholds)
            {
                var str = $"health-examinable-{component.LocPrefix}-{type}-{threshold}";
                var tempLocStr = Loc.GetString($"health-examinable-{component.LocPrefix}-{type}-{threshold}", ("target", Identity.Entity(uid, EntityManager)));

                // i.e., this string doesn't exist, because theres nothing for that threshold
                if (tempLocStr == str)
                    continue;

                if (dmg > threshold && threshold > closest)
                {
                    chosenLocStr = tempLocStr;
                    closest = threshold;
                }
            }

            if (closest == FixedPoint2.Zero)
                continue;

            if (!first)
            {
                msg.PushNewline();
            }
            else
            {
                first = false;
            }
            msg.AddMarkupOrThrow(chosenLocStr);
        }

        if (msg.IsEmpty)
        {
            msg.AddMarkupOrThrow(Loc.GetString($"health-examinable-{component.LocPrefix}-none"));
        }
        // WOLFGATE(Wolfmed) START: GUARD F, a wound host gets a visual inspection instead (P2-D20/D2).
        // LOOK replaces Onyx's AddPartStatusMarkup readout with it.
        }
        else
            _look.AddLookMarkup(uid, examiner, msg, detailed);
        // WOLFGATE END

        // Anything else want to add on to this?
        RaiseLocalEvent(uid, new HealthBeingExaminedEvent(msg), true);

        return msg;
    }
}

/// <summary>
///     A class raised on an entity whose health is being examined
///     in order to add special text that is not handled by the
///     damage thresholds.
/// </summary>
public sealed class HealthBeingExaminedEvent
{
    public FormattedMessage Message;

    public HealthBeingExaminedEvent(FormattedMessage message)
    {
        Message = message;
    }
}
