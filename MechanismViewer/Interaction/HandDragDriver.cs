using HelixToolkit.Wpf;
using MechanismViewer.Catalog;
using MechanismViewer.Physics;
using System.Windows.Media.Media3D;

namespace MechanismViewer.Interaction;

/// <summary>
/// Рычаг или ручка: пользователь тянет ведущую деталь мышью.
/// Для revolute курсор проецируется на плоскость вращения и берётся приращение угла вокруг оси
/// (с накоплением, поэтому ручку можно крутить на много оборотов); для prismatic — смещение вдоль оси.
/// Рука тянет деталь к этому положению через <see cref="MechanismSimulator.HandTarget"/>.
/// </summary>
public sealed class HandDragDriver : IDriver
{
    private readonly MechanismSimulator _simulator;
    private readonly bool _prismatic;
    private readonly Point3D _origin;
    private readonly Vector3D _axis;
    private readonly Vector3D _u;
    private readonly Vector3D _v;

    private double _grabCoordinate;
    private double _lastValue;
    private double _accumulated;
    private bool _dragging;

    public HandDragDriver(MechanismSimulator simulator)
    {
        _simulator = simulator;
        JointSpec joint = simulator.Mechanism.Part(simulator.Driver.PartId).Joint;
        _prismatic = simulator.Driver.IsPrismatic;
        _origin = new Point3D(joint.Origin[0], joint.Origin[1], joint.Origin[2]);
        _axis = new Vector3D(joint.Axis[0], joint.Axis[1], joint.Axis[2]);
        _axis.Normalize();

        // Базис плоскости вращения: v = axis × u, тогда atan2 растёт при повороте по правилу правой руки.
        Vector3D seed = Math.Abs(_axis.X) < 0.9 ? new Vector3D(1, 0, 0) : new Vector3D(0, 1, 0);
        _u = seed - Vector3D.DotProduct(seed, _axis) * _axis;
        _u.Normalize();
        _v = Vector3D.CrossProduct(_axis, _u);
    }

    public string Hint => _simulator.Mechanism.Driver.Type == "crank"
        ? "Ручной привод: возьмите ручку левой кнопкой мыши и вращайте"
        : "Ручной привод: возьмите рычаг левой кнопкой мыши и тяните";

    public bool RunsContinuously => true;
    public string? HandlePart => _simulator.Driver.PartId;

    public void Grab(Ray3D ray)
    {
        if (Measure(ray) is not { } value)
        {
            return;
        }

        _dragging = true;
        _grabCoordinate = _simulator.Driver.Coordinate;
        _lastValue = value;
        _accumulated = 0.0;
        _simulator.HandTarget = _grabCoordinate;
    }

    public void Drag(Ray3D ray)
    {
        if (!_dragging || Measure(ray) is not { } value)
        {
            return;
        }

        double delta = value - _lastValue;
        if (!_prismatic)
        {
            // Переход через ±180° не должен давать скачок на целый оборот.
            delta = Math.IEEERemainder(delta, 2.0 * Math.PI);
        }

        _lastValue = value;
        _accumulated += delta;

        double target = _grabCoordinate + _accumulated;
        if (_simulator.Range is { } range)
        {
            // Курсор ушёл за упор — цель держится на упоре, чтобы обратное движение сразу двигало деталь.
            double clamped = Math.Clamp(target, range.Min, range.Max);
            _accumulated += clamped - target;
            target = clamped;
        }

        _simulator.HandTarget = target;
    }

    public void Release()
    {
        _dragging = false;
        _simulator.HandTarget = null;
    }

    /// <summary>Угол курсора вокруг оси (рад) или его положение вдоль оси (м).</summary>
    private double? Measure(Ray3D ray)
    {
        if (_prismatic)
        {
            // Ближайшая к лучу точка оси: решение для двух скрещивающихся прямых.
            Vector3D d = ray.Direction;
            d.Normalize();
            Vector3D w0 = _origin - ray.Origin;
            double b = Vector3D.DotProduct(_axis, d);
            double denominator = 1.0 - b * b;
            if (denominator < 1e-6)
            {
                return null; // смотрим вдоль оси
            }

            double s = (b * Vector3D.DotProduct(d, w0) - Vector3D.DotProduct(_axis, w0)) / denominator;
            return s * 1e-3;
        }

        if (ray.PlaneIntersection(_origin, _axis) is not { } hit)
        {
            return null;
        }

        Vector3D r = hit - _origin;
        return Math.Atan2(Vector3D.DotProduct(r, _v), Vector3D.DotProduct(r, _u));
    }
}
