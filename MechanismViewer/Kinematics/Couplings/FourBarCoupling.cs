namespace MechanismViewer.Kinematics.Couplings;

/// <summary>
/// Шарнирный четырёхзвенник: кривошип a на оси O2, шатун b, коромысло c на оси O4, стойка d = |O2O4|.
/// В системе стойки (O2 — начало, O4 на +X) палец кривошипа A = a·(cos θ, sin θ), e = |O4A|,
/// угол коромысла φ = π + atan2(−A_y, d − A_x) − branch·acos((c² + e² − b²) / (2·c·e)).
/// Запись через atan2(−A_y, d − A_x) не имеет разрыва, пока a &lt; d.
/// Координата коромысла — смещение от начального угла: q_to = φ(θ0 + q) − φ(θ0).
/// </summary>
public sealed class FourBarCoupling : ICoupling
{
    private const double DerivativeStep = 1e-6;

    private readonly double _a;
    private readonly double _b;
    private readonly double _c;
    private readonly double _d;
    private readonly int _branch;
    private readonly double _theta0;
    private readonly double _phi0;

    public string From { get; }
    public string To { get; }

    /// <summary>Кривошип делает полный оборот (критерий Грасгофа для кривошипно-коромыслового механизма).</summary>
    public bool IsFullRotation { get; }

    /// <summary>Начальный угол коромысла относительно стойки, рад.</summary>
    public double InitialRockerAngle => _phi0;

    /// <param name="crankAngleRad">Начальный угол кривошипа относительно направления O2→O4.</param>
    public FourBarCoupling(string from, string to, double crankM, double couplerM, double rockerM, double groundM,
        int branch, double crankAngleRad)
    {
        From = from;
        To = to;
        (_a, _b, _c, _d) = (crankM, couplerM, rockerM, groundM);
        _branch = branch >= 0 ? 1 : -1;
        _theta0 = crankAngleRad;

        if (_a <= 0 || _b <= 0 || _c <= 0 || _d <= _a)
        {
            throw new ArgumentException($"Четырёхзвенник {from}→{to}: нужны положительные длины и стойка длиннее кривошипа");
        }

        if (Math.Abs(Cosine(_theta0)) > 1.0)
        {
            throw new ArgumentException($"Четырёхзвенник {from}→{to}: в начальном положении звенья не собираются");
        }

        double[] lengths = [_a, _b, _c, _d];
        double s = lengths.Min(), l = lengths.Max();
        IsFullRotation = _a == s && s + l <= lengths.Sum() - s - l;

        _phi0 = Angle(_theta0);
    }

    public double Evaluate(double qFrom) => Angle(_theta0 + qFrom) - _phi0;

    public double Derivative(double qFrom) =>
        (Angle(_theta0 + qFrom + DerivativeStep) - Angle(_theta0 + qFrom - DerivativeStep)) / (2.0 * DerivativeStep);

    /// <summary>Угол коромысла относительно стойки; вне области сборки — в крайнем положении.</summary>
    public double Angle(double theta)
    {
        double ax = _a * Math.Cos(theta), ay = _a * Math.Sin(theta);
        double gamma = Math.Acos(Math.Clamp(Cosine(theta), -1.0, 1.0));
        return Math.PI + Math.Atan2(-ay, _d - ax) - _branch * gamma;
    }

    private double Cosine(double theta)
    {
        double ax = _a * Math.Cos(theta), ay = _a * Math.Sin(theta);
        double e2 = (_d - ax) * (_d - ax) + ay * ay;
        return (_c * _c + e2 - _b * _b) / (2.0 * _c * Math.Sqrt(e2));
    }
}
