// Генератор STL-заготовок для каталога MechanismViewer (мм, двоичный STL).
// Запуск из корня репозитория:  dotnet run --project tools/StlGen [-- <папка Catalog>] [<id механизма> ...]
// Без аргументов перестраивает все заготовки в MechanismViewer/Catalog.
// Каждая деталь строится в своей системе координат: ось шарнира проходит через начало координат
// (см. joint.origin/axis в mechanism.json рядом), шатуны — от первой шарнирной точки вдоль +X.
using System.Numerics;
using System.Text;

var builders = new Dictionary<string, Action<string>>
{
    ["crank-slider-15-50"] = CrankSlider,
    ["lever-rack-m15"] = LeverRack,
    ["button-rack-pinion"] = ButtonRackPinion,
    ["screw-nut-tr8"] = ScrewNut,
    ["worm-1-20"] = Worm,
    ["four-bar-crank-rocker"] = FourBar,
    ["cam-flat-follower"] = CamFollower,
    ["geneva-4"] = Geneva,
    ["toggle-clamp"] = ToggleClamp,
};

string catalog = args.Length > 0 && Directory.Exists(args[0]) ? args[0] : FindCatalog();
string[] only = args.SkipWhile(a => a == catalog).ToArray();

foreach ((string id, Action<string> build) in builders)
{
    if (only.Length == 0 || only.Contains(id))
    {
        build(Path.Combine(catalog, id));
        Console.WriteLine($"{id}: готово");
    }
}

static string FindCatalog()
{
    for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, "MechanismViewer", "Catalog");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }
    }

    throw new DirectoryNotFoundException("Не найдена папка MechanismViewer/Catalog — укажите её первым аргументом");
}

// ---------------- механизмы ----------------

static void CrankSlider(string dir)
{
    // Кривошип: ось вращения — Z через начало координат, палец на (15, 0).
    Save(dir, "Crank", m =>
    {
        m.Cyl(0, 0, 6, 0, 5);                          // ступица не выше плеча: над ней проходит шатун
        m.Box(0, -4, 0, 15, 4, 5);
        m.Cyl(15, 0, 4, 0, 5);
        m.Cyl(15, 0, 2, 5, 10, 24);
    });
    // Шатун 50 мм: от пальца кривошипа (0, 0) вдоль +X до пальца ползуна (50, 0).
    Save(dir, "Rod", m =>
    {
        m.Box(0, -2.5, 5.5, 50, 2.5, 9);
        m.Cyl(0, 0, 4, 5.5, 9);
        m.Cyl(50, 0, 4, 5.5, 9);
    });
    // Ползун: начало координат — его палец.
    Save(dir, "Slider", m =>
    {
        m.Cyl(0, 0, 2, 5, 10, 24);
        m.Box(-9, -7, 10, 9, 7, 18);
    });
    // Направляющие и основание (неподвижно, мировые координаты).
    Save(dir, "Guide", m =>
    {
        m.Box(25, -13, 0, 85, 13, 2);
        // При x < 40 наклонный шатун (z 5,5…9) выходит за |y| = 7,5 — там рельсы только над ним.
        m.Box(40, 7.5, 2, 85, 10.5, 18);
        m.Box(40, -10.5, 2, 85, -7.5, 18);
        m.Box(25, 7.5, 9.5, 40, 10.5, 18);
        m.Box(25, -10.5, 9.5, 40, -7.5, 18);
    });
}

static void LeverRack(string dir)
{
    // Рычаг с шестерней m1,5 z20 (r = 15). Впадина зубьев снизу (−90°), куда входит зуб рейки: зубья на 9° + 18°·k.
    Save(dir, "Lever", m =>
    {
        m.Gear(0, 0, 1.5, 20, 9, 0, 6);
        m.Cyl(0, 0, 4, -2, 12);
        m.Box(-4, 0, 6, 4, 55, 10);
        m.Cyl(0, 58, 6, 6, 16);
    });
    // Рейка: начало координат на делительной прямой, зубья вверх (+Y), зуб k = 0 на x = 0.
    Save(dir, "Rack", m =>
    {
        m.Box(-40, -10, 0, 40, -1.875, 6);
        m.RackTeeth(0, 0, 1, 0, 1.5, -8, 8, 0, 6);
    });
    Save(dir, "Frame", m =>
    {
        m.Box(-48, -30, -4, 48, -5, -2);
        m.Box(-48, -30, -2, 48, -25, 8);
        m.Box(-48, -25, 6, 48, -21, 8);
        m.Box(-48, -25, -2, 48, -21, 0);
    });
}

static void ButtonRackPinion(string dir)
{
    // Кнопка с толкателем: делительная прямая рейки x = 3, зубья смотрят на шестерню (+X).
    Save(dir, "Button", m =>
    {
        m.Box(-3, 0, 0, 1.75, 30, 6);
        m.RackTeeth(3, 15, 0, -1, 1, -4, 4, 0, 6);
        m.Box(-9, 30, -2, 9, 36, 8);
    });
    // Шестерня m1 z12 со стрелкой вдоль +X. Впадина слева (180°), напротив рейки: зубья на 15° + 30°·k.
    Save(dir, "Pinion", m =>
    {
        m.Gear(0, 0, 1, 12, 15, 0, 6);
        m.Cyl(0, 0, 2.5, -2, 9);
        m.Polygon([new(0, -1.2f), new(16, -0.4f), new(16, 0.4f), new(0, 1.2f)], 6, 8);
    });
    // Пружина вдоль +X длиной 14 мм (окно растягивает её между точками крепления).
    Save(dir, "Spring", m =>
    {
        m.RingX(0, 0.8, 1.5, 3.5);
        for (int i = 1; i <= 6; i++)
        {
            double x = 0.8 + i * 1.77 - 0.9;
            m.RingX(x, x + 0.9, 2.4, 3.4);
        }
        m.RingX(13.2, 14, 1.5, 3.5);
    });
    Save(dir, "Housing", m =>
    {
        m.Box(-12, -18, -4, 32, 30, -2);
        m.Box(-10, -18, -2, 20, -14, 8);
        m.Box(-7, -14, -2, -4, 21.5, 6);               // ниже головки нажатой кнопки (y = 30 − 8)
    });
}

static void ScrewNut(string dir)
{
    // Винт Tr8×2 вдоль +Z от фланца мотора; правая резьба.
    Save(dir, "Screw", m =>
    {
        m.Cyl(0, 0, 2.75, 0, 72);
        m.Thread(2.75, 4.0, 2.0, 2, 70, 1.2, 0.6);
        m.Cyl(0, 0, 2.5, 72, 78);
    });
    // Гайка-каретка: начало координат на оси винта, середина гайки.
    Save(dir, "Nut", m =>
    {
        m.Box(-11, -8, -6, 11, 8, 6);
        m.Box(-4, 8, -6, 4, 18, 6);
        m.Cyl(0, 22, 5.5, -6, 6);
        m.Box(-15, -14, -6, 15, -8, 6);
    });
    Save(dir, "Frame", m =>
    {
        m.Cyl(0, 22, 3, -3.5, 76);                     // направляющая
        m.Box(-20, -24, -3.5, 20, -21.5, 84);          // основание — от стенки кронштейна мотора
        m.Box(-12, -21.5, 74, 12, 30, 82);             // торцевая опора
        m.Box(-6, 16, -3.5, 6, 28, 0);                 // крепление направляющей у мотора
    });
}

static void Worm(string dir)
{
    // Червяк m1,5 однозаходный, ось Z: делительный радиус 7,5, шаг π·m.
    Save(dir, "Worm", m =>
    {
        m.Cyl(0, 0, 5.625, 2, 40);
        // Начало витка — так, чтобы впадина под осью колеса (z = 21) была по центру зуба колеса:
        // на угле 90° середина впадины на z0 + p·(k + 1/4) + p/2.
        double p = Math.PI * 1.5, z0 = 21 - p * (3 + 0.25) - p / 2;
        m.Thread(5.625, 9.0, p, z0, 38, 3.0, 1.2);
        m.Cyl(0, 0, 3, 0, 46);
    });
    // Колесо z20 m1,5, ось X: строится в плоскости XY и переносится (x, y, z) → (z, y, x).
    Save(dir, "Wheel", m =>
    {
        m.Map = p => new Vector3(p.Z, p.Y, p.X);
        m.Gear(0, 0, 1.5, 20, 0, -5, 5);
        m.Cyl(0, 0, 6, -8, 8);
        m.Cyl(0, 0, 3, -15, 15);
        m.Box(-1.5, 0, 5, 1.5, 12, 6.5);               // метка на торце — видно вращение
    });
    Save(dir, "Frame", m =>
    {
        m.Box(-22, -24, -3.5, 22, -21.5, 50);          // основание — от стенки кронштейна мотора
        m.Box(-8, -21.5, 44, 8, 4, 50);                // передняя опора червяка
        m.Box(10, -21.5, 16, 14, 26, 26);              // стойки оси колеса
        m.Box(-14, -21.5, 16, -10, 26, 26);
    });
}

static void FourBar(string dir)
{
    // Кривошип 15, шатун 55, коромысло 35, стойка 60 (оси в (0, 0) и (60, 0)).
    Save(dir, "Crank", m =>
    {
        m.Cyl(0, 0, 6, 0, 5);                          // ступица не выше плеча: над ней проходит шатун
        m.Box(0, -4, 0, 15, 4, 5);
        m.Cyl(15, 0, 4, 0, 5);
        m.Cyl(15, 0, 2, 5, 10, 24);
    });
    Save(dir, "Coupler", m =>
    {
        m.Box(0, -2.5, 5.5, 55, 2.5, 9);
        m.Cyl(0, 0, 4, 5.5, 9);
        m.Cyl(55, 0, 4, 5.5, 9);
    });
    Save(dir, "Rocker", m =>
    {
        m.Cyl(0, 0, 5, 0, 5);
        m.Box(0, -3.5, 0, 35, 3.5, 5);
        m.Cyl(35, 0, 4, 0, 5);
        m.Cyl(35, 0, 2, 5, 10, 24);
        m.Box(-12, -1.5, 0, 0, 1.5, 5);                // хвостовик-указатель
    });
    Save(dir, "Frame", m =>
    {
        m.Box(30, -15, -4, 85, 45, -2);
        m.Cyl(60, 0, 7, -2, 0);
    });
}

static void CamFollower(string dir)
{
    // Кулачок: базовый радиус 15, подъём 12 мм — циклоидальный 150°, выстой 30°, опускание 150°, выстой 30°
    // (тот же закон, что в mechanism.json). Плоский толкатель над кулачком (+Y).
    // Контур — огибающая положений тарелки толкателя.
    const double rb = 15, h = 12;
    double rise = 150 * Math.PI / 180, dwell = 30 * Math.PI / 180;

    (double S, double V) Law(double theta)
    {
        double t = theta % (2 * Math.PI);
        if (t < 0) t += 2 * Math.PI;
        if (t < rise) return Cyc(t / rise, h, rise);
        t -= rise;
        if (t < dwell) return (h, 0);
        t -= dwell;
        if (t < rise) { var (s, v) = Cyc(t / rise, -h, rise); return (h + s, v); }
        return (0, 0);
    }

    static (double, double) Cyc(double x, double hh, double b) =>
        (hh * (x - Math.Sin(2 * Math.PI * x) / (2 * Math.PI)), hh / b * (1 - Math.Cos(2 * Math.PI * x)));

    var outline = new List<Vector2>();
    const int n = 720;
    double gamma = Math.PI / 2;
    for (int i = 0; i < n; i++)
    {
        double psi = 2 * Math.PI * i / n;                // угол в системе кулачка
        double theta = gamma - psi;                      // поворот кулачка, при котором эта сторона под толкателем
        var (s, v) = Law(theta);
        double p = rb + s;
        // Огибающая: H·n(ψ) + H'(ψ)·t(ψ), H' = −s'(θ).
        outline.Add(new Vector2(
            (float)(p * Math.Cos(psi) + v * Math.Sin(psi)),
            (float)(p * Math.Sin(psi) - v * Math.Cos(psi))));
    }

    Save(dir, "Cam", m =>
    {
        m.Polygon(outline, 0, 8);
        m.Cyl(0, 0, 5, -2, 10);
    });
    // Толкатель: начало координат — середина тарелки (контактная плоскость y = 0).
    Save(dir, "Follower", m =>
    {
        m.Box(-14, 0, 0, 14, 3, 8);
        m.Box(-3, 3, 2, 3, 50, 6);
        m.Box(-6, 50, 1, 6, 54, 7);
    });
    Save(dir, "Frame", m =>
    {
        m.Box(-22, 18, -4, 22, 48, -2);
        m.Box(-9, 34, -2, -3.2, 48, 8);                // направляющая втулка (две щеки)
        m.Box(3.2, 34, -2, 9, 48, 8);
    });
}

static void Geneva(string dir)
{
    // Мальтийский крест на 4 паза, межосевое 40, палец на r = C·sin(45°).
    const int slots = 4;
    const double c = 40, w = 6.4, pinR = 3;
    double r = c * Math.Sin(Math.PI / slots);
    double rStar = c * Math.Cos(Math.PI / slots);
    // Центр скругления дна паза: дно на 0,3 мм глубже ближней точки пальца (C − r − r_пальца).
    double bottom = c - r - pinR - 0.3 + w / 2;
    double uo = Math.Sqrt(rStar * rStar - w / 2 * (w / 2));

    Vector2 Slot(double angle, double u, double v) => new(
        (float)(u * Math.Cos(angle) - v * Math.Sin(angle)),
        (float)(u * Math.Sin(angle) + v * Math.Cos(angle)));

    var star = new List<Vector2>();
    double lockR = 0;
    for (int k = 0; k < slots; k++)
    {
        double s = (45 + 90 * k) * Math.PI / 180;
        star.Add(Slot(s, uo, -w / 2));
        for (int i = 0; i <= 12; i++)
        {
            double t = -Math.PI / 2 - Math.PI * i / 12;   // дно паза: от v = −w/2 через u_min к v = +w/2
            star.Add(Slot(s, bottom + w / 2 * Math.Cos(t), w / 2 * Math.Sin(t)));
        }
        star.Add(Slot(s, uo, w / 2));

        // Вогнутая запирающая дуга до следующего паза: центр — ось ведущего диска в положении выстоя.
        double mid = s + Math.PI / 4;
        var center = new Vector2((float)(c * Math.Cos(mid)), (float)(c * Math.Sin(mid)));
        Vector2 a = Slot(s, uo, w / 2), b = Slot(s + Math.PI / 2, uo, -w / 2);
        lockR = Vector2.Distance(a, center);
        double a1 = Math.Atan2(a.Y - center.Y, a.X - center.X);
        double a2 = Math.Atan2(b.Y - center.Y, b.X - center.X);
        double sweep = Math.IEEERemainder(a2 - a1, 2 * Math.PI);
        for (int i = 1; i < 30; i++)
        {
            double ang = a1 + sweep * i / 30;
            star.Add(center + new Vector2((float)(lockR * Math.Cos(ang)), (float)(lockR * Math.Sin(ang))));
        }
    }

    // Запирающий диск ведущего звена с выемкой напротив пальца (там проходят лучи креста).
    double diskR = lockR - 0.4, cutR = rStar + 0.6;
    var disk = new List<Vector2>();
    for (int i = 0; i < 360; i++)
    {
        double phi = 2 * Math.PI * i / 360;
        var u = new Vector2((float)Math.Cos(phi), (float)Math.Sin(phi));
        double rho = diskR;
        // Луч из центра входит в окружность выемки (центр (C, 0)) на расстоянии t1.
        double bproj = c * u.X, disc = bproj * bproj - (c * c - cutR * cutR);
        if (disc > 0)
        {
            double t1 = bproj - Math.Sqrt(disc);
            if (t1 > 0 && t1 < rho) rho = t1;
        }
        disk.Add(u * (float)rho);
    }

    Save(dir, "Driver", m =>
    {
        m.Cyl(0, 0, 6, 0, 6);
        m.Box(0, -4, 0, r, 4, 5);
        m.Polygon(disk, 6, 11);
        m.Cyl(r, 0, pinR, 0, 12, 24);
    });
    Save(dir, "Star", m =>
    {
        m.Polygon(star, 6, 11);
        m.Cyl(0, 0, 4, 4, 13);
    });
    Save(dir, "Frame", m =>
    {
        m.Box(20, -32, -4, 75, 32, -2);                // за кронштейном мотора (|x| ≤ 18)
        m.Cyl(40, 0, 6, -2, 4);
    });
}

static void ToggleClamp(string dir)
{
    // Рычажный зажим: рукоятка-кривошип 20 мм, тяга 45 мм, плунжер вдоль +X, упор-наковальня.
    Save(dir, "Handle", m =>
    {
        m.Cyl(0, 0, 6, 0, 6);
        m.Box(0, -4, 0, 20, 4, 5);
        m.Cyl(20, 0, 4.5, 0, 5);
        m.Cyl(20, 0, 2, 5, 10, 24);
        m.Box(20, -4, 0, 56, 4, 5);
        m.Box(56, -7, -1, 76, 7, 5);                   // рукоять: в закрытом положении не доходит до направляющей (x = 78)
    });
    Save(dir, "Link", m =>
    {
        m.Box(0, -3, 5.5, 45, 3, 9);
        m.Cyl(0, 0, 4, 5.5, 9);
        m.Cyl(45, 0, 4, 5.5, 9);
    });
    // Плунжер: начало координат — палец тяги.
    Save(dir, "Plunger", m =>
    {
        m.Cyl(0, 0, 2, 5, 10.5, 24);                   // палец в проушину тяги
        m.Box(-5, -5, 10, 10, 5, 14);                  // колодка над тягой (z 5,5…9)
        m.Box(8, -3, 5.5, 60, 3, 10);
        m.Box(60, -7, 3.5, 64, 7, 12);
    });
    Save(dir, "Frame", m =>
    {
        m.Box(-15, -20, -4, 137, 20, -2);
        m.Cyl(0, 0, 7, -2, 0);
        m.Box(78, 3, -2, 92, 7, 12);                   // направляющая плунжера
        m.Box(78, -7, -2, 92, -3, 12);
        m.Box(78, -7, 12, 92, 7, 14);
        m.Box(131, -12, -2, 137, 12, 14);              // упор
    });
}

static void Save(string dir, string name, Action<Mesh> build)
{
    Directory.CreateDirectory(dir);
    var mesh = new Mesh();
    build(mesh);
    mesh.WriteBinary(Path.Combine(dir, name + ".stl"), name);
}

/// <summary>Набор треугольников с простыми телами: призмы по контуру, цилиндры, зубья, резьба.</summary>
sealed class Mesh
{
    private readonly List<(Vector3 A, Vector3 B, Vector3 C)> _tris = new();

    /// <summary>Преобразование точек при записи (например, перенос оси Z в X).</summary>
    public Func<Vector3, Vector3>? Map { get; set; }

    public void Tri(Vector3 a, Vector3 b, Vector3 c)
    {
        if (Map is { } map) { a = map(a); b = map(b); c = map(c); }
        _tris.Add((a, b, c));
    }

    public void Box(double x0, double y0, double z0, double x1, double y1, double z1) =>
        Polygon([new((float)x0, (float)y0), new((float)x1, (float)y0), new((float)x1, (float)y1), new((float)x0, (float)y1)], z0, z1);

    public void Cyl(double cx, double cy, double r, double z0, double z1, int n = 48)
    {
        var pts = new List<Vector2>();
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            pts.Add(new((float)(cx + r * Math.Cos(a)), (float)(cy + r * Math.Sin(a))));
        }
        Polygon(pts, z0, z1);
    }

    /// <summary>Простой многоугольник (против часовой) выдавлен по Z; крышки — разбиение «отрезанием ушей».</summary>
    public void Polygon(IReadOnlyList<Vector2> pts, double z0, double z1)
    {
        float lo = (float)z0, hi = (float)z1;
        foreach (var (a, b, c) in Triangulate(pts))
        {
            Tri(new(c, lo), new(b, lo), new(a, lo));
            Tri(new(a, hi), new(b, hi), new(c, hi));
        }

        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 p = pts[i], q = pts[(i + 1) % pts.Count];
            Tri(new(p, lo), new(q, lo), new(q, hi));
            Tri(new(p, lo), new(q, hi), new(p, hi));
        }
    }

    /// <summary>Боковой зазор на каждую сторону зуба, доля модуля: прямые боковины трапеции вместо эвольвенты иначе врезаются в ответный зуб.</summary>
    private const double Backlash = 0.1;

    /// <summary>Прямозубое колесо (зуб — трапеция с углом профиля 20°), первый зуб на phaseDeg.</summary>
    public void Gear(double cx, double cy, double m, int z, double phaseDeg, double z0, double z1)
    {
        double r = m * z / 2, rf = r - 1.25 * m, ra = r + m, tan = Math.Tan(20 * Math.PI / 180);
        Cyl(cx, cy, rf, z0, z1, Math.Max(48, 4 * z));
        for (int k = 0; k < z; k++)
        {
            double a = (phaseDeg + 360.0 * k / z) * Math.PI / 180;
            var u = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
            var v = new Vector2(-u.Y, u.X);
            var c = new Vector2((float)cx, (float)cy);
            double rho0 = rf - 0.3;
            double half = Math.PI * m / 4 - Backlash * m;
            float h0 = (float)(half + (r - rho0) * tan), h1 = (float)Math.Max(0.15 * m, half - (ra - r) * tan);
            Polygon([c + u * (float)rho0 - v * h0, c + u * (float)ra - v * h1, c + u * (float)ra + v * h1, c + u * (float)rho0 + v * h0], z0, z1);
        }
    }

    /// <summary>
    /// Зубья рейки модуля m: делительная прямая через (ox, oy) вдоль единичного (sx, sy),
    /// зубья смотрят влево от направления (нормаль (−sy, sx)); зуб k — на расстоянии k·π·m.
    /// </summary>
    public void RackTeeth(double ox, double oy, double sx, double sy, double m, int kFrom, int kTo, double z0, double z1)
    {
        var o = new Vector2((float)ox, (float)oy);
        var s = new Vector2((float)sx, (float)sy);
        var n = new Vector2((float)-sy, (float)sx);
        double tan = Math.Tan(20 * Math.PI / 180);
        double n0 = -1.25 * m - 0.3, n1 = m;
        double half = Math.PI * m / 4 - Backlash * m;
        double h0 = half - n0 * tan, h1 = half - n1 * tan;

        Vector2 P(double along, double normal) => o + s * (float)along + n * (float)normal;

        for (int k = kFrom; k <= kTo; k++)
        {
            double at = k * Math.PI * m;
            Polygon([P(at - h0, n0), P(at + h0, n0), P(at + h1, n1), P(at - h1, n1)], z0, z1);
        }
    }

    /// <summary>Кольцо (шайба) вокруг оси X от x0 до x1.</summary>
    public void RingX(double x0, double x1, double rin, double rout, int n = 32)
    {
        Vector3 P(double x, double r, double a) => new((float)x, (float)(r * Math.Cos(a)), (float)(r * Math.Sin(a)));

        for (int i = 0; i < n; i++)
        {
            double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
            Vector3 o00 = P(x0, rout, a0), o01 = P(x0, rout, a1), o10 = P(x1, rout, a0), o11 = P(x1, rout, a1);
            Vector3 i00 = P(x0, rin, a0), i01 = P(x0, rin, a1), i10 = P(x1, rin, a0), i11 = P(x1, rin, a1);
            Tri(o00, o01, o11); Tri(o00, o11, o10);
            Tri(i00, i11, i01); Tri(i00, i10, i11);
            Tri(o00, i01, o01); Tri(o00, i00, i01);
            Tri(o10, o11, i11); Tri(o10, i11, i10);
        }
    }

    /// <summary>Правая резьба: трапеция (основание wRoot, вершина wTip) по винтовой линии вокруг Z.</summary>
    public void Thread(double rRoot, double rTip, double pitch, double z0, double z1, double wRoot, double wTip, int perTurn = 32)
    {
        int count = (int)Math.Ceiling((z1 - z0) / pitch * perTurn);
        Vector3[] Section(int i)
        {
            double t = 2 * Math.PI * i / perTurn, z = z0 + pitch * i / perTurn;
            var dir = new Vector3((float)Math.Cos(t), (float)Math.Sin(t), 0);
            float r0 = (float)(rRoot - 0.2), r1 = (float)rTip;
            return
            [
                dir * r0 + new Vector3(0, 0, (float)(z - wRoot / 2)),
                dir * r1 + new Vector3(0, 0, (float)(z - wTip / 2)),
                dir * r1 + new Vector3(0, 0, (float)(z + wTip / 2)),
                dir * r0 + new Vector3(0, 0, (float)(z + wRoot / 2))
            ];
        }

        Vector3[] prev = Section(0);
        Tri(prev[0], prev[2], prev[1]); Tri(prev[0], prev[3], prev[2]);
        for (int i = 1; i <= count; i++)
        {
            Vector3[] next = Section(i);
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

    private static IEnumerable<(Vector2, Vector2, Vector2)> Triangulate(IReadOnlyList<Vector2> pts)
    {
        var idx = Enumerable.Range(0, pts.Count).ToList();
        if (Area(pts) < 0) idx.Reverse();
        int guard = 0;
        while (idx.Count > 3 && guard++ < 100000)
        {
            bool clipped = false;
            for (int i = 0; i < idx.Count; i++)
            {
                Vector2 a = pts[idx[(i + idx.Count - 1) % idx.Count]], b = pts[idx[i]], c = pts[idx[(i + 1) % idx.Count]];
                if (Cross(b - a, c - b) <= 1e-9f) continue;
                bool inside = false;
                foreach (int j in idx)
                {
                    Vector2 p = pts[j];
                    if (p == a || p == b || p == c) continue;
                    if (Cross(b - a, p - a) > 0 && Cross(c - b, p - b) > 0 && Cross(a - c, p - c) > 0) { inside = true; break; }
                }
                if (inside) continue;
                yield return (a, b, c);
                idx.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) throw new InvalidOperationException("не удалось разбить контур");
        }
        if (idx.Count == 3) yield return (pts[idx[0]], pts[idx[1]], pts[idx[2]]);
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static double Area(IReadOnlyList<Vector2> p)
    {
        double s = 0;
        for (int i = 0; i < p.Count; i++) s += Cross(p[i], p[(i + 1) % p.Count]);
        return s / 2;
    }

    public void WriteBinary(string path, string name)
    {
        using var w = new BinaryWriter(File.Create(path));
        var header = new byte[80];
        Encoding.ASCII.GetBytes(name.PadRight(80)[..80]).CopyTo(header, 0);
        w.Write(header);
        w.Write((uint)_tris.Count);
        foreach (var (a, b, c) in _tris)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            n = n.LengthSquared() > 0 ? Vector3.Normalize(n) : Vector3.Zero;
            foreach (Vector3 v in new[] { n, a, b, c }) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
            w.Write((ushort)0);
        }
    }
}
