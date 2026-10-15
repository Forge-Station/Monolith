using System.Linq;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;

namespace Content.Server._Forge.KIAS.Controllers;

public sealed partial class KiasControllerIoSystem
{
    public void Command(EntityUid grid, EntityUid card, EntityUid target, KiasControllerNode node, string port,
        KiasGraphValue value, KiasCommandInputs inputs)
    {
        if (!_kias.IsOnline(target) || Transform(target).GridUid != grid || !Profiles(target).Contains(node.Profile)) return;
        var schema = Schema(node.Profile);
        var accepted = false;
        if (schema != null)
            foreach (var item in schema)
                if (item.Id == port && item.Direction == KiasPortDirection.Input && item.Type == value.Type)
                { accepted = true; break; }
        if (!accepted) return;
        if (Comp<KiasGridComponent>(grid).Testing && node.Profile is not ("Speaker" or "Recorder")) return;
        CommandDispatched?.Invoke(grid, card, target, node.Profile, port);
        if (node.Profile.StartsWith("Link.", StringComparison.Ordinal))
        {
            if (port.StartsWith("in:", StringComparison.Ordinal) && value.Type == KiasPortType.Signal)
                EntityManager.System<DeviceLinkSystem>().InvokeSink(Transform(card).ParentUid, target, port[3..]);
            return;
        }
        var message = string.Empty;
        var sourceKey = string.Empty;
        if (node.Profile is "Speaker" or "Recorder")
        {
            message = inputs.Read("Message").Text ?? string.Empty;
            message = message[..Math.Min(message.Length, 256)];
            sourceKey = $"controller:{card}:{node.Id}:{target}:{inputs.Read("Key").Text}:{message}";
        }
        switch (node.Profile)
        {
            case "Speaker":
                if (port is not ("Announce" or "Alarm")) return;
                var channel = inputs.Read("Channel").Enum;
                EntityManager.System<KiasSafetySystem>().Publish(grid, message, port == "Alarm", speaker: target,
                    key: sourceKey, channel: Enum.IsDefined((KiasAudioChannel) channel) ? (KiasAudioChannel) channel : KiasAudioChannel.Notification, record: false);
                break;
            case "Recorder":
                if (port == "Record") EntityManager.System<KiasRecorderSystem>().Record(target, message, sourceKey);
                break;
            case "LightController":
                if (port is "Set" or "On" or "Off") EntityManager.System<KiasActuatorSystem>().SetLights(target, port == "Set" ? value.Bool : port == "On");
                break;
            case "Lighting":
                if (port is "Set" or "On" or "Off") EntityManager.System<KiasActuatorSystem>().SetFixture(target, port == "Set" ? value.Bool : port == "On");
                break;
            case "Suppression":
                if (port == "Trigger") EntityManager.System<KiasActuatorSystem>().Suppress(target);
                break;
            case "Relay":
                if (port is "Closed" or "Open" or "Close") EntityManager.System<KiasRelaySystem>().SetClosed(target, port == "Closed" ? value.Bool : port == "Close");
                break;
            case "DefenceController":
                if (port is "FireLock" or "Lock" or "Unlock") EntityManager.System<KiasDefenceSystem>().SetFireLock(target, port == "FireLock" ? value.Bool : port == "Lock");
                else if (port is "Automatic" or "Enable" or "Disable") EntityManager.System<KiasDefenceSystem>().SetAutomatic(target, port == "Automatic" ? value.Bool : port == "Enable");
                break;
            case "NavigationComms":
                var reason = inputs.Read("MaydayMessage").Text ?? string.Empty;
                if (port == "Mayday") EntityManager.System<KiasProtocolSystem>().Mayday(grid, reason);
                else if (port == "MedicalHelp") EntityManager.System<KiasProtocolSystem>().MedicalHelp(grid, reason);
                break;
            case "Jammer":
                if (_kias.HasRole(grid, KiasDeviceRole.Defence) && port is "Enabled" or "Enable" or "Disable")
                    EntityManager.System<JammerSystem>().SetEnabled(target, port == "Enabled" ? value.Bool : port == "Enable");
                break;
            case "Decoy":
                if (port == "Deploy" && _kias.HasRole(grid, KiasDeviceRole.Defence)) EntityManager.System<KiasCountermeasureSystem>().Deploy(target);
                break;
            case "Ventilation":
                if (port == "Restore" && _kias.HasRole(grid, KiasDeviceRole.Atmosphere)) EntityManager.System<KiasVentilationSystem>().Restore(target);
                break;
            case "DeviceAdapter":
                if (port is "On" or "Off" or "Toggle" && Transform(card).ParentUid is { } rack)
                    EntityManager.System<DeviceLinkSystem>().InvokeSink(rack, target, port);
                break;
            case "Automation":
                var requested = (KiasAlert) inputs.Read("Alert").Enum;
                if (port is "SetAlert" or "EscalateAlert" && Enum.IsDefined(requested))
                {
                    if (port == "EscalateAlert" && Comp<KiasGridComponent>(grid).Core is { } core
                        && TryComp<KiasProtocolComponent>(core, out var state)) requested = (KiasAlert) Math.Max((int) requested, (int) state.Alert);
                    EntityManager.System<KiasProtocolSystem>().SetAlert(grid, requested);
                }
                else if (port == "RunManual") EntityManager.System<KiasProtocolSystem>().Trigger(grid, KiasTrigger.Manual);
                else if (port == "SetQuietMode") EntityManager.System<KiasProtocolSystem>().Trigger(grid, KiasTrigger.QuietMode);
                else if (port == "ResetAlert") EntityManager.System<KiasProtocolSystem>().ResetAlert(grid);
                break;
        }
    }
}
