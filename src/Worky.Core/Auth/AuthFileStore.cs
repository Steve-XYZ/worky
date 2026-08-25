using System.IO;
using System.Text.Json;

namespace Worky.Core.Auth;

public sealed class AuthFileStore : IAuthSessionStore
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    readonly string _directory;
    readonly string _path;

    public AuthFileStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".worky")) { }

    public AuthFileStore(string directory)
    {
        _directory = directory;
        _path = Path.Combine(directory, "auth.json");
    }

    public AuthSession? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            return JsonSerializer.Deserialize<AuthSession>(File.ReadAllText(_path), JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(AuthSession session)
    {
        Directory.CreateDirectory(_directory);
        FilePermissions.ApplyOwnerOnlyToDirectory(_directory);

        var tempPath = Path.Combine(_directory, $".auth.json.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var temp = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                FilePermissions.ApplyOwnerOnlyToFile(temp.SafeFileHandle);
                JsonSerializer.Serialize(temp, session, JsonOptions);
            }
            File.Move(tempPath, _path, overwrite: true);
            FilePermissions.ApplyOwnerOnlyToFile(_path);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
