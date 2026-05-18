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

ent-PoiTreasuryCargoDepot = { ent-PoiTreasuryCargo }

ent-PoiTreasuryGrifty = { ent-PoiTreasuryRestStop }
    .suffix = спесо, сварка, O2, ИРП, пиво
    .desc = { ent-PoiTreasury.desc }

ent-PoiTreasuryCaseysCasino = { ent-PoiTreasuryCasino }

ent-PoiTreasuryBahama = { ent-PoiTreasuryRestStop }

ent-PoiTreasuryTinnia = { ent-PoiTreasuryShelter }

ent-PoiTreasuryThePit = { ent-PoiTreasuryCombat }

ent-PoiTreasuryEdison = { ent-PoiTreasuryIndustrial }

ent-PoiTreasuryOmnichurch = { ent-PoiTreasuryChapel }

ent-PoiTreasuryLPBravo = { ent-PoiTreasuryHighRisk }

ent-PoiTreasuryMcHobo = { ent-PoiTreasuryScrap }

ent-PoiTreasuryAnomalousLab = { ent-PoiTreasuryScience }

ent-PoiTreasuryMiningDrill = { ent-PoiTreasuryMining }

ent-PoiTreasurySevastopol = { ent-PoiTreasuryTech }

ent-PoiTreasuryHammerOfTheUnion = { ent-PoiTreasuryDungeonTech }

ent-PoiTreasuryPolaris = { ent-PoiTreasuryBio }

ent-PoiTreasuryAutomatedTanker = { ent-PoiTreasuryFuel }

ent-PoiTreasuryLancelot = { ent-PoiTreasuryMining }
