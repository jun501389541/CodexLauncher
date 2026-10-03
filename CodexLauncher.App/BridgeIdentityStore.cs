using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text.Json;

namespace CodexLauncher.App;

internal sealed class BridgeIdentity(string bridgeId, byte[] mappingKey, X509Certificate2 certificate, bool wasReset) : IDisposable
{
    public string BridgeId { get; } = bridgeId;
    public byte[] MappingKey { get; } = mappingKey;
    public X509Certificate2 Certificate { get; } = certificate;
    public string Fingerprint => Certificate.GetCertHashString(HashAlgorithmName.SHA256);
    public bool WasReset { get; } = wasReset;
    public void Dispose() { Certificate.Dispose(); CryptographicOperations.ZeroMemory(MappingKey); }
}

// 身份、设备仓库使用同一原子 DPAPI 文件边界，不写入 settings.json。
internal sealed class BridgeIdentityStore(string directory)
{
    private sealed record Payload(int Version, string BridgeId, byte[] MappingKey, byte[] Pfx);
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexLauncher", "bridge");
    public BridgeIdentity LoadOrCreate()
    {
        var path = Path.Combine(directory, "identity.dat");
        SecureBridgeFiles.PrepareDirectory(directory);
        if (File.Exists(path))
        {
            try
            {
                var clear = SecureBridgeFiles.Read(path);
                try
                {
                    var data = JsonSerializer.Deserialize<Payload>(clear) ?? throw new InvalidDataException();
                    if (data.Version != 1 || !Guid.TryParse(data.BridgeId, out _) ||
                        data.MappingKey is not { Length: 32 } || data.Pfx is not { Length: > 0 })
                        throw new InvalidDataException();
                    var certificate = X509CertificateLoader.LoadPkcs12(data.Pfx, null);
                    CryptographicOperations.ZeroMemory(data.Pfx);
                    if (!certificate.HasPrivateKey) { certificate.Dispose(); throw new InvalidDataException(); }
                    return new(data.BridgeId, data.MappingKey, certificate, false);
                }
                finally { CryptographicOperations.ZeroMemory(clear); }
            }
            catch (Exception exception) when (exception is CryptographicException or JsonException or InvalidDataException)
            { /* 明确重置；旧设备仓库属于旧 bridgeId，认证层必须拒绝。 */ }
        }
        var id = Guid.NewGuid().ToString("D");
        using var key = RSA.Create(3072);
        var request = new CertificateRequest("CN=CodexLauncher-" + id, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(100));
        var pfx = generated.Export(X509ContentType.Pfx);
        var mapping = RandomNumberGenerator.GetBytes(32);
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new Payload(1, id, mapping, pfx));
            try { SecureBridgeFiles.Write(path, bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
            return new(id, mapping, X509CertificateLoader.LoadPkcs12(pfx, null), true);
        }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }
}

internal static class SecureBridgeFiles
{
    public static void PrepareDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        var sid = WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException();
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.SetOwner(sid);
        acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(directory).SetAccessControl(acl);
    }
    public static byte[] Read(string path) => Transform(File.ReadAllBytes(path), false);
    public static void Write(string path, byte[] clear)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var cipher = Transform(clear, true);
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(cipher); stream.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var success = protect ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new CryptographicException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
            var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            if (input.Data != IntPtr.Zero) { Marshal.Copy(new byte[input.Size], 0, input.Data, input.Size); Marshal.FreeHGlobal(input.Data); }
            if (output.Data != IntPtr.Zero) { Marshal.Copy(new byte[output.Size], 0, output.Data, output.Size); LocalFree(output.Data); }
        }
    }
}
