using HelixToolkit.Wpf;
using MechanismViewer.Physics;

namespace MechanismViewer.Interaction;

/// <summary>
/// Способ, которым приводится ведущая деталь: мотор или рука пользователя.
/// Окно передаёт сюда луч под курсором, драйвер переводит его в воздействие на <see cref="MechanismSimulator"/>.
/// </summary>
public interface IDriver
{
    /// <summary>Подсказка для пользователя.</summary>
    string Hint { get; }

    /// <summary>true — симуляция идёт всегда (ручной привод), false — по кнопке «Запустить» (мотор).</summary>
    bool RunsContinuously { get; }

    /// <summary>Деталь, за которую берётся пользователь, или null.</summary>
    string? HandlePart { get; }

    /// <summary>Пользователь нажал ЛКМ на <see cref="HandlePart"/>.</summary>
    void Grab(Ray3D ray);

    void Drag(Ray3D ray);

    void Release();
}

public static class DriverFactory
{
    public static IDriver Create(MechanismSimulator simulator) => simulator.Mechanism.Driver.Type switch
    {
        "motor" => new MotorDriver(),
        "lever" or "crank" => new HandDragDriver(simulator),
        "button" => new ButtonDriver(simulator),
        string type => throw new InvalidOperationException($"Механизм «{simulator.Mechanism.Id}»: неизвестный привод «{type}»")
    };
}
