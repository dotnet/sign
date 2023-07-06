// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE.txt file in the project root for more information.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Sign.Core.Test
{
    internal sealed class TestLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<TestLogEntry> _entries = new();

        internal IEnumerable<TestLogEntry> Entries
        {
            get => _entries;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            throw new NotImplementedException();
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            string message = formatter(state, exception);
            TestLogEntry entry = new(logLevel, message);

            _entries.Enqueue(entry);
        }
    }
}