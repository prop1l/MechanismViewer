using MechanismViewer.Catalog;
using MechanismViewer.Kinematics.Couplings;
using System.IO;
using System.Windows;
using System.Windows.Media.Media3D;

namespace MechanismViewer.Visuals;

/// <summary>
/// Строит сетку детали из примитивов <see cref="PartSpec.Shape"/> (мм, система детали).
/// Каждый примитив — замкнутое тело с наружными нормалями; пересекающиеся тела не сливаются,
/// как в STL-заготовках, — для показа этого достаточно. Грани плоские: у каждого треугольника свои вершины.
/// </summary>
public static class ShapeBuilder
{
    /// <summary>Боковой зазор на каждую сторону зуба, доля модуля: прямые боковины трапеции вместо эвольвенты иначе врезаются в ответный зуб.</summary>
    public const double Backlash = 0.12;

    private static readonly double Tan20 = Math.Tan(20 * Math.PI / 180);

    public static MeshGeometry3D Build(MechanismDefinition mechanism, PartSpec part)
    {
        IReadOnlyList<ShapeSpec> shape = part.Shape
            ?? throw new InvalidDataException($"Деталь «{part.Id}»: нет shape");

        var mesh = new Builder(part.ShapeAxis);
        foreach (ShapeSpec s in shape)
        {
            if (s.Box is [double x0, double y0, double z0, double x1, double y1, double z1])
            {
                mesh.Box(x0, y0, z0, x1, y1, z1);
            }
            else if (s.Cylinder is { } c)
            {
                mesh.Cylinder(c.At[0], c.At[1], c.R, c.Z[0], c.Z[1], c.Segments);
            }
            else if (s.Polygon is { } p)
            {
                mesh.Prism(p.Points.Select(v => new Point(v[0], v[1])).ToList(), p.Z[0], p.Z[1]);
            }
            else if (s.Gear is { } g)
            {
                mesh.Gear(g.At[0], g.At[1], g.Module, g.Teeth, g.PhaseDeg, g.Z[0], g.Z[1]);
            }
            else if (s.Rack is { } r)
            {
                mesh.RackTeeth(r.Origin[0], r.Origin[1], r.Direction[0], r.Direction[1], r.Module, r.Teeth[0], r.Teeth[1], r.Z[0], r.Z[1]);
            }
            else if (s.RingX is { } ring)
            {
                mesh.RingX(ring.X[0], ring.X[1], ring.R[0], ring.R[1], ring.Segments);
            }
            else if (s.Thread is { } t)
            {
                mesh.Thread(t.R[0], t.R[1], t.Pitch, t.Z[0], t.Z[1], t.Width[0], t.Width[1], t.PerTurn);
            }
            else if (s.Cam is { } cam)
            {
                mesh.Prism(CamOutline(mechanism, part, cam.Samples), cam.Z[0], cam.Z[1]);
            }
            else if (s.GenevaStar is { } star)
            {
                mesh.Prism(Geneva(star).Star, star.Z[0], star.Z[1]);
            }
            else if (s.GenevaDisk is { } disk)
            {
                mesh.Prism(Geneva(disk).Disk, disk.Z[0], disk.Z[1]);
            }
            else
            {
                throw new InvalidDataException($"Деталь «{part.Id}»: пустой или неполный примитив shape");
            }
        }

        return mesh.ToMesh();
    }

    /// <summary>
    /// Контур кулачка — огибающая положений тарелки толкателя: H(ψ)·n(ψ) + H'(ψ)·t(ψ), H = r_b + s(θ), θ = γ − ψ.
    /// γ — направление на толкатель, ψ — угол в системе кулачка.
    /// </summary>
    private static List<Point> CamOutline(MechanismDefinition mechanism, PartSpec part, int samples)
    {
        CouplingSpec spec = mechanism.Couplings.FirstOrDefault(c => c.Type == "cam" && c.From == part.Id)
            ?? throw new InvalidDataException($"Деталь «{part.Id}»: shape cam требует связи cam от этой детали");
        PartSpec follower = mechanism.Part(spec.To);
        double theta0 = part.Joint.Phase * Math.PI / 180.0;
        var law = new CamCoupling(part.Id, follower.Id,
            (spec.Segments ?? []).Select(s => (s.AngleDeg * Math.PI / 180.0, s.RiseMm * 1e-3, s.Law)).ToList(), theta0);

        double dx = follower.Joint.Origin[0] - part.Joint.Origin[0], dy = follower.Joint.Origin[1] - part.Joint.Origin[1];
        double rb = Math.Sqrt(dx * dx + dy * dy), gamma = Math.Atan2(dy, dx);

        var outline = new List<Point>(samples);
        for (int i = 0; i < samples; i++)
        {
            double psi = 2 * Math.PI * i / samples;
            double q = gamma - psi - theta0;
            double h = rb + law.Evaluate(q) * 1e3, v = law.Derivative(q) * 1e3;
            outline.Add(new Point(h * Math.Cos(psi) + v * Math.Sin(psi), h * Math.Sin(psi) - v * Math.Cos(psi)));
        }

        return outline;
    }

    /// <summary>Контуры креста и запирающего диска: палец на r = C·sin(π/n), дно паза на 0,3 мм глубже пальца.</summary>
    private static (List<Point> Star, List<Point> Disk) Geneva(GenevaShape g)
    {
        int n = g.Slots;
        double c = g.CenterDistance, w = g.SlotWidth, pinR = g.PinRadius;
        double r = c * Math.Sin(Math.PI / n);
        double rStar = c * Math.Cos(Math.PI / n);
        double bottom = c - r - pinR - 0.3 + w / 2;
        double uo = Math.Sqrt(rStar * rStar - w / 2 * (w / 2));

        static Point Slot(double angle, double u, double v) =>
            new(u * Math.Cos(angle) - v * Math.Sin(angle), u * Math.Sin(angle) + v * Math.Cos(angle));

        var star = new List<Point>();
        double lockR = 0;
        for (int k = 0; k < n; k++)
        {
            double s = Math.PI / n + 2 * Math.PI * k / n;
            star.Add(Slot(s, uo, -w / 2));
            for (int i = 0; i <= 12; i++)
            {
                double t = -Math.PI / 2 - Math.PI * i / 12;   // дно паза: от v = −w/2 через u_min к v = +w/2
                star.Add(Slot(s, bottom + w / 2 * Math.Cos(t), w / 2 * Math.Sin(t)));
            }
            star.Add(Slot(s, uo, w / 2));

            // Вогнутая запирающая дуга до следующего паза: центр — ось ведущего диска в положении выстоя.
            double mid = s + Math.PI / n;
            var center = new Point(c * Math.Cos(mid), c * Math.Sin(mid));
            Point a = Slot(s, uo, w / 2), b = Slot(s + 2 * Math.PI / n, uo, -w / 2);
            lockR = (a - center).Length;
            double a1 = Math.Atan2(a.Y - center.Y, a.X - center.X);
            double a2 = Math.Atan2(b.Y - center.Y, b.X - center.X);
            double sweep = Math.IEEERemainder(a2 - a1, 2 * Math.PI);
            for (int i = 1; i < 30; i++)
            {
                double ang = a1 + sweep * i / 30;
                star.Add(new Point(center.X + lockR * Math.Cos(ang), center.Y + lockR * Math.Sin(ang)));
            }
        }

        // Диск с выемкой напротив пальца: там проходят лучи креста.
        double diskR = lockR - 0.4, cutR = rStar + 0.6;
        var disk = new List<Point>();
        for (int i = 0; i < 360; i++)
        {
            double phi = 2 * Math.PI * i / 360;
            double ux = Math.Cos(phi), uy = Math.Sin(phi), rho = diskR;
            double bproj = c * ux, disc = bproj * bproj - (c * c - cutR * cutR);
            if (disc > 0)
            {
                double t1 = bproj - Math.Sqrt(disc);
                if (t1 > 0 && t1 < rho) rho = t1;
            }
            disk.Add(new Point(ux * rho, uy * rho));
        }

        return (star, disk);
    }

    private sealed class Builder(string axis)
    {
        private readonly Point3DCollection _points = new();

        private void Tri(Point3D a, Point3D b, Point3D c)
        {
            if (axis == "y")
            {
                // Поворот на 90° вокруг X: (x, y, z) → (x, z, −y) — ось выдавливания вверх, резьба остаётся правой.
                _points.Add(new Point3D(a.X, a.Z, -a.Y));
                _points.Add(new Point3D(b.X, b.Z, -b.Y));
                _points.Add(new Point3D(c.X, c.Z, -c.Y));
            }
            else if (axis == "x")
            {
                // Перенос Z → X — зеркальное отображение: порядок вершин меняется, чтобы нормали остались наружу.
                _points.Add(new Point3D(a.Z, a.Y, a.X));
                _points.Add(new Point3D(c.Z, c.Y, c.X));
                _points.Add(new Point3D(b.Z, b.Y, b.X));
            }
            else
            {
                _points.Add(a);
                _points.Add(b);
                _points.Add(c);
            }
        }

        public MeshGeometry3D ToMesh() => new() { Positions = _points };

        public void Box(double x0, double y0, double z0, double x1, double y1, double z1) =>
            Prism([new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)], z0, z1);

        public void Cylinder(double cx, double cy, double r, double z0, double z1, int n)
        {
            var pts = new List<Point>(n);
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n;
                pts.Add(new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
            }

            Prism(pts, z0, z1);
        }

        /// <summary>Простой многоугольник, выдавленный по Z; крышки — разбиение «отрезанием ушей».</summary>
        public void Prism(IReadOnlyList<Point> pts, double z0, double z1)
        {
            foreach (var (a, b, c) in Triangulate(pts))
            {
                Tri(new(c.X, c.Y, z0), new(b.X, b.Y, z0), new(a.X, a.Y, z0));
                Tri(new(a.X, a.Y, z1), new(b.X, b.Y, z1), new(c.X, c.Y, z1));
            }

            // Боковины по обходу против часовой — нормали наружу.
            bool ccw = Area(pts) >= 0;
            for (int i = 0; i < pts.Count; i++)
            {
                Point p = pts[i], q = pts[(i + 1) % pts.Count];
                if (!ccw) (p, q) = (q, p);
                Tri(new(p.X, p.Y, z0), new(q.X, q.Y, z0), new(q.X, q.Y, z1));
                Tri(new(p.X, p.Y, z0), new(q.X, q.Y, z1), new(p.X, p.Y, z1));
            }
        }

        public void Gear(double cx, double cy, double m, int z, double phaseDeg, double z0, double z1)
        {
            double r = m * z / 2, rf = r - 1.25 * m, ra = r + m;
            Cylinder(cx, cy, rf, z0, z1, Math.Max(48, 4 * z));
            for (int k = 0; k < z; k++)
            {
                double a = (phaseDeg + 360.0 * k / z) * Math.PI / 180;
                var u = new Vector(Math.Cos(a), Math.Sin(a));
                var v = new Vector(-u.Y, u.X);
                var c = new Point(cx, cy);
                double rho0 = rf - 0.3;
                double half = Math.PI * m / 4 - Backlash * m;
                double h0 = half + (r - rho0) * Tan20, h1 = Math.Max(0.15 * m, half - (ra - r) * Tan20);
                Prism([c + u * rho0 - v * h0, c + u * ra - v * h1, c + u * ra + v * h1, c + u * rho0 + v * h0], z0, z1);
            }
        }

        public void RackTeeth(double ox, double oy, double sx, double sy, double m, int kFrom, int kTo, double z0, double z1)
        {
            var o = new Point(ox, oy);
            var s = new Vector(sx, sy);
            var n = new Vector(-sy, sx);
            double n0 = -1.25 * m - 0.3, n1 = m;
            double half = Math.PI * m / 4 - Backlash * m;
            double h0 = half - n0 * Tan20, h1 = half - n1 * Tan20;

            Point P(double along, double normal) => o + s * along + n * normal;

            for (int k = kFrom; k <= kTo; k++)
            {
                double at = k * Math.PI * m;
                Prism([P(at - h0, n0), P(at + h0, n0), P(at + h1, n1), P(at - h1, n1)], z0, z1);
            }
        }

        public void RingX(double x0, double x1, double rin, double rout, int n)
        {
            static Point3D P(double x, double r, double a) => new(x, r * Math.Cos(a), r * Math.Sin(a));

            for (int i = 0; i < n; i++)
            {
                double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
                Point3D o00 = P(x0, rout, a0), o01 = P(x0, rout, a1), o10 = P(x1, rout, a0), o11 = P(x1, rout, a1);
                Point3D i00 = P(x0, rin, a0), i01 = P(x0, rin, a1), i10 = P(x1, rin, a0), i11 = P(x1, rin, a1);
                Tri(o00, o01, o11); Tri(o00, o11, o10);
                Tri(i00, i11, i01); Tri(i00, i10, i11);
                Tri(o00, i01, o01); Tri(o00, i00, i01);
                Tri(o10, o11, i11); Tri(o10, i11, i10);
            }
        }

        public void Thread(double rRoot, double rTip, double pitch, double z0, double z1, double wRoot, double wTip, int perTurn)
        {
            int count = (int)Math.Ceiling((z1 - z0) / pitch * perTurn);
            Point3D[] Section(int i)
            {
                double t = 2 * Math.PI * i / perTurn, z = z0 + pitch * i / perTurn;
                double cx = Math.Cos(t), cy = Math.Sin(t), r0 = rRoot - 0.2;
                return
                [
                    new(cx * r0, cy * r0, z - wRoot / 2),
                    new(cx * rTip, cy * rTip, z - wTip / 2),
                    new(cx * rTip, cy * rTip, z + wTip / 2),
                    new(cx * r0, cy * r0, z + wRoot / 2)
                ];
            }

            Point3D[] prev = Section(0);
            Tri(prev[0], prev[2], prev[1]); Tri(prev[0], prev[3], prev[2]);
            for (int i = 1; i <= count; i++)
            {
                Point3D[] next = Section(i);
                for (int k = 0; k < 4; k++)
                {
                    int k1 = (k + 1) % 4;
                    Tri(prev[k], next[k], next[k1]);
                    Tri(prev[k], next[k1], prev[k1]);
                }
                prev = next;
            }
            Tri(prev[0], prev[1], prev[2]); Tri(prev[0], prev[2], prev[3]);
        }

        private static IEnumerable<(Point, Point, Point)> Triangulate(IReadOnlyList<Point> pts)
        {
            var idx = Enumerable.Range(0, pts.Count).ToList();
            if (Area(pts) < 0) idx.Reverse();
            int guard = 0;
            while (idx.Count > 3 && guard++ < 100000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    Point a = pts[idx[(i + idx.Count - 1) % idx.Count]], b = pts[idx[i]], c = pts[idx[(i + 1) % idx.Count]];
                    if (Cross(b - a, c - b) <= 1e-9) continue;
                    bool inside = false;
                    foreach (int j in idx)
                    {
                        Point p = pts[j];
                        if (p == a || p == b || p == c) continue;
                        if (Cross(b - a, p - a) > 0 && Cross(c - b, p - b) > 0 && Cross(a - c, p - c) > 0) { inside = true; break; }
                    }
                    if (inside) continue;
                    yield return (a, b, c);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) throw new InvalidDataException("не удалось разбить контур формы на треугольники");
            }
            if (idx.Count == 3) yield return (pts[idx[0]], pts[idx[1]], pts[idx[2]]);
        }

        private static double Cross(Vector a, Vector b) => a.X * b.Y - a.Y * b.X;

        private static double Area(IReadOnlyList<Point> p)
        {
            double s = 0;
            for (int i = 0; i < p.Count; i++) s += Cross((Vector)p[i], (Vector)p[(i + 1) % p.Count]);
            return s / 2;
        }
    }
}
