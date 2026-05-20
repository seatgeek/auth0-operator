using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Controllers;
using Alethic.Auth0.Operator.Core.Models.Client;
using Alethic.Auth0.Operator.Models;
using Alethic.Auth0.Operator.Options;

using Auth0.Core;
using Auth0.Core.Exceptions;

using k8s.Models;

using KubeOps.Abstractions.Queue;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

namespace Alethic.Auth0.Operator.Tests.Controllers
{
    /// <summary>
    /// Coverage for the F4 (LA-RF-81) log-routing + jitter changes in
    /// <c>V1Controller.ReconcileAsync</c>'s <see cref="RateLimitApiException"/> and
    /// <see cref="ErrorApiException"/> catches. See:
    /// <list type="bullet">
    /// <item>plans/2026-05-19__18-12-45 - auth0-operator - resolve-drifts-and-error-noise.md</item>
    /// <item>Architect decision — 2026-05-19 (Option C, log-routing + jitter only, no Polly).</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class V1ControllerRateLimitObservabilityTests
    {
        // ---- Common helpers ----

        private static V1Client MakeClient(string name = "c1", string ns = "default", string? resourceVersion = "rv-1")
        {
            return new V1Client
            {
                Metadata = new V1ObjectMeta
                {
                    Name = name,
                    NamespaceProperty = ns,
                    ResourceVersion = resourceVersion,
                    Annotations = new Dictionary<string, string>(),
                },
                Spec = new V1Client.SpecDef { Conf = new ClientConf() },
                Status = new V1Client.StatusDef(),
            };
        }

        private static RateLimitApiException MakeRateLimitExceptionAt(DateTimeOffset resetAt)
        {
            // The SDK's RateLimit type is parsed from HTTP headers; the simplest way to
            // get an instance pointing at a specific Reset is to round-trip through
            // RateLimit.Parse on a synthetic response.
            using var resp = new HttpResponseMessage((HttpStatusCode)429);
            resp.Headers.TryAddWithoutValidation("X-RateLimit-Reset", resetAt.ToUnixTimeSeconds().ToString());
            resp.Headers.TryAddWithoutValidation("X-RateLimit-Limit", "100");
            resp.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            var rl = RateLimit.Parse(resp.Headers);
            return new RateLimitApiException(rl, new ApiError { Message = "Too Many Requests" });
        }

        // ============================================================================
        // 1. Jitter window — assert requeue delay falls within ±20% of (Reset - Now),
        //    floored at 60s. Drives the sampler at its extreme endpoints to verify
        //    the [-20%, +20%) range and the floor-clamp.
        // ============================================================================

        [TestMethod]
        public async Task RateLimitApiException_RequeueDelay_AppliesJitterWithinTwentyPercentOfResetHint()
        {
            // Reset is 100s in the future → base delay ~100s (well above the 60s floor).
            // With the jitter sampler driven to its endpoints, the requeue delay must
            // land inside [80s, 120s] (i.e. 100s ± 20%).
            var originalSampler = V1Controller<V1Client, V1Client.SpecDef, V1Client.StatusDef, ClientConf>.RateLimitJitterSampler;
            try
            {
                foreach (var (sample, expectMin, expectMax) in new[]
                {
                    (0.0,   80.0,  80.0),    // factor = 0.8 → 100 * 0.8 = 80s exact
                    (0.5,   99.99, 100.01),  // factor = 1.0 → 100s exact
                    (0.999, 119.0, 120.0),   // factor ≈ 1.2 → ~120s
                })
                {
                    V1Controller<V1Client, V1Client.SpecDef, V1Client.StatusDef, ClientConf>.RateLimitJitterSampler = () => sample;

                    var entity = MakeClient();
                    var requeueCalls = new List<(V1Client entity, TimeSpan delay)>();
                    var kube = new Mock<IKubernetesClient>(MockBehavior.Loose);
                    var controller = new TestController(
                        kube.Object,
                        requeue: (e, d) => requeueCalls.Add((e, d)),
                        reconcileImpl: (_, __) => throw MakeRateLimitExceptionAt(DateTimeOffset.Now.AddSeconds(100)));

                    await controller.ReconcileAsync(entity, CancellationToken.None);

                    Assert.AreEqual(1, requeueCalls.Count, $"sample={sample}: expected exactly one Requeue.");
                    var d = requeueCalls[0].delay.TotalSeconds;
                    Assert.IsTrue(d >= expectMin - 1 && d <= expectMax + 1,
                        $"sample={sample}: expected delay in [{expectMin}s, {expectMax}s]; got {d:F2}s.");
                }

                // Floor-clamp: a Reset that is in the past (or very near) must clamp
                // to the 60s floor even when jitter would push lower. With sample=0.0
                // the jitter factor is 0.8 → 60s * 0.8 = 48s pre-clamp → clamp to 60s.
                V1Controller<V1Client, V1Client.SpecDef, V1Client.StatusDef, ClientConf>.RateLimitJitterSampler = () => 0.0;
                var entity2 = MakeClient();
                var requeueCalls2 = new List<(V1Client entity, TimeSpan delay)>();
                var kube2 = new Mock<IKubernetesClient>(MockBehavior.Loose);
                var controller2 = new TestController(
                    kube2.Object,
                    requeue: (e, d) => requeueCalls2.Add((e, d)),
                    reconcileImpl: (_, __) => throw MakeRateLimitExceptionAt(DateTimeOffset.Now.AddSeconds(-30)));
                await controller2.ReconcileAsync(entity2, CancellationToken.None);
                Assert.AreEqual(1, requeueCalls2.Count);
                Assert.AreEqual(TimeSpan.FromMinutes(1), requeueCalls2[0].delay,
                    "Past/near-zero Reset must clamp to the 60s floor regardless of jitter.");
            }
            finally
            {
                V1Controller<V1Client, V1Client.SpecDef, V1Client.StatusDef, ClientConf>.RateLimitJitterSampler = originalSampler;
            }
        }

        // ============================================================================
        // 2. RateLimit warn log carries the new facet fields.
        // ============================================================================

        [TestMethod]
        public async Task RateLimitApiException_LogIncludesResetAndRequeueFields()
        {
            var resetAt = DateTimeOffset.UtcNow.AddSeconds(150);

            var entity = MakeClient();
            var capturingLogger = new CapturingLogger();
            var kube = new Mock<IKubernetesClient>(MockBehavior.Loose);
            var controller = new TestController(
                kube.Object,
                requeue: (_, __) => { },
                logger: capturingLogger,
                reconcileImpl: (_, __) => throw MakeRateLimitExceptionAt(resetAt));

            await controller.ReconcileAsync(entity, CancellationToken.None);

            // The first Warning log emitted from the RateLimit catch is the "Auth0 rate
            // limit exceeded ..." structured payload. Parse it and assert facet fields.
            var warnEntries = capturingLogger.Entries
                .Where(e => e.Level == LogLevel.Warning)
                .Select(e => TryParseJson(e.Message))
                .Where(e => e is not null)
                .ToList();

            var rateLimitEntry = warnEntries
                .FirstOrDefault(e => e!.RootElement.TryGetProperty("message", out var m)
                                     && m.GetString()!.Contains("Auth0 rate limit exceeded"));
            Assert.IsNotNull(rateLimitEntry, "Expected the structured Auth0-rate-limit-exceeded warn log to be emitted.");

            var root = rateLimitEntry!.RootElement;
            Assert.IsTrue(root.TryGetProperty("auth0RateLimitResetUtc", out var resetProp),
                "Expected `auth0RateLimitResetUtc` field on the warn log.");
            Assert.IsFalse(resetProp.ValueKind == JsonValueKind.Null,
                "`auth0RateLimitResetUtc` must be non-null when RateLimit.Reset is present.");
            // ISO-8601 round-trip
            var parsed = DateTimeOffset.Parse(resetProp.GetString()!);
            Assert.AreEqual(resetAt.ToUnixTimeSeconds(), parsed.ToUnixTimeSeconds(),
                "`auth0RateLimitResetUtc` must round-trip to the same instant as RateLimit.Reset.");

            Assert.IsTrue(root.TryGetProperty("auth0RequeueAfterSeconds", out var requeueProp),
                "Expected `auth0RequeueAfterSeconds` field on the warn log.");
            Assert.AreEqual(JsonValueKind.Number, requeueProp.ValueKind);
            var requeueAfter = requeueProp.GetInt32();
            Assert.IsTrue(requeueAfter >= 60,
                $"`auth0RequeueAfterSeconds` must be at least the 60s floor; got {requeueAfter}.");
        }

        // ============================================================================
        // 3. ErrorApiException 429: retryable hint = true.
        // ============================================================================

        [TestMethod]
        public async Task ErrorApiException_429_LogIncludesRetryableHintTrue()
        {
            var (root, statusCode, hint) = await CaptureErrorApiLogAsync(HttpStatusCode.TooManyRequests);
            Assert.AreEqual(429, statusCode);
            Assert.IsTrue(hint, "`auth0RetryableHint` must be true for HTTP 429.");
        }

        // ============================================================================
        // 4. ErrorApiException 400: retryable hint = false.
        // ============================================================================

        [TestMethod]
        public async Task ErrorApiException_400_LogIncludesRetryableHintFalse()
        {
            var (root, statusCode, hint) = await CaptureErrorApiLogAsync(HttpStatusCode.BadRequest);
            Assert.AreEqual(400, statusCode);
            Assert.IsFalse(hint, "`auth0RetryableHint` must be false for HTTP 400.");
        }

        private static async Task<(JsonElement Root, int StatusCode, bool RetryableHint)> CaptureErrorApiLogAsync(HttpStatusCode status)
        {
            var entity = MakeClient();
            var capturingLogger = new CapturingLogger();
            var kube = new Mock<IKubernetesClient>(MockBehavior.Loose);
            var controller = new TestController(
                kube.Object,
                requeue: (_, __) => { },
                logger: capturingLogger,
                reconcileImpl: (_, __) => throw new ErrorApiException(status, new ApiError { Message = "simulated" }));

            await controller.ReconcileAsync(entity, CancellationToken.None);

            var errorEntry = capturingLogger.Entries
                .Where(e => e.Level == LogLevel.Error)
                .Select(e => TryParseJson(e.Message))
                .Where(e => e is not null)
                .FirstOrDefault(e => e!.RootElement.TryGetProperty("message", out var m)
                                     && m.GetString()!.Contains("Auth0 API error during reconciliation"));
            Assert.IsNotNull(errorEntry, "Expected the structured Auth0-API-error log on the error catch path.");

            var root = errorEntry!.RootElement;
            Assert.IsTrue(root.TryGetProperty("auth0StatusCode", out var statusProp),
                "Expected `auth0StatusCode` field on the error log.");
            Assert.IsTrue(root.TryGetProperty("auth0RetryableHint", out var hintProp),
                "Expected `auth0RetryableHint` field on the error log.");

            return (root, statusProp.GetInt32(), hintProp.GetBoolean());
        }

        private static JsonDocument? TryParseJson(string message)
        {
            // LogJson wraps the entry into a JSON-encoded string and emits it via the
            // "{JsonLog}" template — capturing loggers receive the *formatted* message,
            // which is the raw JSON. Defensive try/catch keeps un-parseable lines from
            // tripping the test scaffolding.
            try
            {
                return JsonDocument.Parse(message);
            }
            catch
            {
                return null;
            }
        }

        // ---- Test scaffolding ----

        private sealed class CapturingLogEntry
        {
            public LogLevel Level { get; init; }
            public string Message { get; init; } = string.Empty;
            public Exception? Exception { get; init; }
        }

        /// <summary>
        /// Minimal capturing logger. Records the formatted message (which, for the
        /// structured-JSON helpers under test, is the raw JSON payload).
        /// </summary>
        private sealed class CapturingLogger : ILogger
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

        private sealed class TestController : V1TenantEntityController<V1Client, V1Client.SpecDef, V1Client.StatusDef, ClientConf>
        {
            private readonly Func<TestController, V1Client, Task>? _reconcileImpl;

            public TestController(
                IKubernetesClient kube,
                EntityRequeue<V1Client> requeue,
                Func<TestController, V1Client, Task>? reconcileImpl = null,
                ILogger? logger = null)
                : base(
                    kube,
                    requeue,
                    new MemoryCache(new MemoryCacheOptions()),
                    (ILogger<TestController>)(logger is null
                        ? Microsoft.Extensions.Logging.Abstractions.NullLogger<TestController>.Instance
                        : new TypedLoggerAdapter<TestController>(logger)),
                    global::Microsoft.Extensions.Options.Options.Create(new OperatorOptions()))
            {
                _reconcileImpl = reconcileImpl;
            }

            protected override string EntityTypeName => "A0Client";

            protected override Task<System.Collections.Hashtable?> Get(global::Auth0.ManagementApi.IManagementApiClient api, string id, string defaultNamespace, CancellationToken cancellationToken)
                => Task.FromResult<System.Collections.Hashtable?>(null);

            protected override Task<string?> Find(global::Auth0.ManagementApi.IManagementApiClient api, V1Client entity, V1Client.SpecDef spec, string defaultNamespace, CancellationToken cancellationToken)
                => Task.FromResult<string?>(null);

            protected override string? ValidateCreate(ClientConf conf) => null;

            protected override Task<string> Create(global::Auth0.ManagementApi.IManagementApiClient api, ClientConf conf, string defaultNamespace, CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);

            protected override Task Update(global::Auth0.ManagementApi.IManagementApiClient api, string id, System.Collections.Hashtable? last, ClientConf conf, IReadOnlyList<Alethic.Auth0.Operator.Controllers.DriftField> driftFields, string defaultNamespace, string entityName, Alethic.Auth0.Operator.Services.ITenantApiAccess tenantApiAccess, Alethic.Auth0.Operator.Controllers.DriftLogContext driftContext, CancellationToken cancellationToken)
                => Task.CompletedTask;

            protected override Task Delete(global::Auth0.ManagementApi.IManagementApiClient api, string id, CancellationToken cancellationToken)
                => Task.CompletedTask;

            protected override async Task<(bool needsRequeue, V1Client updatedEntity)> Reconcile(V1Client entity, CancellationToken cancellationToken)
            {
                if (_reconcileImpl is not null)
                    await _reconcileImpl(this, entity);
                return (false, entity);
            }
        }

        /// <summary>
        /// Adapts a non-generic <see cref="ILogger"/> instance into the
        /// <see cref="ILogger{T}"/> the base constructor demands, so tests can supply
        /// a single capturing logger.
        /// </summary>
        private sealed class TypedLoggerAdapter<T> : ILogger<T>
        {
            private readonly ILogger _inner;
            public TypedLoggerAdapter(ILogger inner) { _inner = inner; }
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state)!;
            public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => _inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
