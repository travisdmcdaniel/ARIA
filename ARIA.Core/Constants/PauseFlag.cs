namespace ARIA.Core.Constants;

/// <summary>
/// Lightweight cross-process pause signal. The presence of the flag file means
/// the agent is paused: MessageRouter silently drops incoming messages and the
/// tray icon turns amber.
///
/// This file-based mechanism is a temporary implementation. It will be replaced
/// by a named pipe IPC command in M11 when the StatusPipeServer is built.
/// </summary>
public static class PauseFlag
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ARIA", "paused");

    public static bool IsSet => File.Exists(FilePath);

    public static void Set()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, string.Empty);
    }

    public static void Clear()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }
}
