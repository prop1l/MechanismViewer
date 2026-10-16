using MechanismViewer.Catalog;
using MechanismViewer.Physics;
using MechanismViewer.Visuals;
using System.Windows.Media;
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

    private static MechanismDefinition Load(string id) =>
        CatalogLoader.Load(Path.Combine(CatalogLoader.DefaultRoot, id, CatalogLoader.FileName));

    private static MeshGeometry3D[] Meshes(MechanismScene scene, string partId) =>
        scene.Visuals
            .Where(v => scene.ItemAt(v) == (partId, null))
            .SelectMany(v => ((Model3DGroup)v.Content).Children.OfType<GeometryModel3D>())
            .Select(g => (MeshGeometry3D)g.Geometry)
            .ToArray();

    [Fact]
    public void IdenticalStlFiles_ShareOneFrozenMesh()
    {
        RunSta(() =>
        {
            // GearA2.stl и GearB2.stl побайтово одинаковы — сетка должна быть одна.
            var scene = new MechanismScene(Load("spur-pair-36-36"));
            MeshGeometry3D[] a = Meshes(scene, "gearA"), b = Meshes(scene, "gearB");

            Assert.NotEmpty(a);
            Assert.Equal(a, b);
            Assert.All(a, m => Assert.True(m.IsFrozen));
        });
    }

    [Fact]
    public void ReopeningMechanism_ReusesMeshesWithoutParsing()
    {
        RunSta(() =>
        {
            // Те же экземпляры сеток — значит, второй раз ничего не разбиралось и не строилось.
            var first = new MechanismScene(Load("geneva-4"));
            var second = new MechanismScene(Load("geneva-4"));

            Assert.All(Meshes(first, "star").Zip(Meshes(second, "star")), p => Assert.Same(p.First, p.Second));
            GC.KeepAlive(first);
        });
    }

    [Fact]
    public void SharedMeshes_KeepPerPartHighlight()
    {
        RunSta(() =>
        {
            var scene = new MechanismScene(Load("spur-pair-36-36"));
            scene.SetHover("gearA");

            Color Glow(string id) => scene.Visuals
                .Where(v => scene.ItemAt(v) == (id, null))
                .SelectMany(v => ((Model3DGroup)v.Content).Children.OfType<GeometryModel3D>())
                .Select(g => ((SolidColorBrush)((MaterialGroup)g.Material).Children.OfType<EmissiveMaterial>().Single().Brush).Color)
                .First();

            Assert.NotEqual(Colors.Transparent, Glow("gearA"));
            Assert.Equal(Colors.Transparent, Glow("gearB"));
        });
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
