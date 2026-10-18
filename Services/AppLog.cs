using System.Text;

namespace StopwatchWidget.Services;

/// <summary>
/// Minimal text log used to surface managed failures that would otherwise vanish
/// silently in a packaged WinUI application (no console, no dialog).
/// Written next to the settings file, in the app's local folder.
/// </summary>
internal static class AppLog
{
    private const string FileName = "log.txt";

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            // Resolved on every write: very early in startup the packaged APIs may not
            // be ready yet, and we don't want to lock in a fallback location forever.
            var path = System.IO.Path.Combine(ResolveFolderPath(), FileName);

            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var builder = new StringBuilder();
            builder.Append(DateTimeOffset.Now.ToString("O"))
                   .Append(' ')
                   .AppendLine(message);

            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }

            File.AppendAllText(path, builder.ToString());
        }
        catch
        {
            // Logging must never be the thing that brings the app down.
        }
    }

    private static string ResolveFolderPath()
    {
        try
        {
            // Same folder the settings file uses: always writable for a packaged app.
            return Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch
        {
            // Unpackaged runs have no package identity, so ApplicationData.Current
            // throws. Fall back to a plain folder under %LOCALAPPDATA%.
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StopwatchWidget");
        }
    }
}
