using Robust.Shared.Configuration;

namespace Content.Shared._WF.Wolfmed.CCVar;

/// <summary>
/// Wolfmed settings.
/// </summary>
[CVarDefs]
public sealed class WolfmedCVars
{
    /// <summary>
    /// Plays a looping heartbeat while the local player's own body is in critical condition.
    /// </summary>
    public static readonly CVarDef<bool> CritHeartbeat =
        CVarDef.Create("wolfmed.crit_heartbeat", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Shock damage an EMP deals to each machine body part it reaches (cybernetic limbs, IPC parts). Zero turns
    /// EMP damage to bodies off.
    /// </summary>
    public static readonly CVarDef<float> EmpPartDamage =
        CVarDef.Create("wolfmed.emp_part_damage", 40f, CVar.SERVERONLY);

    /// <summary>
    /// Most one EMP deals to one body in total. The parts share it, so a full chassis takes this much and a lone
    /// cybernetic limb takes wolfmed.emp_part_damage. Zero removes the ceiling.
    /// </summary>
    public static readonly CVarDef<float> EmpBodyDamage =
        CVarDef.Create("wolfmed.emp_body_damage", 120f, CVar.SERVERONLY);

    /// <summary>
    /// Multiplier on every wound's bleed rate. Applied where the rate is computed, so the analyzer, the spurts
    /// and the bloodstream all see the same slowed figure. 0.3 since playtest 1: one untreated arterial arm cut
    /// took about five minutes from Up to arrest. 0.25 since playtest 5, "nerf bloodloss just a bit, you should last
    /// longer in fights": about six minutes.
    /// </summary>
    public static readonly CVarDef<float> BleedRate =
        CVarDef.Create("wolfmed.bleed_rate", 0.25f, CVar.SERVERONLY);

    /// <summary>
    /// Corpse ceiling (M1b): a DEAD wound host's total damage from damage nobody dealt (fire, atmosphere) stops
    /// here; the living are held by the per-part ceiling instead (wolfmed.ambient_part_cap_fraction). High
    /// enough that any one limb can still reach its amputation threshold on a dead body. Zero disables it.
    /// </summary>
    public static readonly CVarDef<float> BodyDamageCap =
        CVarDef.Create("wolfmed.body_damage_cap", 600f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Whether open wounds become infected at all (W5). False leaves existing infections in place and
    /// stops them progressing, so turning it off mid-round is safe.
    /// </summary>
    public static readonly CVarDef<bool> InfectionEnabled =
        CVarDef.Create("wolfmed.infection_enabled", true, CVar.SERVERONLY);

    /// <summary>Multiplier on every infection timer. The knob for tuning, and for accelerating a test.</summary>
    public static readonly CVarDef<float> InfectionRate =
        CVarDef.Create("wolfmed.infection_rate", 1f, CVar.SERVERONLY);

    /// <summary>
    /// Whether an infection that has reached the torso or head can go systemic. False caps the model at the fever
    /// stage; part infections still travel.
    /// </summary>
    public static readonly CVarDef<bool> SepsisEnabled =
        CVarDef.Create("wolfmed.sepsis_enabled", true, CVar.SERVERONLY);

    /// <summary>Whether tourniquets, deep burns and late reattachments can kill a limb.</summary>
    public static readonly CVarDef<bool> NecrosisEnabled =
        CVarDef.Create("wolfmed.necrosis_enabled", true, CVar.SERVERONLY);

    /// <summary>Multiplier on every necrosis timer, including the tourniquet clock.</summary>
    public static readonly CVarDef<float> NecrosisRate =
        CVarDef.Create("wolfmed.necrosis_rate", 1f, CVar.SERVERONLY);

    /// <summary>
    /// Whether wounds and dismemberment make a noise (V1/V2). False silences them without touching any
    /// other part of the model.
    /// </summary>
    public static readonly CVarDef<bool> WoundSfx =
        CVarDef.Create("wolfmed.wound_sfx", true, CVar.SERVERONLY);

    /// <summary>Whether hits that wound throw blood mist or sparks (V4). The one knob for that spawn cost.</summary>
    public static readonly CVarDef<bool> HitDebris =
        CVarDef.Create("wolfmed.hit_debris", true, CVar.SERVERONLY);

    /// <summary>
    /// Whether a cut artery or an open stump throws blood around every few seconds (G2; playtest 5: only those,
    /// an ordinary bleed drips). False leaves the bleeding model alone and only stops the spectacle.
    /// </summary>
    public static readonly CVarDef<bool> BleedSpurts =
        CVarDef.Create("wolfmed.bleed_spurts", true, CVar.SERVERONLY);

    /// <summary>Whether bullets can stray from the aimed body part, by gun spread and range.</summary>
    public static readonly CVarDef<bool> AimScatter =
        CVarDef.Create("wolfmed.aim_scatter", true, CVar.SERVERONLY);

    /// <summary>Chance to hit the aimed part with a tight gun or at point blank.</summary>
    public static readonly CVarDef<float> AimBestChance =
        CVarDef.Create("wolfmed.aim_best_chance", 0.75f, CVar.SERVERONLY);

    /// <summary>Floor for the chance to hit the aimed part, however wide the spread or long the range.</summary>
    public static readonly CVarDef<float> AimWorstChance =
        CVarDef.Create("wolfmed.aim_worst_chance", 0.1f, CVar.SERVERONLY);

    /// <summary>Whether a blast can tear limbs off outright.</summary>
    public static readonly CVarDef<bool> BlastDismember =
        CVarDef.Create("wolfmed.blast_dismember", true, CVar.SERVERONLY);

    /// <summary>Blast damage to one body below which no limb is ever torn off.</summary>
    public static readonly CVarDef<float> BlastDismemberMin =
        CVarDef.Create("wolfmed.blast_dismember_min", 30f, CVar.SERVERONLY);

    /// <summary>Blast damage at which the per-limb chance peaks. Every multiple of it also rolls one more limb.</summary>
    public static readonly CVarDef<float> BlastDismemberFull =
        CVarDef.Create("wolfmed.blast_dismember_full", 150f, CVar.SERVERONLY);

    /// <summary>Peak chance for a rolled limb to come off.</summary>
    public static readonly CVarDef<float> BlastDismemberChance =
        CVarDef.Create("wolfmed.blast_dismember_chance", 0.8f, CVar.SERVERONLY);

    /// <summary>Whether a blast may take the head as well.</summary>
    public static readonly CVarDef<bool> BlastDismemberHead =
        CVarDef.Create("wolfmed.blast_dismember_head", false, CVar.SERVERONLY);

    /// <summary>Client: the fading-out view and camera sway when close to death.</summary>
    public static readonly CVarDef<bool> DyingEffects =
        CVarDef.Create("wolfmed.dying_effects", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Whether consciousness owns Alive/Critical on wound hosts (CONSC). False hands mob state back to the
    /// damage thresholds, which is the pre-CONSC behaviour.
    /// </summary>
    public static readonly CVarDef<bool> Consciousness =
        CVarDef.Create("wolfmed.consciousness", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Effective pain, as a share of the soft pain cap, at which a body goes Downed.</summary>
    public static readonly CVarDef<float> ConsciousnessPainDown =
        CVarDef.Create("wolfmed.consc_pain_down", 0.95f, CVar.SERVERONLY);

    /// <summary>
    /// Pain before the soft clamp, as a share of the soft pain cap, at which a body goes unconscious. Onyx's
    /// PainComponent has no hard cap, so this is measured on the sum of the parts.
    /// </summary>
    public static readonly CVarDef<float> ConsciousnessPainOut =
        CVarDef.Create("wolfmed.consc_pain_out", 1.4f, CVar.SERVERONLY);

    /// <summary>Blood volume fraction at or below which a body goes Downed.</summary>
    public static readonly CVarDef<float> ConsciousnessBloodDown =
        CVarDef.Create("wolfmed.consc_blood_down", 0.5f, CVar.SERVERONLY);

    /// <summary>Blood volume fraction at or below which a body goes unconscious.</summary>
    public static readonly CVarDef<float> ConsciousnessBloodOut =
        CVarDef.Create("wolfmed.consc_blood_out", 0.35f, CVar.SERVERONLY);

    /// <summary>
    /// How far an input has to fall back below the value that caused a state before that state is left, as a
    /// share of the entry value. Keeps a bleeding body from flickering in and out of Downed.
    /// </summary>
    public static readonly CVarDef<float> ConsciousnessHysteresis =
        CVarDef.Create("wolfmed.consc_hysteresis", 0.1f, CVar.SERVERONLY);

    /// <summary>Blood volume fraction at or under which the heart stops (BRAIN).</summary>
    public static readonly CVarDef<float> ArrestBlood =
        CVarDef.Create("wolfmed.arrest_blood", 0.30f, CVar.SERVERONLY);

    /// <summary>Brain oxygenation at or under which the heart stops.</summary>
    public static readonly CVarDef<float> ArrestOxygenation =
        CVarDef.Create("wolfmed.arrest_oxygenation", 0.15f, CVar.SERVERONLY);

    /// <summary>
    /// Blood volume fraction at or under which a pain shock stops the heart instead of stunning. Zero turns
    /// that trigger off (M1a: pain is never a route to death).
    /// </summary>
    public static readonly CVarDef<float> ArrestShockBlood =
        CVarDef.Create("wolfmed.arrest_shock_blood", 0f, CVar.SERVERONLY);

    /// <summary>
    /// Sepsis progress at or past which sepsis is a route on the analyzer and, with wolfmed.brain_sepsis_seconds or
    /// wolfmed.arrest_sepsis_chance set, the heart can stop on its own. Playtest 5 ships both of those at 0.
    /// </summary>
    public static readonly CVarDef<float> ArrestSepsis =
        CVarDef.Create("wolfmed.arrest_sepsis", 80f, CVar.SERVERONLY);

    /// <summary>
    /// Chance per second that late sepsis stops the heart. 0 since M2 (OD15): sepsis at <see cref="ArrestSepsis"/>
    /// drains the brain and the arrest arrives through the oxygen trigger, on the same clock every time.
    /// </summary>
    public static readonly CVarDef<float> ArrestSepsisChance =
        CVarDef.Create("wolfmed.arrest_sepsis_chance", 0f, CVar.SERVERONLY);

    /// <summary>Shock damage in one electrocution at or past which the heart stops. Zero turns that off.</summary>
    public static readonly CVarDef<float> ArrestShockDamage =
        CVarDef.Create("wolfmed.arrest_shock_damage", 60f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds of a stopped heart that drain brain oxygenation from full to nothing. 180 since playtest 5 ("cardiac
    /// arrest onsets too fast, people dying is way more work to fix"; was 120): with wolfmed.brain_damage_rate 0.05,
    /// brain death is about 7.5 minutes after an arrest from a full brain, was 4.
    /// </summary>
    public static readonly CVarDef<float> BrainArrestSeconds =
        CVarDef.Create("wolfmed.brain_arrest_seconds", 180f, CVar.SERVERONLY);

    /// <summary>Seconds of not breathing at all that drain brain oxygenation from full to nothing.</summary>
    public static readonly CVarDef<float> BrainAirlossSeconds =
        CVarDef.Create("wolfmed.brain_airloss_seconds", 180f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds at <see cref="BrainBloodFull"/> blood that drain oxygenation from full to nothing. 600 since playtest 5
    /// (was 300): a patient bled to the arrest line with the bleeding stopped has about 9 minutes for a transfusion
    /// before the oxygen trigger, was 4.5.
    /// </summary>
    public static readonly CVarDef<float> BrainBloodSeconds =
        CVarDef.Create("wolfmed.brain_blood_seconds", 600f, CVar.SERVERONLY);

    /// <summary>Blood volume fraction under which perfusion starts costing the brain oxygen.</summary>
    public static readonly CVarDef<float> BrainBloodStart =
        CVarDef.Create("wolfmed.brain_blood_start", 0.5f, CVar.SERVERONLY);

    /// <summary>Blood volume fraction at which the blood drain is at its full rate.</summary>
    public static readonly CVarDef<float> BrainBloodFull =
        CVarDef.Create("wolfmed.brain_blood_full", 0.3f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds of late sepsis that drain brain oxygenation from full to nothing. 0 since playtest 5: septic shock
    /// puts the patient out instead (WolfmedInfectionSystem.ShockPressure) and sepsis kills only through the organs
    /// (wolfmed.sepsis_organ_damage_from), so a patient found in shock is down, not dying. A server that wants the
    /// old clock sets it back to 600.
    /// </summary>
    public static readonly CVarDef<float> BrainSepsisSeconds =
        CVarDef.Create("wolfmed.brain_sepsis_seconds", 0f, CVar.SERVERONLY);

    /// <summary>
    /// Refill rate while perfused and breathing, as a share of the arrest drain rate. 0.75 since playtest 5 (was 0.5)
    /// so the slower arrest drain leaves the refill after a shock at the rate it was: full from empty in four minutes.
    /// </summary>
    public static readonly CVarDef<float> BrainRefillFactor =
        CVarDef.Create("wolfmed.brain_refill_factor", 0.75f, CVar.SERVERONLY);

    /// <summary>
    /// Multiplier on the cold protection a brain gets from a low body temperature. The curve itself is data
    /// (WolfmedBrainComponent.ColdSteps); 1 uses it as authored and higher values weaken it.
    /// </summary>
    public static readonly CVarDef<float> BrainColdFactor =
        CVarDef.Create("wolfmed.brain_cold_factor", 1f, CVar.SERVERONLY);

    /// <summary>Drain multiplier while somebody is doing CPR, above <see cref="BrainCprFloor"/>.</summary>
    public static readonly CVarDef<float> BrainCprFactor =
        CVarDef.Create("wolfmed.brain_cpr_factor", 0.25f, CVar.SERVERONLY);

    /// <summary>
    /// Playtest 5: brain oxygenation chest compressions hold a living arrested body at. Above it the drain (times
    /// wolfmed.brain_cpr_factor) stops there; under it CPR refills at the corpse rate up to it. 0.4, the damage line,
    /// so a rescuer who keeps going keeps the brain alive for as long as they keep going. 0 turns the hold off and
    /// CPR only slows the drain, as before.
    /// </summary>
    public static readonly CVarDef<float> BrainCprFloor =
        CVarDef.Create("wolfmed.brain_cpr_floor", 0.4f, CVar.SERVERONLY);

    /// <summary>Drain multiplier while an epinephrine-class stimulant is metabolising.</summary>
    public static readonly CVarDef<float> BrainStimulantFactor =
        CVarDef.Create("wolfmed.brain_stimulant_factor", 0.6f, CVar.SERVERONLY);

    /// <summary>Oxygenation under which the brain organ starts taking irreversible damage.</summary>
    public static readonly CVarDef<float> BrainDamageOxygenation =
        CVarDef.Create("wolfmed.brain_damage_oxygenation", 0.4f, CVar.SERVERONLY);

    /// <summary>
    /// Brain organ health lost per second at zero oxygenation, falling linearly to the threshold. 0.05 since
    /// playtest 5 (was 0.1): an empty brain (15 health) lasts five minutes, not two and a half.
    /// </summary>
    public static readonly CVarDef<float> BrainDamageRate =
        CVarDef.Create("wolfmed.brain_damage_rate", 0.05f, CVar.SERVERONLY);

    /// <summary>Oxygenation under which hypoxia starts pushing consciousness down.</summary>
    public static readonly CVarDef<float> BrainPressureStart =
        CVarDef.Create("wolfmed.brain_pressure_start", 0.75f, CVar.SERVERONLY);

    /// <summary>Oxygenation at which hypoxia alone is enough to put a body out.</summary>
    public static readonly CVarDef<float> BrainPressureOut =
        CVarDef.Create("wolfmed.brain_pressure_out", 0.45f, CVar.SERVERONLY);

    /// <summary>
    /// Minutes a repaired brain carries its trauma, with the concussion effects. 10 since playtest 5 (was 30): a
    /// player took half an hour of stutter and blur with every organ reading fine for a permanent bug, and the
    /// analyzer now names it. A revived destroyed brain is damaged rather than traumatised; the trauma is the price
    /// of the repair.
    /// </summary>
    public static readonly CVarDef<float> BrainTraumaMinutes =
        CVarDef.Create("wolfmed.brain_trauma_minutes", 10f, CVar.SERVERONLY);

    /// <summary>
    /// Blood volume fraction under which a defibrillator refuses to shock. Under the 0.30 blood arrest, so the
    /// common arrest is shockable; the post-shock grace covers the transfusion that has to follow.
    /// </summary>
    public static readonly CVarDef<float> DefibBlood =
        CVarDef.Create("wolfmed.defib_blood", 0.25f, CVar.SERVERONLY);

    /// <summary>Best chance a defibrillator has, at full brain oxygenation.</summary>
    public static readonly CVarDef<float> DefibChance =
        CVarDef.Create("wolfmed.defib_chance", 0.85f, CVar.SERVERONLY);

    /// <summary>
    /// Playtest 5, "brain death should be removed": the share of its health a destroyed brain comes back with when
    /// the paddles restart the heart. 0.1: under the brain's SevereAt (half), so the patient is back blurred,
    /// slurring, stuttering and dropping things until the brain is repaired. Never a refusal; a destroyed positronic
    /// core still needs core repair, since a chassis has no damaged-but-running state to come back in.
    /// </summary>
    public static readonly CVarDef<float> ReviveBrainFloor =
        CVarDef.Create("wolfmed.revive_brain_floor", 0.1f, CVar.SERVERONLY);

    /// <summary>What the defibrillator's chance is multiplied by at zero brain oxygenation.</summary>
    public static readonly CVarDef<float> DefibOxygenationFloor =
        CVarDef.Create("wolfmed.defib_oxygenation_floor", 0.15f, CVar.SERVERONLY);

    /// <summary>The autodoc's vital alarm: the monitor beep over a failing occupant. False silences it.</summary>
    public static readonly CVarDef<bool> AutodocAlarm =
        CVarDef.Create("wolfmed.autodoc_alarm", true, CVar.SERVERONLY);

    /// <summary>
    /// Times the autodoc repeats one step that changes nothing about the part before it gives the whole
    /// procedure up. The guard against a completion check that can never pass.
    /// </summary>
    public static readonly CVarDef<int> AutodocStepRetries =
        CVarDef.Create("wolfmed.autodoc_step_retries", 3, CVar.SERVERONLY);

    /// <summary>
    /// Wound severity one tend-wounds surgery pass closes, per damage type of the tended group. Tending is
    /// the surgical way to close cuts and bruises, so it reaches the wound at the strength a suture does
    /// instead of through the damage routing's healing multiplier.
    /// </summary>
    public static readonly CVarDef<float> SurgeryTendStrength =
        CVarDef.Create("wolfmed.surgery_tend_strength", 15f, CVar.SERVERONLY);

    /// <summary>Shocks the autodoc gives one patient before it says it cannot restart the heart.</summary>
    public static readonly CVarDef<int> AutodocDefibAttempts =
        CVarDef.Create("wolfmed.autodoc_defib_attempts", 5, CVar.SERVERONLY);

    /// <summary>Ceiling on a wound host's Asphyxiation and (playtest 5) Bloodloss damage: bookkeeping nothing reads past it.</summary>
    // Uncapped, suffocation counted past 700 on a body that cannot die of the number, since the brain's hypoxia clock
    // carries the lethality; this is the old death line. Bloodloss is not capped: the vital losses (decapitation) deal
    // a fixed lethal figure through it.
    public static readonly CVarDef<float> AirlossCap =
        CVarDef.Create("wolfmed.airloss_cap", 200f, CVar.SERVERONLY);

    /// <summary>
    /// Blood volume fraction under which the autodoc transfuses out of its reservoir. It pushes until the
    /// occupant is back above the pod's own target or the reservoir runs dry.
    /// </summary>
    public static readonly CVarDef<float> AutodocTransfuseBelow =
        CVarDef.Create("wolfmed.autodoc_transfuse_below", 0.8f, CVar.SERVERONLY);

    /// <summary>
    /// Sedation (0 to 1) the autodoc will not push a patient past. Respiratory depression starts at 0.6, so
    /// the pod stops well short of it and says so rather than anaesthetising somebody to death.
    /// </summary>
    public static readonly CVarDef<float> AutodocSedationCap =
        CVarDef.Create("wolfmed.autodoc_sedation_cap", 0.5f, CVar.SERVERONLY);

    /// <summary>
    /// Whether a mechanical body draws the synthetic diagnostics readout instead of the organic vignette
    /// and dying view. False puts a chassis back on the flesh presentation.
    /// </summary>
    public static readonly CVarDef<bool> SyntheticHud =
        CVarDef.Create("wolfmed.synthetic_hud", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Text size multiplier for the synthetic readout.</summary>
    public static readonly CVarDef<float> SyntheticHudScale =
        CVarDef.Create("wolfmed.synthetic_hud_scale", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Asphyxiation at which a suffocating body counts as not breathing at all. Read only while the respirator
    /// is actually suffocating; leftover damage on a breathing body is bookkeeping.
    /// </summary>
    public static readonly CVarDef<float> AirlossFull =
        CVarDef.Create("wolfmed.airloss_full", 100f, CVar.SERVERONLY);

    /// <summary>Brain oxygenation a successful shock or a brain repair leaves at the least.</summary>
    public static readonly CVarDef<float> PostShockOxygenation =
        CVarDef.Create("wolfmed.post_shock_oxygenation", 0.5f, CVar.SERVERONLY);

    /// <summary>Seconds after a successful shock during which the blood and oxygen arrest triggers hold off.</summary>
    public static readonly CVarDef<float> PostShockGraceSeconds =
        CVarDef.Create("wolfmed.post_shock_grace_seconds", 45f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds after a successful shock during which another one only restarts the heart: no oxygenation
    /// restore and no new grace. One restore per arrest episode; a patient who gets up with the blood back
    /// ends the episode early.
    /// </summary>
    public static readonly CVarDef<float> PostShockRepeatSeconds =
        CVarDef.Create("wolfmed.post_shock_repeat_seconds", 300f, CVar.SERVERONLY);

    /// <summary>
    /// Blood volume fraction the transfusion guidance aims for after a shock, with margin over the arrest line.
    /// The units named are the units to this plus the current bleed over the grace.
    /// </summary>
    public static readonly CVarDef<float> PostShockBloodTarget =
        CVarDef.Create("wolfmed.post_shock_blood_target", 0.35f, CVar.SERVERONLY);

    /// <summary>Blood volume fraction at or under which the circulation reads pale rather than normal.</summary>
    public static readonly CVarDef<float> BloodBandPale =
        CVarDef.Create("wolfmed.blood_band_pale", 0.8f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Seconds a pain faint lasts. A hard maximum: damage taken during the faint never extends it (M1a, plan
    /// §3.1).
    /// </summary>
    public static readonly CVarDef<float> PainFaintSeconds =
        CVarDef.Create("wolfmed.pain_faint_seconds", 20f, CVar.SERVERONLY);

    /// <summary>
    /// Summed pain over the pain at waking that re-arms the faint. Pain added during a faint does not count,
    /// so a steady injury never faints twice; a fresh wound can.
    /// </summary>
    public static readonly CVarDef<float> PainFaintRise =
        CVarDef.Create("wolfmed.pain_faint_rise", 40f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds after waking from a pain faint during which no new one starts. 50 since M1b (plan §3.1's fallback):
    /// with overflow growing burns past the ceilings, a fire alone re-armed a faint every 50 s at 30, three in two
    /// minutes; 50 caps any two minutes at two faints (40 s).
    /// </summary>
    public static readonly CVarDef<float> PainFaintCooldown =
        CVarDef.Create("wolfmed.pain_faint_cooldown", 50f, CVar.SERVERONLY);

    /// <summary>Body pain at which an armed pain shock fires: the fall, the scream and the 2 s stun.</summary>
    public static readonly CVarDef<float> PainShockThreshold =
        CVarDef.Create("wolfmed.pain_shock_threshold", 130f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Body pain the shock has to fall back under before it can fire again.</summary>
    public static readonly CVarDef<float> PainShockRearm =
        CVarDef.Create("wolfmed.pain_shock_rearm", 110f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Most pain one arm can hold (playtest 4). Onyx caps every part at the body's 135, so bullets into one arm
    /// reached the Downed line on their own. Zero keeps the part's own cap.
    /// </summary>
    public static readonly CVarDef<float> PartPainCapArm =
        CVarDef.Create("wolfmed.part_pain_cap_arm", 80f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Most pain one hand can hold (playtest 4).</summary>
    public static readonly CVarDef<float> PartPainCapHand =
        CVarDef.Create("wolfmed.part_pain_cap_hand", 50f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Most pain one leg can hold (playtest 4).</summary>
    public static readonly CVarDef<float> PartPainCapLeg =
        CVarDef.Create("wolfmed.part_pain_cap_leg", 90f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Most pain one foot can hold (playtest 4).</summary>
    public static readonly CVarDef<float> PartPainCapFoot =
        CVarDef.Create("wolfmed.part_pain_cap_foot", 50f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Seconds of adrenaline a pain shock gives. It no longer stands anyone up (OD5): it speeds the crawl and
    /// lifts the Downed do-after penalty.
    /// </summary>
    public static readonly CVarDef<float> AdrenalineSeconds =
        CVarDef.Create("wolfmed.adrenaline_seconds", 30f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Crawl speed multiplier while adrenaline runs on a Downed body.</summary>
    public static readonly CVarDef<float> AdrenalineCrawlMultiplier =
        CVarDef.Create("wolfmed.adrenaline_crawl_multiplier", 1.5f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Metres a Downed body can reach for an item on the floor: its own tile and the ones next to it (M1a,
    /// plan §5.3, OD7 (b)). Sidearms fire from the floor (playtest 4); longer guns do not.
    /// </summary>
    public static readonly CVarDef<float> DownedReach =
        CVarDef.Create("wolfmed.downed_reach", 1.5f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Seconds a Call for help keeps the caller flagged on medical HUDs.</summary>
    public static readonly CVarDef<float> CallForHelpSeconds =
        CVarDef.Create("wolfmed.call_for_help_seconds", 60f, CVar.SERVERONLY);

    /// <summary>Seconds after a Call for help before the next one.</summary>
    public static readonly CVarDef<float> CallForHelpCooldown =
        CVarDef.Create("wolfmed.call_for_help_cooldown", 30f, CVar.SERVERONLY);

    /// <summary>
    /// Units a second of net blood (or oil) loss at which the analyzer's circulation line reads "falling fast"
    /// rather than "falling" (M1a, plan §5.5).
    /// </summary>
    public static readonly CVarDef<float> AnalyzerBloodFast =
        CVarDef.Create("wolfmed.analyzer_blood_fast", 1f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds a swallowed painkiller sits in the stomach before it reaches the blood, instead of the stomach's
    /// 20 s (playtest 1). Any reagent with a Wolfmed pain relief effect counts. Never longer than the stomach's
    /// own delay.
    /// </summary>
    public static readonly CVarDef<float> PainkillerAbsorbSeconds =
        CVarDef.Create("wolfmed.painkiller_absorb_seconds", 4f, CVar.SERVERONLY);

    /// <summary>
    /// Pain a second a part sheds from the share of its pain no open wound backs (playtest 4). Onyx recovers pain at
    /// one ninth a second whatever its source, so a treated wound kept hurting for minutes; the wound floor still
    /// holds while the wound is open. Zero keeps Onyx's rate.
    /// </summary>
    public static readonly CVarDef<float> PainLooseRecovery =
        CVarDef.Create("wolfmed.pain_loose_recovery", 3f, CVar.SERVERONLY);

    /// <summary>
    /// Playtest 5: multiplier on every pain gain and every wound's pain floor. 0.85, "same with pain amounts, adjust it
    /// slightly at first". 1 is Onyx's figures.
    /// </summary>
    public static readonly CVarDef<float> PainScale =
        CVarDef.Create("wolfmed.pain_scale", 0.85f, CVar.SERVERONLY);

    /// <summary>
    /// Playtest 5: "pain should rapidly fall if the wound isn't getting worse". The share of a part's wound floor that
    /// still holds once the wound has not worsened for wolfmed.pain_floor_settle_seconds; the loose recovery takes the
    /// pain down to it. A wound getting worse puts the whole floor back. 1 keeps the floor whole.
    /// </summary>
    public static readonly CVarDef<float> PainFloorRest =
        CVarDef.Create("wolfmed.pain_floor_rest", 0.6f, CVar.SERVERONLY);

    /// <summary>Seconds after a wound last got worse for its floor to settle to wolfmed.pain_floor_rest.</summary>
    public static readonly CVarDef<float> PainFloorSettleSeconds =
        CVarDef.Create("wolfmed.pain_floor_settle_seconds", 20f, CVar.SERVERONLY);

    /// <summary>
    /// Blunt plus Slash a single melee hit on flesh must deal for the meaty chop (Bob's chop2/4/5) to play over the
    /// weapon's own sound (playtest 4). 40: a wielded fire axe (45) chops, a machete (32) does not. Zero turns it off.
    /// </summary>
    public static readonly CVarDef<float> ChopSoundDamage =
        CVarDef.Create("wolfmed.chop_sound_damage", 40f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Whether extreme trauma leaves gib decals (playtest 4, GORE). Off skips them entirely.</summary>
    public static readonly CVarDef<bool> GibDecals =
        CVarDef.Create("wolfmed.gib_decals", true, CVar.SERVERONLY);

    /// <summary>How far, in tiles, a gib can land from the body.</summary>
    public static readonly CVarDef<float> GibSpread =
        CVarDef.Create("wolfmed.gib_spread", 1.5f, CVar.SERVERONLY);

    /// <summary>Gibs a limb coming off leaves.</summary>
    public static readonly CVarDef<int> GibsDismemberment =
        CVarDef.Create("wolfmed.gibs_dismemberment", 2, CVar.SERVERONLY);

    /// <summary>Gibs an opened belly leaves.</summary>
    public static readonly CVarDef<int> GibsEvisceration =
        CVarDef.Create("wolfmed.gibs_evisceration", 3, CVar.SERVERONLY);

    /// <summary>Gibs a whole body coming apart leaves.</summary>
    public static readonly CVarDef<int> GibsGib =
        CVarDef.Create("wolfmed.gibs_gib", 7, CVar.SERVERONLY);

    /// <summary>
    /// Seconds between two "You feel your wounds painfully close!" lines on one body (playtest 1). Vacuum deals a
    /// little Heat every second, and each tick on a bleeding body used to say it again.
    /// </summary>
    public static readonly CVarDef<float> CauteryPopupSeconds =
        CVarDef.Create("wolfmed.cautery_popup_seconds", 10f, CVar.SERVERONLY);

    // M1b: burns and caps (plan §3.7, §6, OD11, OD12).

    /// <summary>
    /// Per-part ceiling for damage nobody dealt, as a fraction of the part's lowest destruction threshold (arm
    /// and leg 168, hand and foot 144 since M3 raised the limbs' Blunt and Heat rungs, head 400). A part without one (the torso) keeps its own cap. What the
    /// ceiling trims still grows wounds and fluid loss; it is only not stored. Zero turns it off.
    /// </summary>
    public static readonly CVarDef<float> AmbientPartCapFraction =
        CVarDef.Create("wolfmed.ambient_part_cap_fraction", 0.8f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Multiplier on every burn wound's fluid loss into blood volume (OD11). Zero turns the route off.</summary>
    public static readonly CVarDef<float> BurnFluidRate =
        CVarDef.Create("wolfmed.burn_fluid_rate", 1f, CVar.SERVERONLY);

    /// <summary>Fraction of its fluid loss a dressed burn keeps. Only a graft stops it.</summary>
    public static readonly CVarDef<float> BurnDressedFluidFactor =
        CVarDef.Create("wolfmed.burn_dressed_fluid_factor", 0.25f, CVar.SERVERONLY);

    /// <summary>
    /// Severity a dressed or grafted burn has to grow by, from where it was treated, before the treatment is
    /// lost and it weeps in full again. The burn's own reopen line.
    /// </summary>
    public static readonly CVarDef<float> BurnTreatmentLostSeverity =
        CVarDef.Create("wolfmed.burn_treatment_lost_severity", 15f, CVar.SERVERONLY);

    /// <summary>
    /// Charring severity per point of Heat a part takes while its burn is already at the burn's maximum (a wound
    /// at cap escalates, plan §6.3).
    /// </summary>
    public static readonly CVarDef<float> CharEscalation =
        CVarDef.Create("wolfmed.char_escalation", 1f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds a hand or foot's charring has to sit at its maximum, while the part keeps taking Heat, before the
    /// part crumbles to ash (OD12). The head and torso never crumble.
    /// </summary>
    public static readonly CVarDef<float> CharCrumbleSeconds =
        CVarDef.Create("wolfmed.char_crumble_seconds", 180f, CVar.SERVERONLY);

    /// <summary>Multiplier on wolfmed.char_crumble_seconds for arms and legs.</summary>
    public static readonly CVarDef<float> CharCrumbleLimbMultiplier =
        CVarDef.Create("wolfmed.char_crumble_limb_multiplier", 2f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds without Heat after which a charred part's crumble clock starts over: "keeps taking Heat" allows a
    /// fire's one-second ticks landing on other parts in between.
    /// </summary>
    public static readonly CVarDef<float> CharCrumbleGapSeconds =
        CVarDef.Create("wolfmed.char_crumble_gap_seconds", 10f, CVar.SERVERONLY);

    /// <summary>Units a second of burn fluid loss at which the analyzer reads it as "fast" rather than "slow".</summary>
    public static readonly CVarDef<float> AnalyzerBurnFast =
        CVarDef.Create("wolfmed.analyzer_burn_fast", 0.5f, CVar.SERVERONLY);

    /// <summary>
    /// Playtest 2 (plan §2.2): the slowest a Downed wound host crawls, as a fraction of the normal Downed crawl
    /// (a healthy body's base speed times the lying-down modifier), whatever its leg penalties say. With no working
    /// leg it drags itself on its arms at exactly this; only no working arm and no working leg stops it.
    /// </summary>
    public static readonly CVarDef<float> CrawlFloor =
        CVarDef.Create("wolfmed.crawl_floor", 0.35f, CVar.SERVER | CVar.REPLICATED);

    // M2: arrest, revival and medic information (plan §3.4, §5.2-5.5, §7.2, OD7 (c), OD8, OD10, OD14, OD17).

    /// <summary>
    /// Sedation gained per second while it is under its target: the units of sedating painkiller in the blood times
    /// each reagent's sedationPerUnit (plan §3.4). A steady dose levels off at its target.
    /// </summary>
    public static readonly CVarDef<float> SedationRise =
        CVarDef.Create("wolfmed.sedation_rise", 0.05f, CVar.SERVERONLY);

    /// <summary>Sedation at which the patient is warned it is getting drowsy, and at which examine reads "responds to voice".</summary>
    public static readonly CVarDef<float> SedationWarn =
        CVarDef.Create("wolfmed.sedation_warn", 0.4f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Sedation at which the patient is warned it can barely stay awake. Must stay under the 0.88 Downed point.</summary>
    public static readonly CVarDef<float> SedationWarnHeavy =
        CVarDef.Create("wolfmed.sedation_warn_heavy", 0.8f, CVar.SERVERONLY);

    /// <summary>Brain oxygenation under which examine reads "blue lips": the hypoxia Downed line.</summary>
    public static readonly CVarDef<float> ExamineCyanosisOxygenation =
        CVarDef.Create("wolfmed.examine_cyanosis_oxygenation", 0.54f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Seconds after a heart restarts during which the analyzer keeps "Arrest cause: … Still present: …" (plan §5.5).
    /// </summary>
    public static readonly CVarDef<float> ArrestCauseMemorySeconds =
        CVarDef.Create("wolfmed.arrest_cause_memory_seconds", 300f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds of continuous, stable, non-dying helplessness (Unconscious or shutdown with nothing draining) before the
    /// body is flagged in distress on medical HUDs and the player is offered "wait as a ghost" (OD8 (b)).
    /// </summary>
    public static readonly CVarDef<float> DormantOfferSeconds =
        CVarDef.Create("wolfmed.dormant_offer_seconds", 90f, CVar.SERVERONLY);

    /// <summary>Seconds the explanation card says "A medic is examining you" after an analyzer scan.</summary>
    public static readonly CVarDef<float> CardExaminedSeconds =
        CVarDef.Create("wolfmed.card_examined_seconds", 3f, CVar.SERVERONLY);

    /// <summary>
    /// Severity a sutured wound has to grow by, from where it was sutured, before the suture no longer counts as a
    /// treated wound for infection (P20). The suture's counterpart of wolfmed.burn_treatment_lost_severity.
    /// </summary>
    public static readonly CVarDef<float> SutureTreatmentLostSeverity =
        CVarDef.Create("wolfmed.suture_treatment_lost_severity", 15f, CVar.SERVERONLY);

    // M3: consequences keep mattering (plan §8, §3.6, OD15).

    /// <summary>
    /// Organ damage per point of a hit past its part's reach line, before the organ's own per-type multiplier
    /// and weight share (plan §8). With torso organs at 25 (playtest 4) an unarmoured rifle round (Piercing 14) to the
    /// chest impairs the lungs on hit 10 and fails the heart on hit 27; destroyed organs leave the split, so the
    /// survivors take more. Heavy hits are what wolfmed.organ_hit_cap bounds.
    /// </summary>
    public static readonly CVarDef<float> OrganDamageScale =
        CVarDef.Create("wolfmed.organ_damage_scale", 3.4f, CVar.SERVERONLY);

    /// <summary>
    /// Most health one organ loses to one hit through the reach lines. 3 since playtest 4 (was 5): with organs at 25,
    /// a heavy hit that reaches an organ needs nine of its kind to fail it, whatever else shares the torso.
    /// </summary>
    public static readonly CVarDef<float> OrganHitCap =
        CVarDef.Create("wolfmed.organ_hit_cap", 3f, CVar.SERVERONLY);

    /// <summary>
    /// Damaged lungs as a breathing input (plan §3.3): (impaired line - lung health fraction) / impaired line,
    /// times this, while the lungs are under their impaired line. Zero turns the route off.
    /// </summary>
    public static readonly CVarDef<float> LungDamageFactor =
        CVarDef.Create("wolfmed.lung_damage_factor", 1f, CVar.SERVERONLY);

    /// <summary>Brain health fraction under which the brain holds the patient Downed, cause Brain (plan §3.6).</summary>
    public static readonly CVarDef<float> ConsciousnessBrainDown =
        CVarDef.Create("wolfmed.consc_brain_down", 0.25f, CVar.SERVERONLY);

    /// <summary>Positronic core health fraction under which the chassis is Downed, cause Core (plan §3.6).</summary>
    public static readonly CVarDef<float> ConsciousnessCoreDown =
        CVarDef.Create("wolfmed.consc_core_down", 0.25f, CVar.SERVERONLY);

    /// <summary>
    /// The injury pressure brain or core damage writes. Between the Downed share (0.7) and 1, so it Downs and
    /// never knocks out: neither heals on its own, so an Unconscious rung would be helpless time only surgery ends.
    /// </summary>
    public static readonly CVarDef<float> InjuryDownPressure =
        CVarDef.Create("wolfmed.injury_down_pressure", 0.75f, CVar.SERVERONLY);

    /// <summary>Seconds a heavy blow to the head knocks the patient out (plan §3.6). Nothing extends it.</summary>
    public static readonly CVarDef<float> HeadKnockoutSeconds =
        CVarDef.Create("wolfmed.head_knockout_seconds", 5f, CVar.SERVERONLY);

    /// <summary>Blunt in one hit to the head, after armour, that knocks the patient out.</summary>
    public static readonly CVarDef<float> HeadKnockoutBlunt =
        CVarDef.Create("wolfmed.head_knockout_blunt", 30f, CVar.SERVERONLY);

    /// <summary>Heart health per point of Shock over wolfmed.electric_heart_from in one hit (OD15, replaces the rolls).</summary>
    public static readonly CVarDef<float> ElectricHeartFactor =
        CVarDef.Create("wolfmed.electric_heart_factor", 0.2f, CVar.SERVERONLY);

    /// <summary>Shock in one hit, after armour, from which the current reaches the heart.</summary>
    public static readonly CVarDef<float> ElectricHeartFrom =
        CVarDef.Create("wolfmed.electric_heart_from", 15f, CVar.SERVERONLY);

    // M4: species and IPC death (plan §3.11, §9).

    /// <summary>
    /// Core temperature, in kelvin, at which a machine's positronic core starts losing health and the chassis goes
    /// into thermal shutdown (Dying, cause CoreHeat).
    /// </summary>
    public static readonly CVarDef<float> IpcCoreHeatK =
        CVarDef.Create("wolfmed.ipc_core_heat_k", 500f, CVar.SERVERONLY);

    /// <summary>Core temperature under which thermal shutdown ends and the chassis comes back online.</summary>
    public static readonly CVarDef<float> IpcCoreHeatWakeK =
        CVarDef.Create("wolfmed.ipc_core_heat_wake_k", 450f, CVar.SERVERONLY);

    /// <summary>
    /// Core health lost a second for every 100 K the core is over wolfmed.ipc_core_heat_k. Set against the M1a/M4 IPC
    /// fire measurement: an untreated 10-stack fire destroys the core, one put out at 60 s leaves it standing.
    /// Playtest 3: the core grew from 15 to 40 health, so the rate grew by the same 8/3 to keep that timeline.
    /// </summary>
    public static readonly CVarDef<float> IpcCoreHeatRate =
        CVarDef.Create("wolfmed.ipc_core_heat_rate", 0.5333f, CVar.SERVERONLY);

    /// <summary>
    /// Share of the gap to the chassis temperature the core closes each second. The core soaks the chassis's heat
    /// up rather than following it, so a fire put out early never cooks it.
    /// </summary>
    public static readonly CVarDef<float> IpcCoreHeatSoak =
        CVarDef.Create("wolfmed.ipc_core_heat_soak", 0.02f, CVar.SERVERONLY);

    /// <summary>
    /// Kelvin a second a working, powered coolant pump takes off a core above body temperature. An impaired pump
    /// cools at its organ's impairedCoolingFactor of this.
    /// </summary>
    public static readonly CVarDef<float> IpcPumpCooling =
        CVarDef.Create("wolfmed.ipc_pump_cooling", 5f, CVar.SERVERONLY);

    /// <summary>
    /// Core or chassis temperature from which the readout warns CORE TEMP CRITICAL and the analyzer shows the
    /// temperatures, before the core itself is at risk.
    /// </summary>
    public static readonly CVarDef<float> IpcCoreHeatWarnK =
        CVarDef.Create("wolfmed.ipc_core_heat_warn_k", 400f, CVar.SERVERONLY);

    // M5: toxins, radiation, cold and heat (plan §3.8-3.10, OD13).

    /// <summary>Toxin load (systemic Poison) at which a body goes Downed, cause Toxin.</summary>
    public static readonly CVarDef<float> ConsciousnessToxinDown =
        CVarDef.Create("wolfmed.consc_toxin_down", 60f, CVar.SERVERONLY);

    /// <summary>Toxin load at which a body falls into a toxic coma (Unconscious, breathing) and the brain drains.</summary>
    public static readonly CVarDef<float> ConsciousnessToxinOut =
        CVarDef.Create("wolfmed.consc_toxin_out", 120f, CVar.SERVERONLY);

    /// <summary>Seconds of a toxic coma that drain brain oxygenation from full to nothing (the sepsis shape).</summary>
    public static readonly CVarDef<float> BrainToxinSeconds =
        CVarDef.Create("wolfmed.brain_toxin_seconds", 600f, CVar.SERVERONLY);

    /// <summary>
    /// Poison a working liver clears a second (OD13): the one passive healing a wound host gets. An impaired liver
    /// clears at its organ's impairedClearanceFactor, a failed or missing one not at all.
    /// </summary>
    public static readonly CVarDef<float> ToxinClearance =
        CVarDef.Create("wolfmed.toxin_clearance", 0.1f, CVar.SERVERONLY);

    /// <summary>
    /// Playtest 5: systemic Poison per unit of brute, burn, cold, shock, caustic, asphyxiation or bloodloss a metabolising
    /// reagent would deal to a wound host, which is toxin load on such a body instead of wounds or bookkeeping. 0 makes
    /// that damage free.
    /// </summary>
    public static readonly CVarDef<float> ReagentToxinFactor =
        CVarDef.Create("wolfmed.reagent_toxin_factor", 0.5f, CVar.SERVERONLY);

    /// <summary>
    /// What a hit on a wound host in Avali stasis keeps before it becomes a wound. The stock stasis healed back half
    /// of the flat total after the fact, which a wound host does not read.
    /// </summary>
    public static readonly CVarDef<float> StasisDamageFactor =
        CVarDef.Create("wolfmed.stasis_damage_factor", 0.5f, CVar.SERVERONLY);

    /// <summary>
    /// Share of a wound host's blood each point of spawn Bloodloss takes (a corpse's preset damage, a medical
    /// bounty's roll): 200 leaves a fifth. Wolfmed reads the blood, not the Bloodloss number.
    /// </summary>
    public static readonly CVarDef<float> SpawnBloodlossBlood =
        CVarDef.Create("wolfmed.spawn_bloodloss_blood", 0.004f, CVar.SERVERONLY);

    /// <summary>Radiation at or past which the marrow stops: no blood regenerates (plan §3.9).</summary>
    public static readonly CVarDef<float> RadiationMarrowStop =
        CVarDef.Create("wolfmed.rad_marrow_stop", 40f, CVar.SERVERONLY);

    /// <summary>Radiation at or past which the body also loses blood, wolfmed.rad_marrow_rate a second.</summary>
    public static readonly CVarDef<float> RadiationMarrowBleed =
        CVarDef.Create("wolfmed.rad_marrow_bleed", 100f, CVar.SERVERONLY);

    /// <summary>Units of blood a second lost past wolfmed.rad_marrow_bleed. Discarded, never spilled.</summary>
    public static readonly CVarDef<float> RadiationMarrowRate =
        CVarDef.Create("wolfmed.rad_marrow_rate", 0.1f, CVar.SERVERONLY);

    /// <summary>Radiation at which a body goes Downed, cause Radiation ("radiation sickness"). Never unconscious.</summary>
    public static readonly CVarDef<float> ConsciousnessRadiationDown =
        CVarDef.Create("wolfmed.consc_rad_down", 80f, CVar.SERVERONLY);

    /// <summary>Kelvin over the species' cold damage threshold under which the core is hypothermic: Downed.</summary>
    public static readonly CVarDef<float> HypothermiaDownOffset =
        CVarDef.Create("wolfmed.hypothermia_down_offset", 30f, CVar.SERVERONLY);

    /// <summary>Kelvin over the cold damage threshold under which hypothermia knocks the patient out (breathing).</summary>
    public static readonly CVarDef<float> HypothermiaOutOffset =
        CVarDef.Create("wolfmed.hypothermia_out_offset", 15f, CVar.SERVERONLY);

    /// <summary>Kelvin over the cold damage threshold under which the heart stops, cause "cold".</summary>
    public static readonly CVarDef<float> HypothermiaArrestOffset =
        CVarDef.Create("wolfmed.hypothermia_arrest_offset", 2f, CVar.SERVERONLY);

    /// <summary>
    /// Kelvin under the species' heat damage threshold over which the core is in heat exhaustion: Downed. Past the
    /// threshold itself is heat stroke.
    /// </summary>
    public static readonly CVarDef<float> HyperthermiaDownOffset =
        CVarDef.Create("wolfmed.hyperthermia_down_offset", 7f, CVar.SERVERONLY);

    /// <summary>Seconds of heat stroke that drain brain oxygenation from full to nothing.</summary>
    public static readonly CVarDef<float> HyperthermiaBrainSeconds =
        CVarDef.Create("wolfmed.hyperthermia_brain_seconds", 300f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds after a fire goes out during which the body still cannot enter a heat cause (plan §3.10). It holds
    /// while burning too, never clears a heat cause the body already has, and comes once per cooling cycle. 60, not the
    /// plan's 30: a human put out at a 10-stack fire's hottest (832 K) is under the heat exhaustion line 55 s later.
    /// </summary>
    public static readonly CVarDef<float> HeatFireGraceSeconds =
        CVarDef.Create("wolfmed.heat_fire_grace_seconds", 60f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds for the core to close about two thirds of the gap to a colder body surface. The surface is what the
    /// atmosphere moves, and in space it reaches about 16 K within a minute; the core lags it, so cold takes minutes.
    /// Warming back up toward normal follows the surface at once.
    /// </summary>
    public static readonly CVarDef<float> CoreCoolingSeconds =
        CVarDef.Create("wolfmed.core_cooling_seconds", 900f, CVar.SERVERONLY);

    /// <summary>
    /// Playtest 3: seconds for the core to close about two thirds of the gap to a hotter body surface, so heat stroke
    /// is a timed limit in hot air rather than the moment the air is hot. A protogen (heat lines 318 and 325 K) in a
    /// 330 K room is Downed after about two minutes and in heat stroke after five, instead of within seconds.
    /// </summary>
    public static readonly CVarDef<float> CoreHeatingSeconds =
        CVarDef.Create("wolfmed.core_heating_seconds", 240f, CVar.SERVERONLY);

    /// <summary>Seconds for an overheated core to close about two thirds of the way back toward normal once the air is cooler.</summary>
    public static readonly CVarDef<float> CoreRecoverySeconds =
        CVarDef.Create("wolfmed.core_recovery_seconds", 60f, CVar.SERVERONLY);

    /// <summary>
    /// The furthest a hypothermia or heat exhaustion line may sit from the damage threshold, as a share of the gap
    /// between that threshold and the species' normal temperature. A species whose normal temperature is close to a
    /// threshold gets its offsets scaled down, so it is never Downed at its own normal temperature.
    /// </summary>
    public static readonly CVarDef<float> TemperatureLineMaxShare =
        CVarDef.Create("wolfmed.temperature_line_max_share", 0.6f, CVar.SERVERONLY);

    /// <summary>
    /// Share of the way from normal to a temperature line under which the core counts as nothing at all: no notch on
    /// the health doll for a body a few kelvin off normal in ordinary air.
    /// </summary>
    public static readonly CVarDef<float> TemperatureInputFloor =
        CVarDef.Create("wolfmed.temperature_input_floor", 0.5f, CVar.SERVERONLY);

    // M6: leftovers (plan §12 M6, OD18).

    /// <summary>
    /// One part hit this large or larger, after armour, cancels the do-afters the hit body is performing: treatment,
    /// surgery and anything that breaks on damage (OD18, P17). Ticks that pass interruptsDoAfters false (fire,
    /// bleeding, temperature) and systemic damage never count. 0 turns it off.
    /// </summary>
    public static readonly CVarDef<float> DoAfterInterruptDamage =
        CVarDef.Create("wolfmed.doafter_interrupt_damage", 10f, CVar.SERVERONLY);

    // Playtest 4, SEPSIS: sepsis damages the torso organs, and a sick body shows what is wrong with it.

    /// <summary>
    /// Sepsis progress from which it damages every torso organ. 90, over wolfmed.arrest_sepsis (80), so the brain
    /// drain still kills first when it runs; this is the route for a body whose brain is kept topped up (a pod,
    /// oxygen) while nobody treats the infection.
    /// </summary>
    public static readonly CVarDef<float> SepsisOrganDamageFrom =
        CVarDef.Create("wolfmed.sepsis_organ_damage_from", 90f, CVar.SERVERONLY);

    /// <summary>
    /// Organ health sepsis past wolfmed.sepsis_organ_damage_from takes a minute from each torso organ, times that
    /// organ's weight in the infection profile's sepsisOrganWeights. 1.5: with the torso organs at 25, the kidneys and
    /// liver (1.5) fail in about 11 minutes, the lungs and stomach in 17 and the heart (0.75) in 22. 0 turns it off.
    /// </summary>
    public static readonly CVarDef<float> SepsisOrganDamagePerMinute =
        CVarDef.Create("wolfmed.sepsis_organ_damage_per_minute", 1.5f, CVar.SERVERONLY);

    /// <summary>
    /// INFECTION: sepsis progress from which the patient is in septic shock. Playtest 5: it holds the patient out
    /// (WolfmedInfectionSystem.ShockPressure, cause SepticShock) and names the stage on the alert, the analyzer and
    /// the examine text; the organ damage (wolfmed.sepsis_organ_damage_from) is what kills. 80, the old drain's line.
    /// </summary>
    public static readonly CVarDef<float> SepticShockAt =
        CVarDef.Create("wolfmed.septic_shock_at", 80f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds between checks of every wound host for a condition emote (a cough, a retch). 8: often enough that a sick
    /// patient shows it within half a minute at the default chance, rarely enough not to fill the chat.
    /// </summary>
    public static readonly CVarDef<float> ConditionEmoteInterval =
        CVarDef.Create("wolfmed.condition_emote_interval", 8f, CVar.SERVERONLY);

    /// <summary>
    /// Chance each holding condition plays its emote on a check. 0.35: about one emote every 20 seconds for a
    /// patient with one condition. 0 turns the condition emotes off.
    /// </summary>
    public static readonly CVarDef<float> ConditionEmoteChance =
        CVarDef.Create("wolfmed.condition_emote_chance", 0.35f, CVar.SERVERONLY);

    /// <summary>
    /// Fewest seconds between two real vomits from a retch (sepsis past wolfmed.condition_vomit_sepsis, or a toxic
    /// coma). 60: each one costs upstream's 40 hunger and thirst and leaves a puddle.
    /// </summary>
    public static readonly CVarDef<float> ConditionVomitInterval =
        CVarDef.Create("wolfmed.condition_vomit_interval", 60f, CVar.SERVERONLY);

    /// <summary>Sepsis progress from which the patient coughs. 40, where the flush shows on examine.</summary>
    public static readonly CVarDef<float> ConditionCoughSepsis =
        CVarDef.Create("wolfmed.condition_cough_sepsis", 40f, CVar.SERVERONLY);

    /// <summary>Sepsis progress from which the patient retches. 60, a stage before the brain drain (80).</summary>
    public static readonly CVarDef<float> ConditionRetchSepsis =
        CVarDef.Create("wolfmed.condition_retch_sepsis", 60f, CVar.SERVERONLY);

    /// <summary>Sepsis progress from which a retch brings something up. 90, with the organ damage.</summary>
    public static readonly CVarDef<float> ConditionVomitSepsis =
        CVarDef.Create("wolfmed.condition_vomit_sepsis", 90f, CVar.SERVERONLY);

    /// <summary>
    /// Brain oxygenation under which a patient whose largest drain is the lungs wheezes. 0.6, halfway down the hypoxia
    /// pressure's ramp (0.75 to 0.45) and before the lips go blue (0.54).
    /// </summary>
    public static readonly CVarDef<float> ConditionWheezeOxygenation =
        CVarDef.Create("wolfmed.condition_wheeze_oxygenation", 0.6f, CVar.SERVERONLY);
    // Playtest 4, VISUALS: the pain HUD, wound and rot overlays.

    /// <summary>
    /// Effective body pain at or under which the pain HUD is hidden. 5 of the 135 soft cap: the pain a stubbed toe or a
    /// fading bruise leaves is not worth an icon, and anything that has started to matter shows.
    /// </summary>
    public static readonly CVarDef<float> PainHudFrom =
        CVarDef.Create("wolfmed.pain_hud_from", 5f, CVar.SERVERONLY);

    /// <summary>
    /// Bleed rate on one part, in units a second, at which its wound overlay turns from a drip into a steady trickle.
    /// 2: a 20-severity slash bleeds 0.6 and drips, a 60-severity one 3.6 and an amputation stump 18, and both trickle.
    /// </summary>
    public static readonly CVarDef<float> WoundOverlayStreamRate =
        CVarDef.Create("wolfmed.wound_overlay_stream_rate", 2f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds between the sweeps that re-read every wound host's wound and rot overlays. New wounds and bleed changes
    /// show at once; the sweep catches what raises nothing (an infection reaching Septic, a wound closing, a bleed
    /// stopped by a drug), so this is how late those can show.
    /// </summary>
    public static readonly CVarDef<float> OverlayRefreshSeconds =
        CVarDef.Create("wolfmed.overlay_refresh_seconds", 1f, CVar.SERVERONLY);
    // Playtest 4, IV: the IV drip and the pod's blood reservoir.

    /// <summary>
    /// Units a second a new IV drip moves. tg's DEFAULT_IV_TRANSFER_RATE (Nova keeps it): a blood pack's 30 u in six
    /// seconds, a human's 300 u pool in a minute.
    /// </summary>
    public static readonly CVarDef<float> IvRateDefault =
        CVarDef.Create("wolfmed.iv_rate_default", 5f, CVar.SERVERONLY);

    /// <summary>Slowest flow an IV drip can be set to, in units a second. tg's MIN_IV_TRANSFER_RATE: stopped.</summary>
    public static readonly CVarDef<float> IvRateMin =
        CVarDef.Create("wolfmed.iv_rate_min", 0f, CVar.SERVERONLY);

    /// <summary>Fastest flow an IV drip can be set to, in units a second. Nova's MAX_IV_TRANSFER_RATE (tg's 5, tripled).</summary>
    public static readonly CVarDef<float> IvRateMax =
        CVarDef.Create("wolfmed.iv_rate_max", 15f, CVar.SERVERONLY);

    /// <summary>What an IV drip's flow is rounded to, in units a second. tg's IV_TRANSFER_RATE_STEP.</summary>
    public static readonly CVarDef<float> IvRateStep =
        CVarDef.Create("wolfmed.iv_rate_step", 0.01f, CVar.SERVERONLY);

    /// <summary>
    /// Units of blood one pack of a Bloodpack stack is worth on an IV drip or in the pod's blood reservoir. The same
    /// 30 u a pack restores applied by hand (its Healing ModifyBloodLevel), so a drip only saves the medic the clicks.
    /// </summary>
    public static readonly CVarDef<float> IvUnitsPerPack =
        CVarDef.Create("wolfmed.iv_units_per_pack", 30f, CVar.SERVERONLY);

    /// <summary>Seconds of do-after to put an IV drip's needle into a patient. tg's one second.</summary>
    public static readonly CVarDef<float> IvAttachSeconds =
        CVarDef.Create("wolfmed.iv_attach_seconds", 1f, CVar.SERVERONLY);

    /// <summary>
    /// Piercing a needle ripped out by walking away deals to an arm, through the wound routing. tg's 3 brute; it opens
    /// a puncture wound.
    /// </summary>
    public static readonly CVarDef<float> IvRipDamage =
        CVarDef.Create("wolfmed.iv_rip_damage", 3f, CVar.SERVERONLY);

    /// <summary>
    /// Severity the wound a ripped-out needle opens is raised to. tg adds a moderate pierce wound on top of its 3 brute;
    /// 3 Piercing alone opens a puncture at 3, and a puncture only bleeds from 9, so 10 is a small wound that does
    /// bleed. 0 leaves the wound as the damage made it.
    /// </summary>
    public static readonly CVarDef<float> IvRipWoundSeverity =
        CVarDef.Create("wolfmed.iv_rip_wound_severity", 10f, CVar.SERVERONLY);

    /// <summary>Blood fraction under which a drip taking blood beeps loudly now and then. tg's BLOOD_VOLUME_SAFE.</summary>
    public static readonly CVarDef<float> IvBeepBelow =
        CVarDef.Create("wolfmed.iv_beep_below", 0.85f, CVar.SERVERONLY);

    /// <summary>Chance a second of that beep. tg rolls 5% a two-second machine tick.</summary>
    public static readonly CVarDef<float> IvBeepChance =
        CVarDef.Create("wolfmed.iv_beep_chance", 0.025f, CVar.SERVERONLY);

    /// <summary>Blood fraction under which the pod starts transfusing its occupant out of its blood reservoir.</summary>
    public static readonly CVarDef<float> PodTransfuseBelow =
        CVarDef.Create("wolfmed.pod_transfuse_below", 0.85f, CVar.SERVERONLY);

    /// <summary>Units a second the pod's blood reservoir transfuses. An IV drip's default flow.</summary>
    public static readonly CVarDef<float> PodTransfuseRate =
        CVarDef.Create("wolfmed.pod_transfuse_rate", 5f, CVar.SERVERONLY);

    /// <summary>Blood fraction the pod's blood reservoir transfuses up to once started.</summary>
    public static readonly CVarDef<float> PodTransfuseTo =
        CVarDef.Create("wolfmed.pod_transfuse_to", 0.95f, CVar.SERVERONLY);

    /// <summary>
    /// Playtest 5: units of antibiotic the pod's course pushes at a time into an infected occupant. Spaceacillin
    /// metabolises 0.2 u a tick and every tick treats a full unit's worth, so 5 u is 25 ticks: 300 wound progress.
    /// </summary>
    public static readonly CVarDef<float> PodAntibioticDose =
        CVarDef.Create("wolfmed.pod_antibiotic_dose", 5f, CVar.SERVERONLY);

    /// <summary>Seconds between the course's doses, about what one dose takes to metabolise.</summary>
    public static readonly CVarDef<float> PodAntibioticInterval =
        CVarDef.Create("wolfmed.pod_antibiotic_interval", 30f, CVar.SERVERONLY);

    /// <summary>Units the course gives one occupant at most, so an infection the reservoir cannot beat does not drain it.</summary>
    public static readonly CVarDef<float> PodAntibioticCourse =
        CVarDef.Create("wolfmed.pod_antibiotic_course", 40f, CVar.SERVERONLY);
    // Playtest 4, SOUNDS: the Bobmed sound pack.

    /// <summary>
    /// Summed external bleed rate (every open wound's rate, the unit the inspection reads) under which a bleeding
    /// body drips. 0.5 is the inspection's line between "oozing blood" and "bleeding freely" (look.yml
    /// bleedFlowing): the analyzer has no rate words of its own, only "falling" and "falling fast" for the net blood
    /// flow. A body with a spurt source never drips whatever its rate.
    /// </summary>
    public static readonly CVarDef<float> DripSoundBelow =
        CVarDef.Create("wolfmed.drip_sound_below", 0.5f, CVar.SERVERONLY);

    /// <summary>Seconds between two drips from a lightly bleeding body. 4 is the owner's figure.</summary>
    public static readonly CVarDef<float> DripSoundInterval =
        CVarDef.Create("wolfmed.drip_sound_interval", 4f, CVar.SERVERONLY);

    /// <summary>
    /// Playtest 5, "an infection has to have an actual reason to start": an open wound only goes bad while it is
    /// exposed to the air outside a sealed suit, or something dirty went into it, or the wound is dirty by nature
    /// (a bite, an open abdomen, a lodged round or shrapnel, dead tissue). False is the old model, where every open
    /// wound went bad on its own.
    /// </summary>
    public static readonly CVarDef<bool> InfectionNeedsReason =
        CVarDef.Create("wolfmed.infection_needs_reason", true, CVar.SERVERONLY);

    /// <summary>
    /// Playtest 5, "minor injuries shouldn't cause infections": open air alone never infects a wound in its Minor
    /// stage (under 25 on every organic wound). A dirty wound or a dirty tool still does. False infects them too.
    /// </summary>
    public static readonly CVarDef<bool> InfectionSparesMinor =
        CVarDef.Create("wolfmed.infection_spares_minor", true, CVar.SERVERONLY);

    /// <summary>Volume of a drip in decibels. Low, because it repeats for as long as the bleed lasts.</summary>
    public static readonly CVarDef<float> DripSoundVolume =
        CVarDef.Create("wolfmed.drip_sound_volume", -6f, CVar.SERVERONLY);

    /// <summary>
    /// Executions and weapon suicides: a gun whose one shot adds up to this much on a body part (pellets summed, the
    /// gun's damage modifier applied, Structural and Radiation left out) is medium: a hole in the head and the brain
    /// thrown out. Under it is weak: a head wound and an open artery.
    /// </summary>
    public static readonly CVarDef<float> ExecutionMedium =
        CVarDef.Create("wolfmed.execution_medium", 30f, CVar.SERVERONLY);

    /// <summary>The same sum from which a gun is heavy and destroys the head. A load of several pellets always is.</summary>
    public static readonly CVarDef<float> ExecutionHeavy =
        CVarDef.Create("wolfmed.execution_heavy", 60f, CVar.SERVERONLY);

    /// <summary>One swing of a blade from which an execution cuts deep as well as opening the artery. The arterial rule's own line.</summary>
    public static readonly CVarDef<float> ExecutionBladeMedium =
        CVarDef.Create("wolfmed.execution_blade_medium", 22f, CVar.SERVERONLY);

    /// <summary>One swing of a blade from which an execution takes the head off.</summary>
    public static readonly CVarDef<float> ExecutionBladeHeavy =
        CVarDef.Create("wolfmed.execution_blade_heavy", 40f, CVar.SERVERONLY);

    /// <summary>One swing of a blunt weapon from which an execution caves the skull in. Under it the skull is cracked.</summary>
    public static readonly CVarDef<float> ExecutionBluntMedium =
        CVarDef.Create("wolfmed.execution_blunt_medium", 20f, CVar.SERVERONLY);

    /// <summary>One swing of a blunt weapon from which an execution crushes the head. Above a maintenance jack in both hands (45).</summary>
    public static readonly CVarDef<float> ExecutionBluntHeavy =
        CVarDef.Create("wolfmed.execution_blunt_heavy", 50f, CVar.SERVERONLY);
}
