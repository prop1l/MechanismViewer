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
}
