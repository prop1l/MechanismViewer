namespace MechanismViewer.Kinematics.Couplings;

/// <summary>Клин: сдвиг клина на x поднимает толкатель на x·tg α (α — угол клина). Выигрыш в силе без трения — 1/tg α.</summary>
public sealed class WedgeCoupling(string from, string to, double angleRad) : LinearCoupling(from, to, Math.Tan(angleRad));
