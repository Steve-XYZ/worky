using System.IO;
using System.Text.Json;

namespace Worky.Core.Graph;

public sealed class GraphStateFileStore
{
    public static readonly string DefaultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".worky");

    public static string DefaultPath => Path.Combine(DefaultDirectory, "state.json");

    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    readonly string _directory;
    readonly string _path;

    public GraphStateFileStore()
        : this(DefaultDirectory) { }

    public GraphStateFileStore(string directory)
    {
        _directory = directory;
        _path = Path.Combine(directory, "state.json");
    }

    public GraphState? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            return JsonSerializer.Deserialize<GraphState>(File.ReadAllText(_path), JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(GraphState state)
    {
        Directory.CreateDirectory(_directory);

        var tempPath = Path.Combine(_directory, $".state.json.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var temp = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(temp, state, JsonOptions);
            }
            File.Move(tempPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
