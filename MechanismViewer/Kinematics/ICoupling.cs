namespace MechanismViewer.Kinematics;

/// <summary>
/// Связь координаты ведомой детали с координатой ведущей.
/// Координаты отсчитываются от начального положения деталей, в СИ: рад (revolute) или м (prismatic).
/// </summary>
public interface ICoupling
{
    string From { get; }
    string To { get; }

    /// <summary>q_to(q_from).</summary>
    double Evaluate(double qFrom);

    /// <summary>dq_to/dq_from в точке q_from.</summary>
    double Derivative(double qFrom);
}

/// <summary>Связь с постоянным передаточным отношением: q_to = k·q_from.</summary>
public abstract class LinearCoupling(string from, string to, double factor) : ICoupling
{
    public string From { get; } = from;
    public string To { get; } = to;

    /// <summary>dq_to/dq_from.</summary>
    public double Factor { get; } = factor;

    public double Evaluate(double qFrom) => Factor * qFrom;
    public double Derivative(double qFrom) => Factor;
}
