using MechanismViewer.Catalog;
using System.IO;

namespace MechanismViewer.Physics;

/// <summary>
/// Подвижная деталь механизма на шарнире revolute или prismatic. Динамика в СИ:
/// для вращения — кг·м², Н·м·с/рад, Н·м, рад; для сдвига — кг, Н·с/м, Н, м.
/// </summary>
public sealed class PhysicsBody
{
    public required string Name { get; init; }
    public required string PartId { get; init; }
    public required bool IsPrismatic { get; init; }

    /// <summary>Момент инерции (revolute) или масса (prismatic).</summary>
    public required double Inertia { get; init; }

    public required double ViscousFriction { get; init; }
    public required double CoulombFriction { get; init; }

    /// <summary>Радиус делительной окружности, м, если деталь — шестерня.</summary>
    public double? PitchRadiusM { get; init; }

    /// <summary>Начальная координата шарнира (рад или м).</summary>
    public required double InitialCoordinate { get; init; }

    /// <summary>Смещение от начального положения (рад или м).</summary>
    public double Coordinate { get; internal set; }

    public double Velocity { get; internal set; }

    /// <summary>Отношение скорости детали к скорости ведущего вала, dq/dq_ведущего.</summary>
    public double Ratio { get; internal set; }

    public double AbsoluteCoordinate => InitialCoordinate + Coordinate;

    public static PhysicsBody FromPart(PartSpec part)
    {
        bool prismatic = part.Joint.Type == "prismatic";
        double inertia = prismatic
            ? part.MassKg ?? throw Missing(part, "massKg")
            : part.InertiaKgM2 ?? GearInertia(part) ?? throw Missing(part, "inertiaKgM2");

        return new PhysicsBody
        {
            Name = string.IsNullOrEmpty(part.Name) ? part.Id : part.Name,
            PartId = part.Id,
            IsPrismatic = prismatic,
            Inertia = inertia,
            ViscousFriction = part.ViscousFriction ?? (prismatic ? 0.05 : 2e-6),
            CoulombFriction = part.CoulombFriction ?? (prismatic ? 0.02 : 2e-5),
            PitchRadiusM = part.Gear?.PitchRadiusMm * 1e-3,
            InitialCoordinate = prismatic ? part.Joint.Phase * 1e-3 : part.Joint.Phase * Math.PI / 180.0
        };
    }

    /// <summary>Шестерня как сплошной диск радиуса впадин: J = m·r²/2.</summary>
    private static double? GearInertia(PartSpec part)
    {
        if (part.Gear is not { } gear)
        {
            return null;
        }

        double r = gear.RootRadiusMm * 1e-3;
        double mass = part.DensityKgM3 * Math.PI * r * r * (gear.ThicknessMm * 1e-3);
        return 0.5 * mass * r * r;
    }

    private static InvalidDataException Missing(PartSpec part, string field) =>
        new($"Деталь «{part.Id}»: не задано {field}");
}
