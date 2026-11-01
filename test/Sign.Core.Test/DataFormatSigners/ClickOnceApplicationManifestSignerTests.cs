// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sign.TestInfrastructure;

namespace Sign.Core.Test
{
    public sealed class ClickOnceApplicationManifestSignerTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task ExecuteAsync_RefreshesHashesAndCopiesOnlySignedResults(
            bool mapped,
            bool outOfPlace)
        {
            await using Harness harness = new();
            string physicalName = mapped ? "payload.dll.deploy" : "payload.dll";
            FileInfo payload = harness.CreateFile(physicalName, "unsigned");
            FileInfo excluded = harness.CreateFile("excluded.dll", "excluded");
            FileInfo unsupported = harness.CreateFile("data.txt", "data");
            FileInfo unrelated = harness.CreateFile("Unrelated.exe", "unrelated");
            FileInfo manifest = harness.CreateManifest(
                "App.manifest", "payload.dll", "excluded.dll", "data.txt");
            Matcher antiMatcher = new();
            antiMatcher.AddInclude("excluded.dll");
            SignOptions options = harness.Options(antiMatcher: antiMatcher);
            FileInfo output = outOfPlace
                ? new FileInfo(Path.Combine(harness.Source.FullName, "output", "Renamed.manifest"))
                : manifest;

            await harness.ExecuteAsync(manifest, output, options);

            Assert.Equal("unsigned signed", File.ReadAllText(
                Path.Combine(output.DirectoryName!, physicalName)));
            Assert.Equal("excluded", File.ReadAllText(excluded.FullName));
            Assert.Equal("data", File.ReadAllText(unsupported.FullName));
            Assert.Equal("unrelated", File.ReadAllText(unrelated.FullName));
            if (outOfPlace)
            {
                Assert.Equal("unsigned", File.ReadAllText(payload.FullName));
                Assert.False(File.Exists(Path.Combine(output.DirectoryName!, "excluded.dll")));
                Assert.False(File.Exists(Path.Combine(output.DirectoryName!, "data.txt")));
            }
            Assert.Single(harness.PayloadCalls);
            Assert.Single(harness.ManifestCalls);
            AssertNoStagingDirectories(harness);

            using FileStream stream = output.OpenRead();
            ClickOnceManifestReader reader = new();
            Assert.True(reader.TryReadApplicationManifest(stream, out IApplicationManifest? updated));
            using (updated)
            {
                Assert.NotNull(updated);
                foreach (FileReference reference in updated.FileReferences)
                {
                    string contents = reference.TargetPath switch
                    {
                        "payload.dll" => "unsigned signed",
                        "excluded.dll" => "excluded",
                        _ => "data"
                    };
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(contents);
                    Assert.Equal(bytes.Length, reference.Size);
                    Assert.Equal(Convert.ToBase64String(SHA256.HashData(bytes)), reference.Hash);
                }
            }
            XDocument serialized = XDocument.Load(output.FullName);
            Assert.All(serialized.Descendants(XName.Get(
                "DigestMethod", "http://www.w3.org/2000/09/xmldsig#")),
                element => Assert.Equal("http://www.w3.org/2000/09/xmldsig#sha256",
                    element.Attribute("Algorithm")?.Value));
            Assert.Contains("<!--signed-->", File.ReadAllText(output.FullName));
        }

        [Theory]
        [InlineData("payload.deploy", "payload.deploy")]
        [InlineData("payload.deploy", "payload.deploy.deploy")]
        [InlineData("payload.dll", "payload.dll.deploy")]
        public async Task ExecuteAsync_RemovesOnlyMappingAddedSuffix(
            string targetPath,
            string physicalPath)
        {
            await using Harness harness = new();
            harness.FormatSigner.CanSign(Arg.Any<FileInfo>()).Returns(true);
            FileInfo payload = harness.CreateFile(physicalPath);
            FileInfo manifest = harness.CreateManifest("App.manifest", targetPath);

            await harness.ExecuteAsync(manifest);

            Assert.Equal(targetPath, Assert.Single(harness.PayloadCalls).Name);
            Assert.True(File.Exists(payload.FullName));
            Assert.Equal("payload signed", File.ReadAllText(payload.FullName));
            if (physicalPath != targetPath)
            {
                Assert.False(File.Exists(Path.Combine(harness.Source.FullName, targetPath)));
            }
            AssertNoStagingDirectories(harness);
        }

        [Fact]
        public async Task ExecuteAsync_ExactPathWinsOverMappedCandidate()
        {
            await using Harness harness = new();
            FileInfo exact = harness.CreateFile("payload.dll", "exact");
            FileInfo mapped = harness.CreateFile("payload.dll.deploy", "mapped");
            FileInfo manifest = harness.CreateManifest("App.manifest", "payload.dll");

            await harness.ExecuteAsync(manifest);

            Assert.Equal("exact signed", File.ReadAllText(exact.FullName));
            Assert.Equal("mapped", File.ReadAllText(mapped.FullName));
            Assert.DoesNotContain(harness.Logger.Entries, entry => entry.LogLevel == LogLevel.Error);
        }

        [Fact]
        public async Task ExecuteAsync_MatcherAndAntiMatcherApplyToPublishedFilenames()
        {
            await using Harness harness = new();
            harness.CreateFile("include.dll.deploy");
            harness.CreateFile("exclude.dll.deploy");
            harness.CreateFile("unmatched.dll");
            FileInfo manifest = harness.CreateManifest(
                "App.manifest", "include.dll", "exclude.dll", "unmatched.dll");
            Matcher matcher = new();
            matcher.AddInclude("*.deploy");
            Matcher antiMatcher = new();
            antiMatcher.AddInclude("exclude.dll.deploy");

            await harness.ExecuteAsync(manifest, options:
                harness.Options(matcher, antiMatcher));

            Assert.Equal("include.dll", Assert.Single(harness.PayloadCalls).Name);
            Assert.Equal("payload", File.ReadAllText(
                Path.Combine(harness.Source.FullName, "exclude.dll.deploy")));
            Assert.Equal("payload", File.ReadAllText(
                Path.Combine(harness.Source.FullName, "unmatched.dll")));
        }

        [Fact]
        public async Task ExecuteAsync_AntiMatcherWorksWithoutMatcher()
        {
            await using Harness harness = new();
            harness.CreateFile("payload.dll");
            FileInfo manifest = harness.CreateManifest("App.manifest", "payload.dll");
            Matcher antiMatcher = new();
            antiMatcher.AddInclude("**/*");

            await harness.ExecuteAsync(manifest, options:
                harness.Options(antiMatcher: antiMatcher));

            Assert.Empty(harness.PayloadCalls);
            Assert.Single(harness.ManifestCalls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_ManifestOnlyPolicyNeverSignsOrCopiesPayloads(bool mapped)
        {
            await using Harness harness = new();
            string physicalName = mapped ? "payload.dll.deploy" : "payload.dll";
            FileInfo payload = harness.CreateFile(physicalName);
            FileInfo manifest = harness.CreateManifest("App.manifest", "payload.dll");
            FileInfo output = new(Path.Combine(harness.Source.FullName, "out", "App.manifest"));

            await harness.ExecuteAsync(manifest, output,
                policy: ClickOnceApplicationSigningPolicy.UpdateManifestOnly);

            Assert.Empty(harness.PayloadCalls);
            Assert.Single(harness.ManifestCalls);
            Assert.Equal("payload", File.ReadAllText(payload.FullName));
            Assert.False(File.Exists(Path.Combine(output.DirectoryName!, physicalName)));
            Assert.Contains("<!--signed-->", File.ReadAllText(output.FullName));
            AssertNoStagingDirectories(harness);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_CorrectsMissingAndStaleAssemblyIdentities(bool stale)
        {
            await using Harness harness = new();
            FileInfo payload = harness.CopyAssembly("payload.dll");
            ApplicationManifest application = ClickOnceResolutionTestUtilities.CreateApplicationManifest();
            AssemblyReference reference = new("payload.dll")
            {
                AssemblyIdentity = stale
                    ? new AssemblyIdentity { Name = "Stale", Version = "99.0.0.0" }
                    : null!
            };
            application.AssemblyReferences.Add(reference);
            FileInfo manifest = harness.WriteManifest("App.manifest", application);

            await harness.ExecuteAsync(manifest);

            using FileStream stream = manifest.OpenRead();
            ClickOnceManifestReader reader = new();
            Assert.True(reader.TryReadApplicationManifest(stream, out IApplicationManifest? updated));
            using (updated)
            {
                Assert.NotNull(updated);
                AssemblyReference actual = Assert.Single(
                    ClickOncePayloadResolver.GetPhysicalReferences(updated)
                        .OfType<AssemblyReference>());
                AssemblyIdentity expected = AssemblyIdentity.FromManagedAssembly(payload.FullName);
                Assert.Equal(expected.Name, actual.AssemblyIdentity.Name);
                Assert.Equal(expected.Version, actual.AssemblyIdentity.Version);
                Assert.Equal(expected.PublicKeyToken, actual.AssemblyIdentity.PublicKeyToken);
                Assert.Equal(Convert.ToBase64String(SHA256.HashData(
                    File.ReadAllBytes(payload.FullName))), actual.Hash);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_ReferencedLauncherIsAnOrdinaryPayload(bool entryPoint)
        {
            await using Harness harness = new();
            harness.CopyAssembly("Launcher.exe");
            ApplicationManifest application = ClickOnceResolutionTestUtilities.CreateApplicationManifest();
            AssemblyReference launcher = new("Launcher.exe");
            application.AssemblyReferences.Add(launcher);
            if (entryPoint)
            {
                application.EntryPoint = launcher;
            }
            FileInfo manifest = harness.WriteManifest("App.manifest", application);

            await harness.ExecuteAsync(manifest);

            Assert.Equal("Launcher.exe", Assert.Single(harness.PayloadCalls).Name);
            Assert.Single(harness.ManifestCalls);
        }

        [Fact]
        public async Task ExecuteAsync_NativeLauncherWithoutEmbeddedManifestRemainsAnOrdinaryPayload()
        {
            await using Harness harness = new();
            FileInfo launcherFile = new(Path.Combine(harness.Source.FullName, "Launcher.exe"));
            File.Copy(Path.Combine(AppContext.BaseDirectory,
                "tools", "SDK", "x64", "NavSip.dll"), launcherFile.FullName);
            ApplicationManifest application = ClickOnceResolutionTestUtilities.CreateApplicationManifest();
            AssemblyReference launcher = new("Launcher.exe")
            {
                ReferenceType = AssemblyReferenceType.NativeAssembly,
                AssemblyIdentity = new AssemblyIdentity
                {
                    Name = "Launcher.exe",
                    Version = "1.0.0.0",
                    Type = "win32"
                }
            };
            application.AssemblyReferences.Add(launcher);
            application.EntryPoint = launcher;
            FileInfo manifest = harness.WriteManifest("App.manifest", application);

            await harness.ExecuteAsync(manifest);

            Assert.Equal("Launcher.exe", Assert.Single(harness.PayloadCalls).Name);
            Assert.Single(harness.ManifestCalls);
        }

        [Fact]
        public async Task ExecuteAsync_MappedLookupDoesNotProduceRecoveredProbeErrors()
        {
            await using Harness harness = new();
            harness.CreateFile("payload.dll.deploy");
            FileInfo manifest = harness.CreateManifest("App.manifest", "payload.dll");

            await harness.ExecuteAsync(manifest);

            Assert.Empty(harness.Logger.Entries);
            Assert.Single(harness.PayloadCalls);
        }

        [Fact]
        public async Task ExecuteAsync_LogsDiagnosticsEvenWhenResolutionThrows()
        {
            await using Harness harness = new();
            harness.CreateFile("payload.dll");
            FileInfo manifest = harness.CreateManifest("App.manifest", "payload.dll");
            harness.UseFakeManifest("App.manifest", "payload.dll");
            harness.AfterResolve = _ =>
            {
                harness.Diagnostics.Add(new ClickOnceManifestDiagnostic(
                    "warning", "warning before failure", OutputMessageType.Warning));
                throw new IOException("resolution failed");
            };

            SigningException exception = await Assert.ThrowsAsync<SigningException>(
                () => harness.ExecuteAsync(manifest));

            Assert.IsType<IOException>(exception.InnerException);
            Assert.Equal("warning before failure", Assert.Single(harness.Logger.Entries).Message);
            Assert.Empty(harness.PayloadCalls);
            Assert.Empty(harness.ManifestCalls);
            AssertNoStagingDirectories(harness);
        }

        [Fact]
        public async Task ExecuteAsync_PrerequisitesAndClrReferencesAreNotPayloads()
        {
            await using Harness harness = new();
            ApplicationManifest application = ClickOnceResolutionTestUtilities.CreateApplicationManifest();
            application.AssemblyReferences.Add(new AssemblyReference
            {
                IsPrerequisite = true,
                AssemblyIdentity = new AssemblyIdentity { Name = "Prerequisite", Version = "1.0.0.0" }
            });
            application.AssemblyReferences.Add(new AssemblyReference
            {
                IsPrerequisite = true,
                AssemblyIdentity = new AssemblyIdentity
                {
                    Name = "Microsoft.Windows.CommonLanguageRuntime",
                    Version = "4.0.30319.0"
                }
            });
            FileInfo manifest = harness.WriteManifest("App.manifest", application);

            await harness.ExecuteAsync(manifest);

            Assert.Empty(harness.PayloadCalls);
            Assert.Single(harness.ManifestCalls);
        }

        [Theory]
        [InlineData("<assembly>")]
        [InlineData("<configuration />")]
        [InlineData("""
            <assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
              <assemblyIdentity name="SideBySide" version="1.0.0.0" type="win32" />
            </assembly>
            """)]
        public async Task ExecuteAsync_RejectsUnreadableOrNonApplicationManifestsWithoutChanges(string xml)
        {
            await using Harness harness = new();
            FileInfo manifest = harness.CreateFile("App.manifest", xml);

            Exception exception = await Assert.ThrowsAnyAsync<Exception>(
                () => harness.ExecuteAsync(manifest));

            Assert.True(exception is SigningException or ClickOncePublishLayoutResolutionException);
            Assert.Equal(xml, File.ReadAllText(manifest.FullName));
            Assert.Empty(harness.PayloadCalls);
            Assert.Empty(harness.ManifestCalls);
            AssertNoStagingDirectories(harness);
        }

        [Fact]
        public async Task ExecuteAsync_RejectsDeploymentManifest()
        {
            await using Harness harness = new();
            FileInfo manifest = ClickOnceResolutionTestUtilities.WriteDeploymentManifest(
                harness.Source, "App.application", "App.manifest");

            await Assert.ThrowsAsync<SigningException>(() => harness.ExecuteAsync(manifest));

            Assert.Empty(harness.ManifestCalls);
        }

        [Fact]
        public async Task StandardSigner_DoesNotSelectDormantPipelineForOtherManifestFormats()
        {
            await using Harness harness = new();
            harness.FormatSigner.CanSign(Arg.Any<FileInfo>()).Returns(true);
            FileInfo manifest = harness.CreateFile("Other.manifest", "<other />");

            await harness.AggregatingSigner.SignAsync(new[] { manifest }, harness.Options());

            Assert.Single(harness.PayloadCalls);
            Assert.Empty(harness.ManifestCalls);
            Assert.False(typeof(IDataFormatSigner).IsAssignableFrom(
                typeof(ClickOnceApplicationManifestSigner)));
            Assert.False(new ClickOnceSigner(
                harness.SignatureProvider, harness.CertificateProvider,
                Substitute.For<IServiceProvider>(), Substitute.For<IMageCli>(),
                harness.ManifestSigner, harness.Logger, new FileMatcher()).CanSign(manifest));
        }

        [Theory]
        [InlineData("missing.dll")]
        [InlineData(@"C:\rooted.dll")]
        [InlineData(@"..\outside.dll")]
        [InlineData(@"CON.dll")]
        [InlineData(@"dir.\payload.dll")]
        [InlineData(@"nested\\payload.dll")]
        public async Task ExecuteAsync_RejectsUnsafeOrMissingReferencesBeforeSigning(string target)
        {
            await using Harness harness = new();
            FileInfo manifest = harness.CreateManifest(
                target.Contains("outside", StringComparison.Ordinal)
                    ? @"app\App.manifest" : "App.manifest", target);
            if (target.Contains("outside", StringComparison.Ordinal))
            {
                harness.CreateFile("outside.dll", "outside");
            }
            string original = File.ReadAllText(manifest.FullName);

            await Assert.ThrowsAnyAsync<Exception>(() => harness.ExecuteAsync(manifest));

            Assert.Equal(original, File.ReadAllText(manifest.FullName));
            Assert.Empty(harness.PayloadCalls);
            Assert.Empty(harness.ManifestCalls);
            AssertNoStagingDirectories(harness);
        }

        [Fact]
        public async Task ExecuteAsync_RejectsMappedUnmappedCollisionBeforeCopying()
        {
            await using Harness harness = new();
            harness.CreateFile("payload.dll.deploy");
            FileInfo manifest = harness.CreateManifest(
                "App.manifest", "payload.dll", "payload.dll.deploy");

            await Assert.ThrowsAsync<ClickOnceStagingException>(() => harness.ExecuteAsync(manifest));

            Assert.Empty(harness.PayloadCalls);
            Assert.Empty(harness.ManifestCalls);
            AssertNoStagingDirectories(harness);
        }

        [Fact]
        public async Task ExecuteAsync_RejectsFileDirectoryConflictBeforeCopying()
        {
            await using Harness harness = new();
            harness.CreateFile("payload.dll");
            FileInfo manifest = harness.CreateManifest("App.manifest", @"App.manifest\payload.dll");
            IApplicationManifest fake = harness.UseFakeManifest("App.manifest", @"App.manifest\payload.dll");
            ClickOncePayloadResolver resolver = new(_ => true);
            harness.Pipeline = harness.CreatePipeline(harness.Reader, resolver);

            await Assert.ThrowsAsync<ClickOnceStagingException>(() => harness.ExecuteAsync(manifest));

            fake.DidNotReceive().ResolveFiles(Arg.Any<IReadOnlyList<DirectoryInfo>>());
            Assert.Empty(harness.PayloadCalls);
        }

        [Theory]
        [InlineData("missing")]
        [InlineData("other")]
        [InlineData("outside")]
        [InlineData("disappeared")]
        public async Task ExecuteAsync_EnforcesResolutionPostconditions(string failure)
        {
            await using Harness harness = new();
            harness.CreateFile("payload.dll.deploy");
            FileInfo manifest = harness.CreateManifest("App.manifest", "payload.dll");
            IApplicationManifest fake = harness.UseFakeManifest("App.manifest", "payload.dll");
            harness.AfterResolve = references =>
            {
                BaseReference reference = Assert.Single(references);
                switch (failure)
                {
                    case "missing":
                        reference.ResolvedPath = null;
                        break;
                    case "other":
                        string other = Path.Combine(Path.GetDirectoryName(reference.SourcePath)!, "other.dll");
                        File.WriteAllText(other, "other");
                        reference.ResolvedPath = other;
                        break;
                    case "outside":
                        reference.ResolvedPath = harness.CreateFile("outside.dll").FullName;
                        break;
                    default:
                        File.Delete(reference.ResolvedPath);
                        break;
                }
            };

            Exception exception = await Assert.ThrowsAnyAsync<Exception>(() => harness.ExecuteAsync(manifest));
            if (failure == "disappeared")
            {
                AggregateException aggregate = Assert.IsType<AggregateException>(exception);
                Assert.Contains(aggregate.Flatten().InnerExceptions,
                    inner => inner is SigningException &&
                        inner.Message.Contains("must resolve", StringComparison.Ordinal));
            }
            else
            {
                Assert.IsType<SigningException>(exception);
            }

            Assert.Empty(harness.PayloadCalls);
            fake.DidNotReceive().UpdateFileInfo();
            Assert.Empty(harness.ManifestCalls);
            AssertNoStagingDirectories(harness);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_RevalidatesAfterSigningAndImmediatelyBeforeUpdate(bool duringIdentityRead)
        {
            await using Harness harness = new();
            harness.CreateFile("payload.dll.deploy");
            FileInfo manifest = harness.CreateManifest("App.manifest", "payload.dll");
            IApplicationManifest fake = harness.UseFakeManifest("App.manifest", "payload.dll");
            if (duringIdentityRead)
            {
                fake.When(value => value.SynchronizeAssemblyIdentities())
                    .Do(_ => File.Delete(Assert.Single(harness.FakeReferences).ResolvedPath));
            }
            else
            {
                harness.BeforePayloadSign = _ =>
                    Assert.Single(harness.FakeReferences).ResolvedPath =
                        harness.CreateFile("wrong.dll").FullName;
            }

            await Assert.ThrowsAnyAsync<Exception>(() => harness.ExecuteAsync(manifest));

            fake.DidNotReceive().UpdateFileInfo();
            Assert.Empty(harness.ManifestCalls);
            AssertNoStagingDirectories(harness);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_LogsOnlyNewWarningsAndContinues(bool duringUpdate)
        {
            await using Harness harness = new();
            harness.CreateFile("payload.dll");
            FileInfo manifest = harness.CreateManifest("App.manifest", "payload.dll");
            harness.UseFakeManifest("App.manifest", "payload.dll");
            harness.Diagnostics.Add(new ClickOnceManifestDiagnostic(
                "old", "earlier error", OutputMessageType.Error));
            Action addWarning = () => harness.Diagnostics.Add(
                new ClickOnceManifestDiagnostic("new", "new warning", OutputMessageType.Warning));
            if (duringUpdate)
            {
                harness.BeforeUpdate = addWarning;
            }
            else
            {
                harness.AfterResolve = _ => addWarning();
            }

            await harness.ExecuteAsync(manifest);

            TestLogEntry entry = Assert.Single(harness.Logger.Entries);
            Assert.Equal(LogLevel.Warning, entry.LogLevel);
            Assert.Equal("new warning", entry.Message);
            Assert.Single(harness.ManifestCalls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_NewErrorsPreventManifestSigning(bool duringUpdate)
        {
            await using Harness harness = new();
            harness.CreateFile("payload.dll");
            FileInfo manifest = harness.CreateManifest("App.manifest", "payload.dll");
            IApplicationManifest fake = harness.UseFakeManifest("App.manifest", "payload.dll");
            Action addError = () => harness.Diagnostics.Add(
                new ClickOnceManifestDiagnostic("new", "new error", OutputMessageType.Error));
            if (duringUpdate)
            {
                harness.BeforeUpdate = addError;
            }
            else
            {
                harness.AfterResolve = _ => addError();
            }

            await Assert.ThrowsAsync<SigningException>(() => harness.ExecuteAsync(manifest));

            Assert.Equal(LogLevel.Error, Assert.Single(harness.Logger.Entries).LogLevel);
            Assert.Empty(harness.ManifestCalls);
            if (!duringUpdate)
            {
                Assert.Empty(harness.PayloadCalls);
                fake.DidNotReceive().UpdateFileInfo();
            }
            AssertNoStagingDirectories(harness);
        }

        [Theory]
        [InlineData("signPayload")]
        [InlineData("update")]
        [InlineData("signManifest")]
        public async Task ExecuteAsync_FailuresDoNotPublishPartialLayoutsAndCleanUp(string phase)
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile("payload.dll.deploy");
            FileInfo manifest = harness.CreateManifest("App.manifest", "payload.dll");
            IApplicationManifest fake = harness.UseFakeManifest("App.manifest", "payload.dll");
            InvalidOperationException expected = new("failure");
            if (phase == "signPayload")
            {
                harness.BeforePayloadSign = _ => throw expected;
            }
            else if (phase == "update")
            {
                harness.BeforeUpdate = () => throw expected;
            }
            else
            {
                harness.BeforeManifestSign = _ => throw expected;
            }
            string original = File.ReadAllText(manifest.FullName);

            Exception actual = await Assert.ThrowsAnyAsync<Exception>(
                () => harness.ExecuteAsync(manifest));

            Assert.Same(expected, actual);
            Assert.Equal("payload", File.ReadAllText(payload.FullName));
            Assert.Equal(original, File.ReadAllText(manifest.FullName));
            AssertNoStagingDirectories(harness);
            if (phase == "signPayload")
            {
                fake.DidNotReceive().UpdateFileInfo();
                Assert.Empty(harness.ManifestCalls);
            }
        }

        [Fact]
        public async Task ExecuteAsync_OrdersPayloadUpdateWriteAndManifestSigning()
        {
            await using Harness harness = new();
            harness.CreateFile("payload.dll");
            FileInfo manifest = harness.CreateManifest("App.manifest", "payload.dll");
            IApplicationManifest fake = harness.UseFakeManifest("App.manifest", "payload.dll");
            List<string> order = new();
            harness.AfterResolve = _ => order.Add("resolve");
            harness.BeforePayloadSign = _ => order.Add("payload");
            fake.When(value => value.SynchronizeAssemblyIdentities()).Do(_ => order.Add("identity"));
            harness.BeforeUpdate = () => order.Add("update");
            fake.When(value => value.Write(Arg.Any<FileInfo>())).Do(_ => order.Add("write"));
            harness.BeforeManifestSign = _ => order.Add("manifest");

            await harness.ExecuteAsync(manifest);

            Assert.Equal(new[] { "resolve", "payload", "identity", "update", "write", "manifest" }, order);
            fake.Received().ReadOnly = false;
        }

        [Fact]
        public async Task ExecuteAsync_CoordinatesDuplicateReferencesAndExplicitPayload()
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile("payload.dll.deploy");
            FileInfo manifest = harness.CreateManifest("App.manifest", "payload.dll", "payload.dll");
            harness.FormatSigner.CanSign(Arg.Any<FileInfo>()).Returns(true);
            await harness.AggregatingSigner.SignAsync(
                new[] { SigningFile.Capture(payload) }, harness.Options(), harness.Coordinator);
            await harness.ExecuteAsync(manifest);

            Assert.Single(harness.PayloadCalls);
            Assert.Equal("payload signed", File.ReadAllText(payload.FullName));
            Assert.Single(harness.ManifestCalls);
        }

        [Fact]
        public async Task ExecuteAsync_MultipleManifestsSharePayloadAndApplicationResults()
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile("payload.dll");
            FileInfo first = harness.CreateManifest("First.manifest", "payload.dll");
            FileInfo second = harness.CreateManifest("Second.manifest", "payload.dll");
            await Task.WhenAll(
                harness.ExecuteAsync(first),
                harness.ExecuteAsync(second));
            FileInfo copied = new(Path.Combine(harness.Source.FullName, "copy", "First.manifest"));
            await harness.ExecuteAsync(first, copied);

            Assert.Single(harness.PayloadCalls);
            Assert.Equal(2, harness.ManifestCalls.Count);
            Assert.Equal(File.ReadAllText(first.FullName), File.ReadAllText(copied.FullName));
            Assert.Equal(File.ReadAllText(payload.FullName), File.ReadAllText(
                Path.Combine(copied.DirectoryName!, "payload.dll")));
        }

        [Fact]
        public async Task ExecuteAsync_ContainerIdentitySurvivesStagingAndMapping()
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile("payload.dll.deploy");
            FileInfo manifest = harness.CreateManifest("App.manifest", "payload.dll");
            SigningSourceIdentity container = SigningSourceIdentity.Capture(
                new FileInfo(Path.Combine(harness.Source.FullName, "outer.zip")));
            SigningSourceIdentity inner = SigningSourceIdentity.ContainerEntry(container, "inner.zip");
            SigningFile input = SigningFile.ContainerEntry(manifest, inner, "app/App.manifest");
            SigningSourceIdentity expected = SigningSourceIdentity.ContainerEntry(
                inner, "app/payload.dll.deploy");
            FileInfo otherStagedCopy = harness.CreateFile("explicit.dll", "explicit signed");
            await harness.Coordinator.ExecuteAsync(expected, () => Task.FromResult(otherStagedCopy));
            SigningOperationPlan plan = new(input, manifest);

            await harness.Pipeline.ExecuteAsync(
                plan, harness.Options(), harness.Coordinator,
                ClickOnceApplicationSigningPolicy.SignPayloads);

            Assert.Empty(harness.PayloadCalls);
            Assert.Equal("explicit signed", File.ReadAllText(payload.FullName));
        }

        [Fact]
        public async Task ExecuteAsync_ReadOnlyStagedCopiesAreWritableWithoutChangingExcludedSource()
        {
            await using Harness harness = new();
            FileInfo payload = harness.CreateFile("data.txt.deploy");
            FileInfo manifest = harness.CreateManifest("App.manifest", "data.txt");
            payload.IsReadOnly = true;
            try
            {
                await harness.ExecuteAsync(manifest);
                payload.Refresh();
                Assert.True(payload.IsReadOnly);
                Assert.Equal("payload", File.ReadAllText(payload.FullName));
                Assert.Single(harness.ManifestCalls);
            }
            finally
            {
                payload.IsReadOnly = false;
            }
        }

        [Fact]
        public async Task ExecuteAsync_RealManifestSignerProducesSignatureAfterRefresh()
        {
            await using Harness harness = new();
            harness.ManifestSigner = new ManifestSigner();
            harness.Pipeline = harness.CreatePipeline(new ClickOnceManifestReader());
            harness.CreateFile("data.txt");
            FileInfo manifest = harness.CreateManifest("App.manifest", "data.txt");

            await harness.ExecuteAsync(manifest);

            XDocument document = XDocument.Load(manifest.FullName);
            Assert.Single(document.Root!.Elements(
                XName.Get("Signature", "http://www.w3.org/2000/09/xmldsig#")));
        }

        private static void AssertNoStagingDirectories(Harness harness)
        {
            foreach (DirectoryInfo directory in harness.Directories.Directories.Skip(2))
            {
                Assert.False(Directory.Exists(directory.FullName));
            }
        }

        [Fact]
        public void SelectedArtifact_RejectsFilesOutsideItsRootBeforeSnapshotting()
        {
            using DirectoryServiceStub directories = new();
            DirectoryInfo root = directories.CreateTemporaryDirectory();
            DirectoryInfo outside = directories.CreateTemporaryDirectory();
            FileInfo primary = ClickOnceResolutionTestUtilities.CreateFile(root, "App.manifest");
            FileInfo payload = ClickOnceResolutionTestUtilities.CreateFile(outside, "payload.dll");
            IDisposable owner = Substitute.For<IDisposable>();

            Assert.Throws<ArgumentException>(() => SigningOperationArtifact.OwnedLayout(
                primary, root, new[] { primary, payload }, owner));

            owner.DidNotReceive().Dispose();
        }

        [Fact]
        public void SelectedArtifact_RequiresPrimaryAndDeduplicatesIdenticalFiles()
        {
            using DirectoryServiceStub directories = new();
            DirectoryInfo root = directories.CreateTemporaryDirectory();
            FileInfo primary = ClickOnceResolutionTestUtilities.CreateFile(root, "App.manifest");
            FileInfo payload = ClickOnceResolutionTestUtilities.CreateFile(root, "payload.dll");
            IDisposable owner = Substitute.For<IDisposable>();

            Assert.Throws<ArgumentException>(() => SigningOperationArtifact.OwnedLayout(
                primary, root, new[] { payload }, owner));
            using SigningOperationArtifact artifact = SigningOperationArtifact.OwnedLayout(
                primary, root, new[] { primary, primary, payload }, owner);

            Assert.Equal(2, artifact.GetFiles().Count);
        }

        private sealed class Harness : IAsyncDisposable
        {
            internal DirectoryServiceStub Directories { get; } = new();
            internal DirectoryInfo Source { get; }
            internal SigningOperationCoordinator Coordinator { get; }
            internal IDataFormatSigner FormatSigner { get; } = Substitute.For<IDataFormatSigner>();
            internal IAggregatingDataFormatSigner AggregatingSigner { get; }
            internal ICertificateProvider CertificateProvider { get; } = Substitute.For<ICertificateProvider>();
            internal ISignatureAlgorithmProvider SignatureProvider { get; } = Substitute.For<ISignatureAlgorithmProvider>();
            internal TestLogger<IDataFormatSigner> Logger { get; } = new();
            internal IManifestSigner ManifestSigner { get; set; } = Substitute.For<IManifestSigner>();
            internal IClickOnceManifestReader Reader { get; private set; } = new ClickOnceManifestReader();
            internal ClickOnceApplicationManifestSigner Pipeline { get; set; }
            internal ConcurrentQueue<FileInfo> PayloadCalls { get; } = new();
            internal ConcurrentQueue<FileInfo> ManifestCalls { get; } = new();
            internal List<ClickOnceManifestDiagnostic> Diagnostics { get; } = new();
            internal List<BaseReference> FakeReferences { get; } = new();
            internal Action<IReadOnlyList<BaseReference>>? AfterResolve { get; set; }
            internal Action<IReadOnlyList<FileInfo>>? BeforePayloadSign { get; set; }
            internal Action? BeforeUpdate { get; set; }
            internal Action<FileInfo>? BeforeManifestSign { get; set; }
            private readonly ConcurrentQueue<RSA> _keys = new();

            internal Harness()
            {
                Source = Directories.CreateTemporaryDirectory();
                Coordinator = new SigningOperationCoordinator(Directories);
                FormatSigner.CanSign(Arg.Any<FileInfo>()).Returns(
                    call => ((FileInfo)call[0]).Extension is ".dll" or ".exe");
                FormatSigner.SignAsync(Arg.Any<IEnumerable<FileInfo>>(), Arg.Any<SignOptions>())
                    .Returns(call =>
                    {
                        FileInfo[] files = ((IEnumerable<FileInfo>)call[0]).ToArray();
                        BeforePayloadSign?.Invoke(files);
                        foreach (FileInfo file in files)
                        {
                            PayloadCalls.Enqueue(file);
                            File.AppendAllText(file.FullName, " signed");
                        }
                        return Task.CompletedTask;
                    });
                AggregatingSigner = new AggregatingSigner(
                    new[] { FormatSigner },
                    Substitute.For<IDefaultDataFormatSigner>(),
                    Substitute.For<IContainerProvider>(),
                    Substitute.For<IFileMetadataService>(),
                    new MatcherFactory());
                CertificateProvider.GetCertificateAsync().Returns(_ =>
                {
                    X509Certificate2 certificate = SelfIssuedCertificateCreator.CreateCertificate();
                    _keys.Enqueue(certificate.GetRSAPrivateKey()!);
                    return Task.FromResult(certificate);
                });
                SignatureProvider.GetRsaAsync().Returns(_ =>
                {
                    Assert.True(_keys.TryDequeue(out RSA? key));
                    return Task.FromResult(key!);
                });
                ManifestSigner.When(signer => signer.Sign(
                    Arg.Any<FileInfo>(), Arg.Any<X509Certificate2>(), Arg.Any<RSA>(), Arg.Any<SignOptions>()))
                    .Do(call =>
                    {
                        FileInfo file = (FileInfo)call[0];
                        BeforeManifestSign?.Invoke(file);
                        ManifestCalls.Enqueue(file);
                        File.AppendAllText(file.FullName, "\n<!--signed-->");
                    });
                Pipeline = CreatePipeline(Reader);
            }

            internal ClickOnceApplicationManifestSigner CreatePipeline(
                IClickOnceManifestReader reader,
                ClickOncePayloadResolver? resolver = null)
            {
                return new ClickOnceApplicationManifestSigner(
                    new ClickOnceApplicationPublishLayoutResolver(
                        reader, resolver ?? new ClickOncePayloadResolver()),
                    new ClickOncePublishLayoutStager(Directories),
                    AggregatingSigner, new FileMatcher(), ManifestSigner,
                    CertificateProvider, SignatureProvider, Logger);
            }

            internal IApplicationManifest UseFakeManifest(string name, params string[] targets)
            {
                ApplicationManifest model = ClickOnceResolutionTestUtilities.CreateApplicationManifest();
                foreach (string target in targets)
                {
                    FileReference reference = new() { TargetPath = target };
                    model.FileReferences.Add(reference);
                    FakeReferences.Add(reference);
                }
                IApplicationManifest fake = Substitute.For<IApplicationManifest>();
                fake.AssemblyReferences.Returns(model.AssemblyReferences);
                fake.FileReferences.Returns(model.FileReferences);
                fake.Diagnostics.Returns(_ => Diagnostics.ToArray());
                fake.When(value => value.ResolveFiles(Arg.Any<IReadOnlyList<DirectoryInfo>>()))
                    .Do(_ =>
                    {
                        foreach (BaseReference reference in FakeReferences)
                        {
                            reference.ResolvedPath = reference.SourcePath;
                        }
                        AfterResolve?.Invoke(FakeReferences);
                    });
                fake.When(value => value.UpdateFileInfo()).Do(_ => BeforeUpdate?.Invoke());
                Reader = Substitute.For<IClickOnceManifestReader>();
                Reader.TryReadApplicationManifest(Arg.Any<Stream>(), out Arg.Any<IApplicationManifest?>())
                    .Returns(call =>
                    {
                        call[1] = fake;
                        return true;
                    });
                Pipeline = CreatePipeline(Reader);
                return fake;
            }

            internal Task ExecuteAsync(
                FileInfo manifest,
                FileInfo? output = null,
                SignOptions? options = null,
                ClickOnceApplicationSigningPolicy policy = ClickOnceApplicationSigningPolicy.SignPayloads)
            {
                return Pipeline.ExecuteAsync(
                    new SigningOperationPlan(SigningFile.Capture(manifest), output ?? manifest),
                    options ?? Options(), Coordinator, policy);
            }

            internal SignOptions Options(Matcher? matcher = null, Matcher? antiMatcher = null)
            {
                return new SignOptions(null, null, null, null,
                    HashAlgorithmName.SHA256, HashAlgorithmName.SHA256,
                    null!, matcher, antiMatcher, recurseContainers: true);
            }

            internal FileInfo CreateFile(string path, string contents = "payload") =>
                ClickOnceResolutionTestUtilities.CreateFile(Source, path, contents);

            internal FileInfo CreateManifest(string name, params string[] targets) =>
                ClickOnceResolutionTestUtilities.WriteApplicationManifest(Source, name, targets);

            internal FileInfo WriteManifest(string name, Manifest manifest) =>
                ClickOnceResolutionTestUtilities.WriteManifest(Source, name, manifest);

            internal FileInfo CopyAssembly(string name)
            {
                FileInfo file = new(Path.Combine(Source.FullName, name));
                File.Copy(typeof(Harness).Assembly.Location, file.FullName);
                return file;
            }

            public async ValueTask DisposeAsync()
            {
                await Coordinator.DisposeAsync();
                while (_keys.TryDequeue(out RSA? key))
                {
                    key.Dispose();
                }
                Directories.Dispose();
            }
        }
    }
}
