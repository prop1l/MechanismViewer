namespace MechanismViewer.Physics;

/// <summary>
/// Коллекторный двигатель постоянного тока с ПИ-регулятором скорости.
///
/// Электрическая цепь якоря:  L·di/dt = U − R·i − Ke·ω
/// Электромагнитный момент:   M = Kt·i
/// Ротор жёстко посажен на вал ведущей детали, поэтому механическое уравнение
/// решается в <see cref="MechanismSimulator"/> вместе со всем механизмом.
/// Параметры соответствуют небольшому мотору типоразмера «380» (Ø27,7 мм).
/// </summary>
public sealed class PhysicsMotor
{
    /// <summary>Сопротивление обмотки якоря, Ом.</summary>
    public double Resistance { get; init; } = 20.0;

    /// <summary>Индуктивность якоря, Гн.</summary>
    public double Inductance { get; init; } = 0.01;

    /// <summary>Постоянная момента, Н·м/А (в СИ равна постоянной ЭДС Ke, В·с/рад).</summary>
    public double TorqueConstant { get; init; } = 0.05;

    /// <summary>Момент инерции ротора, кг·м².</summary>
    public double RotorInertia { get; init; } = 1e-6;

    /// <summary>Вязкое трение в подшипниках мотора, Н·м·с/рад.</summary>
    public double ViscousFriction { get; init; } = 1e-6;

    /// <summary>Сухое трение (щётки + подшипники), Н·м.</summary>
    public double CoulombFriction { get; init; } = 5e-5;

    /// <summary>Напряжение источника питания, В. Регулятор не может выдать больше.</summary>
    public double SupplyVoltage { get; set; } = 3.0;

    /// <summary>true — ПИ-регулятор держит заданную скорость; false — на якорь подано всё напряжение питания.</summary>
    public bool SpeedControlEnabled { get; set; } = true;

    /// <summary>Заданная скорость вала, рад/с.</summary>
    public double TargetSpeed { get; set; } = 6.28;

    /// <summary>Пропорциональный коэффициент регулятора, В/(рад/с).</summary>
    public double Kp { get; init; } = 0.2;

    /// <summary>Интегральный коэффициент регулятора, В/рад.</summary>
    public double Ki { get; init; } = 2.0;

    private double _integral;

    /// <summary>Направление вращения: +1 или −1 (реверс меняет знак заданной скорости и напряжения).</summary>
    public double Direction { get; internal set; } = 1.0;

    public double Voltage { get; internal set; }
    public double Current { get; internal set; }
    public double Angle { get; internal set; }
    public double AngularVelocity { get; internal set; }

    public double BackEmf => TorqueConstant * AngularVelocity;
    public double Torque => TorqueConstant * Current;

    /// <summary>Электрическая мощность, потребляемая от источника, Вт.</summary>
    public double InputPower => Voltage * Current;

    /// <summary>Механическая мощность на валу (до трения), Вт.</summary>
    public double MechanicalPower => Torque * AngularVelocity;

    /// <summary>Потери в меди I²R, Вт.</summary>
    public double CopperLoss => Current * Current * Resistance;

    /// <summary>Скорость холостого хода при текущем питании, рад/с.</summary>
    public double NoLoadSpeed => SupplyVoltage / TorqueConstant;

    /// <summary>Пусковой момент при текущем питании, Н·м.</summary>
    public double StallTorque => TorqueConstant * SupplyVoltage / Resistance;

    internal double ComputeVoltage(double dt)
    {
        double limit = Math.Max(SupplyVoltage, 0.0);

        if (!SpeedControlEnabled)
        {
            _integral = 0.0;
            return Direction * limit;
        }

        double error = Direction * TargetSpeed - AngularVelocity;
        double proportional = Kp * error;
        double candidate = _integral + Ki * error * dt;

        // Anti-windup (ограничение интегратора): интеграл растёт только до точки,
        // где выход регулятора достигает напряжения питания, но не уменьшается из-за насыщения.
        // Так при упоре выход равен ровно ±U_пит, а не «застревает» чуть ниже.
        if (candidate > _integral)
        {
            _integral = Math.Max(_integral, Math.Min(candidate, limit - proportional));
        }
        else
        {
            _integral = Math.Min(_integral, Math.Max(candidate, -limit - proportional));
        }

        _integral = Math.Clamp(_integral, -limit, limit);
        return Math.Clamp(proportional + _integral, -limit, limit);
    }

    internal void Reset()
    {
        _integral = 0.0;
        Direction = 1.0;
        Voltage = 0.0;
        Current = 0.0;
        Angle = 0.0;
        AngularVelocity = 0.0;
    }
}
