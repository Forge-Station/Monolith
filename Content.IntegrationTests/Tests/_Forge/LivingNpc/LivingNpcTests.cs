using Content.Server._Forge.LivingNpc;
using Content.Server.NPC.HTN;
using Content.Shared._Forge.LivingNpc;
using Content.Shared._Forge.LivingNpc.Components;
using Content.Shared._Forge.LivingNpc.Prototypes;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Forge.LivingNpc;

[TestFixture]
public sealed class LivingNpcTests
{
    private static readonly string[] LivingNpcPrototypes =
    {
        "MobLivingNpcCivilian",
        "MobLivingNpcBartender",
        "MobLivingNpcJanitor",
        "MobLivingNpcDockworker",
        "MobLivingNpcMerchant",
        "MobLivingNpcSecurity",
        "MobLivingNpcScientist",
        "MobLivingNpcMedic",
        "MobLivingNpcClown",
    };

    [Test]
    public async Task PrototypesLoadAndSpawnWithoutHtn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            Assert.That(proto.HasIndex<LivingNpcPersonalityPrototype>("LivingNpcPersonalityCivilian"), Is.True);
            Assert.That(proto.HasIndex<LivingNpcDialoguePrototype>("LivingNpcDialogueCivilian"), Is.True);

            foreach (var id in LivingNpcPrototypes)
            {
                Assert.That(proto.HasIndex<EntityPrototype>(id), Is.True, $"Missing {id}");
                var uid = entities.SpawnEntity(id, map.GridCoords);
                Assert.Multiple(() =>
                {
                    Assert.That(entities.HasComponent<LivingNpcComponent>(uid), Is.True, $"{id} missing LivingNpc");
                    Assert.That(entities.HasComponent<LivingNpcMemoryComponent>(uid), Is.True, $"{id} missing memory");
                    Assert.That(entities.HasComponent<ActiveLivingNpcComponent>(uid), Is.True, $"{id} should wake on spawn");
                    Assert.That(entities.HasComponent<HTNComponent>(uid), Is.False, $"{id} must not use HTN");
                    var npc = entities.GetComponent<LivingNpcComponent>(uid);
                    Assert.That(npc.HomeSet, Is.True, $"{id} should record a home");
                    Assert.That(npc.Personality.Extraversion, Is.InRange(0f, 1f));
                });
                entities.DeleteEntity(uid);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DamageCreatesMemoryAndInterruptsIntent()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var proto = server.ResolveDependency<IPrototypeManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var npcUid = entities.SpawnEntity("MobLivingNpcCivilian", map.GridCoords);
            var attacker = entities.SpawnEntity("MobLivingNpcCivilian", map.GridCoords);
            var blunt = proto.Index<DamageTypePrototype>("Blunt");
            var damage = entities.System<DamageableSystem>();

            damage.TryChangeDamage(npcUid, new DamageSpecifier(blunt, FixedPoint2.New(12)), origin: attacker);

            var living = entities.GetComponent<LivingNpcComponent>(npcUid);
            var memory = entities.GetComponent<LivingNpcMemoryComponent>(npcUid);

            Assert.Multiple(() =>
            {
                Assert.That(memory.Entries.ContainsKey(attacker), Is.True);
                Assert.That(memory.Entries[attacker].HurtMe, Is.True);
                Assert.That(memory.Entries[attacker].Reputation, Is.LessThan(0f));
                Assert.That(living.Mood.Fear, Is.GreaterThan(0f));
                Assert.That(living.CurrentIntent, Is.EqualTo(LivingNpcIntent.Flee));
            });

            entities.DeleteEntity(npcUid);
            entities.DeleteEntity(attacker);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BrainAssignsIntentAndSecurityFights()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid civilian = default;
        EntityUid security = default;
        await server.WaitPost(() =>
        {
            civilian = entities.SpawnEntity("MobLivingNpcCivilian", map.GridCoords);
            security = entities.SpawnEntity("MobLivingNpcSecurity", map.GridCoords);
            var living = entities.GetComponent<LivingNpcComponent>(civilian);
            living.NextThink = TimeSpan.Zero;
            var sec = entities.GetComponent<LivingNpcComponent>(security);
            sec.NextThink = TimeSpan.Zero;
            Assert.That(sec.WillFight, Is.True);
        });

        await server.WaitRunTicks(20);

        await server.WaitAssertion(() =>
        {
            var living = entities.GetComponent<LivingNpcComponent>(civilian);
            Assert.That(Enum.IsDefined(living.CurrentIntent), Is.True);
            Assert.That(entities.HasComponent<ActiveLivingNpcComponent>(civilian), Is.True);
            entities.DeleteEntity(civilian);
            entities.DeleteEntity(security);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SocialClassifiesSpeech()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var uid = entities.SpawnEntity("MobLivingNpcBartender", map.GridCoords);
            var npc = entities.GetComponent<LivingNpcComponent>(uid);
            var social = entities.System<LivingNpcSocialSystem>();

            Assert.Multiple(() =>
            {
                Assert.That(social.Classify((uid, npc), "Привет"), Is.EqualTo(LivingNpcSpeechKind.Greeting));
                Assert.That(social.Classify((uid, npc), "How are you"), Is.EqualTo(LivingNpcSpeechKind.HowAreYou));
                Assert.That(social.Classify((uid, npc), "спасибо"), Is.EqualTo(LivingNpcSpeechKind.Thanks));
                Assert.That(social.Classify((uid, npc), "help me"), Is.EqualTo(LivingNpcSpeechKind.Help));
                Assert.That(social.PickLine(npc, LivingNpcSpeechKind.Greeting, "Alex"), Is.Not.Null.And.Not.Empty);
            });

            entities.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }
}
