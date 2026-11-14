"""Ports RMC-14's military clothing to the TSF and PDV loadouts.

    python Tools/_WF/RMCClothing/port.py --source <RMC-14 checkout> [--write]

RMC-14 is an SS14 fork, so its sprites are already RSIs; they are copied as they are, licence and all. Each
item takes a Wolfgate item as its parent and only swaps the sprite, name and description, so its stats are the
parent's: the TSF or PDV item it is an alternative to. RMC draws most marine gear in five camouflage patterns
picked by the map; here a family drawn mostly in camouflage is one item whose pattern the wearer picks in the round
(WFCamoPattern), with a hidden look prototype per pattern for it to copy.

TSF gets the UNMC marine gear, PDV the SPP army and colonial militia gear, and both get the generic field gear.
Each loadout goes into the faction's existing loadout groups as one WOLFGATE(RMCClothing) block per group,
replaced on every run. RMC's uniform accessories (ponchos, ties, armbands, patches, pouches) become neck items,
with their accessory state renamed to equipped-NECK, and RMC's scarves are offered in the neck slot as well.
The marine armour is trimmed to one plate style per cut. Left out: the other M3 plate styles and specialist
armours, which only repeat the same silhouette; medals, which are awards; helmet garb; headsets, which carry
radio channels; and sprites under a non-commercial licence.

Everything under the module's Clothing folders is generated and replaced on each run. Without --write it only
reports what it would port.
"""
import argparse
import glob
import hashlib
import json
import os
import re
import shutil
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
MODULE = "RMCClothing"
PREFIX = "WFRMC"
TEXTURES = os.path.join(ROOT, "Resources", "Textures", "_WF", MODULE, "Clothing")
PROTOTYPES = os.path.join(ROOT, "Resources", "Prototypes", "_WF", MODULE, "Clothing")
LOCALE = os.path.join(ROOT, "Resources", "Locale", "en-US", "_WF", MODULE, "Clothing")
ALL_PROTOTYPES = os.path.join(ROOT, "Resources", "Prototypes")
BLOCK_START = "# WOLFGATE(%s) START: RMC-14 military clothing, written by Tools/_WF/%s/port.py" % (MODULE, MODULE)
BLOCK_END = "# WOLFGATE END"
UNHIDE = "  hidden: false # WOLFGATE(%s): ported RMC-14 clothing gives this slot a choice" % MODULE

CAMO = ["jungle", "desert", "snow", "urban", "classic"]
# Labels for RMC's merged pattern sheets.
LABELS = {
    "jungle-desert-classic": "field",
    "jungle-classic": "woodland",
    "snow-urban": "winter",
    "urban-desert": "arid",
    "classic-jungle": "woodland",
    "snow-desert-urban": "pale",
}

# kind: (texture folder, loadout slot, worn state, {faction: parent})
KINDS = {
    "armor_m": ("OuterClothing", "outerClothing", "equipped-OUTERCLOTHING", {"*": "ClothingOuterArmorBPVestMedium"}),
    "armor_l": ("OuterClothing", "outerClothing", "equipped-OUTERCLOTHING", {"*": "ClothingOuterArmorBPVestLight"}),
    "ghillie": ("OuterClothing", "outerClothing", "equipped-OUTERCLOTHING", {"*": "ClothingOuterArmorBPVestLight"}),
    "jacket": ("OuterClothing", "outerClothing", "equipped-OUTERCLOTHING", {"*": "ClothingOuterStorageBase"}),
    "winter": ("OuterClothing", "outerClothing", "equipped-OUTERCLOTHING", {"*": "ClothingOuterWinterCoat"}),
    "helmet": ("Head", "head", "equipped-HELMET",
               {"tsf": "ClothingHeadHelmetMercenaryBlack", "pdv": "ClothingHeadHelmetCombatPDV",
                "both": "ClothingHeadHelmetMercenaryBlack"}),
    "hat": ("Head", "head", "equipped-HELMET", {"*": "ClothingHeadBaseButcherable"}),
    "uniform": ("Uniforms", "jumpsuit", "equipped-INNERCLOTHING",
                {"tsf": "ClothingUniformTSFMC", "pdv": "ClothingUniformJumpsuitNfsdTacCream"}),
    "boots": ("Shoes", "shoes", "equipped-FEET", {"*": "ClothingShoesBootsCombatFilled"}),
    "gloves": ("Hands", "gloves", "equipped-HAND", {"*": "ClothingHandsGlovesTacticalCombatBlack"}),
    "backpack": ("Back", "back", "equipped-BACKPACK",
                 {"tsf": "ClothingBackpackTsfmc", "pdv": "ClothingBackpackPDV", "both": "NFClothingBackpack"}),
    "cloak": ("Back", "back", "equipped-BACKPACK",
              {"tsf": "ClothingBackpackSatchelTsfmc", "pdv": "ClothingBackpackSatchelPDV",
               "both": "ClothingBackpackSatchel"}),
    "satchel": ("Back", "back", "equipped-BACKPACK",
                {"tsf": "ClothingBackpackSatchelTsfmc", "pdv": "ClothingBackpackSatchelPDV",
                 "both": "ClothingBackpackSatchel"}),
    "belt": ("Belt", "belt", "equipped-BELT",
             {"tsf": "ClothingBeltWebbingsNormalTSFMC", "pdv": "ClothingBeltWebbingsNormalBlack",
              "both": "ClothingBeltWebbingsNormalBlack"}),
    "mask": ("Mask", None, "equipped-MASK", {"*": "ClothingMaskNeckGaiter"}),
    "neck": ("Neck", "neck", "equipped-NECK", {"*": "ClothingNeckBase"}),
    "pouch": ("Neck", "neck", "equipped-NECK", {"*": "WFRMCNeckPouchBase"}),
    "eyes": ("Eyes", "eyes", "equipped-EYES",
             {"tsf": "ClothingEyesHudNfsd", "pdv": "ClothingEyesHudSecurity", "both": "ClothingEyesHudSecurity"}),
}

# Loadout groups per (faction, loadout slot); "cmd" items go only to command groups.
GROUPS = {
    ("tsf", "outerClothing"): ["TsfmcOuterClothing"],
    ("tsf", "outerClothing-cmd"): ["TsfmcOuterClothingColonel"],
    ("tsf", "head"): ["TsfmcHead", "TsfmcHeadMedic", "TsfmcHeadEngi"],
    ("tsf", "jumpsuit"): ["TsfmcJumpsuit", "TsfmcJumpsuitEngi"],
    ("tsf", "shoes"): ["TsfmcShoes"],
    ("tsf", "gloves"): ["TsfmcGloves", "TsfmcBrigmedicGloves"],
    ("tsf", "back"): ["TsfmcBackpack", "TsfmcEngiModsuit"],
    ("tsf", "belt"): ["TsfmcBelt", "TsfmcBrigmedicBelt"],
    ("tsf", "mask"): ["TsfmcFace"],
    ("tsf", "eyes"): ["TsfmcGlasses"],
    ("tsf", "neck"): ["TsfmcNeck", "TsfmcNeckEngi", "TsfmcNeckMedic", "TsfmcNeckFTL", "TsfmcNeckCaptain",
                      "TsfmcNeckCommander"],
    ("tsf", "neck-cmd"): ["TsfmcNeckCaptain", "TsfmcNeckCommander"],
    ("pdv", "outerClothing"): ["PDVOuterClothing", "PDVOuterClothingVizier"],
    ("pdv", "outerClothing-cmd"): ["PDVOuterClothingVizier"],
    ("pdv", "head"): ["PDVHead", "PDVCommanderHead"],
    ("pdv", "head-cmd"): ["PDVCommanderHead"],
    ("pdv", "jumpsuit"): ["PDVJumpsuit"],
    ("pdv", "shoes"): ["PDVShoes", "PDVShoesVizier"],
    ("pdv", "gloves"): ["PDVGloves"],
    ("pdv", "back"): ["PDVBackpack"],
    ("pdv", "belt"): ["PDVBelt"],
    ("pdv", "mask"): ["PDVFace"],
    ("pdv", "eyes"): ["PDVHud"],
    ("pdv", "neck"): ["PDVNeck", "PDVCommanderNeck"],
    ("pdv", "neck-cmd"): ["PDVCommanderNeck"],
}
# Single-choice groups that become pickable once they hold more than one item.
# TSF role loadouts, for role-specific gear.
MEDIC, ENGINEER, FTL = ["JobBrigmedic"], ["JobTsfEngineer"], ["JobSeniorOfficer"]
OFFICERS = ["JobBailiff", "JobSheriff"]

UNHIDE_GROUPS = ["TsfmcGloves", "TsfmcGlasses", "PDVJumpsuit", "PDVGloves"]


class F:
    """One family of items: a sprite folder (every camo pattern), a folder of sprites, or one sprite."""

    def __init__(self, fid, kind, faction, path, name, desc, camo=False, cmd=False, only=None, worn=None,
                 icon=None, names=None, neck=False, roles=None):
        self.fid, self.kind, self.faction, self.path = fid, kind, faction, path
        self.name, self.desc, self.camo, self.cmd, self.only = name, desc, camo, cmd, only
        # worn/icon: the source's state names when they differ from the kind's (accessory sheets); without
        # icon, a sheet's "icon" state is used, or else its one-direction state.
        self.worn, self.icon, self.names, self.neck = worn, icon, names or {}, neck
        # roles: the role loadouts that may wear it; it is only offered in their groups, and locked where a group
        # is shared with other roles.
        self.roles = roles


def spec():
    s = []
    A = "OuterClothing/Armor/"
    # TSF armour
    s += [
        F("ArmorM3", "armor_m", "tsf", A + "m3/standard/padded", "TSFMC M3 pattern armor",
          "Standard-issue TSFMC body armor, drawn in the pattern of the theatre it ships to.", camo=True),
        F("ArmorM3L", "armor_l", "tsf", A + "m3/light/padded", "TSFMC M3-L pattern light armor",
          "A lighter cut of the M3 for marines who would rather run than stop a round.", camo=True),
        F("ArmorM3EOD", "armor_m", "tsf", A + "m3/eod/padded", "TSFMC M3-EOD pattern armor",
          "Bulky M3 plates with extra padding, built for breaching and ordnance work.", camo=True, roles=ENGINEER),
        F("ArmorM3T", "armor_m", "tsf", A + "m3/t", "TSFMC M3-T demolitions armor",
          "M3 armor with blast skirts, built for demolitions teams.", camo=True, roles=ENGINEER),
        F("ArmorB12", "armor_m", "tsf", A + "m3/b12", "TSFMC B12 pattern armor",
          "Heavy shoulder guards over an M3 frame, favoured by frontline squad leaders.", camo=True,
          roles=FTL + ["JobBailiff"]),
        F("ArmorM3G4", "armor_m", "tsf", A + "m3/g4", "TSFMC M3-G4 grenadier armor",
          "M3 armor with bandoliers and reinforced plates for grenadiers.", camo=True, roles=FTL),
        F("ArmorM35", "armor_m", "tsf", A + "m35", "TSFMC M35 pyrotechnician armor",
          "Flame-resistant panels over an M3 frame, worn by marines who carry the fire.", camo=True, roles=FTL),
        F("ArmorML66A", "armor_m", "tsf", A + "ml66a", "TSFMC ML66A combat harness",
          "A heavy harness with a support arm for crew-served weapons.", camo=True, roles=FTL),
        F("ArmorXM4", "armor_m", "tsf", A + "io", "TSFMC XM4 pattern recon armor",
          "A trial armor for reconnaissance marines, with extra pouches for maps and data.", camo=True, roles=FTL),
        F("ArmorM3CO", "armor_m", "tsf", A + "m3/co", "TSFMC M3 pattern commanding officer armor",
          "Tailored M3 armor for a commanding officer, gold trim included.", camo=True, cmd=True),
    ]
    s += [
        F("ArmorM3XL", "armor_l", "tsf", A + "m3/extra-light/standard", "TSFMC M3-XL pattern armor vest",
          "A thin plate carrier for crews and support staff who still want something between them and a bullet.",
          camo=True),
        F("ArmorM4", "armor_m", "tsf", A + "m4", "TSFMC M4 pattern armor",
          "A newer TSFMC armor pattern with a slimmer profile and a reworked collar.", camo=True),
        F("ArmorM3Scout", "armor_l", "tsf", A + "m3/scout", "TSFMC M3-S scout armor",
          "A trimmed-down M3 for scouts who need to move quietly.", camo=True),
        F("ArmorM70Flak", "armor_l", "tsf", A + "pilot", "TSFMC M70 flak jacket",
          "A flak jacket for pilots and dropship crews.", camo=True),
        F("ArmorM3Officer", "armor_m", "tsf", A + "m3/officer", "TSFMC M3 pattern officer armor",
          "M3 armor with officer's trim and a padded collar.", camo=True, cmd="also", roles=OFFICERS),
        F("CoatService", "jacket", "tsf", "OuterClothing/Coats/service", "TSFMC service jacket",
          "A field service jacket in theatre colours.", camo=True),
        F("CoatTanker", "jacket", "tsf", "OuterClothing/Coats/tanker.rsi", "TSFMC tanker jacket",
          "A short leather jacket for vehicle crews."),
        F("CoatPilot", "armor_l", "tsf", "OuterClothing/Coats/pilot.rsi", "TSFMC M70B1 light flak jacket",
          "A light flak jacket cut for pilots."),
        F("CoatDressEnlisted", "jacket", "tsf", "OuterClothing/Coats/DressBlues/enlisted.rsi",
          "TSFMC enlisted dress jacket", "A dress jacket for parades, funerals and court-martials."),
        F("CoatDressSenior", "jacket", "tsf", "OuterClothing/Coats/DressBlues/senior.rsi",
          "TSFMC NCO dress jacket", "A dress jacket with a non-commissioned officer's trim."),
        F("CoatDressGeneral", "jacket", "tsf", "OuterClothing/Coats/DressBlues/general.rsi",
          "TSFMC officer dress jacket", "A dress jacket for commissioned officers.", cmd=True),
    ]
    # PDV armour and coats
    S = "OuterClothing/SPP/"
    s += [
        F("ArmorUM5", "armor_m", "pdv", S + "spp.rsi", "PD-VI UM5 personal armor",
          "Standard plate armor of the Vanguard's line infantry."),
        F("ArmorB80", "armor_m", "pdv", S + "army.rsi", "PD-VI 6B80 body armor",
          "Older army armor, still common in the Vanguard's reserve units."),
        F("ArmorUM5CU", "armor_m", "pdv", S + "commando.rsi", "PD-VI UM5CU commando armor",
          "UM5 armor cut down for raids and boarding actions."),
        F("ArmorUH7", "armor_m", "pdv", S + "heavy.rsi", "PD-VI UH7 heavy plated armor",
          "Thick overlapping plates for assault troops."),
        F("ArmorUL6", "armor_l", "pdv", S + "support.rsi", "PD-VI UL6 support armor",
          "Light armor for medics, signallers and engineers."),
        F("ArmorCCC5", "armor_l", "pdv", S + "sof_vest.rsi", "PD-VI CCC5-L tactical vest",
          "A tactical vest for the Vanguard's special forces."),
        F("ArmorCCC5Heavy", "armor_m", "pdv", S + "sof_heavy.rsi", "PD-VI CCC5-L heavy tactical vest",
          "A special forces vest with extra plates."),
        F("ArmorCCC5Plated", "armor_m", "pdv", S + "sof_plated.rsi", "PD-VI CCC5-L plated tactical vest",
          "A special forces vest with plated pouches."),
        F("CoatUL4", "jacket", "pdv", S + "mp.rsi", "PD-VI UL4 camouflaged jacket",
          "A camouflaged field jacket."),
        F("CoatOfficer", "jacket", "pdv", S + "officer.rsi", "PD-VI officer's field coat",
          "A long field coat for Vanguard officers."),
        F("CoatSenior", "jacket", "pdv", S + "senior.rsi", "PD-VI senior officer's coat",
          "A field coat with a senior officer's insignia."),
        F("CoatPCG", "jacket", "pdv", S + "pcg.rsi", "PD-VI guard coat",
          "A dark coat worn by the Vanguard's palace guard."),
        F("CoatKommissarBlack", "jacket", "pdv", S + "kommisarblackcoat.rsi", "PD-VI UL5-B black coat",
          "A black officer's coat that commands attention.", cmd=True),
        F("CoatKommissarBlackLong", "jacket", "pdv", S + "kommisarblackshinel.rsi", "PD-VI UL5-B black greatcoat",
          "A floor-length black greatcoat.", cmd=True),
        F("CoatKommissarWhite", "jacket", "pdv", S + "kommisarwhitecoat.rsi", "PD-VI UL5-W white coat",
          "A white officer's coat for the court.", cmd=True),
        F("CoatKommissarWhiteLong", "jacket", "pdv", S + "kommisarwhiteshinel.rsi", "PD-VI UL5-W white greatcoat",
          "A floor-length white greatcoat for the court.", cmd=True),
        F("CoatSnowSoviet", "winter", "pdv", "OuterClothing/Coats/Snowsuits/soviet.rsi", "PD-VI winter coat",
          "A padded winter coat in Vanguard drab."),
        F("ArmorMilitiaHauberk", "armor_l", "pdv", "OuterClothing/Militia/rebel.rsi", "militia hauberk",
          "Scrap plates riveted over a padded coat."),
        F("ArmorMilitiaPartial", "armor_l", "pdv", "OuterClothing/Militia/hauberk.rsi", "militia partial hauberk",
          "A hauberk with the sleeves cut away."),
        F("ArmorMilitiaBrace", "armor_l", "pdv", "OuterClothing/Militia/brace.rsi", "militia brace",
          "Shoulder plates on a leather brace."),
        F("ArmorMilitiaVest", "armor_l", "pdv", "OuterClothing/Militia/vest.rsi", "militia vest",
          "A plated vest of the sort colonial militias make for themselves."),
    ]
    # Shared outerwear
    s += [
        F("Ghillie", "ghillie", "both", A + "ghillie", "M45 ghillie suit",
          "A light suit of netting and cut cloth. Settle in, keep still, and you all but vanish.", camo=True),
        F("GhillieOld", "ghillie", "both", A + "ghille.rsi", "patched ghillie suit",
          "An older ghillie suit, mended many times over. Keep still and you all but vanish."),
        F("GhillieUDEP", "ghillie", "both", A + "ghillie/forecon.rsi", "UDEP thermal poncho",
          "A camouflaged rain poncho with an infrared-absorbing coat. Keep still and you all but vanish."),
        F("CoatSnow", "winter", "both", "OuterClothing/Coats/Snowsuits/normal.rsi", "snow suit",
          "A padded snow suit for cold-weather deployments."),
        F("CoatSnowGreen", "winter", "both", "OuterClothing/Coats/Snowsuits/green.rsi", "green parka",
          "A padded green parka."),
        F("CoatSnowNavy", "winter", "both", "OuterClothing/Coats/Snowsuits/navy.rsi", "navy parka",
          "A padded navy parka."),
        F("CoatWindbreaker", "jacket", "both", "OuterClothing/Coats/Windbreakers/windbreaker.rsi",
          "green windbreaker", "A light jacket that keeps the wind and rain off."),
        F("CoatWindbreakerBrown", "jacket", "both", "OuterClothing/Coats/Windbreakers/windbreaker_brown.rsi",
          "brown windbreaker", "A light jacket that keeps the wind and rain off."),
        F("CoatWindbreakerGrey", "jacket", "both", "OuterClothing/Coats/Windbreakers/windbreaker_grey.rsi",
          "grey windbreaker", "A light jacket that keeps the wind and rain off."),
        F("CoatWindbreakerExpedition", "jacket", "both",
          "OuterClothing/Coats/Windbreakers/windbreaker_expedition.rsi", "expedition windbreaker",
          "A windbreaker with reflective panels for survey teams."),
    ]
    # Uniforms
    U = "Uniforms/"
    s += [
        F("UniformMarine", "uniform", "tsf", U + "Marine/standard", "TSFMC combat fatigues",
          "Standard TSFMC fatigues in theatre camouflage.", camo=True),
        F("UniformMarineEngineer", "uniform", "tsf", U + "Marine/engineer", "TSFMC combat engineer fatigues",
          "TSFMC fatigues with an engineer's trim.", camo=True, roles=ENGINEER),
        F("UniformMarineMedic", "uniform", "tsf", U + "Marine/medic", "TSFMC corpsman fatigues",
          "TSFMC fatigues with a corpsman's markings.", camo=True, roles=MEDIC),
        F("UniformMarineRTO", "uniform", "tsf", U + "Marine/rto", "TSFMC radio operator fatigues",
          "TSFMC fatigues for radio operators.", camo=True),
        F("UniformMarineTanker", "uniform", "tsf", U + "Marine/tanker", "TSFMC tanker coveralls",
          "Coveralls for vehicle crews.", camo=True),
        F("UniformIntel", "uniform", "tsf", U + "intel", "TSFMC reconnaissance fatigues",
          "Fatigues for recon and intelligence marines.", camo=True),
        F("UniformBoiler", "uniform", "tsf", U + "boiler", "TSFMC utility coveralls",
          "One-piece work coveralls in theatre colours.", camo=True),
        F("UniformMP", "uniform", "tsf", U + "MP/standard", "TSFMC military police uniform",
          "Fatigues with military police markings.", camo=True),
        F("UniformMarsoc", "uniform", "tsf", U + "marsoc.rsi", "TSFMC raider fatigues",
          "Dark fatigues for special operations."),
        F("UniformDressEnlisted", "uniform", "tsf", U + "DressBlues/enlisted.rsi", "TSFMC enlisted dress uniform",
          "Dress trousers and shirt for formal occasions."),
        F("UniformDressSenior", "uniform", "tsf", U + "DressBlues/senior.rsi", "TSFMC NCO dress uniform",
          "Dress trousers and shirt with an NCO's stripe."),
        F("UniformDressGeneral", "uniform", "tsf", U + "DressBlues/general.rsi", "TSFMC officer dress uniform",
          "Dress trousers and shirt with an officer's stripe."),
        F("UniformPDVArmy", "uniform", "pdv", U + "SPP/army.rsi", "PD-VI army fatigues",
          "Standard Vanguard army fatigues."),
        F("UniformPDVArmyAlt", "uniform", "pdv", U + "SPP/army_alt.rsi", "PD-VI army fatigues, rolled",
          "Vanguard army fatigues with the sleeves rolled."),
        F("UniformPDV", "uniform", "pdv", U + "SPP/uniform.rsi", "PD-VI combat fatigues",
          "Vanguard line infantry fatigues."),
        F("UniformPDVEngineer", "uniform", "pdv", U + "SPP/engi.rsi", "PD-VI engineer fatigues",
          "Vanguard fatigues with an engineer's trim."),
        F("UniformPDVMedic", "uniform", "pdv", U + "SPP/medic.rsi", "PD-VI medic fatigues",
          "Vanguard fatigues with a medic's markings."),
        F("UniformPDVMP", "uniform", "pdv", U + "SPP/mp.rsi", "PD-VI military police fatigues",
          "Vanguard fatigues with military police markings."),
        F("UniformPDVOfficer", "uniform", "pdv", U + "SPP/officer.rsi", "PD-VI officer's uniform",
          "A Vanguard officer's service uniform."),
        F("UniformPDVOfficerShirt", "uniform", "pdv", U + "SPP/officershirtspp.rsi", "PD-VI officer's shirt",
          "An officer's shirt and trousers, jacket off."),
        F("UniformPDVSOF", "uniform", "pdv", U + "SPP/sof_uniform.rsi", "PD-VI special forces fatigues",
          "Dark fatigues for the Vanguard's special forces."),
        F("UniformPDVGuard", "uniform", "pdv", U + "SPP/pcg.rsi", "PD-VI guard uniform",
          "The uniform of the Vanguard's palace guard."),
        F("UniformMilitia", "uniform", "pdv", U + "Survivor/clf.rsi", "militia fatigues",
          "Mismatched fatigues of the sort colonial militias wear."),
    ]
    # Helmets and hats
    H = "Head/Helmets/"
    for fid, folder, name, roles in [("M10", "m10/standard", "M10 pattern helmet", None),
                                     ("M10Medic", "m10/medic", "M10 pattern corpsman helmet", MEDIC),
                                     ("M10Tech", "m10/tech", "M10 pattern technician helmet", ENGINEER),
                                     ("M10Officer", "m10/officer", "M10 pattern officer helmet", OFFICERS),
                                     ("M11", "m11", "M11 pattern helmet", None),
                                     ("M12", "m12", "M12 pattern helmet", None),
                                     ("M30", "m30", "M30 pattern helmet", None),
                                     ("M35", "m35", "M35 pyrotechnician helmet", FTL),
                                     ("M3Demo", "m3/demo", "M3-T demolitions helmet", ENGINEER),
                                     ("M3G4", "m3/g4", "M3-G4 grenadier helmet", FTL),
                                     ("M3Scout", "m3/scout", "M3-S scout helmet", None),
                                     ("M50", "m50", "M50 pattern helmet", None)]:
        s.append(F("Helmet" + fid, "helmet", "tsf", H + folder, "TSFMC " + name,
                   "A TSFMC combat helmet in theatre camouflage.", camo=True, roles=roles))
    s += [
        F("HelmetMarsoc", "helmet", "tsf", H + "marsoc.rsi", "TSFMC raider helmet", "A dark special operations helmet."),
        F("HelmetM45", "helmet", "both", H + "m45", "M45 ghillie hood",
          "A helmet buried under netting and cut cloth, to go with a ghillie suit.", camo=True),
        F("HatBeret", "hat", "tsf", "Head/Hats/beret", "TSFMC field beret", "A beret in theatre colours.", camo=True),
        F("HatBeretTan", "hat", "tsf", "Head/Hats/berettan", "TSFMC tan beret", "A tan beret.", camo=True),
        F("HatCap", "hat", "tsf", "Head/Hats/cap", "TSFMC patrol cap", "A soft patrol cap.", camo=True),
        F("HatCapOfficer", "hat", "tsf", "Head/Hats/cap_officer", "TSFMC officer's cap",
          "A patrol cap with an officer's badge.", camo=True),
        F("HatFlapCap", "hat", "tsf", "Head/Hats/flapcap", "TSFMC flap cap",
          "A patrol cap with a neck flap against sun and sand.", camo=True),
        F("HatDressEnlisted", "hat", "tsf", "Head/Hats/DressBlues/enlisted.rsi", "TSFMC dress cap",
          "A white peaked cap for the dress uniform."),
        F("HatDressOfficer", "hat", "tsf", "Head/Hats/DressBlues/officer.rsi", "TSFMC officer's dress cap",
          "A peaked dress cap with an officer's braid."),
        F("HelmetPDV", "helmet", "pdv", "Head/SPP/helmet.rsi", "PD-VI line helmet", "A Vanguard infantry helmet."),
        F("HelmetPDVArmy", "helmet", "pdv", "Head/SPP/army_helmet.rsi", "PD-VI army helmet",
          "An older Vanguard army helmet."),
        F("HelmetPDVHeavy", "helmet", "pdv", "Head/SPP/heavy.rsi", "PD-VI heavy helmet",
          "A thick helmet for assault troops."),
        F("HelmetPDVEngineer", "helmet", "pdv", "Head/SPP/engi.rsi", "PD-VI engineer helmet",
          "A Vanguard helmet with an engineer's welding shade."),
        F("HatPDVBeret", "hat", "pdv", "Head/SPP/beret.rsi", "PD-VI beret", "A Vanguard beret."),
        F("HatPDVArmyBeret", "hat", "pdv", "Head/SPP/army_beret.rsi", "PD-VI army beret", "A green army beret."),
        F("HatPDVBeretSOF", "hat", "pdv", "Head/SPP/beret_sof.rsi", "PD-VI special forces beret",
          "A red beret of the Vanguard's special forces."),
        F("HatPDVCap", "hat", "pdv", "Head/SPP/cap.rsi", "PD-VI field cap", "A soft field cap."),
        F("HatPDVUshanka", "hat", "pdv", "Head/SPP/ushanka.rsi", "PD-VI ushanka", "A fur hat with ear flaps."),
        F("HatPDVPeaked", "hat", "pdv", "Head/SPP/peaked.rsi", "PD-VI peaked cap",
          "A peaked cap for Vanguard officers.", cmd=True),
        F("HatPDVPeakedBlack", "hat", "pdv", "Head/SPP/kommisar_cap_black.rsi", "PD-VI black peaked cap",
          "A black peaked cap that commands attention.", cmd=True),
        F("HatPDVPeakedWhite", "hat", "pdv", "Head/SPP/kommisar_cap_white.rsi", "PD-VI white peaked cap",
          "A white peaked cap for the court.", cmd=True),
        F("HatMilitiaHood", "hat", "pdv", "Head/Hats/hood_rebel.rsi", "militia hood", "A rough cloth hood."),
        F("HatMilitiaHeadband", "hat", "pdv", "Head/Hats/headband/rebel.rsi", "militia headband",
          "A headband in militia colours."),
        F("HatBoonie", "hat", "both", "Head/Hats/boonie", "boonie hat",
          "A wide-brimmed hat that keeps the sun off.", camo=True),
        F("HatBoonieTan", "hat", "both", "Head/Hats/boonie_tan", "tan-banded boonie hat",
          "A wide-brimmed hat with a tan band.", camo=True),
        F("HatBeanies", "hat", "both", "Head/Beanies", "beanie", "A knitted beanie.", camo="dir",
          only=["beanie", "gray", "green", "tan"]),
        F("HatHeadband", "hat", "both", "Head/Hats/headband", "headband", "A cloth headband.", camo="dir",
          only=["brown", "gray", "green", "red", "tan"]),
    ]
    # Field kit
    s += [
        F("Boots", "boots", "both", "Shoes/Boots", "combat boots", "Sturdy boots for long marches.", camo="dir",
          only=["black", "brown", "grey", "jungle", "urban", "jackboots", "swat"]),
        F("Gloves", "gloves", "both", "Hands/Gloves", "combat gloves", "Padded gloves for rough work.", camo="dir",
          only=["black", "brown", "grey", "light_brown", "leather_black", "veteran"]),
        F("Goggles", "eyes", "both", "Eyes/Glasses", "ballistic goggles",
          "Ballistic goggles with the faction's tactical display built into the lens.", camo="dir",
          only=["ballistic_goggles", "M1A1_Ballistic_goggles", "M1A1_Ballistic_goggles_blue",
                "black_ballistic_goggles", "ballistic_goggles_rcm", "ballistic_goggles_rcm_tint", "swatgoggles"]),
        F("Keffiyeh", "mask", "both", "Mask/Keffiyeh", "keffiyeh", "A cotton scarf to wrap against sun and sand.",
          camo="dir"),
        F("Scarf", "mask", "both", "Mask/Scarfs", "field scarf", "A thick scarf to pull over the face.", camo="dir",
          only=["alpha", "black", "bravo", "charlie", "delta", "echo", "gray", "green", "tan"], neck=True),
        F("Neckerchief", "mask", "both", "Mask/Neckerchief", "neckerchief", "A cloth tied at the neck.",
          camo="dir", only=["alpha", "black", "bravo", "charlie", "delta", "echo", "gray", "green", "tan", "white"],
          neck=True),
        F("Smock", "mask", "both", "Mask/Smock", "sniper's smock", "A camouflaged smock that drapes over the head.",
          camo="dir", neck=True),
        F("TornScarf", "mask", "both", "Mask/TornScarfs", "torn scarf", "A ragged scarf to pull over the face.",
          camo="dir", neck=True),
        F("Coif", "mask", "both", "Mask", "coif", "A cloth hood that covers all but the eyes.", camo="dir",
          only=["balaclava", "coif", "green_coif", "grey_coif", "tan_coif", "skull_coif"]),
        # Neck: RMC's uniform accessories, drawn over the armor.
        F("Poncho", "neck", "both", "Accessory/Ponchos", "field poncho",
          "A waterproof poncho thrown over the gear.", camo=True, worn="poncho"),
        F("NeckGuard", "neck", "tsf", "Accessory/PVE/Marine/neckguard", "TSFMC neck guard",
          "A padded collar that clips onto M3 armor.", camo=True, worn="equipped"),
        F("Tie", "neck", "both", "Accessory/Ties", "tie", "A tie for the dress uniform.", camo="dir", worn="tie",
          names={"horrible": "loud tie"}),
        F("Armband", "neck", "both", "Accessory/Armbands", "armband", "A cloth band worn around the arm.",
          camo="dir", worn="armband",
          only=["red_armband", "alpha_armband", "bravo_armband", "charlie_armband", "delta_armband",
                "echo_armband", "foxtrot_armband", "squad_armband", "med_armband", "med_green_armband",
                "engi_armband", "mp_armband"],
          names={"red_armband": "red armband", "alpha_armband": "crimson armband",
                 "foxtrot_armband": "brown armband", "squad_armband": "squad leader's armband",
                 "bravo_armband": "orange armband",
                 "charlie_armband": "purple armband", "delta_armband": "blue armband",
                 "echo_armband": "emerald armband", "med_armband": "medic's armband",
                 "med_green_armband": "green medic's armband", "engi_armband": "engineer's armband",
                 "mp_armband": "military police armband"}),
        F("DropPouch", "pouch", "both", "Webbing", "drop pouches", "Pouches hung low on the hip.",
          camo="dir", worn="equipped",
          only=["drop_pouch", "drop_pouch_black", "drop_pouch_rcm", "grenade_pouch", "ml66a_pouch"],
          names={"drop_pouch": "drop pouches", "drop_pouch_black": "black drop pouches",
                 "drop_pouch_rcm": "tan drop pouches", "grenade_pouch": "grenade pouches",
                 "ml66a_pouch": "heavy gunner's pouches"}),
        F("LegPouch", "pouch", "both", "Webbing/leg_pouch", "leg pouch", "A pouch strapped to the thigh.",
          camo="dir", worn="equipped"),
        F("ArmorPouches", "pouch", "both", "ArmorWebbing", "armor pouches", "Pouches that clip onto a plate carrier.",
          camo="dir", worn="equipped", only=["mag", "drum", "nade", "shell", "pouch"],
          names={"mag": "magazine pouches", "drum": "drum magazine pouches", "nade": "grenade pouches",
                 "shell": "shotgun shell pouches", "pouch": "utility pouches"}),
        F("ShoulderPads", "neck", "tsf", "Accessory/PVE/Marine/pads", "TSFMC shoulder pads",
          "Armored pads that clip onto M3 armor.", camo=True, worn="equipped"),
        F("Bracers", "neck", "tsf", "Accessory/PVE/Marine/bracers", "TSFMC armored bracers",
          "Armored forearm guards.", camo=True, worn="equipped"),
        F("ThighGuards", "neck", "tsf", "Accessory/PVE/Marine/thighguards", "TSFMC thigh guards",
          "Armored plates strapped over the thighs.", camo=True, worn="equipped"),
        F("ShinGuards", "neck", "tsf", "Accessory/PVE/Marine/shinguards", "TSFMC shin guards",
          "Armored plates strapped over the shins.", camo=True, worn="equipped"),
        F("GroinPlate", "neck", "tsf", "Accessory/PVE/Marine/groinplate", "TSFMC groin plate",
          "An armored plate hung below the vest.", camo=True, worn="equipped"),
        F("ArmorPaint", "neck", "both", "Accessory/PVE/Marine/paint", "armor paint",
          "Paint daubed across the chest plate.", camo="dir", worn="equipped",
          only=["skull", "cross", "heart", "medic", "target", "smiley", "mixtape", "inscription", "neutral"],
          names={"inscription": "inscribed armor paint", "neutral": "striped armor paint"}),
        F("PatchTSFMC", "neck", "tsf", "Accessory/Patches/UN/unmc_patch.rsi", "TSFMC shoulder patch",
          "The corps insignia, sewn on the shoulder.", worn="patch"),
        F("PatchTSFMCRecon", "neck", "tsf", "Accessory/Patches/UN/forecon_patch.rsi", "TSFMC recon patch",
          "The insignia of a reconnaissance battalion.", worn="patch"),
        F("PatchPDV", "neck", "pdv", "Accessory/Patches/SPP/spp_patch.rsi", "PD-VI shoulder patch",
          "The Vanguard's insignia, sewn on the shoulder.", worn="patch"),
        F("PatchPDVAlt", "neck", "pdv", "Accessory/Patches/SPP/spp_patch_alt.rsi", "PD-VI subdued shoulder patch",
          "The Vanguard's insignia in field colours.", worn="patch"),
        F("PatchPDVAirborne", "neck", "pdv", "Accessory/Patches/SPP/spp_vdv_patch.rsi", "PD-VI airborne patch",
          "The insignia of the Vanguard's drop troops.", worn="patch"),
        F("PatchPDVNaval", "neck", "pdv", "Accessory/Patches/SPP/spp_naval_patch.rsi", "PD-VI naval patch",
          "The insignia of the Vanguard's fleet crews.", worn="patch"),
        F("PatchTSFFlag", "neck", "tsf", "Accessory/Patches/UN/un_patch.rsi", "TSF flag patch",
          "The Federation's flag, sewn on the shoulder.", worn="patch"),
        F("PatchTSFFlagSquare", "neck", "tsf", "Accessory/Patches/UN/un_patch_square.rsi", "TSF square flag patch",
          "A square Federation flag.", worn="patch"),
        F("PatchTSFMCSenior", "neck", "tsf", "Accessory/Patches/UN/sea_patch.rsi", "TSFMC senior enlisted patch",
          "Worn by the corps' senior enlisted advisors.", worn="patch"),
        F("PatchTSFMCDevils", "neck", "tsf", "Accessory/Patches/UN/solar_devil_patch.rsi", "TSFMC Solar Devils patch",
          "The insignia of a storied marine battalion.", worn="patch"),
        F("PatchTSFMCFalcons", "neck", "tsf", "Accessory/Patches/UN/FallingFalcons/falcons_patch.rsi", "TSFMC Falling Falcons patch",
          "The insignia of a drop battalion.", worn="patch"),
        F("PatchTSFMCFalconsAlt", "neck", "tsf", "Accessory/Patches/UN/FallingFalcons/falcons_patch_alt.rsi", "TSFMC subdued Falcons patch",
          "The Falling Falcons insignia in field colours.", worn="patch"),
        F("PatchTSFMCFalconsLarge", "neck", "tsf", "Accessory/Patches/UN/FallingFalcons/falcons_patch_large.rsi", "TSFMC large Falcons patch",
          "A large Falling Falcons insignia.", worn="patch"),
        F("PatchPDVMedical", "neck", "pdv", "Accessory/Patches/SPP/msc_patch.rsi", "PD-VI medical service patch",
          "The insignia of the Vanguard's medical service.", worn="patch"),
        F("PatchPDVNavalAlt", "neck", "pdv", "Accessory/Patches/SPP/spp_naval_patch_alt.rsi", "PD-VI subdued naval patch",
          "The fleet insignia in field colours.", worn="patch"),
        F("PatchPDVAirborneAlt", "neck", "pdv", "Accessory/Patches/SPP/spp_vdv_patch_alt.rsi", "PD-VI subdued airborne patch",
          "The drop troops' insignia in field colours.", worn="patch"),
        F("PatchMerc", "neck", "pdv", "Accessory/Patches/Misc/merc_patch.rsi", "mercenary patch",
          "A patch for a sword for hire.", worn="patch"),
        F("PatchMercAlt", "neck", "pdv", "Accessory/Patches/Misc/merc_patch_alt.rsi", "subdued mercenary patch",
          "A mercenary patch in field colours.", worn="patch"),
        F("PatchMedic", "neck", "both", "Accessory/Patches/Misc/medic_patch.rsi", "medic patch",
          "A red cross, sewn on the shoulder.", worn="patch"),
        F("PatchOlive", "neck", "both", "Accessory/Patches/Misc/olive_branch_patch.rsi", "olive branch patch",
          "An olive branch, for the optimists.", worn="patch"),
        F("PatchPeace", "neck", "both", "Accessory/Patches/Misc/peace_patch.rsi", "peace patch",
          "A peace sign, sewn on with some irony.", worn="patch"),
        F("CloakGeneral", "neck", "tsf", "Neck/generalformal.rsi", "TSFMC general's formal cloak",
          "A short cloak worn over the dress uniform by flag officers.", cmd=True),
        # Back: packs, satchels, chest rigs and the thermal cloaks.
        F("BackpackMarineMedic", "backpack", "tsf", "Back/Backpacks/Marines/medic", "TSFMC medical pack",
          "A TSFMC rucksack marked with a red cross.", camo="dir"),
        F("BackpackMarineTech", "backpack", "tsf", "Back/Backpacks/Marines/tech", "TSFMC technician pack",
          "A TSFMC rucksack with tool loops.", camo="dir"),
        F("BackpackMarineWelder", "backpack", "tsf", "Back/Backpacks/Marines/welder", "TSFMC welder pack",
          "A TSFMC rucksack with a fuel canister strapped on.", camo="dir"),
        F("BackpackMarineRadio", "backpack", "tsf", "Back/Backpacks/Marines/rto", "TSFMC radio pack",
          "A TSFMC rucksack with a long-range radio set.", camo="dir", only=["jungle-desert-classic", "snow-urban"]),
        F("BackpackMarineMortar", "backpack", "tsf", "Back/Backpacks/Marines/mortar", "TSFMC mortar pack",
          "A TSFMC rucksack built to carry mortar rounds.", camo="dir"),
        F("BackpackMarineMolle", "backpack", "tsf", "Back/Backpacks/Marines/molle.rsi", "TSFMC MOLLE pack",
          "A rucksack covered in webbing for extra pouches."),
        F("BackpackMarinePhone", "backpack", "tsf", "Back/Backpacks/Marines/telephone.rsi",
          "TSFMC field telephone pack", "A rucksack with a field telephone handset."),
        F("BackpackSmockPlain", "backpack", "both", "Back/Backpacks/Marines/smock.rsi", "camouflage smock pack",
          "A pack under a loose camouflage smock."),
        F("SatchelMarineLarge", "satchel", "tsf", "Back/Satchels/Marines/big", "TSFMC large satchel",
          "A large TSFMC satchel.", camo="dir"),
        F("SatchelMarineMedic", "satchel", "tsf", "Back/Satchels/Marines/medic", "TSFMC medical satchel",
          "A TSFMC satchel marked with a red cross.", camo="dir"),
        F("SatchelMarineTech", "satchel", "tsf", "Back/Satchels/Marines/techi.rsi", "TSFMC technician satchel",
          "A TSFMC satchel with tool loops."),
        F("SatchelMarineIntel", "satchel", "tsf", "Back/Satchels/Marines/intel.rsi", "TSFMC intelligence satchel",
          "A TSFMC satchel for maps and documents."),
        F("SatchelMarineMolle", "satchel", "tsf", "Back/Satchels/Marines/molle.rsi", "TSFMC MOLLE satchel",
          "A satchel covered in webbing for extra pouches."),
        F("SatchelMarineGrenade", "satchel", "tsf", "Back/Satchels/Marines/grenadepack.rsi",
          "TSFMC grenade satchel", "A satchel with grenade loops."),
        F("SatchelMarineRocket", "satchel", "tsf", "Back/Satchels/Marines/rocketpack.rsi", "TSFMC rocket satchel",
          "A satchel with tubes for rockets."),
        F("ChestRigIntel", "satchel", "tsf", "Back/Chestrigs/intel", "TSFMC intelligence chest rig",
          "A chest rig with document pouches.", camo="dir"),
        F("ChestRigTech", "satchel", "tsf", "Back/Chestrigs/tech", "TSFMC technician chest rig",
          "A chest rig with tool pouches.", camo="dir"),
        F("LightPack", "satchel", "both", "Back/Satchels/Other", "light pack", "A small pack for short patrols.",
          camo="dir", only=["lightpack", "lightpack_black", "lightpack_medic"],
          names={"lightpack": "light pack", "lightpack_black": "black light pack",
                 "lightpack_medic": "medical light pack"}),
        F("BackpackPDVRadio", "backpack", "pdv", "Back/Backpacks/SPP/rto.rsi", "PD-VI radio pack",
          "A Vanguard rucksack with a long-range radio set."),
        F("SatchelPDVWelder", "satchel", "pdv", "Back/Satchels/SPP/welder.rsi", "PD-VI welder satchel",
          "A Vanguard satchel with a fuel canister strapped on."),
        F("BackpackBlack", "backpack", "pdv", "Back/Backpacks/PMC/standard.rsi", "black combat pack",
          "A black rucksack of the sort mercenary outfits favour."),
        F("BackpackCommando", "backpack", "pdv", "Back/Backpacks/PMC/commando.rsi", "commando pack",
          "A low-slung black rucksack for raiding parties."),
        F("BackpackBlackRadio", "backpack", "pdv", "Back/Backpacks/PMC/rto.rsi", "black radio pack",
          "A black rucksack with a long-range radio set."),
        F("CloakM68", "cloak", "tsf", "Back/Backpacks/Marines/scout_cloak.rsi", "M68 thermal cloak",
          "A scout's camouflage cloak with an infrared-absorbing lining. Keep still and you all but vanish."),
        F("CloakV86", "cloak", "pdv", "Back/Backpacks/SPP/scout_cloak.rsi", "V86 thermal cloak",
          "The Vanguard's answer to the M68: a camouflage cloak that hides its wearer from sight and heat."),
        F("BackpackMarine", "backpack", "tsf", "Back/Backpacks/Marines/standard", "TSFMC field pack",
          "A TSFMC rucksack.", camo="dir"),
        F("SatchelMarine", "satchel", "tsf", "Back/Satchels/Marines/standard", "TSFMC field satchel",
          "A TSFMC satchel.", camo="dir"),
        F("BackpackSmock", "backpack", "both", "Back/Backpacks/Marines/sniper_smock", "sniper's pack",
          "A pack draped in camouflage cloth.", camo="dir"),
        F("SatchelPDV", "satchel", "pdv", "Back/Satchels/SPP/standard.rsi", "PD-VI field satchel",
          "A Vanguard satchel."),
        F("BeltMarine", "belt", "tsf", "Belt/marine", "TSFMC ammo belt", "A TSFMC belt rig for magazines.",
          camo="dir"),
        F("BeltPDV", "belt", "pdv", "Belt/spp_brown.rsi", "PD-VI ammo belt", "A Vanguard belt rig for magazines."),
        F("BeltKnife", "belt", "both", "Belt/knife", "knife rig", "A belt rig of knife sheaths.", camo="dir"),
        F("BeltShotgun", "belt", "both", "Belt/shotgun", "shotgun shell belt", "A belt of shotgun shell loops.",
          camo="dir"),
        F("BeltSparePouch", "belt", "both", "Belt/sparepouch", "spare pouch belt", "A belt of spare pouches.",
          camo="dir"),
        F("BeltMedicBag", "belt", "tsf", "Belt/medicbag", "TSFMC medical pouch belt",
          "A TSFMC belt with a large medical pouch.", camo="dir"),
        F("BeltMortar", "belt", "tsf", "Belt/mortar", "TSFMC mortar belt", "A belt rig for mortar crews.",
          camo="dir"),
        F("BeltHeavyGunner", "belt", "tsf", "Belt/smart_gun_operator", "TSFMC heavy gunner belt",
          "A belt rig for a heavy weapons gunner.", camo="dir"),
        F("BeltHolster", "belt", "tsf", "Belt/holster_pistol", "TSFMC pistol holster belt",
          "A belt with a pistol holster and magazine pouches.", camo="dir"),
        F("BeltRaider", "belt", "tsf", "Belt/SOCOM.rsi", "TSFMC raider belt", "A dark belt rig for special operations."),
        F("BeltTanker", "belt", "tsf", "Belt/tankbelt.rsi", "TSFMC tanker belt", "A slim belt for vehicle crews."),
        F("BeltMedicBagPDV", "belt", "pdv", "Belt/medicbag_spp.rsi", "PD-VI medical pouch belt",
          "A Vanguard belt with a large medical pouch."),
        F("BeltHolsterPDV", "belt", "pdv", "Belt/holster_pistol_spp.rsi", "PD-VI pistol holster belt",
          "A Vanguard belt with a pistol holster."),
        F("BeltMedicBagBlack", "belt", "pdv", "Belt/medicbag_pmc.rsi", "black medical pouch belt",
          "A black belt with a large medical pouch."),
        F("BeltBlack", "belt", "pdv", "Belt/pmc.rsi", "black combat belt", "A black belt rig for magazines."),
        F("BeltBlackLight", "belt", "pdv", "Belt/pmc_alt.rsi", "light black combat belt",
          "A lighter black belt rig."),
        F("BeltMedical", "belt", "both", "Belt/medical.rsi", "medical belt", "A belt of medical pouches."),
        F("BeltMachinegunner", "belt", "both", "Belt/machinegunner.rsi", "machinegunner's belt",
          "A belt rig for belt-fed ammunition."),
        F("BeltUtility", "belt", "both", "Belt/utility.rsi", "utility belt", "A belt of tool loops."),
        F("BeltCombatUtility", "belt", "both", "Belt/combat_utility.rsi", "combat utility belt",
          "A belt of tool loops with magazine pouches."),
        F("BeltConstruction", "belt", "both", "Belt/construction.rsi", "construction belt",
          "A heavy belt for building materials."),
        F("BeltSecurity", "belt", "both", "Belt/security.rsi", "security belt", "A belt for restraints and batons."),
        F("BeltGrenade", "belt", "both", "Belt/grenade.rsi", "grenade belt", "A belt of grenade loops."),
        F("BeltHolsterSMG", "belt", "both", "Belt/holster_smg.rsi", "SMG holster belt",
          "A belt with a holster for a compact submachine gun."),
        F("BeltHolsterSMGPouch", "belt", "both", "Belt/holster_smg_pouch.rsi", "SMG holster belt with pouches",
          "A belt with a submachine gun holster and magazine pouches."),
        F("BeltTanAmmo", "belt", "both", "Belt/rcm_ammo.rsi", "tan ammo belt", "A tan belt rig for magazines."),
        F("BeltTanGeneral", "belt", "both", "Belt/rcm_general.rsi", "tan general belt", "A tan belt of pouches."),
        F("BeltTanMedical", "belt", "both", "Belt/rcm_medical.rsi", "tan medical belt",
          "A tan belt of medical pouches."),
    ]
    return s


COLOURS = {"alpha": "red", "bravo": "orange", "charlie": "purple", "delta": "blue", "echo": "emerald",
           "gray": "grey", "keffiyeh": "", "beanie": "", "light_brown": "light brown", "leather_black": "black leather",
           "swat": "tactical", "ballistic_goggles": "", "M1A1_Ballistic_goggles": "M1A1",
           "M1A1_Ballistic_goggles_blue": "blue M1A1", "black_ballistic_goggles": "black",
           "ballistic_goggles_rcm": "clear", "ballistic_goggles_rcm_tint": "tinted", "swatgoggles": "tactical",
           "balaclava": "black", "coif": "dark", "green_coif": "green", "grey_coif": "grey", "tan_coif": "tan",
           "skull_coif": "skull"}


# Variants whose name doesn't follow the "<colour> <family>" pattern.
NAMES = {"jackboots": "jackboots"}


def camel(text):
    return "".join(p[:1].upper() + p[1:] for p in re.split(r"[^A-Za-z0-9]+", text) if p)


def pattern_name(variant):
    return LABELS.get(variant, variant).replace("_", "-").lower()


def patterned(fam, variants):
    """A family drawn mostly in camouflage becomes one item with switchable patterns."""
    if len(variants) < 2:
        return False
    return fam.camo is True or sum(v in CAMO or v in LABELS for v in variants) * 2 > len(variants)


class Item:
    def __init__(self, fam, src, variant, twin=False, icon="icon"):
        self.fam, self.src, self.variant, self.twin, self.icon = fam, src, variant, twin, icon
        # A patterned item's per-pattern looks; it is drawn as the first.
        self.patterns = []
        self.kind = "neck" if twin else fam.kind
        self.id = PREFIX + ("Neck" if twin else "") + fam.fid + camel(variant)
        folder, _, _, _ = KINDS[fam.kind]
        family = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", "_", fam.fid).lower()
        self.rsi = "%s/%s/%s.rsi" % (folder, family, variant.lower()) if variant else "%s/%s.rsi" % (folder, family)

    @property
    def name(self):
        if not self.variant:
            return self.fam.name
        if self.patterns:
            return self.fam.name
        if self.variant in self.fam.names:
            return self.fam.names[self.variant]
        if self.variant in NAMES:
            return NAMES[self.variant]
        if self.fam.camo is True or self.variant in CAMO or self.variant in LABELS:
            return "%s (%s)" % (self.fam.name, LABELS.get(self.variant, self.variant))
        # A sheet named after its family, such as keffiyeh_black, gives just the colour.
        variant = re.sub(r"^%s_" % re.escape(self.fam.name.split()[-1]), "", self.variant)
        colour = COLOURS.get(variant, variant.replace("_", " "))
        return ("%s %s" % (colour, self.fam.name)).strip()


def rsi_meta(path):
    with open(os.path.join(path, "meta.json"), encoding="utf-8-sig") as f:
        return json.load(f)


def worn_hash(path, state):
    with open(os.path.join(path, state + ".png"), "rb") as f:
        return hashlib.md5(f.read()).hexdigest()


def collect(source):
    base = os.path.join(source, "Resources", "Textures", "_RMC14", "Objects", "Clothing")
    items, skipped, seen = [], [], {}
    for fam in spec():
        root = os.path.join(base, fam.path)
        if fam.camo is True:
            srcs = [(os.path.join(root, c + ".rsi"), c) for c in CAMO]
        elif fam.camo == "dir":
            names = fam.only or sorted(os.path.basename(p)[:-4] for p in glob.glob(os.path.join(root, "*.rsi")))
            srcs = [(os.path.join(root, n + ".rsi"), n) for n in names]
        else:
            srcs = [(root, "")]
        found = []
        for src, variant in srcs:
            label = "%s %s" % (fam.fid, variant)
            if not os.path.isdir(src):
                skipped.append((label, "missing " + os.path.relpath(src, base)))
                continue
            meta = rsi_meta(src)
            states = {s["name"] for s in meta["states"]}
            state = fam.worn or KINDS[fam.kind][2]
            if "NC" in meta.get("license", ""):
                skipped.append((label, "non-commercial licence"))
                continue
            icon = fam.icon or ("icon" if "icon" in states else next(
                (s["name"] for s in meta["states"] if s.get("directions", 1) == 1 and s["name"] != state), None))
            if icon not in states or state not in states:
                skipped.append((label, "no %s or %s state" % (icon, state)))
                continue
            digest = worn_hash(src, state)
            if digest in seen:
                skipped.append((label, "same sprite as " + seen[digest]))
                continue
            seen[digest] = label
            found.append((Item(fam, src, variant, icon=icon), "equipped-NECK" in states))
        if patterned(fam, [i.variant for i, _ in found]):
            looks = [i for i, _ in found]
            main = Item(fam, looks[0].src, "", icon=looks[0].icon)
            main.rsi, main.patterns = looks[0].rsi, looks
            found = [(main, all(n for _, n in found))]
        for item, has_neck in found:
            items.append(item)
            # A scarf with a neck sheet is also offered in the neck slot, as its own item on the same sprite.
            if fam.neck and has_neck:
                twin = Item(fam, item.src, item.variant, twin=True)
                twin.rsi, twin.patterns = item.rsi, item.patterns
                items.append(twin)
    return items, skipped


def entity_yaml(item):
    fam = item.fam
    kinds = KINDS[item.kind][3]
    parent = kinds.get(fam.faction, kinds.get("*"))
    sprite = "_WF/%s/Clothing/%s" % (MODULE, item.rsi)
    out = ["- type: entity", "  parent: " + parent, "  id: " + item.id, "  components:",
           "  - type: Sprite", "    sprite: " + sprite, "    state: icon"]
    if fam.kind == "gloves":
        out += ["    color: \"#FFFFFF\"", "  - type: Clothing", "    sprite: " + sprite, "    clothingVisuals:",
                "      gloves:", "      - state: equipped-HAND", "  - type: Item", "    inhandVisuals:",
                "      left:", "      - state: inhand-left", "      right:", "      - state: inhand-right"]
    else:
        out += ["  - type: Clothing", "    sprite: " + sprite]
    if fam.kind in ("ghillie", "cloak"):
        out += ["  - type: WFGhillieSuit"]
    if item.patterns:
        out += ["  - type: WFCamoPattern", "    patterns:"]
        for look in item.patterns:
            out += ["    - name: " + pattern_name(look.variant), "      look: " + look_id(look)]
    return "\n".join(out) + "\n"


def look_id(look):
    return PREFIX + "Look" + look.fam.fid + camel(look.variant)


def look_yaml(look, item):
    """A hidden copy of the item in one pattern, for WFCamoPattern to copy visuals from; abstract ones can't be
    indexed."""
    sprite = "_WF/%s/Clothing/%s" % (MODULE, look.rsi)
    return "\n".join(["- type: entity", "  parent: " + item.id, "  id: " + look_id(look),
                      "  categories: [ HideSpawnMenu ]", "  components:",
                      "  - type: Sprite", "    sprite: " + sprite, "    state: icon",
                      "  - type: Clothing", "    sprite: " + sprite,
                      "  - type: Item", "    sprite: " + sprite]) + "\n"


def loadout_id(item):
    return PREFIX + "Loadout" + item.id[len(PREFIX):]


def loadout_yaml(item):
    slot = KINDS[item.kind][1]
    out = ["- type: loadout", "  id: " + loadout_id(item)]
    if locked(item):
        out += ["  effects:", "  - !type:WFRoleWhitelistLoadoutEffect", "    roles:"]
        out += ["    - " + r for r in item.fam.roles]
    if slot is None:
        out += ["  storage:", "    back:", "    - " + item.id]
    else:
        out += ["  equipment:", "    %s: %s" % (slot, item.id)]
    return "\n".join(out) + "\n"


def role_groups():
    """Maps every role loadout to its groups, read from the prototypes."""
    roles = {}
    for path in glob.glob(os.path.join(ALL_PROTOTYPES, "**", "*.yml"), recursive=True):
        with open(path, encoding="utf-8", errors="replace") as f:
            text = f.read()
        for block in re.split(r"^- type: ", text, flags=re.M):
            if not block.startswith("roleLoadout"):
                continue
            rid = re.search(r"^  id: (\S+)", block, re.M)
            groups = re.search(r"^  groups:\s*\n((?:\s*(?:- \S+|#.*)\n?)+)", block, re.M)
            if rid and groups:
                roles[rid.group(1)] = set(re.findall(r"^\s*- (\S+)", groups.group(1), re.M))
    return roles


ROLE_GROUPS = None


def groups_for(item):
    global ROLE_GROUPS
    slot = KINDS[item.kind][1] or "mask"
    factions = ["tsf", "pdv"] if item.fam.faction == "both" else [item.fam.faction]
    out = []
    for faction in factions:
        cmd = (faction, slot + "-cmd")
        if item.fam.cmd and cmd in GROUPS:
            out += GROUPS[cmd] + (GROUPS[(faction, slot)] if item.fam.cmd == "also" else [])
        else:
            out += GROUPS[(faction, slot)]
    if item.fam.roles:
        if ROLE_GROUPS is None:
            ROLE_GROUPS = role_groups()
        used = set().union(*(ROLE_GROUPS.get(r, set()) for r in item.fam.roles))
        out = [g for g in out if g in used]
    return list(dict.fromkeys(out))


def locked(item):
    """Whether one of the item's groups is shared with roles that may not wear it."""
    if not item.fam.roles:
        return False
    groups = set(groups_for(item))
    return any(r not in item.fam.roles and gs & groups for r, gs in ROLE_GROUPS.items())


def ftl_escape(text):
    return text.replace("{", "{\"{\"}")


# Marked blocks in upstream loadout groups

def find_group(group_id):
    for path in glob.glob(os.path.join(ALL_PROTOTYPES, "**", "*.yml"), recursive=True):
        if os.sep + "_WF" + os.sep in path:
            continue
        with open(path, encoding="utf-8", errors="replace") as f:
            text = f.read()
        if re.search(r"^  id: %s\s*$" % re.escape(group_id), text, re.M) and "type: loadoutGroup" in text:
            return path
    raise SystemExit("no loadout group " + group_id)


def edit_group(path, group_id, loadouts, unhide):
    with open(path, encoding="utf-8", newline="") as f:
        text = f.read()
    nl = "\r\n" if "\r\n" in text else "\n"
    lines = text.split(nl)
    start = next(i for i, l in enumerate(lines) if re.match(r"^  id: %s\s*$" % re.escape(group_id), l))
    while not lines[start].startswith("- type:"):
        start -= 1
    end = next((i for i in range(start + 1, len(lines)) if lines[i].startswith("- type:")), len(lines))
    proto, skipping = [], False
    for line in lines[start:end]:
        if line.strip().startswith("# WOLFGATE(%s) START" % MODULE):
            skipping = True
            continue
        if skipping:
            skipping = line.strip() != BLOCK_END
            continue
        proto.append(line)
    if unhide:
        # The upstream line may carry its own comment ("hidden: true # No point in showing this...").
        proto = [UNHIDE if re.match(r"^  hidden: true(\s|$)", l) else l for l in proto]
    field = next(i for i, l in enumerate(proto) if l.startswith("  loadouts:"))
    last = field
    for i in range(field + 1, len(proto)):
        if proto[i].startswith("  - ") or proto[i].startswith("  #"):
            last = i
        elif proto[i].strip():
            break
    block = ["  " + BLOCK_START] + ["  - " + l for l in loadouts] + ["  " + BLOCK_END]
    proto = proto[:last + 1] + block + proto[last + 1:]
    out = nl.join(lines[:start] + proto + lines[end:])
    if out != text:
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(out)


def copy_rsi(item):
    """Copies the item's RSI, renaming accessory states to the slot's icon and worn states."""
    fam = item.fam
    dest = os.path.join(TEXTURES, item.rsi)
    shutil.copytree(item.src, dest)
    meta = rsi_meta(dest)
    meta["copyright"] = meta.get("copyright", "").rstrip() + " Ported from RMC-14 (https://github.com/RMC-14/RMC-14)."
    renames = {item.icon: "icon"}
    if fam.worn:
        renames[fam.worn] = KINDS[fam.kind][2]
    for state in meta["states"]:
        new = renames.get(state["name"])
        if new and new != state["name"]:
            os.replace(os.path.join(dest, state["name"] + ".png"), os.path.join(dest, new + ".png"))
            state["name"] = new
    with open(os.path.join(dest, "meta.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(meta, f, indent=2, ensure_ascii=False)
        f.write("\n")


def write(items):
    for path in (TEXTURES, PROTOTYPES, LOCALE):
        shutil.rmtree(path, ignore_errors=True)
        os.makedirs(path)
    entities, loadouts, ftl, groups, looks, names = {}, [], {}, {}, [], set()
    for item in items:
        if not item.twin:
            for look in item.patterns or [item]:
                copy_rsi(look)
            for look in item.patterns:
                looks.append(look_yaml(look, item))
                names.add((pattern_name(look.variant), LABELS.get(look.variant, look.variant).replace("_", " ")))
                ftl.setdefault("Looks", []).append("ent-%s = %s (%s)\n    .desc = %s\n" % (
                    look_id(look), ftl_escape(item.name), LABELS.get(look.variant, look.variant).replace("_", " "),
                    ftl_escape(item.fam.desc)))
        folder = KINDS[item.kind][0]
        entities.setdefault(folder, []).append(entity_yaml(item))
        loadouts.append(loadout_yaml(item))
        ftl.setdefault(folder, []).append("ent-%s = %s\n    .desc = %s\n" % (
            item.id, ftl_escape(item.name), ftl_escape(item.fam.desc)))
        for group in groups_for(item):
            groups.setdefault(group, []).append(loadout_id(item))
    os.makedirs(os.path.join(PROTOTYPES, "Entities"))
    for folder, docs in entities.items():
        with open(os.path.join(PROTOTYPES, "Entities", folder + ".yml"), "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(docs))
    with open(os.path.join(PROTOTYPES, "loadouts.yml"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(loadouts))
    with open(os.path.join(PROTOTYPES, "patterns.yml"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(looks))
    # The outfitters stock every item a faction may wear without a role or command lock, without limit.
    with open(os.path.join(PROTOTYPES, "vending.yml"), "w", encoding="utf-8", newline="\n") as f:
        for faction, vendor in (("tsf", "Tsf"), ("pdv", "Pdv")):
            stock = [i.id for i in items if i.fam.faction in (faction, "both") and not i.fam.roles and not i.fam.cmd]
            f.write("- type: vendingMachineInventory\n  id: %s%sOutfitterInventory\n  startingInventory:\n"
                    % (PREFIX, vendor))
            f.write("".join("    %s: 4294967295\n" % i for i in stock))
            f.write("\n" if faction == "tsf" else "")
    with open(os.path.join(LOCALE, "patterns.ftl"), "w", encoding="utf-8", newline="\n") as f:
        f.write("".join("wf-camo-pattern-%s = %s\n" % (key, label[:1].upper() + label[1:])
                        for key, label in sorted(names)))
    for folder, lines in ftl.items():
        with open(os.path.join(LOCALE, folder.lower() + ".ftl"), "w", encoding="utf-8", newline="\n") as f:
            f.write("".join(lines))
    for group in sorted(set(groups) | set(UNHIDE_GROUPS)):
        edit_group(find_group(group), group, groups.get(group, []), group in UNHIDE_GROUPS)
    return groups


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--source", required=True, help="RMC-14 checkout")
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()
    items, skipped = collect(args.source)
    for label, why in skipped:
        print("skip %-40s %s" % (label, why))
    counts = {}
    for item in items:
        key = item.fam.faction
        counts[key] = counts.get(key, 0) + 1
    print("%d items (%s)" % (len(items), ", ".join("%s %d" % kv for kv in sorted(counts.items()))))
    if args.write:
        groups = write(items)
        print("wrote %d loadout groups" % len(groups))


if __name__ == "__main__":
    sys.exit(main())
