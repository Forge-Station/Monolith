using Content.Shared._Forge.LivingNpc;
using Content.Shared._Forge.LivingNpc.Components;
using Content.Shared.Nutrition.Components;

namespace Content.Server._Forge.LivingNpc;

public sealed partial class LivingNpcBrainSystem
{
    private LivingNpcIntent PickIntent(EntityUid uid, LivingNpcComponent npc, in LivingNpcWorld world, TimeSpan now)
    {
        var best = LivingNpcIntent.Idle;
        var bestScore = 0.05f;

        void Consider(LivingNpcIntent intent, float score)
        {
            if (score > bestScore)
            {
                best = intent;
                bestScore = score;
            }
        }

        var p = npc.Personality;
        var m = npc.Mood;
        var threatClose = world.Threat != null
            ? Math.Clamp(1f - world.ThreatDist / Math.Max(npc.VisionRange, 0.1f), 0f, 1f)
            : 0f;

        if (!string.IsNullOrEmpty(npc.QueuedSpeech))
            Consider(LivingNpcIntent.Converse, 1.35f);

        if (world.Threat != null)
        {
            Consider(LivingNpcIntent.Flee, m.Fear * (1.2f - p.Bravery) * (0.45f + threatClose));
            if (npc.WillFight)
                Consider(LivingNpcIntent.Fight, (m.Anger * 0.65f + p.Bravery * 0.55f) * threatClose * (1.05f - m.Fear * 0.5f));
        }

        if (world.Injured != null)
            Consider(LivingNpcIntent.Help, p.Agreeableness * (1f - m.Fear) * 0.7f);

        if (world.NearbyPerson != null)
        {
            var greeted = TryComp<LivingNpcSocialComponent>(uid, out var social) &&
                          social.Greeted.Contains(world.NearbyPerson.Value);
            var socialScore = p.Extraversion * (0.2f + m.SocialNeed) * (1f - m.Fear * 0.7f);
            if (world.NearbyPersonIsPlayer && !greeted)
                socialScore += 0.25f;
            Consider(LivingNpcIntent.Socialize, socialScore);
        }

        if (TryComp<HungerComponent>(uid, out var hunger) && hunger.CurrentThreshold <= HungerThreshold.Peckish)
        {
            var eat = hunger.CurrentThreshold <= HungerThreshold.Starving ? 0.75f : 0.35f;
            Consider(LivingNpcIntent.Eat, eat);
        }

        if (TryComp<ThirstComponent>(uid, out var thirst) && thirst.CurrentThirstThreshold <= ThirstThreshold.Thirsty)
            Consider(LivingNpcIntent.Eat, 0.4f);

        if (HasComp<LivingNpcRoutineComponent>(uid))
        {
            Consider(LivingNpcIntent.Work, p.Discipline * m.Energy * 0.5f * (world.WorkSpot != null || world.PatrolSpot != null ? 1f : 0.35f));
            Consider(LivingNpcIntent.Rest, (1f - m.Energy) * 0.55f);
        }

        if (world.Stranger != null)
            Consider(LivingNpcIntent.Investigate, p.Curiosity * 0.4f * (1f - m.Fear));

        Consider(LivingNpcIntent.Wander, 0.12f + (1f - p.Discipline) * 0.18f + (1f - m.Energy) * 0.05f);
        Consider(LivingNpcIntent.Fidget, 0.1f + p.Humor * 0.08f);
        Consider(LivingNpcIntent.Idle, 0.08f + (1f - p.Extraversion) * 0.08f);

        if (best == npc.CurrentIntent)
            return best;

        var minDuration = npc.CurrentIntent is LivingNpcIntent.Fight or LivingNpcIntent.Flee or LivingNpcIntent.Converse
            ? 0.2
            : 0.75;

        if ((now - npc.IntentStartedAt).TotalSeconds < minDuration && npc.IntentStartedAt != TimeSpan.Zero)
            return npc.CurrentIntent;

        if (npc.CurrentIntent is not (LivingNpcIntent.Idle or LivingNpcIntent.Fidget) &&
            bestScore < ScoreCurrent(npc, world) * 1.2f + 0.04f)
            return npc.CurrentIntent;

        return best;
    }

    private float ScoreCurrent(LivingNpcComponent npc, in LivingNpcWorld world)
    {
        return npc.CurrentIntent switch
        {
            LivingNpcIntent.Converse when !string.IsNullOrEmpty(npc.QueuedSpeech) => 1.2f,
            LivingNpcIntent.Flee when world.Threat != null => 0.7f,
            LivingNpcIntent.Fight when world.Threat != null => 0.7f,
            LivingNpcIntent.Work => npc.Personality.Discipline * 0.4f,
            LivingNpcIntent.Socialize when world.NearbyPerson != null => 0.35f,
            _ => 0.15f,
        };
    }
}
