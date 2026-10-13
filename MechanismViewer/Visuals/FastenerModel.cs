using MechanismViewer.Catalog;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using static MechanismViewer.Visuals.MeshKit;

namespace MechanismViewer.Visuals;

/// <summary>
/// Упрощённые модели стандартного крепежа по номинальному диаметру d и длине L, мм.
/// Локальная система: начало — центр опорной плоскости головки, +Z — направление ввинчивания;
/// головка лежит в z ≤ 0, стержень — в 0 ≤ z ≤ L. Резьба не моделируется.
/// </summary>
public static class FastenerModel
{
    public static bool IsKnownType(string? type) =>
        type is "socket-screw" or "self-tapping" or "set-screw" or "pin" or "retaining-ring";

    /// <summary>Название стандарта для спецификации.</summary>
    public static string? StandardName(string? type) => type switch
    {
        "socket-screw" => "винт с внутр. шестигранником, ISO 4762",
        "self-tapping" => "саморез с полукруглой головкой, ISO 7049",
        "set-screw" => "установочный винт, ISO 4026",
        "pin" => "штифт/ось, ISO 2338",
        "retaining-ring" => "стопорное кольцо для вала, DIN 471",
        "spring" => "пружина сжатия",
        _ => null
    };

    /// <summary>Все позиции элемента в системе координат детали, к которой он крепится.</summary>
    public static Model3DGroup Build(FastenerSpec spec, Material body, Material recess)
    {
        var group = new Model3DGroup();
        double d = spec.DiameterMm ?? 3.0;
        double length = spec.LengthMm ?? 2.0 * d;
        var axis = new Vector3D(spec.Axis[0], spec.Axis[1], spec.Axis[2]);

        foreach (double[] p in spec.Positions ?? [])
        {
            Frame f = Frame.Along(new Point3D(p[0], p[1], p[2]), axis);
            (MeshKit main, MeshKit? dark) = spec.Type switch
            {
                "socket-screw" => SocketScrew(f, d, length),
                "self-tapping" => SelfTapping(f, d, length),
                "set-screw" => SetScrew(f, d, length),
                "pin" => (Pin(f, d, length), null),
                "retaining-ring" => (RetainingRing(f, d), null),
                _ => (new MeshKit(), null)
            };

            group.Children.Add(main.Build(body));
            if (dark is not null)
            {
                group.Children.Add(dark.Build(recess));
            }
        }

        return group;
    }

    /// <summary>ISO 4762: головка Ø≈1,5d + 0,7, высота d, шестигранник под ключ ≈ 0,8d.</summary>
    private static (MeshKit, MeshKit?) SocketScrew(Frame f, double d, double length)
    {
        double rk = (1.6 * d + 0.7) / 2.0, k = d, r = d / 2.0, c = 0.1 * d;
        var profile = new Profile()
            .Hard(-k, 0).Hard(-k, rk - c).Hard(-k + c, rk).Hard(0, rk).Hard(0, r)
            .Hard(length - c, r).Hard(length, r - c).Hard(length, 0);
        MeshKit body = new MeshKit().Lathe(f, profile, 32);
        MeshKit socket = new MeshKit().Prism(f, Hexagon(0.42 * d), -k - 0.02, -k + 0.4 * d);
        return (body, socket);
    }

    /// <summary>ISO 7049: полукруглая головка Ø2d высотой 0,7d, крестовой шлиц, острый конец.</summary>
    private static (MeshKit, MeshKit?) SelfTapping(Frame f, double d, double length)
    {
        double rk = d, k = 0.7 * d, r = d / 2.0;
        var profile = new Profile()
            .Smooth(-k, 0).Smooth(-k, 0.45 * rk).Smooth(-0.8 * k, 0.8 * rk).Smooth(-0.45 * k, 0.97 * rk).Hard(0, rk)
            .Hard(0, r).Hard(length - d, r).Hard(length, 0.15 * d).Hard(length, 0);
        MeshKit body = new MeshKit().Lathe(f, profile, 32);
        double w = 0.18 * d, s = 1.1 * d;
        MeshKit slot = new MeshKit()
            .Box(f, 0, 0, -k + 0.15 * d, s, w, 0.4 * d)
            .Box(f, 0, 0, -k + 0.15 * d, w, s, 0.4 * d);
        return (body, slot);
    }

    /// <summary>ISO 4026: без головки, шестигранник в торце z = 0.</summary>
    private static (MeshKit, MeshKit?) SetScrew(Frame f, double d, double length)
    {
        double r = d / 2.0, c = 0.1 * d;
        var profile = new Profile().Hard(0, 0).Hard(0, r - c).Hard(c, r).Hard(length - c, r).Hard(length, r - c).Hard(length, 0);
        MeshKit body = new MeshKit().Lathe(f, profile, 24);
        MeshKit socket = new MeshKit().Prism(f, Hexagon(0.3 * d), -0.02, 0.4 * d);
        return (body, socket);
    }

    private static MeshKit Pin(Frame f, double d, double length)
    {
        double r = d / 2.0, c = Math.Min(0.2, 0.1 * d);
        var profile = new Profile().Hard(0, 0).Hard(0, r - c).Hard(c, r).Hard(length - c, r).Hard(length, r - c).Hard(length, 0);
        return new MeshKit().Lathe(f, profile, 32);
    }

    /// <summary>DIN 471: кольцо в канавке вала d, толщина ≈ 0,1d.</summary>
    private static MeshKit RetainingRing(Frame f, double d)
    {
        double rin = 0.47 * d, rout = rin + 0.15 * d + 0.6, s = Math.Max(0.6, 0.1 * d);
        var profile = new Profile().Hard(0, rin).Hard(0, rout).Hard(s, rout).Hard(s, rin).Hard(0, rin);
        return new MeshKit().Lathe(f, profile, 48);
    }

    /// <summary>Правильный шестиугольник с вписанным радиусом (половина размера под ключ).</summary>
    private static List<Point> Hexagon(double inradius)
    {
        double R = inradius / Math.Cos(Math.PI / 6);
        return Enumerable.Range(0, 6)
            .Select(k => new Point(R * Math.Cos(Math.PI / 3 * k), R * Math.Sin(Math.PI / 3 * k)))
            .ToList();
    }

    /// <summary>Металл крепежа по типу (с выключенной эмиссией для подсветки).</summary>
    public static Material BodyMaterial(string? type) => type switch
    {
        "self-tapping" => Highlightable(Color.FromRgb(182, 186, 192), Color.FromRgb(240, 240, 240), 60),
        "pin" => Highlightable(Color.FromRgb(160, 166, 174), Colors.White, 90),
        _ => Highlightable(Color.FromRgb(38, 39, 44), Color.FromRgb(150, 150, 155), 40) // воронёная сталь
    };

    public static Material RecessMaterial() => Highlightable(Color.FromRgb(8, 8, 10), Color.FromRgb(20, 20, 20), 5);

    /// <summary>Diffuse + Specular + Emissive(прозрачная, не замороженная — её меняет подсветка).</summary>
    public static MaterialGroup Highlightable(Color diffuse, Color specular, double power)
    {
        var diffuseBrush = new SolidColorBrush(diffuse);
        diffuseBrush.Freeze();
        var specularBrush = new SolidColorBrush(specular);
        specularBrush.Freeze();

        return new MaterialGroup
        {
            Children =
            {
                new DiffuseMaterial(diffuseBrush),
                new SpecularMaterial(specularBrush, power),
                new EmissiveMaterial(new SolidColorBrush(Colors.Transparent))
            }
        };
    }
}
