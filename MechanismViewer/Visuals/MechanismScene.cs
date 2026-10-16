using HelixToolkit.Wpf;
using MechanismViewer.Catalog;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace MechanismViewer.Visuals;

/// <summary>
/// Собирает 3D-сцену механизма из описания каталога и расставляет детали по координатам шарниров.
/// STL каждой детали задан в её собственной системе: ось шарнира проходит через начало координат.
/// Шатун (joint link) построен от первой шарнирной точки вдоль +X и ставится между точками двух деталей.
/// Крепёж задан в системе своей детали и движется вместе с ней.
/// </summary>
public sealed class MechanismScene
{
    private static readonly Color HoverGlow = Color.FromArgb(255, 70, 70, 40);
    private static readonly Color SelectionGlow = Color.FromArgb(255, 30, 80, 140);

    private readonly List<PartItem> _parts = new();
    private readonly Dictionary<string, PartItem> _byId = new();
    private readonly List<FastenerItem> _fasteners = new();

    private string? _hoverPart;
    private int? _selectedFastener;

    public MechanismDefinition Mechanism { get; }

    public IEnumerable<ModelVisual3D> Visuals =>
        _parts.Select(p => p.Visual).Concat(_fasteners.Select(f => f.Visual).OfType<ModelVisual3D>());

    public MechanismScene(MechanismDefinition mechanism)
    {
        Mechanism = mechanism;

        // Шатуны — в конце: их положение зависит от уже расставленных деталей.
        foreach (PartSpec part in mechanism.Parts.OrderBy(p => p.Joint.Type == "link"))
        {
            Color color = (Color)ColorConverter.ConvertFromString(part.Color);
            var item = new PartItem(part, new ModelVisual3D { Content = Build(PartMeshes(mechanism, part), PartMaterial(color)) });
            _parts.Add(item);
            _byId.Add(part.Id, item);
        }

        for (int i = 0; i < mechanism.Fasteners.Count; i++)
        {
            FastenerSpec spec = mechanism.Fasteners[i];
            _fasteners.Add(new FastenerItem(i, spec, BuildFastener(spec, mechanism.Directory)));
        }

        Update(_ => 0.0);
    }

    /// <summary>Есть ли у позиции спецификации своя модель в сцене.</summary>
    public bool HasVisual(int fastenerIndex) => _fasteners[fastenerIndex].Visual is not null;

    /// <summary>Мировая матрица детали после последнего <see cref="Update"/>.</summary>
    public Matrix3D PoseOf(string partId) => _byId[partId].Pose;

    /// <summary>
    /// Расставляет детали. <paramref name="coordinate"/> — смещение шарнира от начального положения в СИ:
    /// рад для revolute, м для prismatic; для неподвижных деталей не вызывается.
    /// </summary>
    public void Update(Func<string, double> coordinate)
    {
        foreach (PartItem item in _parts)
        {
            JointSpec joint = item.Part.Joint;
            item.Pose = joint.Type switch
            {
                "revolute" => JointPose(joint, joint.Phase + coordinate(item.Part.Id) * 180.0 / Math.PI),
                "prismatic" => JointPose(joint, joint.Phase + coordinate(item.Part.Id) * 1e3),
                "link" => LinkPose(joint),
                _ => JointPose(joint, 0.0)
            };
            item.Visual.Transform = new MatrixTransform3D(item.Pose);
        }

        foreach (FastenerItem fastener in _fasteners)
        {
            if (fastener.Visual is not null)
            {
                Matrix3D pose = fastener.Spec.AttachedTo is { } part ? _byId[part].Pose : Matrix3D.Identity;
                fastener.Visual.Transform = new MatrixTransform3D(pose);
            }
        }
    }

    /// <summary>Что под курсором: деталь (для крепежа — та, к которой он прикреплён) и номер позиции крепежа.</summary>
    public (string? Part, int? Fastener) ItemAt(DependencyObject? hit)
    {
        for (DependencyObject? current = hit; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (_parts.FirstOrDefault(i => ReferenceEquals(i.Visual, current)) is { } part)
            {
                return (part.Part.Id, null);
            }

            if (_fasteners.FirstOrDefault(f => ReferenceEquals(f.Visual, current)) is { } fastener)
            {
                return (fastener.Spec.AttachedTo, fastener.Index);
            }
        }

        return (null, null);
    }

    /// <summary>Id детали под курсором или null.</summary>
    public string? PartAt(DependencyObject? hit) => ItemAt(hit).Part;

    /// <summary>Подсветка детали под курсором (за которую можно взяться).</summary>
    public void SetHover(string? partId)
    {
        _hoverPart = partId;
        RefreshHighlight();
    }

    /// <summary>
    /// Выбор позиции спецификации: подсвечивается её модель, а если модели нет — деталь, к которой она относится.
    /// </summary>
    public void SelectFastener(int? index)
    {
        _selectedFastener = index;
        RefreshHighlight();
    }

    private void RefreshHighlight()
    {
        FastenerItem? selected = _selectedFastener is { } i ? _fasteners[i] : null;
        string? selectedPart = selected is { Visual: null, Spec.AttachedTo: { } part } ? part : null;

        foreach (PartItem item in _parts)
        {
            Color glow = item.Part.Id == selectedPart ? SelectionGlow
                : item.Part.Id == _hoverPart ? HoverGlow
                : Colors.Transparent;
            SetGlow(item.Visual, glow);
        }

        foreach (FastenerItem fastener in _fasteners.Where(f => f.Visual is not null))
        {
            // Мелкий крепёж подсвечивается ярко, иначе его не видно.
            SetGlow(fastener.Visual!, ReferenceEquals(fastener, selected) ? Color.FromRgb(60, 150, 255) : Colors.Transparent);
        }
    }

    private static void SetGlow(ModelVisual3D visual, Color glow)
    {
        foreach (GeometryModel3D geometry in Geometries(visual.Content))
        {
            if (geometry.Material is MaterialGroup group)
            {
                foreach (EmissiveMaterial emissive in group.Children.OfType<EmissiveMaterial>())
                {
                    ((SolidColorBrush)emissive.Brush).Color = glow;
                }
            }
        }
    }

    private static IEnumerable<GeometryModel3D> Geometries(Model3D? model) => model switch
    {
        GeometryModel3D geometry => [geometry],
        Model3DGroup group => group.Children.SelectMany(Geometries),
        _ => []
    };

    private static Matrix3D JointPose(JointSpec joint, double value)
    {
        var axis = new Vector3D(joint.Axis[0], joint.Axis[1], joint.Axis[2]);
        var pose = Matrix3D.Identity;

        if (joint.Type == "revolute")
        {
            pose.Rotate(new Quaternion(axis, value));
        }
        else if (joint.Type == "prismatic")
        {
            axis.Normalize();
            pose.Translate(axis * value);
        }

        pose.Translate(new Vector3D(joint.Origin[0], joint.Origin[1], joint.Origin[2]));
        return pose;
    }

    private Matrix3D LinkPose(JointSpec joint)
    {
        LinkSpec link = joint.Link!;
        Point3D start = _byId[link.From].Pose.Transform(ToPoint(link.FromPoint));
        Point3D end = _byId[link.To].Pose.Transform(ToPoint(link.ToPoint));

        Vector3D x = end - start;
        double length = x.Length;
        x.Normalize();
        // Пружина сжимается и растягивается по своей оси, сечение не меняется.
        double scale = link.StretchLengthMm is { } rest && rest > 0.0 ? length / rest : 1.0;
        var normal = new Vector3D(joint.Axis[0], joint.Axis[1], joint.Axis[2]);
        Vector3D z = normal - Vector3D.DotProduct(normal, x) * x;
        z.Normalize();
        Vector3D y = Vector3D.CrossProduct(z, x);

        // WPF умножает вектор-строку на матрицу: строки — оси звена, последняя — его начало.
        return new Matrix3D(
            x.X * scale, x.Y * scale, x.Z * scale, 0,
            y.X, y.Y, y.Z, 0,
            z.X, z.Y, z.Z, 0,
            start.X, start.Y, start.Z, 1);
    }

    private static Point3D ToPoint(double[] p) => new(p[0], p[1], p[2]);

    /// <summary>Модель позиции спецификации: свой STL в каждой точке установки или стандартный крепёж; null — только в спецификации.</summary>
    private static ModelVisual3D? BuildFastener(FastenerSpec spec, string directory)
    {
        if (spec.Positions is not { Length: > 0 } positions)
        {
            return null;
        }

        if (spec.Stl is { } stl)
        {
            // Одна сетка на все точки установки: у каждой копии только своё положение.
            IReadOnlyList<MeshGeometry3D> meshes = MeshCache.Load(Path.Combine(directory, stl));
            Material material = PartMaterial(Color.FromRgb(60, 62, 68));
            var group = new Model3DGroup();
            var axis = new Vector3D(spec.Axis[0], spec.Axis[1], spec.Axis[2]);
            foreach (double[] p in positions)
            {
                Frame f = Frame.Along(ToPoint(p), axis);
                var placed = Build(meshes, material);
                placed.Transform = new MatrixTransform3D(new Matrix3D(
                    f.X.X, f.X.Y, f.X.Z, 0, f.Y.X, f.Y.Y, f.Y.Z, 0, f.Z.X, f.Z.Y, f.Z.Z, 0, p[0], p[1], p[2], 1));
                group.Children.Add(placed);
            }

            return new ModelVisual3D { Content = group };
        }

        if (!FastenerModel.IsKnownType(spec.Type))
        {
            return null;
        }

        Model3DGroup model = FastenerModel.Build(spec, FastenerModel.BodyMaterial(spec.Type), FastenerModel.RecessMaterial());
        return new ModelVisual3D { Content = model };
    }

    /// <summary>Сетки детали из STL, общие для всех одинаковых деталей (<see cref="MeshCache"/>); материал у каждой детали свой, чтобы подсвечивать её отдельно.</summary>
    private static IReadOnlyList<MeshGeometry3D> PartMeshes(MechanismDefinition mechanism, PartSpec part) =>
        MeshCache.Load(Path.Combine(mechanism.Directory, part.Stl));

    private static Model3DGroup Build(IReadOnlyList<MeshGeometry3D> meshes, Material material)
    {
        var model = new Model3DGroup();
        foreach (MeshGeometry3D mesh in meshes)
        {
            model.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
        }

        return model;
    }

    /// <summary>Основной материал + выключенная эмиссия для подсветки.</summary>
    private static MaterialGroup PartMaterial(Color color) => new()
    {
        Children =
        {
            new DiffuseMaterial(new SolidColorBrush(color)),
            new EmissiveMaterial(new SolidColorBrush(Colors.Transparent))
        }
    };

    private sealed class PartItem(PartSpec part, ModelVisual3D visual)
    {
        public PartSpec Part { get; } = part;
        public ModelVisual3D Visual { get; } = visual;
        public Matrix3D Pose { get; set; } = Matrix3D.Identity;
    }

    private sealed record FastenerItem(int Index, FastenerSpec Spec, ModelVisual3D? Visual);
}
