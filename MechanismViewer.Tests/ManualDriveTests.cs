using HelixToolkit.Wpf;
using MechanismViewer.Catalog;
using MechanismViewer.Interaction;
using MechanismViewer.Physics;
using System.Windows.Media.Media3D;

namespace MechanismViewer.Tests;

public class ManualDriveTests
{
    private static MechanismSimulator Load(string id) =>
        new(CatalogLoader.Load(Path.Combine(CatalogLoader.DefaultRoot, id, CatalogLoader.FileName)));

    private static void Run(MechanismSimulator sim, double seconds)
    {
        for (double t = 0; t < seconds; t += 1.0 / 60.0)
        {
            sim.Step(1.0 / 60.0);
        }
    }

    private static double Deg(double rad) => rad * 180.0 / Math.PI;

    [Fact]
    public void Lever_FollowsHand_AndMovesRackByPitchRadius()
    {
        MechanismSimulator sim = Load("lever-rack-m15");
        sim.HandTarget = 30.0 * Math.PI / 180.0;

        Run(sim, 1.0);

        Assert.Equal(30.0, Deg(sim.Driver.Coordinate), 0);
        Assert.Equal(0.015 * sim.Driver.Coordinate, sim.Output!.Coordinate, 9);
        Assert.InRange(Math.Abs(sim.Driver.Velocity), 0.0, 0.05);
    }

    [Fact]
    public void Lever_FollowsMovingCursorWithoutOvershoot()
    {
        MechanismSimulator sim = Load("lever-rack-m15");
        double maxAngle = 0.0;

        // Курсор за 0,5 с доводит рычаг до 40° (по кадрам 60 Гц), потом держит.
        for (int frame = 0; frame < 90; frame++)
        {
            sim.HandTarget = Math.Min(frame / 30.0, 1.0) * 40.0 * Math.PI / 180.0;
            sim.Step(1.0 / 60.0);
            maxAngle = Math.Max(maxAngle, Deg(sim.Driver.Coordinate));
        }

        Assert.InRange(maxAngle, 39.0, 40.5);
        Assert.Equal(40.0, Deg(sim.Driver.Coordinate), 0);
    }

    [Fact]
    public void Lever_StopsAtRangeLimit()
    {
        MechanismSimulator sim = Load("lever-rack-m15");
        sim.HandTarget = Math.PI; // за упором

        Run(sim, 1.0);

        Assert.Equal(45.0, Deg(sim.Driver.Coordinate), 6);
    }

    [Fact]
    public void Lever_ReleasedStaysPut()
    {
        MechanismSimulator sim = Load("lever-rack-m15");
        sim.HandTarget = -0.4;
        Run(sim, 1.0);
        double held = sim.Driver.Coordinate;

        sim.HandTarget = null;
        Run(sim, 1.0);

        // Без пружины рычаг остаётся, где отпустили (трение гасит остаточную скорость).
        Assert.InRange(sim.Driver.Coordinate, held - 0.02, held + 0.02);
        Assert.Equal(0.0, sim.Driver.Velocity);
    }

    [Fact]
    public void Lever_HandCannotOvercomeLoadAboveItsLimit()
    {
        MechanismSimulator sim = Load("lever-rack-m15");
        // 40 Н на рейке × r 15 мм = 0,6 Н·м > предела руки 0,5 Н·м.
        sim.OutputLoad = 40.0;
        sim.HandTarget = 0.5;

        Run(sim, 1.0);

        Assert.Equal(0.0, sim.Driver.Coordinate, 9);
        Assert.Equal(sim.HandLimit, sim.HandEffort, 9);
    }

    [Fact]
    public void Button_PressesToStop_AndSpringReturnsIt()
    {
        MechanismSimulator sim = Load("button-rack-pinion");
        Assert.Equal(-0.5, sim.SpringForce, 9);

        sim.HandForce = sim.HandLimit;
        Run(sim, 0.2);

        Assert.Equal(8.0, sim.Driver.Coordinate * 1e3, 6);
        Assert.Equal(Deg(0.008 / 0.006), Deg(sim.Output!.Coordinate), 6);
        // На упоре пружина сжата на 8 мм: 0,5 + 0,2·8 = 2,1 Н.
        Assert.Equal(-2.1, sim.SpringForce, 6);

        sim.HandForce = 0.0;
        Run(sim, 0.3);

        Assert.Equal(0.0, sim.Driver.Coordinate, 9);
        Assert.Equal(0.0, sim.Output.Coordinate, 9);
    }

    [Fact]
    public void Button_HandWeakerThanPreload_DoesNotMove()
    {
        MechanismSimulator sim = Load("button-rack-pinion");
        sim.HandForce = 0.3; // меньше поджатия пружины 0,5 Н

        Run(sim, 0.5);

        Assert.Equal(0.0, sim.Driver.Coordinate, 12);
    }

    [Fact]
    public void HandDrag_ConvertsCursorAngleToTarget()
    {
        MechanismSimulator sim = Load("lever-rack-m15");
        var driver = new HandDragDriver(sim);
        var view = new Vector3D(0, 0, -1);

        // Взяли рычаг в точке (0, 50), повели на 20° против часовой.
        driver.Grab(new Ray3D(new Point3D(0, 50, 100), view));
        double a = 20.0 * Math.PI / 180.0;
        driver.Drag(new Ray3D(new Point3D(-50 * Math.Sin(a), 50 * Math.Cos(a), 100), view));

        Assert.Equal(20.0, Deg(sim.HandTarget!.Value), 6);

        // Повели ещё на 60° — цель упирается в 45°, а обратное движение сразу двигает рычаг.
        double b = 80.0 * Math.PI / 180.0;
        driver.Drag(new Ray3D(new Point3D(-50 * Math.Sin(b), 50 * Math.Cos(b), 100), view));
        Assert.Equal(45.0, Deg(sim.HandTarget!.Value), 6);

        double c = 70.0 * Math.PI / 180.0;
        driver.Drag(new Ray3D(new Point3D(-50 * Math.Sin(c), 50 * Math.Cos(c), 100), view));
        Assert.Equal(35.0, Deg(sim.HandTarget!.Value), 6);

        driver.Release();
        Assert.Null(sim.HandTarget);
    }
}
