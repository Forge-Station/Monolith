### Интерфейс

rcd-component-examine-mode-details = { $mode ->
    [Unknown] Режим не выбран.
   *[other] Выбран режим: «{ $mode }».
    }
rcd-component-examine-build-details = Выбран режим строительства: { $name }.

### Сообщения при взаимодействии

# Смена режима
rcd-component-change-mode = РСУ переключён в режим '{ $mode }'.
rcd-component-change-build-mode = РСУ переключён в режим строительства. Строится { $name }.
# Количество зарядов
rcd-component-no-ammo-message = В РСУ закончились заряды!
rcd-component-insufficient-ammo-message = В РСУ не хватает зарядов!
# Демонтаж
rcd-component-tile-indestructible-message = Эта клетка не может быть демонтирова!
rcd-component-deconstruct-target-not-on-whitelist-message = Вы не можете демонтировать это!
rcd-component-nothing-to-deconstruct-message = Здесь нечего демонтировать!
rcd-component-tile-obstructed-message = Вы не можете демонтировать клетку, на которой что-то находится!
# Строительство
rcd-component-no-valid-grid = Вы слишком далеко в космосе, чтобы строить здесь!
rcd-component-must-build-on-empty-tile-message = Здесь уже имеется фундамент!
rcd-component-cannot-build-on-empty-tile-message = Это не может быть построено без фундамента.
rcd-component-must-build-on-subfloor-message = Это может быть построено только на покрытии!
rcd-component-cannot-build-on-subfloor-message = Это не может быть построено на покрытии!
rcd-component-cannot-build-on-occupied-tile-message = Здесь нельзя строить, место уже занято!
rcd-component-cannot-build-identical-tile = Эта клетка уже тут имеется!

### Названия категорий

rcd-component-walls-and-flooring = Стены и полы
rcd-component-platingtiles = покрытие
rcd-component-latticetiles = Напольные решётки
rcd-component-windows-and-grilles = Окна и решётки
rcd-component-airlocks = Шлюзы
rcd-component-electrical = Электроприборы
rcd-component-lighting = Освещение
# Forge-Change: категории РФУ (Goob-Station/Goob-Station#1203)
rcd-component-piping = Трубопровод
rcd-component-pumpsvalves = Насосы и клапаны
rcd-component-vents = Вентиляция
rcd-component-atmosphericutility = Атмос-оборудование

### Названия прототипов (примечание: названия создаваемых предметов будут во множественном числе)

rcd-component-deconstruct = демонтаж
rcd-component-floor-steel = стальной пол
rcd-component-plating = { tiles-plating }
rcd-component-lattice = { tiles-lattice }
rcd-component-PlatingCornerNE = { mono-tile-ne-plating }
rcd-component-PlatingCornerNW = { mono-tile-nw-plating }
rcd-component-PlatingCornerSE = { mono-tile-se-plating }
rcd-component-PlatingCornerSW = { mono-tile-sw-plating }
rcd-component-PlatingHalfV = { mono-tile-v-half-plating }
rcd-component-PlatingHalfH = { mono-tile-h-half-plating }
rcd-component-PlatingWedgeN = { mono-tile-north-wedge-plating }
rcd-component-PlatingWedgeE = { mono-tile-east-wedge-plating }
rcd-component-PlatingWedgeS = { mono-tile-south-wedge-plating }
rcd-component-PlatingWedgeW = { mono-tile-west-wedge-plating }
rcd-component-PlatingHalfTiltNESLower = { mono-tile-ne-s-lower-half-tilt-plating }
rcd-component-PlatingHalfTiltNESUpper = { mono-tile-ne-s-upper-half-tilt-plating }
rcd-component-PlatingHalfTiltSEWLower = { mono-tile-se-w-lower-half-tilt-plating }
rcd-component-PlatingHalfTiltSEWUpper = { mono-tile-se-w-upper-half-tilt-plating }
rcd-component-PlatingHalfTiltSWNLower = { mono-tile-sw-n-lower-half-tilt-plating }
rcd-component-PlatingHalfTiltSWNUpper = { mono-tile-sw-n-upper-half-tilt-plating }
rcd-component-PlatingHalfTiltNWELower = { mono-tile-nw-e-lower-half-tilt-plating }
rcd-component-PlatingHalfTiltNWEUpper = { mono-tile-nw-e-upper-half-tilt-plating }
rcd-component-PlatingHalfTiltNEWLower = { mono-tile-ne-w-lower-half-tilt-plating }
rcd-component-PlatingHalfTiltNEWUpper = { mono-tile-ne-w-upper-half-tilt-plating }
rcd-component-PlatingHalfTiltSENLower = { mono-tile-se-n-lower-half-tilt-plating }
rcd-component-PlatingHalfTiltSENUpper = { mono-tile-se-n-upper-half-tilt-plating }
rcd-component-PlatingHalfTiltSWELower = { mono-tile-sw-e-lower-half-tilt-plating }
rcd-component-PlatingHalfTiltSWEUpper = { mono-tile-sw-e-upper-half-tilt-plating }
rcd-component-PlatingHalfTiltNWSLower = { mono-tile-nw-s-lower-half-tilt-plating }
rcd-component-PlatingHalfTiltNWSUpper = { mono-tile-nw-s-upper-half-tilt-plating }
rcd-component-LatticeCornerNE = { mono-tile-ne-lattice }
rcd-component-LatticeCornerNW = { mono-tile-nw-lattice }
rcd-component-LatticeCornerSE = { mono-tile-se-lattice }
rcd-component-LatticeCornerSW = { mono-tile-sw-lattice }
rcd-component-LatticeHalfV = { mono-tile-v-half-lattice }
rcd-component-LatticeHalfH = { mono-tile-h-half-lattice }
rcd-component-LatticeWedgeN = { mono-tile-north-wedge-lattice }
rcd-component-LatticeWedgeE = { mono-tile-east-wedge-lattice }
rcd-component-LatticeWedgeS = { mono-tile-south-wedge-lattice }
rcd-component-LatticeWedgeW = { mono-tile-west-wedge-lattice }
rcd-component-LatticeHalfTiltNESLower = { mono-tile-ne-s-lower-half-tilt-lattice }
rcd-component-LatticeHalfTiltNESUpper = { mono-tile-ne-s-upper-half-tilt-lattice }
rcd-component-LatticeHalfTiltSEWLower = { mono-tile-se-w-lower-half-tilt-lattice }
rcd-component-LatticeHalfTiltSEWUpper = { mono-tile-se-w-upper-half-tilt-lattice }
rcd-component-LatticeHalfTiltSWNLower = { mono-tile-sw-n-lower-half-tilt-lattice }
rcd-component-LatticeHalfTiltSWNUpper = { mono-tile-sw-n-upper-half-tilt-lattice }
rcd-component-LatticeHalfTiltNWELower = { mono-tile-nw-e-lower-half-tilt-lattice }
rcd-component-LatticeHalfTiltNWEUpper = { mono-tile-nw-e-upper-half-tilt-lattice }
rcd-component-LatticeHalfTiltNEWLower = { mono-tile-ne-w-lower-half-tilt-lattice }
rcd-component-LatticeHalfTiltNEWUpper = { mono-tile-ne-w-upper-half-tilt-lattice }
rcd-component-LatticeHalfTiltSENLower = { mono-tile-se-n-lower-half-tilt-lattice }
rcd-component-LatticeHalfTiltSENUpper = { mono-tile-se-n-upper-half-tilt-lattice }
rcd-component-LatticeHalfTiltSWELower = { mono-tile-sw-e-lower-half-tilt-lattice }
rcd-component-LatticeHalfTiltSWEUpper = { mono-tile-sw-e-upper-half-tilt-lattice }
rcd-component-LatticeHalfTiltNWSLower = { mono-tile-nw-s-lower-half-tilt-lattice }
rcd-component-LatticeHalfTiltNWSUpper = { mono-tile-nw-s-upper-half-tilt-lattice }
