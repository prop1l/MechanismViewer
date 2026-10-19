namespace MechanismViewer.Kinematics.Couplings;

/// <summary>
/// Ременная передача без проскальзывания: θ_to = ratio·θ_from, ratio = r_from/r_to.
/// Открытый ремень сохраняет направление вращения (ratio &gt; 0), перекрёстный — меняет.
/// </summary>
public sealed class BeltCoupling(string from, string to, double ratio) : LinearCoupling(from, to, ratio);
