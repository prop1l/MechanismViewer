namespace MechanismViewer.Kinematics.Couplings;

/// <summary>Винт–гайка: x = ход·θ/2π (ход за оборот, м).</summary>
public sealed class ScrewCoupling(string from, string to, double leadM) : LinearCoupling(from, to, leadM / (2.0 * Math.PI));
