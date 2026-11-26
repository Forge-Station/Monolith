using System.Linq;
using Content.Shared._WF.Traders;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;

namespace Content.Server._WF.Traders;

/// <summary>
/// Handles the cartons traders pack orders into: filling them, and tipping them out again.
/// </summary>
public sealed partial class TraderParcelSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TraderParcelComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<TraderParcelComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<TraderParcelComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<TraderParcelComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<TraderParcelComponent, EntityTerminatingEvent>(OnTerminating);
    }

    private void OnInit(Entity<TraderParcelComponent> ent, ref ComponentInit args)
    {
        _container.EnsureContainer<Container>(ent, ent.Comp.ContainerId);
    }

    /// <summary>
    /// Packs one item. Only the trader calls this; players have no way in.
    /// </summary>
    public bool Insert(Entity<TraderParcelComponent> ent, EntityUid item)
    {
        var container = _container.EnsureContainer<Container>(ent, ent.Comp.ContainerId);
        return _container.Insert(item, container);
    }

    /// <summary>
    /// How much is still packed in there.
    /// </summary>
    public int GetCount(Entity<TraderParcelComponent> ent)
    {
        return _container.TryGetContainer(ent, ent.Comp.ContainerId, out var container)
            ? container.ContainedEntities.Count
            : 0;
    }

    private void OnUseInHand(Entity<TraderParcelComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        Open(ent, args.User);
        args.Handled = true;
    }

    private void OnGetVerbs(Entity<TraderParcelComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("trader-parcel-open-verb"),
            Act = () => Open(ent, user),
        });
    }

    /// <summary>
    /// Tips the parcel out at the user's feet, hands them the top item and destroys the carton.
    /// </summary>
    public void Open(Entity<TraderParcelComponent> ent, EntityUid user)
    {
        var coords = Transform(user).Coordinates;
        var first = EntityUid.Invalid;

        if (_container.TryGetContainer(ent, ent.Comp.ContainerId, out var container))
        {
            first = container.ContainedEntities.FirstOrDefault();
            _container.EmptyContainer(container, force: true, destination: coords);
        }

        if (first.IsValid() && !TerminatingOrDeleted(first))
            _hands.TryPickupAnyHand(user, first);

        _audio.PlayPvs(ent.Comp.OpenSound, coords);
        QueueDel(ent.Owner);
    }

    private void OnExamined(Entity<TraderParcelComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("trader-parcel-examine", ("count", GetCount(ent))));
    }

    /// <summary>
    /// A parcel that dies with goods still in it spills them rather than taking them along.
    /// </summary>
    private void OnTerminating(Entity<TraderParcelComponent> ent, ref EntityTerminatingEvent args)
    {
        if (!_container.TryGetContainer(ent, ent.Comp.ContainerId, out var container))
            return;

        if (container.ContainedEntities.Count == 0)
            return;

        // A parcel dying with its grid or map has nowhere to spill to; the engine takes the lot.
        var coords = _transform.GetMoverCoordinates(ent.Owner);
        if (!coords.IsValid(EntityManager) || TerminatingOrDeleted(coords.EntityId))
            return;

        _container.EmptyContainer(container, force: true, destination: coords);
    }
}
