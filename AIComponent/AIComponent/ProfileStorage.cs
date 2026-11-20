using System.Runtime.InteropServices;
using System.Text.Json;

namespace AIComponent;

/// <summary>Windows user scoped encrypted profile storage. Host may supply a different file path.</summary>
public static class ProfileStorage
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static byte[] Transform(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var success = encrypt
                ? CryptProtectData(ref input, "AIComponent", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }
    public static IReadOnlyList<AiProfile> Load(string path) =>
        !System.IO.File.Exists(path) ? [] :
        JsonSerializer.Deserialize<List<AiProfile>>(Transform(System.IO.File.ReadAllBytes(path), false)) ?? [];
    public static void Save(string path, IEnumerable<AiProfile> profiles)
    {
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        System.IO.Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            System.IO.File.WriteAllBytes(temporary, Transform(JsonSerializer.SerializeToUtf8Bytes(profiles), true));
            System.IO.File.Move(temporary, path, true);
        }
        finally { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); }
    }
}
