// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using NSubstitute;

namespace Sign.Core.Test
{
    public sealed partial class ClickOnceDeploymentManifestSignerTests
    {
        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public async Task ExecuteAsync_WhenPreflightInspectsRepeatedGraphSources_EnumeratesEachDirectoryPatternOncePerInvocation(bool include, bool exclude)
        {
            IFileMatcher matcher = Substitute.For<IFileMatcher>();
            FileMatcher realMatcher = new();
            List<(string Directory, Matcher Pattern)> traversals = new();
            matcher.EnumerateMatches(Arg.Any<DirectoryInfoBase>(), Arg.Any<Matcher>()).Returns(call =>
            {
                DirectoryInfoBase directory = call.Arg<DirectoryInfoBase>();
                Matcher pattern = call.Arg<Matcher>();
                traversals.Add((directory.FullName, pattern));
                return realMatcher.EnumerateMatches(directory, pattern);
            });
            await using Harness harness = new(fileMatcher: matcher);
            FileInfo payload = harness.File("payload.dll", "payload");
            harness.File("data.txt", "data");
            harness.File(@"nested\child.dll", "child");
            FileInfo childApp = harness.Application(@"nested\Child.manifest", "child.dll");
            FileInfo child = harness.Deployment(@"nested\Child.application", childApp.Name);
            FileInfo app = harness.Application("App.manifest", payload.Name, "data.txt", @"nested\Child.application");
            FileInfo root = harness.Deployment("Root.application", app.Name);
            Matcher inclusion = new();
            inclusion.AddInclude("**/*");
            Matcher exclusion = new();
            exclusion.AddInclude("**/excluded.*");
            harness.Options = SelectionOptions(harness, include ? inclusion : null, exclude ? exclusion : null);
            payload.IsReadOnly = true;
            try
            {
                SigningOperationPlan[] plans =
                {
                    new(SigningFile.Capture(root), harness.Output(root.Name)),
                    new(SigningFile.Capture(payload), harness.Output(payload.Name))
                };

                await Assert.ThrowsAsync<SigningException>(() =>
                    harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, 1));

                Assert.Empty(harness.Calls);
                int patternCount = (include ? 1 : 0) + (exclude ? 1 : 0);
                Assert.Equal(2 * patternCount, traversals.Count);
                foreach (string directory in new[] { harness.Source.FullName, child.DirectoryName! })
                {
                    if (include)
                    {
                        Assert.Single(traversals, call => call.Directory == directory &&
                            ReferenceEquals(call.Pattern, inclusion));
                    }
                    if (exclude)
                    {
                        Assert.Single(traversals, call => call.Directory == directory &&
                            ReferenceEquals(call.Pattern, exclusion));
                    }
                }
                traversals.Clear();
                await Assert.ThrowsAsync<SigningException>(() =>
                    harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, 1));
                Assert.Equal(2 * patternCount, traversals.Count);
            }
            finally
            {
                payload.IsReadOnly = false;
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecuteAsync_WhenMatcherResultsChangeAfterPreflight_ReevaluatesAdjacentSelectionDuringExecution(bool exclusion)
        {
            IFileMatcher matcher = Substitute.For<IFileMatcher>();
            FileMatcher realMatcher = new();
            bool execution = false;
            int liveTraversals = 0;
            matcher.EnumerateMatches(Arg.Any<DirectoryInfoBase>(), Arg.Any<Matcher>()).Returns(call =>
            {
                DirectoryInfoBase directory = call.Arg<DirectoryInfoBase>();
                Matcher pattern = call.Arg<Matcher>();
                FileInfo[] matches = realMatcher.EnumerateMatches(directory, pattern).ToArray();
                if (execution)
                {
                    liveTraversals++;
                }
                return matches.Where(file => exclusion
                    ? !execution && file.Name == "setup.exe"
                    : execution || file.Name != "setup.exe").ToArray();
            });
            await using Harness harness = new(fileMatcher: matcher);
            harness.File("payload.dll", "payload");
            harness.File("setup.exe", "setup");
            FileInfo application = harness.Application("App.manifest", "payload.dll");
            FileInfo deployment = harness.Deployment("Root.application", application.Name);
            Matcher pattern = new();
            pattern.AddInclude("**/*");
            harness.Options = SelectionOptions(harness, exclusion ? null : pattern, exclusion ? pattern : null);
            harness.BeforeSign = _ => execution = true;

            await harness.ExecuteAsync(deployment, harness.Output(deployment.Name));

            Assert.Contains("setup.exe", harness.Calls);
            Assert.True(liveTraversals > 0);
            Assert.Equal("setup signed", System.IO.File.ReadAllText(harness.Output("setup.exe").FullName));
        }

        private static SignOptions SelectionOptions(Harness harness, Matcher? matcher, Matcher? antiMatcher) =>
            new(null, null, null, null, harness.Options.FileHashAlgorithm,
                harness.Options.TimestampHashAlgorithm, harness.Options.TimestampService,
                matcher, antiMatcher, true);
    }
}
