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
}
