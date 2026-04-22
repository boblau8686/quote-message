namespace MsgDots;

/// <summary>
/// Simple file logger — writes to %TEMP%\msgdots.log.
/// Mirrors macOS EQLog behaviour so log analysis is consistent.
/// </summary>
static class QMLog
{
    private static readonly string _path =
        Path.Combine(Path.GetTempPath(), "msgdots.log");

    public static void Info(string message)
    {
        var line = $"[{DateTime.Now:o}] {message}";
        Console.WriteLine(line);
        try { File.AppendAllText(_path, line + Environment.NewLine); }
        catch { /* never crash on log failure */ }
    }
}
