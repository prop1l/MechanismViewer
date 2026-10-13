namespace MechanismViewer.Kinematics.Couplings;

/// <summary>Шестерня–рейка: x = r·θ (r — радиус делительной окружности, м). Ведущей может быть любая из двух.</summary>
public sealed class RackPinionCoupling(string from, string to, double factor) : LinearCoupling(from, to, factor)
{
    public static RackPinionCoupling Create(string from, string to, bool rackDrivesPinion, double pitchRadiusM) =>
        new(from, to, rackDrivesPinion ? 1.0 / pitchRadiusM : pitchRadiusM);
}
