# Спрайт ядра KIAS

Ресурс: `Resources/Textures/_Forge/KIAS/KiasCore.rsi/core.png`.
Размер кадра: **32×64**, один статичный вид, настоящий прозрачный фон.
Создан встроенным инструментом OpenAI imagegen для Forge, 2026-10-08.
Лицензия RSI: CC-BY-SA-3.0. Авторство Hurtsay относится к прежним ресурсам контроллера и шкафа и не приписывается этому изображению.

Сгенерированный исходник экспортирован в размер игрового кадра методом nearest-neighbor, без сглаживания. Ядро сохраняет этот отдельный ресурс; остальные устройства используют новый набор из [SPRITE_PACK.md](SPRITE_PACK.md). Вертикальное смещение на половину тайла совмещает нижнюю половину изображения с точкой установки машины. Визуализация статуса привязана к основному слою изображения.

Слой ядра — `Mobs`, как у высоких статуй с таким же смещением. Ранее использовался слой по умолчанию `Objects`: персонажи и направленные окна (`Mobs`) всегда перекрывали ядро независимо от взаимного положения. Общий слой позволяет штатной сортировке Robust по нижней границе спрайта выбирать передний объект. Обычные стены/полнотайловые окна остаются ниже; двери сохраняют свой более высокий слой. Размер 32×64, смещение и физический корпус не изменены. Регрессия `TallCoreSharesDepthWithDirectionalWindowAndHuman` проверяет полученные по сети клиентские спрайты; графические кадры до/после не сняты.

## Промпт генерации

```text
Create a production game sprite for Space Station 14 / Monolith Station: KIAS automation core, a tall standalone floor-mounted machine, single south-facing view, transparent background. Pixel art asset, logical sprite exactly 32 pixels wide by 64 pixels high, displayed enlarged at integer scale ideally 1024 by 2048. Every visible pixel is a crisp flat-color square on this 32x64 logical grid. Single isolated object fills the logical frame with 1-2px transparent margins. SS13/SS14 classic top-down 3/4 front view, small visible top panel, tall vertical front. Industrial dark charcoal and muted violet steel enclosure matching KIAS controller rack, restrained bright cyan inset core chamber in upper middle, 3 subtle cyan horizontal status bars, lower dark cooling grille, thick bevelled corners, sturdy narrow base. Readable silhouette at native 32x64 size. Opaque dark outline and limited palette roughly 16 colors. No lettering, no logo, no watermark, no ground, no drop shadow outside the object, no scene, no characters, no gradients, no antialiasing, no photorealism. One sprite only, no sprite sheet. This is the final visible machine art, not a concept presentation.
```
