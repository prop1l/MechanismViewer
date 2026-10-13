using HelixToolkit.Wpf;

namespace MechanismViewer.Interaction;

/// <summary>Мотор крутит механизм сам; управление — слайдерами питания, скорости и нагрузки.</summary>
public sealed class MotorDriver : IDriver
{
    public string Hint => "Привод от мотора: «Запустить», питание и скорость — на панели сверху";
    public bool RunsContinuously => false;
    public string? HandlePart => null;

    public void Grab(Ray3D ray) { }
    public void Drag(Ray3D ray) { }
    public void Release() { }
}
