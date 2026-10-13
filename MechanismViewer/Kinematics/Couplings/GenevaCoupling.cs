namespace MechanismViewer.Kinematics.Couplings;

/// <summary>
/// Мальтийский крест с n пазами: палец ведущего диска на радиусе r = C·sin(π/n), межосевое C.
/// θ' — угол пальца от линии центров (ведущий → крест), приведённый к [−π, π), k — номер оборота.
/// Пока палец в пазу (|θ'| &lt; π/2 − π/n): φ' = −atan2(r·sin θ', C − r·cos θ'); иначе крест заперт: φ' = −sign(θ')·π/n.
/// Угол креста φ = φ' − k·2π/n непрерывен; на входе и выходе пальца скорость креста нулевая (без удара).
/// </summary>
public sealed class GenevaCoupling : ICoupling
{
    private readonly int _n;
    private readonly double _c;
    private readonly double _r;
    private readonly double _engage;
    private readonly double _theta0;
    private readonly double _phi0;

    public string From { get; }
    public string To { get; }

    /// <summary>Радиус пальца ведущего диска, м.</summary>
    public double PinRadius => _r;

    /// <param name="driverAngleRad">Начальный угол пальца относительно линии центров.</param>
    public GenevaCoupling(string from, string to, int slots, double centerDistanceM, double driverAngleRad)
    {
        if (slots < 3)
        {
            throw new ArgumentException($"Мальтийский крест {from}→{to}: нужно не меньше 3 пазов");
        }

        From = from;
        To = to;
        _n = slots;
        _c = centerDistanceM;
        _r = centerDistanceM * Math.Sin(Math.PI / slots);
        _engage = Math.PI / 2.0 - Math.PI / slots;
        _theta0 = driverAngleRad;
        _phi0 = Angle(driverAngleRad);
    }

    public double Evaluate(double qFrom) => Angle(_theta0 + qFrom) - _phi0;

    public double Derivative(double qFrom)
    {
        double t = Wrap(_theta0 + qFrom, out _);
        if (Math.Abs(t) >= _engage)
        {
            return 0.0;
        }

        double cos = Math.Cos(t);
        return -_r * (_c * cos - _r) / (_c * _c - 2.0 * _c * _r * cos + _r * _r);
    }

    /// <summary>Угол креста (рад) при угле пальца θ от линии центров.</summary>
    public double Angle(double theta)
    {
        double t = Wrap(theta, out int turns);
        double local = Math.Abs(t) < _engage
            ? -Math.Atan2(_r * Math.Sin(t), _c - _r * Math.Cos(t))
            : -Math.Sign(t) * Math.PI / _n;
        return local - turns * 2.0 * Math.PI / _n;
    }

    private static double Wrap(double theta, out int turns)
    {
        turns = (int)Math.Floor((theta + Math.PI) / (2.0 * Math.PI));
        return theta - turns * 2.0 * Math.PI;
    }
}
