using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

namespace Alethic.Auth0.Operator.Tests.TestSupport
{
    /// <summary>
    /// A single captured log entry — level, formatted message text, and exception.
    /// </summary>
    internal sealed class CapturingLogEntry
    {
        public LogLevel Level { get; init; }
        public string Message { get; init; } = string.Empty;
        public Exception? Exception { get; init; }
    }

    /// <summary>
    /// Minimal capturing <see cref="ILogger"/>. Records the formatted message — which, for
    /// the structured-JSON helpers under test (<c>LogAuth0Read</c> / <c>LogAuth0Write</c>
    /// / <c>LogWarningJson</c>), is the raw JSON payload. Lifted out of
    /// <c>V1ControllerRateLimitObservabilityTests</c> in M3 (LA-RF-81 code review) so
    /// multiple test classes can share the same scaffold without duplication.
    /// </summary>
    internal sealed class CapturingLogger : ILogger
    {
        public List<CapturingLogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new CapturingLogEntry
            {
                Level = logLevel,
                Message = formatter(state, exception),
                Exception = exception,
            });
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    /// <summary>
    /// Adapts a non-generic <see cref="ILogger"/> into the <see cref="ILogger{T}"/> that
    /// controller constructors require, so tests can supply a single capturing logger.
    /// </summary>
    internal sealed class TypedLoggerAdapter<T> : ILogger<T>
    {
        private readonly ILogger _inner;
        public TypedLoggerAdapter(ILogger inner) { _inner = inner; }
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state)!;
        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
