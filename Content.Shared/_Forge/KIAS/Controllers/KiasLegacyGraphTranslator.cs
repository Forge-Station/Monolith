namespace Content.Shared._Forge.KIAS.Controllers;

public sealed class KiasLegacyGraphImport
{
    public KiasControllerProgram? Program;
    public bool Enabled;
    public List<string> Errors = new();
}

public static class KiasLegacyGraphTranslator
{
    public static KiasLegacyGraphImport Import(KiasProtocolRecord record,
        Func<EntityUid, string, bool>? supports = null, Func<EntityUid, string, string?>? bridge = null,
        Func<KiasTrigger, string>? fallbackMessage = null)
    {
        var result = new KiasLegacyGraphImport { Enabled = record.Enabled };
        if (!Enum.IsDefined(record.Trigger) || !float.IsFinite(record.MinimumValue) || !float.IsFinite(record.Cooldown)
            || record.Disposition is { } disposition && !Enum.IsDefined(disposition) || record.Actions.Count is < 1 or > 8)
        { result.Errors.Add("legacy-record"); return result; }
        if (record.RequireCrewUnavailable && record.Trigger is not (KiasTrigger.CrewCritical or KiasTrigger.CrewDead
            or KiasTrigger.VesselCritical or KiasTrigger.AtmosDanger or KiasTrigger.Fire or KiasTrigger.PowerDeficit or KiasTrigger.CrewUnavailable))
        { result.Errors.Add("legacy-crew-condition"); return result; }
        var program = new KiasControllerProgram { Name = record.PresetId.Length > 0 ? record.PresetId : record.Trigger.ToString() };
        KiasControllerNode Node(KiasNodeKind kind, string profile = "", int column = 0, int row = 0)
        {
            var node = new KiasControllerNode { Id = program.Nodes.Count + 1, Kind = kind, Profile = profile,
                X = column * 320, Y = row * 220 };
            program.Nodes.Add(node); return node;
        }
        void Wire(KiasControllerNode from, string output, KiasControllerNode to, string input) => program.Wires.Add(new()
            { FromNode = from.Id, FromPort = output, ToNode = to.Id, ToPort = input });
        var source = Node(KiasNodeKind.Any, "Automation");
        var condition = Node(KiasNodeKind.NumberCompare, column: 1);
        condition.Config.Comparison = KiasComparison.GreaterEqual;
        var threshold = Node(KiasNodeKind.NumberConstant, column: 0, row: 1); threshold.Config.Number = record.MinimumValue;
        Wire(source, "Value", condition, "A"); Wire(threshold, "Value", condition, "B");
        if (record.Disposition is { } required)
        {
            var comparison = Node(KiasNodeKind.EnumCompare, column: 1, row: 1);
            var constant = Node(KiasNodeKind.EnumConstant, row: 2); constant.Config.Enum = (int) required;
            Wire(source, "Disposition", comparison, "A"); Wire(constant, "Value", comparison, "B");
            var both = Node(KiasNodeKind.And, column: 2, row: 1);
            Wire(condition, "Value", both, "A"); Wire(comparison, "Value", both, "B"); condition = both;
        }
        if (record.RequireCrewUnavailable)
        {
            var both = Node(KiasNodeKind.And, column: 2, row: 2);
            Wire(condition, "Value", both, "A"); Wire(source, "AllCrewUnavailable", both, "B"); condition = both;
        }
        var gate = Node(KiasNodeKind.If, column: 3);
        Wire(condition, "Value", gate, "Condition");
        if (record.Trigger == KiasTrigger.Boot)
        {
            var boot = Node(KiasNodeKind.OnStart, row: 3); Wire(boot, "Started", gate, "Trigger");
        }
        else Wire(source, record.Trigger.ToString(), gate, "Trigger");
        var cooldown = Node(KiasNodeKind.Cooldown, column: 4);
        cooldown.Config.Seconds = Math.Clamp(record.Cooldown, 1, 600);
        Wire(gate, "True", cooldown, "Trigger"); Wire(source, "EventKey", cooldown, "Key");
        var channel = Node(KiasNodeKind.EnumConstant, column: 4, row: 1);
        channel.Config.Enum = (int) Channel(record.Trigger);
        var actionRow = 0;
        void Message(KiasControllerNode target, string input, string text)
        {
            if (text.Length > 0 || record.Trigger == KiasTrigger.Boot)
            {
                var constant = Node(KiasNodeKind.StringConstant, column: 5, row: actionRow++);
                constant.Config.Text = text.Length > 0 ? text : fallbackMessage?.Invoke(record.Trigger) ?? "KIAS online";
                Wire(constant, "Value", target, input);
            }
            else Wire(source, "Message", target, input);
        }
        KiasControllerNode Target(string profile, KiasProtocolAction action, bool specific = true)
        {
            var target = Node(specific && action.Target != null ? KiasNodeKind.Specific : KiasNodeKind.All,
                profile, 6, actionRow++);
            target.Group = profile is "Speaker" or "LightController" ? action.Group : string.Empty;
            if (target.Kind == KiasNodeKind.Specific)
            {
                target.Binding = action.Target;
                if (supports?.Invoke(action.Target!.Value, profile) != true) result.Errors.Add($"legacy-target:{profile}");
            }
            return target;
        }
        void Act(KiasControllerNode target, string input) => Wire(cooldown, "Ready", target, input);
        void Record(KiasProtocolAction action)
        {
            var recorder = Target("Recorder", action, false);
            Message(recorder, "Message", action.Message); Wire(source, "EventKey", recorder, "Key"); Act(recorder, "Record");
        }
        var alert = Alert(record.Trigger, record.Disposition);
        if (alert is { } level)
        {
            var destination = Node(KiasNodeKind.All, "Automation", 6, actionRow++);
            var constant = Node(KiasNodeKind.EnumConstant, column: 5, row: actionRow++); constant.Config.Enum = (int) level;
            Wire(constant, "Value", destination, "Alert"); Act(destination, "EscalateAlert");
        }
        foreach (var action in record.Actions)
        {
            if (!Enum.IsDefined(action.Kind) || action.Message.Length > 256 || action.Group.Length > 32 || action.Port.Length > 60)
            { result.Errors.Add("legacy-action"); continue; }
            KiasControllerNode target;
            switch (action.Kind)
            {
                case KiasActionKind.Announce:
                    target = Target("Speaker", action); Message(target, "Message", action.Message);
                    Wire(channel, "Value", target, "Channel"); Wire(source, "EventKey", target, "Key"); Act(target, "Alarm");
                    Record(action);
                    break;
                case KiasActionKind.Record:
                    if (action.Target != null) result.Errors.Add("legacy-record-target");
                    Record(action);
                    break;
                case KiasActionKind.Lights:
                    Act(Target("LightController", action, false), action.Value ? "On" : "Off"); break;
                case KiasActionKind.Pdc:
                    Act(Target("DefenceController", action), action.Value ? "Enable" : "Disable"); break;
                case KiasActionKind.FireLock:
                    Act(Target("DefenceController", action, false), action.Value ? "Lock" : "Unlock"); break;
                case KiasActionKind.Mayday: case KiasActionKind.MedicalHelp:
                    target = Target("NavigationComms", action, false);
                    if (action.Kind == KiasActionKind.MedicalHelp)
                    {
                        Message(target, "MaydayMessage", action.Message); Act(target, "MedicalHelp"); break;
                    }
                    var reason = Node(KiasNodeKind.StringLatch, column: 5, row: actionRow++);
                    Message(reason, "Value", action.Message); Act(reason, "Store");
                    Wire(reason, "Stored", target, "MaydayMessage"); Wire(reason, "Saved", target, "Mayday");
                    var armed = Node(KiasNodeKind.Latch, column: 5, row: actionRow++);
                    var repeat = Node(KiasNodeKind.Clock, column: 6, row: actionRow++); repeat.Config.Seconds = 180;
                    Wire(reason, "Saved", armed, "Set"); Wire(armed, "Value", repeat, "Enabled"); Wire(repeat, "Tick", target, "Mayday");
                    Wire(source, "AlertReset", armed, "Reset"); Wire(source, "AlertReset", reason, "Reset");
                    break;
                case KiasActionKind.Suppression: case KiasActionKind.Relay: case KiasActionKind.Jammer:
                case KiasActionKind.Decoy: case KiasActionKind.RestoreVentilation:
                    if (action.Target == null) { result.Errors.Add($"legacy-target-required:{action.Kind}"); break; }
                    var profile = action.Kind switch
                    {
                        KiasActionKind.Suppression => "Suppression", KiasActionKind.Relay => "Relay", KiasActionKind.Jammer => "Jammer",
                        KiasActionKind.Decoy => "Decoy", _ => "Ventilation"
                    };
                    var port = action.Kind switch
                    {
                        KiasActionKind.Suppression => "Trigger", KiasActionKind.Relay => action.Value ? "Close" : "Open",
                        KiasActionKind.Jammer => action.Value ? "Enable" : "Disable", KiasActionKind.Decoy => "Deploy", _ => "Restore"
                    };
                    Act(Target(profile, action), port); break;
                case KiasActionKind.DevicePort:
                    if (action.Target is not { } uid || bridge?.Invoke(uid, action.Port) is not { } family)
                    { result.Errors.Add("legacy-device-port"); break; }
                    target = Target(family, action); Act(target, "in:" + action.Port); break;
            }
        }
        if (result.Errors.Count == 0) result.Program = program;
        return result;
    }

    private static KiasAudioChannel Channel(KiasTrigger trigger) => trigger switch
    {
        KiasTrigger.HullImpact or KiasTrigger.Collision or KiasTrigger.WeaponFlash or KiasTrigger.Manual => KiasAudioChannel.Battle,
        KiasTrigger.CrewCritical or KiasTrigger.CrewDead or KiasTrigger.VesselCritical or KiasTrigger.AtmosDanger or KiasTrigger.Fire => KiasAudioChannel.Emergency,
        KiasTrigger.HullDamage or KiasTrigger.AnomalyGrowth or KiasTrigger.PowerDeficit or KiasTrigger.Proximity => KiasAudioChannel.Warning,
        _ => KiasAudioChannel.Notification
    };
    private static KiasAlert? Alert(KiasTrigger trigger, KiasContactDisposition? disposition) => trigger switch
    {
        KiasTrigger.HullImpact or KiasTrigger.Collision or KiasTrigger.Manual => KiasAlert.Battle,
        KiasTrigger.WeaponFlash when disposition == KiasContactDisposition.Hostile => KiasAlert.Battle,
        KiasTrigger.CrewCritical or KiasTrigger.CrewDead or KiasTrigger.VesselCritical or KiasTrigger.AtmosDanger
            or KiasTrigger.Fire or KiasTrigger.Boarding => KiasAlert.Emergency,
        KiasTrigger.Contact => KiasAlert.Contact, _ => null
    };
}
