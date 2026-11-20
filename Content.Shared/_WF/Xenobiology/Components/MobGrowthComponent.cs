using System.Linq;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenobiology.Components;

/// <summary>
/// Grows a mob through stages, such as baby to adult, once it is fed enough.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MobGrowthComponent : Component
{
    /// <summary>
    /// Hunger needed to grow.
    /// </summary>
    [DataField(required: true)]
    public float HungerRequired;

    /// <summary>
    /// Hunger spent on growing.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float GrowthCost = -75f;

    [DataField(required: true), AutoNetworkedField]
    public string CurrentStage = string.Empty;

    [ViewVariables]
    public string FirstStage => Stages.Keys.FirstOrDefault() ?? string.Empty;

    [ViewVariables]
    public bool IsFirstStage => FirstStage == CurrentStage;

    [DataField(required: true), AutoNetworkedField]
    public Dictionary<string, GrowthStageData> Stages = new();

    [ViewVariables, AutoNetworkedField]
    public TimeSpan NextGrowthTime;

    [DataField, AutoNetworkedField]
    public TimeSpan GrowthInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The entity's name before the stage is put in front of it.
    /// </summary>
    [ViewVariables]
    public string BaseEntityName = string.Empty;
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class GrowthStageData
{
    [DataField]
    public string? NextStage { get; set; }

    [DataField]
    public string? Sprite { get; set; }

    [DataField]
    public string? DisplayName { get; set; } = string.Empty;
}

[Serializable, NetSerializable]
public enum GrowthStateVisuals : byte
{
    Sprite,
}

[Serializable, NetSerializable]
public enum XenoSlimeVisuals : byte
{
    Color,
    Shader,
}
