## Магазин / окно эволюции

hive-store-name = Эволюция Улья
store-currency-display-hive-dna = Очки ДНК
store-category-hive-abilities = Способности Улья

## Инъектор

hive-injector-empty = Инъектор пуст.
hive-injector-already-member = Вы уже часть Улья.
hive-injector-injected = Холодная жидкость расходится по венам... Вы слышите шёпот Улья.

## Пожирание

hive-devour-start-self = Вы начинаете пожирать { $target }...
hive-devour-start-others = { CAPITALIZE($user) } вгрызается в { $target }!
hive-devour-end-self = Вы пожрали { $target } и получили { $dna } очк. ДНК.
hive-devour-end-self-nodna = Вы пожрали { $target }, но не получили ничего ценного.
hive-devour-end-others = { CAPITALIZE($user) } пожирает { $target }!

hive-devour-fail-invalid = { CAPITALIZE($target) } не годится для пожирания.
hive-devour-fail-already = От { $target } уже ничего не осталось.
hive-devour-fail-hive = Вы не можете пожрать собрата по Улью.
hive-devour-fail-alive = { CAPITALIZE($target) } ещё слишком жив(а).
hive-devour-fail-not-dead = { CAPITALIZE($target) } ещё жив(а).

hive-devoured-unrevivable = Тело выедено изнутри. Реанимация невозможна.

## Прототипы

ent-ActionHiveShop = Эволюция
    .desc = Открыть окно эволюции и приобрести способности Улья за очки ДНК.

ent-ActionHiveDevour = Пожирание
    .desc = Пожрать мёртвое или умирающее тело гуманоида, получив его ДНК. Тело после этого нельзя будет реанимировать.

ent-HiveInjector = инъектор Улья
    .desc = Одноразовый инъектор с мутной зеленоватой жидкостью. Кажется, она шевелится.

hive-spell = Во славу Матери и Улья!