#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Targeting;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>Bullets land on the aimed part by a roll against gun spread and range; melee always lands where aimed.</summary>
[TestFixture]
[TestOf(typeof(WolfmedAimScatterSystem))]
public sealed class WolfmedAimScatterTest : GameTest
{
    [Test]
    public async Task BulletsStrayMeleeDoesNotTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var aim = server.System<WolfmedAimScatterSystem>();

        await server.WaitAssertion(() =>
        {
            var shooter = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.GetComponent<TargetingComponent>(shooter).Target = TargetBodyPart.Head;
            var gun = entities.SpawnEntity("WeaponPistolMk58", map.GridCoords);
            var bullet = entities.SpawnEntity("BulletMinigun", map.GridCoords);
            entities.GetComponent<ProjectileComponent>(bullet).Weapon = gun;
            var knife = entities.SpawnEntity("KitchenKnife", map.GridCoords);

            // A roll that always beats the hit chance: the round strays, and the head's only neighbour is the torso.
            aim.ForcedRoll = 0.99f;
            var strayed = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(6, 0)));
            Hit(entities, strayed, shooter, bullet, "Piercing");
            Assert.Multiple(() =>
            {
                Assert.That(Wounded(entities, strayed, BodyPartType.Head), Is.False);
                Assert.That(Wounded(entities, strayed, BodyPartType.Torso), Is.True);
            });

            // The same roll with a knife: melee is not scattered.
            var stabbed = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(0, 1)));
            Hit(entities, stabbed, shooter, knife, "Slash");
            Assert.That(Wounded(entities, stabbed, BodyPartType.Head), Is.True);

            // A roll under every chance: the bullet lands where it was aimed.
            aim.ForcedRoll = 0f;
            var headshot = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(6, 2)));
            Hit(entities, headshot, shooter, bullet, "Piercing");
            Assert.That(Wounded(entities, headshot, BodyPartType.Head), Is.True);
            aim.ForcedRoll = null;

            // Spread and range drive the chance: a tight gun up close is at the ceiling, a hot gun at range at the floor.
            var gunComp = entities.GetComponent<GunComponent>(gun);
            var head = Part(entities, headshot, BodyPartType.Head);
            var headComp = entities.GetComponent<BodyPartComponent>(head);

            gunComp.CurrentAngle = Angle.FromDegrees(0.5);
            gunComp.MinAngleModified = Angle.FromDegrees(0.5);
            var tight = aim.HitChance(stabbed, shooter, gun, Part(entities, stabbed, BodyPartType.Head), headComp);

            gunComp.CurrentAngle = Angle.FromDegrees(30);
            var hot = aim.HitChance(headshot, shooter, gun, head, headComp);

            Assert.Multiple(() =>
            {
                Assert.That(tight, Is.EqualTo(0.75f).Within(0.001f));
                Assert.That(hot, Is.EqualTo(0.1f).Within(0.001f));
            });
        });
        await server.WaitPost(() => aim.ForcedRoll = null);
    }

    private static void Hit(IEntityManager entities, EntityUid body, EntityUid shooter, EntityUid tool, string type)
    {
        var spec = new DamageSpecifier();
        spec.DamageDict[new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(15);
        entities.System<DamageableSystem>().TryChangeDamage(body, spec, origin: shooter, tool: tool);
    }

    private static EntityUid Part(IEntityManager entities, EntityUid body, BodyPartType type)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body).First(part => part.Component.PartType == type).Id;
    }

    private static bool Wounded(IEntityManager entities, EntityUid body, BodyPartType type)
    {
        var part = Part(entities, body, type);
        return entities.System<WoundSystem>().GetWounds((part, entities.GetComponent<WoundableComponent>(part))).Any();
    }
}
