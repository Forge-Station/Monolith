using System.Linq;
using Content.Server._WF.Remade.Counterplay;
using Content.Server.Body.Components;
using Content.Server.Zombies;
using Content.Shared._WF.Remade;
using Content.Shared.Body.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Components;
using Robust.Shared.Audio;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.Remade.Conversion;

/// <summary>
/// Dead animals turn like dead crew. DS13 sorts an animal by size, so a small one rises as a hopper and a big one as a lurker here;
/// each Remade's mass range is data on its <see cref="WFRemadeMobPrototype"/>.
/// </summary>
public sealed partial class WFRemadeConversionSystem
{
    private static readonly SoundSpecifier GibSound = new SoundCollectionSpecifier("gib");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFRemadeConvertingComponent, ComponentShutdown>(OnConvertingShutdown);
    }

    /// <summary>A shaking animal has no jitter status to run out, so its shake is taken off when the throes end.</summary>
    private void OnConvertingShutdown(Entity<WFRemadeConvertingComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent) && IsAnimal(ent))
            RemComp<JitteringComponent>(ent);
    }

    /// <summary>Whether this is a living thing that is neither crew, a Remade, a machine nor anyone's player.</summary>
    public bool IsAnimal(EntityUid uid)
    {
        return HasComp<MobStateComponent>(uid)
               && HasComp<BloodstreamComponent>(uid)
               && !HasComp<HumanoidAppearanceComponent>(uid)
               && !HasComp<WFRemadeComponent>(uid)
               && !HasComp<ZombieImmuneComponent>(uid)
               && !_minds.TryGetMind(uid, out _, out _);
    }

    /// <summary>Whether a living animal would turn if it died now: it has its head and some Remade fits its mass.</summary>
    public bool WouldTurnIfKilled(EntityUid uid)
    {
        return IsAnimal(uid)
               && !HasComp<WFNecrotoxinProtectedComponent>(uid)
               && HasHeadIfAny(uid)
               && AnimalWeights(uid).Count > 0;
    }

    /// <summary>Whether this can be turned at all: crew or an animal.</summary>
    public bool IsTurnable(EntityUid uid)
    {
        return HasComp<HumanoidAppearanceComponent>(uid) || IsAnimal(uid);
    }

    /// <summary>What a dead animal can rise as, by its mass.</summary>
    public Dictionary<WFRemadeMobPrototype, float> AnimalWeights(EntityUid animal)
    {
        var mass = TryComp<PhysicsComponent>(animal, out var physics) ? physics.FixturesMass : 0f;
        var weights = new Dictionary<WFRemadeMobPrototype, float>();
        foreach (var mob in _proto.EnumeratePrototypes<WFRemadeMobPrototype>())
        {
            if (mob.AnimalWeight > 0f && mass >= mob.AnimalMinMass && (mob.AnimalMaxMass <= 0f || mass <= mob.AnimalMaxMass))
                weights[mob] = mob.AnimalWeight;
        }

        return weights;
    }

    /// <summary>Shakes a body in the throes. A crewman's shake is a status, and an animal's is the bare component.</summary>
    private void Shake(EntityUid uid)
    {
        if (HasComp<HumanoidAppearanceComponent>(uid))
            _jitter.DoJitter(uid, PulseRate * 2, refresh: true, amplitude: 15f, frequency: 6f, forceValueChange: true);
        else
            _jitter.AddJitter(uid, 15f, 6f);
    }

    /// <summary>The corpse bursts. Its gear falls, and its organs and limbs go with it rather than spilling over the deck.</summary>
    private void Gib(EntityUid uid)
    {
        var xform = Transform(uid);
        foreach (var item in _inventory.GetHandOrInventoryEntities(uid).ToList())
        {
            _transform.DropNextTo(item, (uid, xform));
        }

        // The body's parts and organs would otherwise be dropped as it is deleted.
        if (HasComp<BodyComponent>(uid))
        {
            foreach (var organ in _body.GetBodyOrgans(uid).ToList())
            {
                QueueDel(organ.Id);
            }

            foreach (var part in _body.GetBodyChildren(uid).ToList())
            {
                QueueDel(part.Id);
            }
        }

        _audio.PlayPvs(GibSound, uid, AudioParams.Default.WithVolume(-8f).WithMaxDistance(10f));
        QueueDel(uid);
    }
}
