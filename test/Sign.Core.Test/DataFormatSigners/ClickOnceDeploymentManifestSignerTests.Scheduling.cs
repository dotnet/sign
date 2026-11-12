// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Sign.Core.Test
{
    public partial class ClickOnceDeploymentManifestSignerTests
    {
        [Theory]
        [InlineData(1, false)]
        [InlineData(1, true)]
        [InlineData(4, false)]
        [InlineData(4, true)]
        public async Task ExecuteAsync_WhenMultipleGraphsRunUnderScheduling_BoundsDiscoveryStagingSigningAndCleanupCapacity(
            int maxConcurrency, bool reverse)
        {
            ObservingManifestReader reader = new();
            await using Harness harness = new(reader: reader);
            List<SigningOperationPlan> plans = new();
            for (int index = 0; index < 8; ++index)
            {
                string prefix = $"pair{index}";
                FileInfo payload = harness.File(Path.Combine(prefix, "payload.dll"), "payload");
                FileInfo inner = harness.Application(Path.Combine(prefix, "Inner.manifest"), payload.Name);
                FileInfo child = harness.Deployment(Path.Combine(prefix, "Child.application"), inner.Name);
                FileInfo application = harness.Application(Path.Combine(prefix, "App.manifest"), child.Name);
                FileInfo deployment = harness.Deployment(Path.Combine(prefix, "Root.application"), application.Name);
                foreach (FileInfo file in new[] { payload, inner, child, application, deployment })
                {
                    plans.Add(new(SigningFile.Capture(file),
                        harness.Output(Path.GetRelativePath(harness.Source.FullName, file.FullName))));
                }
            }
            if (reverse)
            {
                plans.Reverse();
            }
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            object schedulerLocal = typeof(SigningOperationCoordinator).GetField("_scheduler", flags)!
                .GetValue(harness.Coordinator)!;
            Type operationType = typeof(SigningOperationCoordinator).GetNestedType(
                "OperationState", BindingFlags.NonPublic)!;
            ConcurrentDictionary<string, int> observed = new();
            int active = 0;
            int peak = 0;
            Func<string, IDisposable> observe = phase =>
            {
                object scheduler = schedulerLocal.GetType().GetProperty("Value")!.GetValue(schedulerLocal)!;
                Assert.NotNull(scheduler);
                SemaphoreSlim permits = (SemaphoreSlim)scheduler.GetType().GetField("_permits", flags)!
                    .GetValue(scheduler)!;
                Assert.InRange(maxConcurrency - permits.CurrentCount, 1, maxConcurrency);
                object? operation = operationType.GetProperty(
                    "ExecutingState", BindingFlags.Static | flags)!.GetValue(null);
                if (operation is not null)
                {
                    object permit = operationType.GetProperty("Permit", flags)!.GetValue(operation)!;
                    Assert.NotNull(permit);
                    Assert.Same(permits, permit.GetType().GetField("_permits", flags)!.GetValue(permit));
                }
                int current = Interlocked.Increment(ref active);
                int previous;
                do
                {
                    previous = Volatile.Read(ref peak);
                }
                while (current > previous && Interlocked.CompareExchange(ref peak, current, previous) != previous);
                observed.AddOrUpdate(phase, 1, (_, count) => count + 1);
                Thread.Sleep(10);
                return new ObservationScope(() => Interlocked.Decrement(ref active));
            };
            reader.WorkScope = observe;
            harness.WorkScope = observe;
            try
            {
                await harness.Pipeline.ExecuteAsync(plans, harness.Options, harness.Coordinator, maxConcurrency)
                    .WaitAsync(TimeSpan.FromSeconds(60));
            }
            finally
            {
                reader.WorkScope = null;
                harness.WorkScope = null;
            }
            Assert.InRange(peak, 1, maxConcurrency);
            Assert.Equal(0, active);
            foreach (string phase in new[] { "manifest discovery", "staging/snapshot directory creation",
                "manifest signing", "ordinary signing", "artifact cleanup" })
            {
                Assert.True(observed.TryGetValue(phase, out int count) && count > 0, phase);
            }
        }

        private sealed class ObservationScope : IDisposable
        {
            private Action? _dispose;
            internal ObservationScope(Action dispose) => _dispose = dispose;
            public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }

        private sealed class ObservingManifestReader : IClickOnceManifestReader
        {
            private readonly ClickOnceManifestReader _reader = new();
            internal Func<string, IDisposable>? WorkScope { get; set; }
            public bool TryReadApplicationManifest(Stream stream,
                [NotNullWhen(true)] out IApplicationManifest? manifest)
            {
                using IDisposable? scope = WorkScope?.Invoke("manifest discovery");
                return _reader.TryReadApplicationManifest(stream, out manifest);
            }
            public bool TryReadDeployManifest(Stream stream,
                [NotNullWhen(true)] out IDeployManifest? manifest)
            {
                using IDisposable? scope = WorkScope?.Invoke("manifest discovery");
                return _reader.TryReadDeployManifest(stream, out manifest);
            }
        }

        private sealed class ObservingDirectoryService : IDirectoryService
        {
            private readonly IDirectoryService _inner;
            private readonly Func<string, IDisposable?> _scope;
            private readonly Action<DirectoryInfo>? _created;
            internal ObservingDirectoryService(IDirectoryService inner, Func<string, IDisposable?> scope,
                Action<DirectoryInfo>? created = null)
            {
                _inner = inner;
                _scope = scope;
                _created = created;
            }
            public DirectoryInfo CreateTemporaryDirectory()
            {
                using IDisposable? scope = _scope("staging/snapshot directory creation");
                DirectoryInfo directory = _inner.CreateTemporaryDirectory();
                _created?.Invoke(directory);
                return directory;
            }
            public void Delete(DirectoryInfo directory)
            {
                using IDisposable? scope = _scope("artifact cleanup");
                _inner.Delete(directory);
            }
            public void Dispose() => _inner.Dispose();
        }
    }
}
