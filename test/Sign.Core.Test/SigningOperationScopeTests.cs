// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

namespace Sign.Core.Test
{
    public class SigningOperationScopeTests
    {
        private static readonly Func<FileInfo, bool>
            RequiresSequentialCoordination =
                file => string.Equals(
                    file.Extension,
                    ".application",
                    StringComparison.OrdinalIgnoreCase);

        [Fact]
        public void Create_ContainerEntryPlan_Throws()
        {
            FileInfo file = new(@"C:\temp\App.exe");
            SigningOperationPlan plan = new(
                SigningFile.ContainerEntry(
                    file,
                    SigningSourceIdentity.PhysicalFile(
                        path: @"C:\container.zip"),
                    entryPath: "App.exe"),
                file);

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => SigningOperationScope.Create(
                    plan,
                    RequiresSequentialCoordination));

            Assert.Equal("plans", exception.ParamName);
        }

        [Fact]
        public void Create_GroupWithContainerEntryPlan_Throws()
        {
            SigningOperationPlan physical =
                CreatePlan(@"C:\temp\App.application");
            FileInfo file = new(@"C:\temp\App.exe");
            SigningOperationPlan entry = new(
                SigningFile.ContainerEntry(
                    file,
                    SigningSourceIdentity.PhysicalFile(
                        path: @"C:\container.zip"),
                    entryPath: "App.exe"),
                file);

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => SigningOperationScope.Create(
                    new[] { physical, entry },
                    RequiresSequentialCoordination));

            Assert.Equal("plans", exception.ParamName);
        }

        [Fact]
        public void Contains_FileScope_ContainsOnlyFileAndItsEntries()
        {
            SigningOperationScope scope = SigningOperationScope.Create(
                CreatePlan(@"C:\publish\container.zip"),
                RequiresSequentialCoordination);
            SigningSourceIdentity container =
                SigningSourceIdentity.PhysicalFile(
                    path: @"C:\PUBLISH\Container.ZIP");
            SigningSourceIdentity nestedEntry =
                SigningSourceIdentity.ContainerEntry(
                    parent: SigningSourceIdentity.ContainerEntry(
                        parent: container,
                        entryPath: "nested.zip"),
                    entryPath: "App.exe");

            Assert.True(scope.Contains(container));
            Assert.True(scope.Contains(nestedEntry));
            Assert.False(
                scope.Contains(
                    SigningSourceIdentity.PhysicalFile(
                        path: @"C:\publish\App.exe")));
            Assert.False(
                scope.Contains(
                    SigningSourceIdentity.ContainerEntry(
                        parent: SigningSourceIdentity.PhysicalFile(
                            path: @"C:\publish\other.zip"),
                        entryPath: "App.exe")));
        }

        [Fact]
        public void Contains_DirectoryTreeScope_ContainsFilesInDirectoryTree()
        {
            SigningOperationScope scope = SigningOperationScope.Create(
                CreatePlan(@"C:\publish\App.application"),
                RequiresSequentialCoordination);

            Assert.True(
                scope.Contains(
                    SigningSourceIdentity.PhysicalFile(
                        path: @"C:\publish\App.application")));
            Assert.True(
                scope.Contains(
                    SigningSourceIdentity.PhysicalFile(
                        path:
                            @"C:\publish\Application Files\App.exe")));
            Assert.True(
                scope.Contains(
                    SigningSourceIdentity.ContainerEntry(
                        parent: SigningSourceIdentity.PhysicalFile(
                            path: @"C:\publish\container.zip"),
                        entryPath: "App.exe")));
            Assert.False(
                scope.Contains(
                    SigningSourceIdentity.PhysicalFile(
                        path: @"C:\publish2\App.exe")));
            Assert.False(
                scope.Contains(
                    SigningSourceIdentity.PhysicalFile(
                        path: @"C:\App.exe")));
        }

        [Fact]
        public void Contains_GroupScope_ContainsUnionOfPlanScopes()
        {
            SigningOperationScope scope = SigningOperationScope.Create(
                new[]
                {
                    CreatePlan(@"C:\first\App.exe"),
                    CreatePlan(@"D:\publish\App.application")
                },
                RequiresSequentialCoordination);

            Assert.True(
                scope.Contains(
                    SigningSourceIdentity.PhysicalFile(
                        path: @"C:\FIRST\app.EXE")));
            Assert.True(
                scope.Contains(
                    SigningSourceIdentity.ContainerEntry(
                        parent: SigningSourceIdentity.PhysicalFile(
                            path: @"C:\first\App.exe"),
                        entryPath: "nested.dll")));
            Assert.True(
                scope.Contains(
                    SigningSourceIdentity.PhysicalFile(
                        path: @"D:\publish\sub\App.dll")));
            Assert.False(
                scope.Contains(
                    SigningSourceIdentity.PhysicalFile(
                        path: @"C:\first\Other.exe")));
            Assert.False(
                scope.Contains(
                    SigningSourceIdentity.PhysicalFile(
                        path: @"D:\publish2\App.dll")));
        }

        [Fact]
        public void Contains_Null_Throws()
        {
            SigningOperationScope scope = SigningOperationScope.Create(
                CreatePlan(@"C:\publish\App.exe"),
                RequiresSequentialCoordination);

            Assert.Throws<ArgumentNullException>(
                () => scope.Contains(null!));
        }

        [Fact]
        public void CreateGroups_DistinctFiles_AreSeparateGroups()
        {
            SigningOperationPlan first =
                CreatePlan(@"C:\publish\App.exe");
            SigningOperationPlan second =
                CreatePlan(@"C:\publish\App.dll");
            SigningOperationPlan container =
                CreatePlan(@"C:\publish\container.zip");

            AssertGroups(
                new[] { first, second, container },
                new[] { first },
                new[] { second },
                new[] { container });
        }

        [Fact]
        public void CreateGroups_ManyDistinctFiles_PerformsNoDirectoryChecks()
        {
            const int PlanCount = 10_000;
            List<SigningOperationPlan> plans = Enumerable.Range(
                    start: 0,
                    count: PlanCount)
                .Select(
                    index => CreatePlan(
                        $@"C:\publish\App{index}.exe"))
                .ToList();

            IReadOnlyList<SigningOperationGroup> groups =
                SigningOperationScope.CreateGroups(
                    plans,
                    RequiresSequentialCoordination,
                    out int directoryOperationCount);

            Assert.Equal(PlanCount, groups.Count);
            Assert.Equal(0, directoryOperationCount);
        }

        [Fact]
        public void CreateGroups_DirectoryScope_ChecksOnlyMatchingPathRange()
        {
            const int UnrelatedPlanCount = 10_000;
            List<SigningOperationPlan> plans = Enumerable.Range(
                    start: 0,
                    count: UnrelatedPlanCount)
                .Select(
                    index => CreatePlan(
                        $@"C:\other\App{index}.exe"))
                .ToList();
            plans.Add(CreatePlan(@"C:\publish\App.application"));
            plans.Add(CreatePlan(@"C:\zzz\App.exe"));

            IReadOnlyList<SigningOperationGroup> groups =
                SigningOperationScope.CreateGroups(
                    plans,
                    RequiresSequentialCoordination,
                    out int directoryOperationCount);

            Assert.Equal(UnrelatedPlanCount + 2, groups.Count);
            Assert.InRange(
                directoryOperationCount,
                low: 1,
                high: 64);
        }

        [Fact]
        public void CreateGroups_ManySameDirectoryScopes_PerformsLinearWork()
        {
            const int PlanCount = 10_000;
            List<SigningOperationPlan> plans = Enumerable.Range(
                    start: 0,
                    count: PlanCount)
                .Select(
                    index => CreatePlan(
                        $@"C:\publish\App{index}.application"))
                .ToList();

            IReadOnlyList<SigningOperationGroup> groups =
                SigningOperationScope.CreateGroups(
                    plans,
                    RequiresSequentialCoordination,
                    out int directoryOperationCount);

            SigningOperationGroup group = Assert.Single(groups);
            Assert.Equal(plans, group.Plans);
            Assert.InRange(
                directoryOperationCount,
                low: PlanCount,
                high: PlanCount + 64);
        }

        [Fact]
        public void CreateGroups_ManyNestedDirectoryScopes_PerformsNearLinearWork()
        {
            const int PlanCount = 1_000;
            List<SigningOperationPlan> plans = new(PlanCount);
            string directory = @"C:\publish";

            for (int index = 0; index < PlanCount; ++index)
            {
                plans.Add(
                    CreatePlan(
                        Path.Combine(
                            directory,
                            $"App{index}.application")));
                directory = Path.Combine(directory, $"d{index}");
            }

            IReadOnlyList<SigningOperationGroup> groups =
                SigningOperationScope.CreateGroups(
                    plans,
                    RequiresSequentialCoordination,
                    out int directoryOperationCount);

            SigningOperationGroup group = Assert.Single(groups);
            Assert.Equal(plans, group.Plans);
            Assert.InRange(
                directoryOperationCount,
                low: PlanCount,
                high: PlanCount * 32);
        }

        [Fact]
        public void CreateGroups_EquivalentIdentities_ShareGroup()
        {
            SigningOperationPlan first =
                CreatePlan(@"C:\publish\App.exe");
            SigningOperationPlan second =
                CreatePlan(@"C:\PUBLISH\sub\..\app.EXE");
            SigningOperationPlan other =
                CreatePlan(@"C:\publish\App.dll");

            AssertGroups(
                new[] { first, second, other },
                new[] { first, second },
                new[] { other });
        }

        [Fact]
        public void CreateGroups_IndependentDuplicatePairs_AreSeparateGroups()
        {
            SigningOperationPlan first =
                CreatePlan(@"C:\first\App.exe");
            SigningOperationPlan firstDuplicate =
                CreatePlan(@"C:\FIRST\app.EXE");
            SigningOperationPlan second =
                CreatePlan(@"D:\second\App.exe");
            SigningOperationPlan secondDuplicate =
                CreatePlan(@"D:\SECOND\app.EXE");

            AssertGroups(
                new[]
                {
                    first,
                    firstDuplicate,
                    second,
                    secondDuplicate
                },
                new[] { first, firstDuplicate },
                new[] { second, secondDuplicate });
        }

        [Theory]
        [InlineData(@"C:\publish\App.exe")]
        [InlineData(
            @"C:\publish\Application Files\App_1_0\App.exe")]
        [InlineData(@"C:\publish\container.zip")]
        [InlineData(@"C:\PUBLISH\APP.EXE")]
        public void CreateGroups_FileInClickOnceDirectoryTree_SharesGroup(
            string path)
        {
            SigningOperationPlan application =
                CreatePlan(@"C:\publish\App.application");
            SigningOperationPlan file = CreatePlan(path);
            SigningOperationPlan other =
                CreatePlan(@"C:\other\App.exe");

            AssertGroups(
                new[] { application, file, other },
                new[] { application, file },
                new[] { other });
            AssertGroups(
                new[] { file, application, other },
                new[] { file, application },
                new[] { other });
        }

        [Theory]
        [InlineData(@"C:\publish2\App.exe")]
        [InlineData(@"C:\pub\App.exe")]
        [InlineData(@"C:\App.exe")]
        [InlineData(@"D:\publish\App.exe")]
        public void CreateGroups_FileOutsideClickOnceDirectoryTree_IsSeparate(
            string path)
        {
            SigningOperationPlan application =
                CreatePlan(@"C:\publish\App.application");
            SigningOperationPlan file = CreatePlan(path);

            AssertGroups(
                new[] { application, file },
                new[] { application },
                new[] { file });
        }

        [Fact]
        public void CreateGroups_NestedClickOnceInputs_ShareGroup()
        {
            SigningOperationPlan outer =
                CreatePlan(@"C:\publish\App.application");
            SigningOperationPlan inner =
                CreatePlan(
                    @"C:\publish\nested\Nested.application");
            SigningOperationPlan other =
                CreatePlan(@"C:\other\App.exe");

            AssertGroups(
                new[] { outer, inner, other },
                new[] { outer, inner },
                new[] { other });
            AssertGroups(
                new[] { inner, outer, other },
                new[] { inner, outer },
                new[] { other });
        }

        [Fact]
        public void CreateGroups_ClickOnceInputsInSameDirectory_ShareGroup()
        {
            SigningOperationPlan first =
                CreatePlan(@"C:\publish\First.application");
            SigningOperationPlan second =
                CreatePlan(@"C:\publish\Second.application");

            AssertGroups(
                new[] { first, second },
                new[] { first, second });
        }

        [Fact]
        public void CreateGroups_SiblingClickOnceInputs_AreSeparateGroups()
        {
            SigningOperationPlan first =
                CreatePlan(
                    @"C:\publish\first\App.application");
            SigningOperationPlan second =
                CreatePlan(
                    @"C:\publish\second\App.application");

            AssertGroups(
                new[] { first, second },
                new[] { first },
                new[] { second });
        }

        [Fact]
        public void CreateGroups_IndependentClickOnceGroups_AreSeparate()
        {
            SigningOperationPlan firstApplication =
                CreatePlan(
                    @"C:\first\App.application");
            SigningOperationPlan firstPayload =
                CreatePlan(@"C:\first\App.exe");
            SigningOperationPlan secondApplication =
                CreatePlan(
                    @"D:\second\App.application");
            SigningOperationPlan secondPayload =
                CreatePlan(@"D:\second\App.exe");

            AssertGroups(
                new[]
                {
                    firstApplication,
                    firstPayload,
                    secondApplication,
                    secondPayload
                },
                new[] { firstApplication, firstPayload },
                new[] { secondApplication, secondPayload });
        }

        [Fact]
        public void CreateGroups_LoneClickOnceInput_IsSinglePlanGroup()
        {
            SigningOperationPlan application =
                CreatePlan(@"C:\publish\App.application");

            AssertGroups(
                new[] { application },
                new[] { application });
        }

        [Fact]
        public void CreateGroups_FileInClickOnceParentDirectory_IsSeparate()
        {
            SigningOperationPlan application =
                CreatePlan(
                    @"C:\publish\app\App.application");
            SigningOperationPlan file =
                CreatePlan(@"C:\publish\App.exe");

            AssertGroups(
                new[] { application, file },
                new[] { application },
                new[] { file });
        }

        [Fact]
        public void CreateGroups_ClickOnceAtDriveRoot_SharesDriveFiles()
        {
            SigningOperationPlan application =
                CreatePlan(@"C:\App.application");
            SigningOperationPlan file =
                CreatePlan(@"C:\publish\App.exe");
            SigningOperationPlan otherDrive =
                CreatePlan(@"D:\App.exe");

            AssertGroups(
                new[] { application, file, otherDrive },
                new[] { application, file },
                new[] { otherDrive });
        }

        [Fact]
        public void CreateGroups_ClickOnceAtUncShareRoot_SharesShareFiles()
        {
            SigningOperationPlan application =
                CreatePlan(@"\\server\share\App.application");
            SigningOperationPlan file =
                CreatePlan(
                    @"\\server\share\publish\App.exe");
            SigningOperationPlan otherShare =
                CreatePlan(@"\\server\share2\App.exe");

            AssertGroups(
                new[] { application, file, otherShare },
                new[] { application, file },
                new[] { otherShare });
        }

        [Fact]
        public void CreateGroups_AllPlansInClickOnceDirectoryTree_AreIncluded()
        {
            SigningOperationPlan application =
                CreatePlan(@"C:\publish\App.application");
            SigningOperationPlan text =
                CreatePlan(@"C:\publish\readme.txt");
            SigningOperationPlan duplicateText =
                CreatePlan(@"C:\publish\README.TXT");

            AssertGroups(
                new[] { application, text, duplicateText },
                new[] { application, text, duplicateText });
        }

        [Fact]
        public void CreateGroups_NoPlans_ReturnsEmpty()
        {
            AssertGroups(Array.Empty<SigningOperationPlan>());
        }

        [Fact]
        public void CreateGroups_NullArguments_Throw()
        {
            SigningOperationPlan[] plans = [];

            Assert.Throws<ArgumentNullException>(
                () => SigningOperationScope.CreateGroups(
                    null!,
                    RequiresSequentialCoordination));
            Assert.Throws<ArgumentNullException>(
                () => SigningOperationScope.CreateGroups(
                    plans,
                    null!));
        }

        private static void AssertGroups(
            IReadOnlyList<SigningOperationPlan> plans,
            params IReadOnlyList<SigningOperationPlan>[] expected)
        {
            IReadOnlyList<SigningOperationGroup> actual =
                SigningOperationScope.CreateGroups(
                    plans,
                    RequiresSequentialCoordination);

            Assert.Equal(expected.Length, actual.Count);

            for (int index = 0; index < expected.Length; ++index)
            {
                Assert.Equal(expected[index], actual[index].Plans);
                Assert.All(
                    actual[index].Plans,
                    plan => Assert.True(
                        actual[index].Scope.Contains(
                            plan.Source.SourceIdentity)));
            }
        }

        private static SigningOperationPlan CreatePlan(string path)
        {
            FileInfo file = new(path);

            return new SigningOperationPlan(
                SigningFile.Capture(file),
                file);
        }
    }
}
