namespace MechanismViewer.Search;

/// <summary>
/// Требования пользователя к механизму. null — параметр не важен.
/// Движение: rotation, linear (как в <c>motion</c> каталога). Привод: motor, manual (рычаг/ручка), button.
/// </summary>
public sealed record SearchQuery
{
    public string? Input { get; init; }
    public string? Output { get; init; }
    public string? Driver { get; init; }

    /// <summary>Передаточное число |ω_вых/ω_вх| (для вращение→вращение).</summary>
    public double? Ratio { get; init; }

    /// <summary>Ход выходного звена, мм.</summary>
    public double? StrokeMm { get; init; }

    /// <summary>Угол поворота, °.</summary>
    public double? AngleDeg { get; init; }

    /// <summary>Момент нагрузки на вращающемся выходе, Н·м.</summary>
    public double? OutputTorqueNm { get; init; }

    /// <summary>Усилие нагрузки на поступательном выходе, Н.</summary>
    public double? OutputForceN { get; init; }

    /// <summary>Нужная скорость вала, рад/с (механизм должен её допускать).</summary>
    public double? SpeedRadS { get; init; }

    /// <summary>Наибольшие габариты (в любой ориентации), мм.</summary>
    public double[]? MaxEnvelopeMm { get; init; }

    /// <summary>Допуск на кинематические параметры, %.</summary>
    public double TolerancePercent { get; init; } = 20.0;

    /// <summary>Ни одно требование не задано — подходит весь каталог.</summary>
    public bool IsEmpty =>
        Input is null && Output is null && Driver is null && Ratio is null && StrokeMm is null && AngleDeg is null
        && OutputTorqueNm is null && OutputForceN is null && SpeedRadS is null && MaxEnvelopeMm is null;

    /// <summary>Заданные требования одной строкой: «вращение → линейное · ход 25 мм · усилие 0,3 Н».</summary>
    public string Summary()
    {
        var parts = new List<string>();
        if (Input is not null || Output is not null)
        {
            parts.Add($"{(Input is null ? "любое" : MechanismSearch.MotionName(Input))} → {(Output is null ? "любое" : MechanismSearch.MotionName(Output))}");
        }

        if (Driver is not null) parts.Add($"привод: {MechanismSearch.DriverName(Driver)}");
        if (Ratio is { } ratio) parts.Add($"передаточное {N(ratio)}");
        if (StrokeMm is { } stroke) parts.Add($"ход {N(stroke)} мм");
        if (AngleDeg is { } angle) parts.Add($"угол {N(angle)}°");
        if (OutputTorqueNm is { } torque) parts.Add($"момент {N(torque)} Н·м");
        if (OutputForceN is { } force) parts.Add($"усилие {N(force)} Н");
        if (SpeedRadS is { } speed) parts.Add($"скорость {N(speed)} рад/с");
        if (MaxEnvelopeMm is { } size) parts.Add($"габариты {string.Join("×", size.Select(N))} мм");
        return string.Join(" · ", parts);
    }

    private static string N(double value) => value.ToString("0.###", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
}
