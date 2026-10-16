using HelixToolkit.Wpf;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Media3D;

namespace MechanismViewer.Visuals;

/// <summary>
/// Кэш сеток деталей из STL. Файл разбирается один раз, сетка замораживается и делится между всеми деталями
/// с тем же содержимым: обе шестерни пары, повторное открытие механизма, одинаковые детали разных механизмов.
/// Ключ — хеш содержимого файла, а не путь, поэтому одинаковые файлы в разных папках дают одну сетку.
/// Ссылки слабые: сетка живёт, пока её показывает хоть одна сцена, и не держит память после закрытия.
/// Замороженная сетка доступна из любого потока и не тратит память на уведомления об изменениях.
/// </summary>
public static class MeshCache
{
    private static readonly Dictionary<string, WeakReference<MeshGeometry3D>[]> Entries = new();
    private static readonly Lock Sync = new();

    /// <summary>Сетки STL-файла (по содержимому).</summary>
    public static IReadOnlyList<MeshGeometry3D> Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        return GetOrAdd(data, () =>
        {
            Model3DGroup model = new StLReader().Read(new MemoryStream(data))
                ?? throw new InvalidDataException($"Не удалось прочитать STL: {path}");

            return model.Children
                .OfType<GeometryModel3D>()
                .Select(g => g.Geometry)
                .OfType<MeshGeometry3D>()
                .ToArray();
        });
    }

    private static IReadOnlyList<MeshGeometry3D> GetOrAdd(byte[] content, Func<MeshGeometry3D[]> build)
    {
        string key = Convert.ToHexString(SHA256.HashData(content));

        lock (Sync)
        {
            if (Entries.TryGetValue(key, out WeakReference<MeshGeometry3D>[]? refs) && TryGetAll(refs) is { } cached)
            {
                return cached;
            }
        }

        MeshGeometry3D[] meshes = build();
        foreach (MeshGeometry3D mesh in meshes)
        {
            mesh.Freeze();
        }

        lock (Sync)
        {
            foreach (string dead in Entries.Where(e => TryGetAll(e.Value) is null).Select(e => e.Key).ToList())
            {
                Entries.Remove(dead);
            }

            Entries[key] = meshes.Select(m => new WeakReference<MeshGeometry3D>(m)).ToArray();
        }

        return meshes;
    }

    private static MeshGeometry3D[]? TryGetAll(WeakReference<MeshGeometry3D>[] refs)
    {
        var meshes = new MeshGeometry3D[refs.Length];
        for (int i = 0; i < refs.Length; i++)
        {
            if (!refs[i].TryGetTarget(out MeshGeometry3D? mesh))
            {
                return null;
            }

            meshes[i] = mesh;
        }

        return meshes;
    }
}
