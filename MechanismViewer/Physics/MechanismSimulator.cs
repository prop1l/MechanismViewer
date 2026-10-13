using MechanismViewer.Catalog;
using MechanismViewer.Kinematics;

namespace MechanismViewer.Physics;

/// <summary>
/// Механизм с одной степенью свободы q (координата ведущей детали). Ведущую деталь крутит
/// двигатель постоянного тока или пользователь (рычаг, ручка, кнопка).
///
/// Координаты остальных деталей q_i(q) и передаточные функции k_i = dq_i/dq даёт <see cref="KinematicSolver"/>.
/// Параметры, приведённые к ведущей координате (принцип виртуальной работы):
///     J(q) = Σ J_i·k_i²,   b(q) = Σ b_i·k_i²,   c(q) = Σ c_i·|k_i| + |k_вых|·|нагрузка|
/// Уравнения вместе с током якоря (без мотора Kt = 0):
///     L·i'  = U − R·i − Ke·ω
///     J·ω'  = Kt·i − b·ω − c·sign(ω) − ½·J'(q)·ω² + F_внеш
/// Внешняя сила F_внеш = f0 − k·q − d·ω: возвратная пружина и рука. Рука тянет к целевому положению
/// как жёсткая пружина с критическим демпфером, но не сильнее HandLimit.
/// Линейная часть (ток, вязкое трение, пружины) решается неявно, сухое трение и ½·J'·ω² — явно.
/// Для передач с постоянным отношением J' = 0 и система совпадает с зубчатой парой.
/// </summary>
public sealed class MechanismSimulator
{
    private const double FixedStep = 1.0 / 480.0;
    private const double MaxFrameTime = 0.1;
    private const double StopSpeed = 1e-4; // рад/с
    private const double DerivativeStep = 1e-5; // рад

    /// <summary>Наименьшая собственная частота «руки» при перетаскивании, Гц.</summary>
    public const double HandFrequencyHz = 8.0;

    /// <summary>Отклонение от курсора, при котором рука развивает полное усилие: 0,5° или 0,2 мм.</summary>
    public const double HandToleranceRad = 0.5 * Math.PI / 180.0;
    public const double HandToleranceM = 0.2e-3;

    private readonly PartState[] _state;
    private readonly int _outputIndex;
    private readonly (double Min, double Max)? _range;
    private readonly (double Stiffness, double Preload)? _spring;
    private double _accumulator;

    public MechanismDefinition Mechanism { get; }
    public KinematicSolver Kinematics { get; }

    /// <summary>Подвижные детали в порядке <see cref="KinematicSolver.Parts"/>; [0] — ведущая.</summary>
    public IReadOnlyList<PhysicsBody> Bodies { get; }

    public PhysicsBody Driver => Bodies[0];
    public PhysicsBody? Output => _outputIndex >= 0 ? Bodies[_outputIndex] : null;

    /// <summary>Ведущую деталь крутит мотор (иначе — пользователь).</summary>
    public bool IsMotorDriven { get; }

    /// <summary>Двигатель на валу ведущей детали (участвует, только если <see cref="IsMotorDriven"/>).</summary>
    public PhysicsMotor Motor { get; } = new();

    /// <summary>Сопротивление на выходной детали: Н·м (вращение) или Н (сдвиг).</summary>
    public double OutputLoad { get; set; }

    /// <summary>Пределы ведущей координаты от начального положения (рад или м), если есть упоры.</summary>
    public (double Min, double Max)? Range => _range;

    /// <summary>Наибольшее усилие руки: Н·м или Н.</summary>
    public double HandLimit { get; }

    /// <summary>Целевое положение ведущей координаты, к которому тянет рука; null — рука отпущена.</summary>
    public double? HandTarget { get; set; }

    /// <summary>Постоянное усилие руки (нажатие кнопки), Н·м или Н; не больше <see cref="HandLimit"/>.</summary>
    public double HandForce { get; set; }

    /// <summary>Усилие, которое рука приложила на последнем шаге.</summary>
    public double HandEffort { get; private set; }

    /// <summary>Усилие возвратной пружины на последнем шаге.</summary>
    public double SpringForce { get; private set; }

    /// <summary>Мотор: момент, который ведущий вал передаёт механизму (по модулю), Н·м; рука — |усилие руки|.</summary>
    public double DriveTorque { get; private set; }

    /// <summary>Окружная сила на делительной окружности ведущей шестерни, Н (0, если ведущая — не шестерня).</summary>
    public double TangentialForce { get; private set; }

    /// <summary>Мотор отдаёт всё напряжение питания, но вал стоит — нагрузка выше пускового момента.</summary>
    public bool IsStalled { get; private set; }

    /// <summary>Приведённый момент инерции (кг·м²) или масса (кг) в текущем положении.</summary>
    public double EffectiveInertia { get; private set; }

    public double Time { get; private set; }

    public MechanismSimulator(MechanismDefinition mechanism)
    {
        Mechanism = mechanism;
        Kinematics = new KinematicSolver(mechanism);
        Bodies = Kinematics.Parts.Select(PhysicsBody.FromPart).ToList();
        _state = new PartState[Bodies.Count];
        _outputIndex = mechanism.Output is null ? Bodies.Count - 1 : Kinematics.IndexOf(mechanism.Output);

        DriverSpec spec = mechanism.Driver;
        IsMotorDriven = spec.Type == "motor";

        // Перевод из единиц шарнира (° и Н·м или мм и Н) в СИ ведущей координаты.
        bool prismatic = Driver.IsPrismatic;
        double toSi = prismatic ? 1e-3 : Math.PI / 180.0;

        if (spec.Range is [double min, double max])
        {
            _range = (Math.Min(min, max) * toSi, Math.Max(min, max) * toSi);
        }

        if (spec.Spring is { } spring)
        {
            _spring = (spring.Stiffness / toSi, spring.Preload);
        }

        HandLimit = spec.HandForce ?? (prismatic ? 10.0 : 0.5);

        Reset();
    }

    public void Step(double elapsedSeconds)
    {
        _accumulator = Math.Min(_accumulator + elapsedSeconds, MaxFrameTime);

        while (_accumulator >= FixedStep)
        {
            Integrate(FixedStep);
            _accumulator -= FixedStep;
        }
    }

    private void Integrate(double dt)
    {
        PhysicsMotor m = Motor;
        PhysicsBody driver = Driver;
        double q = driver.Coordinate;
        double w = driver.Velocity;

        Kinematics.Solve(q, _state);
        (double jEff, double bEff, double cEff) = Reduce(_state);
        double dJ = (InertiaAt(q + DerivativeStep) - InertiaAt(q - DerivativeStep)) / (2.0 * DerivativeStep);
        double inertial = -0.5 * dJ * w * w;

        // Вал ведущей детали: ротор + сама деталь.
        double jShaft = (IsMotorDriven ? m.RotorInertia : 0.0) + driver.Inertia;
        double bShaft = (IsMotorDriven ? m.ViscousFriction : 0.0) + driver.ViscousFriction;
        double cShaft = (IsMotorDriven ? m.CoulombFriction : 0.0) + driver.CoulombFriction;

        double kt = IsMotorDriven ? m.TorqueConstant : 0.0;
        double ke = kt;
        double i0 = m.Current;
        double u = IsMotorDriven ? m.ComputeVoltage(dt) : 0.0;

        // Электрическое уравнение (неявный Эйлер): a·i1 + Ke·ω1 = q_e
        double a = m.Inductance / dt + m.Resistance;
        double qe = u + m.Inductance / dt * i0;

        // Ток при заблокированном вале — по нему проверяем, сорвёт ли привод сухое трение.
        double iLocked = qe / a;

        // Внешняя сила F = force − stiffness·q1 − damping·ω1, где q1 = q + ω1·dt (неявно).
        (double W, double I) SolveStep(double force, double stiffness, double damping)
        {
            // Механическое уравнение (неявный Эйлер): g·ω1 − Kt·i1 = (J/dt)·ω0 − F_сух + F_инерц + F_внеш
            double g = jEff / dt + bEff + damping + stiffness * dt;
            double external = force - stiffness * q; // внешняя сила при ω1 = 0
            double driveLocked = kt * iLocked + inertial + external;

            if (Math.Abs(w) < StopSpeed && Math.Abs(driveLocked) <= cEff)
            {
                return (0.0, iLocked);
            }

            double dir = Math.Abs(w) >= StopSpeed ? Math.Sign(w) : Math.Sign(driveLocked);
            double p = jEff / dt * w - cEff * dir + inertial + external;

            double i1 = (qe - ke * p / g) / (a + ke * kt / g);
            double w1 = (p + kt * i1) / g;

            // Сухое трение не может развернуть вращение.
            if (w * w1 < 0.0 && Math.Abs(kt * i1 + inertial + external) <= cEff)
            {
                return (0.0, iLocked);
            }

            return (w1, i1);
        }

        double springStiffness = _spring?.Stiffness ?? 0.0;
        double springForce = -(_spring?.Preload ?? 0.0);
        double handConstant = IsMotorDriven ? 0.0 : Math.Clamp(HandForce, -HandLimit, HandLimit);
        double handStiffness = 0.0;
        double handDamping = 0.0;
        double handTarget = 0.0;
        double wNew;
        double iNew;

        if (!IsMotorDriven && HandTarget is { } target)
        {
            // Не мягче собственной частоты HandFrequencyHz и не мягче «полное усилие на HandTolerance»,
            // иначе у лёгких деталей трение оставляет заметный недоход до курсора.
            double omega = 2.0 * Math.PI * HandFrequencyHz;
            double tolerance = driver.IsPrismatic ? HandToleranceM : HandToleranceRad;
            double kH = Math.Max(jEff * omega * omega, HandLimit / tolerance);
            double dH = 2.0 * Math.Sqrt(kH * jEff);

            (wNew, iNew) = SolveStep(springForce + handConstant + kH * target, springStiffness + kH, dH);
            double handPull = kH * (target - (q + wNew * dt)) - dH * wNew;

            if (Math.Abs(handPull) <= HandLimit)
            {
                (handStiffness, handDamping, handTarget) = (kH, dH, target);
            }
            else
            {
                // Рука не справляется — шаг пересчитывается с предельным усилием в ту же сторону.
                handConstant += Math.Sign(handPull) * HandLimit;
                (wNew, iNew) = SolveStep(springForce + handConstant, springStiffness, 0.0);
            }
        }
        else
        {
            (wNew, iNew) = SolveStep(springForce + handConstant, springStiffness, 0.0);
        }

        double qNew = q + wNew * dt;

        // Упоры: неупругий удар — скорость в сторону упора гасится.
        if (_range is { } range)
        {
            if (qNew >= range.Max)
            {
                qNew = range.Max;
                wNew = Math.Min(wNew, 0.0);
            }
            else if (qNew <= range.Min)
            {
                qNew = range.Min;
                wNew = Math.Max(wNew, 0.0);
            }

            // Ходовой винт: доехал до конца — мотор крутит обратно.
            if (IsMotorDriven && Mechanism.Driver.ReverseAtLimits)
            {
                if (qNew >= range.Max)
                {
                    m.Direction = -1.0;
                }
                else if (qNew <= range.Min)
                {
                    m.Direction = 1.0;
                }
            }
        }

        double accel = (wNew - w) / dt;
        double drive = kt * iNew;

        m.Voltage = u;
        m.Current = iNew;
        m.AngularVelocity = wNew;
        m.Angle = qNew;

        ApplyCoordinate(qNew, wNew);

        HandEffort = handStiffness * (handTarget - qNew) - handDamping * wNew + handConstant;
        SpringForce = _spring is { } s ? -(s.Preload + s.Stiffness * qNew) : 0.0;

        if (IsMotorDriven)
        {
            // Момент, который вал передаёт остальному механизму (для индикации).
            double frictionShaft = wNew == 0.0
                ? Math.Sign(drive) * Math.Min(Math.Abs(drive), cShaft)
                : cShaft * Math.Sign(wNew);

            DriveTorque = Math.Abs(jShaft * accel - drive + bShaft * wNew + frictionShaft);
        }
        else
        {
            DriveTorque = Math.Abs(HandEffort);
        }

        TangentialForce = driver.PitchRadiusM is { } r && !driver.IsPrismatic ? DriveTorque / r : 0.0;
        EffectiveInertia = jEff;
        // Стопор — только когда мотор уже выдаёт всё, что может; короткая пауза при трогании не считается.
        IsStalled = IsMotorDriven && wNew == 0.0 && m.SupplyVoltage > 1e-3 && Math.Abs(u) >= m.SupplyVoltage - 1e-6;
        Time += dt;
    }

    /// <summary>Приведённые к ведущей координате инерция, вязкое и сухое трение (с ротором мотора и нагрузкой).</summary>
    private (double J, double B, double C) Reduce(ReadOnlySpan<PartState> state)
    {
        double j = IsMotorDriven ? Motor.RotorInertia : 0.0;
        double b = IsMotorDriven ? Motor.ViscousFriction : 0.0;
        double c = IsMotorDriven ? Motor.CoulombFriction : 0.0;

        for (int i = 0; i < Bodies.Count; i++)
        {
            double k = state[i].K;
            PhysicsBody body = Bodies[i];
            j += body.Inertia * k * k;
            b += body.ViscousFriction * k * k;
            c += body.CoulombFriction * Math.Abs(k);
        }

        if (_outputIndex >= 0)
        {
            c += Math.Abs(state[_outputIndex].K) * Math.Abs(OutputLoad);
        }

        return (j, b, c);
    }

    private double InertiaAt(double q)
    {
        Span<PartState> state = stackalloc PartState[Bodies.Count];
        Kinematics.Solve(q, state);
        return Reduce(state).J;
    }

    private void ApplyCoordinate(double q, double w)
    {
        Kinematics.Solve(q, _state);

        for (int i = 0; i < Bodies.Count; i++)
        {
            Bodies[i].Coordinate = _state[i].Q;
            Bodies[i].Ratio = _state[i].K;
            Bodies[i].Velocity = _state[i].K * w;
        }
    }

    public void Reset()
    {
        Motor.Reset();
        ApplyCoordinate(0.0, 0.0);

        HandTarget = null;
        HandForce = 0.0;
        HandEffort = 0.0;
        SpringForce = _spring is { } s ? -s.Preload : 0.0;
        DriveTorque = 0.0;
        TangentialForce = 0.0;
        IsStalled = false;
        Time = 0.0;
        _accumulator = 0.0;
        Kinematics.Solve(0.0, _state);
        EffectiveInertia = Reduce(_state).J;
    }
}
