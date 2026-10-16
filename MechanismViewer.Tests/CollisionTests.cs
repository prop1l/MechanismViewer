using MechanismViewer.Catalog;
using MechanismViewer.Kinematics;
using MechanismViewer.Physics;
using MechanismViewer.Visuals;
using System.Collections;
using System.Windows.Media.Media3D;
using Xunit.Abstractions;

namespace MechanismViewer.Tests;

/// <summary>
/// Детали не должны проходить друг сквозь друга: механизм проводится по всему ходу,
/// и для каждой пары тел (детали, крепёж, мотор) считается объём взаимного проникновения на воксельной сетке.
/// </summary>
public class CollisionTests(ITestOutputHelper output)
{
    private const double Cell = 0.25;
    private const int Samples = 120;

    /// <summary>Пересечение мельче этого — погрешность сетки у касающихся поверхностей, мм³.</summary>
    private const double Noise = 1.0;

    /// <summary>
    /// Конструктивные пересечения, кроме общих (палец шатуна в его проушинах, вал мотора в ведущей детали):
    /// одна деталь входит в другую по замыслу, отверстие в модели не вырезано.
    /// </summary>
    private static readonly Dictionary<string, (string, string)[]> Designed = new()
    {
        // Винт в резьбе гайки, направляющая во втулке гайки, конец винта в торцевой опоре.
        ["screw-nut-tr8"] = [("screw", "nut"), ("nut", "frame"), ("screw", "frame")],
        // Ось колеса в стойках, конец червяка в передней опоре.
        ["worm-1-20"] = [("wheel", "frame"), ("worm", "frame")],
        // Оси блок-шестерни, колеса и указателя — часть основания.
        ["gearbox-slider-indicator"] = [("cluster", "frame"), ("crankGear", "frame"), ("indicator", "frame")],
    };

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
        {
            throw new AggregateException(error);
        }
    }

    public static TheoryData<string> Ids()
    {
        var data = new TheoryData<string>();
        foreach (MechanismDefinition m in CatalogLoader.LoadAll(CatalogLoader.DefaultRoot))
        {
            data.Add(m.Id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void PartsDoNotPassThroughEachOther(string id)
    {
        RunSta(() =>
        {
            MechanismDefinition m = CatalogLoader.Load(Path.Combine(CatalogLoader.DefaultRoot, id, CatalogLoader.FileName));
            var sim = new MechanismSimulator(m);
            var scene = new MechanismScene(m);

            var bodies = new List<Body>();
            foreach (ModelVisual3D visual in scene.Visuals)
            {
                (string? part, int? fastener) = scene.ItemAt(visual);
                bool isStatic = part is null || m.Part(part).Joint.Type == "fixed";
                string name = fastener is { } f ? $"{m.Fasteners[f].Kind} ({part})" : part!;
                bodies.Add(new Body(name, part, fastener is not null, isStatic, visual, Triangles(visual.Content, Matrix3D.Identity)));
            }

            MotorModel? motor = m.Driver.Type == "motor" ? new MotorModel(m.Driver.MotorGapMm, m.Driver.HubSeatMm, m.Driver.ShaftEndMm) : null;
            if (motor is not null)
            {
                bodies.Add(new Body("мотор", null, false, true, motor.Stator, Triangles(motor.Stator.Content, Matrix3D.Identity)));
                // Вал проходит сквозь ведущую деталь, ступица прижимает её: для проверки ротор — как крепёж этой детали.
                bodies.Add(new Body("ротор мотора", m.Driver.Part, true, false, motor.Rotor, Triangles(motor.Rotor.Content, Matrix3D.Identity)));
            }

            foreach (Body b in bodies)
            {
                b.Build();
            }

            var designed = new HashSet<(string, string)>(Designed.GetValueOrDefault(id, []));
            foreach (PartSpec part in m.Parts.Where(p => p.Joint is { Type: "link", Link: not null }))
            {
                designed.Add((part.Joint.Link!.From, part.Id));
                designed.Add((part.Joint.Link!.To, part.Id));
            }

            bool IsDesigned(Body a, Body b) =>
                !a.IsFastener && !b.IsFastener && (designed.Contains((a.Name, b.Name)) || designed.Contains((b.Name, a.Name)));

            (double from, double to) = Sweep(m, sim);
            var worst = new Dictionary<(int, int), Hit>();
            KinematicSolver kin = sim.Kinematics;
            var state = new PartState[kin.Parts.Count];

            for (int s = 0; s <= Samples; s++)
            {
                double q = from + (to - from) * s / Samples;
                kin.Solve(q, state);
                scene.Update(pid => state[kin.IndexOf(pid)].Q);
                if (motor is not null)
                {
                    double[] axis = m.Part(m.Driver.Part).Joint.Origin;
                    motor.SetPose(new System.Numerics.Vector3((float)axis[0], (float)axis[1], (float)axis[2]), q * 180.0 / Math.PI);
                }

                foreach (Body b in bodies)
                {
                    b.SetPose(b.Visual.Transform.Value);
                }

                for (int i = 0; i < bodies.Count; i++)
                {
                    for (int j = i + 1; j < bodies.Count; j++)
                    {
                        Body a = bodies[i], b = bodies[j];
                        // Крепёж входит в свою деталь, вал мотора — в его подшипники; неподвижные пары достаточно проверить один раз.
                        if ((a.IsFastener || b.IsFastener) && a.Part is not null && a.Part == b.Part
                            || motor is not null && a.Visual == motor.Stator && b.Visual == motor.Rotor
                            || a.IsStatic && b.IsStatic && s > 0)
                        {
                            continue;
                        }

                        (double volume, Rect3D where) = Overlap(a, b);
                        Hit hit = worst.GetValueOrDefault((i, j), new Hit(0, double.MaxValue, 0, Rect3D.Empty));
                        worst[(i, j)] = volume > hit.Max
                            ? new Hit(volume, Math.Min(hit.Min, volume), q, where)
                            : hit with { Min = Math.Min(hit.Min, volume) };
                    }
                }
            }

            var errors = new List<string>();
            foreach (((int i, int j), Hit w) in worst.Where(p => p.Value.Max > 0).OrderByDescending(p => p.Value.Max))
            {
                Body a = bodies[i], b = bodies[j];
                Rect3D r = w.Where;
                string text = $"{a.Name} × {b.Name}: {w.Max:0.##} мм³ (наименьшее {w.Min:0.##}) при q = {w.AtQ:0.###}, " +
                    $"область [{r.X:0.#}; {r.Y:0.#}; {r.Z:0.#}]…[{r.X + r.SizeX:0.#}; {r.Y + r.SizeY:0.#}; {r.Z + r.SizeZ:0.#}]";
                output.WriteLine(text);

                // Конструктивное пересечение постоянно; если оно растёт по ходу — туда заходит посторонняя часть детали.
                bool bad = IsDesigned(a, b) ? w.Max - w.Min > 0.25 * w.Max + 2 * Noise : w.Max > Noise;
                if (bad)
                {
                    errors.Add(text);
                }
            }

            Assert.True(errors.Count == 0, $"{id}: детали проходят друг сквозь друга:\n" + string.Join("\n", errors));
        });
    }

    private readonly record struct Hit(double Max, double Min, double AtQ, Rect3D Where);

    private static (double, double) Sweep(MechanismDefinition m, MechanismSimulator sim)
    {
        if (sim.Range is { } r)
        {
            return (r.Min, r.Max);
        }

        // Полный оборот самой медленной вращающейся детали.
        double slowest = 1.0;
        var kin = sim.Kinematics;
        var state = kin.Solve(0.1);
        for (int i = 1; i < kin.Parts.Count; i++)
        {
            if (kin.Parts[i].Joint.Type == "revolute" && m.Couplings.Any(c => c.To == kin.Parts[i].Id && c.Type == "gear"))
            {
                slowest = Math.Max(slowest, 1.0 / Math.Abs(state[i].K));
            }
        }

        return (0, 2 * Math.PI * slowest);
    }

    private static List<(Point3D, Point3D, Point3D)> Triangles(Model3D? model, Matrix3D parent)
    {
        var list = new List<(Point3D, Point3D, Point3D)>();
        if (model is null)
        {
            return list;
        }

        Matrix3D m = model.Transform is { } t ? t.Value * parent : parent;
        if (model is Model3DGroup group)
        {
            foreach (Model3D child in group.Children)
            {
                list.AddRange(Triangles(child, m));
            }
        }
        else if (model is GeometryModel3D { Geometry: MeshGeometry3D mesh })
        {
            var pts = mesh.Positions;
            var idx = mesh.TriangleIndices;
            if (idx.Count == 0)
            {
                for (int k = 0; k + 2 < pts.Count; k += 3)
                {
                    list.Add((m.Transform(pts[k]), m.Transform(pts[k + 1]), m.Transform(pts[k + 2])));
                }
            }
            else
            {
                for (int k = 0; k + 2 < idx.Count; k += 3)
                {
                    list.Add((m.Transform(pts[idx[k]]), m.Transform(pts[idx[k + 1]]), m.Transform(pts[idx[k + 2]])));
                }
            }
        }

        return list;
    }

    private static (double, Rect3D) Overlap(Body a, Body b)
    {
        Rect3D box = Rect3D.Intersect(a.WorldBox, b.WorldBox);
        if (box.IsEmpty || box.SizeX <= 0 || box.SizeY <= 0 || box.SizeZ <= 0)
        {
            return (0, Rect3D.Empty);
        }

        int count = 0;
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        for (double x = box.X + Cell / 2; x < box.X + box.SizeX; x += Cell)
        {
            for (double y = box.Y + Cell / 2; y < box.Y + box.SizeY; y += Cell)
            {
                for (double z = box.Z + Cell / 2; z < box.Z + box.SizeZ; z += Cell)
                {
                    var p = new Point3D(x, y, z);
                    if (a.Contains(p) && b.Contains(p))
                    {
                        count++;
                        minX = Math.Min(minX, x); minY = Math.Min(minY, y); minZ = Math.Min(minZ, z);
                        maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); maxZ = Math.Max(maxZ, z);
                    }
                }
            }
        }

        return count == 0
            ? (0, Rect3D.Empty)
            : (count * Cell * Cell * Cell, new Rect3D(minX, minY, minZ, maxX - minX, maxY - minY, maxZ - minZ));
    }

    /// <summary>Тело в своей системе координат: воксельная карта «внутри» по числу оборотов (winding) вдоль Z.</summary>
    private sealed class Body(string name, string? part, bool isFastener, bool isStatic, ModelVisual3D visual, List<(Point3D A, Point3D B, Point3D C)> tris)
    {
        private const double Step = Cell / 2;
        private BitArray _inside = new(0);
        private double _x0, _y0, _z0;
        private int _nx, _ny, _nz;
        private Matrix3D _toLocal;
        private Rect3D _localBox;

        public string Name { get; } = name;
        public string? Part { get; } = part;
        public bool IsFastener { get; } = isFastener;
        public bool IsStatic { get; } = isStatic;
        public ModelVisual3D Visual { get; } = visual;
        public Rect3D WorldBox { get; private set; }

        public void Build()
        {
            if (tris.Count == 0)
            {
                return;
            }

            double x0 = tris.Min(t => Math.Min(t.A.X, Math.Min(t.B.X, t.C.X))), x1 = tris.Max(t => Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)));
            double y0 = tris.Min(t => Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y))), y1 = tris.Max(t => Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y)));
            double z0 = tris.Min(t => Math.Min(t.A.Z, Math.Min(t.B.Z, t.C.Z))), z1 = tris.Max(t => Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z)));
            _localBox = new Rect3D(x0, y0, z0, x1 - x0, y1 - y0, z1 - z0);
            _x0 = x0 + 1.234e-4; _y0 = y0 + 2.345e-4; _z0 = z0;
            _nx = (int)Math.Ceiling((x1 - x0) / Step) + 1;
            _ny = (int)Math.Ceiling((y1 - y0) / Step) + 1;
            _nz = (int)Math.Ceiling((z1 - z0) / Step) + 1;
            _inside = new BitArray(_nx * _ny * _nz);

            var columns = new List<(double Z, int Sign)>?[_nx * _ny];
            foreach (var (a, b, c) in tris)
            {
                double area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
                if (Math.Abs(area) < 1e-12)
                {
                    continue;
                }

                int sign = area > 0 ? -1 : 1;
                int i0 = Math.Max(0, (int)Math.Floor((Math.Min(a.X, Math.Min(b.X, c.X)) - _x0) / Step));
                int i1 = Math.Min(_nx - 1, (int)Math.Ceiling((Math.Max(a.X, Math.Max(b.X, c.X)) - _x0) / Step));
                int j0 = Math.Max(0, (int)Math.Floor((Math.Min(a.Y, Math.Min(b.Y, c.Y)) - _y0) / Step));
                int j1 = Math.Min(_ny - 1, (int)Math.Ceiling((Math.Max(a.Y, Math.Max(b.Y, c.Y)) - _y0) / Step));
                for (int i = i0; i <= i1; i++)
                {
                    double px = _x0 + i * Step;
                    for (int j = j0; j <= j1; j++)
                    {
                        double py = _y0 + j * Step;
                        double w0 = ((b.X - px) * (c.Y - py) - (c.X - px) * (b.Y - py)) / area;
                        double w1 = ((c.X - px) * (a.Y - py) - (a.X - px) * (c.Y - py)) / area;
                        double w2 = 1 - w0 - w1;
                        if (w0 < 0 || w1 < 0 || w2 < 0)
                        {
                            continue;
                        }

                        double z = w0 * a.Z + w1 * b.Z + w2 * c.Z;
                        (columns[i * _ny + j] ??= new()).Add((z, sign));
                    }
                }
            }

            for (int col = 0; col < columns.Length; col++)
            {
                if (columns[col] is not { } list)
                {
                    continue;
                }

                list.Sort((p, q) => p.Z.CompareTo(q.Z));
                int winding = 0, next = 0;
                for (int k = 0; k < _nz; k++)
                {
                    double z = _z0 + k * Step;
                    while (next < list.Count && list[next].Z <= z)
                    {
                        winding += list[next++].Sign;
                    }

                    if (winding != 0)
                    {
                        _inside[col * _nz + k] = true;
                    }
                }
            }
        }

        public void SetPose(Matrix3D pose)
        {
            _toLocal = pose;
            _toLocal.Invert();
            WorldBox = tris.Count == 0 ? Rect3D.Empty : Transform(_localBox, pose);
        }

        public bool Contains(Point3D world)
        {
            Point3D p = _toLocal.Transform(world);
            int i = (int)Math.Round((p.X - _x0) / Step), j = (int)Math.Round((p.Y - _y0) / Step), k = (int)Math.Round((p.Z - _z0) / Step);
            return i >= 0 && j >= 0 && k >= 0 && i < _nx && j < _ny && k < _nz && _inside[(i * _ny + j) * _nz + k];
        }

        private static Rect3D Transform(Rect3D r, Matrix3D m)
        {
            var result = Rect3D.Empty;
            for (int c = 0; c < 8; c++)
            {
                var p = new Point3D(r.X + ((c & 1) != 0 ? r.SizeX : 0), r.Y + ((c & 2) != 0 ? r.SizeY : 0), r.Z + ((c & 4) != 0 ? r.SizeZ : 0));
                result.Union(m.Transform(p));
            }

            return result;
        }
    }
}
