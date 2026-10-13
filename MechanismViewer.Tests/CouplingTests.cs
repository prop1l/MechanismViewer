using MechanismViewer.Kinematics;
using MechanismViewer.Kinematics.Couplings;

namespace MechanismViewer.Tests;

public class CouplingTests
{
    [Fact]
    public void Gear_ScalesAngleByRatio()
    {
        var gear = new GearCoupling("a", "b", -0.5);

        Assert.Equal(-1.0, gear.Evaluate(2.0), 12);
        Assert.Equal(-0.5, gear.Derivative(123.0), 12);
    }

    [Fact]
    public void Rack_MovesPitchRadiusPerRadian()
    {
        var rack = new RackPinionCoupling("p", "r", 0.01);

        Assert.Equal(2 * Math.PI * 0.01, rack.Evaluate(2 * Math.PI), 12);
    }

    [Fact]
    public void Screw_MovesLeadPerTurn()
    {
        var screw = new ScrewCoupling("s", "n", 0.002);

        Assert.Equal(0.002, screw.Evaluate(2 * Math.PI), 12);
        Assert.Equal(-0.006, screw.Evaluate(-6 * Math.PI), 12);
    }

    [Fact]
    public void CrankSlider_DeadCentersGiveFullStroke()
    {
        var crank = new CrankSliderCoupling("c", "s", 0.015, 0.05, 0.0, 0.0);

        Assert.Equal(0.0, crank.Evaluate(0.0), 12);
        Assert.Equal(-0.030, crank.Evaluate(Math.PI), 12);
        Assert.Equal(0.030, crank.Stroke, 12);
        // В мёртвых точках ползун останавливается.
        Assert.Equal(0.0, crank.Derivative(0.0), 12);
        Assert.Equal(0.0, crank.Derivative(Math.PI), 12);
    }

    [Theory]
    [InlineData(0.0, 0.3)]
    [InlineData(0.0, 2.0)]
    [InlineData(0.004, 1.0)]
    [InlineData(-0.004, 4.0)]
    public void CrankSlider_DerivativeMatchesFiniteDifference(double offset, double q)
    {
        var crank = new CrankSliderCoupling("c", "s", 0.015, 0.05, offset, 0.2);
        const double h = 1e-6;

        double numeric = (crank.Evaluate(q + h) - crank.Evaluate(q - h)) / (2 * h);

        Assert.Equal(numeric, crank.Derivative(q), 8);
    }

    [Fact]
    public void CrankSlider_RejectsRodShorterThanCrank()
    {
        Assert.Throws<ArgumentException>(() => new CrankSliderCoupling("c", "s", 0.02, 0.015, 0.0, 0.0));
    }

    [Fact]
    public void CrankSlider_RespectsCrankPhase()
    {
        // Начиная с φ0 = π/2, поворот на π/2 приводит в нижнюю мёртвую точку.
        var crank = new CrankSliderCoupling("c", "s", 0.015, 0.05, 0.0, Math.PI / 2);
        double x0 = Math.Sqrt(0.05 * 0.05 - 0.015 * 0.015);

        Assert.Equal(0.035 - x0, crank.Evaluate(Math.PI / 2), 12);
    }
}
