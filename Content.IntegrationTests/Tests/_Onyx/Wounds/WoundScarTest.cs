using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE(Wolfmed): Onyx's SharedBodySystem.TryDetachPart lives on WolfmedBodySystem here.
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.CCVar;
using Content.Shared.FixedPoint;
using Content.Shared.Rejuvenate;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Onyx.Wounds;

[TestFixture]
[TestOf(typeof(WoundScarSystem))]
public sealed class WoundScarTest : GameTest
{
    // WOLFGATE(Wolfmed): Onyx's `InitialBody` + `organs:` is Nubody. Wolfgate uses Shitmed's `Body` + a `body` prototype,
    // and BodyPartType.Chest does not exist here (D9), so every part is Torso-rooted with Wolfgate part entities.
    [TestPrototypes]
    private const string Prototypes = @"
- type: body
  id: WoundScarBodyGraph
  name: ""wound scar body""
  root: torso
  slots:
    torso:
      part: TorsoHuman
      connections:
      - head
    head:
      part: HeadHuman

- type: entity
  id: WoundScarBody
  parent: InventoryBase
  components:
  - type: Body
    prototype: WoundScarBodyGraph
  - type: Damageable
    damageContainer: Biological
  - type: WoundHost
";

    [Test]
    public async Task ThresholdTreatmentAttachmentAndRejuvenateTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var configuration = server.ResolveDependency<IConfigurationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            // WOLFGATE(Wolfmed): WoundScarSystem multiplies the wound's own scar chance by CCVars.SurgeryScarChance,
            // which ships at 0.35, so Onyx's test only passes about a third of the time. Pin it to 1 so the
            // threshold behaviour under test is deterministic.
            configuration.SetCVar(CCVars.SurgeryScarChance, 1f);
            var body = entities.SpawnEntity("WoundScarBody", map.GridCoords);
            var graph = entities.System<SharedBodySystem>();
            var wfBody = entities.System<WolfmedBodySystem>(); // WOLFGATE(Wolfmed)
            var wounds = entities.System<WoundSystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var torso = parts.Single(candidate => candidate.Component.PartType == BodyPartType.Torso).Id; // WOLFGATE(Wolfmed): D9
            var part = parts.Single(candidate => candidate.Component.PartType == BodyPartType.Head).Id;
            var woundable = entities.GetComponent<WoundableComponent>(part);

            var light = wounds.CreateOrMergeWound(part, new ProtoId<WoundPrototype>("BluntWound"), 19)!.Value;
            Assert.That(wounds.CloseWound(light));
            Assert.That(wounds.GetWounds((part, woundable)).Count(HasScar), Is.Zero);
            Assert.That(wounds.RemoveWound(light));

            var heavy = wounds.CreateOrMergeWound(part, new ProtoId<WoundPrototype>("BluntWound"), 20)!.Value;
            Assert.That(wounds.CloseWound(heavy));
            var scar = wounds.GetWounds((part, woundable)).Single(HasScar);
            Assert.That(scar.Comp.State, Is.EqualTo(WoundState.Scarred));
            Assert.That(wounds.TreatWound(scar.Owner, FixedPoint2.New(1)), Is.False);
            Assert.That(wounds.RemoveWound(scar.Owner), Is.False);

            Assert.That(wfBody.TryDetachPart(part)); // WOLFGATE(Wolfmed): §2.7
            Assert.That(wounds.GetWounds((part, woundable)).Count(HasScar), Is.EqualTo(1));
            Assert.That(graph.AttachPart(torso, "head", part)); // WOLFGATE(Wolfmed): Shitmed's attach takes a slot id
            Assert.That(wounds.GetWounds((part, woundable)).Count(HasScar), Is.EqualTo(1));

            entities.EventBus.RaiseLocalEvent(body, new RejuvenateEvent());
            Assert.That(wounds.GetWounds((part, woundable)), Is.Empty);
        });

        await server.WaitPost(() =>
            configuration.SetCVar(CCVars.SurgeryScarChance, CCVars.SurgeryScarChance.DefaultValue));
    }

    private static bool HasScar(Entity<WoundComponent> wound) => wound.Comp.State == WoundState.Scarred;
}
