using System.Linq;
using System.Reflection;
using Content.Server.Xenoarchaeology.XenoArtifacts;
using Content.Shared.Whitelist;
using Content.Shared.Xenoarchaeology.XenoArtifacts;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Trade;

public sealed class ForgeArtifactStudyPresetSystem : EntitySystem
{
    private const int FirstNodeId = 100;

    private static readonly FieldInfo NodeTreeField =
        typeof(ArtifactComponent).GetField("NodeTree", BindingFlags.Instance | BindingFlags.Public)!;

    private static readonly FieldInfo CurrentNodeIdField =
        typeof(ArtifactComponent).GetField("CurrentNodeId", BindingFlags.Instance | BindingFlags.Public)!;

    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private EntityWhitelistSystem _whitelistSystem = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ForgeArtifactStudyPresetComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, ForgeArtifactStudyPresetComponent component, MapInitEvent args)
    {
        Timer.Spawn(0,
            () =>
            {
                if (!Deleted(uid) && TryComp(uid, out ForgeArtifactStudyPresetComponent? preset))
                    ApplyPreset(uid, preset);
            });
    }

    public bool ApplyPreset(EntityUid uid, ForgeArtifactStudyPresetComponent component)
    {
        if (component.Applied)
            return true;

        if (!TryComp(uid, out ArtifactComponent? artifact))
            return false;

        var triggers = GetValidTriggers(uid, component);
        var effects = GetValidEffects(uid, component);
        if (triggers.Count == 0 || effects.Count == 0)
            return false;

        RemoveArtifactNodeComponents(uid);

        var nodeCount = PickNodeCount(component);
        var nodes = BuildConnectedNodeTree(nodeCount, triggers, effects, _random);
        if (!TryReplaceArtifactNodeTree(artifact, nodes))
            return false;

        nodes[0].Discovered = true;

        ApplyNodeComponents(uid, nodes[0]);
        component.Applied = true;

        return true;
    }

    private int PickNodeCount(ForgeArtifactStudyPresetComponent component)
    {
        if (component.NodeCountMin <= 0 && component.NodeCountMax <= 0)
            return Math.Max(1, component.NodeCount);

        var min = Math.Max(1, component.NodeCountMin);
        var max = Math.Max(min, component.NodeCountMax);
        return _random.Next(min, max + 1);
    }

    private static bool TryReplaceArtifactNodeTree(ArtifactComponent artifact, List<ArtifactNode> nodes)
    {
        if (NodeTreeField.GetValue(artifact) is not List<ArtifactNode> nodeTree)
            return false;

        nodeTree.Clear();
        nodeTree.AddRange(nodes);
        CurrentNodeIdField.SetValue(artifact, nodes[0].Id);
        return true;
    }

    private List<string> GetValidTriggers(EntityUid uid, ForgeArtifactStudyPresetComponent component)
    {
        return component.Triggers.Count == 0
            ? GetDefaultTriggerIds(uid)
            : GetConfiguredTriggerIds(uid, component.Triggers);
    }

    private List<string> GetDefaultTriggerIds(EntityUid uid)
    {
        var triggers = new List<string>();

        foreach (var trigger in _prototype.EnumeratePrototypes<ArtifactTriggerPrototype>())
        {
            if (IsAllowedForArtifact(uid, trigger.Whitelist, trigger.Blacklist))
                triggers.Add(trigger.ID);
        }

        return triggers;
    }

    private List<string> GetConfiguredTriggerIds(EntityUid uid, IReadOnlyList<string> configuredTriggers)
    {
        var triggers = new List<string>();

        foreach (var trigger in configuredTriggers)
        {
            if (!_prototype.TryIndex<ArtifactTriggerPrototype>(trigger, out var prototype))
            {
                Log.Warning($"Forge artifact study preset references missing trigger prototype '{trigger}'.");
                continue;
            }

            if (IsAllowedForArtifact(uid, prototype.Whitelist, prototype.Blacklist))
                triggers.Add(trigger);
        }

        return triggers;
    }

    private List<string> GetValidEffects(EntityUid uid, ForgeArtifactStudyPresetComponent component)
    {
        var excluded = new HashSet<string>(component.ExcludedEffects);
        return component.Effects.Count == 0
            ? GetDefaultEffectIds(uid, excluded)
            : GetConfiguredEffectIds(uid, component.Effects, excluded);
    }

    private List<string> GetDefaultEffectIds(EntityUid uid, IReadOnlySet<string> excluded)
    {
        var effects = new List<string>();

        foreach (var effect in _prototype.EnumeratePrototypes<ArtifactEffectPrototype>())
        {
            if (excluded.Contains(effect.ID))
                continue;

            if (IsAllowedForArtifact(uid, effect.Whitelist, effect.Blacklist))
                effects.Add(effect.ID);
        }

        return effects;
    }

    private List<string> GetConfiguredEffectIds(
        EntityUid uid,
        IReadOnlyList<string> configuredEffects,
        IReadOnlySet<string> excluded
    )
    {
        var effects = new List<string>();

        foreach (var effect in configuredEffects)
        {
            if (!_prototype.TryIndex<ArtifactEffectPrototype>(effect, out var prototype))
            {
                Log.Warning($"Forge artifact study preset references missing effect prototype '{effect}'.");
                continue;
            }

            if (excluded.Contains(effect))
                continue;

            if (IsAllowedForArtifact(uid, prototype.Whitelist, prototype.Blacklist))
                effects.Add(effect);
        }

        return effects;
    }

    private bool IsAllowedForArtifact(EntityUid uid, EntityWhitelist? whitelist, EntityWhitelist? blacklist)
    {
        return _whitelistSystem.IsWhitelistPassOrNull(whitelist, uid) &&
            _whitelistSystem.IsBlacklistFailOrNull(blacklist, uid);
    }

    private static List<ArtifactNode> BuildConnectedNodeTree(
        int nodeCount,
        IReadOnlyList<string> triggers,
        IReadOnlyList<string> effects,
        IRobustRandom random
    )
    {
        var nodes = new List<ArtifactNode>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            var node = new ArtifactNode
            {
                Id = FirstNodeId + i,
                Depth = i,
                Trigger = random.Pick(triggers),
                Effect = random.Pick(effects),
            };

            if (i > 0)
                node.Edges.Add(FirstNodeId + i - 1);

            if (i + 1 < nodeCount)
                node.Edges.Add(FirstNodeId + i + 1);

            nodes.Add(node);
        }

        return nodes;
    }

    private void RemoveArtifactNodeComponents(EntityUid uid)
    {
        var componentNames = GetArtifactNodeComponentNames();
        var entityPrototype = MetaData(uid).EntityPrototype;
        foreach (var name in componentNames)
        {
            RestoreOrRemoveNodeComponent(uid, name, entityPrototype);
        }
    }

    private HashSet<string> GetArtifactNodeComponentNames()
    {
        var componentNames = new HashSet<string>();
        foreach (var trigger in _prototype.EnumeratePrototypes<ArtifactTriggerPrototype>())
        {
            AddComponentNames(componentNames, trigger.Components);
        }

        foreach (var effect in _prototype.EnumeratePrototypes<ArtifactEffectPrototype>())
        {
            AddComponentNames(componentNames, effect.Components);
            AddComponentNames(componentNames, effect.PermanentComponents);
        }

        return componentNames;
    }

    private void RestoreOrRemoveNodeComponent(EntityUid uid, string name, EntityPrototype? entityPrototype)
    {
        var registration = Factory.GetRegistration(name);
        if (entityPrototype?.Components.TryGetComponent(name, out var prototypeComponent) ?? false)
        {
            RestorePrototypeComponent(uid, name, registration.Type, prototypeComponent);
            return;
        }

        if (HasComp(uid, registration.Type))
            RemComp(uid, registration.Type);
    }

    private void RestorePrototypeComponent(
        EntityUid uid,
        string name,
        Type componentType,
        IComponent prototypeComponent
    )
    {
        var component = (Component) Factory.GetComponent(name);
        var restored = (object?) component;
        _serialization.CopyTo(prototypeComponent, ref restored);

        if (HasComp(uid, componentType))
            RemComp(uid, componentType);

        if (restored is Component restoredComponent)
            AddComp(uid, restoredComponent);
    }

    private static void AddComponentNames(HashSet<string> target, ComponentRegistry components)
    {
        foreach (var (name, _) in components)
        {
            target.Add(name);
        }
    }

    private void ApplyNodeComponents(EntityUid uid, ArtifactNode node)
    {
        if (!_prototype.TryIndex<ArtifactTriggerPrototype>(node.Trigger, out var trigger) ||
            !_prototype.TryIndex<ArtifactEffectPrototype>(node.Effect, out var effect))
            return;

        foreach (var (name, entry) in effect.Components.Concat(effect.PermanentComponents).Concat(trigger.Components))
        {
            var registration = Factory.GetRegistration(name);
            var component = (Component)Factory.GetComponent(registration);
            var copied = (object?)component;
            _serialization.CopyTo(entry.Component, ref copied);

            if (HasComp(uid, registration.Type))
                RemComp(uid, registration.Type);

            if (copied is Component copiedComponent)
                AddComp(uid, copiedComponent);
        }
    }
}
