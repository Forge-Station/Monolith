#pragma warning disable RA0002
using Content.Server._Forge.KIAS;
using Content.Server._Forge.KIAS.Controllers;
using Content.Server.Power.Components;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared._Forge.KIAS;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.Mind;
using Robust.Server.Player;
using System.Linq;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasControllerEditorTests
{
    [Test]
    public async Task FrozenMappingAllowsUnpoweredEditingButInitializedPausedMapsDoNot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap(initialized: false);
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var coordinates = new EntityCoordinates(map.Grid, .5f, .5f);
            var actor = em.SpawnEntity("MobHuman", coordinates);
            var other = em.SpawnEntity("MobHuman", coordinates);
            var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
            var mind = em.System<SharedMindSystem>().CreateMind(session.UserId);
            em.System<SharedMindSystem>().TransferTo(mind, actor);
            var admin = pair.Server.ResolveDependency<IAdminManager>().GetAdminData(actor)!;
            Assert.That(admin.HasFlag(AdminFlags.Mapping), Is.True);
            var programmer = em.SpawnEntity("KiasControllerProgrammer", coordinates);
            var card = em.SpawnEntity("KiasProgrammableController", coordinates);
            var speaker = em.SpawnEntity("KiasSpeaker", coordinates);
            Assert.That(em.System<KiasSystem>().IsOnline(programmer), Is.False);
            var physical = em.System<KiasControllerPhysicalSystem>();
            Assert.That(physical.CanConfigure(programmer, other), Is.False);
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(programmer, KiasControllerProgrammerComponent.SlotId, card, actor), Is.True);
            var ui = em.System<UserInterfaceSystem>();
            Assert.That(ui.TryOpenUi(programmer, KiasControllerUiKey.Programmer, actor), Is.True);
            Assert.That(ui.TryGetUiState<KiasControllerEditorState>(programmer, KiasControllerUiKey.Programmer, out var state), Is.True);
            Assert.That(state!.Mapping, Is.True);
            Assert.That(state.Online, Is.False, "Mapping must not power the machine or activate its network.");
            Assert.That(state.Devices.Any(device => device.Entity == em.GetNetEntity(speaker) && device.Profile == "Speaker"), Is.True);
            var draft = em.GetComponent<KiasControllerProgrammerComponent>(programmer);
            var editor = em.System<KiasControllerUiSystem>();
            bool Edit(KiasGraphEdit action, string text = "") => editor.Edit((programmer, draft), actor,
                new() { Edit = action, Text = text, Revision = draft.DraftRevision });
            Assert.That(Edit(KiasGraphEdit.Rename, "Mapping card"), Is.True);
            Assert.That(editor.Edit((programmer, draft), actor, new() { Revision = draft.DraftRevision, Edit = KiasGraphEdit.Add,
                Kind = KiasNodeKind.Specific, Profile = "Speaker", Binding = em.GetNetEntity(speaker) }), Is.True);
            Assert.That(Edit(KiasGraphEdit.Write), Is.True);
            Assert.That(em.GetComponent<KiasControllerCardComponent>(card).Program.Name, Is.EqualTo("Mapping card"));
            var deleted = em.SpawnEntity("KiasSpeaker", coordinates);
            var deletedNet = em.GetNetEntity(deleted);
            em.DeleteEntity(deleted);
            Assert.That(editor.Edit((programmer, draft), actor, new() { Revision = draft.DraftRevision, Edit = KiasGraphEdit.Add,
                Kind = KiasNodeKind.Specific, Profile = "Speaker", Binding = deletedNet }), Is.False);
            EntityUid lateSpeaker = default;
            for (var i = 0; i < 257; i++) lateSpeaker = em.SpawnEntity("KiasSpeaker", coordinates);
            editor.Refresh((programmer, draft));
            Assert.That(ui.TryGetUiState<KiasControllerEditorState>(programmer, KiasControllerUiKey.Programmer, out state), Is.True);
            Assert.That(state!.CatalogTotal, Is.GreaterThan(256));
            Assert.That(state.Devices.Count, Is.LessThanOrEqualTo(256));
            Assert.That(state.Devices.Any(device => device.Entity == em.GetNetEntity(lateSpeaker)), Is.False);
            var revision = draft.DraftRevision;
            Assert.That(editor.Edit((programmer, draft), actor, new() { Edit = KiasGraphEdit.Catalog, Node = 1 }), Is.True);
            Assert.That(ui.TryGetUiState<KiasControllerEditorState>(programmer, KiasControllerUiKey.Programmer, out state), Is.True);
            Assert.That(state!.CatalogPage, Is.EqualTo(1));
            var lateId = em.System<KiasDeviceIdentitySystem>().Identifier(lateSpeaker);
            Assert.That(editor.Edit((programmer, draft), actor, new() { Edit = KiasGraphEdit.Catalog, Text = lateId }), Is.True);
            Assert.That(ui.TryGetUiState<KiasControllerEditorState>(programmer, KiasControllerUiKey.Programmer, out state), Is.True);
            Assert.That(state!.Devices.Any(device => device.Entity == em.GetNetEntity(lateSpeaker) && device.Profile == "Speaker"), Is.True);
            Assert.That(draft.DraftRevision, Is.EqualTo(revision));
            Assert.That(draft.DraftDirty, Is.False);
            Assert.That(editor.Edit((programmer, draft), actor, new() { Revision = revision, Edit = KiasGraphEdit.Add,
                Kind = KiasNodeKind.Specific, Profile = "Speaker", Binding = em.GetNetEntity(lateSpeaker) }), Is.True);
            Assert.That(Edit(KiasGraphEdit.Write), Is.True);
            var flags = admin.Flags;
            admin.Flags &= ~AdminFlags.Mapping;
            Assert.That(Edit(KiasGraphEdit.Rename, "no mapping permission"), Is.False);
            admin.Flags = flags;
            admin.Active = false;
            Assert.That(Edit(KiasGraphEdit.Rename, "deadmin"), Is.False);
            admin.Active = true;
            var maps = em.System<SharedMapSystem>();
            maps.InitializeMap(map.MapId, unpause: false);
            Assert.That(maps.IsPaused(map.MapId), Is.True);
            var core = em.SpawnEntity("KiasCore", coordinates);
            em.RemoveComponent<ApcPowerReceiverComponent>(core);
            em.SpawnEntity("KiasDataCable", coordinates);
            em.System<KiasSystem>().Rebuild(map.Grid);
            Assert.That(physical.CanConfigure(programmer, actor), Is.True, "Ordinary ownership may allow access, but it must not bypass power.");
            Assert.That(Edit(KiasGraphEdit.Rename, "initialized paused map"), Is.False);
            maps.SetPaused(map.MapId, false);
            Assert.That(Edit(KiasGraphEdit.Write), Is.False);
            Assert.That(em.System<KiasSystem>().IsOnline(programmer), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DraftLeaseAtomicWriteStaleMessagesRangeAndDirtyEjection()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var coordinates = new EntityCoordinates(map.Grid, .5f, .5f);
            EntityUid Spawn(string prototype)
            {
                var uid = em.SpawnEntity(prototype, coordinates);
                em.RemoveComponent<ApcPowerReceiverComponent>(uid); return uid;
            }
            Spawn("KiasCore"); Spawn("KiasDataCable");
            var programmer = Spawn("KiasControllerProgrammer");
            var card = Spawn("KiasProgrammableController");
            var actor = Spawn("MobHuman"); var other = Spawn("MobHuman");
            em.System<KiasSystem>().Rebuild(map.Grid);
            var slots = em.System<ItemSlotsSystem>();
            Assert.That(slots.TryInsert(programmer, KiasControllerProgrammerComponent.SlotId, card, actor), Is.True);
            var ui = em.System<UserInterfaceSystem>(); var editor = em.System<KiasControllerUiSystem>();
            Assert.That(ui.TryOpenUi(programmer, KiasControllerUiKey.Programmer, actor), Is.True);
            Assert.That(ui.TryOpenUi(programmer, KiasControllerUiKey.Programmer, other), Is.False, "Only one writer may own the draft lease.");
            var draft = em.GetComponent<KiasControllerProgrammerComponent>(programmer);
            var stored = em.GetComponent<KiasControllerCardComponent>(card);
            var initialName = em.GetComponent<MetaDataComponent>(card).EntityName;
            bool Edit(KiasGraphEdit action, string text = "", uint? revision = null) => editor.Edit((programmer, draft), actor,
                new() { Edit = action, Text = text, Revision = revision ?? draft.DraftRevision });
            Assert.That(Edit(KiasGraphEdit.Rename, "Written program"), Is.True);
            Assert.That(stored.Program.Name, Is.EqualTo("Controller"));
            Assert.That(em.GetComponent<MetaDataComponent>(card).EntityName, Is.EqualTo(initialName));
            Assert.That(Edit(KiasGraphEdit.Rename, "stale", draft.DraftRevision - 1), Is.False);
            Assert.That(Edit(KiasGraphEdit.Eject), Is.False);
            Assert.That(Edit(KiasGraphEdit.Write), Is.True);
            Assert.That(stored.Program.Name, Is.EqualTo("Written program")); Assert.That(stored.Revision, Is.EqualTo(1));
            Assert.That(em.GetComponent<MetaDataComponent>(card).EntityName, Is.EqualTo(stored.Program.Name));
            Assert.That(Edit(KiasGraphEdit.Rename, "Discard me"), Is.True);
            Assert.That(Edit(KiasGraphEdit.Discard), Is.True); Assert.That(draft.Draft!.Name, Is.EqualTo(stored.Program.Name));
            Assert.That(editor.Edit((programmer, draft), actor, new() { Revision = draft.DraftRevision, Edit = KiasGraphEdit.Add,
                Kind = KiasNodeKind.Specific, Profile = "Speaker", Binding = em.GetNetEntity(actor) }), Is.False);
            em.System<SharedTransformSystem>().SetCoordinates(actor, new EntityCoordinates(map.Grid, 100, 100));
            Assert.That(Edit(KiasGraphEdit.Write), Is.False, "A UI handle does not authorize remote writes.");
            em.System<SharedTransformSystem>().SetCoordinates(actor, coordinates);
            em.EnsureComponent<KiasClaimComponent>(map.Grid).Owner = new Robust.Shared.Network.NetUserId(Guid.NewGuid());
            Assert.That(Edit(KiasGraphEdit.Rename, "unauthorized"), Is.False);
            em.RemoveComponent<KiasClaimComponent>(map.Grid);
            ui.CloseUi(programmer, KiasControllerUiKey.Programmer, actor);
            Assert.That(Edit(KiasGraphEdit.Write), Is.False);
            Assert.That(slots.TryEject(programmer, KiasControllerProgrammerComponent.SlotId, actor, out _), Is.True);
            Assert.That(em.GetComponent<MetaDataComponent>(card).EntityName, Is.EqualTo("Written program"));
        });
        await pair.CleanReturnAsync();
    }
}
