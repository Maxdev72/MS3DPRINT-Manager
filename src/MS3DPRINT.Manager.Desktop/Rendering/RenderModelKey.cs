using MS3DPRINT.Manager.Desktop.Viewer;

namespace MS3DPRINT.Manager.Desktop.Rendering;

/// <summary>
/// The key of a part of the scene: a model file, or "file.3mf|n" for object n of a 3MF taken apart (« | » cannot be in a
/// Windows file name). Orientations, painted zones and cameras are remembered under this key.
/// </summary>
internal static class RenderModelKey
{
    public static string File(string key) => key.Split('|')[0];

    public static string For(string file, int index) => file + "|" + index;

    public static MeshModel Load(string key)
    {
        var bar = key.IndexOf('|');
        if (bar < 0) return MeshLoader.Load(key);
        var objects = ThreeMfReader.LoadObjects(key[..bar]);
        return int.TryParse(key[(bar + 1)..], out var index) && index >= 0 && index < objects.Count
            ? objects[index].Mesh : throw new InvalidDataException("Cet objet n’est plus dans le fichier 3MF.");
    }

    public static string Name(string key)
    {
        var bar = key.IndexOf('|');
        return bar < 0 ? Path.GetFileNameWithoutExtension(key)
            : $"{Path.GetFileNameWithoutExtension(key[..bar])} · objet {(int.TryParse(key[(bar + 1)..], out var index) ? index + 1 : 0)}";
    }
}
