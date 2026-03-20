<p align="center"> <img alt="Frontier Station 14" width="880" height="300" src="https://raw.githubusercontent.com/Monolith-Station/Monolith/89d435f0d2c54c4b0e6c3b1bf4493c9c908a6ac7/Resources/Textures/_Mono/Logo/logo.png?raw=true" /></p>

**Corvax Forge Monolith** - это русскоязычное ответвление **Monolith**, форка [Monolith](https://github.com/Monolith-Station), которое работает на движке [Robust Toolbox](https://github.com/space-wizards/RobustToolbox), написанном на C#.

В этой сборке представлены собственные наработки, адаптации и контент, созданный русскоязычным комьюнити.
Если вы хотите разместить сервер или разрабатывать контент для **Corvax Forge Monolith**, используйте этот репозиторий. Он включает **RobustToolbox** и контент-пак для создания новых дополнений.

## Ссылки

<div class="header" align="center">

[Discord](https://discord.gg/7wDwSPde58) | [Steam](https://store.steampowered.com/app/1255460/Space_Station_14/) | [Boosty](https://boosty.to/corvaxforge) | [Вики](https://station14.ru/wiki/%D0%9F%D0%BE%D1%80%D1%82%D0%B0%D0%BB:Frontier)


</div>

## Сборка

Обратитесь к [руководству Space Wizards](https://docs.spacestation14.com/en/general-development/setup/setting-up-a-development-environment.html) по настройке среды разработки для получения общей информации, но имейте в виду, что Corvax Forge Monolith — это не то же самое, и многие вещи могут не применяться.
Мы предоставляем несколько скриптов, показанных ниже, чтобы упростить работу.

### Необходимые программы

- Git
- .NET SDK 9.0.101

### Windows

```
1. Клонируйте этот репозиторий
2. Запустите `Scripts/bat/updateEngine.bat` в терминале или в проводнике, чтобы загрузить движок
3. Запустите `Scripts/bat/buildAllDebug.bat` после внесения любых изменений в исходный код
4. Запустите `Scripts/bat/runQuickAll.bat`, чтобы запустить клиент и сервер
5. Подключитесь к localhost в клиенте и играйте
```

### Linux
```
1. Клонируйте этот репозиторий
2. Запустите `Scripts/sh/updateEngine.sh` в терминале, чтобы загрузить движок
3. Запустите `Scripts/sh/buildAllDebug.sh` после внесения любых изменений в исходный код
4. Запустите `Scripts/sh/runQuickAll.sh`, чтобы запустить клиент и сервер
5. Подключитесь к localhost в клиенте и играйте
```
## Лицензия

Смотрите заголовки REUSE для подробной информации о лицензировании каждого файла и о том, под какими именно лицензиями были сделаны вклады. Работа в целом лицензируется под GNU Affero General Public License версии 3.0.

По умолчанию оригинальный код, добавленный в кодовую базу Monolith после коммита 04d8ce483f638320d1b85a7aaacdf01442757363, распространяется под лицензией Mozilla Public License версии 2.0 с удалённым Exhibit B. См. LICENSE-MPL.txt.

Контент, добавленный в этот репозиторий после коммита 2fca06eaba205ae6fe3aceb8ae2a0594f0effee0, лицензируется под GNU Affero General Public License версии 3.0, если не указано иное. См. LICENSE-AGPLv3.txt.

Контент, добавленный в этот репозиторий до коммита 2fca06eaba205ae6fe3aceb8ae2a0594f0effee0, лицензируется под лицензией MIT, если не указано иное. См. LICENSE-MIT.txt.

Коммит 2fca06eaba205ae6fe3aceb8ae2a0594f0effee0 был отправлен 1 июля 2024 года в 16:04 UTC.

Большинство ассетов лицензированы под CC-BY-SA 3.0, если не указано иное. Ассеты имеют информацию о лицензии и авторских правах в файле метаданных. Пример.

Обратите внимание, что некоторые ассеты лицензированы под некоммерческой лицензией CC-BY-NC-SA 3.0 или похожими некоммерческими лицензиями, и их необходимо удалить, если вы хотите использовать этот проект в коммерческих целях.

Обратите внимание, что некоторые ассеты лицензированы под All rights reserved т.е. всё права на их использование, копирование, изменение остаются за Corvax Forge данные ассеты необходимо удалить, если вы хотите использовать данный проект.

