using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Onyx.Medical; // WOLFGATE: P4-D13 vendors the medical patch into Content.Server.
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction.Components;
using Content.Shared.Sticky.Components;
using Content.Shared.Sticky.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// The medical patch as a delivery device: the on-attach dose, the periodic transfer, the stop on unstick and
/// the single-use trash swap. Deliberately asserts no wound healing - the patch carries whatever is inside it,
/// and this is also the D2 sanity check that it behaves identically on a wound host.
/// </summary>
/// <remarks>
/// <para>
/// PLAN4 §6.2 T-PATCH (WP12-9). The carried reagent is a bespoke inert test chem with no metabolisms, so this
/// file is independent of WP12-2's reagent content.
/// </para>
/// <para>
/// WOLFGATE finding, measured here: a stuck patch is genuinely un-unstickable while
/// <c>UnremoveableComponent</c> is on it. MedicalPatchSystem.OnStuck adds that component, and
/// SharedInteractionSystem subscribes <c>UnremoveableComponent</c> to
/// <c>ContainerGettingRemovedAttemptEvent</c> and cancels unconditionally, so
/// StickySystem.UnstickFromEntity can never take the patch out of the target's sticker container - including
/// the call MedicalPatchSystem.Update makes itself when the patch runs dry, which makes
/// <c>singleUse</c>/<c>trashObject</c> unreachable in normal play. This is NOT a port defect: Onyx's own
/// SharedInteractionSystem cancels identically at the pin, so the vendored file is faithful. Recorded for the
/// balance pass rather than patched here; the two tests below remove the component first, exactly as the
/// gib / unequip paths do.
/// </para>
/// </remarks>
[TestFixture]
[TestOf(typeof(MedicalPatchSystem))]
public sealed class WolfmedMedicalPatchTest : GameTest
{
    // WOLFGATE: MedicalPatchSystem is server-only (P4-D13) and nothing here reads client state, but sticking and
    // then unsticking hands the patch between the target's `stickers_container` and the user's hands inside one
    // tick, and RT's CLIENT ContainerSystem.HandleComponentState trips its own
    // `DebugTools.Assert(container.Contains(entity))` replicating that churn
    // (RobustToolbox/Robust.Client/GameObjects/EntitySystems/ContainerSystem.cs:206). It is vanilla StickySystem
    // behaviour that any sticky item shares and that phase 4 did not touch, so the pair runs disconnected
    // rather than being worked around in the vendored file.
    public override PoolSettings PoolSettings => PsDisconnected;

    // WOLFGATE: two patch prototypes rather than PLAN4's one. `singleUse: true` deletes the patch inside
    // EntityUnstuckEvent, which would make assertion (c) - "no further transfer after unsticking" - vacuously
    // true. WolfmedPatchItem keeps the patch alive so (c) measures the Update loop actually stopping;
    // WolfmedPatchSingleUse covers (d).
    //
    // The target carries a plain injectable solution rather than a bloodstream: MedicalPatchSystem.TryInject
    // only ever calls TryGetInjectableSolution, and a real BloodstreamComponent would metabolise the contents
    // away mid-test.
    [TestPrototypes]
    private const string Prototypes = @"
- type: reagent
  id: WolfmedPatchTestChem
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-nondescript
  color: ""#8C8C8C""

- type: entity
  id: WolfmedPatchTrash
  parent: BaseItem
  name: ""wolfmed spent test patch""

- type: entity
  id: WolfmedPatchItem
  parent: BaseItem
  name: ""wolfmed test patch""
  components:
  - type: SolutionContainerManager
    solutions:
      patch:
        maxVol: 100
        reagents:
        - ReagentId: WolfmedPatchTestChem
          Quantity: 100
  - type: Sticky
  - type: MedicalPatch
    solutionName: patch
    transferAmount: 5
    updateTime: 1
    injectAmmountOnAttatch: 2

- type: entity
  id: WolfmedPatchSingleUse
  parent: WolfmedPatchItem
  components:
  - type: MedicalPatch
    solutionName: patch
    transferAmount: 5
    updateTime: 1
    singleUse: true
    trashObject: WolfmedPatchTrash

- type: entity
  id: WolfmedPatchTarget
  parent: MobHuman
  components:
  - type: SolutionContainerManager
    solutions:
      wolfmedpatch:
        maxVol: 100
  - type: InjectableSolution
    solution: wolfmedpatch
";

    [Test]
    public async Task MedicalPatchInjectsOnScheduleAndStopsOnUnstickTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var patch = EntityUid.Invalid;
        var target = EntityUid.Invalid;
        var user = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            patch = entities.SpawnEntity("WolfmedPatchItem", map.GridCoords);
            target = entities.SpawnEntity("WolfmedPatchTarget", map.GridCoords);
            user = entities.SpawnEntity("MobHuman", map.GridCoords);

            Assert.That(Contained(entities, target), Is.EqualTo(FixedPoint2.Zero),
                "the target must start dry for the on-attach dose to be measurable.");

            // StickToEntity is what AfterInteract/the do-after ends in; it inserts the patch into the target's
            // sticker container, sets StuckTo (which the Update loop requires) and raises EntityStuckEvent.
            entities.System<StickySystem>()
                .StickToEntity((patch, entities.GetComponent<StickyComponent>(patch)), target, user);

            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<StickyComponent>(patch).StuckTo, Is.EqualTo(target));
                Assert.That(entities.HasComponent<UnremoveableComponent>(patch), Is.True,
                    "OnStuck must make a worn patch unremoveable by hand.");
                // `injectAmmountOnAttatch: 2` on the fixture; OnStuck injects it synchronously.
                Assert.That(Contained(entities, target), Is.EqualTo(FixedPoint2.New(2)));
            });
        });

        // MedicalPatchComponent.NextUpdate defaults to TimeSpan.Zero and OnStuck never seeds it, so the first
        // server tick after sticking is already due and transfers a full `transferAmount`. That first tick is
        // the reason this measures 5u after a fraction of a second rather than after `updateTime`.
        await Pair.RunTicksSync(2);

        var afterFirstTick = FixedPoint2.Zero;
        await server.WaitAssertion(() =>
        {
            afterFirstTick = Contained(entities, target);
            Assert.That(afterFirstTick, Is.EqualTo(FixedPoint2.New(7)),
                "2u on attach plus one immediate 5u transfer (NextUpdate starts at zero).");
        });

        // `updateTime: 1`, so one more full transfer lands inside 1.1 s and no more than two can.
        await RunSeconds(1.1f);

        await server.WaitAssertion(() =>
        {
            Assert.That(Contained(entities, target), Is.EqualTo(FixedPoint2.New(12)),
                "exactly one further 5u transfer must land in 1.1 s at updateTime 1.");

            // WOLFGATE (finding, see the class remarks): UnremoveableComponent has to come off first or
            // StickySystem.UnstickFromEntity is refused outright, which is what the gib / unequip paths do.
            entities.RemoveComponent<UnremoveableComponent>(patch);
            entities.System<StickySystem>()
                .UnstickFromEntity((patch, entities.GetComponent<StickyComponent>(patch)), user);

            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<StickyComponent>(patch).StuckTo, Is.Null);
                Assert.That(entities.HasComponent<UnremoveableComponent>(patch), Is.False);
                Assert.That(entities.Deleted(patch), Is.False,
                    "a non-single-use patch survives unsticking, which is what makes the next assertion mean something.");
            });
        });

        await RunSeconds(1.5f);

        await server.WaitAssertion(() =>
            Assert.That(Contained(entities, target), Is.EqualTo(FixedPoint2.New(12)),
                "an unstuck patch must transfer nothing: the Update loop skips a null StuckTo."));
    }

    /// <summary>PLAN4 §6.2 T-PATCH (d): the single-use trash swap.</summary>
    [Test]
    public async Task SingleUseMedicalPatchSpawnsTrashAndDeletesItselfTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var patch = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            patch = entities.SpawnEntity("WolfmedPatchSingleUse", map.GridCoords);
            var target = entities.SpawnEntity("WolfmedPatchTarget", map.GridCoords);
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var sticky = entities.System<StickySystem>();

            Assert.That(CountTrash(entities), Is.Zero);

            sticky.StickToEntity((patch, entities.GetComponent<StickyComponent>(patch)), target, user);
            entities.RemoveComponent<UnremoveableComponent>(patch); // WOLFGATE: see the class remarks.
            sticky.UnstickFromEntity((patch, entities.GetComponent<StickyComponent>(patch)), user);

            // `trashObject: WolfmedPatchTrash` on the fixture; OnUnstuck spawns it before queueing the patch.
            Assert.That(CountTrash(entities), Is.EqualTo(1));
        });

        // QueueDel runs at the end of the tick, so the deletion is only observable after one.
        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
            Assert.That(entities.Deleted(patch), Is.True, "a single-use patch must delete itself on unstick."));
    }

    private static FixedPoint2 Contained(IEntityManager entities, EntityUid target)
    {
        return entities.System<SharedSolutionContainerSystem>()
            .TryGetInjectableSolution(target, out _, out var solution)
            ? solution.GetTotalPrototypeQuantity("WolfmedPatchTestChem")
            : FixedPoint2.Zero;
    }

    private static int CountTrash(IEntityManager entities)
    {
        return entities.EntityQuery<MetaDataComponent>(true)
            .Count(meta => meta.EntityPrototype?.ID == "WolfmedPatchTrash");
    }
}
