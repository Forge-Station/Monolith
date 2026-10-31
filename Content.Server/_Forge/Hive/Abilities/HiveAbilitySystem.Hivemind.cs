using Content.Shared._Forge.Hive;
using Content.Shared._Forge.Hive.Components;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Server._Forge.Hive.Abilities;


public sealed partial class HiveAbilitySystem
{

    private int _hiveDnaBank;

    private void InitializeHivemind()
    {
        SubscribeLocalEvent<HiveMemberComponent, HiveHivemindLinkEvent>(OnHivemindLink);
        SubscribeLocalEvent<HiveMemberComponent, HiveDnaChannelEvent>(OnDnaChannel);
        SubscribeLocalEvent<HiveLinkedComponent, ComponentShutdown>(OnLinkedShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _hiveDnaBank = 0);
    }

    #region Связь Улья

    private void OnHivemindLink(Entity<HiveMemberComponent> ent, ref HiveHivemindLinkEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<PullerComponent>(ent, out var puller) || puller.Pulling is not { } target)
        {
            _popup.PopupEntity(Loc.GetString("hive-link-need-grab"), ent, ent);
            return;
        }

        if (!HasComp<MobStateComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("hive-link-invalid", ("target", target)), ent, ent);
            return;
        }

        if (HasComp<HiveMemberComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("hive-link-already-member", ("target", target)), ent, ent);
            return;
        }

        args.Handled = true;
        var channel = ent.Comp.HivemindChannel;

        if (TryComp<HiveLinkedComponent>(target, out var existing))
        {
            existing.EndTime = _timing.CurTime + args.Duration;
            existing.LinkedBy = ent.Owner;
        }
        else
        {
            var linked = AddComp<HiveLinkedComponent>(target);
            linked.EndTime = _timing.CurTime + args.Duration;
            linked.LinkedBy = ent.Owner;
            linked.Channel = channel;
            _collectiveMind.AddCollectiveMind(target, channel);
        }

        _popup.PopupEntity(Loc.GetString("hive-link-success", ("target", target)), ent, ent, PopupType.Medium);
        _popup.PopupEntity(Loc.GetString("hive-link-victim"), target, target, PopupType.LargeCaution);

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(ent):user} linked {ToPrettyString(target):target} to the Hive mind for {args.Duration}");
    }

    private void UpdateHivemind(TimeSpan now)
    {
        var query = EntityQueryEnumerator<HiveLinkedComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now >= comp.EndTime)
                RemCompDeferred<HiveLinkedComponent>(uid);
        }
    }

    private void OnLinkedShutdown(Entity<HiveLinkedComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;


        if (HasComp<HiveMemberComponent>(ent))
            return;

        _collectiveMind.RemoveCollectiveMind(ent.Owner, ent.Comp.Channel);
        _popup.PopupEntity(Loc.GetString("hive-link-expired"), ent, ent, PopupType.Medium);
    }

    #endregion

    #region Канал поглощённого ДНК

    private void OnDnaChannel(Entity<HiveMemberComponent> ent, ref HiveDnaChannelEvent args)
    {
        if (args.Handled || !TryComp<ActorComponent>(ent, out var actor))
            return;

        args.Handled = true;

        var own = _hive.GetDna(ent.Owner).Int();
        var prompt = Loc.GetString("hive-dna-channel-prompt", ("own", own), ("bank", _hiveDnaBank));
        var user = ent.Owner;


        _quickDialog.OpenDialog<string>(actor.PlayerSession, Loc.GetString("hive-dna-channel-title"), prompt,
            input => OnDnaChannelInput(user, input));
    }

    private void OnDnaChannelInput(EntityUid user, string input)
    {
        if (TerminatingOrDeleted(user) || !HasComp<HiveMemberComponent>(user))
            return;

        if (!int.TryParse(input.Trim(), out var amount) || amount == 0)
        {
            _popup.PopupEntity(Loc.GetString("hive-dna-channel-invalid"), user, user);
            return;
        }

        if (amount > 0)
        {
            // Отдаём Улью.
            if (_hive.GetDna(user) < amount)
            {
                _popup.PopupEntity(Loc.GetString("hive-dna-channel-not-enough-own"), user, user);
                return;
            }

            _hive.AddDna(user, -amount);
            _hiveDnaBank += amount;
            _popup.PopupEntity(Loc.GetString("hive-dna-channel-deposited", ("amount", amount), ("bank", _hiveDnaBank)), user, user);
        }
        else
        {

            var take = -amount;
            if (_hiveDnaBank < take)
            {
                _popup.PopupEntity(Loc.GetString("hive-dna-channel-not-enough-bank", ("bank", _hiveDnaBank)), user, user);
                return;
            }

            _hiveDnaBank -= take;
            _hive.AddDna(user, take);
            _popup.PopupEntity(Loc.GetString("hive-dna-channel-withdrawn", ("amount", take), ("bank", _hiveDnaBank)), user, user);
        }

        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):user} moved {amount} DNA to the Hive bank (now {_hiveDnaBank})");
    }

    #endregion
}
