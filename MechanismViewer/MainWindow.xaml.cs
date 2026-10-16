using HelixToolkit.Wpf;
using MechanismViewer.Catalog;
using MechanismViewer.Interaction;
using MechanismViewer.Physics;
using MechanismViewer.Search;
using MechanismViewer.Visuals;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace MechanismViewer;

public partial class MainWindow : Window
{
    private const string DefaultMechanismId = "spur-pair-36-36";

    private readonly DispatcherTimer _renderTimer;
    private readonly FreeFlyCamera _freeCamera;

    private IReadOnlyList<MechanismDefinition> _catalog = [];
    private MechanismSimulator? _physics;
    private double _loadScale = 1e-3;
    private MechanismScene? _scene;
    private MotorModel? _motorVisual;
    private IDriver _driver = new MotorDriver();
    private bool _dragging;
    private string? _hoverPart;
    private bool _running;
    private DateTime _lastFrameTime;

    public MainWindow()
    {
        InitializeComponent();

        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _renderTimer.Tick += RenderTimer_Tick;

        _freeCamera = new FreeFlyCamera(Viewport);
        _freeCamera.StateChanged += FreeCamera_StateChanged;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewKeyUp += MainWindow_PreviewKeyUp;
        Deactivated += (_, _) => _freeCamera.ReleaseKeys();

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _catalog = CatalogLoader.LoadAll(CatalogLoader.DefaultRoot);
            if (_catalog.Count == 0)
            {
                throw new FileNotFoundException($"Каталог механизмов пуст: {CatalogLoader.DefaultRoot}");
            }

            // Пустой запрос — весь каталог; открывается механизм по умолчанию.
            ShowSearchResults(MechanismSearch.Run(_catalog, new SearchQuery()), DefaultMechanismId);
            _lastFrameTime = DateTime.UtcNow;
            _renderTimer.Start();
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка загрузки каталога");
            MessageBox.Show(this, ex.Message, "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReadQuery() is not { } query)
        {
            SetStatus("Исправьте поля, выделенные красным: нужно положительное число");
            return;
        }

        ShowSearchResults(MechanismSearch.Run(_catalog, query), preferredId: null);
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        InputCombo.SelectedIndex = 0;
        OutputCombo.SelectedIndex = 0;
        DriverCombo.SelectedIndex = 0;

        foreach (TextBox box in NumberBoxes)
        {
            box.Text = "";
            box.ClearValue(Control.BorderBrushProperty);
        }

        ToleranceBox.Text = "20";
        ShowSearchResults(MechanismSearch.Run(_catalog, new SearchQuery()), _scene?.Mechanism.Id);
    }

    private IEnumerable<TextBox> NumberBoxes =>
        [RatioBox, StrokeBox, AngleBox, TorqueBox, ForceBox, SpeedBox, SizeXBox, SizeYBox, SizeZBox, ToleranceBox];

    /// <summary>Запрос из формы или null, если в каком-то поле не число.</summary>
    private SearchQuery? ReadQuery()
    {
        bool valid = true;

        double? Read(TextBox box)
        {
            string text = box.Text.Trim().Replace(',', '.');
            if (text.Length == 0)
            {
                box.ClearValue(Control.BorderBrushProperty);
                return null;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0.0)
            {
                box.ClearValue(Control.BorderBrushProperty);
                return value;
            }

            box.BorderBrush = Brushes.IndianRed;
            valid = false;
            return null;
        }

        double?[] size = [Read(SizeXBox), Read(SizeYBox), Read(SizeZBox)];
        if (size.Count(s => s is not null) is 1 or 2)
        {
            // Габариты нужны все три: незаполненные подсвечиваются.
            foreach (TextBox box in new[] { SizeXBox, SizeYBox, SizeZBox }.Where(b => b.Text.Trim().Length == 0))
            {
                box.BorderBrush = Brushes.IndianRed;
            }

            valid = false;
        }

        var query = new SearchQuery
        {
            Input = (InputCombo.SelectedItem as ComboBoxItem)?.Tag as string,
            Output = (OutputCombo.SelectedItem as ComboBoxItem)?.Tag as string,
            Driver = (DriverCombo.SelectedItem as ComboBoxItem)?.Tag as string,
            Ratio = Read(RatioBox),
            StrokeMm = Read(StrokeBox),
            AngleDeg = Read(AngleBox),
            OutputTorqueNm = Read(TorqueBox),
            OutputForceN = Read(ForceBox),
            SpeedRadS = Read(SpeedBox),
            MaxEnvelopeMm = size.All(s => s is not null) ? size.Select(s => s!.Value).ToArray() : null,
            TolerancePercent = Read(ToleranceBox) ?? 20.0
        };

        return valid ? query : null;
    }

    /// <summary>Показывает результаты и открывает <paramref name="preferredId"/> (если найден) или лучший результат.</summary>
    private void ShowSearchResults(SearchOutcome outcome, string? preferredId)
    {
        List<SearchResultItem> items = outcome.Results.Select(r => new SearchResultItem(r)).ToList();
        ResultsList.ItemsSource = items;

        ResultsHeader.Text = items.Count == 0
            ? "Ничего не найдено — ослабьте требования"
            : $"Найдено: {items.Count} из {_catalog.Count}";

        RejectedList.ItemsSource = outcome.Rejected.Select(r => new RejectedItem(r.Mechanism.Name, r.Reason)).ToList();
        RejectedExpander.Header = $"Не подошли: {outcome.Rejected.Count}";
        RejectedExpander.Visibility = outcome.Rejected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        SearchResultItem? selected = items.FirstOrDefault(i => i.Mechanism.Id == preferredId) ?? items.FirstOrDefault();
        ResultsList.SelectedItem = selected; // → ResultsList_SelectionChanged → ShowMechanism

        if (selected is not null)
        {
            ResultsList.ScrollIntoView(selected);
        }
        else
        {
            SetStatus("По запросу ничего не найдено");
        }
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is not SearchResultItem { Mechanism: var mechanism }
            || ReferenceEquals(mechanism, _scene?.Mechanism))
        {
            return;
        }

        try
        {
            ShowMechanism(mechanism);
            SetStatus($"{mechanism.Name}. {_driver.Hint}");
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось открыть «{mechanism.Name}»");
            MessageBox.Show(this, ex.Message, "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowMechanism(MechanismDefinition mechanism)
    {
        var physics = new MechanismSimulator(mechanism);
        var scene = new MechanismScene(mechanism);
        IDriver driver = DriverFactory.Create(physics);

        EndDrag();

        if (_scene is not null)
        {
            foreach (ModelVisual3D visual in _scene.Visuals)
            {
                Viewport.Children.Remove(visual);
            }
        }

        if (_motorVisual is not null)
        {
            Viewport.Children.Remove(_motorVisual.Stator);
            Viewport.Children.Remove(_motorVisual.Rotor);
            _motorVisual = null;
        }

        _physics = physics;
        _scene = scene;
        _driver = driver;
        _hoverPart = null;
        _running = driver.RunsContinuously;

        Visibility motorOnly = physics.IsMotorDriven ? Visibility.Visible : Visibility.Collapsed;
        RunPanel.Visibility = motorOnly;
        MotorPanel.Visibility = motorOnly;
        MotorText.Visibility = motorOnly;
        MotorPowerText.Visibility = motorOnly;
        HintText.Text = driver.Hint;

        // Нагрузка на вращающийся выход — в мН·м, на поступательный — в Н; предел — паспортный, если указан.
        bool linearOutput = physics.Output?.IsPrismatic == true;
        _loadScale = linearOutput ? 1.0 : 1e-3;
        LoadSlider.Value = 0.0;
        LoadSlider.Maximum = linearOutput
            ? mechanism.Specs.MaxForceN ?? 1.0
            : (mechanism.Specs.MaxTorqueNm ?? 0.01) * 1e3;
        LoadLabel.Text = linearOutput ? "Нагрузка на выходе, Н" : "Нагрузка на выходе, мН·м";

        // Привод принимает текущие значения органов управления.
        physics.Motor.SupplyVoltage = VoltageSlider.Value;
        physics.Motor.TargetSpeed = SpeedSlider.Value;
        physics.Motor.SpeedControlEnabled = SpeedControlCheckBox.IsChecked == true;
        physics.OutputLoad = LoadSlider.Value * _loadScale;

        foreach (ModelVisual3D visual in scene.Visuals)
        {
            Viewport.Children.Add(visual);
        }

        if (mechanism.Driver.Type == "motor")
        {
            _motorVisual = new MotorModel(mechanism.Driver.MotorGapMm, mechanism.Driver.HubSeatMm, mechanism.Driver.ShaftEndMm);
            Viewport.Children.Add(_motorVisual.Stator);
            Viewport.Children.Add(_motorVisual.Rotor);
        }

        FastenerList.ItemsSource = mechanism.Fasteners
            .Select((f, i) => new FastenerRow(i, f, mechanism, scene.HasVisual(i)))
            .ToList();
        int pieces = mechanism.Fasteners.Sum(f => f.Quantity);
        FastenerExpander.Header = $"Крепёж и комплектующие: {mechanism.Fasteners.Count} поз., {pieces} шт.";
        FastenerExpander.Visibility = mechanism.Fasteners.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        Title = $"Mechanism Viewer — {mechanism.Name}";
        SyncVisuals();
        Viewport.ZoomExtents();
    }

    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        DateTime now = DateTime.UtcNow;
        double elapsed = Math.Clamp((now - _lastFrameTime).TotalSeconds, 0.0, 0.05);
        _lastFrameTime = now;

        if (_running)
        {
            _physics?.Step(elapsed);
        }

        SyncVisuals();
    }

    private void SyncVisuals()
    {
        if (_physics is null || _scene is null)
        {
            return;
        }

        MechanismSimulator physics = _physics;
        _scene.Update(id => physics.Bodies.FirstOrDefault(b => b.PartId == id)?.Coordinate ?? 0.0);

        PhysicsMotor motor = physics.Motor;
        double[] axis = _scene.Mechanism.Part(physics.Driver.PartId).Joint.Origin;
        _motorVisual?.SetPose(new System.Numerics.Vector3((float)axis[0], (float)axis[1], (float)axis[2]), motor.Angle * 180.0 / Math.PI);

        MotorText.Text =
            $"Мотор: U = {motor.Voltage:0.00} В, I = {motor.Current * 1000:0.0} мА, " +
            $"M = {motor.Torque * 1000:0.000} мН·м, ЭДС = {motor.BackEmf:0.00} В";
        MotorPowerText.Text =
            $"P эл = {motor.InputPower * 1000:0.0} мВт, P мех = {motor.MechanicalPower * 1000:0.0} мВт, " +
            $"нагрев I²R = {motor.CopperLoss * 1000:0.0} мВт  (Mпуск = {motor.StallTorque * 1000:0.0} мН·м, ωхх = {motor.NoLoadSpeed:0} рад/с)";

        BodiesText.Text = string.Join(Environment.NewLine, physics.Bodies.Select(DescribeBody));

        DriveText.Text = physics.IsMotorDriven ? DescribeMotorDrive(physics) : DescribeHandDrive(physics);
    }

    private static string DescribeMotorDrive(MechanismSimulator physics)
    {
        string force = physics.TangentialForce > 0.0 ? $", окружная сила {physics.TangentialForce:0.000} Н" : "";
        return physics.IsStalled
            ? $"Ведущий вал: {physics.DriveTorque * 1000:0.000} мН·м — МОТОР ЗАСТОПОРЕН (нагрузка выше пускового момента)"
            : $"Ведущий вал: передаваемый момент {physics.DriveTorque * 1000:0.000} мН·м{force}, " +
              $"J прив = {physics.EffectiveInertia * 1e6:0.000} г·см²";
    }

    private static string DescribeHandDrive(MechanismSimulator physics)
    {
        bool prismatic = physics.Driver.IsPrismatic;
        string hand = prismatic
            ? $"Рука: {physics.HandEffort:0.00} Н (не больше {physics.HandLimit:0.#} Н)"
            : $"Рука: {physics.HandEffort * 1000:0.0} мН·м (не больше {physics.HandLimit * 1000:0} мН·м)";

        string spring = physics.Mechanism.Driver.Spring is null ? ""
            : prismatic ? $", пружина {physics.SpringForce:0.00} Н" : $", пружина {physics.SpringForce * 1000:0.0} мН·м";

        string range = "";
        if (physics.Range is { } r)
        {
            double k = prismatic ? 1e3 : 180.0 / Math.PI;
            string unit = prismatic ? "мм" : "°";
            range = $", ход {r.Min * k:0.#}…{r.Max * k:0.#} {unit}";
        }

        return hand + spring + range;
    }

    private void Viewport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_scene is null || _freeCamera.IsActive)
        {
            return;
        }

        Point position = e.GetPosition(Viewport.Viewport);
        (string? part, int? fastener) = ItemUnderCursor(position);

        // Клик по крепежу выбирает его в спецификации, по пустому месту — снимает выбор.
        if (fastener is { } index)
        {
            FastenerList.SelectedItem = FastenerList.Items.Cast<FastenerRow>().FirstOrDefault(r => r.Index == index);
            FastenerList.ScrollIntoView(FastenerList.SelectedItem);
        }
        else if (part is null)
        {
            FastenerList.SelectedItem = null;
        }

        if (_driver.HandlePart is not { } handle || part != handle || Viewport.Viewport.GetRay(position) is not { } ray)
        {
            return;
        }

        _dragging = true;
        Viewport.CaptureMouse();
        _driver.Grab(ray);
        _scene.SetHover(handle);
        e.Handled = true; // камера не должна вращаться, пока держим деталь
    }

    private void Viewport_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_scene is null || _driver.HandlePart is not { } handle)
        {
            return;
        }

        Point position = e.GetPosition(Viewport.Viewport);

        if (_dragging)
        {
            if (Viewport.Viewport.GetRay(position) is { } ray)
            {
                _driver.Drag(ray);
            }

            e.Handled = true;
            return;
        }

        // Подсветка и курсор-«рука» над деталью, за которую можно взяться.
        string? hover = !_freeCamera.IsActive && ItemUnderCursor(position).Part == handle ? handle : null;
        if (hover != _hoverPart)
        {
            _hoverPart = hover;
            _scene.SetHover(hover);
            Viewport.Cursor = hover is null ? null : Cursors.Hand;
        }
    }

    private void Viewport_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            EndDrag();
            e.Handled = true;
        }
    }

    private void Viewport_LostMouseCapture(object sender, MouseEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        _driver.Release();
        _scene?.SetHover(_hoverPart);

        if (Viewport.IsMouseCaptured)
        {
            Viewport.ReleaseMouseCapture();
        }
    }

    private (string? Part, int? Fastener) ItemUnderCursor(Point position)
    {
        HitTestResult? hit = VisualTreeHelper.HitTest(Viewport.Viewport, position);
        return hit is RayHitTestResult ray && _scene is not null ? _scene.ItemAt(ray.VisualHit) : (null, null);
    }

    private void FastenerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _scene?.SelectFastener((FastenerList.SelectedItem as FastenerRow)?.Index);
    }

    private static string DescribeBody(PhysicsBody body)
    {
        if (body.IsPrismatic)
        {
            return $"{body.Name}: v = {body.Velocity * 1000:0.0} мм/с, x = {body.AbsoluteCoordinate * 1000:0.0} мм";
        }

        double rpm = body.Velocity * 60.0 / (2.0 * Math.PI);
        double degrees = body.AbsoluteCoordinate * 180.0 / Math.PI;
        double angle = ((degrees % 360.0) + 360.0) % 360.0;
        return $"{body.Name}: ω = {body.Velocity:0.00} рад/с ({rpm:0.0} об/мин), φ = {angle:0.0}°";
    }

    // Органы управления срабатывают ещё в InitializeComponent, до загрузки механизма, — отсюда «?.».
    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _physics?.Motor.TargetSpeed = e.NewValue;
    }

    private void VoltageSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _physics?.Motor.SupplyVoltage = e.NewValue;
    }

    private void SpeedControlCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _physics?.Motor.SpeedControlEnabled = ((System.Windows.Controls.CheckBox)sender).IsChecked == true;
    }

    private void LoadSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _physics?.OutputLoad = e.NewValue * _loadScale;
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        _running = true;
        SetStatus("Физическая симуляция запущена");
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        _running = false;
        SetStatus("Физическая симуляция на паузе");
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        EndDrag();
        _running = _driver.RunsContinuously;
        _physics?.Reset();
        SyncVisuals();
        Viewport.ZoomExtents();
        SetStatus("Состояние сброшено");
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.F && !e.IsRepeat)
        {
            _freeCamera.Toggle();
            e.Handled = true;
        }
        else if (key == Key.Escape && _freeCamera.IsActive)
        {
            _freeCamera.Deactivate();
            e.Handled = true;
        }
        else if (_freeCamera.HandleKeyDown(key))
        {
            e.Handled = true;
        }
    }

    private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_freeCamera.HandleKeyUp(key))
        {
            e.Handled = true;
        }
    }

    private void FreeCameraToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (FreeCameraToggle.IsChecked == true)
        {
            _freeCamera.Activate();
        }
        else
        {
            _freeCamera.Deactivate();
        }

        // Если включить не удалось (нет перспективной камеры), кнопка не должна остаться нажатой.
        FreeCameraToggle.IsChecked = _freeCamera.IsActive;
    }

    private void FreeCamera_StateChanged(object? sender, EventArgs e)
    {
        FreeCameraToggle.IsChecked = _freeCamera.IsActive;
        FlyHelp.Visibility = _freeCamera.IsActive ? Visibility.Visible : Visibility.Collapsed;
        FlySpeedText.Text = $"Скорость полёта: {_freeCamera.Speed:0} мм/с";

        if (_freeCamera.IsActive)
        {
            SetStatus("Свободная камера: удерживайте ПКМ для обзора, WASD — движение");
        }
        else
        {
            SetStatus("Орбитальная камера");
        }
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _renderTimer.Stop();
        _freeCamera.Deactivate(); // отписка от CompositionTarget.Rendering
    }
}
