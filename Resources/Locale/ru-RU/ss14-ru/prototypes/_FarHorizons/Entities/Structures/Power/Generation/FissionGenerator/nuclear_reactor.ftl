ent-BaseNuclearReactor = ядерный реактор
    .desc = Корпус ядерного реактора с гнёздами для топливных стержней и других компонентов. Эй, постойте… разве один из таких когда-то не взорвался?

# Crew nuclear reactors
ent-NuclearReactorCrew = { ent-BaseNuclearReactor }
    .desc = { ent-BaseNuclearReactor.desc }
ent-NuclearReactorNormal = { ent-BaseNuclearReactor }
    .desc = { ent-BaseNuclearReactor.desc }
ent-NuclearReactorEmpty = { ent-BaseNuclearReactor }
    .desc = { ent-BaseNuclearReactor.desc }
    .suffix = Пустой
ent-NuclearReactorRandom = { ent-BaseNuclearReactor }
    .desc = { ent-BaseNuclearReactor.desc }
    .suffix = Случайный
ent-NuclearReactorMeltdown = { ent-BaseNuclearReactor }
    .desc = { ent-BaseNuclearReactor.desc }
    .suffix = Расплавление
ent-NuclearReactorMelted = { ent-BaseNuclearReactor }
    .desc = Корпус ядерного реактора, давно подвергшийся расплавлению активной зоны. До сих пор излучает остаточное тепло и радиацию.
    .suffix = Расплавлен
ent-NuclearReactorSmall = малый ядерный реактор
    .desc = { ent-BaseNuclearReactor.desc }
ent-NuclearReactorSmallRandom = { ent-NuclearReactorSmall }
    .desc = { ent-BaseNuclearReactor.desc }
    .suffix = Случайный
ent-NuclearReactorSmallMelted = { ent-NuclearReactorSmall }
    .desc = { ent-BaseNuclearReactor.desc }
    .suffix = Расплавлен

# Salvage nuclear reactors
ent-NuclearReactorSalvage = { ent-BaseNuclearReactor }
    .desc = { ent-BaseNuclearReactor.desc }
ent-NuclearReactorNormalSalvage = { ent-NuclearReactorSalvage }
    .desc = { ent-NuclearReactorSalvage.desc }
    .suffix = Утилизация
ent-NuclearReactorEmptySalvage = { ent-NuclearReactorSalvage }
    .desc = { ent-NuclearReactorSalvage.desc }
    .suffix = Пустой, Утилизация
ent-NuclearReactorRandomSalvage = { ent-NuclearReactorSalvage }
    .desc = { ent-NuclearReactorSalvage.desc }
    .suffix = Случайный, Утилизация
ent-NuclearReactorMeltedSalvage = { ent-NuclearReactorSalvage }
    .desc = { ent-NuclearReactorMelted.desc }
    .suffix = Расплавлен, Утилизация

# Small salvage nuclear reactors
ent-NuclearReactorSmallSalvage = { ent-NuclearReactorSmall }
    .desc = { ent-NuclearReactorSalvage.desc }
    .suffix = Расплавлен, Утилизация
ent-NuclearReactorSmallRandomSalvage = { ent-NuclearReactorSmall }
    .desc = { ent-NuclearReactorSalvage.desc }
    .suffix = Случайный, Утилизация
ent-NuclearReactorSmallMeltedSalvage = { ent-NuclearReactorSmall }
    .desc = { ent-NuclearReactorMelted.desc }
    .suffix = Расплавлен, Утилизация

# Nuclear debris resultant from a meltdown
ent-NuclearDebrisChunk = ядерные обломки
    .desc = Вы не замечаете графит на полу. Вы в шоке. Немедленно обратитесь в медблок.
