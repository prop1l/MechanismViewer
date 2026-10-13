using HelixToolkit.Wpf;
using MechanismViewer.Physics;

namespace MechanismViewer.Interaction;

/// <summary>
/// Кнопка: пока пользователь держит ЛКМ на кнопке, на неё давит усилие руки (driver.handForce);
/// после отпускания её возвращает пружина.
/// </summary>
public sealed class ButtonDriver(MechanismSimulator simulator) : IDriver
{
    public string Hint => "Кнопка: нажмите и держите её левой кнопкой мыши, отпустите — вернёт пружина";
    public bool RunsContinuously => true;
    public string? HandlePart => simulator.Driver.PartId;

    public void Grab(Ray3D ray) => simulator.HandForce = simulator.HandLimit;
    public void Drag(Ray3D ray) { }
    public void Release() => simulator.HandForce = 0.0;
}
