// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    /// <summary>
    /// Stores and retrieves a single secret string keyed by service + account.
    /// Implementations must keep secrets out of the Unity project tree and out
    /// of plaintext <c>EditorPrefs</c>.
    /// </summary>
    public interface INeoComposeTokenSecretBackend
    {
        /// <summary>
        /// True when this backend can be used on the current platform.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// A short human-readable name for diagnostics and warnings.
        /// </summary>
        string Name { get; }

        string? Read(string service, string account);
        void Write(string service, string account, string secret);
        void Delete(string service, string account);
    }

    /// <summary>
    /// Selects the most secure available secret backend for the current
    /// platform: macOS Keychain, Windows Credential Manager, or Linux Secret
    /// Service. Falls back to a restricted per-user file outside the project
    /// tree only when no native store is available.
    /// </summary>
    public static class NeoComposeTokenSecretBackends
    {
        public static INeoComposeTokenSecretBackend CreateDefault()
        {
            var native = CreateNativeForPlatform();
            if (native != null && native.IsAvailable) return native;

            Debug.LogWarning(
                "Neo Compose could not access an OS-native secret store" +
                (native != null ? $" ({native.Name})" : "") +
                ". Falling back to a restricted per-user file outside the project. " +
                "Your Neo Compose sign-in will be stored less securely until the " +
                "native secret store is available.");
            return new NeoComposeFileSecretBackend();
        }

        private static INeoComposeTokenSecretBackend? CreateNativeForPlatform()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor:
                    return new NeoComposeMacKeychainSecretBackend();
                case RuntimePlatform.WindowsEditor:
                    return new NeoComposeWindowsCredentialSecretBackend();
                case RuntimePlatform.LinuxEditor:
                    return new NeoComposeLinuxSecretToolSecretBackend();
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// macOS Keychain backend using the built-in <c>security</c> tool. The
    /// secret is stored as a generic password keyed by service + account.
    /// </summary>
    public sealed class NeoComposeMacKeychainSecretBackend : INeoComposeTokenSecretBackend
    {
        public string Name => "macOS Keychain";

        public bool IsAvailable =>
            Application.platform == RuntimePlatform.OSXEditor &&
            File.Exists("/usr/bin/security");

        public string? Read(string service, string account)
        {
            var result = NeoComposeProcess.Run(
                "/usr/bin/security",
                new[] { "find-generic-password", "-s", service, "-a", account, "-w" });
            if (result.ExitCode != 0) return null;
            var value = result.StandardOutput.TrimEnd('\n', '\r');
            return value.Length == 0 ? null : value;
        }

        public void Write(string service, string account, string secret)
        {
            // -U updates the entry in place when it already exists.
            var result = NeoComposeProcess.Run(
                "/usr/bin/security",
                new[]
                {
                    "add-generic-password",
                    "-s", service,
                    "-a", account,
                    "-w", secret,
                    "-U",
                });
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to write Neo Compose token to the macOS Keychain: {result.StandardError.Trim()}");
            }
        }

        public void Delete(string service, string account)
        {
            // A missing entry is not an error for delete.
            NeoComposeProcess.Run(
                "/usr/bin/security",
                new[] { "delete-generic-password", "-s", service, "-a", account });
        }
    }

    /// <summary>
    /// Linux Secret Service backend using the <c>secret-tool</c> CLI. The secret
    /// is supplied through stdin so it never appears in the process argument
    /// list.
    /// </summary>
    public sealed class NeoComposeLinuxSecretToolSecretBackend : INeoComposeTokenSecretBackend
    {
        private const string Tool = "secret-tool";

        public string Name => "Linux Secret Service (secret-tool)";

        public bool IsAvailable =>
            Application.platform == RuntimePlatform.LinuxEditor &&
            NeoComposeProcess.CommandExists(Tool);

        public string? Read(string service, string account)
        {
            var result = NeoComposeProcess.Run(
                Tool,
                new[] { "lookup", "service", service, "account", account });
            if (result.ExitCode != 0) return null;
            var value = result.StandardOutput.TrimEnd('\n', '\r');
            return value.Length == 0 ? null : value;
        }

        public void Write(string service, string account, string secret)
        {
            var result = NeoComposeProcess.Run(
                Tool,
                new[]
                {
                    "store",
                    "--label", "Neo Compose Unity",
                    "service", service,
                    "account", account,
                },
                stdin: secret);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to write Neo Compose token to the Linux Secret Service: {result.StandardError.Trim()}");
            }
        }

        public void Delete(string service, string account)
        {
            NeoComposeProcess.Run(
                Tool,
                new[] { "clear", "service", service, "account", account });
        }
    }

    /// <summary>
    /// Windows Credential Manager backend using the Win32 credential APIs.
    /// </summary>
    public sealed class NeoComposeWindowsCredentialSecretBackend : INeoComposeTokenSecretBackend
    {
        public string Name => "Windows Credential Manager";

        public bool IsAvailable => Application.platform == RuntimePlatform.WindowsEditor;

        public string? Read(string service, string account)
        {
            if (!CredRead(TargetName(service, account), CRED_TYPE_GENERIC, 0, out var handle))
            {
                return null;
            }

            try
            {
                var credential = Marshal.PtrToStructure<CREDENTIAL>(handle);
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                {
                    return null;
                }

                var bytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                CredFree(handle);
            }
        }

        public void Write(string service, string account, string secret)
        {
            var blob = Encoding.UTF8.GetBytes(secret);
            var blobHandle = Marshal.AllocHGlobal(blob.Length);
            try
            {
                Marshal.Copy(blob, 0, blobHandle, blob.Length);
                var credential = new CREDENTIAL
                {
                    Type = CRED_TYPE_GENERIC,
                    TargetName = TargetName(service, account),
                    CredentialBlob = blobHandle,
                    CredentialBlobSize = blob.Length,
                    Persist = CRED_PERSIST_LOCAL_MACHINE,
                    UserName = account,
                };
                if (!CredWrite(ref credential, 0))
                {
                    throw new InvalidOperationException(
                        $"Failed to write Neo Compose token to the Windows Credential Manager (error {Marshal.GetLastWin32Error()}).");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(blobHandle);
            }
        }

        public void Delete(string service, string account)
        {
            CredDelete(TargetName(service, account), CRED_TYPE_GENERIC, 0);
        }

        private static string TargetName(string service, string account) => $"{service}:{account}";

        private const int CRED_TYPE_GENERIC = 1;
        private const int CRED_PERSIST_LOCAL_MACHINE = 2;

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredReadW")]
        private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredWriteW")]
        private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredDeleteW")]
        private static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string? Comment;
            public long LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string UserName;
        }
    }

    /// <summary>
    /// Last-resort backend that stores the secret in a restricted per-user file
    /// outside the Unity project tree. Used only when no OS-native store is
    /// available. Never writes inside <c>Assets/</c> or to <c>EditorPrefs</c>.
    /// </summary>
    public sealed class NeoComposeFileSecretBackend : INeoComposeTokenSecretBackend
    {
        private readonly string rootDirectory;

        public NeoComposeFileSecretBackend(string? rootDirectory = null)
        {
            this.rootDirectory = rootDirectory ?? DefaultRootDirectory();
        }

        public string Name => "Restricted user file";

        public bool IsAvailable => true;

        public string Root => rootDirectory;

        public string? Read(string service, string account)
        {
            var path = PathFor(service, account);
            if (!File.Exists(path)) return null;
            var value = File.ReadAllText(path, Encoding.UTF8);
            return value.Length == 0 ? null : value;
        }

        public void Write(string service, string account, string secret)
        {
            Directory.CreateDirectory(rootDirectory);
            RestrictDirectory(rootDirectory);
            var path = PathFor(service, account);
            File.WriteAllText(path, secret, new UTF8Encoding(false));
            RestrictFile(path);
        }

        public void Delete(string service, string account)
        {
            var path = PathFor(service, account);
            if (File.Exists(path)) File.Delete(path);
        }

        private string PathFor(string service, string account)
        {
            var fileName = NeoComposeSecretKey.Sanitize($"{service}__{account}") + ".token";
            return Path.Combine(rootDirectory, fileName);
        }

        private static string DefaultRootDirectory()
        {
            string baseDir;
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            else
            {
                var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                baseDir = string.IsNullOrWhiteSpace(xdg)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
                    : xdg;
            }

            return Path.Combine(baseDir, "neocompose", "unity-tokens");
        }

        private static void RestrictDirectory(string directory)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor) return;
            NeoComposeProcess.Run("/bin/chmod", new[] { "700", directory });
        }

        private static void RestrictFile(string path)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor) return;
            NeoComposeProcess.Run("/bin/chmod", new[] { "600", path });
        }
    }

    /// <summary>
    /// Builds a stable, filesystem- and keychain-safe key from an auth base URL.
    /// </summary>
    public static class NeoComposeSecretKey
    {
        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "default";
            var builder = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                builder.Append(
                    char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '_'
                        ? c
                        : '_');
            }

            return builder.ToString();
        }
    }
}
