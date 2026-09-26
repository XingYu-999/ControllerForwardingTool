namespace ControllerForwardingTool.Core;

internal static class AppDataPaths
{
    // Resolve the Windows known folder, including redirected user profiles.
    internal static string LocalRoot
    {
        get
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
                throw new IOException("Windows 未能提供当前用户的 LocalAppData 目录。");
            return root;
        }
    }

    internal static string DirectoryPath => Path.Combine(LocalRoot, "ControllerForwardingTool");
    internal static string SettingsPath => Path.Combine(DirectoryPath, "bridge-settings.json");
    internal static string WindowPlacementPath => Path.Combine(DirectoryPath, "window-placement.json");
    internal static string LogDirectoryPath => Path.Combine(DirectoryPath, "logs");
    internal static string RuntimeDirectoryPath => Path.Combine(DirectoryPath, "runtime");

    internal static string EnsureRuntimeDirectory()
    {
        Directory.CreateDirectory(RuntimeDirectoryPath);
        return RuntimeDirectoryPath;
    }
}
