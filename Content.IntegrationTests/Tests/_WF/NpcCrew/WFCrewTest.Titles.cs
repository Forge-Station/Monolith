#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Shared.Access.Components;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>A profile's own title for a role goes on the crewman's name and card; a profile without one keeps the role's.</summary>
    [Test]
    public async Task ProfileTitlesReplaceTheRolesOwn()
    {
        var deck = await CreateDeck(new Vector2(760, 760), 7, true);
        await Server.WaitAssertion(() =>
        {
            // A crew card is only issued aboard a ship, so the deck is made one.
            SEntMan.EnsureComponent<ShuttleComponent>(deck);
            var crew = Server.System<WFCrewSystem>();
            var unit = crew.SpawnCrewman("WFCrewPilot", new EntityCoordinates(deck, new Vector2(2.5f)), "titles",
                "WFCrewAdsOfficer", "WFMobCrewBodyIpc", "Orion Unit 07", "WFCrewProfileAds")!.Value;
            var spacer = crew.SpawnCrewman("WFCrewPilot", new EntityCoordinates(deck, new Vector2(4.5f)), "titles",
                null, null, "Jane Doe", "WFCrewProfileHauler")!.Value;
            foreach (var member in new[] { unit, spacer })
            {
                SEntMan.GetComponent<HTNComponent>(member).Enabled = false;
                crew.Apply((member, SEntMan.GetComponent<WFCrewComponent>(member)));
            }

            Assert.That(SEntMan.GetComponent<MetaDataComponent>(unit).EntityName, Is.EqualTo("Helm Unit Orion Unit 07"));
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(spacer).EntityName, Is.EqualTo("First Officer Jane Doe"));

            var inventory = Server.System<InventorySystem>();
            Assert.That(inventory.TryGetSlotEntity(unit, "id", out var slot) && slot is { } card, Is.True, "The automaton carries a card.");
            var found = false;
            foreach (var held in inventory.GetHandOrInventoryEntities(unit))
            {
                if (SEntMan.TryGetComponent<IdCardComponent>(held, out var id))
                {
                    Assert.That(id.LocalizedJobTitle, Is.EqualTo("Helm Unit"));
                    found = true;
                }
                else if (SEntMan.TryGetComponent<PdaComponent>(held, out var pda) && pda.ContainedId is { } inner
                         && SEntMan.TryGetComponent<IdCardComponent>(inner, out var pdaId))
                {
                    Assert.That(pdaId.LocalizedJobTitle, Is.EqualTo("Helm Unit"));
                    found = true;
                }
            }

            Assert.That(found, Is.True, "The automaton's card carries its title.");
        });
    }
}
