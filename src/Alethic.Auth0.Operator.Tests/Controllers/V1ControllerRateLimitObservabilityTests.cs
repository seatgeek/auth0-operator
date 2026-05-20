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
using Alethic.Auth0.Operator.Tests.TestSupport;

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
            // Jitter is now one-sided [1.0, 1.2) — never below the floored reset hint —
            // so with the jitter sampler driven to its endpoints, the requeue delay must
            // land inside [100s, 120s].
            // The baseDelay is computed inside ReconcileAsync from (Reset - Now), so a
            // small clock drift between exception construction and catch-handling shaves
            // up to ~2s off the floored 100s base. Assertions tolerate that drift while
            // still enforcing the [1.0, 1.2) jitter shape.
            foreach (var (sample, expectMin, expectMax) in new[]
            {
                (0.0,   98.0,  100.0),   // factor = 1.0 → ~100s (minus clock drift)
                (0.5,   107.0, 110.0),   // factor = 1.1 → ~110s
                (0.999, 117.0, 120.0),   // factor ≈ 1.2 → ~120s
            })
            {
                var entity = MakeClient();
                var requeueCalls = new List<(V1Client entity, TimeSpan delay)>();
                var kube = new Mock<IKubernetesClient>(MockBehavior.Loose);
                var controller = new TestController(
                    kube.Object,
                    requeue: (e, d) => requeueCalls.Add((e, d)),
                    reconcileImpl: (_, __) => throw MakeRateLimitExceptionAt(DateTimeOffset.Now.AddSeconds(100)),
                    jitterSample: sample);

                await controller.ReconcileAsync(entity, CancellationToken.None);

                Assert.AreEqual(1, requeueCalls.Count, $"sample={sample}: expected exactly one Requeue.");
                var d = requeueCalls[0].delay.TotalSeconds;
                Assert.IsTrue(d >= expectMin && d <= expectMax,
                    $"sample={sample}: expected delay in [{expectMin}s, {expectMax}s]; got {d:F2}s.");
            }

            // Floor-clamp: a Reset that is in the past (or very near) must clamp to the
            // 60s floor. With one-sided jitter the floored 60s only spreads upward
            // (60s → [60s, 72s]); driving the sample to 0.0 yields exactly 60s.
            var entity2 = MakeClient();
            var requeueCalls2 = new List<(V1Client entity, TimeSpan delay)>();
            var kube2 = new Mock<IKubernetesClient>(MockBehavior.Loose);
            var controller2 = new TestController(
                kube2.Object,
                requeue: (e, d) => requeueCalls2.Add((e, d)),
                reconcileImpl: (_, __) => throw MakeRateLimitExceptionAt(DateTimeOffset.Now.AddSeconds(-30)),
                jitterSample: 0.0);
            await controller2.ReconcileAsync(entity2, CancellationToken.None);
            Assert.AreEqual(1, requeueCalls2.Count);
            Assert.AreEqual(TimeSpan.FromMinutes(1), requeueCalls2[0].delay,
                "Past/near-zero Reset must clamp to the 60s floor with sample=0.0 (factor=1.0).");
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

            Assert.IsTrue(root.TryGetProperty("auth0RequeueFloorSeconds", out var floorProp),
                "Expected `auth0RequeueFloorSeconds` field (pre-jitter floor) on the warn log.");
            Assert.AreEqual(JsonValueKind.Number, floorProp.ValueKind);
            var requeueFloor = floorProp.GetInt32();
            Assert.IsTrue(requeueFloor >= 60,
                $"`auth0RequeueFloorSeconds` must be at least the 60s floor; got {requeueFloor}.");

            Assert.IsTrue(root.TryGetProperty("auth0RequeueAfterSeconds", out var requeueProp),
                "Expected `auth0RequeueAfterSeconds` field (post-jitter actual delay) on the warn log.");
            Assert.AreEqual(JsonValueKind.Number, requeueProp.ValueKind);
            var requeueAfter = requeueProp.GetInt32();
            Assert.IsTrue(requeueAfter >= requeueFloor,
                $"`auth0RequeueAfterSeconds` ({requeueAfter}) must be >= floor ({requeueFloor}) — jitter is one-sided upward.");
            Assert.IsTrue(requeueAfter <= (int)Math.Ceiling(requeueFloor * 1.2),
                $"`auth0RequeueAfterSeconds` ({requeueAfter}) must be <= floor*1.2 ({requeueFloor * 1.2}).");
        }

        // ============================================================================
        // 2b. RateLimit warn log: auth0RequeueAfterSeconds carries the POST-jitter value
        //     used by Requeue(...), not the pre-jitter floor.
        // ============================================================================

        [TestMethod]
        public async Task RateLimitApiException_RequeueDelay_LogReportsPostJitterValue()
        {
            // Reset 150s in the future, jitter sample = 0.999 → factor ≈ 1.1998 → ~180s
            // (well above the 150s floor). The log's auth0RequeueAfterSeconds must reflect
            // the post-jitter value (~180s), not the pre-jitter floor (150s).
            var resetAt = DateTimeOffset.UtcNow.AddSeconds(150);
            var entity = MakeClient();
            var capturingLogger = new CapturingLogger();
            var kube = new Mock<IKubernetesClient>(MockBehavior.Loose);
            var controller = new TestController(
                kube.Object,
                requeue: (_, __) => { },
                logger: capturingLogger,
                reconcileImpl: (_, __) => throw MakeRateLimitExceptionAt(resetAt),
                jitterSample: 0.999);

            await controller.ReconcileAsync(entity, CancellationToken.None);

            var rateLimitEntry = capturingLogger.Entries
                .Where(e => e.Level == LogLevel.Warning)
                .Select(e => TryParseJson(e.Message))
                .Where(e => e is not null)
                .FirstOrDefault(e => e!.RootElement.TryGetProperty("message", out var m)
                                     && m.GetString()!.Contains("Auth0 rate limit exceeded"));
            Assert.IsNotNull(rateLimitEntry);

            var root = rateLimitEntry!.RootElement;
            var floor = root.GetProperty("auth0RequeueFloorSeconds").GetInt32();
            var after = root.GetProperty("auth0RequeueAfterSeconds").GetInt32();

            // floor should be ~150s (give or take 1s for the clock); after should be ~floor*1.2.
            Assert.IsTrue(floor >= 149 && floor <= 150, $"floor={floor}, expected ~150s.");
            var expectedAfter = floor * 1.2;
            Assert.IsTrue(Math.Abs(after - expectedAfter) <= 1,
                $"auth0RequeueAfterSeconds ({after}) must equal floor*1.2 (~{expectedAfter:F1}) ±1s.");
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
        // CapturingLogger / CapturingLogEntry / TypedLoggerAdapter live in
        // Alethic.Auth0.Operator.Tests.TestSupport (lifted in M3 / LA-RF-81 review).

        private sealed class TestController : V1TenantEntityController<V1Client, V1Client.SpecDef, V1Client.StatusDef, ClientConf>
        {
            private readonly Func<TestController, V1Client, Task>? _reconcileImpl;
            private readonly double? _jitterSample;

            public TestController(
                IKubernetesClient kube,
                EntityRequeue<V1Client> requeue,
                Func<TestController, V1Client, Task>? reconcileImpl = null,
                ILogger? logger = null,
                double? jitterSample = null)
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
                _jitterSample = jitterSample;
            }

            protected override string EntityTypeName => "A0Client";

            // F4 (LA-RF-81): override the protected virtual jitter seam to drive jitter
            // deterministically when the test supplies a fixed sample.
            protected override double SampleRateLimitJitter()
                => _jitterSample ?? base.SampleRateLimitJitter();

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
    }
}
