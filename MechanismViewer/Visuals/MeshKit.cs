using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace MechanismViewer.Visuals;

/// <summary>
/// Локальная система координат детали: точка (x, y, z) → Origin + x·X + y·Y + z·Z.
/// Оси должны образовывать правую ортонормированную тройку.
/// </summary>
public readonly record struct Frame(Point3D Origin, Vector3D X, Vector3D Y, Vector3D Z)
{
    public static Frame World { get; } = new(new Point3D(), new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, 1));

    /// <summary>Система с осью Z вдоль <paramref name="axis"/> и началом в <paramref name="origin"/>.</summary>
    public static Frame Along(Point3D origin, Vector3D axis)
    {
        axis.Normalize();
        Vector3D helper = Math.Abs(axis.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
        Vector3D x = Vector3D.CrossProduct(helper, axis);
        x.Normalize();
        return new Frame(origin, x, Vector3D.CrossProduct(axis, x), axis);
    }

    public Point3D P(double x, double y, double z) => Origin + x * X + y * Y + z * Z;

    public Vector3D V(double x, double y, double z) => x * X + y * Y + z * Z;
}

/// <summary>
/// Профиль тела вращения в координатах (z — вдоль оси, r — радиус).
/// Профиль обходится от задней части к передней — тогда нормали смотрят наружу.
/// <see cref="Hard"/> создаёт острое ребро (вершина дублируется), <see cref="Smooth"/> — гладкий переход.
/// </summary>
public sealed class Profile
{
    internal List<Point> Points { get; } = new();

    public Profile Hard(double z, double r)
    {
        Points.Add(new Point(z, r));
        Points.Add(new Point(z, r));
        return this;
    }

    public Profile Smooth(double z, double r)
    {
        Points.Add(new Point(z, r));
        return this;
    }

    /// <summary>Дуга скругления: центр (cz, cr), углы в градусах (0° — вдоль +z, 90° — вдоль +r).</summary>
    public Profile Arc(double cz, double cr, double radius, double fromDeg, double toDeg, int segments = 6)
    {
        for (int k = 0; k <= segments; k++)
        {
            double a = (fromDeg + (toDeg - fromDeg) * k / segments) * Math.PI / 180.0;
            Points.Add(new Point(cz + radius * Math.Cos(a), cr + radius * Math.Sin(a)));
        }

        return this;
    }
}

/// <summary>
/// Построитель сетки для WPF с правильными нормалями: гладкие поверхности сглаживаются,
/// а там, где вершина продублирована, остаётся острое ребро.
/// </summary>
public sealed class MeshKit
{
    private const double CoincideEps = 1e-9;
    private const double PoleRadius = 1e-4;

    private readonly MeshGeometry3D _mesh = new();

    private int AddVertex(Point3D p, Vector3D n)
    {
        _mesh.Positions.Add(p);
        _mesh.Normals.Add(n);
        return _mesh.Positions.Count - 1;
    }

    private void AddTriangle(int a, int b, int c)
    {
        Point3D pa = _mesh.Positions[a], pb = _mesh.Positions[b], pc = _mesh.Positions[c];
        if (Vector3D.CrossProduct(pb - pa, pc - pa).LengthSquared < 1e-14)
        {
            return; // вырожденный треугольник на остром ребре
        }

        _mesh.TriangleIndices.Add(a);
        _mesh.TriangleIndices.Add(b);
        _mesh.TriangleIndices.Add(c);
    }

    /// <summary>
    /// Прямоугольная сетка точек [строка, столбец]. Нормаль = (d/dстолбец) × (d/dстрока).
    /// Совпадающие соседние точки задают острое ребро.
    /// </summary>
    public void Grid(Point3D[,] pts, bool closedColumns)
    {
        int rows = pts.GetLength(0), cols = pts.GetLength(1);
        var index = new int[rows, cols];

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                Point3D p = pts[r, c];

                Vector3D du = Neighbour(r, c, 0, +1) - Neighbour(r, c, 0, -1);
                Vector3D dv = Neighbour(r, c, +1, 0) - Neighbour(r, c, -1, 0);
                Vector3D n = Vector3D.CrossProduct(du, dv);
                if (n.LengthSquared > 1e-30)
                {
                    n.Normalize();
                }

                index[r, c] = AddVertex(p, n);
            }
        }

        int lastColumn = closedColumns ? cols : cols - 1;
        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < lastColumn; c++)
            {
                int c1 = (c + 1) % cols;
                AddTriangle(index[r, c], index[r, c1], index[r + 1, c]);
                AddTriangle(index[r, c1], index[r + 1, c1], index[r + 1, c]);
            }
        }

        Point3D Neighbour(int r, int c, int dr, int dc)
        {
            int nr = r + dr, nc = c + dc;
            if (closedColumns)
            {
                nc = (nc + cols) % cols;
            }

            if (nr < 0 || nr >= rows || nc < 0 || nc >= cols)
            {
                return pts[r, c];
            }

            Point3D q = pts[nr, nc];
            return (q - pts[r, c]).LengthSquared < CoincideEps ? pts[r, c] : q;
        }
    }

    /// <summary>Тело вращения вокруг оси Z системы <paramref name="f"/>.</summary>
    public MeshKit Lathe(Frame f, Profile profile, int segments = 64)
    {
        List<Point> prof = profile.Points;
        var pts = new Point3D[prof.Count, segments];

        for (int r = 0; r < prof.Count; r++)
        {
            double radius = Math.Max(prof[r].Y, PoleRadius);
            for (int c = 0; c < segments; c++)
            {
                double a = 2 * Math.PI * c / segments;
                pts[r, c] = f.P(radius * Math.Cos(a), radius * Math.Sin(a), prof[r].X);
            }
        }

        Grid(pts, true);
        return this;
    }

    /// <summary>
    /// Призма вдоль оси Z системы: набор колец (z, контур). Все контуры — одинаковой длины,
    /// обход против часовой стрелки; строки идут от задней части к передней.
    /// </summary>
    public MeshKit Loft(Frame f, IReadOnlyList<(double Z, IReadOnlyList<Point> Outline)> rings)
    {
        int cols = rings[0].Outline.Count;
        var pts = new Point3D[rings.Count, cols];

        for (int r = 0; r < rings.Count; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                Point q = rings[r].Outline[c];
                pts[r, c] = f.P(q.X, q.Y, rings[r].Z);
            }
        }

        Grid(pts, true);
        return this;
    }

    /// <summary>Плоская крышка по контуру (обход против часовой), нормаль ±Z системы.</summary>
    public MeshKit Cap(Frame f, IReadOnlyList<Point> outline, double z, bool facesForward)
    {
        Vector3D n = facesForward ? f.Z : -f.Z;
        double cx = outline.Average(p => p.X), cy = outline.Average(p => p.Y);
        int center = AddVertex(f.P(cx, cy, z), n);
        int first = _mesh.Positions.Count;

        foreach (Point p in outline)
        {
            AddVertex(f.P(p.X, p.Y, z), n);
        }

        for (int k = 0; k < outline.Count; k++)
        {
            int a = first + k, b = first + (k + 1) % outline.Count;
            if (facesForward)
            {
                AddTriangle(center, a, b);
            }
            else
            {
                AddTriangle(center, b, a);
            }
        }

        return this;
    }

    /// <summary>Выпуклая призма с плоскими гранями: контур в плоскости XY, выдавлен от z0 до z1.</summary>
    public MeshKit Prism(Frame f, IReadOnlyList<Point> outline, double z0, double z1)
    {
        Cap(f, outline, z0, false);
        Cap(f, outline, z1, true);

        for (int k = 0; k < outline.Count; k++)
        {
            Point a = outline[k], b = outline[(k + 1) % outline.Count];
            Vector3D n = Vector3D.CrossProduct(f.V(b.X - a.X, b.Y - a.Y, 0), f.Z);
            if (n.LengthSquared < 1e-18)
            {
                continue;
            }

            n.Normalize();
            int i0 = AddVertex(f.P(a.X, a.Y, z0), n);
            int i1 = AddVertex(f.P(b.X, b.Y, z0), n);
            int i2 = AddVertex(f.P(b.X, b.Y, z1), n);
            int i3 = AddVertex(f.P(a.X, a.Y, z1), n);
            AddTriangle(i0, i1, i2);
            AddTriangle(i0, i2, i3);
        }

        return this;
    }

    /// <summary>Параллелепипед с центром в (x, y, z) системы и размерами sx×sy×sz.</summary>
    public MeshKit Box(Frame f, double x, double y, double z, double sx, double sy, double sz)
    {
        double hx = sx / 2, hy = sy / 2;
        var outline = new[]
        {
            new Point(x - hx, y - hy), new Point(x + hx, y - hy),
            new Point(x + hx, y + hy), new Point(x - hx, y + hy)
        };
        return Prism(f, outline, z - sz / 2, z + sz / 2);
    }

    /// <summary>
    /// Пластина толщиной z0…z1 с контуром <paramref name="outline"/> (против часовой)
    /// и круглым отверстием радиуса <paramref name="holeRadius"/> в (hx, hy).
    /// Контур должен быть звёздным относительно центра отверстия; radius = 0 — без отверстия.
    /// </summary>
    public MeshKit PlateWithHole(Frame f, IReadOnlyList<Point> outline, double hx, double hy,
        double holeRadius, double z0, double z1)
    {
        double hr = Math.Max(holeRadius, PoleRadius);
        var hole = outline
            .Select(p => Math.Atan2(p.Y - hy, p.X - hx))
            .Select(a => new Point(hx + hr * Math.Cos(a), hy + hr * Math.Sin(a)))
            .ToList();
        var outer = outline.ToList();

        // Сечение обходится: тыльная грань → торец → лицевая грань → стенка отверстия.
        var rings = new List<(double, IReadOnlyList<Point>)>
        {
            (z0, hole), (z0, outer), (z0, outer), (z1, outer), (z1, outer), (z1, hole)
        };
        if (holeRadius > 0)
        {
            rings.Add((z1, hole));
            rings.Add((z0, hole));
            rings.Add((z0, hole));
        }

        return Loft(f, rings);
    }

    /// <summary>Гибкая трубка (провод, вывод) по сглаженному пути с плоскими торцами.</summary>
    public MeshKit Tube(IReadOnlyList<Point3D> controlPoints, double radius, int segments = 16, int samplesPerSpan = 8)
    {
        List<Point3D> path = CatmullRom(controlPoints, samplesPerSpan);

        // Параллельный перенос базиса вдоль пути — трубка не перекручивается.
        Vector3D t = path[1] - path[0];
        t.Normalize();
        Frame start = Frame.Along(path[0], t);
        Vector3D nrm = start.X;

        var pts = new Point3D[path.Count + 4, segments];
        var cols = new List<Point3D[]>();

        for (int i = 0; i < path.Count; i++)
        {
            Vector3D tangent = i < path.Count - 1 ? path[i + 1] - path[Math.Max(i - 1, 0)] : path[i] - path[i - 1];
            tangent.Normalize();
            nrm -= Vector3D.DotProduct(nrm, tangent) * tangent;
            nrm.Normalize();
            Vector3D bin = Vector3D.CrossProduct(tangent, nrm);

            var ring = new Point3D[segments];
            for (int c = 0; c < segments; c++)
            {
                double a = 2 * Math.PI * c / segments;
                ring[c] = path[i] + radius * (Math.Cos(a) * nrm + Math.Sin(a) * bin);
            }

            cols.Add(ring);
        }

        // Торцы: центр → кольцо (острое ребро) → боковая поверхность → кольцо → центр.
        // Ориентация: (угол против часовой вокруг касательной) × (вдоль пути) смотрит наружу.
        // «Центр» — крошечное кольцо, чтобы нормаль на полюсе считалась.
        for (int c = 0; c < segments; c++)
        {
            pts[0, c] = path[0] + (cols[0][c] - path[0]) * (PoleRadius / radius);
            pts[1, c] = cols[0][c];
            for (int i = 0; i < path.Count; i++)
            {
                pts[i + 2, c] = cols[i][c];
            }

            pts[path.Count + 2, c] = cols[^1][c];
            pts[path.Count + 3, c] = path[^1] + (cols[^1][c] - path[^1]) * (PoleRadius / radius);
        }

        Grid(pts, true);
        return this;
    }

    private static List<Point3D> CatmullRom(IReadOnlyList<Point3D> p, int samples)
    {
        var result = new List<Point3D>();
        for (int i = 0; i < p.Count - 1; i++)
        {
            Point3D p0 = p[Math.Max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[Math.Min(i + 2, p.Count - 1)];
            for (int s = 0; s < samples; s++)
            {
                double t = s / (double)samples, t2 = t * t, t3 = t2 * t;
                result.Add(new Point3D(
                    Cr(p0.X, p1.X, p2.X, p3.X), Cr(p0.Y, p1.Y, p2.Y, p3.Y), Cr(p0.Z, p1.Z, p2.Z, p3.Z)));

                double Cr(double a, double b, double c, double d) =>
                    0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3);
            }
        }

        result.Add(p[^1]);
        return result;
    }

    public GeometryModel3D Build(Material material)
    {
        _mesh.Freeze();
        return new GeometryModel3D(_mesh, material) { BackMaterial = material };
    }

    /// <summary>Скруглённый прямоугольник (против часовой), стороны разбиты не крупнее step.</summary>
    public static List<Point> RoundedRect(double x0, double y0, double x1, double y1, double radius,
        double step = 1.5, int arcSegments = 6)
    {
        var pts = new List<Point>();
        (double cx, double cy, double a0)[] corners =
        {
            (x1 - radius, y0 + radius, -90), (x1 - radius, y1 - radius, 0),
            (x0 + radius, y1 - radius, 90), (x0 + radius, y0 + radius, 180)
        };

        for (int k = 0; k < 4; k++)
        {
            var (cx, cy, a0) = corners[k];
            for (int s = 0; s <= arcSegments; s++)
            {
                double a = (a0 + 90.0 * s / arcSegments) * Math.PI / 180;
                pts.Add(new Point(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a)));
            }

            Point from = pts[^1];
            var (nx, ny, na) = corners[(k + 1) % 4];
            double nextA = na * Math.PI / 180;
            var to = new Point(nx + radius * Math.Cos(nextA), ny + radius * Math.Sin(nextA));
            int n = Math.Max(1, (int)Math.Ceiling((to - from).Length / step));
            for (int s = 1; s < n; s++)
            {
                pts.Add(from + (to - from) * (s / (double)n));
            }
        }

        return pts;
    }

    /// <summary>
    /// Круг радиуса R, срезанный лысками |y| ≤ F, с отступом внутрь на <paramref name="inset"/>.
    /// Число точек не зависит от отступа — контуры можно соединять в Loft. Углы лысок — острые.
    /// </summary>
    public static List<Point> FlattedCircle(double radius, double flat, double inset, int arcSegments = 20, int flatSegments = 6)
    {
        double r = radius - inset, h = flat - inset;
        double phi = Math.Asin(Math.Clamp(h / r, -1, 1));
        double xc = r * Math.Cos(phi);
        var pts = new List<Point>();

        void ArcPart(double a0, double a1)
        {
            for (int s = 0; s <= arcSegments; s++)
            {
                double a = a0 + (a1 - a0) * s / arcSegments;
                pts.Add(new Point(r * Math.Cos(a), r * Math.Sin(a)));
            }
        }

        void FlatPart(Point from, Point to)
        {
            for (int s = 0; s <= flatSegments; s++)
            {
                pts.Add(from + (to - from) * (s / (double)flatSegments));
            }
        }

        ArcPart(-phi, phi);
        FlatPart(new Point(xc, h), new Point(-xc, h));
        ArcPart(Math.PI - phi, Math.PI + phi);
        FlatPart(new Point(-xc, -h), new Point(xc, -h));
        return pts;
    }

    public static List<Point> Circle(double cx, double cy, double radius, int segments)
    {
        var pts = new List<Point>();
        for (int k = 0; k < segments; k++)
        {
            double a = 2 * Math.PI * k / segments;
            pts.Add(new Point(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a)));
        }

        return pts;
    }

    public static Material Mat(Color diffuse, Color specular, double power)
    {
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(new SolidColorBrush(diffuse)));
        group.Children.Add(new SpecularMaterial(new SolidColorBrush(specular), power));
        group.Freeze();
        return group;
    }
}
