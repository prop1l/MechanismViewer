using MechanismViewer.Catalog;
using MechanismViewer.Physics;
using MechanismViewer.Visuals;
using System.Windows.Media.Media3D;

namespace MechanismViewer.Tests;

public class SceneTests
{
    // Объекты WPF создаются только в STA-потоке.
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

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.7)]
    [InlineData(2.0)]
    [InlineData(3.6)]
    [InlineData(5.1)]
    public void CrankSliderRod_ConnectsCrankPinToSliderPin(double crankAngle)
    {
        RunSta(() =>
        {
            MechanismDefinition m = CatalogLoader.Load(Path.Combine(CatalogLoader.DefaultRoot, "crank-slider-15-50", CatalogLoader.FileName));
            var sim = new MechanismSimulator(m);
            var scene = new MechanismScene(m);
            double slider = sim.Kinematics.Solve(crankAngle)[sim.Kinematics.IndexOf("slider")].Q;

            scene.Update(id => id switch { "crank" => crankAngle, "slider" => slider, _ => 0.0 });

            Matrix3D rod = scene.PoseOf("rod");
            Point3D crankPin = scene.PoseOf("crank").Transform(new Point3D(15, 0, 0));
            Point3D sliderPin = scene.PoseOf("slider").Transform(new Point3D(0, 0, 0));

            Assert.True((rod.Transform(new Point3D(0, 0, 0)) - crankPin).Length < 1e-9);
            Assert.True((rod.Transform(new Point3D(50, 0, 0)) - sliderPin).Length < 1e-9, "длина шатуна должна сохраняться");
            Assert.Equal(0.0, sliderPin.Y, 9);
        });
    }
}
