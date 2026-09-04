using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Circus.Users.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Circus.Users.Core.Security;

/// <summary>
/// Хранит секреты в диспетчере учётных данных Windows.
/// </summary>
/// <remarks>
/// Запись шифруется под учётной записью текущего пользователя, поэтому токен нельзя
/// прочитать ни с другой машины, ни из-под другого администратора. В файлы приложения
/// и в репозиторий он не попадает вовсе.
/// </remarks>
public sealed class WindowsCredentialStore : ISecretStore
{
    private const int GenericCredential = 1;
    private const int PersistLocalMachine = 2;
    private const int NotFound = 1168;

    private readonly ILogger<WindowsCredentialStore> _logger;

    public WindowsCredentialStore(ILogger<WindowsCredentialStore> logger) => _logger = logger;

    public string? Read(string key)
    {
        if (!CredRead(key, GenericCredential, 0, out var handle))
        {
            var error = Marshal.GetLastWin32Error();

            if (error != NotFound)
            {
                _logger.LogWarning("Не удалось прочитать секрет {Key}: код {Error}", key, error);
            }

            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(handle);

            return credential.CredentialBlobSize == 0
                ? null
                : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(handle);
        }
    }

    public void Write(string key, string secret)
    {
        var blob = Marshal.StringToCoTaskMemUni(secret);

        try
        {
            var credential = new Credential
            {
                Type = GenericCredential,
                TargetName = key,
                CredentialBlob = blob,
                CredentialBlobSize = (uint)(secret.Length * 2),
                Persist = PersistLocalMachine,
                UserName = Environment.UserName,
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Не удалось сохранить секрет «{key}».");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public void Delete(string key)
    {
        if (!CredDelete(key, GenericCredential, 0) && Marshal.GetLastWin32Error() != NotFound)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Не удалось удалить секрет «{key}».");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int flags, out nint credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(nint buffer);
}
