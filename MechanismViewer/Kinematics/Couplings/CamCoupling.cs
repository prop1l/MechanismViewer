namespace MechanismViewer.Kinematics.Couplings;

/// <summary>
/// Кулачок → поступательный толкатель. Закон s(θ) задан участками за оборот (подъём, выстой, опускание);
/// θ — угол поворота кулачка, s(θ + 2π) = s(θ). Координата толкателя — смещение от начального положения.
/// Циклоидальный закон на участке длиной B с подъёмом h: s = h·(β/B − sin(2πβ/B)/2π), гармонический: s = h·(1 − cos(πβ/B))/2.
/// </summary>
public sealed class CamCoupling : ICoupling
{
    private readonly (double Start, double Length, double Rise, double Base, bool Cycloidal)[] _segments;
    private readonly double _theta0;
    private readonly double _s0;

    public string From { get; }
    public string To { get; }

    /// <summary>Наибольший подъём толкателя над начальным уровнем закона, м.</summary>
    public double Stroke { get; }

    /// <param name="segments">Углы (рад), подъёмы (м) и законы участков; сумма углов — 2π, сумма подъёмов — 0.</param>
    public CamCoupling(string from, string to, IReadOnlyList<(double Angle, double Rise, string Law)> segments, double camPhaseRad)
    {
        From = from;
        To = to;

        double total = segments.Sum(s => s.Angle);
        if (segments.Count == 0 || Math.Abs(total - 2.0 * Math.PI) > 1e-6)
        {
            throw new ArgumentException($"Кулачок {from}→{to}: сумма углов участков должна быть 360°");
        }

        if (Math.Abs(segments.Sum(s => s.Rise)) > 1e-9)
        {
            throw new ArgumentException($"Кулачок {from}→{to}: за оборот толкатель должен вернуться (сумма подъёмов 0)");
        }

        if (segments.FirstOrDefault(s => s.Law is not ("cycloidal" or "harmonic")) is { Law: { } bad } && bad.Length > 0)
        {
            throw new ArgumentException($"Кулачок {from}→{to}: неизвестный закон «{bad}»");
        }

        var list = new List<(double, double, double, double, bool)>();
        double start = 0.0, level = 0.0, max = 0.0;
        foreach ((double angle, double rise, string law) in segments)
        {
            list.Add((start, angle, rise, level, law == "cycloidal"));
            start += angle;
            level += rise;
            max = Math.Max(max, level);
        }

        _segments = list.ToArray();
        Stroke = max;
        _theta0 = camPhaseRad;
        _s0 = Lift(camPhaseRad);
    }

    public double Evaluate(double qFrom) => Lift(_theta0 + qFrom) - _s0;

    public double Derivative(double qFrom)
    {
        (var segment, double beta) = Locate(_theta0 + qFrom);
        if (segment.Rise == 0.0)
        {
            return 0.0;
        }

        double x = beta / segment.Length;
        return segment.Cycloidal
            ? segment.Rise / segment.Length * (1.0 - Math.Cos(2.0 * Math.PI * x))
            : segment.Rise * Math.PI / (2.0 * segment.Length) * Math.Sin(Math.PI * x);
    }

    /// <summary>Подъём толкателя s(θ), м.</summary>
    public double Lift(double theta)
    {
        (var segment, double beta) = Locate(theta);
        double x = beta / segment.Length;
        double shape = segment.Cycloidal
            ? x - Math.Sin(2.0 * Math.PI * x) / (2.0 * Math.PI)
            : (1.0 - Math.Cos(Math.PI * x)) / 2.0;
        return segment.Base + segment.Rise * shape;
    }

    private ((double Start, double Length, double Rise, double Base, bool Cycloidal), double Beta) Locate(double theta)
    {
        double t = theta % (2.0 * Math.PI);
        if (t < 0.0)
        {
            t += 2.0 * Math.PI;
        }

        foreach (var segment in _segments)
        {
            if (t < segment.Start + segment.Length)
            {
                return (segment, t - segment.Start);
            }
        }

        var last = _segments[^1];
        return (last, last.Length);
    }
}
