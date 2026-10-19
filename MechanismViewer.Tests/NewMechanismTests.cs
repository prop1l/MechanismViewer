using HelixToolkit.Wpf;
using MechanismViewer.Catalog;
using MechanismViewer.Kinematics;
using MechanismViewer.Kinematics.Couplings;
using MechanismViewer.Physics;
using MechanismViewer.Visuals;
using System.Windows.Media.Media3D;

namespace MechanismViewer.Tests;

public class NewMechanismTests
{
    private static MechanismDefinition Load(string id) =>
        CatalogLoader.Load(Path.Combine(CatalogLoader.DefaultRoot, id, CatalogLoader.FileName));

    private static double Deg(double rad) => rad * 180.0 / Math.PI;
    private static double Rad(double deg) => deg * Math.PI / 180.0;

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

    // ---------- четырёхзвенник ----------

    [Fact]
    public void FourBar_IsGrashofAndSwingsAbout51Degrees()
    {
        var bar = new FourBarCoupling("c", "r", 0.015, 0.055, 0.035, 0.060, 1, 0.0);
        double[] angles = Enumerable.Range(0, 720).Select(i => Deg(bar.Angle(Rad(i / 2.0)))).ToArray();

        Assert.True(bar.IsFullRotation);
        Assert.Equal(94.096, Deg(bar.InitialRockerAngle), 2);
        Assert.Equal(51.2, angles.Max() - angles.Min(), 0);
    }

    [Theory]
    [InlineData(0.4)]
    [InlineData(2.0)]
    [InlineData(4.5)]
    public void FourBar_DerivativeMatchesFiniteDifference(double q)
    {
        var bar = new FourBarCoupling("c", "r", 0.015, 0.055, 0.035, 0.060, 1, 0.0);
        double numeric = (bar.Evaluate(q + 1e-5) - bar.Evaluate(q - 1e-5)) / 2e-5;

        Assert.Equal(numeric, bar.Derivative(q), 6);
    }

    [Fact]
    public void FourBar_NonGrashofIsReported()
    {
        // Кривошип не самое короткое звено — полного оборота нет.
        var bar = new FourBarCoupling("c", "r", 0.040, 0.050, 0.020, 0.060, 1, Rad(60));

        Assert.False(bar.IsFullRotation);
    }

    [Fact]
    public void FourBar_WrongRockerPhaseInJson_IsRejectedWithExpectedValue()
    {
        MechanismDefinition m = Load("four-bar-crank-rocker");
        var broken = m with
        {
            Parts = m.Parts.Select(p => p.Id == "rocker" ? p with { Joint = p.Joint with { Phase = 80 } } : p).ToList()
        };

        var error = Assert.Throws<InvalidDataException>(() => new KinematicSolver(broken));
        Assert.Contains("94,096", error.Message.Replace('.', ','));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.3)]
    [InlineData(3.1)]
    [InlineData(5.0)]
    public void FourBar_CouplerKeepsItsLengthInScene(double crankAngle)
    {
        RunSta(() =>
        {
            MechanismDefinition m = Load("four-bar-crank-rocker");
            var solver = new KinematicSolver(m);
            PartState[] state = solver.Solve(crankAngle);
            var scene = new MechanismScene(m);
            scene.Update(id => solver.IndexOf(id) is var i and >= 0 ? state[i].Q : 0.0);

            Point3D crankPin = scene.PoseOf("crank").Transform(new Point3D(15, 0, 0));
            Point3D rockerPin = scene.PoseOf("rocker").Transform(new Point3D(35, 0, 0));

            Assert.InRange((rockerPin - crankPin).Length, 55.0 - 1e-4, 55.0 + 1e-4);
        });
    }

    // ---------- кулачок ----------

    private static CamCoupling Cam() => new("cam", "f",
        [(Rad(150), 0.012, "cycloidal"), (Rad(30), 0.0, "cycloidal"), (Rad(150), -0.012, "cycloidal"), (Rad(30), 0.0, "cycloidal")], 0.0);

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(75, 6.0)]
    [InlineData(150, 12.0)]
    [InlineData(170, 12.0)]
    [InlineData(255, 6.0)]
    [InlineData(330, 0.0)]
    [InlineData(400, 1.3006)] // второй оборот: 40° подъёма, 12·(0,2667 − sin(1,6755)/2π)
    public void Cam_FollowsCycloidalLaw(double camDeg, double liftMm)
    {
        Assert.Equal(liftMm, Cam().Evaluate(Rad(camDeg)) * 1e3, 3);
    }

    [Theory]
    [InlineData(30.0)]
    [InlineData(200.0)]
    [InlineData(355.0)]
    public void Cam_DerivativeMatchesFiniteDifference(double camDeg)
    {
        CamCoupling cam = Cam();
        double q = Rad(camDeg);
        double numeric = (cam.Evaluate(q + 1e-6) - cam.Evaluate(q - 1e-6)) / 2e-6;

        Assert.Equal(numeric, cam.Derivative(q), 7);
    }

    [Fact]
    public void Cam_RejectsOpenLaw()
    {
        Assert.Throws<ArgumentException>(() => new CamCoupling("c", "f", [(Rad(180), 0.01, "cycloidal"), (Rad(180), 0.0, "cycloidal")], 0.0));
        Assert.Throws<ArgumentException>(() => new CamCoupling("c", "f", [(Rad(90), 0.0, "cycloidal")], 0.0));
    }

    [Fact]
    public void CamShape_TouchesFollowerAtEveryAngle()
    {
        RunSta(() =>
        {
            MechanismDefinition m = Load("cam-flat-follower");
            Point3D[] points = ShapeBuilder.Build(m, m.Part("cam")).Positions
                .Where(p => p.Z > 0.5 && p.Z < 7.5 || Math.Abs(p.Z - 8) < 1e-3) // контур кулачка, без ступицы
                .ToArray();
            CamCoupling cam = Cam();

            for (int deg = 0; deg < 360; deg += 15)
            {
                double theta = Rad(deg);
                double top = points.Max(p => p.X * Math.Sin(theta) + p.Y * Math.Cos(theta));
                Assert.Equal(15.0 + cam.Evaluate(theta) * 1e3, top, 1);
            }
        });
    }

    // ---------- мальтийский крест ----------

    [Fact]
    public void Geneva_TurnsQuarterPerRevolution_AndDwellsMostOfIt()
    {
        var geneva = new GenevaCoupling("d", "s", 4, 0.040, Math.PI);
        int moving = 0;

        for (int i = 1; i <= 720; i++)
        {
            double q = Rad(i / 2.0);
            if (Math.Abs(geneva.Derivative(q)) > 1e-9) moving++;
        }

        Assert.Equal(-90.0, Deg(geneva.Evaluate(2 * Math.PI)), 9);
        Assert.Equal(-180.0, Deg(geneva.Evaluate(4 * Math.PI)), 9);
        // Палец в пазу четверть оборота (90° из 360°).
        Assert.InRange(moving, 178, 182);
    }

    [Fact]
    public void Geneva_IsContinuousAndSmoothAtEngagement()
    {
        var geneva = new GenevaCoupling("d", "s", 4, 0.040, Math.PI);
        double previous = geneva.Evaluate(0);

        for (int i = 1; i <= 7200; i++)
        {
            double q = Rad(i / 10.0);
            double value = geneva.Evaluate(q);
            Assert.True(Math.Abs(value - previous) < Rad(0.5), $"скачок при {i / 10.0}°");
            previous = value;
        }

        // Вход пальца в паз (θ' = −45°, т. е. поворот на 135° от старта) — без удара.
        Assert.InRange(geneva.Derivative(Rad(135.01)), -0.01, 0.0);
    }

    [Fact]
    public void Geneva_InCatalog_RotatesOppositeToDriver()
    {
        var sim = new MechanismSimulator(Load("geneva-4"));
        sim.Motor.TargetSpeed = 10.0;

        while (sim.Driver.Coordinate < 2 * Math.PI)
        {
            sim.Step(1.0 / 60.0);
        }

        Assert.InRange(Deg(sim.Output!.Coordinate), -90.0001, -89.9);
    }

    // ---------- ходовой винт, червяк, зажим ----------

    [Fact]
    public void ScrewNut_ReversesAtBothEnds()
    {
        var sim = new MechanismSimulator(Load("screw-nut-tr8"));
        sim.Motor.TargetSpeed = 30.0;
        double min = 0.0;
        bool cameBack = false;

        for (int frame = 0; frame < 60 * 30; frame++)
        {
            sim.Step(1.0 / 60.0);
            min = Math.Min(min, sim.Output!.Coordinate);
            cameBack |= min < -0.0499 && sim.Output.Coordinate > -0.040;
        }

        Assert.Equal(-0.050, min, 4);
        Assert.True(cameBack, "гайка должна уехать обратно после упора");
        Assert.InRange(sim.Output!.Coordinate, -0.0501, 1e-9);
    }

    [Fact]
    public void Worm_OneTurnAdvancesWheelOneTooth()
    {
        var solver = new KinematicSolver(Load("worm-1-20"));

        Assert.Equal(18.0, Deg(solver.Solve(2 * Math.PI)[solver.IndexOf("wheel")].Q), 9);
    }

    [Fact]
    public void ToggleClamp_GainsForceNearDeadCenter()
    {
        var solver = new KinematicSolver(Load("toggle-clamp"));
        int plunger = solver.IndexOf("plunger");

        // Рукоятка стартует на 110°; −20° → 90°, −107° → 3° (за 3° до мёртвой точки).
        double open = Math.Abs(solver.Solve(Rad(-20))[plunger].K);
        double closed = Math.Abs(solver.Solve(Rad(-107))[plunger].K);

        Assert.Equal(0.031, solver.Solve(Rad(-107))[plunger].Q, 3);
        Assert.True(open / closed > 10, $"выигрыш в силе ×{open / closed:0.0}");
    }

    // ---------- синусная и качающаяся кулисы ----------

    [Fact]
    public void Yoke_IsPureHarmonic()
    {
        var yoke = new YokeCoupling("c", "y", 0.012, 0.0);

        Assert.Equal(0.024, yoke.Stroke, 12);
        Assert.Equal(-0.012, yoke.Evaluate(Rad(90)), 12);
        Assert.Equal(-0.024, yoke.Evaluate(Rad(180)), 12);
        // Без шатуна ход симметричен: за четверть оборота от мёртвой точки — ровно половина хода.
        Assert.Equal(yoke.Evaluate(Rad(90)), yoke.Evaluate(Rad(270)), 12);
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(2.0)]
    [InlineData(4.4)]
    public void SlottedLever_DerivativeMatchesFiniteDifference(double q)
    {
        var lever = new SlottedLeverCoupling("c", "l", 0.015, 0.040, 0.4);
        double numeric = (lever.Evaluate(q + 1e-6) - lever.Evaluate(q - 1e-6)) / 2e-6;

        Assert.Equal(numeric, lever.Derivative(q), 8);
    }

    [Fact]
    public void SlottedLever_SwingsSlowlyOneWayAndQuicklyBack()
    {
        var lever = new SlottedLeverCoupling("c", "l", 0.015, 0.040, 0.0);
        double[] angles = Enumerable.Range(0, 3600).Select(i => lever.Evaluate(Rad(i / 10.0))).ToArray();
        int forward = Enumerable.Range(0, 3600).Count(i => lever.Derivative(Rad(i / 10.0)) > 0);

        Assert.Equal(44.05, Deg(angles.Max() - angles.Min()), 1);
        Assert.Equal(1.648, lever.TimeRatio, 3);
        // Рабочий ход (кулиса поворачивается против часовой) — 224° оборота кривошипа.
        Assert.InRange(forward / 10.0, 223.5, 224.5);
    }

    [Fact]
    public void SlottedLever_RejectsCrankLongerThanDistance()
    {
        Assert.Throws<ArgumentException>(() => new SlottedLeverCoupling("c", "l", 0.04, 0.03, 0.0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(2.5)]
    [InlineData(4.0)]
    public void SlottedLever_PinStaysOnSlotAxisInScene(double crankAngle)
    {
        RunSta(() =>
        {
            MechanismDefinition m = Load("slotted-lever-quick-return");
            var solver = new KinematicSolver(m);
            PartState[] state = solver.Solve(crankAngle);
            var scene = new MechanismScene(m);
            scene.Update(id => solver.IndexOf(id) is var i and >= 0 ? state[i].Q : 0.0);

            Point3D pin = scene.PoseOf("crank").Transform(new Point3D(15, 0, 0));
            Matrix3D toLever = scene.PoseOf("lever");
            toLever.Invert();
            Point3D local = toLever.Transform(pin);

            Assert.InRange(local.Y, -1e-6, 1e-6);
            Assert.InRange(local.X, 25.0 - 1e-6, 55.0 + 1e-6);
        });
    }

    // ---------- эллипсограф ----------

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.9)]
    [InlineData(2.2)]
    [InlineData(3.9)]
    [InlineData(5.5)]
    public void Ellipsograph_KeepsLinkLengthAndDrawsEllipse(double q)
    {
        RunSta(() =>
        {
            MechanismDefinition m = Load("ellipsograph");
            var solver = new KinematicSolver(m);
            PartState[] state = solver.Solve(q);
            var scene = new MechanismScene(m);
            scene.Update(id => solver.IndexOf(id) is var i and >= 0 ? state[i].Q : 0.0);

            Point3D a = scene.PoseOf("slider-x").Transform(new Point3D());
            Point3D b = scene.PoseOf("slider-y").Transform(new Point3D());
            Point3D crankPin = scene.PoseOf("crank").Transform(new Point3D(20, 0, 0));
            Point3D pen = scene.PoseOf("rod").Transform(new Point3D(55, 0, 0));

            Assert.InRange((b - a).Length, 40.0 - 1e-6, 40.0 + 1e-6);
            // Палец рукоятки — в середине звена.
            Assert.InRange(Math.Abs(crankPin.X - (a.X + b.X) / 2) + Math.Abs(crankPin.Y - (a.Y + b.Y) / 2), 0.0, 1e-6);
            // Перо — на эллипсе с полуосями 15 (X) и 55 (Y).
            Assert.Equal(1.0, pen.X * pen.X / (15.0 * 15.0) + pen.Y * pen.Y / (55.0 * 55.0), 6);
        });
    }

    // ---------- клин и ремень ----------

    [Fact]
    public void Wedge_LiftsPlungerByTangentOfAngle()
    {
        var solver = new KinematicSolver(Load("wedge-20"));
        PartState plunger = solver.Solve(0.016)[solver.IndexOf("plunger")];

        Assert.Equal(5.82, plunger.Q * 1e3, 2);
        Assert.Equal(Math.Tan(Rad(20)), plunger.K, 12);
    }

    [Fact]
    public void Belt_KeepsDirectionAndHalvesSpeed()
    {
        var solver = new KinematicSolver(Load("belt-flat-1-2"));
        PartState driven = solver.Solve(2 * Math.PI)[solver.IndexOf("driven")];

        Assert.Equal(Math.PI, driven.Q, 12);
        Assert.Equal(0.5, driven.K, 12);
    }

    [Fact]
    public void BeltShape_WrapsBothPulleys()
    {
        RunSta(() =>
        {
            MechanismDefinition m = Load("belt-flat-1-2");
            Point3D[] points = ShapeBuilder.Build(m, m.Part("belt")).Positions.ToArray();

            // Ни одна точка ремня не заходит внутрь ободов, и ремень доходит до крайних точек обоих шкивов.
            Assert.All(points, p =>
            {
                Assert.True(Math.Sqrt(p.X * p.X + p.Y * p.Y) > 10.09);
                Assert.True(Math.Sqrt((p.X - 70) * (p.X - 70) + p.Y * p.Y) > 20.09);
            });
            Assert.Equal(-11.3, points.Min(p => p.X), 1);
            Assert.Equal(91.3, points.Max(p => p.X), 1);
        });
    }

    // ---------- детали на подвижных деталях, кулачковый перекладчик ----------

    private static Point3D WorkpieceCenter(MechanismScene scene) =>
        scene.PoseOf("workpiece").Transform(new Point3D(0, -57, 0));

    private static MechanismScene PickAndPlaceAt(MechanismDefinition m, KinematicSolver solver, double camDeg)
    {
        PartState[] state = solver.Solve(Rad(camDeg));
        var scene = new MechanismScene(m);
        scene.Update(id => solver.IndexOf(id) is var i and >= 0 ? state[i].Q : 0.0);
        return scene;
    }

    [Fact]
    public void PickAndPlace_SlideRidesOnCarriage()
    {
        RunSta(() =>
        {
            MechanismDefinition m = Load("pick-and-place-cam");

            // 180° — обработка: каретка на B (x = 45), захват поднят.
            Point3D slide = PickAndPlaceAt(m, new KinematicSolver(m), 180).PoseOf("slide").Transform(new Point3D());

            Assert.Equal(45.0, slide.X, 6);
            Assert.Equal(40.0, slide.Y, 6);
            Assert.Equal(31.0, slide.Z, 6);
        });
    }

    [Fact]
    public void PickAndPlace_GearTrainMultipliesStrokeByFour()
    {
        var solver = new KinematicSolver(Load("pick-and-place-cam"));
        PartState[] state = solver.Solve(Rad(180));

        Assert.Equal(12.5, state[solver.IndexOf("x-follower")].Q * 1e3, 6);
        Assert.Equal(50.0, state[solver.IndexOf("carriage")].Q * 1e3, 6);
    }

    [Theory]
    [InlineData(0, 95.0, -29.0)]   // лежит на загрузке
    [InlineData(86, 70.0, -17.0)]  // едет зажатым посередине переноса
    [InlineData(180, 45.0, -29.0)] // стоит на обработке, захват поднят
    [InlineData(350, 95.0, -29.0)] // вернулся на загрузку
    public void PickAndPlace_CarriesWorkpieceThereAndBack(double camDeg, double x, double y)
    {
        RunSta(() =>
        {
            MechanismDefinition m = Load("pick-and-place-cam");
            Point3D center = WorkpieceCenter(PickAndPlaceAt(m, new KinematicSolver(m), camDeg));

            Assert.Equal(x, center.X, 6);
            Assert.Equal(y, center.Y, 6);
        });
    }

    [Theory]
    [InlineData(0, 26, 95.0)]    // захват опускается на A
    [InlineData(146, 214, 45.0)] // поднимается, ждёт обработки, опускается на B
    [InlineData(334, 360, 95.0)] // поднимается с A
    public void PickAndPlace_ReleasedWorkpieceDoesNotMove(double fromDeg, double toDeg, double x)
    {
        RunSta(() =>
        {
            MechanismDefinition m = Load("pick-and-place-cam");
            var solver = new KinematicSolver(m);

            for (double deg = fromDeg; deg <= toDeg; deg += 2)
            {
                Point3D center = WorkpieceCenter(PickAndPlaceAt(m, solver, deg));
                Assert.Equal(x, center.X, 6);
                Assert.Equal(-29.0, center.Y, 6);
            }
        });
    }

    [Theory]
    [InlineData(40)]
    [InlineData(86)]
    [InlineData(132)]
    [InlineData(240)]
    public void PickAndPlace_JawsClampTheCube(double camDeg)
    {
        RunSta(() =>
        {
            MechanismDefinition m = Load("pick-and-place-cam");
            MechanismScene scene = PickAndPlaceAt(m, new KinematicSolver(m), camDeg);
            Point3D cube = WorkpieceCenter(scene);

            // Пальцы губок (x = ±9 в разведённом положении) сходятся на 3,95 мм — до 0,05 мм от граней кубика.
            double right = scene.PoseOf("jaw-r").Transform(new Point3D(9, -57, 0)).X;
            double left = scene.PoseOf("jaw-l").Transform(new Point3D(-9, -57, 0)).X;

            Assert.Equal(cube.X + 5.05, right, 6);
            Assert.Equal(cube.X - 5.05, left, 6);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(150)]
    [InlineData(300)]
    public void PickAndPlace_SlideRollerRestsOnLiftBeam(double camDeg)
    {
        RunSta(() =>
        {
            MechanismDefinition m = Load("pick-and-place-cam");
            MechanismScene scene = PickAndPlaceAt(m, new KinematicSolver(m), camDeg);

            double rollerBottom = scene.PoseOf("slide").Transform(new Point3D(0, 46.1, 0)).Y;
            double beamTop = scene.PoseOf("lift").Transform(new Point3D(0, 36, 0)).Y;

            Assert.Equal(0.1, rollerBottom - beamTop, 6);
        });
    }

    [Theory]
    [InlineData("lift", 0)]
    [InlineData("lift", 13)]
    [InlineData("lift", 90)]
    [InlineData("x-follower", 86)]
    [InlineData("x-follower", 200)]
    public void PickAndPlace_RollersTouchCamProfiles(string follower, double camDeg)
    {
        RunSta(() =>
        {
            MechanismDefinition m = Load("pick-and-place-cam");
            MechanismScene scene = PickAndPlaceAt(m, new KinematicSolver(m), camDeg);

            double zFrom = follower == "lift" ? 2 : 10;
            Point3D[] profile = ShapeBuilder.Build(m, m.Part("camshaft")).Positions
                .Where(p => Math.Abs(p.Z - zFrom) < 1e-6 && Math.Sqrt(p.X * p.X + p.Y * p.Y) > 20)
                .Select(p => scene.PoseOf("camshaft").Transform(p))
                .ToArray();
            Point3D roller = scene.PoseOf(follower).Transform(new Point3D());

            double nearest = profile.Min(p => Math.Sqrt((p.X - roller.X) * (p.X - roller.X) + (p.Y - roller.Y) * (p.Y - roller.Y)));
            Assert.Equal(5.0, nearest, 1);
        });
    }

    [Fact]
    public void Parent_CycleIsRejected()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string json = File.ReadAllText(Path.Combine(CatalogLoader.DefaultRoot, "pick-and-place-cam", CatalogLoader.FileName))
            .Replace("\"origin\": [95, 40, 0], \"axis\": [-1, 0, 0]", "\"parent\": \"workpiece\", \"origin\": [95, 40, 0], \"axis\": [-1, 0, 0]");
        string path = Path.Combine(dir, CatalogLoader.FileName);
        File.WriteAllText(path, json);

        Assert.Contains("parent", Assert.Throws<InvalidDataException>(() => CatalogLoader.Load(path)).Message);
    }

    // ---------- пневматический клиновой захват ----------

    private static MechanismScene WedgeGripperAt(double pistonMm)
    {
        MechanismDefinition m = Load("gripper-wedge-pneumatic");
        var solver = new KinematicSolver(m);
        PartState[] state = solver.Solve(pistonMm * 1e-3);
        var scene = new MechanismScene(m);
        scene.Update(id => solver.IndexOf(id) is var i and >= 0 ? state[i].Q : 0.0);
        return scene;
    }

    [Theory]
    [InlineData(0.0, 9.0)]
    [InlineData(2.0, 7.0)]
    [InlineData(3.95, 5.05)]
    public void WedgeGripper_JawsCloseSymmetricallyOnCube(double pistonMm, double fingerX)
    {
        RunSta(() =>
        {
            MechanismScene scene = WedgeGripperAt(pistonMm);

            Assert.Equal(fingerX, scene.PoseOf("jaw-r").Transform(new Point3D(9, -57, 0)).X, 6);
            Assert.Equal(-fingerX, scene.PoseOf("jaw-l").Transform(new Point3D(-9, -57, 0)).X, 6);
        });
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.5)]
    [InlineData(3.95)]
    public void WedgeGripper_JawPinsStayOnSlotCenterlines(double pistonMm)
    {
        RunSta(() =>
        {
            MechanismScene scene = WedgeGripperAt(pistonMm);
            Matrix3D toWedge = scene.PoseOf("piston");
            toWedge.Invert();

            // Пазы клина в его системе: x = ±(y + 21). Пальцы губок — точно на осевых линиях при любом ходе.
            Point3D right = toWedge.Transform(scene.PoseOf("jaw-r").Transform(new Point3D(9, -42, 0)));
            Point3D left = toWedge.Transform(scene.PoseOf("jaw-l").Transform(new Point3D(-9, -42, 0)));

            Assert.Equal(right.Y + 21, right.X, 6);
            Assert.Equal(-(left.Y + 21), left.X, 6);
            // Палец остаётся между закрытым концом паза (y = −10,5) и нижней кромкой клина (y = −18).
            Assert.InRange(right.Y, -16.0, -12.0);
        });
    }

    [Theory]
    [InlineData(0.0, 14.6)]
    [InlineData(3.95, 10.65)]
    public void WedgeGripper_AirCompressesReturnSpring(double pistonMm, double lengthMm)
    {
        RunSta(() =>
        {
            MechanismScene scene = WedgeGripperAt(pistonMm);
            Point3D top = scene.PoseOf("body").Transform(new Point3D(0, -8.2, 0));
            Point3D piston = scene.PoseOf("piston").Transform(new Point3D(0, 7.2, 0));

            Assert.Equal(lengthMm, (top - piston).Length, 6);
        });
    }

    [Theory]
    [InlineData(0, 14.6)]    // губки разведены
    [InlineData(86, 10.65)]  // кубик зажат, поршень поднят воздухом
    [InlineData(180, 14.6)]  // обработка, губки разведены
    [InlineData(240, 10.65)]
    public void PickAndPlace_GripperPinsStayInWedgeSlots(double camDeg, double springMm)
    {
        RunSta(() =>
        {
            MechanismDefinition m = Load("pick-and-place-cam");
            MechanismScene scene = PickAndPlaceAt(m, new KinematicSolver(m), camDeg);
            Matrix3D toWedge = scene.PoseOf("piston");
            toWedge.Invert();

            Point3D right = toWedge.Transform(scene.PoseOf("jaw-r").Transform(new Point3D(9, -42, 5)));
            Point3D left = toWedge.Transform(scene.PoseOf("jaw-l").Transform(new Point3D(-9, -42, 5)));
            Point3D top = scene.PoseOf("slide").Transform(new Point3D(0, -8.2, 5));
            Point3D piston = scene.PoseOf("piston").Transform(new Point3D(0, 7.2, 5));

            Assert.Equal(right.Y + 21, right.X, 6);
            Assert.Equal(-(left.Y + 21), left.X, 6);
            Assert.Equal(springMm, (top - piston).Length, 6);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]   // губки сходятся
    [InlineData(86)]
    [InlineData(139)]  // губки расходятся
    [InlineData(300)]
    public void PickAndPlace_GripRodRollerRestsOnGripBeam(double camDeg)
    {
        RunSta(() =>
        {
            MechanismDefinition m = Load("pick-and-place-cam");
            MechanismScene scene = PickAndPlaceAt(m, new KinematicSolver(m), camDeg);

            // Ролик штока (r 4, центр на y = 98,1 в системе штока) — в 0,1 мм над балкой захвата при любой фазе.
            double rollerBottom = scene.PoseOf("piston").Transform(new Point3D(0, 94.1, 15)).Y;
            double beamTop = scene.PoseOf("grip").Transform(new Point3D(0, 54, 46)).Y;

            Assert.Equal(0.1, rollerBottom - beamTop, 6);
        });
    }
}
