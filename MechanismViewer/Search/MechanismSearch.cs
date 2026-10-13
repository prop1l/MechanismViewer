using MechanismViewer.Catalog;
using System.Globalization;

namespace MechanismViewer.Search;

public enum NoteKind
{
    /// <summary>Требование выполнено.</summary>
    Match,

    /// <summary>Подходит с оговоркой (отклонение, малый запас, нет данных).</summary>
    Warning,

    Info
}

public sealed record SearchNote(NoteKind Kind, string Text);

/// <summary>Подходящий механизм: оценка 0…100 и пояснения к ней.</summary>
public sealed record SearchResult(MechanismDefinition Mechanism, double Score, IReadOnlyList<SearchNote> Notes);

/// <summary>Механизм, отсеянный жёстким требованием.</summary>
public sealed record RejectedMechanism(MechanismDefinition Mechanism, string Reason);

public sealed record SearchOutcome(IReadOnlyList<SearchResult> Results, IReadOnlyList<RejectedMechanism> Rejected);

/// <summary>
/// Поиск по каталогу в два шага.
/// 1. Жёсткие фильтры: вид движения, привод, допустимая нагрузка и скорость, габариты.
/// 2. Ранжирование: штраф за отклонение кинематики от требуемой (в логарифмах, чтобы ×2 и ×0,5 весили одинаково),
///    за малый запас по нагрузке и отсутствие паспортных данных; небольшой бонус за компактность.
/// </summary>
public static class MechanismSearch
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static SearchOutcome Run(IEnumerable<MechanismDefinition> catalog, SearchQuery query)
    {
        var results = new List<SearchResult>();
        var rejected = new List<RejectedMechanism>();

        foreach (MechanismDefinition mechanism in catalog)
        {
            var notes = new List<SearchNote>();
            string? reason = Filter(mechanism, query, notes);

            if (reason is not null)
            {
                rejected.Add(new RejectedMechanism(mechanism, reason));
                continue;
            }

            double score = 100.0 - Rank(mechanism, query, notes);
            results.Add(new SearchResult(mechanism, Math.Clamp(score, 0.0, 100.0), notes));
        }

        return new SearchOutcome(
            results.OrderByDescending(r => r.Score).ThenBy(r => r.Mechanism.Name).ToList(),
            rejected);
    }

    /// <summary>Причина отказа или null. Выполненные требования дописываются в <paramref name="notes"/>.</summary>
    private static string? Filter(MechanismDefinition m, SearchQuery q, List<SearchNote> notes)
    {
        if (q.Input is { } input && input != m.Motion.Input)
        {
            return $"на входе {MotionName(m.Motion.Input)}, а нужно {MotionName(input)}";
        }

        if (q.Output is { } output && output != m.Motion.Output)
        {
            return $"на выходе {MotionName(m.Motion.Output)}, а нужно {MotionName(output)}";
        }

        if (q.Input is not null || q.Output is not null)
        {
            notes.Add(new SearchNote(NoteKind.Match, $"{MotionName(m.Motion.Input)} → {MotionName(m.Motion.Output)}"));
        }

        if (q.Driver is { } driver)
        {
            if (!DriverMatches(driver, m.Driver.Type))
            {
                return $"привод — {DriverName(m.Driver.Type)}, а нужен {DriverName(driver)}";
            }

            notes.Add(new SearchNote(NoteKind.Match, $"привод: {DriverName(m.Driver.Type)}"));
        }

        if (q.OutputTorqueNm is { } torque && m.Motion.Output == "rotation" && m.Specs.MaxTorqueNm is { } maxTorque && maxTorque < torque)
        {
            return $"не выдержит момент: допустимо {N(maxTorque)} Н·м < {N(torque)} Н·м";
        }

        if (q.OutputForceN is { } force && m.Motion.Output == "linear" && m.Specs.MaxForceN is { } maxForce && maxForce < force)
        {
            return $"не выдержит усилие: допустимо {N(maxForce)} Н < {N(force)} Н";
        }

        if (q.SpeedRadS is { } speed && m.Specs.MaxSpeedRadS is { } maxSpeed && maxSpeed < speed)
        {
            return $"допустимая скорость {N(maxSpeed)} рад/с < {N(speed)} рад/с";
        }

        if (q.MaxEnvelopeMm is { Length: 3 } limit && m.EnvelopeMm is { Length: 3 } size)
        {
            if (!Fits(size, limit))
            {
                return $"габариты {Size(size)} мм не помещаются в {Size(limit)} мм";
            }

            bool asIs = size.Zip(limit).All(p => p.First <= p.Second);
            notes.Add(new SearchNote(NoteKind.Match,
                asIs ? $"габариты {Size(size)} мм" : $"габариты {Size(size)} мм — помещается при повороте"));
        }

        return null;
    }

    /// <summary>Сумма штрафов (из 100).</summary>
    private static double Rank(MechanismDefinition m, SearchQuery q, List<SearchNote> notes)
    {
        double penalty = 0.0;
        double tolerance = Math.Log(1.0 + Math.Max(q.TolerancePercent, 1.0) / 100.0);

        penalty += Kinematic("передаточное число", "", q.Ratio, m.Specs.Ratio is { } r ? Math.Abs(r) : null, tolerance, notes);
        penalty += Kinematic("ход", " мм", q.StrokeMm, m.Specs.StrokeMm, tolerance, notes);
        penalty += Kinematic("угол поворота", "°", q.AngleDeg, m.Specs.AngleDeg, tolerance, notes);

        penalty += Load(q.OutputTorqueNm, "момент", "Н·м", m.Motion.Output == "rotation", m.Specs.MaxTorqueNm, notes);
        penalty += Load(q.OutputForceN, "усилие", "Н", m.Motion.Output == "linear", m.Specs.MaxForceN, notes);

        if (q.SpeedRadS is { } speed)
        {
            if (m.Specs.MaxSpeedRadS is { } maxSpeed)
            {
                notes.Add(new SearchNote(NoteKind.Match, $"скорость до {N(maxSpeed)} рад/с"));
            }
            else
            {
                penalty += 5.0;
                notes.Add(new SearchNote(NoteKind.Warning, "допустимая скорость не указана"));
            }
        }

        if (q.MaxEnvelopeMm is { Length: 3 } limit)
        {
            if (m.EnvelopeMm is { Length: 3 } size)
            {
                // До 5 очков за запас по объёму: при равной кинематике выше — компактный.
                double fill = size.Aggregate(1.0, (a, b) => a * b) / limit.Aggregate(1.0, (a, b) => a * b);
                penalty += 5.0 * Math.Clamp(fill, 0.0, 1.0);
            }
            else
            {
                penalty += 5.0;
                notes.Add(new SearchNote(NoteKind.Warning, "габариты не указаны"));
            }
        }

        return penalty;
    }

    /// <summary>
    /// В пределах допуска — до 10 очков штрафа, дальше — ещё до 40 (полностью при отклонении в 2 раза за допуском).
    /// </summary>
    private static double Kinematic(string name, string unit, double? required, double? actual, double tolerance, List<SearchNote> notes)
    {
        if (required is not { } need || need <= 0.0)
        {
            return 0.0;
        }

        if (actual is not { } have || have <= 0.0)
        {
            notes.Add(new SearchNote(NoteKind.Warning, $"{name}: не указан в паспорте"));
            return 20.0;
        }

        double deviation = Math.Abs(Math.Log(have / need));
        string percent = ((have / need - 1.0) * 100.0).ToString("+0;−0", Ru);

        if (deviation <= tolerance)
        {
            notes.Add(new SearchNote(NoteKind.Match, $"{name} {N(have)}{unit} (нужно {N(need)}{unit}, {percent}%)"));
            return 10.0 * deviation / tolerance;
        }

        notes.Add(new SearchNote(NoteKind.Warning, $"{name} {N(have)}{unit} — вне допуска (нужно {N(need)}{unit}, {percent}%)"));
        return 10.0 + 40.0 * Math.Min(1.0, (deviation - tolerance) / Math.Log(2.0));
    }

    private static double Load(double? required, string name, string unit, bool applicable, double? allowed, List<SearchNote> notes)
    {
        if (required is not { } need || need <= 0.0)
        {
            return 0.0;
        }

        if (!applicable)
        {
            notes.Add(new SearchNote(NoteKind.Warning, $"нагрузка задана как {name}, а выход другого типа"));
            return 15.0;
        }

        if (allowed is not { } max)
        {
            notes.Add(new SearchNote(NoteKind.Warning, $"допустимый {name} не указан"));
            return 15.0;
        }

        double margin = max / need;
        if (margin < 1.25)
        {
            notes.Add(new SearchNote(NoteKind.Warning, $"{name} до {N(max)} {unit} — малый запас ×{N(margin)}"));
            return 10.0;
        }

        notes.Add(new SearchNote(NoteKind.Match, $"{name} до {N(max)} {unit}, запас ×{N(margin)}"));
        return 0.0;
    }

    /// <summary>Помещается ли коробка в коробку при повороте на 90° вокруг осей (сравнение отсортированных размеров).</summary>
    private static bool Fits(double[] size, double[] limit) =>
        size.Order().Zip(limit.Order()).All(p => p.First <= p.Second);

    public static bool DriverMatches(string query, string type) => query switch
    {
        "manual" => type is "lever" or "crank",
        _ => query == type
    };

    public static string MotionName(string motion) => motion switch
    {
        "rotation" => "вращение",
        "linear" => "линейное движение",
        "oscillation" => "качание",
        "intermittent" => "прерывистое вращение",
        _ => motion
    };

    public static string DriverName(string driver) => driver switch
    {
        "motor" => "мотор",
        "manual" => "ручной",
        "lever" => "рычаг",
        "crank" => "ручка",
        "button" => "кнопка",
        _ => driver
    };

    private static string N(double value) => value.ToString("0.###", Ru);

    private static string Size(double[] size) => string.Join("×", size.Select(N));
}
