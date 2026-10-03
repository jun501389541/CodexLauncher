namespace CodexLauncher.Core;

public sealed class DiagnosticLogger(string path)
{
    public void Write(string stage, string status, TimeSpan? elapsed)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O}\t{stage}\t{status}\t{(elapsed?.TotalMilliseconds.ToString("0") ?? "-")}ms{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
