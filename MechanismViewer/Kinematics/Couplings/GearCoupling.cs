namespace MechanismViewer.Kinematics.Couplings;

/// <summary>Зубчатое зацепление (цилиндрическое, коническое, червячное): θ_to = ratio·θ_from.</summary>
public sealed class GearCoupling(string from, string to, double ratio) : LinearCoupling(from, to, ratio);
