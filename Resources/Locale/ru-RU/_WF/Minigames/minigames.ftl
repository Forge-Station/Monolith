## Игры, которые НПС играют за ставку (Content.Server/_WF/Minigames).

wf-minigame-name-orepiles = Рудные Отвалы
wf-minigame-name-liarsdice = Кости лжеца
wf-minigame-name-lockbox = Сейф
wf-minigame-name-higherlower = Выше или Ниже
wf-minigame-name-memory = Память
wf-minigame-name-riddles = Загадки

wf-minigame-difficulty-easy = легкий стол
wf-minigame-difficulty-normal = обычный стол
wf-minigame-difficulty-hard = сложный стол

## Предложение

wf-minigame-offer = {$game}, {$difficulty}. Ставка {$stake}. Победа приносит {$prize}. Играете?
wf-minigame-offer-ladder = {$game}, {$difficulty}. Ставка {$stake}. Полный заход приносит {$prize}; снятие раньше — меньше. Играете?
wf-minigame-declined = Как хотите...
wf-minigame-cooldown = Вы уже получили выигрыш от меня. Вернитесь через {$minutes ->
    [one] минуту
    *[other] {$minutes} минут
}.
wf-minigame-house-broke = Я выплатил всё, что мог сегодня. Попробуйте завтра.
wf-minigame-begin = Начнем играть!
wf-minigame-reward-item = {$count ->
    [one] {$item}
    *[other] {$count}x {$item}
}

## Окно

wf-minigame-window-title = Игра
wf-minigame-window-header = {$game}, {$difficulty}
wf-minigame-window-stakes = Ставка {$stake}. Приз {$prize}.
wf-minigame-window-won = Вы выиграли.
wf-minigame-window-lost = Вы проиграли.
wf-minigame-window-thinking = Они думают...
wf-minigame-line-start = Ваш ход.

## Рудные Отвалы

wf-minigame-ore-rules = Взять 1–3 руды за ход. Тот, кто берет последнюю, проигрывает.
wf-minigame-ore-take = Взять {$count}
wf-minigame-ore-pile = Осталось {$pile} руды
wf-minigame-ore-start = {$pile} руды на столе. Вы ходите первыми.
wf-minigame-ore-player-took = Вы взяли {$count}. Осталось {$pile}.
wf-minigame-ore-npc-took = Они взяли {$count}. Осталось {$pile}.
wf-minigame-ore-player-last = Вы взяли последнюю руду.
wf-minigame-ore-npc-last = Им пришлось взять последнюю руду.

## Кости лжеца

wf-minigame-dice-rules = Сделайте ставку на количество костей с определенной гранью или обвините в лжи предыдущую ставку.
wf-minigame-dice-bid = Ставка
wf-minigame-dice-call = Лжец!
wf-minigame-dice-pick = {$count} x {$face}
wf-minigame-dice-round = Раунд {$round} из {$rounds}
wf-minigame-dice-yours = Ваши кости: {$dice}
wf-minigame-dice-theirs = Их кости: {$count} под чашкой
wf-minigame-dice-theirs-shown = Их кости в прошлом раунде: {$dice}
wf-minigame-dice-no-bid = Нет ставки. Вы открываете.
wf-minigame-dice-your-bid = Ваша ставка: {$count} x {$face}
wf-minigame-dice-their-bid = Их ставка: {$count} x {$face}
wf-minigame-dice-start = Пять костей каждому, без джокеров. Выиграйте {$rounds} раундов подряд.
wf-minigame-dice-player-bid = Вы поставили {$count} x {$face}.
wf-minigame-dice-npc-bid = Они поставили {$count} x {$face}.
wf-minigame-dice-player-called-wrong = Вы обвинили. Было {$count} x {$face}. Ставка осталась в силе.
wf-minigame-dice-npc-called-right = Они обвинили. Было только {$count} x {$face}.
wf-minigame-dice-player-called-right = Вы обвинили. Было только {$count} x {$face}.
wf-minigame-dice-npc-called-wrong = Они обвинили. Было {$count} x {$face}. Ваша ставка осталась в силе.
wf-minigame-dice-round-won = Раунд за вами: было {$count} x {$face}. {$won} из {$rounds}. Новые кости.

## Сейф

wf-minigame-lock-rules = Угадайте код. Попадание: правильная цифра, правильное место. Близко: правильная цифра, неправильное место.
wf-minigame-lock-placeholder = Код
wf-minigame-lock-try = Попробовать
wf-minigame-lock-tries = {$left ->
    [one] 1 попытка осталась
    *[other] {$left} попыток осталось
}
wf-minigame-lock-code = Код был {$code}.
wf-minigame-lock-guess = {$code}: {$hits} попаданий, {$near} близко
wf-minigame-lock-start = Код из {$digits} цифр. У вас есть {$tries} попыток.
wf-minigame-lock-feedback = {$hits} попаданий, {$near} близко. Осталось {$left}.
wf-minigame-lock-open = Он щелкает открытым на попытке {$tries}.
wf-minigame-lock-jammed = Заклинило. Код был {$code}.

## Выше или Ниже

wf-minigame-cards-rules = Назовите следующую карту выше или ниже. Ничья проигрывает. Получаете выигрыш после любого правильного хода.
wf-minigame-cards-higher = Выше
wf-minigame-cards-lower = Ниже
wf-minigame-cards-cash-out = Снять выигрыш
wf-minigame-cards-card = {$rank} из {$suit}
wf-minigame-cards-run = Серия {$streak} из {$max}. Следующий правильный ход приносит {$next}x.
wf-minigame-cards-run-top = Серия {$streak}. Вершина лестницы.
wf-minigame-cards-start = Поднимайтесь по лестнице до {$top}x. Новая колода после каждой карты.
wf-minigame-cards-right = Правильно. Серия из {$streak}, стоит {$multiple}x.
wf-minigame-cards-wrong = Неправильный ход.
wf-minigame-cards-tie = Та же масть. Ничья проигрывает.
wf-minigame-cards-cashed = Вы сняли выигрыш на {$multiple}x.
wf-minigame-cards-top = Вершина лестницы: {$multiple}x.
wf-minigame-cards-rank-1 = Два
wf-minigame-cards-rank-2 = Три
wf-minigame-cards-rank-3 = Четыре
wf-minigame-cards-rank-4 = Пять
wf-minigame-cards-rank-5 = Шесть
wf-minigame-cards-rank-6 = Семь
wf-minigame-cards-rank-7 = Восемь
wf-minigame-cards-rank-8 = Девять
wf-minigame-cards-rank-9 = Десять
wf-minigame-cards-rank-10 = Джек
wf-minigame-cards-rank-11 = Дама
wf-minigame-cards-rank-12 = Король
wf-minigame-cards-rank-13 = Туз
wf-minigame-cards-suit-0 = Червы
wf-minigame-cards-suit-1 = Бубны
wf-minigame-cards-suit-2 = Крести
wf-minigame-cards-suit-3 = Пики

## Память

wf-minigame-memory-rules = Следите, как загораются панели, затем нажмите их в том же порядке.
wf-minigame-memory-progress = {$entered} из {$length} нажато. Достигните {$target}, чтобы выиграть.
wf-minigame-memory-start = Достигните последовательности из {$length}.
wf-minigame-memory-next = Правильно. Теперь {$length} из {$target}.
wf-minigame-memory-wrong = Неправильная панель. Вы достигли {$reached}.
wf-minigame-memory-won = Все правильно.

## Загадки

wf-minigame-riddle-number = Загадка {$number} из {$total}
wf-minigame-riddle-start = Ответьте правильно на {$count} загадок подряд.
wf-minigame-riddle-right = Правильно. Осталось {$left}.
wf-minigame-riddle-wrong = Неправильно.
wf-minigame-riddle-won = Все правильно.

## Хосты

wf-minigame-prompt-rules = Как это работает?

wf-minigame-dealer-greeting = Присаживайтесь. Карты, кости или сейф?
wf-minigame-dealer-farewell = Вернитесь, когда ваша удача вновь улыбнётся вам.
wf-minigame-dealer-prompt-cards = Давайте поиграем в Выше или Ниже.
wf-minigame-dealer-response-cards = Новая колода после каждой карты.
wf-minigame-dealer-prompt-dice = Кости лжеца.
wf-minigame-dealer-response-dice = Перемешайте кубик.
wf-minigame-dealer-prompt-lock = Я взломаю ваш сейф.
wf-minigame-dealer-response-lock = Четыре цифры. Не так много попыток.
wf-minigame-dealer-response-rules = Деньги на стол или покажите мне свой ID. Выиграйте, и я заплачу в несколько раз больше. Выиграйте один раз, и вам придется подождать перед следующей игрой.

wf-minigame-sage-greeting = Ах, посетитель. Вам нравятся головоломки?
wf-minigame-sage-farewell = Подумайте об этом.
wf-minigame-sage-prompt-riddles = Задавайте свои загадки.
wf-minigame-sage-response-riddles = Три подряд, без угадывания.
wf-minigame-sage-prompt-memory = Игра на память.
wf-minigame-sage-response-memory = Следите внимательно.
wf-minigame-sage-prompt-ore = Рудные отвалы, высокие ставки.
wf-minigame-sage-response-ore = Никто не побеждает меня в этой игре. Вы можете попробовать.
wf-minigame-sage-response-rules = Деньги на стол или ваш ID. Выиграйте, и я хорошо заплачу. Проиграете — останетесь без ничего.

wf-minigame-gambler-greeting = Эй, приятель. У тебя есть несколько кредитов? Повысим интерес.
wf-minigame-gambler-farewell = Увидимся по пути.
wf-minigame-gambler-prompt-ore = Рудные отвалы.
wf-minigame-gambler-response-ore = Последний проигрывает. Легко, правда?
wf-minigame-gambler-prompt-dice = Кости лжеца.
wf-minigame-gambler-response-dice = Не обманывайте меня сейчас.
wf-minigame-gambler-prompt-cards = Выше или Ниже.
wf-minigame-gambler-response-cards = Вот ваша первая карта.
wf-minigame-gambler-response-rules = Деньги на стол или ваш ID. Я заплачу три к одному. Не выигрывайте слишком часто.

wf-minigame-miner-greeting = Хм. Редко бывают посетители в этом секторе. Вы играете?
wf-minigame-miner-farewell = Берегите потолок.
wf-minigame-miner-prompt-ore-easy = Дружеская игра в рудные отвалы.
wf-minigame-miner-response-ore-easy = Я буду мягок. Золото и серебро, если вы победите.
wf-minigame-miner-prompt-dice = Кости лжеца за алмазы.
wf-minigame-miner-response-dice = Алмазы и уран в кульке.
wf-minigame-miner-prompt-ore-hard = Рудные отвалы. Ваша лучшая игра.
wf-minigame-miner-response-ore-hard = Победите меня, и алмазный бур будет вашим. Вы этого не сделаете.
wf-minigame-miner-response-rules = Ставка наличными или из сейфа с вашего счета. Я не платчу деньгами. Я платчу рудой и лучшим буром, который вы когда-либо держали.
