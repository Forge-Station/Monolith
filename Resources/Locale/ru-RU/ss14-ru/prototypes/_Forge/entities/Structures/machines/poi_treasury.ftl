# Казны захвата POI (Resources/Prototypes/_Forge/Entities/Structures/Machines/poi_treasury.yml)

ent-PoiTreasury = казна POI
    .desc = Укреплённый сейф у консоли захвата этой точки интереса. Заглянуть внутрь может любой, а забирать предметы — только текущий лидер захвата. Пополняется налогом с продаж и периодическими наградами.

ent-PoiTreasuryDebug = { ent-PoiTreasury }
    .suffix = спесо, сталь, пласталь
    .desc = { ent-PoiTreasury.desc }

ent-PoiTreasuryTrade = { ent-PoiTreasury }
    .suffix = спесо 10/100/1000 (умеренно)
    .desc = { ent-PoiTreasury.desc }

ent-PoiTreasuryCargo = { ent-PoiTreasury }
    .suffix = спесо, сталь, пласталь, картон
    .desc = { ent-PoiTreasury.desc }

ent-PoiTreasuryRestStop = { ent-PoiTreasury }
    .suffix = спесо, ИРП, пиво, сигареты
    .desc = { ent-PoiTreasury.desc }

ent-PoiTreasuryShelter = { ent-PoiTreasury }
    .suffix = спесо, T2 аптечки, фасоль
    .desc = { ent-PoiTreasury.desc }

ent-PoiTreasuryCasino = { ent-PoiTreasury }
    .suffix = спесо, кости, сигареты
    .desc = { ent-PoiTreasury.desc }

ent-PoiTreasuryCombat = { ent-PoiTreasury }
    .suffix = спесо, оружие+патроны, T2 мед, ножи
    .desc = { ent-PoiTreasury.desc }

ent-PoiTreasuryIndustrial = { ent-PoiTreasury }
    .suffix = спесо, сталь, пласталь, кабель, редкие мат.
    .desc = { ent-PoiTreasury.desc }

ent-PoiTreasuryChapel = { ent-PoiTreasury }
    .suffix = спесо, свеча, библия
    .desc = { ent-PoiTreasury.desc }

ent-PoiTreasuryHighRisk = { ent-PoiTreasury }
    .suffix = спесо, телекристалл, оружие, T2 мед
    .desc = { ent-PoiTreasury.desc }

ent-PoiTreasuryScrap = { ent-PoiTreasury }
    .suffix = спесо, лом, сталь, сварка, инструменты
    .desc = { ent-PoiTreasury.desc }

ent-PoiTreasuryScience = { ent-PoiTreasury }
    .suffix = спесо, плазма, колба, сканер, анализатор
    .desc = { ent-PoiTreasury.desc }

ent-PoiTreasuryMining = { ent-PoiTreasury }
    .suffix = сталь, пласталь, плазма, алмаз, кирка ×2
    .desc = { ent-PoiTreasury.desc }

ent-PoiTreasuryTech = { ent-PoiTreasury }
    .suffix = спесо, батарея, кабель, сталь, флешка
    .desc = { ent-PoiTreasury.desc }

ent-PoiTreasuryDungeonTech = { ent-PoiTreasury }
    .suffix = диск 5k/10k, пласталь, спесо
    .desc = { ent-PoiTreasury.desc }

ent-PoiTreasuryBio = { ent-PoiTreasury }
    .suffix = спесо, T2 аптечки, химия
    .desc = { ent-PoiTreasury.desc }

ent-PoiTreasuryFuel = { ent-PoiTreasury }
    .suffix = сварка, плазма, O2, спесо, сталь
    .desc = { ent-PoiTreasury.desc }

# --- Варианты под конкретные POI (суффикс = пул наград родителя) ---

ent-PoiTreasuryTradeMall = { ent-PoiTreasuryTrade }
    .desc = { ent-PoiTreasuryTrade.desc }
ent-PoiTreasuryCargoDepot = { ent-PoiTreasuryCargo }
    .desc = { ent-PoiTreasuryCargo.desc }
ent-PoiTreasuryGrifty = { ent-PoiTreasuryRestStop }
    .suffix = спесо, сварка, O2, ИРП, пиво
    .desc = { ent-PoiTreasury.desc }

ent-PoiTreasuryCaseysCasino = { ent-PoiTreasuryCasino }
    .desc = { ent-PoiTreasuryCasino.desc }
ent-PoiTreasuryBahama = { ent-PoiTreasuryRestStop }
    .desc = { ent-PoiTreasuryRestStop.desc }
ent-PoiTreasuryTinnia = { ent-PoiTreasuryShelter }
    .desc = { ent-PoiTreasuryShelter.desc }
ent-PoiTreasuryThePit = { ent-PoiTreasuryCombat }
    .desc = { ent-PoiTreasuryCombat.desc }
ent-PoiTreasuryEdison = { ent-PoiTreasuryIndustrial }
    .desc = { ent-PoiTreasuryIndustrial.desc }
ent-PoiTreasuryOmnichurch = { ent-PoiTreasuryChapel }
    .desc = { ent-PoiTreasuryChapel.desc }
ent-PoiTreasuryLPBravo = { ent-PoiTreasuryHighRisk }
    .desc = { ent-PoiTreasuryHighRisk.desc }
ent-PoiTreasuryMcHobo = { ent-PoiTreasuryScrap }
    .desc = { ent-PoiTreasuryScrap.desc }
ent-PoiTreasuryAnomalousLab = { ent-PoiTreasuryScience }
    .desc = { ent-PoiTreasuryScience.desc }
ent-PoiTreasuryMiningDrill = { ent-PoiTreasuryMining }
    .desc = { ent-PoiTreasuryMining.desc }
ent-PoiTreasurySevastopol = { ent-PoiTreasuryTech }
    .desc = { ent-PoiTreasuryTech.desc }
ent-PoiTreasuryHammerOfTheUnion = { ent-PoiTreasuryDungeonTech }
    .desc = { ent-PoiTreasuryDungeonTech.desc }
ent-PoiTreasuryPolaris = { ent-PoiTreasuryBio }
    .desc = { ent-PoiTreasuryBio.desc }
ent-PoiTreasuryAutomatedTanker = { ent-PoiTreasuryFuel }
    .desc = { ent-PoiTreasuryFuel.desc }
ent-PoiTreasuryLancelot = { ent-PoiTreasuryMining }
    .desc = { ent-PoiTreasuryMining.desc }
