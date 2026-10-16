using MechanismViewer.Catalog;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace MechanismViewer.Visuals;

/// <summary>
/// Миниатюра механизма для списка: сцена в начальном положении, вид с той же стороны, что и в главном окне,
/// камера охватывает все детали. Рисуется вне экрана в 2× разрешении, результат заморожен.
/// Сетки берутся из <see cref="MeshCache"/>, поэтому миниатюра почти ничего не стоит, если механизм уже открыт.
/// </summary>
public static class ThumbnailRenderer
{
    private static readonly Vector3D ViewDirection = new(-60, -70, -140);

    public static BitmapSource Render(MechanismDefinition mechanism, int width, int height)
    {
        var scene = new MechanismScene(mechanism);
        var viewport = new Viewport3D { Width = width, Height = height };

        var bounds = Rect3D.Empty;
        foreach (ModelVisual3D visual in scene.Visuals)
        {
            viewport.Children.Add(visual);
            if (visual.Content is { } content)
            {
                bounds.Union(visual.Transform.TransformBounds(content.Bounds));
            }
        }

        viewport.Children.Add(new ModelVisual3D
        {
            Content = new Model3DGroup
            {
                Children =
                {
                    new AmbientLight(Color.FromRgb(70, 72, 80)),
                    new DirectionalLight(Color.FromRgb(225, 225, 225), new Vector3D(-0.4, -0.6, -1)),
                    new DirectionalLight(Color.FromRgb(70, 70, 78), new Vector3D(0.5, 0.4, 1))
                }
            }
        });

        viewport.Camera = FitCamera(bounds, (double)width / height);
        viewport.Measure(new Size(width, height));
        viewport.Arrange(new Rect(0, 0, width, height));
        viewport.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(viewport);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Перспективная камера, в кадр которой целиком входит описанная сфера <paramref name="bounds"/>.</summary>
    private static PerspectiveCamera FitCamera(Rect3D bounds, double aspect)
    {
        const double fov = 40;
        var direction = ViewDirection;
        direction.Normalize();

        if (bounds.IsEmpty)
        {
            return new PerspectiveCamera(new Point3D(0, 0, 100), direction, new Vector3D(0, 1, 0), fov);
        }

        var center = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
        double radius = new Vector3D(bounds.SizeX, bounds.SizeY, bounds.SizeZ).Length / 2;
        // Горизонтальный угол задан, вертикальный уже на широком кадре: вписываем по меньшему.
        double half = Math.Atan(Math.Tan(fov / 2 * Math.PI / 180) * Math.Min(1.0, 1.0 / aspect));
        // Сфера с запасом описывает плоские механизмы — кадр плотнее, чем строгое вписывание.
        double distance = radius / Math.Sin(half) * 0.72;

        return new PerspectiveCamera(center - direction * distance, direction, new Vector3D(0, 1, 0), fov)
        {
            NearPlaneDistance = Math.Max(0.1, distance - radius * 2),
            FarPlaneDistance = distance + radius * 2
        };
    }
}
