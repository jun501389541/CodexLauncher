using System.Diagnostics;

namespace CodexLauncher.Core;

public static class GatewayPortOwner
{
    public static int? FindCurrentOwner(int entryPort, int controllerPort)
        => FindCommonOwner(ReadNetstat(), entryPort, controllerPort);

    public static int? FindCurrentListenerOwner(int port)
        => FindListener(ReadNetstat(), port);

    private static string ReadNetstat()
    {
        var start = new ProcessStartInfo("netstat.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        start.ArgumentList.Add("-ano");
        start.ArgumentList.Add("-p");
        start.ArgumentList.Add("TCP");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法查询本地监听端口。");
        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(5000) || process.ExitCode != 0)
            throw new InvalidOperationException("查询本地监听端口失败。");
        return output;
    }

    public static int? FindCommonOwner(string netstatOutput, int entryPort, int controllerPort)
    {
        var entryOwner = FindListener(netstatOutput, entryPort);
        var controllerOwner = FindListener(netstatOutput, controllerPort);
        return entryOwner is not null && entryOwner == controllerOwner ? entryOwner : null;
    }

    private static int? FindListener(string output, int port)
    {
        foreach (var line in output.Split('\n'))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5 || !fields[0].Equals("TCP", StringComparison.OrdinalIgnoreCase)
                || !fields[1].Equals($"127.0.0.1:{port}", StringComparison.Ordinal)
                || fields[2] != "0.0.0.0:0" || !fields[3].Equals("LISTENING", StringComparison.OrdinalIgnoreCase))
                continue;
            if (int.TryParse(fields[^1], out var pid)) return pid;
        }
        return null;
    }
}
