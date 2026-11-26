using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server._Mono.Radar;
using Content.Shared._WF.Planets.Jetpack; // WOLFGATE(Planets)
using Content.Shared.Atmos.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Collections;
using Robust.Shared.Timing;

namespace Content.Server.Movement.Systems;

public sealed partial class JetpackSystem : SharedJetpackSystem
{
    [Dependency] private GasTankSystem _gasTank = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityManager _entityManager = default!;
    [Dependency] private WFAtmosphericJetpackSystem _wfAtmosphericJetpack = default!; // WOLFGATE(Planets)

    public override void Initialize()
    {
        base.Initialize();

        // Subscribe to ActiveJetpackComponent events
        SubscribeLocalEvent<ActiveJetpackComponent, ComponentStartup>(OnJetpackActivated);
        SubscribeLocalEvent<ActiveJetpackComponent, ComponentShutdown>(OnJetpackDeactivated);
    }

    protected override bool CanEnable(EntityUid uid, EntityUid user, JetpackComponent component)
    {
        // WOLFGATE(Planets) START: an atmospheric pack burns welding fuel from a solution, not gas from a tank.
        if (TryComp<WFAtmosphericJetpackComponent>(uid, out var wfAtmospheric))
            return base.CanEnable(uid, user, component) && _wfAtmosphericJetpack.CanFly((uid, wfAtmospheric), user);
        // WOLFGATE END

        return base.CanEnable(uid, user, component) &&
               TryComp<GasTankComponent>(uid, out var gasTank) &&
               !(gasTank.Air.TotalMoles < component.MoleUsage);
    }

    /// <summary>
    /// Adds radar blip to jetpacks when they are activated - Mono
    /// </summary>
    private void OnJetpackActivated(EntityUid uid, ActiveJetpackComponent component, ComponentStartup args)
    {
        if (TryComp<JetpackComponent>(uid, out var jetpack) && jetpack.DetectionRange > 0)
        {
            var blip = EnsureComp<RadarBlipComponent>(uid);
            blip.Config.Color = Color.Cyan;
            blip.Config.Bounds = new(-0.25f, -0.25f, 0.25f, 0.25f);
            blip.VisibleFromOtherGrids = true;
            blip.MaxDistance = jetpack.DetectionRange;
        }
    }

    /// <summary>
    /// Removes radar blip from jetpacks when they are deactivated - Mono
    /// </summary>
    private void OnJetpackDeactivated(EntityUid uid, ActiveJetpackComponent component, ComponentShutdown args)
    {
        RemComp<RadarBlipComponent>(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var toDisable = new ValueList<(EntityUid Uid, JetpackComponent Component)>();
        var query = EntityQueryEnumerator<ActiveJetpackComponent, JetpackComponent, GasTankComponent>();

        while (query.MoveNext(out var uid, out var active, out var comp, out var gasTankComp))
        {
            if (_timing.CurTime < active.TargetTime)
                continue;

            // WOLFGATE(Planets) START: a wearer carried below orbit on a hull never changes parent, so the pack is cut here.
            // An atmospheric pack has no gas tank, so it never reaches this loop; WFAtmosphericJetpackFuelSystem burns it.
            if (comp.JetpackUser is { } wearer && WfInAtmosphere(wearer, uid))
            {
                toDisable.Add((uid, comp));
                continue;
            }
            // WOLFGATE END

            var gasTank = (uid, gasTankComp);
            active.TargetTime = _timing.CurTime + TimeSpan.FromSeconds(active.EffectCooldown);
            // Forge-Change-start
            var usage = comp.MoleUsage;
            if (comp.JetpackUser is { } user &&
                TryComp<JetpackUserComponent>(user, out var userComp))
            {
                usage *= userComp.SuitFuelUsageMultiplier;
            }
            // Forge-Change-end
            var usedAir = _gasTank.RemoveAir(gasTank, usage);

            if (usedAir == null)
                continue;

            var usedEnoughAir =
                MathHelper.CloseTo(usedAir.TotalMoles, usage, usage / 100); // Forge-Change

            if (!usedEnoughAir)
            {
                toDisable.Add((uid, comp));
            }

            _gasTank.UpdateUserInterface(gasTank);
        }

        foreach (var (uid, comp) in toDisable)
        {
            SetEnabled(uid, comp, false);
        }
    }
}
