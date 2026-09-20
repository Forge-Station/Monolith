using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Leviathans.Components;

/// <summary>
/// Huge space dragon leviathan with gravity, tempest and solar-flare abilities.
/// Starfire breath is wired separately via ActionGun.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VoidDrakeComponent : Component
{
    [DataField]
    public float GravityRange = 32f;

    [DataField]
    public float GravityPull = 8f;

    [DataField]
    public DamageSpecifier GravityDamage = new()
    {
        DamageDict = { ["Blunt"] = 12 }
    };

    [DataField]
    public TimeSpan GravityCooldown = TimeSpan.FromSeconds(14);

    [DataField]
    public float TempestRange = 28f;

    [DataField]
    public float TempestThrow = 14f;

    [DataField]
    public TimeSpan TempestStun = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan TempestCooldown = TimeSpan.FromSeconds(16);

    [DataField]
    public float FlareRange = 36f;

    [DataField]
    public TimeSpan FlareFlash = TimeSpan.FromSeconds(8);

    [DataField]
    public DamageSpecifier FlareDamage = new()
    {
        DamageDict = { ["Heat"] = 18 }
    };

    [DataField]
    public float FlareEmpEnergy = 80000f;

    [DataField]
    public TimeSpan FlareCooldown = TimeSpan.FromSeconds(22);

    [DataField]
    public TimeSpan NpcAbilityRangeCheck = TimeSpan.FromSeconds(1);

    [DataField]
    public float RamRadius = 16f;

    [DataField]
    public TimeSpan RamInterval = TimeSpan.FromSeconds(0.05);

    [DataField]
    public float HuntRange = 2048f;

    [DataField]
    public float HuntSpeed = 110f;

    [DataField]
    public int MinHealth = 100000;

    [DataField]
    public int MaxHealth = 1000000;

    [DataField]
    public float ShootRange = 220f;

    [DataField]
    public TimeSpan ShootInterval = TimeSpan.FromSeconds(1.6);

    public TimeSpan NextGravity;
    public TimeSpan NextTempest;
    public TimeSpan NextFlare;
    public TimeSpan NextNpcCheck;
    public TimeSpan NextRam;
    public TimeSpan NextShot;

    [DataField]
    public SoundSpecifier? SoundRoar = new SoundPathSpecifier("/Audio/Animals/space_dragon_roar.ogg")
    {
        Params = AudioParams.Default.WithVolume(4f)
    };

    [DataField]
    public SoundSpecifier? SoundFlare = new SoundPathSpecifier("/Audio/Magic/fireball.ogg");
}
