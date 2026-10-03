using System.Diagnostics;
using System.Text.Json;

namespace CodexLauncher.Core;

public sealed class GatewayProcessHost(string dataDirectory) : IGatewayProcessHost
{
    private sealed record ProcessRecord(int Id, string BinaryPath, long StartedAtUtcTicks);
    private string RecordPath => Path.Combine(dataDirectory, "gateway-process.json");
    private string ConfigPath => Path.Combine(dataDirectory, "config.yaml");

    public bool IsConfigCurrent(GatewayPorts ports) =>
        File.Exists(ConfigPath) && File.ReadAllText(ConfigPath) == GatewayConfig.Build(ports);

    public void SaveOwnedConfig(GatewayPorts ports)
    {
        if (!IsOwnedRunning()) throw new InvalidOperationException("固定入口并非启动器管理的进程，无法保存配置。");
        var temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, GatewayConfig.Build(ports));
        File.Move(temp, ConfigPath, true);
    }

    public bool IsOwnedRunning()
    {
        var record = ReadRecord();
        if (record is null) return false;
        try
        {
            using var process = Process.GetProcessById(record.Id);
            return !process.HasExited && MatchesRecord(process, record);
        }
        catch (ArgumentException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    public bool ControlsPorts(GatewayPorts ports)
    {
        var record = ReadRecord();
        return record is not null && IsOwnedRunning()
            && GatewayPortOwner.FindCurrentOwner(ports.Entry, ports.Controller) == record.Id;
    }

    public void Start(string binaryPath, GatewayPorts ports)
    {
        var absoluteBinary = Path.GetFullPath(binaryPath);
        if (!File.Exists(absoluteBinary) || !Path.GetFileName(absoluteBinary).Equals("mihomo.exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("请选择有效的 mihomo.exe 程序。", absoluteBinary);
        if (IsOwnedRunning()) throw new InvalidOperationException("启动器管理的固定入口已在运行。");
        Directory.CreateDirectory(dataDirectory);
        var configPath = ConfigPath;
        File.WriteAllText(configPath, GatewayConfig.Build(ports));
        var start = new ProcessStartInfo(absoluteBinary)
        {
            WorkingDirectory = dataDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("-d");
        start.ArgumentList.Add(dataDirectory);
        start.ArgumentList.Add("-f");
        start.ArgumentList.Add(configPath);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Mihomo 未能启动。");
        try
        {
            var temp = RecordPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new ProcessRecord(process.Id, absoluteBinary, process.StartTime.ToUniversalTime().Ticks)));
            File.Move(temp, RecordPath, true);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }

    public void StopOwned()
    {
        var record = ReadRecord();
        if (record is null) return;
        try
        {
            using var process = Process.GetProcessById(record.Id);
            if (!MatchesRecord(process, record))
                throw new InvalidOperationException("固定入口的进程 ID 已被其他程序占用，停止操作已取消。");
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5000)) throw new TimeoutException("固定入口进程尚未退出。");
            }
        }
        catch (ArgumentException) { /* The recorded process has already exited. */ }
        File.Delete(RecordPath);
    }

    public bool TryStopCompatibleExternal(string binaryPath, GatewayPorts ports)
    {
        var ownerPid = GatewayPortOwner.FindCurrentOwner(ports.Entry, ports.Controller);
        if (ownerPid is null) return false;
        try
        {
            using var process = Process.GetProcessById(ownerPid.Value);
            if (!Path.GetFileName(binaryPath).Equals("mihomo.exe", StringComparison.OrdinalIgnoreCase)
                || !IsExpectedBinary(process, binaryPath)) return false;
            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(5000)) throw new TimeoutException("固定入口进程尚未退出。");
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private ProcessRecord? ReadRecord()
    {
        if (!File.Exists(RecordPath)) return null;
        return JsonSerializer.Deserialize<ProcessRecord>(File.ReadAllText(RecordPath))
            ?? throw new InvalidDataException("固定入口进程记录损坏。");
    }

    private static bool IsExpectedBinary(Process process, string expectedPath) =>
        Path.GetFullPath(process.MainModule?.FileName ?? "")
            .Equals(Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesRecord(Process process, ProcessRecord record) =>
        IsExpectedBinary(process, record.BinaryPath)
        && process.StartTime.ToUniversalTime().Ticks == record.StartedAtUtcTicks;
}
