using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Events;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._WF.Wolfmed.Consciousness;

/// <summary>Stops a body that is down from getting itself onto or through a table.</summary>
// A climb is refused when the body is Downed or out and is the one climbing; a medic lifting it on still works.
// Lying down strips the tables' layer from a body's masks (StandingStateSystem); while Downed the layer stays, and
// a body lifted onto a table by somebody else keeps the climb's own masks.
public sealed class WolfmedDownedClimbSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StandingStateSystem _standing = default!;

    /// <summary>The layer lying down takes off a body's masks (StandingStateSystem's standing layer): the tables' own.</summary>
    private const int TableLayer = (int) CollisionGroup.TableLayer;

    public override void Initialize()
    {
        base.Initialize();
        // Playtest 4: after the pod's own refusal, so a body that is down gets "for lying in, not on" there, not this.
        SubscribeLocalEvent<ClimbableComponent, AttemptClimbEvent>(OnAttemptClimb, after: new[] { typeof(SharedAutodocSystem) });
        SubscribeLocalEvent<WolfmedDownedComponent, EndClimbEvent>(OnEndClimb);
    }

    /// <summary>Downed, or out cold, under Wolfmed.</summary>
    public bool IsDown(EntityUid body) =>
        HasComp<WolfmedDownedComponent>(body) ||
        TryComp(body, out WolfmedConsciousnessComponent? consciousness) &&
        consciousness.State == WolfmedConsciousness.Unconscious;

    private void OnAttemptClimb(Entity<ClimbableComponent> ent, ref AttemptClimbEvent args)
    {
        if (args.Cancelled || args.User != args.Climber || !IsDown(args.Climber))
            return;

        args.Cancelled = true;
        _popup.PopupClient(Loc.GetString("wolfmed-downed-cant-climb"), args.User, args.User);
    }

    /// <summary>Off a table a medic put it on: solid to tables again while still Downed.</summary>
    private void OnEndClimb(Entity<WolfmedDownedComponent> ent, ref EndClimbEvent args)
    {
        HoldTables(ent);
    }

    /// <summary>
    /// Puts the tables' layer back on the fixtures lying down took it off, so a Downed body bumps into tables as a
    /// standing one does. Not while it is on a table already: the climb owns those masks until it gets off.
    /// Called by <see cref="WolfmedDownedSystem"/> as the body goes Downed.
    /// </summary>
    public void HoldTables(EntityUid body)
    {
        if (!Climbing(body))
            SetTableLayer(body, true);
    }

    /// <summary>
    /// Downed is over but the body is still lying (a stun, or out cold): upstream's crawl-under-tables again.
    /// Called by <see cref="WolfmedDownedSystem"/> after its stand attempt.
    /// </summary>
    public void ReleaseTables(EntityUid body)
    {
        if (_standing.IsDown(body) && !Climbing(body))
            SetTableLayer(body, false);
    }

    private bool Climbing(EntityUid body) => TryComp(body, out ClimbingComponent? climbing) && climbing.IsClimbing;

    private void SetTableLayer(EntityUid body, bool solid)
    {
        if (!TryComp(body, out StandingStateComponent? standing) || !TryComp(body, out FixturesComponent? fixtures))
            return;

        foreach (var key in standing.ChangedFixtures)
        {
            if (!fixtures.Fixtures.TryGetValue(key, out var fixture))
                continue;

            var mask = solid ? fixture.CollisionMask | TableLayer : fixture.CollisionMask & ~TableLayer;
            if (mask != fixture.CollisionMask)
                _physics.SetCollisionMask(body, key, fixture, mask, fixtures);
        }
    }
}
