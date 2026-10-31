using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Hive.Components;


[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HiveInjectorComponent : Component
{

    [DataField, AutoNetworkedField]
    public bool Used;


    [DataField]
    public bool DeleteOnUse;


    [DataField]
    public TimeSpan InjectTime = TimeSpan.FromSeconds(2);

    [DataField]
    public SoundSpecifier? InjectSound = new SoundPathSpecifier("/Audio/Items/hypospray.ogg");
}
