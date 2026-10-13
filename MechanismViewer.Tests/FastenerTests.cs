using MechanismViewer.Catalog;
using MechanismViewer.Visuals;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace MechanismViewer.Tests;

public class FastenerTests
{
    private static MechanismDefinition Load(string id) =>
        CatalogLoader.Load(Path.Combine(CatalogLoader.DefaultRoot, id, CatalogLoader.FileName));

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

    private static IEnumerable<Color> Glows(Model3D model) => model switch
    {
        GeometryModel3D { Material: MaterialGroup group } => group.Children.OfType<EmissiveMaterial>()
            .Select(e => ((SolidColorBrush)e.Brush).Color),
        Model3DGroup g => g.Children.SelectMany(Glows),
        _ => []
    };

    private static bool IsLit(ModelVisual3D visual) => Glows(visual.Content).Any(c => c.A > 0 && c != Colors.Transparent);

    [Fact]
    public void Quantity_ComesFromPositionsWhenGiven()
    {
        MechanismDefinition lever = Load("lever-rack-m15");

        Assert.Equal([1, 1, 4], lever.Fasteners.Select(f => f.Quantity));
    }

    [Fact]
    public void Scene_DrawsOnlyFastenersWithPositions()
    {
        RunSta(() =>
        {
            var scene = new MechanismScene(Load("lever-rack-m15"));

            Assert.False(scene.HasVisual(0)); // ось — часть ступицы
            Assert.True(scene.HasVisual(1));  // стопорное кольцо
            Assert.True(scene.HasVisual(2));  // 4 винта
            // 3 детали + 2 позиции крепежа.
            Assert.Equal(5, scene.Visuals.Count());
        });
    }

    [Fact]
    public void Fastener_MovesWithItsPart()
    {
        RunSta(() =>
        {
            var scene = new MechanismScene(Load("lever-rack-m15"));
            ModelVisual3D ring = scene.Visuals.ElementAt(3); // первая позиция крепежа с моделью

            scene.Update(id => id == "lever" ? Math.PI / 6 : 0.0);

            Assert.Equal(scene.PoseOf("lever"), ring.Transform.Value);
            Assert.NotEqual(Matrix3D.Identity, ring.Transform.Value);
        });
    }

    [Fact]
    public void Selection_LightsFastenerOrFallsBackToItsPart()
    {
        RunSta(() =>
        {
            var scene = new MechanismScene(Load("lever-rack-m15"));
            ModelVisual3D lever = scene.Visuals.First();
            ModelVisual3D screws = scene.Visuals.ElementAt(4);

            scene.SelectFastener(2);
            Assert.True(IsLit(screws));
            Assert.False(IsLit(lever));

            // Ось рычага без своей модели — подсвечивается сам рычаг.
            scene.SelectFastener(0);
            Assert.False(IsLit(screws));
            Assert.True(IsLit(lever));

            scene.SelectFastener(null);
            Assert.All(scene.Visuals, v => Assert.False(IsLit(v)));
        });
    }

    [Fact]
    public void ItemAt_MapsFastenerToItsPart()
    {
        RunSta(() =>
        {
            var scene = new MechanismScene(Load("lever-rack-m15"));
            ModelVisual3D ring = scene.Visuals.ElementAt(3);

            Assert.Equal(("lever", (int?)1), scene.ItemAt(ring));
        });
    }

    [Fact]
    public void ProceduralModels_HaveGeometryAtEveryPosition()
    {
        RunSta(() =>
        {
            foreach (string type in new[] { "socket-screw", "self-tapping", "set-screw", "pin", "retaining-ring" })
            {
                var spec = new FastenerSpec
                {
                    Kind = type,
                    Size = "M3",
                    Type = type,
                    DiameterMm = 3,
                    LengthMm = 8,
                    Positions = [[0, 0, 0], [10, 0, 0]]
                };

                Model3DGroup model = FastenerModel.Build(spec, FastenerModel.BodyMaterial(type), FastenerModel.RecessMaterial());
                Rect3D bounds = model.Bounds;

                Assert.False(bounds.IsEmpty, type);
                Assert.True(bounds.SizeX > 10, $"{type}: обе позиции");
            }
        });
    }

    [Fact]
    public void Loader_RejectsPositionedFastenerWithoutGeometry()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mv-fastener-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, CatalogLoader.FileName);
            File.WriteAllText(path, """
                {
                  "id": "bad", "name": "bad",
                  "motion": { "input": "rotation", "output": "rotation" },
                  "driver": { "type": "motor", "part": "a" },
                  "parts": [ { "id": "a", "stl": "a.stl", "joint": { "type": "revolute" } } ],
                  "fasteners": [ { "kind": "винт", "size": "M3", "positions": [[0, 0, 0]] } ]
                }
                """);

            var error = Assert.Throws<InvalidDataException>(() => CatalogLoader.Load(path));
            Assert.Contains("винт", error.Message);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
