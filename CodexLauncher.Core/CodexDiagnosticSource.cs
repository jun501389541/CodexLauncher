using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CodexLauncher.Core;

/// <summary>增量读取位置。只记录读到哪一条，不携带任何日志内容。</summary>
public sealed record DiagnosticCursor(long LastId = 0, bool Initialized = false)
{
    public static readonly DiagnosticCursor Empty = new();

    public bool IsEmpty => !Initialized;
}

/// <summary>可验证的 Codex 桌面实例身份，用于把诊断事件限定到当前实例。</summary>
public sealed record CodexInstance
{
    public int ProcessId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public string ExecutablePath { get; init; } = "";
    public string PackageFamilyName { get; init; } = "";

    /// <summary>属于该实例的后端进程编号；为空表示实例归属无法确认，诊断源必须停用。</summary>
    public IReadOnlyList<int> BackendProcessIds { get; init; } = Array.Empty<int>();

    public bool HasVerifiedIdentity => ProcessId > 0 && StartedAt != default && ExecutablePath.Length > 0;

    /// <summary>判断路径是否位于给定目录内；匹配包含目录边界，避免前缀误判。</summary>
    public static bool IsPathWithin(string? path, string? directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory)) return false;
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            var candidate = Path.GetFullPath(path);
            if (candidate.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
            return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}

/// <summary>一次增量读取的结果：规范化事件、新的游标和来源可用性。</summary>
public sealed record DiagnosticReadResult(
    bool SourceAvailable,
    IReadOnlyList<NetworkDiagnosticEvent> Events,
    DiagnosticCursor Cursor,
    string? UnavailableReason = null)
{
    public static DiagnosticReadResult Unavailable(DiagnosticCursor cursor, string reason) =>
        new(false, Array.Empty<NetworkDiagnosticEvent>(), cursor, reason);
}

/// <summary>只读诊断事件来源；不返回日志原文，只返回规范化类别。</summary>
public interface ICodexDiagnosticSource
{
    Task<DiagnosticReadResult> ReadSinceAsync(DiagnosticCursor cursor, CodexInstance instance, CancellationToken cancellationToken);
}

/// <summary>
/// 本机 Codex 诊断库的只读增量适配器。
/// 只读取白名单来源的结构化字段，从监测开始后的记录读取、不回扫历史，
/// 只保留规范化状态与错误类别；库缺失、锁定、字段变化或格式不支持时退回线路检测。
/// </summary>
public sealed class CodexDiagnosticSource(string? databasePath = null, TimeSpan? readTimeout = null) : ICodexDiagnosticSource
{
    /// <summary>适配器验证过的诊断库结构版本；更高版本视为不受支持。</summary>
    public const long SupportedSchemaVersion = 2;

    private const int MaxEvents = 200;
    private const int MaxRowsPerRead = 500;

    /// <summary>锁等待上限（秒）。Microsoft.Data.Sqlite 每次执行命令都会用连接级默认超时
    /// 覆盖 busy_timeout，因此必须显式压低，否则被锁定的库会一直等到 30 秒默认值。</summary>
    private const int LockTimeoutSeconds = 1;
    private const string HttpClientTarget = "codex_http_client::client";
    private const string RemoteControlTarget = "codex_app_server_transport::transport::remote_control::websocket";

    private static readonly string[] RequiredColumns =
        ["id", "ts", "ts_nanos", "target", "feedback_log_body", "process_uuid"];

    private readonly string? _configuredPath = databasePath;
    private readonly TimeSpan _readTimeout = readTimeout ?? TimeSpan.FromSeconds(2);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ReadState _state = new();

    /// <summary>解析本机诊断库路径；找不到返回 null。</summary>
    public static string? ResolveDatabasePath()
    {
        foreach (var root in CandidateRoots())
        {
            if (!Directory.Exists(root)) continue;
            string? best = null;
            var bestVersion = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "logs_*.sqlite"))
                {
                    var version = VersionOf(Path.GetFileNameWithoutExtension(file));
                    if (version <= bestVersion) continue;
                    bestVersion = version;
                    best = file;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (best is not null) return best;
        }
        return null;
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var home = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(home)) yield return home;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile)) yield return Path.Combine(profile, ".codex");
    }

    private static int VersionOf(string name)
    {
        var separator = name.LastIndexOf('_');
        if (separator < 0 || separator == name.Length - 1) return 0;
        return int.TryParse(name.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : 0;
    }

    public async Task<DiagnosticReadResult> ReadSinceAsync(DiagnosticCursor cursor, CodexInstance instance, CancellationToken cancellationToken)
    {
        if (instance is null || !instance.HasVerifiedIdentity || instance.BackendProcessIds.Count == 0)
            return DiagnosticReadResult.Unavailable(cursor, "未能确认 Codex 桌面实例，仅依据线路检测");

        var path = _configuredPath ?? ResolveDatabasePath();
        if (path is null || !File.Exists(path))
            return DiagnosticReadResult.Unavailable(cursor, "未找到 Codex 诊断库，仅依据线路检测");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // SQLite 读取是同步 API：放到后台线程，并施加硬性截止时间。
            // 诊断库被独占锁定时读取可能长时间阻塞，此处必须降级返回而不是冻结调用方。
            var working = _state.Clone();
            var read = Task.Run(() => ReadCore(path, cursor, instance, working), cancellationToken);
            try
            {
                var result = await read.WaitAsync(_readTimeout, cancellationToken);
                _state.Commit(working);
                return result;
            }
            catch (TimeoutException)
            {
                // 放弃这次读取：不提交状态，下一次读取重新对齐游标，避免迟到结果污染事件序列。
                return DiagnosticReadResult.Unavailable(cursor, "诊断库响应超时，仅依据线路检测");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private DiagnosticReadResult ReadCore(string path, DiagnosticCursor cursor, CodexInstance instance, ReadState working)
    {
        SqliteConnection connection;
        try
        {
            connection = OpenReadOnly(path);
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            return DiagnosticReadResult.Unavailable(cursor, "诊断库当前不可读，仅依据线路检测");
        }

        using (connection)
        {
            try
            {
                if (!TryReadSchema(connection, out var reason))
                    return DiagnosticReadResult.Unavailable(cursor, reason!);

                var maxId = Scalar(connection, "SELECT COALESCE(MAX(id), 0) FROM logs;");
                var key = $"{instance.ProcessId}@{instance.StartedAt.UtcTicks}";
                var instanceChanged = !string.Equals(working.InstanceKey, key, StringComparison.Ordinal);
                working.InstanceKey = key;

                // 实例变化、首次读取或诊断库被重建时不回放历史，只把游标对到当前位置。
                if (instanceChanged || !cursor.Initialized || cursor.LastId > maxId)
                {
                    working.Events.Clear();
                    return new DiagnosticReadResult(true, Array.Empty<NetworkDiagnosticEvent>(), new DiagnosticCursor(maxId, true));
                }

                var lastScanned = cursor.LastId;
                foreach (var row in ReadRows(connection, cursor.LastId, instance.BackendProcessIds))
                {
                    lastScanned = row.Id;
                    if (TryNormalize(row, out var normalized)) working.Events.Add(normalized);
                }
                Trim(working.Events);

                return new DiagnosticReadResult(true, working.Events.ToArray(), new DiagnosticCursor(lastScanned, true));
            }
            catch (SqliteException)
            {
                return DiagnosticReadResult.Unavailable(cursor, "诊断库当前不可读，仅依据线路检测");
            }
        }
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = LockTimeoutSeconds
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    /// <summary>结构或版本与已验证的样本不一致时停用适配器，退回网络探测。</summary>
    private static bool TryReadSchema(SqliteConnection connection, out string? reason)
    {
        reason = null;
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM pragma_table_info('logs');";
            using var reader = command.ExecuteReader();
            while (reader.Read()) columns.Add(reader.GetString(0));
        }
        catch (SqliteException exception) when (!IsLocked(exception))
        {
            reason = "诊断库结构无法识别，仅依据线路检测";
            return false;
        }

        if (columns.Count == 0 || RequiredColumns.Any(column => !columns.Contains(column)))
        {
            reason = "诊断库字段已变化，仅依据线路检测";
            return false;
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM _sqlx_migrations WHERE success = 1;";
            var version = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (version > SupportedSchemaVersion)
            {
                reason = "诊断库版本不受支持，仅依据线路检测";
                return false;
            }
        }
        catch (SqliteException exception) when (!IsLocked(exception))
        {
            reason = "诊断库版本无法识别，仅依据线路检测";
            return false;
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException)
        {
            reason = "诊断库版本无法识别，仅依据线路检测";
            return false;
        }

        return true;
    }

    /// <summary>库被其他进程锁定（SQLITE_BUSY / SQLITE_LOCKED）：这是暂时不可读，不是结构变化。</summary>
    private static bool IsLocked(SqliteException exception) =>
        exception.SqliteErrorCode is 5 or 6 or 261 or 262;

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static IEnumerable<LogRow> ReadRows(SqliteConnection connection, long afterId, IReadOnlyList<int> processIds)
    {
        using var command = connection.CreateCommand();
        var predicates = new List<string>(processIds.Count);
        for (var index = 0; index < processIds.Count; index++)
        {
            predicates.Add($"process_uuid LIKE $pid{index}");
            command.Parameters.AddWithValue($"$pid{index}", $"pid:{processIds[index]}:%");
        }

        command.CommandText =
            "SELECT id, ts, ts_nanos, target, feedback_log_body FROM logs " +
            $"WHERE id > $after AND target IN ('{HttpClientTarget}', '{RemoteControlTarget}') " +
            $"AND ({string.Join(" OR ", predicates)}) ORDER BY id LIMIT {MaxRowsPerRead};";
        command.Parameters.AddWithValue("$after", afterId);

        var rows = new List<LogRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new LogRow(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }
        return rows;
    }

    /// <summary>把一条记录归一化为网络事件；结构不符或与网络无关的记录一律丢弃。</summary>
    private static bool TryNormalize(LogRow row, out NetworkDiagnosticEvent normalized)
    {
        normalized = null!;
        var body = row.Body;
        if (string.IsNullOrEmpty(body)) return false;

        var timestamp = DateTimeOffset.FromUnixTimeSeconds(row.Timestamp)
            .AddTicks(row.TimestampNanos / 100).ToLocalTime();

        if (string.Equals(row.Target, HttpClientTarget, StringComparison.Ordinal))
        {
            // 只认带结构锚点的请求结果，避免把用户文字或工具输出当成网络证据。
            if (body.Contains("Request completed method=", StringComparison.Ordinal))
            {
                var status = Field(body, "status=");
                if (status is null || status.Length != 3 || !int.TryParse(status, NumberStyles.None, CultureInfo.InvariantCulture, out var code))
                    return false;
                // 4xx（含 403）不是网络健康证据；2xx/3xx 为成功，5xx 为服务端失败。
                if (code is >= 200 and < 400)
                {
                    normalized = new NetworkDiagnosticEvent(NetworkEventKind.NetworkSuccess, timestamp, "http-2xx");
                    return true;
                }
                if (code >= 500)
                {
                    normalized = new NetworkDiagnosticEvent(NetworkEventKind.NetworkFailure, timestamp, "http-5xx");
                    return true;
                }
                return false;
            }

            if (body.Contains("Request failed method=", StringComparison.Ordinal))
            {
                var timeout = string.Equals(Field(body, "is_timeout="), "true", StringComparison.Ordinal);
                var connect = string.Equals(Field(body, "is_connect="), "true", StringComparison.Ordinal);
                var category = timeout ? "http-timeout" : connect ? "http-connect" : "http-error";
                normalized = new NetworkDiagnosticEvent(NetworkEventKind.NetworkFailure, timestamp, category);
                return true;
            }

            return false;
        }

        if (!string.Equals(row.Target, RemoteControlTarget, StringComparison.Ordinal)) return false;

        if (body.Contains("remote control websocket status changed", StringComparison.Ordinal))
        {
            switch (Field(body, "next_status="))
            {
                case "Connected":
                    normalized = new NetworkDiagnosticEvent(NetworkEventKind.Recovery, timestamp, "websocket-connected");
                    return true;
                case "Connecting":
                    normalized = new NetworkDiagnosticEvent(NetworkEventKind.Reconnect, timestamp, "websocket-connecting");
                    return true;
                case "Errored":
                    normalized = new NetworkDiagnosticEvent(NetworkEventKind.Reconnect, timestamp, "websocket-errored");
                    return true;
                default:
                    return false;
            }
        }

        if (body.Contains("failed to connect to app-server remote control websocket", StringComparison.Ordinal))
        {
            normalized = new NetworkDiagnosticEvent(NetworkEventKind.Reconnect, timestamp, "websocket-connect-failed");
            return true;
        }

        if (body.Contains("remote control websocket reader disconnected", StringComparison.Ordinal))
        {
            normalized = new NetworkDiagnosticEvent(NetworkEventKind.Reconnect, timestamp, "websocket-disconnected");
            return true;
        }

        if (body.Contains("reset app-server remote control websocket reconnect backoff", StringComparison.Ordinal))
        {
            normalized = new NetworkDiagnosticEvent(NetworkEventKind.Recovery, timestamp, "websocket-backoff-reset");
            return true;
        }

        return false;
    }

    /// <summary>读取 "key=value" 形式的字段；要求键前有空白，避免 previous_status 误配 status。</summary>
    private static string? Field(string body, string key)
    {
        var index = body.IndexOf(" " + key, StringComparison.Ordinal);
        if (index < 0) return null;
        var start = index + key.Length + 1;
        var end = start;
        while (end < body.Length && !char.IsWhiteSpace(body[end])) end++;
        return end > start ? body[start..end] : null;
    }

    /// <summary>限制事件数量，但不裁剪尚未恢复的重连起点，否则会被误判成已恢复。</summary>
    private static void Trim(List<NetworkDiagnosticEvent> events)
    {
        if (events.Count <= MaxEvents) return;
        var keepFrom = events.Count - MaxEvents;
        for (var index = events.Count - 1; index >= 0; index--)
        {
            if (events[index].Kind == NetworkEventKind.Recovery) break;
            if (events[index].Kind == NetworkEventKind.Reconnect)
            {
                keepFrom = Math.Min(keepFrom, index);
                break;
            }
        }
        if (keepFrom > 0) events.RemoveRange(0, keepFrom);
    }

    private readonly record struct LogRow(long Id, long Timestamp, long TimestampNanos, string Target, string? Body);

    /// <summary>读取期间的可变状态；只有完整成功的一次读取才会提交，超时或失败不污染已有序列。</summary>
    private sealed class ReadState
    {
        public string? InstanceKey { get; set; }

        public List<NetworkDiagnosticEvent> Events { get; } = [];

        public ReadState Clone()
        {
            var clone = new ReadState { InstanceKey = InstanceKey };
            clone.Events.AddRange(Events);
            return clone;
        }

        public void Commit(ReadState working)
        {
            InstanceKey = working.InstanceKey;
            Events.Clear();
            Events.AddRange(working.Events);
        }
    }
}
