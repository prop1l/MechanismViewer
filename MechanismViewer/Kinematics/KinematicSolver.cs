using MechanismViewer.Catalog;
using MechanismViewer.Kinematics.Couplings;
using System.IO;

namespace MechanismViewer.Kinematics;

/// <summary>Координата детали и её передаточная функция k = dq_i/dq от ведущей координаты.</summary>
public readonly record struct PartState(double Q, double K);

/// <summary>
/// Механизм с одной степенью свободы: координата ведущей детали q однозначно задаёт
/// координаты всех подвижных деталей. Связи обходятся в ширину от ведущей детали.
/// </summary>
public sealed class KinematicSolver
{
    private readonly (ICoupling Coupling, int From, int To)[] _steps;

    /// <summary>Подвижные детали (revolute/prismatic) в порядке обхода; [0] — ведущая.</summary>
    public IReadOnlyList<PartSpec> Parts { get; }

    public IReadOnlyList<ICoupling> Couplings { get; }

    public KinematicSolver(MechanismDefinition mechanism)
    {
        PartSpec driver = mechanism.Part(mechanism.Driver.Part);
        if (!IsMoving(driver))
        {
            throw new InvalidDataException($"Механизм «{mechanism.Id}»: ведущая деталь «{driver.Id}» должна иметь шарнир revolute или prismatic");
        }

        List<ICoupling> couplings = mechanism.Couplings.Select(c => Create(c, mechanism)).ToList();
        var order = new List<PartSpec> { driver };
        var index = new Dictionary<string, int> { [driver.Id] = 0 };
        var steps = new List<(ICoupling, int, int)>();
        var queue = new Queue<string>([driver.Id]);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            foreach (ICoupling coupling in couplings.Where(c => c.From == current))
            {
                if (index.ContainsKey(coupling.To))
                {
                    throw new InvalidDataException(
                        $"Механизм «{mechanism.Id}»: деталь «{coupling.To}» получает движение по двум связям — замкнутые контуры не поддерживаются");
                }

                index[coupling.To] = order.Count;
                order.Add(mechanism.Part(coupling.To));
                steps.Add((coupling, index[current], index[coupling.To]));
                queue.Enqueue(coupling.To);
            }
        }

        foreach (PartSpec part in mechanism.Parts.Where(IsMoving))
        {
            if (!index.ContainsKey(part.Id))
            {
                throw new InvalidDataException($"Механизм «{mechanism.Id}»: деталь «{part.Id}» не связана с приводом");
            }
        }

        foreach (ICoupling coupling in couplings.Where(c => !index.ContainsKey(c.From)))
        {
            throw new InvalidDataException($"Механизм «{mechanism.Id}»: связь {coupling.From}→{coupling.To} не достижима от привода");
        }

        Parts = order;
        Couplings = couplings;
        _steps = steps.ToArray();
    }

    public int IndexOf(string partId)
    {
        for (int i = 0; i < Parts.Count; i++)
        {
            if (Parts[i].Id == partId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Заполняет <paramref name="result"/> (по индексам <see cref="Parts"/>) для ведущей координаты q.</summary>
    public void Solve(double q, Span<PartState> result)
    {
        result[0] = new PartState(q, 1.0);

        foreach ((ICoupling coupling, int from, int to) in _steps)
        {
            PartState source = result[from];
            result[to] = new PartState(
                coupling.Evaluate(source.Q),
                coupling.Derivative(source.Q) * source.K);
        }
    }

    public PartState[] Solve(double q)
    {
        var result = new PartState[Parts.Count];
        Solve(q, result);
        return result;
    }

    public static bool IsMoving(PartSpec part) => part.Joint.Type is "revolute" or "prismatic";

    private static ICoupling Create(CouplingSpec spec, MechanismDefinition mechanism)
    {
        PartSpec from = mechanism.Part(spec.From);
        PartSpec to = mechanism.Part(spec.To);

        string[] allowed = spec.Type switch
        {
            "gear" or "four-bar" or "geneva" => ["revolute→revolute"],
            // Рейка может вести шестерню (кнопка, толкатель) или шестерня — рейку.
            "rack" => ["revolute→prismatic", "prismatic→revolute"],
            "screw" or "crank-slider" or "cam" => ["revolute→prismatic"],
            _ => throw new InvalidDataException($"Механизм «{mechanism.Id}»: неизвестный тип связи «{spec.Type}»")
        };

        string actual = $"{from.Joint.Type}→{to.Joint.Type}";
        if (!allowed.Contains(actual))
        {
            throw new InvalidDataException(
                $"Механизм «{mechanism.Id}»: связь {spec.Type} {from.Id}→{to.Id} требует шарниров {string.Join(" или ", allowed)}");
        }

        return spec.Type switch
        {
            "gear" => new GearCoupling(from.Id, to.Id, spec.Ratio ?? GearRatio(from, to, mechanism)),
            "rack" => RackPinionCoupling.Create(from.Id, to.Id, rackDrivesPinion: actual == "prismatic→revolute",
                Mm(spec.RadiusMm ?? (from.Gear ?? to.Gear)?.PitchRadiusMm ?? Missing(mechanism, spec, "radiusMm"))),
            "screw" => new ScrewCoupling(from.Id, to.Id, Mm(spec.LeadMm ?? Missing(mechanism, spec, "leadMm"))),
            "four-bar" => FourBar(spec, from, to, mechanism),
            "geneva" => Geneva(spec, from, to, mechanism),
            "cam" => Cam(spec, from, to, mechanism),
            _ => new CrankSliderCoupling(from.Id, to.Id,
                Mm(spec.CrankMm ?? Missing(mechanism, spec, "crankMm")),
                Mm(spec.RodMm ?? Missing(mechanism, spec, "rodMm")),
                Mm(spec.OffsetMm),
                from.Joint.Phase * Math.PI / 180.0)
        };
    }

    /// <summary>Вектор между осями двух деталей в плоскости XY (оси шарниров — вдоль Z), м, и его угол.</summary>
    private static (double Length, double Angle) Between(PartSpec from, PartSpec to)
    {
        double dx = to.Joint.Origin[0] - from.Joint.Origin[0];
        double dy = to.Joint.Origin[1] - from.Joint.Origin[1];
        return (Math.Sqrt(dx * dx + dy * dy) * 1e-3, Math.Atan2(dy, dx));
    }

    private static ICoupling FourBar(CouplingSpec spec, PartSpec crank, PartSpec rocker, MechanismDefinition mechanism)
    {
        (double ground, double groundAngle) = Between(crank, rocker);
        var coupling = new FourBarCoupling(crank.Id, rocker.Id,
            Mm(spec.CrankMm ?? Missing(mechanism, spec, "crankMm")),
            Mm(spec.CouplerMm ?? Missing(mechanism, spec, "couplerMm")),
            Mm(spec.RockerMm ?? Missing(mechanism, spec, "rockerMm")),
            ground, spec.Branch, crank.Joint.Phase * Math.PI / 180.0 - groundAngle);

        // STL коромысла ставится по phase: она должна совпадать с собранной геометрией, иначе шатун «рвётся».
        double expected = (groundAngle + coupling.InitialRockerAngle) * 180.0 / Math.PI;
        if (Math.Abs(Math.IEEERemainder(expected - rocker.Joint.Phase, 360.0)) > 0.5)
        {
            throw new InvalidDataException(
                $"Механизм «{mechanism.Id}»: начальный угол коромысла «{rocker.Id}» должен быть {expected:0.###}° (phase), а задан {rocker.Joint.Phase}°");
        }

        return coupling;
    }

    private static ICoupling Geneva(CouplingSpec spec, PartSpec driver, PartSpec cross, MechanismDefinition mechanism)
    {
        (double distance, double lineAngle) = Between(driver, cross);
        return new GenevaCoupling(driver.Id, cross.Id,
            spec.Slots ?? (int)Missing(mechanism, spec, "slots"),
            distance, driver.Joint.Phase * Math.PI / 180.0 - lineAngle);
    }

    private static ICoupling Cam(CouplingSpec spec, PartSpec cam, PartSpec follower, MechanismDefinition mechanism)
    {
        IReadOnlyList<CamSegmentSpec> segments = spec.Segments
            ?? throw new InvalidDataException($"Механизм «{mechanism.Id}»: у кулачка {cam.Id}→{follower.Id} не заданы segments");

        return new CamCoupling(cam.Id, follower.Id,
            segments.Select(s => (s.AngleDeg * Math.PI / 180.0, Mm(s.RiseMm), s.Law)).ToList(),
            cam.Joint.Phase * Math.PI / 180.0);
    }

    private static double GearRatio(PartSpec from, PartSpec to, MechanismDefinition mechanism)
    {
        if (from.Gear is null || to.Gear is null)
        {
            throw new InvalidDataException(
                $"Механизм «{mechanism.Id}»: для связи gear {from.Id}→{to.Id} задайте ratio или gear у обеих деталей");
        }

        // Внешнее зацепление меняет направление вращения.
        return -(double)from.Gear.Teeth / to.Gear.Teeth;
    }

    private static double Missing(MechanismDefinition mechanism, CouplingSpec spec, string field) =>
        throw new InvalidDataException($"Механизм «{mechanism.Id}»: у связи {spec.Type} {spec.From}→{spec.To} не задано {field}");

    private static double Mm(double value) => value * 1e-3;
}
