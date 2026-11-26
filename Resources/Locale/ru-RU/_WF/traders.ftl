# NPC traders (Docs/Wolfgate/Traders/plan.md)

## Naming
ent-WFTraderWolfgate = Джон Фордж
    .desc = Название на вывеске: "Если Фордж это производит - он это продаст."
    .suffix = Продавец, Фордж
ent-WFTraderAstro = Отто Кэсслер
    .desc = Оборудует экипажи для работы в вакууме; продает скафандры для выхода в открытый космос, защитные костюмы и пилотское снаряжение.
    .suffix = Продавец, Космос
ent-WFTraderClothes = Вильгельмина Фарроу
    .desc = Заведует прилавком с одеждой; прочная униформа и рабочая обувь.
    .suffix = Продавец, Одежда
ent-WFTraderFlatpack = Маркус Дельгадо
    .desc = Оптовый поставщик оборудования в разобранном виде; поставляет комплектующие для сборочных цехов и станционного оборудования в разобранном виде.
    .suffix = Продавец, Упаковки
ent-WFTraderFuelTechnician = Дара Восс
    .desc = Завхоз топливного причала, загорелый и невозмутимый.
    .suffix = Продавец, Топливо
ent-WFTraderSalvage = Ривва Окафор
    .desc = Обслуживает склад аварийно-спасательного оборудования: буровые установки, дробилки и горнодобывающее оборудование для месторождений.
    .suffix = Продавец, Сальваж
ent-WFTraderShipyard = Агент Судоверфи
    .desc = Торговый представитель судостроительной верфи в отглаженном воротничке и с натянутой улыбкой.
    .suffix = Продавец, Корабельные корпуса
ent-WFTraderTools = Пип Йун
    .desc = Продавец хозтоваров: инструменты, кабели и комплектующие для машиностроения.
    .suffix = Продавец, Инструментов
ent-WFTraderUsedShips = Бенни Соколов
    .desc = Голос продавца подержанных кораблей, слышен за две палубы от вас.
    .suffix = Покупатель, Продавец, Б/у корабли
ent-WFTraderWanderer = Странствующий торговец
    .desc = Продает инструменты, воздух и запасное снаряжение из трюма по низким ценам, пока они не закончатся.
    .suffix = Продавец, Странствующий
ent-WFTraderSurplus = Диллер излишков
    .desc = Продаёт армейское снаряжение, любому, кому это нужно.
    .suffix = Продавец, Военные излишки
ent-WFTraderCurios = Торговец диковинками
    .desc = Антиквариат, семейные реликвии и необычные предметы, каждый со своей историей.
    .suffix = Продавец, Антиквариат
ent-WFTraderMedic = Поставщик медицинских товаров
    .desc = Излишки товаров из клиник, продажа из резерва.
    .suffix = Продавец, Медицина
ent-WFTraderShipwright = Корабельный плотник
    .desc = Подруливающие устройства, приводы и корпусное оборудование, доставляемые туда, где бы ни находились корабли.
    .suffix = Продавец, Корабельный плотник
ent-WFTraderFence = Пиратский скупщик краденого
    .desc = Перевозит товары, о которых никто не спрашивает, по ценам, с которыми никто не спорит.
    .suffix = Продавец, Краденое
ent-WFTraderArmourer = Оружейник
    .desc = Корабельные орудия, которые никогда не попадают в каталоги верфей, точки крепления для их установки и снаряды для их подачи.
    .suffix = Продавец, Оружейник
ent-WFTraderSystemsBroker = Системный брокер
    .desc = Защитные генераторы, приводы и комплектующие от поставщиков, не осуществляющих прямые продажи.
    .suffix = Продавец, Системный брокер

## Conversation
trader-busy = Один момент, пожалуйста.
trader-put-on-table = Пожалуйста, положите это на стол.
trader-not-your-id = Это не ваш ID.
trader-short = Вам недостаёт { $amount }.
trader-request-item = Пожалуйста, дайте мне ваш { $thing }.
trader-thing-payment = ID или наличные
trader-thing-id = ID
trader-thing-id-voucher = ID или ваучер корабля
trader-thing-deed-id = ID корабля
trader-cannot-help = Я не могу помочь вам с этим.
trader-wont-trade = Торговец не хочет иметь с вами ничего общего.
trader-verb-talk = Поговорить
trader-confirm-yes = Да, давайте.
trader-confirm-no = Нет, забудьте.

## Windows
trader-dialogue-window-title = Разговор
trader-shop-window-title = На продаже
trader-shop-zone-cash = Наличные в зоне: { $amount }
trader-shop-zone-id = ID в зоне: { $name }
trader-shop-zone-none = Ничего в зоне.
trader-shop-balance = Баланс банка: { $amount }
trader-shop-basket-title = Ваш заказ
trader-shop-basket-empty = Ничего ещё не выбрано.
trader-shop-total = Итого: { $amount }
trader-shop-purchase = Купить
trader-shop-clear = Очистить
trader-shop-search = Поиск...
trader-shop-search-empty = Ничто не совпадает.
trader-shop-stock-left = { $name } ({ $count } осталось)

trader-wanderer-greeting = Здравствуйте, космонавт! Всё должно уйти, и по таким ценам это обязательно произойдёт.
trader-wanderer-farewell = Летай безопасно. Я уйду ещё до того, как ты передумаешь.
trader-wanderer-prompt-shop = Что у тебя есть?
trader-wanderer-response-shop = Всего по чуть-чуть. Посмотри сам.
trader-wanderer-prompt-who = Откуда ты всё это берёшь?
trader-wanderer-response-who = Здесь и там. Имущество, спасение, игра в карты. Все достаточно честно.
trader-shop-empty = В наличии ничего нет сейчас.
trader-shop-thanks = С вами приятно иметь дела.

## Parcels
trader-parcel-open-verb = Открыть
trader-parcel-examine = Внутри { $count ->
    [one] один предмет
   *[other] { $count } предметов
}.

## Shop receipt
trader-shop-receipt-name = Кассовый чек
trader-shop-receipt-header = [head=2]{ $trader }[/head]
    Продано: { $customer }
    { $time }
trader-shop-receipt-line = { $count }x { $item } - { $price }
trader-shop-receipt-total =
    Итого: { $total }
    Оплачено: { $paid }
    Сдача: { $change }

## Refuelling
trader-refuel-not-docked = Ваш корабль не пришвартован здесь.
trader-refuel-no-ship = Я не вижу вашего корабля.
trader-refuel-full = Ваши баки уже полны.
trader-refuel-quote = { $count } генераторов, { $cost }.
trader-refuel-done = Все готово.
trader-refuel-receipt-name = Чек по топливу
trader-refuel-receipt-header = [head=2]{ $trader }[/head]
    Обслужено для: { $customer }
    Корабль: { $ship }
    { $time }
trader-refuel-receipt-line = { $generator }: { $amount } единиц { $fuel } - { $price }
trader-refuel-receipt-total =
    Итого: { $total }
    Оплачено: { $paid }
    Сдача: { $change }

## Shipyard dealer
trader-shipyard-no-selling = Я только продаю. Возьмите её к торговцу б/у кораблями.
trader-shipyard-prompt-unassign = Я хочу отказаться от документов на свой корабль.
trader-shipyard-response-unassign = Покажи мне карту.
trader-shipyard-unassign-confirm = Снять { $ship } с вашей карты? Он останется там, где находится, но не будет вашим по бумагам и его шлюзы вас не пустят.
trader-shipyard-unassigned = Готово. { $ship } снята с вашей карты, вы вольны купить другой.
trader-shipyard-prompt-rename = Я хочу переименовать свой корабль.
trader-shipyard-response-rename = Новые печати на документах? Покажите мне карту.
trader-shipyard-rename-ask = И как теперь называть { $ship }?
trader-shipyard-rename-placeholder = Новое имя корабля
trader-shipyard-rename-cancelled = Значит, оставляем старое имя.
trader-shipyard-renamed = Готово. Теперь она - { $ship }.
trader-shipyard-papers-refused = Я не могу изменить эти документы прямо сейчас.
trader-shipyard-papers-refused-reason = Я не могу изменить эти документы прямо сейчас. { $reason }
trader-text-submit = Подтвердить
trader-shipyard-sold = Он теперь ваш. Летите осторожно.
trader-shipyard-cash-only = Я не могу использовать ваш счёт, поэтому вся сумма должна быть наличными на прилавке. Вы получите сдачу обратно.
trader-shipyard-counter-cash-label = На прилавке:{" "}
trader-shipyard-unknown-design = Неизвестный дизайн
trader-shipyard-receipt-name = Чек по покупке корабля
trader-shipyard-receipt-header = [head=2]{ $trader }[/head]
    Продано: { $customer }
    { $time }
trader-shipyard-receipt-line = { $ship } ({ $design }) - { $price }

## Used ships
trader-used-window-title = Лот
trader-used-empty = Ничего на лотах сейчас нет. Вернитесь, когда кто-то выставит.
trader-used-buy = Купить
trader-used-row-detail = { $design } - один владелец: { $seller }
trader-used-no-ship = Я не вижу корабля по этому ID.
trader-used-quote = Я могу дать вам { $amount } за { $ship }.
trader-used-sale-refused = Не могу принять его так, боюсь.
trader-used-sale-refused-reason = Не могу принять его так, боюсь. { $reason }
trader-used-sale-done = { $amount }, прямо на ваш счёт. Пожалуйста.
trader-used-sale-done-cash = { $amount }, наличными на прилавке. Пожалуйста.
trader-used-quote-voucher = { $ship } пришёл по ваучеру, поэтому я возьму его обратно, но бесплатно.
trader-used-sale-done-voucher = Он снят с вашего ваучера. Пожалуйста.
trader-used-gone = Кто-то был быстрее вас.
trader-used-load-failed = Он не влезает в доки. Ваша сдача снова на столе.
trader-used-sold = { $ship }, продана! Без возврата или отмены.
trader-used-no-session = Мне нужен настоящий покупатель для оформления документов.
trader-used-deed-failed = Документы не проходят. Я вернул деньги на ваш счёт; он остаётся на лоте.
trader-used-deed-failed-cash = Документы не проходят. Ваша сдача снова на прилавке; он остаётся на лоте.
trader-used-new-stock = Новое на лоте: { $ship }, { $design } - { $price }.
trader-used-sale-receipt-name = Чек по обмену корабля
trader-used-sale-receipt-header = [head=2]{ $trader }[/head]
    Куплено у: { $seller }
    { $time }
trader-used-sale-receipt-line = { $ship } - { $amount }
trader-used-buy-receipt-name = Чек по покупке б/у корабля
trader-used-buy-receipt-header = [head=2]{ $trader }[/head]
    Продано: { $customer }
    { $time }
trader-used-buy-receipt-line = { $ship } ({ $design }), раньше у { $seller } - { $price }

## Corwin Ashgrove - shipyard dealer (shipyard_dealer.yml)
trader-shipyard-greeting = Хотите поставить своё имя на корпус?
trader-shipyard-farewell = Счастливого пути туда, куда вы летите.
trader-shipyard-prompt-civilian = Какие корабли у вас на продаже?
trader-shipyard-response-civilian = Полный список гражданских кораблей. Можете ознакомиться.
trader-shipyard-prompt-expedition = Есть ли что-нибудь, предназначенное для экспедиций?
trader-shipyard-response-expedition = Длинные корпуса, вот они.
trader-shipyard-prompt-sr = Я слышал о колониальных кораблях - есть в наличии?
trader-shipyard-response-sr = Список сотрудников. Кредиты говорят сами за себя.
trader-shipyard-prompt-who = Что вы делаете?
trader-shipyard-response-who = Агент Судоверфи, прямые продажи с завода. Каждый корпус в моих списках приходит с линии без изменений, передаётся вам сразу. Я не покупаю обратно - если вы закончили с одним, Бенни в конце коридора занимается обменами.

## Benny Sokolov - used ship salesman (used_ship_salesman.yml)
trader-used-greeting = Подходите - посмотреть лоты или подписать документы на продажу?
trader-used-farewell = Вы знаете, где меня найти.
trader-used-prompt-lot = Что у вас на лоте?
trader-used-response-lot = Посмотрите. Каждый из них прилетел сюда самостоятельно.
trader-used-prompt-sell = Я хочу продать свой корабль.
trader-used-response-sell = Давайте посмотрим, сколько он стоит.
trader-used-prompt-who = Что вы делаете?
trader-used-response-who = Бенни Соколов, обмены и перепродажи. Пришвартуйте судно сюда, и я куплю его у вас по рыночной цене, деньги сразу на ваш счёт. Через несколько минут он снова появится на лоте, точно такой же, как вы оставили, с небольшой надбавкой сверху. Все продается как есть, и каждая продажа окончательна.

## Dara Voss - fuel dock trader (fuel_technician.yml)
trader-fuel-tech-greeting = Что могу вам предложить?
trader-fuel-tech-farewell = Берегите ноги на выходе!
trader-fuel-tech-prompt-shop = Что у вас на продаже?
trader-fuel-tech-response-shop = Вот что у меня есть на продаже!
trader-fuel-tech-prompt-refuel = Можно ли заправить мой корабль?
trader-fuel-tech-response-refuel = Давайте посмотрю на него.
trader-fuel-tech-prompt-who = Кто вы?
trader-fuel-tech-response-who = Я Дара Вос - я работаю в топливном доке этой станции. Продаю плазму, уран и бананий, банки AME и топливо для сварки. Дайте мне ID корабля, и я заправлю каждый генератор на борту по рыночной цене.
trader-surplus-greeting = Не торопитесь, и мы поладим. Хотите вооружиться?
trader-surplus-farewell = Не направляйте это на меня на выходе.
trader-surplus-response-shop = Всё это работает, большая часть функционирует. Посмотрите.
trader-surplus-response-who = Квартирмейстеры уходят в отставку. Склады закрываются. Кто-то должен взять это на себя.
trader-curios-greeting = Ах, покупатель, ценящий изысканные вещи. Заходите.
trader-curios-farewell = Берегите его. Или продайте; я не буду спрашивать.
trader-curios-response-shop = Каждый предмет имеет историю. Я даже расскажу вам настоящую, за плату.
trader-curios-response-who = В основном имущество. Люди умирают здесь и оставляют после себя самые красивые вещи.
trader-medic-greeting = Вы выглядите так, будто после драки. Что вам нужно?
trader-medic-farewell = Постарайтесь вернуться целым.
trader-medic-response-shop = Быстрее и дешевле, чем умирать. Посмотрите сами.
trader-medic-response-who = Я покупаю излишки от клиник, которые заказывают слишком много. Вы удивитесь.
trader-shipwright-greeting = Осторожно, кабели! Ищете что-нибудь для тягача?
trader-shipwright-farewell = Прочно закрепите. Вакуум не даёт второй попытки.
trader-shipwright-response-shop = Двигатели, подруливающие устройства, гироскопы и платы для их управления. Все это было доставлено с настоящей судоремонтной верфи.
trader-shipwright-response-who = Я зарабатываю на жизнь оснащением корпусов судов. Здесь же верфь приезжает к клиенту.
trader-fence-greeting = Легко, пожалуйста. Никто здесь не спрашивает, откуда что-либо пришло, и я был бы благодарен, если бы вы сделали то же самое.
trader-fence-farewell = Вы никогда меня не видели, и я вас никогда не видел. По рукам?
trader-fence-response-shop = Вещи, упавшие с других кораблей. Посмотрите сами, и говорите потише.
trader-fence-response-who = Я занимаюсь перевозкой товаров. Куда они делись вчера – это никого не касается, и меня уж точно.
trader-armourer-greeting = Руки прочь от ящиков, пока не заплатите. Что вы хотите установить?
trader-armourer-farewell = Прикрепите его к чему-нибудь, что выдержит отдачу. Возврат денег за пробоины в корпусе я не делаю.
trader-armourer-response-shop = Оружие, которое так и не попало в каталог, точки крепления для него и патроны к нему. По одному экземпляру каждого вида оружия, так что не медлите.
trader-armourer-response-who = Верфи строят слишком много кораблей, военно-морские силы выводят из эксплуатации, и кому-то приходится все это убирать. Это я.
trader-systems-greeting = Добро пожаловать на борт. Защитные экраны, приводы, фитинги — всё то, что, как вам говорят на верфи, не продаётся.
trader-systems-farewell = Проложите кабели правильно. Мне бы очень не хотелось услышать, что вы потеряли его при первом же прыжке.
trader-systems-response-shop = Защитные генераторы, приводы, двигатели и платы для всего этого. По одному экземпляру каждого на этот раз.
trader-systems-response-who = Я знаю людей на свалках, которые не продают товары населению. Они знают, что я плачу заранее.

trader-repair-no-vehicle = Я не вижу вашего транспортного средства. Припаркуйте его спереди.
trader-repair-fine = Всё впорядке с { THE($vehicle) }, нечего чинить.
trader-repair-quote = { CAPITALIZE(THE($vehicle)) }, полный ремонт: { $cost }.
trader-repair-done = { CAPITALIZE(THE($vehicle)) } готов к отправке.
trader-devour-start = { CAPITALIZE(THE($trader)) } приседает над { THE($target) } и начинает есть.
