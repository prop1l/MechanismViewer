using MechanismViewer.Catalog;
using MechanismViewer.Visuals;

namespace MechanismViewer;

/// <summary>Строка спецификации крепежа в панели окна.</summary>
public sealed class FastenerRow(int index, FastenerSpec spec, MechanismDefinition mechanism, bool hasVisual)
{
    public int Index { get; } = index;

    public string Title => $"{spec.Quantity} × {spec.Kind}";

    public string Detail => FastenerModel.StandardName(spec.Type) is { } standard
        ? $"{spec.Size} · {standard}"
        : spec.Size;

    /// <summary>Где искать в модели: на какой детали и показан ли сам элемент.</summary>
    public string Where
    {
        get
        {
            string? part = spec.AttachedTo is { } id ? mechanism.Part(id) is { } p && p.Name.Length > 0 ? p.Name : id : null;
            string shown = hasVisual ? "показан в модели" : spec.Note ?? "только в спецификации";
            return part is null ? shown : $"{part} · {shown}";
        }
    }
}
