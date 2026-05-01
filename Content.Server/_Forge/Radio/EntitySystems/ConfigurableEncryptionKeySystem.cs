using System.Diagnostics.CodeAnalysis;
using Content.Server.Popups;
using Content.Shared.Examine;
using Content.Shared._Forge.Radio;
using Content.Shared._Forge.Radio.Components;
using Content.Shared.Radio.Components;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Utility;

namespace Content.Server._Forge.Radio.EntitySystems;

public sealed class ConfigurableEncryptionKeySystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EncryptionKeyHolderComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAlternativeVerbs);
        SubscribeLocalEvent<EncryptionKeyHolderComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<EncryptionKeyHolderComponent, SelectConfigurableEncryptionKeyFrequencyMessage>(OnSelectFrequency);
        SubscribeLocalEvent<ConfigurableEncryptionKeyComponent, ExaminedEvent>(OnKeyExamined);
    }

    public bool TryGetFrequency(
        EntityUid uid,
        string channel,
        out int frequency,
        EncryptionKeyHolderComponent? holder = null)
    {
        frequency = default;

        if (!Resolve(uid, ref holder, false))
            return false;

        if (!TryGetKey(uid, holder, channel, out _, out var configurable, out _))
            return false;

        frequency = configurable.Frequency;
        return true;
    }

    private void OnGetAlternativeVerbs(EntityUid uid, EncryptionKeyHolderComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (!TryGetKey(uid, component, out _, out _, out _))
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("configurable-encryption-key-verb-text"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/settings.svg.192dpi.png")),
            Act = () =>
            {
                UpdateUi(uid, component);
                _ui.TryToggleUi(uid, ConfigurableEncryptionKeyUiKey.Key, args.User);
            },
        });
    }

    private void OnUiOpened(EntityUid uid, EncryptionKeyHolderComponent component, BoundUIOpenedEvent args)
    {
        if (args.UiKey is not ConfigurableEncryptionKeyUiKey.Key)
            return;

        if (!TryGetKey(uid, component, out _, out _, out _))
        {
            _ui.CloseUi(uid, ConfigurableEncryptionKeyUiKey.Key, args.Actor);
            return;
        }

        UpdateUi(uid, component);
    }

    private void OnSelectFrequency(EntityUid uid, EncryptionKeyHolderComponent component, SelectConfigurableEncryptionKeyFrequencyMessage args)
    {
        if (!args.Actor.Valid)
            return;

        if (!TryGetKey(uid, component, out _, out var configurable, out _))
        {
            _ui.CloseUi(uid, ConfigurableEncryptionKeyUiKey.Key, args.Actor);
            return;
        }

        if (args.Frequency < 0)
        {
            UpdateUi(uid, component);
            return;
        }

        if (args.Frequency < configurable.MinFrequency || args.Frequency > configurable.MaxFrequency)
        {
            _popup.PopupEntity(
                Loc.GetString("configurable-encryption-key-frequency-out-of-range",
                    ("min", configurable.MinFrequency),
                    ("max", configurable.MaxFrequency)),
                uid,
                args.Actor);
            UpdateUi(uid, component);
            return;
        }

        if (args.Frequency != configurable.Frequency)
        {
            configurable.Frequency = args.Frequency;

            _popup.PopupEntity(
                Loc.GetString("configurable-encryption-key-frequency-set", ("frequency", configurable.Frequency)),
                uid,
                args.Actor);
        }

        UpdateUi(uid, component);
    }

    private void OnKeyExamined(EntityUid uid, ConfigurableEncryptionKeyComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("configurable-encryption-key-examine", ("frequency", component.Frequency)));
    }

    private void UpdateUi(EntityUid uid, EncryptionKeyHolderComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;

        if (!TryGetKey(uid, component, out _, out var configurable, out _))
            return;

        _ui.SetUiState(uid,
            ConfigurableEncryptionKeyUiKey.Key,
            new ConfigurableEncryptionKeyBoundUIState(
                configurable.Frequency,
                configurable.MinFrequency,
                configurable.MaxFrequency));
    }

    private bool TryGetKey(
        EntityUid uid,
        EncryptionKeyHolderComponent component,
        [NotNullWhen(true)] out EntityUid? keyUid,
        [NotNullWhen(true)] out ConfigurableEncryptionKeyComponent? configurable,
        [NotNullWhen(true)] out EncryptionKeyComponent? key)
    {
        keyUid = null;
        configurable = null;
        key = null;

        if (!component.Initialized)
            return false;

        foreach (var contained in component.KeyContainer.ContainedEntities)
        {
            if (!TryComp(contained, out ConfigurableEncryptionKeyComponent? configurableKey)
                || !TryComp(contained, out EncryptionKeyComponent? encryptionKey))
                continue;

            keyUid = contained;
            configurable = configurableKey;
            key = encryptionKey;
            return true;
        }

        return false;
    }

    private bool TryGetKey(
        EntityUid uid,
        EncryptionKeyHolderComponent component,
        string channel,
        [NotNullWhen(true)] out EntityUid? keyUid,
        [NotNullWhen(true)] out ConfigurableEncryptionKeyComponent? configurable,
        [NotNullWhen(true)] out EncryptionKeyComponent? key)
    {
        keyUid = null;
        configurable = null;
        key = null;

        if (!component.Initialized)
            return false;

        foreach (var contained in component.KeyContainer.ContainedEntities)
        {
            if (!TryComp(contained, out ConfigurableEncryptionKeyComponent? configurableKey)
                || !TryComp(contained, out EncryptionKeyComponent? encryptionKey)
                || !encryptionKey.Channels.Contains(channel)
                || configurableKey.Channel != channel)
                continue;

            keyUid = contained;
            configurable = configurableKey;
            key = encryptionKey;
            return true;
        }

        return false;
    }
}
