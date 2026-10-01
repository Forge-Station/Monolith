using System.Linq;
using Content.Shared._Onyx.Targeting;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage; // WOLFGATE(Wolfmed): DamageableComponent lives here in Wolfgate, not in .Damage.Components.
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE(Wolfmed): D12 damage facade and the PartStatusSystem severity shim.
using Content.Shared.Damage.Prototypes;
using Content.Shared.IdentityManagement;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.HealthExaminable;

// WOLFGATE(Wolfmed): LOOK: the health examine no longer routes here. Onyx's readout lists damage types, wound
// states and severities, which is analyzer information rather than something an examiner can see, so
// HealthExaminableSystem.CreateMarkup calls WolfmedVisualInspectionSystem instead. The readout below is
// kept verbatim for a fork that wants it back; the analyzer's wounds tab carries the same data in-game.
public sealed partial class HealthExaminableSystem
{
    private static readonly ProtoId<WoundPrototype> SurgicalIncision = "SurgicalIncisionWound";

    [Dependency] private WolfmedDamageableSystem _damageable = default!; // WOLFGATE(Wolfmed): D12, Onyx-shaped damage API.
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private void AddPartStatusMarkup(EntityUid examined, EntityUid examiner, FormattedMessage message)
    {
        var parts = _body.GetBodyChildren(examined)
            .OrderBy(part => PartOrder(part.Component.PartType))
            .ThenBy(part => part.Component.Symmetry)
            .ToList();
        if (parts.Count == 0)
            return;

        if (!message.IsEmpty)
            message.PushNewline();
        message.AddMarkupOrThrow(Loc.GetString(examined == examiner
                ? "health-examinable-part-title-self"
                : "health-examinable-part-title-other",
            ("entity", Identity.Name(examined, EntityManager))));

        foreach (var (part, _) in parts)
        {
            var details = new List<string>();
            var injuries = new List<string>();
            var totalDamage = 0f;
            if (TryComp(part, out DamageableComponent? damageable))
            {
                foreach (var (type, amount) in _damageable.GetPositiveDamage((part, damageable)).DamageDict
                             .OrderBy(entry => entry.Key)) // WOLFGATE(Wolfmed): DamageSpecifier.DamageDict is keyed by string here, not ProtoId<DamageTypePrototype>.
                {
                    if (!_prototypes.TryIndex<DamageTypePrototype>(type, out var damageType))
                        continue;

                    totalDamage += amount.Float();
                    // WOLFGATE(Wolfmed): string key, so no .Id — see the OrderBy above.
                    var injury = Loc.GetString($"health-examinable-part-damage-{type.ToLowerInvariant()}");
                    injuries.Add(injury.StartsWith("health-examinable-part-damage-")
                        ? damageType.LocalizedName
                        : injury);
                }
            }

            if (injuries.Count > 0)
                details.Add(Loc.GetString("health-examinable-part-injuries", ("types", string.Join(", ", injuries))));

            var woundStates = new Dictionary<WoundState, int>();
            var bleeding = false;
            var woundObservations = new Dictionary<LocId, int>();
            var scars = 0;
            var openIncisions = 0;
            foreach (var wound in _wounds.GetWounds(part))
            {
                if (HasComp<WoundScarComponent>(wound))
                    scars++;

                if (!_prototypes.TryIndex(wound.Comp.Prototype, out var prototype) ||
                    prototype.Visibility != WoundVisibility.Visible)
                    continue;

                if (wound.Comp.State is WoundState.Healed or WoundState.Scarred)
                    continue;

                bleeding |= CompOrNull<WoundBleedingComponent>(wound)?.CurrentRate > 0f;
                if (wound.Comp.Prototype == SurgicalIncision)
                {
                    if (wound.Comp.State == WoundState.Open)
                        openIncisions++;
                    continue;
                }

                woundStates[wound.Comp.State] = woundStates.GetValueOrDefault(wound.Comp.State) + 1;
                if (prototype.GetStageDefinition(wound.Comp.Severity)?.ExamineDescription is { } observation)
                    woundObservations[observation] = woundObservations.GetValueOrDefault(observation) + 1;
            }

            foreach (var state in new[] { WoundState.Stabilized, WoundState.Closed })
            {
                if (woundStates.TryGetValue(state, out var count))
                    details.Add(Loc.GetString($"health-examinable-part-wound-{state.ToString().ToLowerInvariant()}",
                        ("count", count)));
            }

            if (openIncisions > 0)
                details.Add(Loc.GetString("health-examinable-part-incision-open", ("count", openIncisions)));

            if (bleeding)
                details.Add(Loc.GetString("health-examinable-part-bleeding"));
            foreach (var (observation, count) in woundObservations)
            {
                var description = Loc.GetString(observation);
                details.Add(count > 1 ? $"{description} ×{count}" : description);
            }
            if (scars > 0)
                details.Add(Loc.GetString("health-examinable-part-scars", ("count", scars)));

            var severity = PartStatusSystem.GetSeverity(totalDamage).ToString().ToLowerInvariant();
            var painLevel = examined == examiner ? GetPainLevel(part) : null;
            var summary = Loc.GetString(painLevel == null
                    ? "health-examinable-part-summary"
                    : "health-examinable-part-summary-pain",
                ("part", Name(part)),
                ("severity", Loc.GetString($"health-examinable-part-severity-{severity}")),
                ("pain", painLevel == null ? string.Empty : Loc.GetString($"health-examinable-pain-{painLevel}")));
            var detail = details.Count == 0 ? string.Empty : string.Join(" · ", details);

            message.AddMarkupOrThrow($"[partstatus summary=\"{FormattedMessage.EscapeStringParameter(summary)}\" severity=\"{severity}\" details=\"{FormattedMessage.EscapeStringParameter(detail)}\" /]");
            message.PushNewline();
            message.AddMarkupOrThrow(Loc.GetString(detail.Length == 0
                    ? "health-examinable-part-chat-line"
                    : "health-examinable-part-chat-line-details",
                ("summary", summary),
                ("details", detail)));
            message.AddMarkupOrThrow("[partstatusend /]");
        }
    }

    private static int PartOrder(BodyPartType type) => type switch
    {
        BodyPartType.Head => 0,
        BodyPartType.Torso => 1, // WOLFGATE(Wolfmed): D9: Chest and Groin folded to Torso; Wolfgate's enum has neither.
        BodyPartType.Arm => 3,
        BodyPartType.Hand => 4,
        BodyPartType.Leg => 5,
        BodyPartType.Foot => 6,
        BodyPartType.Tail => 7,
        _ => 8,
    };
}
