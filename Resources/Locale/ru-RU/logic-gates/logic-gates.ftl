logic-gate-examine = Сейчас установлена логическая операция «{ logic-gate-operation-name }».
logic-gate-cycle = Переключено на операцию «{ logic-gate-operation-name }».
power-sensor-examine =
    Сейчас отслеживается заряд { $output ->
        [true] выходных
       *[false] входных
    } аккумуляторов электросети.
power-sensor-voltage-examine = Отслеживается электросеть: { $voltage }.
power-sensor-switch =
    Включено отслеживание заряда { $output ->
        [true] выходных
       *[false] входных
    } аккумуляторов электросети.
power-sensor-voltage-switch = Выбрана электросеть: { $voltage }.

logic-gate-operation-name = { $gate ->
    [OR] ИЛИ
    [AND] И
    [XOR] Исключающее ИЛИ
    [NOR] ИЛИ-НЕ
    [NAND] И-НЕ
    [XNOR] Эквивалентность
   *[other] { $gate }
    }
