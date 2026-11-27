using Content.Server.Body.Components;
using Content.Shared._WF.WolfChem.Narcotics;
using Content.Shared.Cargo.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Construction.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Item;
using Content.Shared.Tag;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.WolfChem.Narcotics;

/// <summary>
/// The contraband is the drug, not what it comes in. Any item or movable structure with a street drug in one of its
/// solutions, a beaker, syringe, pill, baggie, vial or storage tank, is marked the way the ready-made products are,
/// with the <c>WFNarcotic</c> tag and the contraband, sale tax and stock marker of <c>WFNarcoticMarks</c>, and loses
/// them once the drug is out. A blank stick that takes a copy is marked for good.
/// </summary>
public sealed class WFNarcoticGoodsSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private TagSystem _tag = default!;

    public static readonly EntProtoId Marks = "WFNarcoticMarks";
    public static readonly ProtoId<TagPrototype> Tag = "WFNarcotic";

    /// <summary>The reagent group whose priced members are street drugs.</summary>
    public const string Group = "Narcotics";

    private ComponentRegistry? _marks;

    public override void Initialize()
    {
        base.Initialize();
        // Items and what can be unbolted and hauled to a pallet; a thing that is both is simply checked twice.
        SubscribeLocalEvent<ItemComponent, SolutionChangedEvent>((Entity<ItemComponent> ent, ref SolutionChangedEvent args) => OnChanged(ent, args.Solution.Comp.Solution));
        SubscribeLocalEvent<AnchorableComponent, SolutionChangedEvent>((Entity<AnchorableComponent> ent, ref SolutionChangedEvent args) => OnChanged(ent, args.Solution.Comp.Solution));
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => _marks = null);
    }

    private void OnChanged(EntityUid ent, Solution changed)
    {
        // A creature small enough to carry is not a package.
        if (HasComp<BloodstreamComponent>(ent))
            return;

        if (HoldsNarcotics(changed))
        {
            SetNarcotic(ent, true);
            return;
        }

        // Only what was marked here is unmarked, and only once none of its solutions holds any.
        if (!HasComp<WFNarcoticMarkedComponent>(ent))
            return;

        foreach (var (_, solution) in _solutions.EnumerateSolutions(ent))
        {
            if (HoldsNarcotics(solution.Comp.Solution))
                return;
        }

        SetNarcotic(ent, false);
    }

    /// <summary>Whether a solution holds any street drug: a reagent of the narcotics group that has a price.</summary>
    public bool HoldsNarcotics(Solution solution)
    {
        foreach (var reagent in solution.Contents)
        {
            if (reagent.Quantity > FixedPoint2.Zero
                && _prototype.TryIndex<ReagentPrototype>(reagent.Reagent.Prototype, out var proto)
                && proto.Group == Group
                && proto.PricePerUnit > 0f)
                return true;
        }

        return false;
    }

    /// <summary>The street value of the drugs in a solution, each at its grade.</summary>
    public double StreetValue(Solution solution)
    {
        var value = 0.0;
        foreach (var reagent in solution.Contents)
        {
            if (reagent.Quantity > FixedPoint2.Zero
                && _prototype.TryIndex<ReagentPrototype>(reagent.Reagent.Prototype, out var proto)
                && proto.Group == Group
                && proto.PricePerUnit > 0f)
                value += reagent.Quantity.Double() * proto.PricePerUnit * solution.GetPriceFactor(reagent.Reagent.Prototype);
        }

        return value;
    }

    /// <summary>
    /// The street value of the narcotics a thing itself holds, leaving out what is inside its containers. A narcotic
    /// with no drug in it, a written stick, counts its own price.
    /// </summary>
    public double StreetValue(EntityUid uid)
    {
        var value = 0.0;
        // A solution is an entity inside its holder; it is counted there, not again on its own.
        foreach (var (_, solution) in _solutions.EnumerateSolutions(uid, includeSelf: false))
        {
            value += StreetValue(solution.Comp.Solution);
        }

        if (value <= 0 && _tag.HasTag(uid, Tag) && TryComp<StaticPriceComponent>(uid, out var price))
            value = price.Price;

        return value;
    }

    /// <summary>
    /// What a narcotic adds to a contraband hand-in: its flat reward, plus a point each time the street value handed
    /// in so far, kept in <paramref name="pool"/>, fills another <c>wf.narcotics.turn_in_value</c>. It goes into
    /// <paramref name="taken"/> whether or not it brought a point, so a pallet pays for all it is given, however it is
    /// packed, and leaves nothing behind to be counted again.
    /// </summary>
    public int TurnIn(EntityUid uid, int reward, ref double pool, HashSet<EntityUid> taken)
    {
        var rate = _cfg.GetCVar(WFNarcoticsCVars.TurnInValue);
        if (rate <= 0 || !_tag.HasTag(uid, Tag))
            return reward;

        var value = StreetValue(uid);
        if (value <= 0)
            return reward;

        var before = (int) (pool / rate);
        pool += value;
        taken.Add(uid);
        return reward + (int) (pool / rate) - before;
    }

    /// <summary>Marks a thing as a narcotic, or takes back a mark this system gave; ready-made products keep theirs.</summary>
    public void SetNarcotic(EntityUid uid, bool narcotic)
    {
        if (TerminatingOrDeleted(uid) || GetMarks() is not { } marks)
            return;

        if (narcotic)
        {
            if (_tag.HasTag(uid, Tag))
                return;

            // Whatever the item already had of these stays its own, now and when the mark comes off.
            var marked = EnsureComp<WFNarcoticMarkedComponent>(uid);
            var added = new ComponentRegistry();
            foreach (var (name, entry) in marks)
            {
                if (HasComp(uid, entry.Component.GetType()))
                    continue;

                added.Add(name, entry);
                marked.Added.Add(name);
            }

            EntityManager.AddComponents(uid, added, removeExisting: false);
            _tag.AddTag(uid, Tag);
        }
        else if (TryComp<WFNarcoticMarkedComponent>(uid, out var marked))
        {
            var added = new ComponentRegistry();
            foreach (var name in marked.Added)
            {
                if (marks.TryGetValue(name, out var entry))
                    added.Add(name, entry);
            }

            EntityManager.RemoveComponents(uid, added);
            _tag.RemoveTag(uid, Tag);
            RemComp<WFNarcoticMarkedComponent>(uid);
        }
    }

    /// <summary>The marks prototype's own components, without the transform and metadata every prototype carries.</summary>
    private ComponentRegistry? GetMarks()
    {
        if (_marks != null)
            return _marks;

        if (!_prototype.TryIndex<EntityPrototype>(Marks, out var proto))
            return null;

        _marks = new ComponentRegistry();
        foreach (var (name, entry) in proto.Components)
        {
            if (name != "Transform" && name != "MetaData")
                _marks.Add(name, entry);
        }

        return _marks;
    }
}
