using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace TrendGovernor.Wpf;

internal static class CredentialRules
{
    private static readonly string[] PlaceholderTokens =
    [
        "PASTE_KEY_HERE", "PASTE_SECRET_HERE", "API_KEY_HERE", "API_SECRET_HERE",
        "YOUR_API_KEY", "YOUR_API_SECRET", "CHANGE_ME", "PLACEHOLDER"
    ];

    public static bool IsUsable(string? apiKey, string? apiSecret)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret)) return false;
        apiKey = apiKey.Trim();
        apiSecret = apiSecret.Trim();
        if (apiKey.Length < 20 || apiSecret.Length < 20) return false;
        return !PlaceholderTokens.Any(token =>
            apiKey.Contains(token, StringComparison.OrdinalIgnoreCase) ||
            apiSecret.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}

internal static class LocalCredentialVault
{
    private const int CryptProtectUiForbidden = 0x1;
    private static readonly string VaultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TrendGovernor");
    private static readonly string VaultPath = Path.Combine(VaultDirectory, "binance-credentials.bin");

    private sealed record CredentialPayload(string ApiKey, string ApiSecret, DateTime SavedUtc);

    public static string DisplayPath => VaultPath;

    public static void Save(string apiKey, string apiSecret)
    {
        apiKey = apiKey.Trim();
        apiSecret = apiSecret.Trim();
        if (!CredentialRules.IsUsable(apiKey, apiSecret))
            throw new InvalidOperationException("API Key/Secret không hợp lệ hoặc vẫn là dữ liệu mẫu.");

        Directory.CreateDirectory(VaultDirectory);
        var json = JsonSerializer.Serialize(new CredentialPayload(apiKey, apiSecret, DateTime.UtcNow));
        var encrypted = Protect(Encoding.UTF8.GetBytes(json));
        File.WriteAllBytes(VaultPath, encrypted);
    }

    public static bool TryLoad(out string apiKey, out string apiSecret)
    {
        apiKey = "";
        apiSecret = "";
        try
        {
            if (!File.Exists(VaultPath)) return false;
            var payload = JsonSerializer.Deserialize<CredentialPayload>(
                Encoding.UTF8.GetString(Unprotect(File.ReadAllBytes(VaultPath))));
            if (payload is null || !CredentialRules.IsUsable(payload.ApiKey, payload.ApiSecret)) return false;
            apiKey = payload.ApiKey;
            apiSecret = payload.ApiSecret;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Protect(byte[] data)
    {
        var input = ToBlob(data);
        try
        {
            if (!CryptProtectData(ref input, "TrendGovernor Binance credentials", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output))
                throw new InvalidOperationException($"Không mã hóa được API. Win32={Marshal.GetLastWin32Error()}.");
            try { return FromBlob(output); }
            finally { if (output.pbData != IntPtr.Zero) LocalFree(output.pbData); }
        }
        finally { if (input.pbData != IntPtr.Zero) Marshal.FreeHGlobal(input.pbData); }
    }

    private static byte[] Unprotect(byte[] data)
    {
        var input = ToBlob(data);
        try
        {
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output))
                throw new InvalidOperationException($"Không giải mã được API. Win32={Marshal.GetLastWin32Error()}.");
            try { return FromBlob(output); }
            finally { if (output.pbData != IntPtr.Zero) LocalFree(output.pbData); }
        }
        finally { if (input.pbData != IntPtr.Zero) Marshal.FreeHGlobal(input.pbData); }
    }

    private static DATA_BLOB ToBlob(byte[] data)
    {
        var ptr = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, ptr, data.Length);
        return new DATA_BLOB { cbData = data.Length, pbData = ptr };
    }

    private static byte[] FromBlob(DATA_BLOB blob)
    {
        var result = new byte[blob.cbData];
        Marshal.Copy(blob.pbData, result, 0, blob.cbData);
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
