# UI4: the analyzer's "what is this and what do I do" text.
#
# One short line per subject, derived from the wound prototype id or from a condition name:
#   wolfmed-treatment-short-<slug>       the tooltip, and the procedure window's summary
# The procedure itself is a wolfmedTreatmentProcedure prototype whose rows point at:
#   wolfmed-treatment-step-<slug>-<n>    one step, one row, with its own tool icon
#   wolfmed-treatment-avoid-<slug>-<n>   a "do not" line under the steps
# A -mechanical slug is used when the part is a chassis and the advice differs.
#
# Every claim here is taken from WolfmedWoundTreatmentMatrixTest (which item reaches which wound, and what
# clears the ones no item reaches) and from the Wound treatment guidebook page. A line must never start
# with a bracket, a dot or an asterisk: Fluent would end the pattern there.

# Analyzer chrome -------------------------------------------------------------

wolfmed-treatment-guidebook-button = Open guidebook
health-analyzer-wound-targeted-tag = targeted
health-analyzer-wound-target-part-hint = Aim at this part.
health-analyzer-wound-banner-sepsis = Sepsis
health-analyzer-wound-banner-septic-shock = Septic shock
health-analyzer-wound-banner-blood-low = Low blood
health-analyzer-wound-banner-brain-death = Brain death
health-analyzer-wound-banner-cardiac-arrest = Cardiac arrest
health-analyzer-wound-banner-shutdown = Shutdown
health-analyzer-wound-banner-core-failure = Core failure
wolfmed-treatment-resolved = Resolved. Nothing on this finding is outstanding.
wolfmed-treatment-avoid-heading = Do not
wolfmed-treatment-step-done = done
wolfmed-treatment-step-surgery = On the operating table
wolfmed-treatment-title = {$finding}, {$part}

# Categories ------------------------------------------------------------------

wolfmed-treatment-short-cat-ballistic = Rounds and fragments. Pull anything lodged out with a hemostat first, then gauze and sutures.
wolfmed-treatment-short-cat-blunt = Blunt trauma. A bruise pack treats the bruising; fractures, dislocations and internal bleeding are separate findings.
wolfmed-treatment-short-cat-burn = Burns from heat, cold, shock or acid. Ointment or regenerative mesh, and wash acid off first.
wolfmed-treatment-short-cat-cut = Open cuts. Gauze slows the bleeding, sutures close them.
wolfmed-treatment-short-cat-infection = Contaminated tissue. Antiseptic while it is local, spaceacillin once it has spread.
wolfmed-treatment-short-cat-internal = Injuries inside the body. No dressing reaches these; they come out on the operating table.
wolfmed-treatment-short-cat-mechanical = Chassis damage. A welding tool, a wrench or a cable coil, depending on the finding. No medicine reaches it.
wolfmed-treatment-short-cat-other = Findings that fit no other group. Click a row for what to do about it.
wolfmed-treatment-short-cat-puncture = Punctures. They bleed harder than cuts. Gauze, then sutures.

# Onyx per-damage-type defaults -----------------------------------------------

wolfmed-treatment-short-blunt-wound = Crushed tissue under unbroken skin. A bruise pack is the whole treatment.
wolfmed-treatment-step-blunt-wound-1 = Select the injured part on the doll.
wolfmed-treatment-step-blunt-wound-2 = Apply a bruise pack to the patient, and repeat until the wound stops being listed.
wolfmed-treatment-step-blunt-wound-3 = Scan again. A heavy blunt hit often leaves a fracture or an internal bleed behind, and neither shows as this wound.
wolfmed-treatment-avoid-blunt-wound-1 = Ointment, gauze and sutures do not reach blunt damage and will refuse the part.

wolfmed-treatment-short-slash-wound = An open cut. Gauze slows the bleeding, sutures close it.
wolfmed-treatment-step-slash-wound-1 = Select the injured part on the doll.
wolfmed-treatment-step-slash-wound-2 = Press gauze on the part until the bleeding stops.
wolfmed-treatment-step-slash-wound-3 = Suture the cut closed. Medicated sutures are quickest, plain ones do the job, and a makeshift one closes it dirty.
wolfmed-treatment-step-slash-wound-4 = Clean it with antiseptic if it was left open for long, or closed with a makeshift suture.
wolfmed-treatment-avoid-slash-wound-1 = A bruise pack does nothing for a cut. Removing damage barely closes a wound, so expect to finish with sutures.

wolfmed-treatment-short-piercing-wound = A puncture. It bleeds harder than a cut and hurts more.
wolfmed-treatment-step-piercing-wound-1 = Select the injured part on the doll.
wolfmed-treatment-step-piercing-wound-2 = Check for an embedded object first: nothing closes a wound with something still in it.
wolfmed-treatment-step-piercing-wound-3 = Press gauze on the part until the bleeding stops.
wolfmed-treatment-step-piercing-wound-4 = Suture it closed, medicated or plain. A makeshift suture closes it dirty, so clean it after.
wolfmed-treatment-avoid-piercing-wound-1 = A bruise pack does nothing for a puncture.

wolfmed-treatment-short-burn-wound = Burned tissue from heat, cold, shock or acid. Ointment or regenerative mesh.
wolfmed-treatment-step-burn-wound-1 = If acid caused it, wash the patient first, or the residue keeps burning.
wolfmed-treatment-step-burn-wound-2 = Select the burned part on the doll.
wolfmed-treatment-step-burn-wound-3 = Apply ointment, or regenerative mesh for a deep burn.
wolfmed-treatment-step-burn-wound-4 = Watch the top stage. A burn that reaches critical chars the tissue, and only a skin graft surgery clears that.
wolfmed-treatment-avoid-burn-wound-1 = A bruise pack and gauze do not reach burns.

wolfmed-treatment-short-electrical-wound = Shock damage through the part. Ointment or regenerative mesh.
wolfmed-treatment-step-electrical-wound-1 = Cut the power source before you touch the patient.
wolfmed-treatment-step-electrical-wound-2 = Select the part on the doll.
wolfmed-treatment-step-electrical-wound-3 = Apply ointment or regenerative mesh.
wolfmed-treatment-step-electrical-wound-4 = A strong shock also leaves internal burns. Check the heart on the organs tab.

wolfmed-treatment-short-electrical-wound-mechanical = Shock damage in the chassis wiring. A cable coil repairs it.
wolfmed-treatment-step-electrical-wound-mechanical-1 = Cut the power source first.
wolfmed-treatment-step-electrical-wound-mechanical-2 = Select the part on the doll.
wolfmed-treatment-step-electrical-wound-mechanical-3 = Use a cable coil on the chassis until the damage clears.
wolfmed-treatment-avoid-electrical-wound-mechanical-1 = Ointment, mesh and every other topical refuse a chassis outright.

wolfmed-treatment-short-bone-fracture-wound = A broken bone. It slows movement and hand work until it is set and mended.
wolfmed-treatment-step-bone-fracture-wound-1 = Select the broken limb on the doll.
wolfmed-treatment-step-bone-fracture-wound-2 = In the field, use a splint on an arm, hand, leg or foot. That sets the bone and cuts the penalty to a quarter.
wolfmed-treatment-step-bone-fracture-wound-3 = A hairline crack is too slight for a splint to hold.
wolfmed-treatment-step-bone-fracture-wound-4 = On the table, open an incision, set the bone with a bone setter, then mend it with bone gel.
wolfmed-treatment-step-bone-fracture-wound-5 = Osteogen knits a simple break without surgery, and stasizium knits any grade.
wolfmed-treatment-avoid-bone-fracture-wound-1 = Set is not mended. The bone is still broken, and a fresh hard blow to the limb undoes a splint.

wolfmed-treatment-short-systemic-bleeding-wound = Blood loss with no one part to blame. Only surgery stops it.
wolfmed-treatment-step-systemic-bleeding-wound-1 = Watch the blood level on the left while you work.
wolfmed-treatment-step-systemic-bleeding-wound-2 = Run the Stop Bleeding surgery through an open incision.
wolfmed-treatment-step-systemic-bleeding-wound-3 = Replace what was lost once the bleeding has stopped.
wolfmed-treatment-avoid-systemic-bleeding-wound-1 = No dressing reaches this. There is no part to put one on.

wolfmed-treatment-short-systemic-bleeding-wound-mechanical = Fluid loss with no one part to blame. Only surgery stops it.
wolfmed-treatment-step-systemic-bleeding-wound-mechanical-1 = Open the chassis and run the Stop Bleeding surgery.
wolfmed-treatment-step-systemic-bleeding-wound-mechanical-2 = Check every part for an unsealed breach afterwards.

wolfmed-treatment-short-internal-bleeding-wound = Bleeding inside the part. No dressing reaches it; it comes out on the table.
wolfmed-treatment-step-internal-bleeding-wound-1 = Note the part. The bleeding is invisible from outside and only the analyzer shows it.
wolfmed-treatment-step-internal-bleeding-wound-2 = Open an incision on that part.
wolfmed-treatment-step-internal-bleeding-wound-3 = Run the Stop Internal Bleeding surgery.
wolfmed-treatment-step-internal-bleeding-wound-4 = Check the organs tab. A destroyed organ or a crushing blow is the usual cause.
wolfmed-treatment-avoid-internal-bleeding-wound-1 = Gauze and sutures do not reach an internal bleed.

wolfmed-treatment-short-surgical-incision-wound = An incision left open. It bleeds while it is open and it is the classic way to catch an infection.
wolfmed-treatment-step-surgical-incision-wound-1 = Finish the procedure you started.
wolfmed-treatment-step-surgical-incision-wound-2 = Close the incision. The last step of every procedure seals it with a cautery.
wolfmed-treatment-step-surgical-incision-wound-3 = If it is still bleeding after that, run the Stop Bleeding surgery.
wolfmed-treatment-step-surgical-incision-wound-4 = Clean the site with antiseptic if the patient lay open for a while.

wolfmed-treatment-short-surgical-incision-wound-mechanical = An open access panel. It leaks while it is open.
wolfmed-treatment-step-surgical-incision-wound-mechanical-1 = Finish the procedure you started.
wolfmed-treatment-step-surgical-incision-wound-mechanical-2 = Seal the panel with a cautery as the last step.
wolfmed-treatment-step-surgical-incision-wound-mechanical-3 = A chassis cannot be infected, so there is nothing to clean.

wolfmed-treatment-short-dismemberment-wound = The stump of a severed limb. It bleeds fast and nothing clots it.
wolfmed-treatment-step-dismemberment-wound-1 = Apply a tourniquet to the stump at once if there is anything to tie around.
wolfmed-treatment-step-dismemberment-wound-2 = Get the patient to a table and run the Stop Bleeding surgery.
wolfmed-treatment-step-dismemberment-wound-3 = Loosen the tourniquet as soon as the bleeding is under control. Ten minutes under one kills what is left.
wolfmed-treatment-step-dismemberment-wound-4 = A severed limb keeps for five minutes. Reattach inside that, or fit a replacement part.
wolfmed-treatment-avoid-dismemberment-wound-1 = Gauze will not hold a stump. This wound carries no damage type, so no topical reaches it.

wolfmed-treatment-short-dismemberment-wound-mechanical = A severed mount. Fluid runs out of it and nothing seals itself.
wolfmed-treatment-step-dismemberment-wound-mechanical-1 = Apply a tourniquet to the stump if there is anything to tie around. On a chassis it is bleeding control and nothing more.
wolfmed-treatment-step-dismemberment-wound-mechanical-2 = Run the Stop Bleeding surgery.
wolfmed-treatment-step-dismemberment-wound-mechanical-3 = Fit a replacement limb.

wolfmed-treatment-short-amputation-consequence-wound = Untreated damage where a limb came off. It hides the surgeries that would attach a new part.
wolfmed-treatment-step-amputation-consequence-wound-1 = Stop the bleeding from the stump first.
wolfmed-treatment-step-amputation-consequence-wound-2 = Open an incision on the stump.
wolfmed-treatment-step-amputation-consequence-wound-3 = Run the Repair Amputation Damage surgery.
wolfmed-treatment-step-amputation-consequence-wound-4 = The attachment surgeries appear once it is gone.

wolfmed-treatment-short-amputation-consequence-wound-mechanical = An unfinished mount where a limb came off. It hides the surgeries that would fit a new one.
wolfmed-treatment-step-amputation-consequence-wound-mechanical-1 = Seal any leak from the mount first.
wolfmed-treatment-step-amputation-consequence-wound-mechanical-2 = Open the mount and run the Repair Amputation Damage surgery.
wolfmed-treatment-step-amputation-consequence-wound-mechanical-3 = The attachment surgeries appear once it is gone.

wolfmed-treatment-short-medical-scar-wound = A scar. It is a record of treatment that finished, not an injury.
wolfmed-treatment-step-medical-scar-wound-1 = Nothing to do. A scar carries no pain, no bleeding and no loss of function.
wolfmed-treatment-step-medical-scar-wound-2 = Nothing removes one either. Treat the findings above it and ignore this row.

# Onyx chassis defaults -------------------------------------------------------

wolfmed-treatment-short-ipc-mechanical-damage-wound = The total damage on an IPC chassis. A welding tool, a wrench or a cable coil brings it down.
wolfmed-treatment-step-ipc-mechanical-damage-wound-1 = Pull out anything embedded first. Nothing repairs a part with an object still in it.
wolfmed-treatment-step-ipc-mechanical-damage-wound-2 = Select the damaged part on the doll.
wolfmed-treatment-step-ipc-mechanical-damage-wound-3 = Use a welding tool on the chassis for dents and breaches. A wrench does dents too and needs no fuel.
wolfmed-treatment-step-ipc-mechanical-damage-wound-4 = Use a cable coil for shock damage.
wolfmed-treatment-avoid-ipc-mechanical-damage-wound-1 = No topical, reagent or medicine reaches a chassis at all.

wolfmed-treatment-short-cybernetic-mechanical-damage-wound = The total damage on a cybernetic limb. A welding tool, a wrench or a cable coil brings it down.
wolfmed-treatment-step-cybernetic-mechanical-damage-wound-1 = Pull out anything embedded first.
wolfmed-treatment-step-cybernetic-mechanical-damage-wound-2 = Select the limb on the doll.
wolfmed-treatment-step-cybernetic-mechanical-damage-wound-3 = Use a welding tool, or a wrench for dents, and a cable coil for shock damage.
wolfmed-treatment-avoid-cybernetic-mechanical-damage-wound-1 = A cybernetic limb is repaired, not treated. Medicine does nothing for it even on a human patient.

wolfmed-treatment-short-cybernetic-frame-fracture-wound = A bent frame in a cybernetic limb. It slows movement and hand work until it is straightened.
wolfmed-treatment-step-cybernetic-frame-fracture-wound-1 = Open the limb.
wolfmed-treatment-step-cybernetic-frame-fracture-wound-2 = Run the Mend Fracture surgery: set the frame with a bone setter, then mend it with bone gel.
wolfmed-treatment-step-cybernetic-frame-fracture-wound-3 = Seal it.

# Onyx non-human tissue -------------------------------------------------------

wolfmed-treatment-short-slime-blunt-wound = A blunt hit on slime tissue. A bruise pack works exactly as it does on flesh.
wolfmed-treatment-step-slime-blunt-wound-1 = Select the injured part on the doll.
wolfmed-treatment-step-slime-blunt-wound-2 = Apply a bruise pack until the wound stops being listed.

wolfmed-treatment-short-slime-slash-wound = A cut in slime tissue. Gauze, then sutures.
wolfmed-treatment-step-slime-slash-wound-1 = Press gauze on the part until the bleeding stops.
wolfmed-treatment-step-slime-slash-wound-2 = Suture it closed.

wolfmed-treatment-short-slime-piercing-wound = A puncture in slime tissue. Gauze, then sutures.
wolfmed-treatment-step-slime-piercing-wound-1 = Check for an embedded object and pull it out first.
wolfmed-treatment-step-slime-piercing-wound-2 = Press gauze on the part, then close it with sutures.

wolfmed-treatment-short-slime-burn-wound = A burn on slime tissue. Ointment or regenerative mesh.
wolfmed-treatment-step-slime-burn-wound-1 = Wash the patient first if acid caused it.
wolfmed-treatment-step-slime-burn-wound-2 = Apply ointment, or regenerative mesh for a deep burn.

wolfmed-treatment-short-plant-blunt-wound = A blunt hit on plant tissue. A bruise pack treats it.
wolfmed-treatment-step-plant-blunt-wound-1 = Select the injured part on the doll.
wolfmed-treatment-step-plant-blunt-wound-2 = Apply a bruise pack until the wound stops being listed.

wolfmed-treatment-short-plant-slash-wound = A cut in plant tissue. It loses sap the way flesh loses blood.
wolfmed-treatment-step-plant-slash-wound-1 = Press gauze on the part until the loss stops.
wolfmed-treatment-step-plant-slash-wound-2 = Suture it closed.

wolfmed-treatment-short-plant-piercing-wound = A puncture in plant tissue. Gauze, then sutures.
wolfmed-treatment-step-plant-piercing-wound-1 = Check for an embedded object and pull it out first.
wolfmed-treatment-step-plant-piercing-wound-2 = Press gauze on the part, then close it with sutures.

wolfmed-treatment-short-plant-burn-wound = A burn on plant tissue. Ointment or regenerative mesh.
wolfmed-treatment-step-plant-burn-wound-1 = Wash the patient first if acid caused it.
wolfmed-treatment-step-plant-burn-wound-2 = Apply ointment, or regenerative mesh for a deep burn.

# W1 ballistic ----------------------------------------------------------------

wolfmed-treatment-short-wolfmed-graze-wound = A round that only caught the limb. It bleeds briefly and stops on its own.
wolfmed-treatment-step-wolfmed-graze-wound-1 = Nothing is needed in a fight. The bleed stops by itself.
wolfmed-treatment-step-wolfmed-graze-wound-2 = Press gauze on the part if you want it closed now.
wolfmed-treatment-step-wolfmed-graze-wound-3 = Clean it with antiseptic if the patient will be a while. An open wound still catches an infection.

wolfmed-treatment-short-wolfmed-gunshot-wound = A round that went through. Good bleeding, real pain, and a high infection risk.
wolfmed-treatment-step-wolfmed-gunshot-wound-1 = Check the part for a lodged round first. Nothing closes a wound with one still in it.
wolfmed-treatment-step-wolfmed-gunshot-wound-2 = Press gauze on the part. That slows the bleeding and cuts the infection rate to about a seventh.
wolfmed-treatment-step-wolfmed-gunshot-wound-3 = Suture it closed, medicated or plain. A sutured wound stops the infection clock outright; a makeshift stitch does not.
wolfmed-treatment-step-wolfmed-gunshot-wound-4 = Clean the site with antiseptic if it was left open: ethanol, bleach or spaceacillin on the skin.
wolfmed-treatment-avoid-wolfmed-gunshot-wound-1 = A bruise pack does nothing here.

wolfmed-treatment-short-wolfmed-lodged-round-wound = A round still in the part. It never clots, never closes, and blocks every other treatment on that part.
wolfmed-treatment-step-wolfmed-lodged-round-wound-1 = Select the part on the targeting doll.
wolfmed-treatment-step-wolfmed-lodged-round-wound-2 = Use a hemostat or tweezers on the patient and wait out the bar. That pulls the round out cleanly.
wolfmed-treatment-step-wolfmed-lodged-round-wound-3 = Any sharp item works instead, a knife, a scalpel or a shard, but it is slower, it hurts, and it leaves a fresh dirty cut that infects faster.
wolfmed-treatment-step-wolfmed-lodged-round-wound-4 = Only then treat what is underneath with gauze and sutures.
wolfmed-treatment-step-wolfmed-lodged-round-wound-5 = A surgical pod does the whole job unattended: its Remove Embedded Objects programme opens the part, takes the round out and closes again.
wolfmed-treatment-avoid-wolfmed-lodged-round-wound-1 = Nothing at all works while an object is in the part. Not gauze, not sutures, not surgery.

wolfmed-treatment-short-wolfmed-shrapnel-wound = Fragments in the part. Each one blocks treatment, and the analyzer counts how many are left.
wolfmed-treatment-step-wolfmed-shrapnel-wound-1 = Select the part on the targeting doll.
wolfmed-treatment-step-wolfmed-shrapnel-wound-2 = Pull one fragment out with a hemostat or tweezers. Repeat until the embedded count reaches zero.
wolfmed-treatment-step-wolfmed-shrapnel-wound-3 = Treat what is underneath with gauze and sutures.
wolfmed-treatment-step-wolfmed-shrapnel-wound-4 = A surgical pod clears every fragment in one pass with its Remove Embedded Objects programme, and closes the part after.
wolfmed-treatment-avoid-wolfmed-shrapnel-wound-1 = Do not dig with a knife unless you have to. It is slower, it hurts, and it leaves a dirty cut.

# W2 slash and bite -----------------------------------------------------------

wolfmed-treatment-short-wolfmed-arterial-bleed-wound = A cut artery. It bleeds several times faster than anything else and it never clots.
wolfmed-treatment-step-wolfmed-arterial-bleed-wound-1 = On an arm, hand, leg or foot, apply a tourniquet. That stops the flow outright. With none to hand, tear a jumpsuit into a makeshift one; a hard hit knocks it loose.
wolfmed-treatment-step-wolfmed-arterial-bleed-wound-2 = Only then will sutures close the wound underneath.
wolfmed-treatment-step-wolfmed-arterial-bleed-wound-3 = In the torso or the head there is nothing to tie around. Run the Repair Severed Artery surgery, which clamps the vessel with a hemostat and then sutures it.
wolfmed-treatment-step-wolfmed-arterial-bleed-wound-4 = With nothing else to hand, hold something hot and use the Cauterise wound verb. It costs a deep burn and a great deal of pain.
wolfmed-treatment-step-wolfmed-arterial-bleed-wound-5 = Take the tourniquet off as soon as the wound is closed. Ten minutes under one kills the limb.
wolfmed-treatment-avoid-wolfmed-arterial-bleed-wound-1 = Gauze only slows an arterial bleed. It buys minutes, it will not take the wound's severity, and it will not close it.

wolfmed-treatment-short-wolfmed-tendon-cut-wound = A severed tendon. The limb stays slow or lame until it is repaired on the table.
wolfmed-treatment-step-wolfmed-tendon-cut-wound-1 = Open an incision on the limb.
wolfmed-treatment-step-wolfmed-tendon-cut-wound-2 = Run the Repair Severed Tendon surgery.
wolfmed-treatment-step-wolfmed-tendon-cut-wound-3 = Seal the incision.
wolfmed-treatment-avoid-wolfmed-tendon-cut-wound-1 = No topical reaches a tendon. Gauze and sutures close the cut over it and change nothing underneath.

wolfmed-treatment-short-wolfmed-avulsion-wound = A bite that tore tissue away. It bleeds, it almost always scars, and it infects readily.
wolfmed-treatment-step-wolfmed-avulsion-wound-1 = Press gauze on the part until the bleeding stops.
wolfmed-treatment-step-wolfmed-avulsion-wound-2 = Suture it closed, medicated or plain.
wolfmed-treatment-step-wolfmed-avulsion-wound-3 = Clean it with antiseptic. A bite is one of the dirtiest wounds there is.
wolfmed-treatment-step-wolfmed-avulsion-wound-4 = Expect a scar. Scars are a record, not an injury.

# W3 blunt trauma -------------------------------------------------------------

wolfmed-treatment-short-wolfmed-crush-injury-wound = Crushed tissue. It slows the limb, seeps at its worst, and often starts an internal bleed.
wolfmed-treatment-step-wolfmed-crush-injury-wound-1 = Apply a bruise pack to the part until the wound clears.
wolfmed-treatment-step-wolfmed-crush-injury-wound-2 = Scan again for internal bleeding, a fracture and a dislocated joint. One heavy hit can leave all three.

wolfmed-treatment-short-wolfmed-concussion-wound = A concussion. Knockdowns, blurred sight and slurred speech until it fades.
wolfmed-treatment-step-wolfmed-concussion-wound-1 = There is no treatment. It fades on its own.
wolfmed-treatment-step-wolfmed-concussion-wound-2 = It fades four times faster while the patient sleeps or lies buckled in a medical bed. Put them in one.
wolfmed-treatment-step-wolfmed-concussion-wound-3 = Check the head for a fracture while you are there.
wolfmed-treatment-avoid-wolfmed-concussion-wound-1 = Painkillers hide what it hurts and do nothing for the concussion itself.

wolfmed-treatment-short-wolfmed-dislocation-wound = A joint out of its socket. It costs the limb its use exactly as a fracture does.
wolfmed-treatment-step-wolfmed-dislocation-wound-1 = Select the limb on the targeting doll.
wolfmed-treatment-step-wolfmed-dislocation-wound-2 = Alt-click the patient and choose Relocate joint.
wolfmed-treatment-step-wolfmed-dislocation-wound-3 = On yourself it takes two and a half times as long and hurts twice as much. Get someone else to do it.
wolfmed-treatment-step-wolfmed-dislocation-wound-4 = A surgical pod sets it with its Relocate Joint programme, which needs neither your hands nor a second person.
wolfmed-treatment-avoid-wolfmed-dislocation-wound-1 = No item sets a joint. A splint is for a broken bone, not a popped one.

wolfmed-treatment-short-wolfmed-organ-contusion-wound = A bruised organ under the bruising. The wound and the organ are treated separately.
wolfmed-treatment-step-wolfmed-organ-contusion-wound-1 = Apply a bruise pack to the chest for the wound itself.
wolfmed-treatment-step-wolfmed-organ-contusion-wound-2 = Read the organ's condition on the organs tab.
wolfmed-treatment-step-wolfmed-organ-contusion-wound-3 = Restore the organ with its own surgery. A bruise pack does nothing for what is under the ribs.

# W4 burns --------------------------------------------------------------------

wolfmed-treatment-short-wolfmed-charring-wound = Charred, dead tissue left by a burn that reached its critical stage. The limb does not work and it can rot.
wolfmed-treatment-step-wolfmed-charring-wound-1 = Open an incision on the part.
wolfmed-treatment-step-wolfmed-charring-wound-2 = Graft it with a skin graft. Skin grafts are in the burn kit and the surgical crate, come out of the medical vendor, and print on a medical lathe.
wolfmed-treatment-step-wolfmed-charring-wound-3 = Seal the incision.
wolfmed-treatment-step-wolfmed-charring-wound-4 = Watch the necrosis flag while you work. Charred tissue is already dying.
wolfmed-treatment-avoid-wolfmed-charring-wound-1 = Nothing in a medkit reaches charred tissue. It carries no damage type, so ointment and mesh both refuse it.

wolfmed-treatment-short-wolfmed-frostbite-wound = Frozen tissue. It numbs the part, so the patient under-reports it, and deep frostbite risks rotting.
wolfmed-treatment-step-wolfmed-frostbite-wound-1 = Get the patient out of the cold first.
wolfmed-treatment-step-wolfmed-frostbite-wound-2 = Apply ointment or regenerative mesh to the part.
wolfmed-treatment-step-wolfmed-frostbite-wound-3 = Trust the analyzer over the patient. A numb limb does not hurt.
wolfmed-treatment-step-wolfmed-frostbite-wound-4 = Watch the necrosis flag. A limb frozen through is already dying.

wolfmed-treatment-short-wolfmed-chemical-burn-wound = Acid still on the skin, eating the part. Wash it off first or the damage comes straight back.
wolfmed-treatment-step-wolfmed-chemical-burn-wound-1 = Rinse the patient. Water from any source works: a splash, a spray bottle, a fire extinguisher, a puddle, space cleaner.
wolfmed-treatment-step-wolfmed-chemical-burn-wound-2 = Only then apply ointment or regenerative mesh to the burn.
wolfmed-treatment-avoid-wolfmed-chemical-burn-wound-1 = Treating the burn before the wash is wasted ointment. The residue keeps eating the part.

wolfmed-treatment-short-wolfmed-internal-burn-wound = Burns inside the body from a strong shock. It damages the heart and locks the muscles for a moment.
wolfmed-treatment-step-wolfmed-internal-burn-wound-1 = Cut the power source before you touch the patient.
wolfmed-treatment-step-wolfmed-internal-burn-wound-2 = Apply ointment or regenerative mesh to the part.
wolfmed-treatment-step-wolfmed-internal-burn-wound-3 = Check the heart on the organs tab and run the Heal Heart surgery if it has lost condition.

# W5 time ---------------------------------------------------------------------

wolfmed-treatment-short-wolfmed-necrosis-wound = Dead tissue. It is permanent, the limb works badly, and it keeps infecting the body toward the chest while it is attached.
wolfmed-treatment-step-wolfmed-necrosis-wound-1 = Nothing treats it. No reagent, no topical and no surgery brings dead tissue back.
wolfmed-treatment-step-wolfmed-necrosis-wound-2 = Amputate the part and fit a replacement.
wolfmed-treatment-step-wolfmed-necrosis-wound-3 = Give spaceacillin for the infection it spreads. Sepsis follows once that reaches the chest or the head.
wolfmed-treatment-avoid-wolfmed-necrosis-wound-1 = Leaving it attached keeps the infection running. It is the source.

# W6 mechanical ---------------------------------------------------------------

wolfmed-treatment-short-wolfmed-dent-wound = Blunt force on a chassis. Cosmetic until it is deep, then it slows the limb.
wolfmed-treatment-step-wolfmed-dent-wound-1 = Select the part on the doll.
wolfmed-treatment-step-wolfmed-dent-wound-2 = Use a welding tool on the chassis, or a wrench, which needs no fuel and does nothing else.
wolfmed-treatment-step-wolfmed-dent-wound-3 = Repeat until the analyzer stops listing it.

wolfmed-treatment-short-wolfmed-breach-wound = A hole in the chassis leaking coolant and hydraulic fluid. It never clots.
wolfmed-treatment-step-wolfmed-breach-wound-1 = Pull out anything embedded first with a hemostat or tweezers. Nothing seals a breach with an object in it.
wolfmed-treatment-step-wolfmed-breach-wound-2 = Weld the breach shut with a welding tool.
wolfmed-treatment-step-wolfmed-breach-wound-3 = Repeat until the leak stops being listed.
wolfmed-treatment-avoid-wolfmed-breach-wound-1 = A wrench and a cable coil do not seal a breach, and no dressing reaches a chassis.

wolfmed-treatment-short-wolfmed-short-circuit-wound = Shorted wiring. The frame locks up and throws sparks each time it worsens.
wolfmed-treatment-step-wolfmed-short-circuit-wound-1 = Cut the power source first.
wolfmed-treatment-step-wolfmed-short-circuit-wound-2 = Select the part on the doll.
wolfmed-treatment-step-wolfmed-short-circuit-wound-3 = Use a cable coil on the chassis until it clears.
wolfmed-treatment-avoid-wolfmed-short-circuit-wound-1 = A welding tool does not reach shorted wiring.

wolfmed-treatment-short-wolfmed-servo-damage-wound = A cut servo run. The limb stays slow or lame until the run behind the panel is replaced.
wolfmed-treatment-step-wolfmed-servo-damage-wound-1 = Open the limb.
wolfmed-treatment-step-wolfmed-servo-damage-wound-2 = Run the Replace Damaged Servo surgery. Its tool is a cable coil.
wolfmed-treatment-step-wolfmed-servo-damage-wound-3 = Seal it.
wolfmed-treatment-avoid-wolfmed-servo-damage-wound-1 = Welding the panel shut does nothing for the servo underneath.

wolfmed-treatment-short-wolfmed-overheating-wound = The part is running too hot to work properly. Time and cold are the only treatments.
wolfmed-treatment-step-wolfmed-overheating-wound-1 = No tool reaches it. The part sheds heat on its own.
wolfmed-treatment-step-wolfmed-overheating-wound-2 = It cools faster in a cold room or under anything cold.
wolfmed-treatment-step-wolfmed-overheating-wound-3 = Fastest of all, hose the patient down with water or a fire extinguisher.

# Conditions ------------------------------------------------------------------

wolfmed-treatment-short-cond-bleeding = This part is losing blood. Gauze slows it, sutures close it, a tourniquet stops a limb outright.
wolfmed-treatment-step-cond-bleeding-1 = Press gauze on the part to slow the bleeding.
wolfmed-treatment-step-cond-bleeding-2 = Suture the wound closed to stop it: medicated, plain, or a makeshift one at a pinch.
wolfmed-treatment-step-cond-bleeding-3 = If it will not stop and it is a limb, apply a tourniquet, or one torn from a jumpsuit, and get the patient to a table.
wolfmed-treatment-step-cond-bleeding-4 = Take the tourniquet off within ten minutes, or the limb dies under it.
wolfmed-treatment-avoid-cond-bleeding-1 = A bruise pack does not stop bleeding, and gauze only slows an arterial bleed.

wolfmed-treatment-short-cond-bleeding-mechanical = The chassis is leaking coolant and hydraulic fluid. A welding tool seals it.
wolfmed-treatment-step-cond-bleeding-mechanical-1 = Pull out anything embedded first.
wolfmed-treatment-step-cond-bleeding-mechanical-2 = Weld the breach shut with a welding tool.
wolfmed-treatment-step-cond-bleeding-mechanical-3 = A chassis breach never clots. It runs until it is sealed.

wolfmed-treatment-short-cond-internal-bleeding = Blood is being lost inside this part. Nothing you can put on the skin reaches it.
wolfmed-treatment-step-cond-internal-bleeding-1 = Get the patient to a table.
wolfmed-treatment-step-cond-internal-bleeding-2 = Open an incision on this part.
wolfmed-treatment-step-cond-internal-bleeding-3 = Run the Stop Internal Bleeding surgery.
wolfmed-treatment-step-cond-internal-bleeding-4 = Check the organs tab. A destroyed organ is the usual cause.

wolfmed-treatment-short-cond-fracture = A broken bone in this part. It slows movement and hand work until it is set and mended.
wolfmed-treatment-step-cond-fracture-1 = In the field, splint an arm, hand, leg or foot. The break goes to set, which is a quarter of the penalty.
wolfmed-treatment-step-cond-fracture-2 = A hairline crack is too slight for a splint to hold.
wolfmed-treatment-step-cond-fracture-3 = On the table, open an incision, set the bone with a bone setter, then mend it with bone gel.
wolfmed-treatment-step-cond-fracture-4 = Osteogen knits a simple break without surgery, and stasizium knits any grade.
wolfmed-treatment-avoid-cond-fracture-1 = Set is not mended, and a fresh hard blow to the limb undoes a splint.

wolfmed-treatment-short-cond-fracture-mechanical = The frame in this part is bent. It slows movement and hand work until it is straightened.
wolfmed-treatment-step-cond-fracture-mechanical-1 = Open the limb.
wolfmed-treatment-step-cond-fracture-mechanical-2 = Set the frame with a bone setter, then mend it with bone gel.
wolfmed-treatment-step-cond-fracture-mechanical-3 = Seal it.

wolfmed-treatment-short-cond-clotting = How the bleeding here is going: clotting in progress, stopped, or only partly stopped.
wolfmed-treatment-step-cond-clotting-1 = Clotting in progress means the wound will stop shortly on its own. Watch it rather than spending gauze.
wolfmed-treatment-step-cond-clotting-2 = Bleeding stopped means this part is no longer losing blood, but the wound is still open and can still infect.
wolfmed-treatment-step-cond-clotting-3 = Partial hemostasis means one wound here has stopped and another has not. Find the one still running.
wolfmed-treatment-step-cond-clotting-4 = An arterial bleed and a wound with an object in it never clot at all.

wolfmed-treatment-short-cond-clotting-mechanical = How the leak here is going: sealant setting, sealed, or only partly sealed.
wolfmed-treatment-step-cond-clotting-mechanical-1 = Sealant setting means the leak will stop shortly.
wolfmed-treatment-step-cond-clotting-mechanical-2 = Leak sealed means this part is no longer losing fluid.
wolfmed-treatment-step-cond-clotting-mechanical-3 = Partially sealed means one breach here is still open. Weld it.

wolfmed-treatment-short-cond-embedded = Objects still in this part. Each one blocks every treatment on the part until it is out.
wolfmed-treatment-step-cond-embedded-1 = Select the part on the targeting doll.
wolfmed-treatment-step-cond-embedded-2 = Use a hemostat or tweezers on the patient. One object comes out per attempt.
wolfmed-treatment-step-cond-embedded-3 = Repeat until the count reaches zero, then treat the wound underneath.
wolfmed-treatment-step-cond-embedded-4 = A sharp item works instead but is slower, hurts, and leaves a dirty cut.

wolfmed-treatment-short-cond-infection-local = The wound itself has gone bad. It hurts, and it reopens faster than a dressing closes it.
wolfmed-treatment-step-cond-infection-local-1 = Apply antiseptic to the skin: ethanol, bleach or spaceacillin. That drains a local infection over the next minute.
wolfmed-treatment-step-cond-infection-local-2 = Close the wound. Sutures or cautery stop the infection clock outright; a makeshift suture does not, it leaves the wound dirty.
wolfmed-treatment-step-cond-infection-local-3 = Gauze alone cuts the rate to about a seventh, and a tourniquet halves it.
wolfmed-treatment-avoid-cond-infection-local-1 = Antiseptic does nothing once the infection has spread past the wound.

wolfmed-treatment-short-cond-infection-spreading = The infection has left the wound and is in the tissue, travelling towards the torso. Nothing systemic yet: the fever and the sepsis start once it reaches the torso or head.
wolfmed-treatment-step-cond-infection-spreading-1 = Give spaceacillin in the bloodstream. Antiseptic on the skin no longer helps.
wolfmed-treatment-step-cond-infection-spreading-2 = Bottles come from the medical vendor and the medical supplies crate. Chemistry makes it from cryptobiolin and inaprovaline in equal parts.
wolfmed-treatment-step-cond-infection-spreading-3 = Close or clean the wound underneath, or it will simply infect again.
wolfmed-treatment-avoid-cond-infection-spreading-1 = More than about 25 units of spaceacillin is poisonous in its own right. Treat and stop.

wolfmed-treatment-short-cond-infection-septic = The infection has the tissue here and travels toward the chest. Sepsis follows once it reaches the chest or the head, and kills if nobody acts.
wolfmed-treatment-step-cond-infection-septic-1 = Give spaceacillin now. It clears every wound and every infected part at once, and pulls back any sepsis.
wolfmed-treatment-step-cond-infection-septic-2 = Find the source. Sepsis keeps growing while the torso or head is infected, and an infected limb keeps feeding them.
wolfmed-treatment-step-cond-infection-septic-3 = A necrotic limb has to come off. Nothing else stops it feeding the sepsis.
wolfmed-treatment-avoid-cond-infection-septic-1 = More than about 25 units of spaceacillin is poisonous in its own right. Treat and stop.

wolfmed-treatment-short-cond-necrosis = The tissue here is dead. It is permanent and it keeps infecting the body toward the chest.
wolfmed-treatment-step-cond-necrosis-1 = Amputate the part and fit a replacement.
wolfmed-treatment-step-cond-necrosis-2 = Give spaceacillin for the infection it spreads, and for the sepsis once that reaches the chest.
wolfmed-treatment-avoid-cond-necrosis-1 = No reagent, topical or surgery brings dead tissue back.

wolfmed-treatment-short-cond-necrosis-risk = Circulation here is failing. The part dies if nothing changes.
wolfmed-treatment-step-cond-necrosis-risk-1 = A tourniquet is a ten-minute clock. Suture what is under it, or use the Loosen tourniquet verb and accept the bleeding.
wolfmed-treatment-step-cond-necrosis-risk-2 = A limb frozen through or burned to charring is already dying. Warm it, treat the burn, or graft the charring now.
wolfmed-treatment-step-cond-necrosis-risk-3 = A severed limb keeps for five minutes. Reattach inside that or it goes back on dead.
wolfmed-treatment-step-cond-necrosis-risk-4 = The patient feels the limb go cold before the end. Believe them.

wolfmed-treatment-short-cond-overheating = The part is running too hot to work properly. Time and cold are the only treatments.
wolfmed-treatment-step-cond-overheating-1 = No tool reaches it.
wolfmed-treatment-step-cond-overheating-2 = Move the patient somewhere cold, or hold something cold against the part.
wolfmed-treatment-step-cond-overheating-3 = Water or a fire extinguisher cools it in one step.

wolfmed-treatment-short-cond-impaired = This part is working at reduced effectiveness. Treat what is listed on this card and it comes back.
wolfmed-treatment-step-cond-impaired-1 = Work through the findings on this card. Fractures, crush injuries, dislocations, severed tendons and damaged servos all cost function.
wolfmed-treatment-step-cond-impaired-2 = A fracture shadows the others. Mend the bone first, then scan again.

wolfmed-treatment-short-cond-disabled = This part does not work at all. Something on this card has to be fixed before it will.
wolfmed-treatment-step-cond-disabled-1 = Mend the fracture first. A broken bone is the usual cause and it hides everything else.
wolfmed-treatment-step-cond-disabled-2 = Relocate a dislocated joint with the Relocate joint verb.
wolfmed-treatment-step-cond-disabled-3 = Severed tendons and damaged servos need their own surgeries.
wolfmed-treatment-step-cond-disabled-4 = Dead tissue never comes back. That part has to be replaced.

wolfmed-treatment-short-cond-unavailable = The part is missing or detached. Nothing on it can be treated while it is off the body.
wolfmed-treatment-step-cond-unavailable-1 = Stop the bleeding from the stump: a tourniquet, then the Stop Bleeding surgery.
wolfmed-treatment-step-cond-unavailable-2 = Run Repair Amputation Damage on the stump before you attach anything.
wolfmed-treatment-step-cond-unavailable-3 = A severed limb keeps for five minutes. Put it back inside that, or fit a replacement part.

wolfmed-treatment-short-cond-sepsis = Systemic infection. It grows while the torso or head is infected, and it kills if it is left alone: its late stage, septic shock, puts the patient out and eats the organs.
wolfmed-treatment-step-cond-sepsis-1 = Give spaceacillin now. It clears every wound and every infected part at once and pulls the sepsis back.
wolfmed-treatment-step-cond-sepsis-2 = Find the source: an infected torso or head, fed by a wound there or by an infection that travelled up a limb from a wound or dead tissue.
wolfmed-treatment-step-cond-sepsis-3 = Amputate a necrotic limb. While it is attached the sepsis keeps climbing.
wolfmed-treatment-avoid-cond-sepsis-1 = More than about 25 units of spaceacillin is poisonous in its own right. Treat and stop.

wolfmed-treatment-short-cond-blood-low = The patient is dangerously low on blood. Stop the loss first, then replace what is gone.
wolfmed-treatment-step-cond-blood-low-1 = Work down this tab and stop every bleeding part. Replacing blood while it is still running out is wasted.
wolfmed-treatment-step-cond-blood-low-2 = Gauze slows a bleed, sutures close it, a tourniquet stops a limb outright.
wolfmed-treatment-step-cond-blood-low-3 = Then give a bloodpack to put back what was lost.

# Evisceration (EVISC).

wolfmed-treatment-short-wolfmed-evisceration-wound = The abdomen is open and the organs are out of it. Nothing carried in a bag closes this; it is an operating table or nothing.
wolfmed-treatment-step-wolfmed-evisceration-wound-1 = Pack the wound with gauze. It slows the bleeding. It will not stop it and it will not close anything.
wolfmed-treatment-step-wolfmed-evisceration-wound-2 = Keep blood or saline going in. The patient is losing it faster than any other wound in the book.
wolfmed-treatment-step-wolfmed-evisceration-wound-3 = Gather the organs off the floor, or fetch replacements, and put each one back with its own insertion surgery. The belly is already open, so there is no incision to cut.
wolfmed-treatment-step-wolfmed-evisceration-wound-4 = Run Close Evisceration. A hemostat clamps the torn vessels first; nothing will close while they are still running.
wolfmed-treatment-step-wolfmed-evisceration-wound-5 = Then the cautery closes the abdomen. It leaves a sutured cut, which ordinary treatment finishes.
wolfmed-treatment-step-wolfmed-evisceration-wound-6 = Clean it with antiseptic and follow up with antibiotics. Nothing in the game gets infected faster.
wolfmed-treatment-avoid-wolfmed-evisceration-wound-1 = Do not expect sutures, topicals or a medibot to touch this. Closing it with organs still missing is fine; the patient then needs transplants, not an open abdomen.

wolfmed-treatment-short-wolfmed-chassis-breach-wound = The torso plating is torn open and the internals are out. Coolant does not clot, so it runs until somebody welds the seam.
wolfmed-treatment-step-wolfmed-chassis-breach-wound-1 = Pack the breach. It slows the leak while you work.
wolfmed-treatment-step-wolfmed-chassis-breach-wound-2 = Put the components back with their own insertion surgery. The casing is already open, so there is nothing to cut.
wolfmed-treatment-step-wolfmed-chassis-breach-wound-3 = Run Weld Chassis Breach. A wrench seats the torn plating first.
wolfmed-treatment-step-wolfmed-chassis-breach-wound-4 = Then weld the seam shut. It leaves an ordinary hole in the casing, which a welder closes normally.
wolfmed-treatment-avoid-wolfmed-chassis-breach-wound-1 = Do not just weld at it. A breach this size refuses the tool outright until the plating has been seated on the table.

# BRAIN: the two body-level findings that outrank everything else on the tab.
wolfmed-treatment-short-cond-cardiac-arrest = The heart has stopped. The brain has a few minutes of oxygen left and then the patient is dead.
wolfmed-treatment-step-cond-cardiac-arrest-1 = Start CPR and keep it going. It does not restart the heart; it buys the brain time.
wolfmed-treatment-step-cond-cardiac-arrest-2 = Get the blood back up. Below 40% the paddles cannot circulate anything.
wolfmed-treatment-step-cond-cardiac-arrest-3 = Then shock them. Every failed shock can be tried again, so keep going.
wolfmed-treatment-avoid-cond-cardiac-arrest-1 = Do not stop CPR to fetch things. Every second without it is oxygen the brain will not get back.
wolfmed-treatment-avoid-cond-cardiac-arrest-2 = Cold slows the clock right down. A body on the way to cryo has far longer than one on a warm floor.

wolfmed-treatment-short-cond-brain-death = The brain organ is destroyed and the patient is dead. A shock brings them back, with major brain damage until the brain is repaired.
wolfmed-treatment-step-cond-brain-death-1 = Get the blood above 40% first. A defibrillator below that does nothing at all.
wolfmed-treatment-step-cond-brain-death-2 = Shock them. They come back with major brain damage: blurred sight, slurred and stuttering speech, a grip that lets go.
wolfmed-treatment-step-cond-brain-death-3 = Then open the head and repair the brain. Nothing else raises brain activity.
wolfmed-treatment-avoid-cond-brain-death-1 = Rot is the one thing that cannot be undone. Get the body cold before you go looking for a surgeon.
wolfmed-treatment-avoid-cond-brain-death-2 = A repaired brain keeps the trauma for about ten minutes. Blurred sight and a shaky grip are expected.
