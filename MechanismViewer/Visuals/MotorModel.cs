using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using static MechanismViewer.Visuals.MeshKit;

namespace MechanismViewer.Visuals;

/// <summary>
/// Процедурная 3D-модель коллекторного мотора типоразмера «380» на алюминиевом кронштейне, мм.
///
/// Ось мотора — Z, начало координат — на оси вала в плоскости заднего торца шестерни (z = 0).
/// Корпус уходит в −Z, вал проходит сквозь шестерню (z = 0…10) и выступает спереди.
///
/// Статор (неподвижно): стальной стакан с лысками и завальцованной кромкой, этикетка,
/// пластиковая задняя крышка с вентиляционными окнами, латунные клеммы с пайкой,
/// помехоподавляющий конденсатор, провода с разъёмом, L-кронштейн с косынками и винтами, оптопара энкодера.
/// Ротор (вращается): вал с фасками, распорная втулка, ступица со стопорным винтом, диск энкодера.
/// </summary>
public sealed class MotorModel
{
    // Корпус «380»: Ø27,7 мм, лыски на 22,5 мм, длина стакана 30,5 мм.
    private const double CanRadius = 13.85;
    private const double CanFlat = 11.25;
    private const double CanLength = 30.5;
    private const double EndCapLength = 4.5;
    private const double ShaftRadius = 2.0; // = посадочное отверстие шестерни

    private const double BracketThickness = 2.5;
    private const double BaseTop = -21.5;
    private const double BaseBottom = -24.0;
    private const double BaseBack = -66.0;

    private const double TerminalY = 8.6;
    private const int Smooth = 72;

    private static readonly Material Nickel = Mat(Color.FromRgb(150, 156, 164), Color.FromRgb(235, 235, 235), 45);
    private static readonly Material Polished = Mat(Color.FromRgb(175, 180, 186), Colors.White, 110);
    private static readonly Material Brass = Mat(Color.FromRgb(176, 136, 52), Color.FromRgb(255, 225, 150), 55);
    private static readonly Material Bronze = Mat(Color.FromRgb(140, 92, 45), Color.FromRgb(220, 170, 110), 40);
    private static readonly Material Plastic = Mat(Color.FromRgb(40, 43, 50), Color.FromRgb(90, 90, 95), 25);
    private static readonly Material PlasticLight = Mat(Color.FromRgb(62, 66, 74), Color.FromRgb(90, 90, 95), 25);
    private static readonly Material Hole = Mat(Color.FromRgb(8, 8, 10), Color.FromRgb(20, 20, 20), 5);
    private static readonly Material BlackOxide = Mat(Color.FromRgb(36, 37, 41), Color.FromRgb(150, 150, 155), 40);
    private static readonly Material Aluminium = Mat(Color.FromRgb(120, 132, 146), Color.FromRgb(170, 175, 185), 20);
    private static readonly Material LabelBlue = Mat(Color.FromRgb(24, 70, 150), Color.FromRgb(140, 150, 170), 35);
    private static readonly Material LabelGold = Mat(Color.FromRgb(214, 168, 30), Color.FromRgb(255, 235, 160), 40);
    private static readonly Material Solder = Mat(Color.FromRgb(180, 184, 190), Colors.White, 80);
    private static readonly Material WireRed = Mat(Color.FromRgb(190, 30, 30), Color.FromRgb(160, 110, 110), 30);
    private static readonly Material WireBlack = Mat(Color.FromRgb(22, 22, 24), Color.FromRgb(110, 110, 115), 30);
    private static readonly Material Ceramic = Mat(Color.FromRgb(186, 92, 28), Color.FromRgb(140, 110, 80), 20);
    private static readonly Material Nylon = Mat(Color.FromRgb(222, 218, 204), Color.FromRgb(120, 120, 115), 15);
    private static readonly Material Cable = Mat(Color.FromRgb(70, 74, 80), Color.FromRgb(120, 120, 125), 25);
    private static readonly Material Rubber = Mat(Color.FromRgb(18, 18, 20), Color.FromRgb(40, 40, 40), 5);
    private static readonly Material Marker = Mat(Color.FromRgb(220, 30, 40), Color.FromRgb(255, 160, 160), 30);

    public ModelVisual3D Stator { get; }
    public ModelVisual3D Rotor { get; }

    /// <param name="gapToGearMm">Зазор от заднего торца ведущей детали (z = 0) до фланца мотора.</param>
    /// <param name="hubSeatMm">Передний торец ведущей детали, к которому ступица мотора прижимает её.</param>
    /// <param name="shaftEndMm">
    /// Если задан — вал кончается внутри ступицы самой детали на этой высоте, а ступица мотора не ставится
    /// (деталь зажата своим стопорным винтом, а над ней ходят шатуны).
    /// </param>
    public MotorModel(double gapToGearMm, double hubSeatMm = 10.0, double? shaftEndMm = null)
    {
        var layout = new Layout(gapToGearMm, hubSeatMm, shaftEndMm);
        Stator = new ModelVisual3D { Content = BuildStator(layout) };
        Rotor = new ModelVisual3D { Content = BuildRotor(layout) };
    }

    /// <summary>Ставит мотор на ось шестерни и поворачивает ротор на угол вала.</summary>
    public void SetPose(Vector3 axisPositionMm, double rotorAngleDegrees)
    {
        var translate = new TranslateTransform3D(axisPositionMm.X, axisPositionMm.Y, axisPositionMm.Z);
        Stator.Transform = translate;

        var rotor = new Transform3DGroup();
        rotor.Children.Add(new RotateTransform3D(
            new AxisAngleRotation3D(new Vector3D(0, 0, 1), rotorAngleDegrees)));
        rotor.Children.Add(translate);
        Rotor.Transform = rotor;
    }

    /// <summary>Ключевые координаты вдоль оси Z.</summary>
    private readonly struct Layout(double gap, double hubSeat, double? shaftEnd)
    {
        public double HubSeat { get; } = hubSeat;                        // передний торец ведущей детали
        public double? ShaftEnd { get; } = shaftEnd;                     // задан — вал без ступицы мотора
        public double CanFront { get; } = -gap;                          // передний торец стакана = тыл кронштейна
        public double BracketFront { get; } = -gap + BracketThickness;
        public double CanBack { get; } = -gap - CanLength;
        public double CapBack { get; } = -gap - CanLength - EndCapLength;
    }

    private static Model3DGroup BuildStator(Layout l)
    {
        var g = new Model3DGroup();
        Frame w = Frame.World;

        BuildCan(g, l);
        BuildEndCap(g, l);
        BuildTerminals(g, l);
        BuildBracket(g, l);
        BuildEncoderSensor(g, l);

        // Передний подшипниковый выступ стакана (входит в отверстие кронштейна) и бронзовая втулка.
        g.Children.Add(new MeshKit().Lathe(w, new Profile()
            .Hard(l.CanFront, 3.0)
            .Hard(l.CanFront, 5.0)
            .Arc(l.CanFront + 1.6, 4.6, 0.4, 90, 0, 4)
            .Hard(l.CanFront + 2.0, 3.0), Smooth).Build(Nickel));

        g.Children.Add(new MeshKit().Lathe(w, Annulus(l.CanFront + 1.8, l.CanFront + 2.15, ShaftRadius + 0.05, 3.0), 48).Build(Bronze));

        return g;
    }

    private static void BuildCan(Model3DGroup g, Layout l)
    {
        Frame w = Frame.World;
        double zb = l.CanBack, zf = l.CanFront;
        const double frontFillet = 1.2, rearLip = 0.5;

        // Стакан: завальцованная задняя кромка → цилиндр с лысками → скругление переднего торца.
        var rings = new List<(double, IReadOnlyList<Point>)>();
        for (int k = 0; k <= 4; k++)
        {
            double a = k / 4.0 * Math.PI / 2;
            rings.Add((zb + rearLip - rearLip * Math.Cos(a), Can(rearLip - rearLip * Math.Sin(a))));
        }

        for (int k = 0; k <= 8; k++)
        {
            double a = k / 8.0 * Math.PI / 2;
            rings.Add((zf - frontFillet + frontFillet * Math.Sin(a), Can(frontFillet - frontFillet * Math.Cos(a))));
        }

        g.Children.Add(new MeshKit()
            .Loft(w, rings)
            .Cap(w, Can(frontFillet), zf, true)
            .Cap(w, Can(rearLip), zb, false)
            .Build(Nickel));

        double z0 = zb + 7.0, z1 = zb + 23.0;
        g.Children.Add(Band(z0, z1, -0.06, LabelBlue));
        g.Children.Add(Band(z0 + 1.0, z0 + 1.6, -0.09, LabelGold));
        g.Children.Add(Band(z1 - 1.6, z1 - 1.0, -0.09, LabelGold));

        // Отогнутые лапки стакана, удерживающие крышку (на лысках).
        foreach (double s in new[] { 1.0, -1.0 })
        {
            g.Children.Add(new MeshKit()
                .Box(w, 0, s * (CanFlat - 0.6 + 0.2), zb - 0.5, 3.2, 0.4, 2.2)
                .Build(Nickel));
        }

        static List<Point> Can(double inset) => FlattedCircle(CanRadius, CanFlat, inset);

        static GeometryModel3D Band(double z0, double z1, double inset, Material m) =>
            new MeshKit().Loft(Frame.World, new List<(double, IReadOnlyList<Point>)>
            {
                (z0, FlattedCircle(CanRadius, CanFlat, 0)), (z0, FlattedCircle(CanRadius, CanFlat, inset)),
                (z0, FlattedCircle(CanRadius, CanFlat, inset)), (z1, FlattedCircle(CanRadius, CanFlat, inset)),
                (z1, FlattedCircle(CanRadius, CanFlat, inset)), (z1, FlattedCircle(CanRadius, CanFlat, 0)),
            }).Build(m);
    }

    private static void BuildEndCap(Model3DGroup g, Layout l)
    {
        Frame w = Frame.World;
        double zb = l.CanBack, ze = l.CapBack;
        const double inset = 0.6, fillet = 0.8;

        // Крышка повторяет контур стакана, задняя кромка скруглена (кольца — от тыла к фронту).
        var rings = new List<(double, IReadOnlyList<Point>)>();
        for (int k = 8; k >= 0; k--)
        {
            double a = k / 8.0 * Math.PI / 2;
            rings.Add((ze + fillet - fillet * Math.Sin(a), FlattedCircle(CanRadius, CanFlat, inset + fillet - fillet * Math.Cos(a))));
        }

        rings.Add((zb + 0.4, FlattedCircle(CanRadius, CanFlat, inset)));

        g.Children.Add(new MeshKit()
            .Loft(w, rings)
            .Cap(w, FlattedCircle(CanRadius, CanFlat, inset + fillet), ze, false)
            .Build(Plastic));

        // Задний подшипниковый прилив с бронзовой втулкой.
        g.Children.Add(new MeshKit().Lathe(w, new Profile()
            .Hard(ze, 2.65)
            .Hard(ze - 1.6, 2.65)
            .Arc(ze - 1.2, 3.8, 0.4, 180, 90, 4)
            .Hard(ze + 0.05, 4.2), Smooth).Build(Plastic));

        g.Children.Add(new MeshKit().Lathe(w, Annulus(ze - 1.75, ze - 1.3, ShaftRadius + 0.05, 2.65), 48).Build(Bronze));

        // Вентиляционные окна (тёмные проёмы на заднем торце).
        var vents = new MeshKit();
        foreach (double deg in new[] { 40.0, 140.0, 220.0, 320.0 })
        {
            double a = deg * Math.PI / 180;
            var f = new Frame(new Point3D(8.6 * Math.Cos(a), 8.6 * Math.Sin(a), ze),
                new Vector3D(-Math.Sin(a), Math.Cos(a), 0), new Vector3D(Math.Cos(a), Math.Sin(a), 0), new Vector3D(0, 0, -1));
            vents.Box(f, 0, 0, 0.02, 4.2, 1.5, 0.06);
        }

        g.Children.Add(vents.Build(Hole));

        // Маркировка полярности: красная точка у «+».
        g.Children.Add(new MeshKit()
            .Lathe(Frame.Along(new Point3D(3.2, TerminalY, ze), new Vector3D(0, 0, -1)), Disc(0, 0.04, 0.55), 20)
            .Build(Marker));
    }

    private static void BuildTerminals(Model3DGroup g, Layout l)
    {
        double ze = l.CapBack;
        var bosses = new MeshKit();
        var lugs = new MeshKit();
        var holes = new MeshKit();
        var solder = new MeshKit();

        // Лепесток в плоскости XZ: X — поперёк, Y — вдоль оси мотора (мировая Z), толщина — по мировой Y.
        var lugFrame = new Frame(new Point3D(), new Vector3D(1, 0, 0), new Vector3D(0, 0, 1), new Vector3D(0, -1, 0));
        const double lugHalf = 1.4, lugHoleZ = -4.4;

        var lugOutline = new List<Point> { new(lugHalf, ze + 0.2), new(-lugHalf, ze + 0.2) };
        for (int k = 0; k <= 12; k++)
        {
            double a = Math.PI + Math.PI * k / 12;
            lugOutline.Add(new Point(lugHalf * Math.Cos(a), ze + lugHoleZ + lugHalf * Math.Sin(a)));
        }

        foreach (double s in new[] { 1.0, -1.0 })
        {
            double y = s * TerminalY;
            bosses.Box(Frame.World, 0, y, ze - 0.6, 4.6, 2.4, 1.2);
            lugs.Prism(lugFrame, lugOutline, -y - 0.2, -y + 0.2);

            var axisY = Frame.Along(new Point3D(0, y, ze + lugHoleZ), new Vector3D(0, 1, 0));
            holes.Lathe(axisY, Disc(-0.24, 0.24, 0.6), 20);

            // Капля припоя с выходящим проводом.
            solder.Lathe(Frame.Along(new Point3D(0, y + s * 0.15, ze + lugHoleZ - 0.2), new Vector3D(0, 0, 1)),
                Ellipsoid(1.05, 0.95), 24);
        }

        g.Children.Add(bosses.Build(PlasticLight));
        g.Children.Add(lugs.Build(Brass));
        g.Children.Add(holes.Build(Hole));
        g.Children.Add(solder.Build(Solder));

        // Помехоподавляющий керамический конденсатор между клеммами.
        var capCenter = new Point3D(-9.3, 0, ze - 2.4);
        g.Children.Add(new MeshKit().Lathe(Frame.Along(capCenter, new Vector3D(1, 0, 0)), new Profile()
            .Smooth(-0.7, 0)
            .Arc(0, 1.8, 0.7, 180, 0, 8)
            .Smooth(0.7, 0), 40).Build(Ceramic));

        var leads = new MeshKit();
        foreach (double s in new[] { 1.0, -1.0 })
        {
            leads.Tube(new[]
            {
                new Point3D(-9.3, s * 2.0, ze - 2.4),
                new Point3D(-8.6, s * 4.6, ze - 2.2),
                new Point3D(-4.5, s * 8.0, ze - 1.6),
                new Point3D(-1.0, s * TerminalY, ze - 1.4)
            }, 0.22, 10, 6);
        }

        g.Children.Add(leads.Build(Solder));

        // Провода: от пайки назад и вниз, в разъём на основании.
        const double entryY = BaseTop + 1.9, entryZ = BaseBack + 9.5;
        foreach ((double side, Material insulation) in new[] { (1.0, WireRed), (-1.0, WireBlack) })
        {
            g.Children.Add(new MeshKit().Tube(new[]
            {
                new Point3D(0, side * TerminalY, ze + lugHoleZ - 0.5),
                new Point3D(0, side * (TerminalY + 0.4), ze - 8.0),
                new Point3D(side * 3.0, side > 0 ? 5.0 : -12.5, ze - 11.0),
                new Point3D(side * 4.5, side > 0 ? -8.0 : -17.5, ze - 13.0),
                new Point3D(side * 2.5, -17.5, ze - 14.5),
                new Point3D(side * 1.25, entryY, ze - 15.5),
                new Point3D(side * 1.25, entryY, entryZ + 2.5)
            }, 0.62, 18, 10).Build(insulation));
        }

        // Двухконтактный разъём (типа JST) с защёлкой и уходящий назад кабель.
        double cz = entryZ, cy = BaseTop + 1.8;
        g.Children.Add(new MeshKit()
            .Box(Frame.World, 0, cy, cz, 6.2, 3.6, 5.0)
            .Box(Frame.World, 0, cy + 2.0, cz - 0.8, 2.4, 0.5, 2.4)
            .Build(Nylon));

        var entries = new MeshKit();
        foreach (double x in new[] { 1.25, -1.25 })
        {
            entries.Lathe(Frame.Along(new Point3D(x, entryY, cz + 2.5), new Vector3D(0, 0, 1)), Disc(0, 0.03, 0.8), 16);
        }

        g.Children.Add(entries.Build(Hole));

        g.Children.Add(new MeshKit().Tube(new[]
        {
            new Point3D(0, cy, cz - 2.4),
            new Point3D(0, cy, BaseBack + 1.0),
            new Point3D(0, cy - 1.0, BaseBack - 2.5),
            new Point3D(0, BaseBottom - 4.0, BaseBack - 5.0),
            new Point3D(0, BaseBottom - 9.0, BaseBack - 6.0)
        }, 1.1, 20, 10).Build(Cable));
    }

    private static void BuildBracket(Model3DGroup g, Layout l)
    {
        var plates = new MeshKit();

        // Вертикальная стенка с отверстием под подшипниковый выступ мотора.
        plates.PlateWithHole(Frame.World, RoundedRect(-18, BaseBottom, 18, 16, 2.5), 0, 0, 5.3,
            l.CanFront, l.BracketFront);

        // Основание: локальная Y = мировая Z, выдавливание вниз (локальная Z = −мировая Y).
        var baseFrame = new Frame(new Point3D(), new Vector3D(1, 0, 0), new Vector3D(0, 0, 1), new Vector3D(0, -1, 0));
        plates.PlateWithHole(baseFrame, RoundedRect(-18, BaseBack, 18, l.BracketFront, 2.5), 0, -30, 0,
            -BaseTop, -BaseBottom);

        // Косынки жёсткости между стенкой и основанием.
        var gussetFrame = new Frame(new Point3D(), new Vector3D(0, 0, 1), new Vector3D(0, 1, 0), new Vector3D(-1, 0, 0));
        var gusset = new List<Point>
        {
            new(l.CanFront, BaseTop), new(l.CanFront, BaseTop + 13.0), new(l.CanFront - 13.0, BaseTop)
        };
        plates.Prism(gussetFrame, gusset, -18, -16);
        plates.Prism(gussetFrame, gusset, 16, 18);

        g.Children.Add(plates.Build(Aluminium));

        // Винты: крепление мотора к стенке и кронштейна к основанию.
        var screws = new MeshKit();
        var slots = new MeshKit();
        foreach (double x in new[] { 12.5, -12.5 })
        {
            PanScrew(screws, slots, Frame.Along(new Point3D(x, 0, l.BracketFront), new Vector3D(0, 0, 1)));
        }

        foreach (double x in new[] { 13.0, -13.0 })
        {
            foreach (double z in new[] { l.CanFront - 8.0, BaseBack + 6.0 })
            {
                PanScrew(screws, slots, Frame.Along(new Point3D(x, BaseTop, z), new Vector3D(0, 1, 0)));
            }
        }

        g.Children.Add(screws.Build(BlackOxide));
        g.Children.Add(slots.Build(Hole));

        // Резиновые ножки под основанием.
        var feet = new MeshKit();
        foreach (double x in new[] { 14.0, -14.0 })
        {
            foreach (double z in new[] { l.CanFront - 2.0, BaseBack + 4.0 })
            {
                feet.Lathe(Frame.Along(new Point3D(x, BaseBottom, z), new Vector3D(0, -1, 0)), new Profile()
                    .Hard(0, 0).Hard(0, 3.0).Arc(0.9, 2.6, 0.4, 90, 0, 4).Smooth(1.3, 0), 32);
            }
        }

        g.Children.Add(feet.Build(Rubber));
    }

    /// <summary>Оптопара (щелевой датчик) энкодера на стойке, охватывает край диска.</summary>
    private static void BuildEncoderSensor(Model3DGroup g, Layout l)
    {
        double ze = l.CapBack;
        var body = new MeshKit();
        body.Box(Frame.World, 6.4, 0, ze - 2.95, 4.4, 3.2, 0.8);  // передняя щека
        body.Box(Frame.World, 6.4, 0, ze - 4.45, 4.4, 3.2, 0.8);  // задняя щека
        body.Box(Frame.World, 8.0, 0, ze - 3.7, 1.2, 3.2, 2.3);   // перемычка
        g.Children.Add(body.Build(Plastic));

        var post = new MeshKit();
        post.Box(Frame.World, 9.4, (BaseTop + 1.6) / 2, ze - 3.7, 2.0, 1.6 - BaseTop, 2.3);
        post.Box(Frame.World, 9.4, BaseTop + 0.4, ze - 3.7, 5.0, 0.8, 5.0); // лапка
        g.Children.Add(post.Build(Aluminium));

        // Окна излучателя/приёмника.
        var windows = new MeshKit();
        windows.Box(Frame.World, 5.2, 0, ze - 3.33, 0.9, 0.9, 0.05);
        windows.Box(Frame.World, 5.2, 0, ze - 4.07, 0.9, 0.9, 0.05);
        g.Children.Add(windows.Build(Hole));
    }

    private static Model3DGroup BuildRotor(Layout l)
    {
        var g = new Model3DGroup();
        Frame w = Frame.World;
        double ze = l.CapBack;
        double gearFront = l.HubSeat, hubFront = gearFront + 5.5;

        double s0 = ze - 5.2, s1 = l.ShaftEnd ?? hubFront + 2.0, ch = 0.3, r = ShaftRadius - 0.03;
        g.Children.Add(new MeshKit().Lathe(w, new Profile()
            .Hard(s0, 0).Hard(s0, r - ch).Hard(s0 + ch, r)
            .Hard(s1 - ch, r).Hard(s1, r - ch).Hard(s1, 0), 40).Build(Polished));

        // Латунная распорная втулка между кронштейном и шестернёй.
        g.Children.Add(new MeshKit().Lathe(w, Chamfered(l.BracketFront + 0.2, -0.15, ShaftRadius, 3.2, 0.3), Smooth).Build(Brass));

        if (l.ShaftEnd is null)
        {
            // Ступица, прижимающая шестерню, со стопорным винтом.
            g.Children.Add(new MeshKit().Lathe(w, Chamfered(gearFront + 0.15, hubFront, ShaftRadius, 5.5, 0.45), Smooth).Build(Polished));

            var screwAxis = Frame.Along(new Point3D(5.5 - 0.1, 0, (gearFront + hubFront) / 2 + 0.3), new Vector3D(1, 0, 0));
            g.Children.Add(new MeshKit().Lathe(screwAxis, Disc(0, 0.16, 1.25), 24).Build(BlackOxide));
            g.Children.Add(new MeshKit().Prism(screwAxis, Circle(0, 0, 0.62, 6), 0.1, 0.19).Build(Hole));

            // Гравировка-риска на ступице — видно вращение даже на малых оборотах.
            g.Children.Add(new MeshKit()
                .Box(w, 0, (ShaftRadius + 5.5 - 0.45) / 2, hubFront + 0.01, 0.5, 5.5 - 0.45 - ShaftRadius - 0.3, 0.04)
                .Build(Marker));
        }

        g.Children.Add(new MeshKit().Lathe(w, Chamfered(ze - 4.9, ze - 2.4, ShaftRadius, 3.0, 0.25), 48).Build(Brass));
        g.Children.Add(new MeshKit().Lathe(w, Annulus(ze - 3.9, ze - 3.5, 2.9, 6.5), Smooth).Build(Polished));

        var slots = new MeshKit();
        const int slotCount = 20;
        for (int k = 0; k < slotCount; k++)
        {
            double a = 2 * Math.PI * k / slotCount;
            var f = new Frame(new Point3D(5.3 * Math.Cos(a), 5.3 * Math.Sin(a), ze - 3.7),
                new Vector3D(Math.Cos(a), Math.Sin(a), 0), new Vector3D(-Math.Sin(a), Math.Cos(a), 0), new Vector3D(0, 0, 1));
            slots.Box(f, 0, 0, 0, 1.7, 0.75, 0.46);
        }

        g.Children.Add(slots.Build(Hole));

        var index = new MeshKit();
        var fi = new Frame(new Point3D(3.8, 0, ze - 3.7), new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, 1));
        index.Box(fi, 0, 0, 0, 1.0, 0.6, 0.46);
        g.Children.Add(index.Build(Marker));

        return g;
    }

    /// <summary>Кольцо (z0…z1, ri…ro) с острыми рёбрами.</summary>
    private static Profile Annulus(double z0, double z1, double ri, double ro) => new Profile()
        .Hard(z1, ri).Hard(z0, ri).Hard(z0, ro).Hard(z1, ro).Hard(z1, ri);

    /// <summary>Кольцо с фасками по наружным кромкам.</summary>
    private static Profile Chamfered(double z0, double z1, double ri, double ro, double c) => new Profile()
        .Hard(z1, ri).Hard(z0, ri).Hard(z0, ro - c).Hard(z0 + c, ro).Hard(z1 - c, ro).Hard(z1, ro - c).Hard(z1, ri);

    /// <summary>Сплошной диск/цилиндр (z0…z1, радиус r).</summary>
    private static Profile Disc(double z0, double z1, double r) => new Profile()
        .Hard(z0, 0).Hard(z0, r).Hard(z1, r).Hard(z1, 0);

    /// <summary>Эллипсоид вращения: полуось rz вдоль оси, r — радиус.</summary>
    private static Profile Ellipsoid(double rz, double r)
    {
        var p = new Profile();
        for (int k = 0; k <= 12; k++)
        {
            double a = -Math.PI / 2 + Math.PI * k / 12;
            p.Smooth(rz * Math.Sin(a), r * Math.Cos(a));
        }

        return p;
    }

    /// <summary>Винт с полукруглой головкой и крестовым шлицем; ось Z системы — наружу из детали.</summary>
    private static void PanScrew(MeshKit head, MeshKit slot, Frame f)
    {
        const double radius = 2.2, side = 0.55, dome = 0.95;
        var p = new Profile().Hard(0, 0).Hard(0, radius).Smooth(side, radius);
        for (int k = 1; k <= 8; k++)
        {
            double a = Math.PI / 2 * k / 8;
            p.Smooth(side + dome * Math.Sin(a), radius * Math.Cos(a));
        }

        head.Lathe(f, p, 40);
        slot.Box(f, 0, 0, side + dome - 0.02, 2.6, 0.45, 0.2);
        slot.Box(f, 0, 0, side + dome - 0.02, 0.45, 2.6, 0.2);
    }
}
