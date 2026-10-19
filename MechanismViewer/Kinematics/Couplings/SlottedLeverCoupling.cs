namespace MechanismViewer.Kinematics.Couplings;

/// <summary>
/// Качающаяся кулиса: палец кривошипа радиуса r скользит в прорези коромысла, ось коромысла — на расстоянии d &gt; r
/// от оси кривошипа. В системе стойки (ось коромысла — начало, ось кривошипа на +X) угол коромысла
/// ψ(θ) = atan2(r·sin θ, d + r·cos θ), размах ±asin(r/d). Рабочий ход занимает π + 2·asin(r/d) оборота кривошипа,
/// обратный — π − 2·asin(r/d): механизм быстрого возврата (поперечно-строгальный станок).
/// Координата коромысла — смещение от начального угла.
/// </summary>
public sealed class SlottedLeverCoupling : ICoupling
{
    private readonly double _r;
    private readonly double _d;
    private readonly double _theta0;
    private readonly double _psi0;

    public string From { get; }
    public string To { get; }

    /// <summary>Начальный угол коромысла относительно линии «ось коромысла → ось кривошипа», рад.</summary>
    public double InitialRockerAngle => _psi0;

    /// <summary>Полный угол качания, рад.</summary>
    public double Swing => 2.0 * Math.Asin(_r / _d);

    /// <summary>Отношение времени рабочего хода ко времени обратного при постоянной скорости кривошипа.</summary>
    public double TimeRatio => (Math.PI + Swing) / (Math.PI - Swing);

    /// <param name="crankAngleRad">Начальный угол кривошипа относительно линии «ось коромысла → ось кривошипа».</param>
    public SlottedLeverCoupling(string from, string to, double crankM, double distanceM, double crankAngleRad)
    {
        if (crankM <= 0.0 || distanceM <= crankM)
        {
            throw new ArgumentException(
                $"Кулиса {from}→{to}: ось коромысла должна быть дальше от оси кривошипа, чем его радиус (r = {crankM * 1e3} мм, d = {distanceM * 1e3} мм)");
        }

        From = from;
        To = to;
        _r = crankM;
        _d = distanceM;
        _theta0 = crankAngleRad;
        _psi0 = Angle(crankAngleRad);
    }

    public double Evaluate(double qFrom) => Angle(_theta0 + qFrom) - _psi0;

    public double Derivative(double qFrom)
    {
        double cos = Math.Cos(_theta0 + qFrom);
        return _r * (_d * cos + _r) / (_d * _d + 2.0 * _d * _r * cos + _r * _r);
    }

    /// <summary>Угол коромысла (рад) при угле кривошипа θ.</summary>
    public double Angle(double theta) => Math.Atan2(_r * Math.Sin(theta), _d + _r * Math.Cos(theta));
}
