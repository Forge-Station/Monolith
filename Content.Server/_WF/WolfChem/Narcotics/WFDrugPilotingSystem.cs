using System.Numerics;
using Content.Server.Physics.Controllers;
using Content.Shared._WF.WolfChem.Narcotics;
using Content.Shared.Drunk;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.WolfChem.Narcotics;

/// <summary>
/// Flying a ship while high. Stimulants sharpen the turn and the thrust and twitch the stick in an overdose, and their
/// crash dulls both; opioids make the ship answer late and hold the stick through a nod; cannabis softens the brakes
/// and the turn; alcohol makes the heading wander. All of it scales with the phase of the high.
/// </summary>
public sealed class WFDrugPilotingSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WFAlcoholSystem _alcohol = default!;
    [Dependency] private WFIntoxicationSystem _intoxication = default!;

    /// <summary>Input below this is no input, so a lagging stick comes to rest.</summary>
    private const float Deadzone = 0.02f;

    /// <summary>Radians a full sway turns the thrust off its heading, per point of wander.</summary>
    private const float SwaySkew = 1f;

    private const float TwitchSeconds = 0.2f;

    public override void Initialize()
    {
        base.Initialize();
        // After the pilot's own handler, which writes the input these change.
        SubscribeLocalEvent<WFIntoxicationComponent, GetShuttleInputsEvent>(OnIntoxicated, after: new[] { typeof(MoverController) });
        SubscribeLocalEvent<DrunkComponent, GetShuttleInputsEvent>(OnDrunk, after: new[] { typeof(MoverController) });
    }

    private void OnIntoxicated(Entity<WFIntoxicationComponent> ent, ref GetShuttleInputsEvent args)
    {
        Shape(ent, ref args);
    }

    private void OnDrunk(Entity<DrunkComponent> ent, ref GetShuttleInputsEvent args)
    {
        if (!HasComp<WFIntoxicationComponent>(ent))
            Shape(ent, ref args);
    }

    /// <summary>What a body's highs, crashes and drink do to its flying right now.</summary>
    public WFPilotHandling GetHandling(EntityUid uid)
    {
        var handling = new WFPilotHandling();
        handling.Wander = _alcohol.DrunkPhase(uid) switch
        {
            WFDrugPhase.Light => 0.1f,
            WFDrugPhase.Heavy => 0.25f,
            WFDrugPhase.Overdose => 0.45f,
            _ => 0f,
        };

        if (!TryComp<WFIntoxicationComponent>(uid, out var comp))
            return handling;

        var stimulant = _intoxication.FamilyPhase(comp, WFDrugFamily.Stimulant);
        if (stimulant != WFDrugPhase.None)
        {
            handling.Angular *= stimulant == WFDrugPhase.Light ? 1.05f : 1.1f;
            handling.Acceleration *= stimulant == WFDrugPhase.Light ? 1.03f : 1.05f;
            handling.Twitch = stimulant == WFDrugPhase.Overdose;
        }
        else if (Crashing(comp, WFDrugFamily.Stimulant))
        {
            handling.Angular *= 0.88f;
            handling.Acceleration *= 0.92f;
        }

        handling.Lag = _intoxication.FamilyPhase(comp, WFDrugFamily.Opioid) switch
        {
            WFDrugPhase.Light => 0.25f,
            WFDrugPhase.Heavy => 0.6f,
            WFDrugPhase.Overdose => 1.2f,
            _ => 0f,
        };
        handling.Held = handling.Lag > 0f && _intoxication.IsNodding(comp);

        var (brakes, turn) = _intoxication.FamilyPhase(comp, WFDrugFamily.Cannabinoid) switch
        {
            WFDrugPhase.Light => (0.85f, 0.95f),
            WFDrugPhase.Heavy => (0.65f, 0.85f),
            WFDrugPhase.Overdose => (0.5f, 0.75f),
            _ => (1f, 1f),
        };
        handling.Brakes *= brakes;
        handling.Angular *= turn;
        return handling;
    }

    private bool Crashing(WFIntoxicationComponent comp, WFDrugFamily family)
    {
        foreach (var profile in _intoxication.ActiveCrashes(comp))
        {
            if (_proto.TryIndex(profile, out var proto) && proto.Family == family)
                return true;
        }

        return false;
    }

    private void Shape(EntityUid uid, ref GetShuttleInputsEvent args)
    {
        if (!args.GotInput)
            return;

        var handling = GetHandling(uid);
        args.AngularMul *= handling.Angular;
        args.AccelMul *= handling.Acceleration;

        if (!handling.ShapesInput)
        {
            RemCompDeferred<WFPilotImpairmentComponent>(uid);
            return;
        }

        if (!TryComp<WFPilotImpairmentComponent>(uid, out var state))
        {
            state = AddComp<WFPilotImpairmentComponent>(uid);
            state.Seed = _random.NextFloat(0f, 100f);
            state.NextTwitch = _random.NextFloat(2f, 5f);
        }

        var seconds = args.FrameTime;
        state.Time += seconds;
        var active = args.Input != null;
        var input = args.Input ?? new ShuttleInput(Vector2.Zero, 0f, 0f);
        var strafe = input.Strafe;
        var rotation = input.Rotation;
        var brakes = input.Brakes * handling.Brakes;

        if (active && handling.Wander > 0f)
        {
            var time = state.Time;
            var seed = state.Seed;
            rotation += handling.Wander * Sway(time, seed);
            strafe = new Angle(handling.Wander * SwaySkew * Sway(time * 0.8f, seed + 31f)).RotateVec(strafe);
        }

        if (handling.Twitch)
        {
            state.NextTwitch -= seconds;
            if (state.NextTwitch <= 0f)
            {
                state.NextTwitch = _random.NextFloat(2f, 5f);
                state.TwitchLeft = TwitchSeconds;
                state.TwitchTurn = _random.Prob(0.5f) ? 1f : -1f;
            }
        }

        if (state.TwitchLeft > 0f)
        {
            state.TwitchLeft -= seconds;
            if (active)
                rotation += state.TwitchTurn;
        }

        if (handling.Lag > 0f)
        {
            // The ship gets where the stick was a moment ago, and through a nod, where it was when the eyes shut.
            if (!handling.Held)
            {
                var follow = 1f - MathF.Exp(-seconds / handling.Lag);
                state.Strafe += (strafe - state.Strafe) * follow;
                state.Rotation += (rotation - state.Rotation) * follow;
                state.Brakes += (brakes - state.Brakes) * follow;
            }

            strafe = state.Strafe;
            rotation = state.Rotation;
            brakes = state.Brakes;
            active = strafe.LengthSquared() > Deadzone * Deadzone || MathF.Abs(rotation) > Deadzone || brakes > Deadzone;
        }
        else
        {
            state.Strafe = strafe;
            state.Rotation = rotation;
            state.Brakes = brakes;
        }

        if (!active)
        {
            args.Input = null;
            return;
        }

        // The pilot's handler leaves the speed limit out with no input; a stick still moving on its own keeps it.
        if (args.Input == null && TryComp<PilotComponent>(uid, out var pilot))
            args.SetMaxVelocity = pilot.SetMaxVelocity;

        args.Input = new ShuttleInput(strafe, Math.Clamp(rotation, -1f, 1f), brakes);
    }

    /// <summary>A slow, uneven sway between -1 and 1.</summary>
    private static float Sway(float time, float seed)
    {
        return 0.6f * MathF.Sin(0.9f * time + seed) + 0.4f * MathF.Sin(0.37f * time + 2.1f * seed);
    }
}

/// <summary>How a pilot's state changes the ship's handling.</summary>
public record struct WFPilotHandling()
{
    /// <summary>Multiplier on the ship's turn rate.</summary>
    public float Angular = 1f;

    /// <summary>Multiplier on the ship's thrust.</summary>
    public float Acceleration = 1f;

    /// <summary>Multiplier on the brake input.</summary>
    public float Brakes = 1f;

    /// <summary>Seconds the ship trails the stick by; zero for none.</summary>
    public float Lag;

    /// <summary>True through a nod: the stick stays where it was.</summary>
    public bool Held;

    /// <summary>How far the heading wanders, as a share of full turn input.</summary>
    public float Wander;

    /// <summary>The stick jerks to one side every few seconds.</summary>
    public bool Twitch;

    /// <summary>True when the input itself changes, not only the multipliers.</summary>
    public readonly bool ShapesInput => Lag > 0f || Wander > 0f || Twitch || !MathHelper.CloseTo(Brakes, 1f);
}
