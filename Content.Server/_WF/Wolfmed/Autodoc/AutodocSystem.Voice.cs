using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared._WF.Wolfmed.Autodoc;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;

namespace Content.Server._WF.Wolfmed.Autodoc;

public sealed partial class AutodocSystem
{
    /// <summary>Which family of work a step line belongs to, so a procedure announces each kind once.</summary>
    private static readonly Dictionary<AutodocVoiceEvent, AutodocStepFamily> StepFamilies = new()
    {
        [AutodocVoiceEvent.StepIncision] = AutodocStepFamily.Incision,
        [AutodocVoiceEvent.StepRetract] = AutodocStepFamily.Incision,
        [AutodocVoiceEvent.StepClamp] = AutodocStepFamily.Bleeding,
        [AutodocVoiceEvent.StepCauterise] = AutodocStepFamily.Bleeding,
        [AutodocVoiceEvent.StepSaw] = AutodocStepFamily.Bone,
        [AutodocVoiceEvent.StepDrill] = AutodocStepFamily.Bone,
        [AutodocVoiceEvent.StepSetBone] = AutodocStepFamily.Bone,
        [AutodocVoiceEvent.StepBoneGel] = AutodocStepFamily.Bone,
        [AutodocVoiceEvent.StepSuture] = AutodocStepFamily.Closing,
        [AutodocVoiceEvent.StepClose] = AutodocStepFamily.Closing,
        [AutodocVoiceEvent.StepEvisceration] = AutodocStepFamily.Closing,
        [AutodocVoiceEvent.StepRemovePart] = AutodocStepFamily.Part,
        [AutodocVoiceEvent.StepAttachPart] = AutodocStepFamily.Part,
        [AutodocVoiceEvent.StepAmputate] = AutodocStepFamily.Part,
        [AutodocVoiceEvent.StepRemoveOrgan] = AutodocStepFamily.Organ,
        [AutodocVoiceEvent.StepInsertOrgan] = AutodocStepFamily.Organ,
        [AutodocVoiceEvent.StepEmbedded] = AutodocStepFamily.Organ,
        [AutodocVoiceEvent.StepCavity] = AutodocStepFamily.Organ,
        [AutodocVoiceEvent.StepWeld] = AutodocStepFamily.Mechanical,
        [AutodocVoiceEvent.StepWrench] = AutodocStepFamily.Mechanical,
        [AutodocVoiceEvent.StepWire] = AutodocStepFamily.Mechanical,
    };

    /// <summary>
    /// One event, one line: the pod picks a variant and either speaks it now, queues it, or drops it,
    /// depending on the line's priority. Nothing ever plays over anything else, so the ogg and the chat
    /// transcript are emitted together at the moment the line actually starts.
    /// </summary>
    public void Speak(Entity<AutodocComponent> ent, AutodocVoiceEvent voiceEvent, params (string, object)[] args)
    {
        CountVoice(ent, voiceEvent); // Playtest 3 SAM
        if (!_protos.TryIndex(ent.Comp.Voice, out var voice) ||
            !voice.Events.TryGetValue(voiceEvent, out var ids) ||
            ids.Count == 0)
            return;

        var id = _random.Pick(ids);
        if (!voice.Lines.TryGetValue(id, out var line))
            return;

        var busy = IsSpeaking(ent);
        switch (line.Priority)
        {
            case AutodocVoicePriority.Urgent:
                // Cuts the current line off mid-word and takes its place; the queue behind it survives.
                _audio.Stop(ent.Comp.VoiceStream);
                ent.Comp.VoiceStream = null;
                PlayLine(ent, line, args);
                return;

            case AutodocVoicePriority.Step:
                if (busy || ent.Comp.VoiceQueue.Count > 0)
                    return;
                break;

            case AutodocVoicePriority.Chatter:
                if (busy || ent.Comp.VoiceQueue.Count > 0 ||
                    _timing.CurTime < ent.Comp.VoiceBusyUntil + TimeSpan.FromSeconds(ent.Comp.VoiceChatterSilence))
                    return;
                break;
        }

        if (!busy && ent.Comp.VoiceQueue.Count == 0)
        {
            PlayLine(ent, line, args);
            return;
        }

        if (ent.Comp.VoiceQueue.Count >= ent.Comp.VoiceQueueMax)
            return;

        ent.Comp.VoiceQueue.Add(new AutodocVoiceRequest(id, args));
    }

    /// <summary>A step line, at most one per family per procedure.</summary>
    private void SpeakStep(Entity<AutodocComponent> ent, AutodocVoiceEvent voiceEvent)
    {
        var family = StepFamilies.GetValueOrDefault(voiceEvent, AutodocStepFamily.Other);
        // Playtest 3 SAM: a procedure's first step line is its announcement; a closure the pod added is part of the one before.
        if (ent.Comp.SpokenFamilies.Count == 0 && ent.Comp.Queue.Count > 0 && !ent.Comp.Queue[0].Continuation)
            ent.Comp.ProceduresAnnounced++;

        if (!ent.Comp.SpokenFamilies.Add(family))
            return;

        Speak(ent, voiceEvent);
    }

    /// <summary>True while a line is still playing.</summary>
    public bool IsSpeaking(Entity<AutodocComponent> ent) => _timing.CurTime < ent.Comp.VoiceBusyUntil;

    /// <summary>How long a line lasts, read off the ogg so the queue can never outrun the audio.</summary>
    public TimeSpan GetLineLength(AutodocVoiceLine line) => _audio.GetAudioLength(_audio.ResolveSound(line.Sound));

    private void PlayLine(Entity<AutodocComponent> ent, AutodocVoiceLine line, (string, object)[] args)
    {
        var stream = _audio.PlayPvs(line.Sound, ent.Owner, AudioParams.Default.WithVolume(ent.Comp.VoiceGain));
        ent.Comp.VoiceStream = stream?.Entity;
        ent.Comp.VoiceBusyUntil = _timing.CurTime + GetLineLength(line);
        ent.Comp.VoiceSpoken++;
        var text = Loc.GetString(line.Message, args);
        ent.Comp.LastLine = text;
        _chat.TrySendInGameICMessage(ent.Owner, text, InGameICChatType.Speak, false);
        UpdateUi(ent);
    }

    /// <summary>Starts the next queued line once the one before it has finished.</summary>
    private void TickVoice(Entity<AutodocComponent> ent)
    {
        if (ent.Comp.VoiceQueue.Count == 0 || IsSpeaking(ent))
            return;

        var next = ent.Comp.VoiceQueue[0];
        ent.Comp.VoiceQueue.RemoveAt(0);

        if (_protos.TryIndex(ent.Comp.Voice, out var voice) && voice.Lines.TryGetValue(next.Line, out var line))
            PlayLine(ent, line, next.Args);
    }

    /// <summary>Volume for a step or tool sound: quieter while S.A.M. is talking over it.</summary>
    private AudioParams DuckedParams(Entity<AutodocComponent> ent, AudioParams param)
    {
        param = param.AddVolume(ent.Comp.ToolVolume);
        return IsSpeaking(ent)
            ? param.AddVolume(SharedAudioSystem.GainToVolume(ent.Comp.DuckedToolGain))
            : param;
    }

    /// <summary>Small talk while there is somebody in the pod and nothing to do.</summary>
    private void TickIdleChatter(Entity<AutodocComponent> ent)
    {
        if (ent.Comp.State is not (AutodocState.Idle or AutodocState.Complete) ||
            !IsPowered(ent) ||
            GetOccupant(ent) == null)
            return;

        if (ent.Comp.NextIdleChatter == TimeSpan.Zero)
        {
            ent.Comp.NextIdleChatter = _timing.CurTime + TimeSpan.FromSeconds(
                _random.NextFloat(ent.Comp.IdleChatterMin, ent.Comp.IdleChatterMax));
            return;
        }

        if (_timing.CurTime < ent.Comp.NextIdleChatter)
            return;

        ent.Comp.NextIdleChatter = _timing.CurTime + TimeSpan.FromSeconds(
            _random.NextFloat(ent.Comp.IdleChatterMin, ent.Comp.IdleChatterMax));
        Speak(ent, IsEmagged(ent) ? AutodocVoiceEvent.Emag : AutodocVoiceEvent.Idle);
    }
}
