// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
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
        private const string FallbackWarningSessionKey =
            "NeoCompose.Unity.SecretBackendFallbackWarningShown";

        public static INeoComposeTokenSecretBackend CreateDefault()
        {
            var native = CreateNativeForPlatform();
            var fallback = new NeoComposeFileSecretBackend();
            if (native == null)
            {
                WarnFallbackOnce("");
                return fallback;
            }

            if (!native.IsAvailable) WarnFallbackOnce(native.Name);
            return new NeoComposeResilientSecretBackend(
                native,
                fallback,
                () => WarnFallbackOnce(native.Name));
        }

        private static void WarnFallbackOnce(string nativeName)
        {
            if (SessionState.GetBool(FallbackWarningSessionKey, false)) return;
            SessionState.SetBool(FallbackWarningSessionKey, true);
            Debug.LogWarning(
                "Neo Compose could not access an OS-native secret store" +
                (nativeName.Length > 0 ? $" ({nativeName})" : "") +
                ". Falling back to a restricted per-user file outside the project. " +
                "Your Neo Compose sign-in will be stored less securely until the " +
                "native secret store is available.");
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
    /// Uses the native credential store when an operation actually succeeds,
    /// while retaining a restricted file as a resilient fallback. Reads probe
    /// both stores and opportunistically migrate fallback credentials back into
    /// the native store when it becomes usable.
    /// </summary>
    public sealed class NeoComposeResilientSecretBackend : INeoComposeTokenSecretBackend
    {
        private readonly INeoComposeTokenSecretBackend native;
        private readonly INeoComposeTokenSecretBackend fallback;
        private readonly Action onFallbackUsed;

        public NeoComposeResilientSecretBackend(
            INeoComposeTokenSecretBackend native,
            INeoComposeTokenSecretBackend fallback,
            Action? onFallbackUsed = null)
        {
            this.native = native ?? throw new ArgumentNullException(nameof(native));
            this.fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            this.onFallbackUsed = onFallbackUsed ?? (() => { });
        }

        public bool IsAvailable => native.IsAvailable || fallback.IsAvailable;

        public string Name => $"{native.Name} with {fallback.Name} fallback";

        public string? Read(string service, string account)
        {
            if (TryReadNative(service, account, out var nativeValue))
            {
                TryDeleteFallback(service, account);
                return nativeValue;
            }

            var fallbackValue = fallback.Read(service, account);
            if (string.IsNullOrWhiteSpace(fallbackValue)) return null;

            onFallbackUsed();
            if (TryWriteNative(service, account, fallbackValue!))
            {
                TryDeleteFallback(service, account);
            }

            return fallbackValue;
        }

        public void Write(string service, string account, string secret)
        {
            if (TryWriteNative(service, account, secret))
            {
                TryDeleteFallback(service, account);
                return;
            }

            onFallbackUsed();
            fallback.Write(service, account, secret);
        }

        public void Delete(string service, string account)
        {
            Exception? nativeFailure = null;
            try
            {
                // Availability is only a probe of the native service's current
                // state. Always attempt deletion so a temporarily locked or
                // disconnected service cannot leave a credential behind.
                native.Delete(service, account);
            }
            catch (Exception exception)
            {
                nativeFailure = exception;
            }

            fallback.Delete(service, account);
            if (nativeFailure != null)
            {
                Debug.LogWarning(
                    $"[NeoCompose] Could not delete a credential from {native.Name}: " +
                    nativeFailure.Message);
            }
        }

        private bool TryReadNative(string service, string account, out string? value)
        {
            value = null;
            if (!native.IsAvailable) return false;
            try
            {
                value = native.Read(service, account);
                return !string.IsNullOrWhiteSpace(value);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool TryWriteNative(string service, string account, string secret)
        {
            if (!native.IsAvailable) return false;
            try
            {
                native.Write(service, account, secret);
                return TryReadNative(service, account, out var written) &&
                    string.Equals(written, secret, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void TryDeleteFallback(string service, string account)
        {
            try
            {
                fallback.Delete(service, account);
            }
            catch (Exception)
            {
                // A stale fallback copy is preferable to losing the native value.
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
            public int MemberCount;
            public IntPtr Members;
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
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);
        private readonly string rootDirectory;

        public NeoComposeFileSecretBackend(string? rootDirectory = null)
        {
            this.rootDirectory = ValidateRootDirectory(rootDirectory ?? DefaultRootDirectory());
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
            var temporaryPath = Path.Combine(
                rootDirectory,
                "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporaryPath, secret, Utf8WithoutBom);
                RestrictFile(temporaryPath);
                if (File.Exists(path))
                {
                    File.Replace(temporaryPath, path, null);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }

                RestrictFile(path);
                if (!string.Equals(Read(service, account), secret, StringComparison.Ordinal))
                {
                    throw new IOException("Neo Compose could not verify the restricted credential file after writing it.");
                }
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
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

        private static string ValidateRootDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("Credential directory cannot be empty.", nameof(directory));
            }

            if (!Path.IsPathRooted(directory))
            {
                throw new ArgumentException(
                    "Credential directory must be an absolute per-user path.",
                    nameof(directory));
            }

            var fullPath = Path.GetFullPath(directory);
            var pathRoot = Path.GetPathRoot(fullPath);
            if (string.Equals(
                fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                pathRoot?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Credential directory cannot be a filesystem root.", nameof(directory));
            }

            var assetsPath = Path.GetFullPath(Application.dataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var projectPath = Path.GetDirectoryName(assetsPath) ?? assetsPath;
            var candidate = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(candidate, projectPath, StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(projectPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Credential directory must be outside the Unity project.",
                    nameof(directory));
            }

            return candidate;
        }

        private static void RestrictDirectory(string directory)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor) return;
            EnsureChmod("700", directory);
        }

        private static void RestrictFile(string path)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor) return;
            EnsureChmod("600", path);
        }

        private static void EnsureChmod(string mode, string path)
        {
            var result = NeoComposeProcess.Run("/bin/chmod", new[] { mode, path });
            if (result.ExitCode == 0) return;
            throw new IOException(
                $"Neo Compose could not restrict credential permissions for '{path}': " +
                result.StandardError.Trim());
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
