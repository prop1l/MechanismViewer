using System.IO;
using System.Text.Json;

namespace MechanismViewer.Catalog;

/// <summary>Сканирует <c>Catalog/*/mechanism.json</c> и загружает описания механизмов.</summary>
public static class CatalogLoader
{
    public const string FileName = "mechanism.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string DefaultRoot => Path.Combine(AppContext.BaseDirectory, "Catalog");

    public static IReadOnlyList<MechanismDefinition> LoadAll(string root)
    {
        if (!System.IO.Directory.Exists(root))
        {
            return [];
        }

        return System.IO.Directory
            .EnumerateFiles(root, FileName, SearchOption.AllDirectories)
            .Select(Load)
            .OrderBy(m => m.Category)
            .ThenBy(m => m.Name)
            .ToList();
    }

    public static MechanismDefinition Load(string path)
    {
        MechanismDefinition definition;

        try
        {
            using FileStream stream = File.OpenRead(path);
            definition = JsonSerializer.Deserialize<MechanismDefinition>(stream, Options)
                ?? throw new InvalidDataException("пустой файл");
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"Ошибка в описании механизма {path}: {ex.Message}", ex);
        }

        definition = definition with { Directory = Path.GetDirectoryName(path)! };
        Validate(definition, path);
        return definition;
    }

    private static void Validate(MechanismDefinition m, string path)
    {
        var ids = new HashSet<string>();
        foreach (PartSpec part in m.Parts)
        {
            if (!ids.Add(part.Id))
            {
                throw new InvalidDataException($"{path}: деталь «{part.Id}» описана дважды");
            }

            if ((part.Stl is null) == (part.Shape is null))
            {
                throw new InvalidDataException($"{path}: у детали «{part.Id}» задайте либо stl, либо shape");
            }

            if (part.Shape is { } shape)
            {
                if (shape.Count == 0 || shape.Any(s => s.KindCount != 1))
                {
                    throw new InvalidDataException($"{path}: у детали «{part.Id}» в каждом элементе shape должен быть ровно один примитив");
                }

                if (part.ShapeAxis is not ("z" or "x" or "y"))
                {
                    throw new InvalidDataException($"{path}: у детали «{part.Id}» shapeAxis — z, x или y");
                }
            }

            if (part.Joint.Origin.Length != 3 || part.Joint.Axis.Length != 3)
            {
                throw new InvalidDataException($"{path}: у детали «{part.Id}» origin/axis должны быть из 3 чисел");
            }
        }

        IEnumerable<string> references = m.Couplings
            .SelectMany(c => new[] { c.From, c.To })
            .Concat(m.Parts.Select(p => p.Joint.Link).OfType<LinkSpec>().SelectMany(l => new[] { l.From, l.To }))
            .Append(m.Driver.Part);

        if (m.Output is not null)
        {
            references = references.Append(m.Output);
        }

        references = references.Concat(m.Fasteners.Select(f => f.AttachedTo).OfType<string>());

        foreach (FastenerSpec fastener in m.Fasteners.Where(f => f.Positions is { Length: > 0 }))
        {
            if (fastener.Positions!.Any(p => p.Length != 3) || fastener.Axis.Length != 3)
            {
                throw new InvalidDataException($"{path}: у крепежа «{fastener.Kind}» positions/axis должны быть из 3 чисел");
            }

            if (fastener.Stl is null && (fastener.Type is null || fastener.DiameterMm is null))
            {
                throw new InvalidDataException($"{path}: крепёж «{fastener.Kind}» с positions требует stl или type + diameterMm");
            }
        }

        foreach (string id in references)
        {
            if (!ids.Contains(id))
            {
                throw new InvalidDataException($"{path}: ссылка на несуществующую деталь «{id}»");
            }
        }

        foreach (PartSpec part in m.Parts.Where(p => p.Joint.Type == "link"))
        {
            LinkSpec link = part.Joint.Link
                ?? throw new InvalidDataException($"{path}: у шатуна «{part.Id}» нет поля link");

            if (m.Part(link.From).Joint.Type == "link" || m.Part(link.To).Joint.Type == "link")
            {
                throw new InvalidDataException($"{path}: шатун «{part.Id}» должен соединять детали с шарнирами, а не другие шатуны");
            }
        }
    }
}
