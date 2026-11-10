using System.Threading.Tasks;
using Content.Server.Discord;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;

namespace Content.Server._WF.ChatWebhook;

/// <summary>
///     Chat channels that can be copied to a Discord webhook.
/// </summary>
public enum ChatWebhookChannel
{
    Dead,
    Looc,
}

/// <summary>
///     Raised by the chat system after a message is sent in a relayed channel.
/// </summary>
public readonly record struct ChatWebhookEvent(ChatWebhookChannel Channel, string Author, string Message);

/// <summary>
///     Copies dead chat and LOOC to Discord webhooks. One way: nothing comes back from Discord.
/// </summary>
public sealed partial class ChatWebhookSystem : EntitySystem
{
    private const int MaxContentLength = 2000;
    private const int MaxUsernameLength = 80;

    [Dependency] private DiscordWebhook _discord = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private readonly Dictionary<ChatWebhookChannel, Target> _targets = new()
    {
        [ChatWebhookChannel.Dead] = new(),
        [ChatWebhookChannel.Looc] = new(),
    };

    public override void Initialize()
    {
        SubscribeLocalEvent<ChatWebhookEvent>(OnChat);
        Subs.CVar(_cfg, CCVars.DeadChatWebhook, url => SetUrl(ChatWebhookChannel.Dead, url), true);
        Subs.CVar(_cfg, CCVars.LoocWebhook, url => SetUrl(ChatWebhookChannel.Looc, url), true);
    }

    private void SetUrl(ChatWebhookChannel channel, string url)
    {
        var target = _targets[channel];
        target.Url = url;
        target.Identifier = null;
    }

    private void OnChat(ChatWebhookEvent ev)
    {
        var target = _targets[ev.Channel];
        if (string.IsNullOrWhiteSpace(target.Url))
            return;

        _ = SendAsync(target, target.Url, ev);
    }

    private async Task SendAsync(Target target, string url, ChatWebhookEvent ev)
    {
        try
        {
            // Resolve the id and token once per URL.
            if (target.Identifier == null)
            {
                if (await _discord.GetWebhook(url) is not { } data)
                    return;

                if (url != target.Url)
                    return;

                target.Identifier = data.ToIdentifier();
            }

            var payload = new WebhookPayload
            {
                Username = Truncate(ev.Author, MaxUsernameLength),
                Content = Truncate(ev.Message, MaxContentLength),
            };

            await _discord.CreateMessage(target.Identifier.Value, payload);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to send {ev.Channel} chat to Discord: {e}");
        }
    }

    private static string Truncate(string value, int max)
    {
        return value.Length <= max ? value : value[..max];
    }

    private sealed class Target
    {
        public string Url = string.Empty;
        public WebhookIdentifier? Identifier;
    }
}
