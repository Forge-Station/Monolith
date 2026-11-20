using Content.Shared.Access.Systems;
using Content.Shared.Damage;
using Content.Shared.DetailExaminable;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.HealthExaminable;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.IdentityManagement.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Enums;
using Robust.Shared.Utility;

namespace Content.Shared._WF.ExamineCard;

/// <summary>
/// Builds a character's examine card. The server sends it; the client builds one at once from what it already
/// knows, so the card shows without waiting for the server's.
/// </summary>
public abstract partial class SharedExamineCardSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedHumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private HealthExaminableSystem _health = default!;
    [Dependency] private ILocalizationManager _loc = default!;

    /// <summary>Fluent variables that hold item names or other text the patterns leave open.</summary>
    private static readonly string[] OpenVariables = { "items", "item", "part", "findings", "count", "name" };

    /// <summary>Slots shown on the card, in order. Pockets and hidden slots stay off it.</summary>
    public static readonly string[] Slots =
    {
        "head", "eyes", "mask", "ears", "neck", "jumpsuit", "outerClothing", "suitstorage", "back", "gloves",
        "belt", "id", "shoes",
    };

    /// <summary>
    /// Reads what an examiner can see of a character. From beyond details range that is the face, build, what
    /// they hold and a rough look at their wounds, but not their ID or clothes. A predicted card leaves out the
    /// health look and the chip patterns, which only the server has in full.
    /// </summary>
    public ExamineCardEvent BuildCard(EntityUid target, EntityUid examiner, bool detailed = true, bool predicted = false)
    {
        var card = new ExamineCardEvent
        {
            Target = GetNetEntity(target),
            Range = detailed ? ExamineCardRange.Detailed : ExamineCardRange.Distant,
            Self = examiner == target,
            State = CompOrNull<MobStateComponent>(target)?.CurrentState ?? MobState.Alive,
        };

        // Your own card too, so you see the disguise everyone else sees.
        var see = new SeeIdentityAttemptEvent();
        RaiseLocalEvent(target, see);
        var faceVisible = !see.Cancelled;

        var trueName = Name(target);
        if (faceVisible)
            card.Name = trueName;

        // The same rule as the description verb: only while the identity shows their real name.
        if (detailed && TryComp<DetailExaminableComponent>(target, out var description)
            && !string.IsNullOrWhiteSpace(description.Content)
            && Identity.Name(target, EntityManager) == trueName)
            card.Description = description.Content;

        if (TryComp<HumanoidAppearanceComponent>(target, out var appearance))
        {
            var gender = appearance.Gender switch
            {
                Gender.Female => Loc.GetString("identity-gender-feminine"),
                Gender.Male => Loc.GetString("identity-gender-masculine"),
                _ => Loc.GetString("identity-gender-person"),
            };
            card.Appearance = $"{_humanoid.GetAgeRepresentation(appearance.Species, appearance.Age)} {gender}";

            card.Species = _humanoid.GetSpeciesRepresentation(appearance.Species, appearance.CustomSpeciesName);
        }

        if (!detailed)
        {
            card.Stamp = ExamineCardStamp.TooFar;
        }
        else if (_idCard.TryFindIdCard(target, out var id))
        {
            card.IdName = string.IsNullOrWhiteSpace(id.Comp.FullName) ? null : id.Comp.FullName;
            card.IdJob = id.Comp.LocalizedJobTitle;
            card.JobIcon = id.Comp.JobIcon;
            card.Stamp = !faceVisible
                ? ExamineCardStamp.Unverified
                : string.Equals(card.IdName?.Trim(), trueName.Trim(), StringComparison.OrdinalIgnoreCase)
                    ? ExamineCardStamp.Match
                    : ExamineCardStamp.Mismatch;
        }
        else
        {
            card.Stamp = ExamineCardStamp.NoId;
        }

        foreach (var slot in Slots)
        {
            if (detailed && _inventory.TryGetSlotEntity(target, slot, out var item))
                card.Worn.Add(new ExamineCardSlot(slot, GetNetEntity(item.Value), Name(item.Value)));
        }

        if (TryComp<HandsComponent>(target, out var hands))
        {
            foreach (var hand in _hands.EnumerateHands(target, hands))
            {
                if (hand.HeldEntity is not { } held || HasComp<VirtualItemComponent>(held))
                    continue;

                var slot = $"hand-{hand.Location.ToString().ToLowerInvariant()}";
                card.Worn.Add(new ExamineCardSlot(slot, GetNetEntity(held), Name(held)));
            }
        }

        // The health verb allows a wound host's look from any range, with less detail; anything else needs to be close.
        card.HasHealth = TryComp<HealthExaminableComponent>(target, out var health)
                         && TryComp<DamageableComponent>(target, out var damage)
                         && (detailed || HasComp<WoundHostComponent>(target));
        if (predicted)
            return card;

        if (card.HasHealth)
            card.Health = _health.CreateMarkup(target, examiner, health!, Comp<DamageableComponent>(target), detailed);

        card.Patterns = BuildPatterns(target);
        return card;
    }

    /// <summary>
    /// Renders each chip rule the way its system would for this character, as the body and as the identity, with
    /// the open variables left as wildcards.
    /// </summary>
    public List<ExamineCardPattern> BuildPatterns(EntityUid target)
    {
        var patterns = new List<ExamineCardPattern>();
        var seen = new HashSet<string>();
        var subjects = new List<EntityUid> { target };
        if (Identity.Entity(target, EntityManager) is var identity && identity != target)
            subjects.Add(identity);

        var wildcard = ExamineCardText.Wildcard.ToString();
        foreach (var rule in ExamineChipRules.All)
        {
            if (!_loc.HasString(rule.Message))
                continue;

            var label = rule.Chip == null ? null : Loc.GetString($"wf-examine-chip-{rule.Chip}");
            foreach (var subject in subjects)
            {
                var args = new List<(string, object)>
                {
                    ("ent", subject), ("target", subject), ("user", subject), ("entity", subject),
                };
                if (TryComp<HumanoidAppearanceComponent>(target, out var appearance))
                {
                    args.Add(("age", _humanoid.GetAgeRepresentation(appearance.Species, appearance.Age)));
                    args.Add(("species", _humanoid.GetSpeciesRepresentation(appearance.Species, appearance.CustomSpeciesName).ToLower()));
                }

                foreach (var variable in OpenVariables)
                {
                    args.Add((variable, wildcard));
                }

                var text = ExamineCardText.Normalize(FormattedMessage.FromMarkupPermissive(Loc.GetString(rule.Message, args.ToArray())).ToString());

                // INDEFINITE can't see the real word, so let the wildcard take the article too.
                foreach (var article in new[] { "an ", "a ", "An ", "A " })
                {
                    text = text.Replace(article + wildcard, wildcard);
                }
                // A pattern that is nearly all wildcard would swallow unrelated lines.
                if (text.Replace(wildcard, string.Empty).Trim().Length < 4 || !seen.Add(text))
                    continue;

                patterns.Add(new ExamineCardPattern(label, rule.Tone, text.Split(ExamineCardText.Wildcard)));
            }
        }

        return patterns;
    }
}
