using HelixToolkit.Wpf;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace MechanismViewer.Visuals;

/// <summary>
/// Свободная камера («полёт» по сцене) поверх HelixViewport3D.
///
/// Управление: ПКМ (удерживать) — обзор мышью; W/S — вперёд/назад по взгляду; A/D — вбок;
/// E/Space — вверх, Q/C — вниз; Shift — быстрее, Ctrl — медленнее; колесо — базовая скорость.
/// Пока камера активна, встроенные орбитальные жесты Helix отключены.
/// Единицы сцены — мм.
/// </summary>
public sealed class FreeFlyCamera
{
    private const double MouseSensitivity = 0.12; // градусов на пиксель
    private const double MinPitchToUp = 1.0;       // не даём взгляду совпасть с вертикалью
    private const double Responsiveness = 12.0;    // скорость сглаживания разгона/торможения, 1/с
    private const double OrbitTargetDistance = 100.0;

    private readonly HelixViewport3D _viewport;
    private readonly HashSet<Key> _pressed = new();
    private readonly Stopwatch _clock = new();

    private Vector3D _velocity;
    private bool _looking;
    private NativePoint _lookCenterScreen;

    public FreeFlyCamera(HelixViewport3D viewport)
    {
        _viewport = viewport;

        _viewport.PreviewMouseRightButtonDown += OnRightDown;
        _viewport.PreviewMouseRightButtonUp += OnRightUp;
        _viewport.PreviewMouseMove += OnMouseMove;
        _viewport.PreviewMouseWheel += OnMouseWheel;
        _viewport.LostMouseCapture += (_, _) => StopLooking();
    }

    public bool IsActive { get; private set; }

    /// <summary>Базовая скорость полёта, мм/с.</summary>
    public double Speed { get; private set; } = 60.0;

    /// <summary>Изменилось состояние (вкл/выкл, скорость) — для подсказки в интерфейсе.</summary>
    public event EventHandler? StateChanged;

    private PerspectiveCamera? Camera => _viewport.Camera as PerspectiveCamera;

    private Vector3D WorldUp
    {
        get
        {
            Vector3D up = _viewport.ModelUpDirection;
            up.Normalize();
            return up;
        }
    }

    public void Toggle()
    {
        if (IsActive)
        {
            Deactivate();
        }
        else
        {
            Activate();
        }
    }

    public void Activate()
    {
        if (IsActive || Camera is null)
        {
            return;
        }

        IsActive = true;
        SetHelixControls(false);

        // Выравниваем «верх» камеры по вертикали сцены — горизонт без завала.
        PerspectiveCamera cam = Camera;
        cam.UpDirection = WorldUp;
        cam.LookDirection = ClampPitch(Normalized(cam.LookDirection)) * OrbitTargetDistance;

        _velocity = new Vector3D();
        _pressed.Clear();
        _clock.Restart();
        CompositionTarget.Rendering += OnRendering;

        _viewport.Focus();
        Keyboard.Focus(_viewport);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        StopLooking();
        IsActive = false;
        CompositionTarget.Rendering -= OnRendering;
        _clock.Stop();
        _pressed.Clear();
        SetHelixControls(true);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Обработка клавиш окна. Возвращает true, если клавиша поглощена камерой.</summary>
    public bool HandleKeyDown(Key key)
    {
        if (!IsActive || !IsFlightKey(key))
        {
            return false;
        }

        _pressed.Add(key);
        return true;
    }

    public bool HandleKeyUp(Key key)
    {
        if (!IsActive)
        {
            return false;
        }

        return _pressed.Remove(key) || IsFlightKey(key);
    }

    /// <summary>Окно потеряло фокус — отпускаем все клавиши, иначе камера «улетит».</summary>
    public void ReleaseKeys()
    {
        _pressed.Clear();
        StopLooking();
    }

    private static bool IsFlightKey(Key key) => key is
        Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E or Key.C or Key.Space or
        Key.Up or Key.Down or Key.Left or Key.Right or Key.PageUp or Key.PageDown or
        Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl;

    private void SetHelixControls(bool enabled)
    {
        _viewport.IsRotationEnabled = enabled;
        _viewport.IsPanEnabled = enabled;
        _viewport.IsZoomEnabled = enabled;
        _viewport.IsMoveEnabled = enabled;
        _viewport.IsChangeFieldOfViewEnabled = enabled;
        _viewport.IsInertiaEnabled = enabled;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        PerspectiveCamera? cam = Camera;
        if (cam is null)
        {
            return;
        }

        double dt = Math.Min(_clock.Elapsed.TotalSeconds, 0.1);
        _clock.Restart();
        if (dt <= 0)
        {
            return;
        }

        Vector3D forward = Normalized(cam.LookDirection);
        Vector3D up = WorldUp;
        Vector3D right = Normalized(Vector3D.CrossProduct(forward, up));

        var wish = new Vector3D();
        if (Down(Key.W) || Down(Key.Up)) wish += forward;
        if (Down(Key.S) || Down(Key.Down)) wish -= forward;
        if (Down(Key.D) || Down(Key.Right)) wish += right;
        if (Down(Key.A) || Down(Key.Left)) wish -= right;
        if (Down(Key.E) || Down(Key.Space) || Down(Key.PageUp)) wish += up;
        if (Down(Key.Q) || Down(Key.C) || Down(Key.PageDown)) wish -= up;

        if (wish.LengthSquared > 0)
        {
            wish.Normalize();
        }

        double speed = Speed;
        if (Down(Key.LeftShift) || Down(Key.RightShift)) speed *= 4.0;
        if (Down(Key.LeftCtrl) || Down(Key.RightCtrl)) speed *= 0.25;

        // Плавный разгон и торможение (экспоненциальное сглаживание, не зависит от FPS).
        double blend = 1.0 - Math.Exp(-Responsiveness * dt);
        _velocity += (wish * speed - _velocity) * blend;

        if (_velocity.LengthSquared < 1e-6)
        {
            _velocity = new Vector3D();
            return;
        }

        cam.Position += _velocity * dt;
    }

    private bool Down(Key key) => _pressed.Contains(key);

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!IsActive)
        {
            return;
        }

        Speed = Math.Clamp(Speed * Math.Pow(1.25, e.Delta / 120.0), 2.0, 2000.0);
        e.Handled = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsActive)
        {
            return;
        }

        if (PresentationSource.FromVisual(_viewport) is null)
        {
            return;
        }

        Point center = _viewport.PointToScreen(new Point(_viewport.ActualWidth / 2, _viewport.ActualHeight / 2));
        _lookCenterScreen = new NativePoint { X = (int)Math.Round(center.X), Y = (int)Math.Round(center.Y) };

        _looking = true;
        _viewport.CaptureMouse();
        _viewport.Cursor = Cursors.None;
        SetCursorPos(_lookCenterScreen.X, _lookCenterScreen.Y);
        e.Handled = true;
    }

    private void OnRightUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsActive)
        {
            return;
        }

        StopLooking();
        e.Handled = true;
    }

    private void StopLooking()
    {
        if (!_looking)
        {
            return;
        }

        _looking = false;
        _viewport.Cursor = null;
        if (_viewport.IsMouseCaptured)
        {
            _viewport.ReleaseMouseCapture();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsActive || !_looking)
        {
            return;
        }

        e.Handled = true;

        // Смещение считаем в координатах Win32: позиции WPF при масштабировании DPI
        // могут не совпасть с точкой SetCursorPos, и взгляд начал бы сам «уплывать».
        if (!GetCursorPos(out NativePoint cursor) || Camera is not { } cam)
        {
            return;
        }

        var delta = new Vector(cursor.X - _lookCenterScreen.X, cursor.Y - _lookCenterScreen.Y);

        // Нулевое смещение приходит и от собственного возврата курсора в центр.
        if (delta.X == 0 && delta.Y == 0)
        {
            return;
        }

        Vector3D up = WorldUp;
        Vector3D look = Normalized(cam.LookDirection);

        var yaw = new RotateTransform3D(new AxisAngleRotation3D(up, -delta.X * MouseSensitivity));
        look = yaw.Transform(look);

        Vector3D right = Normalized(Vector3D.CrossProduct(look, up));
        var pitch = new RotateTransform3D(new AxisAngleRotation3D(right, -delta.Y * MouseSensitivity));
        look = ClampPitch(pitch.Transform(look));

        cam.LookDirection = look * OrbitTargetDistance;
        cam.UpDirection = up;

        // Возвращаем курсор в центр — обзор не упирается в край экрана.
        SetCursorPos(_lookCenterScreen.X, _lookCenterScreen.Y);
    }

    private Vector3D ClampPitch(Vector3D look)
    {
        Vector3D up = WorldUp;
        double angle = Vector3D.AngleBetween(look, up);
        double clamped = Math.Clamp(angle, MinPitchToUp, 180.0 - MinPitchToUp);
        if (Math.Abs(clamped - angle) < 1e-9)
        {
            return look;
        }

        Vector3D horizontal = look - Vector3D.DotProduct(look, up) * up;
        if (horizontal.LengthSquared < 1e-12)
        {
            horizontal = Vector3D.CrossProduct(up, new Vector3D(1, 0, 0));
        }

        horizontal.Normalize();
        double rad = clamped * Math.PI / 180.0;
        return Normalized(up * Math.Cos(rad) + horizontal * Math.Sin(rad));
    }

    private static Vector3D Normalized(Vector3D v)
    {
        v.Normalize();
        return v;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
