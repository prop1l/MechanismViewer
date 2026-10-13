using MechanismViewer.Catalog;
using MechanismViewer.Physics;

namespace MechanismViewer.Tests;

public class SimulatorTests
{
    private static MechanismDefinition Load(string id) =>
        CatalogLoader.Load(Path.Combine(CatalogLoader.DefaultRoot, id, CatalogLoader.FileName));

    public static TheoryData<string> CatalogIds() =>
        new(CatalogLoader.LoadAll(CatalogLoader.DefaultRoot).Select(m => m.Id));

    [Theory]
    [MemberData(nameof(CatalogIds))]
    public void EveryCatalogEntry_BuildsAndRuns(string id)
    {
        var sim = new MechanismSimulator(Load(id));
        sim.Motor.SpeedControlEnabled = true;

        // Ручные приводы: рука тянет к середине хода (или на четверть оборота), кнопку жмут.
        if (sim.Mechanism.Driver.Type == "button")
        {
            sim.HandForce = sim.HandLimit;
        }
        else if (!sim.IsMotorDriven)
        {
            sim.HandTarget = sim.Range is { } r ? (r.Min + r.Max) / 2 + (r.Max - r.Min) / 4 : Math.PI / 2;
        }

        for (int i = 0; i < 120; i++)
        {
            sim.Step(1.0 / 60.0);
        }

        Assert.All(sim.Bodies, b => Assert.True(double.IsFinite(b.Coordinate) && double.IsFinite(b.Velocity)));
        Assert.NotEqual(0.0, sim.Driver.Coordinate);
    }

    // Эталон — PhysicsWorld до замены на MechanismSimulator: тот же сценарий, 120 кадров по 1/60 с.
    [Theory]
    [InlineData(true, 3, 6.28, 0, 6.279999943883879, 12.391803339938718, -9.337477148948642, 0.0024280000393199224, 3.256000077268792E-05, false)]
    [InlineData(false, 3, 0, 5, 18.53846153846154, 36.384979993804585, -33.33065380281451, 0.10365384615384612, 0.005057076923076922, false)]
    [InlineData(false, 3, 0, 10, 0, 0, 3.0543261909900763, 0.15, 0.00743, true)]
    [InlineData(true, 6, 12, 2, 11.999999726787596, 23.29500003215968, -20.240673841169606, 0.043000000191436925, 0.0020440000037619923, false)]
    public void SpurPair_MatchesFormerPhysicsWorld(bool control, double voltage, double target, double loadMNm,
        double omegaA, double angleA, double angleB, double current, double meshTorque, bool stalled)
    {
        var sim = new MechanismSimulator(Load("spur-pair-36-36"));
        sim.Motor.SpeedControlEnabled = control;
        sim.Motor.SupplyVoltage = voltage;
        sim.Motor.TargetSpeed = target;
        sim.OutputLoad = loadMNm * 1e-3;

        for (int i = 0; i < 120; i++)
        {
            sim.Step(1.0 / 60.0);
        }

        Assert.Equal(omegaA, sim.Driver.Velocity, 9);
        Assert.Equal(angleA, sim.Driver.AbsoluteCoordinate, 9);
        Assert.Equal(angleB, sim.Output!.AbsoluteCoordinate, 9);
        Assert.Equal(current, sim.Motor.Current, 9);
        Assert.Equal(meshTorque, sim.DriveTorque, 9);
        Assert.Equal(stalled, sim.IsStalled);
    }

    [Fact]
    public void CrankSlider_SliderStaysWithinStroke()
    {
        var sim = new MechanismSimulator(Load("crank-slider-15-50"));
        sim.Motor.SpeedControlEnabled = true;
        sim.Motor.TargetSpeed = 10.0;

        double min = double.MaxValue, max = double.MinValue;
        for (int i = 0; i < 600; i++)
        {
            sim.Step(1.0 / 60.0);
            min = Math.Min(min, sim.Output!.Coordinate);
            max = Math.Max(max, sim.Output.Coordinate);
        }

        // Ход 30 мм: ползун ходит от начального (верхняя мёртвая точка) до −30 мм.
        // Положение берётся раз в кадр, поэтому мёртвые точки попадают в выборку с точностью ~0,01 мм.
        Assert.InRange(max, -1e-5, 1e-12);
        Assert.InRange(min, -0.030 - 1e-12, -0.030 + 1e-5);
        Assert.InRange(sim.Driver.Velocity, 9.0, 11.0);
    }

    [Fact]
    public void CrankSlider_EffectiveInertiaVariesWithAngle()
    {
        var sim = new MechanismSimulator(Load("crank-slider-15-50"));
        double atDeadCenter = sim.EffectiveInertia;

        sim.Motor.SpeedControlEnabled = false;
        while (sim.Driver.Coordinate < Math.PI / 2)
        {
            sim.Step(1.0 / 480.0);
        }

        // В мёртвой точке ползун не участвует, около 90° его масса добавляется с плечом ≈ r.
        Assert.True(sim.EffectiveInertia > atDeadCenter * 1.5);
    }
}
