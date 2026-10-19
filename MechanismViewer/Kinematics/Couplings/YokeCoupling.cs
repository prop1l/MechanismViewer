namespace MechanismViewer.Kinematics.Couplings;

/// <summary>
/// Синусный (кулисный) механизм: палец кривошипа радиуса r ходит в прорези кулисы, перпендикулярной оси ползуна.
/// Ползун повторяет проекцию пальца на свою ось: x(θ) = r·cos(θ − γ), γ — направление оси ползуна.
/// Закон чисто гармонический, без поправки на длину шатуна. Координата ползуна — смещение от начального положения.
/// Эллипсограф: каждый ползун — проекция конца звена длиной L, то есть та же связь с r = L.
/// </summary>
public sealed class YokeCoupling : ICoupling
{
    private readonly double _r;
    private readonly double _theta0;

    public string From { get; }
    public string To { get; }

    /// <summary>Полный ход ползуна, м.</summary>
    public double Stroke => 2.0 * _r;

    /// <param name="crankAngleRad">Начальный угол кривошипа относительно оси ползуна.</param>
    public YokeCoupling(string from, string to, double crankM, double crankAngleRad)
    {
        if (crankM <= 0.0)
        {
            throw new ArgumentException($"Кулиса {from}→{to}: радиус кривошипа должен быть положительным");
        }

        From = from;
        To = to;
        _r = crankM;
        _theta0 = crankAngleRad;
    }

    public double Evaluate(double qFrom) => _r * (Math.Cos(_theta0 + qFrom) - Math.Cos(_theta0));

    public double Derivative(double qFrom) => -_r * Math.Sin(_theta0 + qFrom);
}
