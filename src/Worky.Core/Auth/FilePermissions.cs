using System.IO;
using Microsoft.Win32.SafeHandles;

namespace Worky.Core.Auth;

public static class FilePermissions
{
    public static void ApplyOwnerOnlyToFile(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static void ApplyOwnerOnlyToFile(SafeFileHandle handle)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(handle, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static void ApplyOwnerOnlyToDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
