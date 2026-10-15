#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared._Forge.KIAS.Controllers;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasGraphTests
{
    private static IReadOnlyList<KiasGraphPort>? Profile(string id) => id == "Test" ? new List<KiasGraphPort>
    {
        KiasGraphCatalog.Port("Pulse", KiasPortType.Signal, true),
        KiasGraphCatalog.Port("Trigger", KiasPortType.Signal),
        KiasGraphCatalog.Port("Message", KiasPortType.String),
        KiasGraphCatalog.Port("Amount", KiasPortType.Number),
        KiasGraphCatalog.Port("State", KiasPortType.Bool, true)
    } : null;

    private static KiasControllerProgram Program(params KiasNodeKind[] kinds)
    {
        var result = new KiasControllerProgram();
        for (var i = 0; i < kinds.Length; i++) result.Nodes.Add(new() { Id = i + 1, Kind = kinds[i], Profile = "Test" });
        return result;
    }

    private static void Wire(KiasControllerProgram program, int from, string output, int to, string input) =>
        program.Wires.Add(new() { FromNode = from, FromPort = output, ToNode = to, ToPort = input });

    private static KiasGraphMachine Machine(KiasControllerProgram program,
        Action<KiasControllerNode, string, KiasGraphValue>? command = null, Action<int, double, uint>? schedule = null)
    {
        var compilation = KiasGraphCompiler.Compile(program, Profile);
        Assert.That(compilation.Errors, Is.Empty);
        var machine = new KiasGraphMachine(compilation.Graph!, command ?? ((_, _, _) => { }), schedule ?? ((_, _, _) => { }), () => 10);
        machine.Start();
        return machine;
    }

    [Test]
    public void EnumDomainsInferLegacyValuesAndRejectCrossDomainConnections()
    {
        var channel = KiasGraphCatalog.Port("Channel", KiasPortType.Enum);
        channel.EnumDomain = KiasEnumDomain.AudioChannel;
        var disposition = KiasGraphCatalog.Port("Disposition", KiasPortType.Enum, true);
        disposition.EnumDomain = KiasEnumDomain.ContactDisposition;
        IReadOnlyList<KiasGraphPort>? Ports(string id) => id == "Test" ? new[] { channel, disposition } : null;
        var legacy = Program(KiasNodeKind.EnumConstant, KiasNodeKind.All);
        legacy.Nodes[0].Config.Enum = 3;
        Wire(legacy, 1, "Value", 2, "Channel");
        var compiled = KiasGraphCompiler.Compile(legacy, Ports);
        Assert.That(compiled.Success, Is.True);
        Assert.That(compiled.Graph!.Ports[new(1, "Value")].EnumDomain, Is.EqualTo(KiasEnumDomain.AudioChannel));
        Assert.That(legacy.Nodes[0].Config.EnumDomain, Is.EqualTo(KiasEnumDomain.Unspecified));
        legacy.Nodes[0].Config.EnumDomain = KiasEnumDomain.Alert;
        Assert.That(KiasGraphCompiler.Compile(legacy, Ports).Errors, Does.Contain("enum-domain"));
        legacy.Nodes[0].Config.EnumDomain = KiasEnumDomain.AudioChannel;
        legacy.Nodes[0].Config.Enum = 99;
        Assert.That(KiasGraphCompiler.Compile(legacy, Ports).Success, Is.False);
        var compare = Program(KiasNodeKind.Any, KiasNodeKind.EnumCompare, KiasNodeKind.EnumConstant);
        compare.Nodes[2].Config.EnumDomain = KiasEnumDomain.AudioChannel;
        Wire(compare, 1, "Disposition", 2, "A"); Wire(compare, 3, "Value", 2, "B");
        Assert.That(KiasGraphCompiler.Compile(compare, Ports).Errors, Does.Contain("enum-domain"));
    }

    [Test]
    public void LogicTruthTablesAndComparisonTypes()
    {
        foreach (var kind in new[] { KiasNodeKind.And, KiasNodeKind.Or, KiasNodeKind.Xor, KiasNodeKind.Nand,
                     KiasNodeKind.Nor, KiasNodeKind.Xnor, KiasNodeKind.Not })
        foreach (var a in new[] { false, true })
        foreach (var b in new[] { false, true })
        {
            var machine = Machine(Program(kind));
            machine.Input(1, "A", KiasGraphValue.Boolean(a));
            if (kind != KiasNodeKind.Not) machine.Input(1, "B", KiasGraphValue.Boolean(b));
            Assert.That(machine.Value(1, "Value").Bool, Is.EqualTo(kind switch
            {
                KiasNodeKind.And => a && b, KiasNodeKind.Or => a || b, KiasNodeKind.Xor => a != b,
                KiasNodeKind.Nand => !(a && b), KiasNodeKind.Nor => !(a || b), KiasNodeKind.Xnor => a == b, _ => !a
            }));
        }
        foreach (var comparison in Enum.GetValues<KiasComparison>())
        {
            var program = Program(KiasNodeKind.NumberCompare);
            program.Nodes[0].Config.Comparison = comparison;
            var machine = Machine(program);
            machine.Input(1, "A", KiasGraphValue.Numeric(2));
            machine.Input(1, "B", KiasGraphValue.Numeric(3));
            Assert.That(machine.Value(1, "Value").Bool, Is.EqualTo(comparison is KiasComparison.NotEqual or KiasComparison.Less or KiasComparison.LessEqual));
        }
        foreach (var kind in new[] { KiasNodeKind.BoolCompare, KiasNodeKind.StringCompare, KiasNodeKind.EnumCompare })
        {
            var machine = Machine(Program(kind));
            var value = kind switch
            {
                KiasNodeKind.BoolCompare => KiasGraphValue.Boolean(true),
                KiasNodeKind.StringCompare => KiasGraphValue.String("abc"), _ => KiasGraphValue.Enumeration(3)
            };
            machine.Input(1, "A", value);
            machine.Input(1, "B", value);
            Assert.That(machine.Value(1, "Value").Bool, Is.True);
        }
    }

    [Test]
    public void IfFanOutConstantsAndPulseStreams()
    {
        var program = Program(KiasNodeKind.BoolConstant, KiasNodeKind.OnStart, KiasNodeKind.If,
            KiasNodeKind.All, KiasNodeKind.Any, KiasNodeKind.StringConstant);
        program.Nodes[0].Config.Bool = true;
        program.Nodes[5].Config.Text = "message";
        Wire(program, 1, "Value", 3, "Condition");
        Wire(program, 2, "Started", 3, "Trigger");
        Wire(program, 3, "True", 4, "Trigger");
        Wire(program, 3, "True", 5, "Trigger");
        Wire(program, 6, "Value", 4, "Message");
        var commands = new List<(int Node, string Port, KiasGraphValue Value)>();
        var machine = Machine(program, (node, port, value) => commands.Add((node.Id, port, value)));
        Assert.That(commands.Count(item => item.Port == "Trigger"), Is.EqualTo(2));
        Assert.That(commands.FindIndex(item => item.Port == "Message"), Is.LessThan(commands.FindIndex(item => item.Port == "Trigger")));
        machine.Input(3, "Condition", KiasGraphValue.Boolean(false));
        machine.Input(3, "Trigger", KiasGraphValue.Pulse);
        Assert.That(commands.Count(item => item.Port == "Trigger"), Is.EqualTo(2));
        machine.Input(3, "Condition", KiasGraphValue.Boolean(true));
        machine.Input(3, "Trigger", KiasGraphValue.Pulse);
        machine.Input(3, "Trigger", KiasGraphValue.Pulse);
        Assert.That(commands.Count(item => item.Port == "Trigger"), Is.EqualTo(6));
    }

    [Test]
    public void TimerCancelClockStopAndColdBoot()
    {
        var scheduled = new List<(int Node, double Seconds, uint Token)>();
        var program = Program(KiasNodeKind.Timer, KiasNodeKind.Clock, KiasNodeKind.All);
        program.Nodes[1].Config.Bool = true;
        Wire(program, 1, "Elapsed", 3, "Trigger");
        Wire(program, 2, "Tick", 3, "Trigger");
        var commands = 0;
        var machine = Machine(program, (_, port, _) => { if (port == "Trigger") commands++; },
            (node, seconds, token) => scheduled.Add((node, seconds, token)));
        machine.Input(1, "Trigger", KiasGraphValue.Pulse);
        var timer = scheduled[^1];
        machine.Input(1, "Cancel", KiasGraphValue.Pulse);
        machine.TimerElapsed(timer.Node, timer.Token);
        Assert.That(commands, Is.Zero);
        machine.Input(1, "Trigger", KiasGraphValue.Pulse);
        timer = scheduled[^1];
        machine.TimerElapsed(timer.Node, timer.Token);
        Assert.That(commands, Is.EqualTo(1));
        var clock = scheduled[0];
        machine.TimerElapsed(clock.Node, clock.Token);
        Assert.That(commands, Is.EqualTo(2));
        clock = scheduled[^1];
        machine.Input(2, "Enabled", KiasGraphValue.Boolean(false));
        machine.TimerElapsed(clock.Node, clock.Token);
        Assert.That(commands, Is.EqualTo(2));
        machine.Input(1, "Trigger", KiasGraphValue.Pulse);
        timer = scheduled[^1];
        machine.Stop();
        machine.TimerElapsed(timer.Node, timer.Token);
        machine.Start();
        machine.TimerElapsed(timer.Node, timer.Token);
        Assert.That(commands, Is.EqualTo(2), "Cold boot never reuses a timer token from the previous runtime.");
    }

    [Test]
    public void StateNodesAreIndependentAndReset()
    {
        var machine = Machine(Program(KiasNodeKind.Latch, KiasNodeKind.Toggle, KiasNodeKind.Counter, KiasNodeKind.Edge));
        machine.Input(1, "Set", KiasGraphValue.Pulse);
        Assert.That(machine.Value(1, "Value").Bool, Is.True);
        machine.Input(1, "Reset", KiasGraphValue.Pulse);
        Assert.That(machine.Value(1, "Value").Bool, Is.False);
        machine.Input(2, "Trigger", KiasGraphValue.Pulse);
        Assert.That(machine.Value(2, "Value").Bool, Is.True);
        machine.Input(2, "Trigger", KiasGraphValue.Pulse);
        Assert.That(machine.Value(2, "Value").Bool, Is.False);
        machine.Input(3, "Increment", KiasGraphValue.Pulse);
        machine.Input(3, "Increment", KiasGraphValue.Pulse);
        machine.Input(3, "Decrement", KiasGraphValue.Pulse);
        Assert.That(machine.Value(3, "Value").Number, Is.EqualTo(1));
        Assert.That(Machine(Program(KiasNodeKind.Counter)).Value(1, "Value").Number, Is.Zero);
        machine.Start();
        Assert.That(machine.Value(3, "Value").Number, Is.Zero);
    }

    [Test]
    public void ValidatorRejectsMalformedGraphsAndAllowsStateFeedback()
    {
        void Invalid(KiasControllerProgram program) => Assert.That(KiasGraphCompiler.Compile(program, Profile).Success, Is.False);
        var program = Program(KiasNodeKind.Not, KiasNodeKind.Not);
        Wire(program, 1, "Value", 2, "A"); Wire(program, 2, "Value", 1, "A"); Invalid(program);
        program = Program(KiasNodeKind.Latch, KiasNodeKind.Edge);
        Wire(program, 1, "Value", 2, "Value"); Wire(program, 2, "Rising", 1, "Reset");
        Assert.That(KiasGraphCompiler.Compile(program, Profile).Success, Is.True);
        program = Program(KiasNodeKind.NumberConstant, KiasNodeKind.Not);
        Wire(program, 1, "Value", 2, "A"); Invalid(program);
        program = Program(KiasNodeKind.BoolConstant, KiasNodeKind.BoolConstant, KiasNodeKind.Not);
        Wire(program, 1, "Value", 3, "A"); Wire(program, 2, "Value", 3, "A"); Invalid(program);
        program = Program(KiasNodeKind.OnStart); Wire(program, 1, "Started", 99, "Missing"); Invalid(program);
        program = Program(KiasNodeKind.OnStart, KiasNodeKind.OnStart); program.Nodes[1].Id = 1; Invalid(program);
        program = Program(KiasNodeKind.Any); program.Nodes[0].Profile = "Unknown"; Invalid(program);
        program = Program(KiasNodeKind.Timer); program.Nodes[0].Config.Seconds = double.NaN; Invalid(program);
        program.Nodes[0].Config.Seconds = 0; Invalid(program);
        program.Nodes[0].Config.Seconds = 601; Invalid(program);
        program = Program(KiasNodeKind.StringConstant); program.Nodes[0].Config.Text = new string('a', 257); Invalid(program);
        program = Program(KiasNodeKind.NumberConstant); program.Nodes[0].Config.Number = double.PositiveInfinity; Invalid(program);
        program = Program(KiasNodeKind.OnStart); program.Version++; Invalid(program);
        program = new(); for (var i = 1; i <= 129; i++) program.Nodes.Add(new() { Id = i, Kind = KiasNodeKind.OnStart }); Invalid(program);
    }

    [Test]
    public void FeedbackBudgetFaultsLocallyAndDraftCopyIsIndependent()
    {
        var program = Program(KiasNodeKind.Counter, KiasNodeKind.Any, KiasNodeKind.All);
        Wire(program, 2, "Pulse", 1, "Increment");
        Wire(program, 1, "Value", 3, "Amount");
        var compiled = KiasGraphCompiler.Compile(program, Profile);
        Assert.That(compiled.Success, Is.True);
        KiasGraphMachine machine = null!;
        machine = new(compiled.Graph!, (_, _, _) => machine.EmitExternal(2, "Pulse", KiasGraphValue.Pulse),
            (_, _, _) => { }, () => 10) { Budget = 24 };
        machine.Start();
        Assert.That(machine.Active, Is.False);
        Assert.That(machine.Fault, Is.EqualTo("evaluation-budget"));
        var healthy = Machine(program);
        Assert.That(healthy.Active, Is.True);
        var copy = program.Copy();
        copy.Nodes[0].Config.Number = 500;
        copy.Wires[0].FromPort = "Changed";
        Assert.That(program.Nodes[0].Config.Number, Is.Zero);
        Assert.That(program.Wires[0].FromPort, Is.EqualTo("Pulse"));
    }
    [Test]
    public void CooldownKeepsIndependentSourceKeysAndBoundsUntrustedStreams()
    {
        var program = Program(KiasNodeKind.Cooldown, KiasNodeKind.All);
        Wire(program, 1, "Ready", 2, "Trigger");
        var commands = 0;
        var machine = Machine(program, (_, port, _) => { if (port == "Trigger") commands++; });
        foreach (var key in new[] { "fore", "aft", "fore" })
        {
            machine.Input(1, "Key", KiasGraphValue.String(key));
            machine.Input(1, "Trigger", KiasGraphValue.Pulse);
        }
        Assert.That(commands, Is.EqualTo(2));
        machine.Input(1, "Reset", KiasGraphValue.Pulse);
        machine.Input(1, "Trigger", KiasGraphValue.Pulse);
        Assert.That(commands, Is.EqualTo(3));
        for (var i = 0; i < 512; i++)
        {
            machine.Input(1, "Key", KiasGraphValue.String($"source-{i}"));
            machine.Input(1, "Trigger", KiasGraphValue.Pulse);
        }
        Assert.That(machine.Active, Is.False);
        Assert.That(machine.Fault, Is.EqualTo("cooldown-budget"));
    }

    [Test]
    public void StringLatchStoresBeforePulseAndResetsOnColdBoot()
    {
        var program = Program(KiasNodeKind.StringLatch, KiasNodeKind.All);
        program.Nodes[0].Config.Text = "initial";
        Wire(program, 1, "Stored", 2, "Message");
        Wire(program, 1, "Saved", 2, "Trigger");
        KiasGraphMachine machine = null!;
        string? captured = null;
        var compiled = KiasGraphCompiler.Compile(program, Profile);
        Assert.That(compiled.Errors, Is.Empty);
        machine = new(compiled.Graph!, (_, port, _) =>
        {
            if (port == "Trigger") captured = machine.InputValue(2, "Message").Text;
        }, (_, _, _) => { }, () => 10);
        machine.Start();
        machine.Input(1, "Value", KiasGraphValue.String("distress reason"));
        Assert.That(machine.Value(1, "Stored").Text, Is.EqualTo("initial"));
        machine.Input(1, "Store", KiasGraphValue.Pulse);
        Assert.That(captured, Is.EqualTo("distress reason"));
        machine.Input(1, "Value", KiasGraphValue.String("later event"));
        Assert.That(machine.Value(1, "Stored").Text, Is.EqualTo("distress reason"));
        machine.Start();
        Assert.That(machine.Value(1, "Stored").Text, Is.EqualTo("initial"));
        machine.Input(1, "Store", KiasGraphValue.Pulse);
        Assert.That(machine.Value(1, "Stored").Type, Is.EqualTo(KiasPortType.String));
        Assert.That(machine.Value(1, "Stored").Text, Is.Empty);
    }

    [TestCase(0.1)]
    [TestCase(6)]
    [TestCase(600)]
    public void DiagnosticProbeDoesNotMutateLiveStateOrScheduleLiveTimers(double seconds)
    {
        var program = Program(KiasNodeKind.Any, KiasNodeKind.Counter, KiasNodeKind.Timer, KiasNodeKind.All);
        program.Nodes[2].Config.Seconds = seconds;
        Wire(program, 1, "Pulse", 2, "Increment");
        Wire(program, 1, "Pulse", 3, "Trigger");
        Wire(program, 2, "Value", 4, "Amount");
        Wire(program, 3, "Elapsed", 4, "Trigger");
        var commands = 0;
        var schedules = new List<(int Node, uint Token)>();
        var machine = Machine(program, (_, _, _) => commands++, (node, _, token) => schedules.Add((node, token)));
        var baseline = commands;
        machine.ProbeExternal(1, "Pulse", KiasGraphValue.Pulse, default);
        Assert.Multiple(() =>
        {
            Assert.That(machine.Value(2, "Value").Number, Is.Zero);
            Assert.That(commands, Is.EqualTo(baseline));
            Assert.That(schedules, Is.Empty);
            Assert.That(machine.Active, Is.True);
        });
        machine.EmitExternal(1, "Pulse", KiasGraphValue.Pulse);
        Assert.That(machine.Value(2, "Value").Number, Is.EqualTo(1));
        Assert.That(schedules, Has.Count.EqualTo(1));
        machine.TimerElapsed(schedules[0].Node, schedules[0].Token);
        Assert.That(commands, Is.EqualTo(baseline + 2));
    }
}
