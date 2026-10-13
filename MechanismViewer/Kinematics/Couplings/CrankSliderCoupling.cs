namespace MechanismViewer.Kinematics.Couplings;

/// <summary>
/// Кривошипно-ползунный механизм. Ось ползуна — направление 0° кривошипа, смещённая на e по нормали.
/// Положение ползуна от оси кривошипа:  x(φ) = r·cos φ + √(L² − (r·sin φ − e)²),
/// координата ползуна — смещение от начального положения: q_to = x(φ0 + q) − x(φ0).
/// </summary>
public sealed class CrankSliderCoupling : ICoupling
{
    private readonly double _r;
    private readonly double _l;
    private readonly double _e;
    private readonly double _phase;
    private readonly double _x0;

    public string From { get; }
    public string To { get; }

    /// <param name="crankPhaseRad">Начальный угол кривошипа относительно оси ползуна.</param>
    public CrankSliderCoupling(string from, string to, double crankM, double rodM, double offsetM, double crankPhaseRad)
    {
        if (crankM <= 0.0 || rodM < crankM + Math.Abs(offsetM))
        {
            throw new ArgumentException(
                $"Кривошип {from}→{to}: шатун должен быть не короче r + |e| (r = {crankM * 1e3} мм, L = {rodM * 1e3} мм, e = {offsetM * 1e3} мм)");
        }

        From = from;
        To = to;
        _r = crankM;
        _l = rodM;
        _e = offsetM;
        _phase = crankPhaseRad;
        _x0 = Position(crankPhaseRad);
    }

    /// <summary>Полный ход ползуна, м.</summary>
    public double Stroke => Math.Sqrt((_l + _r) * (_l + _r) - _e * _e) - Math.Sqrt((_l - _r) * (_l - _r) - _e * _e);

    public double Evaluate(double qFrom) => Position(_phase + qFrom) - _x0;

    public double Derivative(double qFrom)
    {
        double phi = _phase + qFrom;
        double s = _r * Math.Sin(phi) - _e;
        return -_r * Math.Sin(phi) - s * _r * Math.Cos(phi) / Math.Sqrt(_l * _l - s * s);
    }

    private double Position(double phi)
    {
        double s = _r * Math.Sin(phi) - _e;
        return _r * Math.Cos(phi) + Math.Sqrt(_l * _l - s * s);
    }
}
