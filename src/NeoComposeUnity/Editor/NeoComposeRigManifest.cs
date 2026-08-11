// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    /// <summary>
    /// One repository checkout a rig is bound to (the coordinator worktree and,
    /// when the rig owns one, the SDK worktree).
    /// </summary>
    public sealed class NeoComposeRigRepositoryCheckout
    {
        public string path = "";
        public string baseCommit = "";
        public string worktreeCommit = "";
    }

    public sealed class NeoComposeRigRepositories
    {
        public NeoComposeRigRepositoryCheckout neoCompose = new();

        /// <summary>Null for app-only rigs that never selected an SDK worktree.</summary>
        public NeoComposeRigRepositoryCheckout? neoComposeDotnet;
    }

    /// <summary>The rig's disposable Convex deployment.</summary>
    public sealed class NeoComposeRigDeployment
    {
        public string project = "";
        public string reference = "";
        public string name = "";
        public string url = "";
        public string siteUrl = "";
    }

    /// <summary>The rig's local web app and PartyServer origins.</summary>
    public sealed class NeoComposeRigWeb
    {
        public string origin = "";
        public int port;
        public string partyServerOrigin = "";
        public int partyServerPort;
    }

    /// <summary>The seed the rig's deployment was materialized from.</summary>
    public sealed class NeoComposeRigSeed
    {
        public string id = "";
        public string sha256 = "";
        public int schemaEpoch;
        public string[] migrationReceipts = Array.Empty<string>();
        public string neoComposeCommit = "";
        public string neoComposeDotnetCommit = "";
    }

    /// <summary>
    /// The seeded HelloWorld project inside the rig's deployment. Null for an
    /// unseeded rig, in which case only endpoints are overlaid and the committed
    /// project/version/channel identifiers stay in force.
    /// </summary>
    public sealed class NeoComposeRigSample
    {
        public string organizationName = "";
        public string projectId = "";
        public string projectName = "";
        public string versionId = "";
        public string releaseChannelId = "";
        public string runtimeOAuthClientId = "";
        public string[] runtimeOAuthScopes = Array.Empty<string>();
    }

    /// <summary>
    /// C# mirror of the P53 rig manifest (<c>rig.json</c>) the Neo rig
    /// coordinator writes. The shape is the coordinator's <c>IRigManifest</c> at
    /// <c>neo-compose/scripts/agent-rig/lib.mts</c>; this reader accepts exactly
    /// that shape at <see cref="NeoComposeRigManifestReader.SupportedFormatVersion"/>.
    /// </summary>
    public sealed class NeoComposeRigManifest
    {
        public int formatVersion;
        public string rigId = "";
        public string createdAt = "";
        public string expiresAt = "";

        /// <summary>
        /// The repository the rig's agent implements in — <c>neo-compose</c> or
        /// <c>neo-compose-dotnet</c>. Null on manifests written before the
        /// coordinator recorded it; those rigs could only ever be app-sourced.
        /// Unity reads it for diagnostics only: both worktrees consume the same
        /// endpoints and the same sample either way.
        /// </summary>
        public string? source;

        public NeoComposeRigRepositories repositories = new();
        public NeoComposeRigDeployment deployment = new();
        public NeoComposeRigWeb web = new();

        /// <summary>Null until a durable seed catalog exists.</summary>
        public NeoComposeRigSeed? seed;

        /// <summary>Null until the seed carries the canonical HelloWorld fixture.</summary>
        public NeoComposeRigSample? sample;
    }

    /// <summary>
    /// Reads and validates a rig manifest. The manifest is contractually
    /// non-secret (P53 §2): it is read by tooling that logs freely, so a
    /// secret-looking field name is a hard failure rather than a redaction.
    /// </summary>
    public static class NeoComposeRigManifestReader
    {
        /// <summary>
        /// The only manifest format this reader understands. A newer coordinator
        /// bumps it and this reader refuses rather than guessing.
        /// </summary>
        public const int SupportedFormatVersion = 1;

        /// <summary>
        /// Mirrors the coordinator's <c>SECRET_FIELD_PATTERN</c>. Kept as a field
        /// name test, not a value test — the manifest must never grow a place to
        /// put credential material in the first place.
        /// </summary>
        internal static readonly Regex SecretFieldPattern = new(
            "secret|token|key|password|credential|cookie|authorization",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// The two deliberate exceptions the spec's manifest shape carries. A
        /// runtime OAuth <em>client id</em> and its scopes are public identifiers,
        /// not credentials.
        /// </summary>
        internal static readonly string[] AllowedSecretLookingFields =
        {
            "runtimeOAuthClientId",
            "runtimeOAuthScopes",
        };

        /// <summary>Reads and validates the manifest at <paramref name="manifestPath"/>.</summary>
        public static NeoComposeRigManifest Read(string manifestPath)
        {
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException(
                    $"Rig manifest {manifestPath} does not exist. Run \"npm run agent:setup\" from the Neo workspace.");
            }

            return Parse(File.ReadAllText(manifestPath), manifestPath);
        }

        /// <summary>
        /// Validates then deserializes rig manifest JSON. <paramref name="sourcePath"/>
        /// only pinpoints failures; it is never read from.
        /// </summary>
        public static NeoComposeRigManifest Parse(string json, string sourcePath)
        {
            JToken root;
            try
            {
                // DateParseHandling.None keeps createdAt / expiresAt as the exact
                // ISO strings the coordinator wrote. Newtonsoft otherwise converts
                // them to DateTime on parse and re-formats them on the way out,
                // which would silently rewrite the manifest's own timestamps.
                using var textReader = new StringReader(json);
                using var jsonReader = new JsonTextReader(textReader)
                {
                    DateParseHandling = DateParseHandling.None,
                };
                root = JToken.ReadFrom(jsonReader);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Rig manifest {sourcePath} is not valid JSON: {exception.Message}");
            }

            if (root is not JObject document)
            {
                throw new InvalidOperationException($"Rig manifest {sourcePath} must be a JSON object.");
            }

            var declaredFormatVersion = document["formatVersion"]?.Value<int?>();
            if (declaredFormatVersion != SupportedFormatVersion)
            {
                throw new InvalidOperationException(
                    $"Rig manifest {sourcePath} declares formatVersion " +
                    $"{document["formatVersion"]?.ToString() ?? "(missing)"}; this reader understands " +
                    $"{SupportedFormatVersion}.");
            }

            if (string.IsNullOrWhiteSpace(document["rigId"]?.Value<string>()))
            {
                throw new InvalidOperationException($"Rig manifest {sourcePath} is missing rigId.");
            }

            if (string.IsNullOrWhiteSpace(document["deployment"]?["name"]?.Value<string>()))
            {
                throw new InvalidOperationException($"Rig manifest {sourcePath} is missing deployment.name.");
            }

            if (string.IsNullOrWhiteSpace(document["web"]?["origin"]?.Value<string>()))
            {
                throw new InvalidOperationException($"Rig manifest {sourcePath} is missing web.origin.");
            }

            if (document["web"]?["port"]?.Value<int?>() == null)
            {
                throw new InvalidOperationException($"Rig manifest {sourcePath} is missing web.port.");
            }

            if (string.IsNullOrWhiteSpace(document["deployment"]?["url"]?.Value<string>()))
            {
                throw new InvalidOperationException($"Rig manifest {sourcePath} is missing deployment.url.");
            }

            AssertCarriesNoSecrets(document, sourcePath, "$");

            var manifest = document.ToObject<NeoComposeRigManifest>();
            if (manifest == null)
            {
                throw new InvalidOperationException($"Rig manifest {sourcePath} could not be deserialized.");
            }

            return manifest;
        }

        /// <summary>
        /// Walks every field name in the document and rejects any that looks
        /// secret-bearing, mirroring the coordinator's
        /// <c>assertManifestCarriesNoSecrets</c> so both sides fail the same way.
        /// </summary>
        internal static void AssertCarriesNoSecrets(JToken token, string sourcePath, string path)
        {
            if (token is JArray array)
            {
                for (var index = 0; index < array.Count; index++)
                {
                    AssertCarriesNoSecrets(array[index], sourcePath, $"{path}[{index}]");
                }

                return;
            }

            if (token is not JObject document) return;

            foreach (var property in document.Properties())
            {
                if (SecretFieldPattern.IsMatch(property.Name) &&
                    Array.IndexOf(AllowedSecretLookingFields, property.Name) < 0)
                {
                    throw new InvalidOperationException(
                        $"Rig manifest {sourcePath} field {path}.{property.Name} looks secret-bearing; " +
                        "the manifest must stay non-secret (P53 §2).");
                }

                AssertCarriesNoSecrets(property.Value, sourcePath, $"{path}.{property.Name}");
            }
        }
    }

    /// <summary>
    /// The gitignored pointer file a bound worktree carries, associating it with
    /// exactly one rig manifest.
    /// </summary>
    public sealed class NeoComposeRigPointer
    {
        public string rigId = "";
        public string manifestPath = "";
    }

    /// <summary>An active rig manifest together with the path it was read from.</summary>
    public sealed class NeoComposeRigResolution
    {
        public NeoComposeRigResolution(string manifestPath, NeoComposeRigManifest manifest)
        {
            ManifestPath = manifestPath;
            Manifest = manifest;
        }

        public string ManifestPath { get; }

        public NeoComposeRigManifest Manifest { get; }

        /// <summary>One-line rig identity for status lines and headless logs.</summary>
        public string Describe()
        {
            return $"rig {Manifest.rigId} → deployment {Manifest.deployment.name} ({Manifest.web.origin})";
        }
    }

    /// <summary>
    /// Finds the rig manifest that governs this Unity project, if any.
    /// <see cref="ManifestEnvironmentVariable"/> is the explicit headless
    /// override and must agree with the worktree pointer whenever both exist —
    /// headless Unity rejects disagreement rather than picking a winner
    /// (P53 §6).
    /// </summary>
    public static class NeoComposeRigManifestResolver
    {
        public const string ManifestEnvironmentVariable = "NEO_COMPOSE_RIG_MANIFEST";

        /// <summary>
        /// Gitignored pointer file name. It lives at the bound repository /
        /// worktree root, above the Unity project, so resolution walks up.
        /// </summary>
        public const string PointerFileName = ".neo-rig";

        /// <summary>
        /// Resolves the active rig for this Unity project, or null when no rig is
        /// bound. Throws when a rig <em>is</em> bound but its wiring is
        /// inconsistent or its manifest is invalid.
        /// </summary>
        public static NeoComposeRigResolution? Resolve()
        {
            return Resolve(
                UnityProjectRoot(),
                Environment.GetEnvironmentVariable(ManifestEnvironmentVariable));
        }

        /// <summary>Seam overload so resolution is testable without touching process state.</summary>
        internal static NeoComposeRigResolution? Resolve(string startDirectory, string? environmentOverride)
        {
            var manifestPath = ResolveActiveManifestPath(startDirectory, environmentOverride);
            if (manifestPath == null) return null;

            return new NeoComposeRigResolution(
                manifestPath,
                NeoComposeRigManifestReader.Read(manifestPath));
        }

        /// <summary>
        /// The absolute manifest path the environment variable or pointer selects,
        /// or null when neither exists.
        /// </summary>
        internal static string? ResolveActiveManifestPath(string startDirectory, string? environmentOverride)
        {
            var pointerPath = FindPointerFile(startDirectory);
            var pointer = pointerPath == null ? null : ReadPointer(pointerPath);

            if (string.IsNullOrWhiteSpace(environmentOverride))
            {
                return pointer?.manifestPath;
            }

            if (!Path.IsPathRooted(environmentOverride))
            {
                throw new InvalidOperationException(
                    $"{ManifestEnvironmentVariable} must be an absolute path, but was \"{environmentOverride}\".");
            }

            if (pointer != null && !PathsAgree(pointer.manifestPath, environmentOverride!))
            {
                throw new InvalidOperationException(
                    $"{ManifestEnvironmentVariable} ({environmentOverride}) disagrees with the rig pointer " +
                    $"{pointerPath} ({pointer.manifestPath}). Fix one before continuing.");
            }

            return environmentOverride;
        }

        /// <summary>
        /// Walks up from <paramref name="startDirectory"/> to the filesystem root
        /// looking for the pointer file, returning its absolute path or null.
        /// </summary>
        internal static string? FindPointerFile(string startDirectory)
        {
            var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, PointerFileName);
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }

            return null;
        }

        /// <summary>Reads and validates a pointer file.</summary>
        internal static NeoComposeRigPointer ReadPointer(string pointerPath)
        {
            JToken root;
            try
            {
                root = JToken.Parse(File.ReadAllText(pointerPath));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Rig pointer {pointerPath} is not valid JSON: {exception.Message}");
            }

            if (root is not JObject document)
            {
                throw new InvalidOperationException($"Rig pointer {pointerPath} must be a JSON object.");
            }

            var rigId = document["rigId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(rigId))
            {
                throw new InvalidOperationException($"Rig pointer {pointerPath} is missing rigId.");
            }

            var manifestPath = document["manifestPath"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                throw new InvalidOperationException($"Rig pointer {pointerPath} is missing manifestPath.");
            }

            return new NeoComposeRigPointer
            {
                rigId = rigId!,
                manifestPath = manifestPath!,
            };
        }

        /// <summary>Unity project root — the directory holding <c>Assets</c>.</summary>
        internal static string UnityProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static bool PathsAgree(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.Ordinal);
        }
    }
}
