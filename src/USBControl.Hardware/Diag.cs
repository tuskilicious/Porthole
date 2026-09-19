using System.IO;

namespace USBControl.Hardware;

/// <summary>
/// Opt-in diagnostics: create an empty %APPDATA%\USBControl\debug.flag to get a verbose
/// enumeration trace in the same directory (useful when running elevated, where shell
/// environment variables don't reach the process). No cost when disabled.
/// </summary>
internal static class Diag
{
    private static readonly bool Enabled = File.Exists(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "USBControl", "debug.flag"));

    private static readonly object Lock = new();

    public static void Log(string message)
    {
        if (!Enabled)
            return;
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "USBControl");
            Directory.CreateDirectory(dir);
            lock (Lock)
                File.AppendAllText(Path.Combine(dir, "diagnostics.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} {message}\r\n");
        }
        catch
        {
            // diagnostics must never break the app
        }
    }
}
