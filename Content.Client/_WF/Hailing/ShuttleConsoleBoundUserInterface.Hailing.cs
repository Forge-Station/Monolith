using Content.Client._WF.Hailing;
using Content.Shared._WF.Hailing;
using Robust.Client.UserInterface;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    private void WfHailOpen()
    {
        if (_window == null)
            return;

        _window.WfHailAttach(this.CreateDisposableControl<HailWindow>());

        _window.HailTargetsRequested += () => SendMessage(new HailTargetsRequestMessage());
        _window.HailRequested += ship => SendMessage(new HailRequestMessage(ship));
        _window.HailAnswerRequested += accept => SendMessage(new HailAnswerMessage(accept));
        _window.HailEndRequested += () => SendMessage(new HailEndMessage());
        _window.HailSayRequested += text => SendMessage(new HailSayMessage(text));
    }

    private void WfHailReceiveMessage(BoundUserInterfaceMessage message)
    {
        switch (message)
        {
            case HailTargetsMessage targets:
                _window?.WfHailTargets(targets.Targets);
                break;

            case HailEndedMessage ended:
                _window?.WfHailEnded(ended.Reason, ended.Ship);
                break;
        }
    }
}
