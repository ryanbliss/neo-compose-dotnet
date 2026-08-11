// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using NeoCompose.Unity.Editor;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Golden-fixture coverage for the P53 rig manifest reader.
    /// </summary>
    /// <remarks>
    /// The two fixtures under <c>Packages/com.ryanbliss.neocompose/Tests/</c> are
    /// the shapes the Neo rig coordinator writes at <c>formatVersion</c> 1: an
    /// unseeded rig (no seed catalog entry, no sample project yet) and a seeded
    /// one carrying the canonical HelloWorld fixture. Unity must consume both
    /// without the coordinator having to trim anything.
    /// </remarks>
    public class NeoComposeRigManifestTests
    {
        private const string PackageRoot =
            "Packages/com.ryanbliss.neocompose/Tests";

        private const string UnseededFixture = "rig-manifest-unseeded.json";
        private const string SeededFixture = "rig-manifest-seeded.json";

        private string tempRoot = "";

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(
                Path.GetTempPath(),
                "neo-compose-rig-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }

        private static string LoadFixture(string fileName)
        {
            return File.ReadAllText(Path.Combine(PackageRoot, fileName));
        }

        [Test]
        public void RigManifest_ParsesUnseededFixture()
        {
            var manifest = NeoComposeRigManifestReader.Parse(
                LoadFixture(UnseededFixture),
                UnseededFixture);

            Assert.AreEqual(1, manifest.formatVersion);
            Assert.AreEqual("rig-20260810-formal-toucan", manifest.rigId);
            Assert.AreEqual("2026-08-10T14:22:05.317Z", manifest.createdAt);
            Assert.AreEqual("2026-08-13T14:22:05.317Z", manifest.expiresAt);
            Assert.AreEqual("formal-toucan-689", manifest.deployment.name);
            Assert.AreEqual("dev:formal-toucan-689", manifest.deployment.reference);
            Assert.AreEqual("https://formal-toucan-689.convex.cloud", manifest.deployment.url);
            Assert.AreEqual("https://formal-toucan-689.convex.site", manifest.deployment.siteUrl);
            Assert.AreEqual("http://127.0.0.1:31100", manifest.web.origin);
            Assert.AreEqual(31100, manifest.web.port);
            Assert.AreEqual("http://127.0.0.1:31101", manifest.web.partyServerOrigin);
            Assert.AreEqual(31101, manifest.web.partyServerPort);
            Assert.IsNotNull(manifest.repositories.neoCompose);
            // App-only rigs never select an SDK worktree.
            Assert.IsNull(manifest.repositories.neoComposeDotnet);
            Assert.IsNull(manifest.seed);
            Assert.IsNull(manifest.sample);
            // Manifests written before the coordinator recorded the source stay
            // readable: the field is absent, not empty.
            Assert.IsNull(manifest.source);
        }

        [Test]
        public void RigManifest_ParsesSeededFixture()
        {
            var manifest = NeoComposeRigManifestReader.Parse(
                LoadFixture(SeededFixture),
                SeededFixture);

            Assert.AreEqual("rig-20260810-brisk-ocelot", manifest.rigId);
            Assert.AreEqual("neo-compose-dotnet", manifest.source);
            Assert.AreEqual("brisk-ocelot-412", manifest.deployment.name);
            Assert.AreEqual("http://127.0.0.1:31200", manifest.web.origin);
            Assert.IsNotNull(manifest.repositories.neoComposeDotnet);
            Assert.AreEqual(
                "b3b3dd4c7e1a92f0",
                manifest.repositories.neoComposeDotnet!.worktreeCommit);

            Assert.IsNotNull(manifest.seed);
            Assert.AreEqual("helloworld-2026-08-10", manifest.seed!.id);
            Assert.AreEqual(17, manifest.seed.schemaEpoch);
            Assert.AreEqual(2, manifest.seed.migrationReceipts.Length);

            Assert.IsNotNull(manifest.sample);
            Assert.AreEqual("Rig Brisk Ocelot", manifest.sample!.organizationName);
            Assert.AreEqual("k57c3n1q8w2m0v9xr4jb6d5haz", manifest.sample.projectId);
            Assert.AreEqual("Hello World", manifest.sample.projectName);
            Assert.AreEqual("j92f7t0p3s6y1k4nw8rc5b2mqe", manifest.sample.versionId);
            Assert.AreEqual("h41d9x5v7z0g2l6ps3bt8n1krw", manifest.sample.releaseChannelId);
            // The two deliberate exceptions to the secret-field rejection: a
            // client id and its scopes are public identifiers.
            Assert.AreEqual("neo-rig-brisk-ocelot-hello-world", manifest.sample.runtimeOAuthClientId);
            Assert.AreEqual(2, manifest.sample.runtimeOAuthScopes.Length);
        }

        [Test]
        public void RigManifest_RejectsUnsupportedFormatVersion()
        {
            var json = LoadFixture(UnseededFixture).Replace("\"formatVersion\": 1", "\"formatVersion\": 2");

            var exception = Assert.Throws<InvalidOperationException>(
                () => NeoComposeRigManifestReader.Parse(json, "rig.json"));
            Assert.AreEqual(
                "Rig manifest rig.json declares formatVersion 2; this reader understands 1.",
                exception!.Message);
        }

        [Test]
        public void RigManifest_RejectsMissingFormatVersion()
        {
            Assert.Throws<InvalidOperationException>(
                () => NeoComposeRigManifestReader.Parse("{\"rigId\":\"a\"}", "rig.json"));
        }

        [Test]
        public void RigManifest_RejectsMissingRequiredFields()
        {
            var missingRigId = LoadFixture(UnseededFixture)
                .Replace("\"rigId\": \"rig-20260810-formal-toucan\"", "\"rigId\": \"\"");
            Assert.IsTrue(
                Assert.Throws<InvalidOperationException>(
                        () => NeoComposeRigManifestReader.Parse(missingRigId, "rig.json"))!
                    .Message.Contains("missing rigId"));

            var missingDeploymentName = LoadFixture(UnseededFixture)
                .Replace("\"name\": \"formal-toucan-689\"", "\"name\": \"\"");
            Assert.IsTrue(
                Assert.Throws<InvalidOperationException>(
                        () => NeoComposeRigManifestReader.Parse(missingDeploymentName, "rig.json"))!
                    .Message.Contains("missing deployment.name"));

            var missingWebOrigin = LoadFixture(UnseededFixture)
                .Replace("\"origin\": \"http://127.0.0.1:31100\"", "\"origin\": \"\"");
            Assert.IsTrue(
                Assert.Throws<InvalidOperationException>(
                        () => NeoComposeRigManifestReader.Parse(missingWebOrigin, "rig.json"))!
                    .Message.Contains("missing web.origin"));

            var missingWebPort = LoadFixture(UnseededFixture)
                .Replace("\"port\": 31100,", "");
            Assert.IsTrue(
                Assert.Throws<InvalidOperationException>(
                        () => NeoComposeRigManifestReader.Parse(missingWebPort, "rig.json"))!
                    .Message.Contains("missing web.port"));

            var missingDeploymentUrl = LoadFixture(UnseededFixture)
                .Replace(
                    "\"url\": \"https://formal-toucan-689.convex.cloud\",",
                    "\"url\": \"\",");
            Assert.IsTrue(
                Assert.Throws<InvalidOperationException>(
                        () => NeoComposeRigManifestReader.Parse(missingDeploymentUrl, "rig.json"))!
                    .Message.Contains("missing deployment.url"));
        }

        [Test]
        public void RigManifest_RejectsSecretBearingFieldNames()
        {
            var withSecret = LoadFixture(UnseededFixture)
                .Replace(
                    "\"name\": \"formal-toucan-689\",",
                    "\"name\": \"formal-toucan-689\",\n    \"deployKey\": \"dev:formal-toucan-689|abc\",");

            var exception = Assert.Throws<InvalidOperationException>(
                () => NeoComposeRigManifestReader.Parse(withSecret, "rig.json"));
            Assert.IsTrue(exception!.Message.Contains("$.deployment.deployKey"));
            Assert.IsTrue(exception.Message.Contains("must stay non-secret"));
        }

        [Test]
        public void RigManifest_RejectsSecretBearingFieldNamesInsideArrays()
        {
            const string json =
                "{\"formatVersion\":1,\"rigId\":\"r\"," +
                "\"deployment\":{\"name\":\"d\",\"url\":\"https://d.convex.cloud\"}," +
                "\"web\":{\"origin\":\"http://127.0.0.1:1\",\"port\":1}," +
                "\"extras\":[{\"sessionCookie\":\"x\"}]}";

            var exception = Assert.Throws<InvalidOperationException>(
                () => NeoComposeRigManifestReader.Parse(json, "rig.json"));
            Assert.IsTrue(exception!.Message.Contains("$.extras[0].sessionCookie"));
        }

        [Test]
        public void RigManifestResolver_ReturnsNullWithoutEnvironmentOrPointer()
        {
            var projectRoot = Path.Combine(tempRoot, "repo", "samples", "HelloWorld");
            Directory.CreateDirectory(projectRoot);

            Assert.IsNull(NeoComposeRigManifestResolver.Resolve(projectRoot, null));
            Assert.IsNull(NeoComposeRigManifestResolver.Resolve(projectRoot, ""));
        }

        [Test]
        public void RigManifestResolver_FindsPointerByWalkingUpFromTheProject()
        {
            var repositoryRoot = Path.Combine(tempRoot, "repo");
            var projectRoot = Path.Combine(repositoryRoot, "samples", "HelloWorld");
            Directory.CreateDirectory(projectRoot);
            var manifestPath = WriteManifest(UnseededFixture);
            WritePointer(repositoryRoot, "rig-20260810-formal-toucan", manifestPath);

            var resolution = NeoComposeRigManifestResolver.Resolve(projectRoot, null);

            Assert.IsNotNull(resolution);
            Assert.AreEqual(manifestPath, resolution!.ManifestPath);
            Assert.AreEqual("rig-20260810-formal-toucan", resolution.Manifest.rigId);
        }

        [Test]
        public void RigManifestResolver_AcceptsEnvironmentOverrideAgreeingWithThePointer()
        {
            var repositoryRoot = Path.Combine(tempRoot, "repo");
            var projectRoot = Path.Combine(repositoryRoot, "samples", "HelloWorld");
            Directory.CreateDirectory(projectRoot);
            var manifestPath = WriteManifest(UnseededFixture);
            WritePointer(repositoryRoot, "rig-20260810-formal-toucan", manifestPath);

            var resolution = NeoComposeRigManifestResolver.Resolve(projectRoot, manifestPath);

            Assert.IsNotNull(resolution);
            Assert.AreEqual("formal-toucan-689", resolution!.Manifest.deployment.name);
        }

        [Test]
        public void RigManifestResolver_RejectsEnvironmentPointerDisagreement()
        {
            var repositoryRoot = Path.Combine(tempRoot, "repo");
            var projectRoot = Path.Combine(repositoryRoot, "samples", "HelloWorld");
            Directory.CreateDirectory(projectRoot);
            var pointedManifest = WriteManifest(UnseededFixture);
            var otherManifest = WriteManifest(SeededFixture, "other-rig.json");
            WritePointer(repositoryRoot, "rig-20260810-formal-toucan", pointedManifest);

            var exception = Assert.Throws<InvalidOperationException>(
                () => NeoComposeRigManifestResolver.Resolve(projectRoot, otherManifest));
            Assert.IsTrue(exception!.Message.Contains("NEO_COMPOSE_RIG_MANIFEST"));
            Assert.IsTrue(exception.Message.Contains("disagrees with the rig pointer"));
        }

        [Test]
        public void RigManifestResolver_RejectsRelativeEnvironmentOverride()
        {
            var projectRoot = Path.Combine(tempRoot, "repo", "samples", "HelloWorld");
            Directory.CreateDirectory(projectRoot);

            var exception = Assert.Throws<InvalidOperationException>(
                () => NeoComposeRigManifestResolver.Resolve(projectRoot, "rig.json"));
            Assert.IsTrue(exception!.Message.Contains("must be an absolute path"));
        }

        [Test]
        public void RigManifestResolver_RejectsPointerWithoutManifestPath()
        {
            var repositoryRoot = Path.Combine(tempRoot, "repo");
            var projectRoot = Path.Combine(repositoryRoot, "samples", "HelloWorld");
            Directory.CreateDirectory(projectRoot);
            File.WriteAllText(
                Path.Combine(repositoryRoot, NeoComposeRigManifestResolver.PointerFileName),
                "{\"rigId\":\"rig-1\"}");

            var exception = Assert.Throws<InvalidOperationException>(
                () => NeoComposeRigManifestResolver.Resolve(projectRoot, null));
            Assert.IsTrue(exception!.Message.Contains("missing manifestPath"));
        }

        private string WriteManifest(string fixtureFileName, string writtenFileName = "rig.json")
        {
            var path = Path.Combine(tempRoot, writtenFileName);
            File.WriteAllText(path, LoadFixture(fixtureFileName));
            return path;
        }

        private static void WritePointer(string worktreeDirectory, string rigId, string manifestPath)
        {
            Directory.CreateDirectory(worktreeDirectory);
            File.WriteAllText(
                Path.Combine(worktreeDirectory, NeoComposeRigManifestResolver.PointerFileName),
                "{\n  \"rigId\": \"" + rigId + "\",\n  \"manifestPath\": \"" +
                manifestPath.Replace("\\", "\\\\") + "\"\n}\n");
        }
    }
}
