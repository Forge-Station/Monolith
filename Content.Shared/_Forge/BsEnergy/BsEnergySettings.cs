namespace Content.Shared._Forge.BsEnergy;

public sealed class BsEnergySettings
{
    public const int KvtConst = 1000;
    public const int MvtConst = 1000000;

    public const int PassiveIncome = 20;                // Цена за кВт в минуту (пассивная прибыль за не распределённую энергию)
    public const int PasswordMaxLenght = 20;            // Максимальная длина пароля
    public const float UpdateInterval = 1.0f;           // Частота обновления расчётов (задержка в секундах)
    public const float GeneratorLossFactor = 0.9f;      // Коэффициент передачи энергии в диапазоне (0; 1]
}
