using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Alethic.Auth0.Operator.Controllers;
using Alethic.Auth0.Operator.Core.Models;
using Alethic.Auth0.Operator.Options;
using Alethic.Auth0.Operator.Services;
using KubeOps.Abstractions.Queue;
using KubeOps.KubernetesClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Auth0.Operator.Tests.Controllers
{
    /// <summary>
    /// Coverage for <see cref="V1ClientController"/>'s per-client connection enable/disable
    /// helpers, which use Auth0's replacement for the deprecated <c>enabled_clients</c> field:
    ///
    /// <c>PATCH /api/v2/connections/{connectionId}/clients</c> with body
    /// <c>[{"client_id":"...","status":true|false}]</c> — success = HTTP 204.
    ///
    /// The controller's <c>IHttpClientFactory</c> dependency lets us inject a fake
    /// <see cref="HttpMessageHandler"/> and assert on the exact request stream.
    /// </summary>
    [TestClass]
    public class V1ClientControllerEnabledConnectionsTests
    {
        private const string TenantBaseUri = "https://example.us.auth0.com/api/v2/";
        private const string ConnectionId = "con_test123";
        private const string ClientId = "abc_clientid";

        // ---- Golden path: enable issues PATCH with status:true and 204 clears ----

        [TestMethod]
        public async Task EnableClientOnConnection_ClientNotYetEnabled_IssuesPatchAndSucceeds()
        {
            var handler = new RecordingHandler((_, __) => new HttpResponseMessage(HttpStatusCode.NoContent));
            var controller = BuildController(handler);
            var tenant = new FakeTenantApiAccess();

            await controller.EnableClientOnConnectionAsync(tenant, ConnectionId, ClientId,
                defaultNamespace: "default", CancellationToken.None);

            Assert.AreEqual(1, handler.Requests.Count);
            var req = handler.Requests[0];
            Assert.AreEqual(HttpMethod.Patch, req.Method);
            Assert.AreEqual(new Uri(new Uri(TenantBaseUri), $"connections/{ConnectionId}/clients"),
                req.RequestUri);
            StringAssert.Contains(req.Body, $"\"client_id\":\"{ClientId}\"");
            StringAssert.Contains(req.Body, "\"status\":true");
            // Body must be a JSON array (per Auth0 PATCH contract).
            Assert.IsTrue(req.Body.TrimStart().StartsWith("["),
                "Auth0 requires the PATCH body to be a JSON array of rows.");
        }

        // ---- Success path emits a structured info log so production has an audit trail ----

        [TestMethod]
        public async Task EnableClientOnConnection_Success_EmitsStructuredSuccessLog()
        {
            var handler = new RecordingHandler((_, __) => new HttpResponseMessage(HttpStatusCode.NoContent));
            var logger = new RecordingLogger<V1ClientController>();
            var controller = BuildController(handler, logger);

            await controller.EnableClientOnConnectionAsync(new FakeTenantApiAccess(), ConnectionId, ClientId,
                defaultNamespace: "default", CancellationToken.None);

            var hit = logger.Messages.SingleOrDefault(m =>
                m.Contains("\"operation\":\"enable_client_on_connection\"")
                && m.Contains("succeeded")
                && m.Contains($"\"connectionId\":\"{ConnectionId}\"")
                && m.Contains($"\"clientId\":\"{ClientId}\""));
            Assert.IsNotNull(hit,
                "Expected a structured info log carrying the operation label, success phrasing, and the connection/client pair.\n"
                + "Captured messages:\n  - " + string.Join("\n  - ", logger.Messages));
        }

        [TestMethod]
        public async Task DisableClientOnConnection_Success_EmitsStructuredSuccessLog()
        {
            var handler = new RecordingHandler((_, __) => new HttpResponseMessage(HttpStatusCode.NoContent));
            var logger = new RecordingLogger<V1ClientController>();
            var controller = BuildController(handler, logger);

            await controller.DisableClientOnConnectionAsync(new FakeTenantApiAccess(), ConnectionId, ClientId,
                defaultNamespace: "default", CancellationToken.None);

            var hit = logger.Messages.SingleOrDefault(m =>
                m.Contains("\"operation\":\"disable_client_on_connection\"")
                && m.Contains("succeeded")
                && m.Contains($"\"connectionId\":\"{ConnectionId}\"")
                && m.Contains($"\"clientId\":\"{ClientId}\""));
            Assert.IsNotNull(hit,
                "Expected a structured info log carrying the operation label and success phrasing for the disable path.\n"
                + "Captured messages:\n  - " + string.Join("\n  - ", logger.Messages));
        }

        // ---- Golden path: disable issues PATCH with status:false and 204 clears ----

        [TestMethod]
        public async Task DisableClientOnConnection_ClientEnabled_IssuesPatchAndSucceeds()
        {
            var handler = new RecordingHandler((_, __) => new HttpResponseMessage(HttpStatusCode.NoContent));
            var controller = BuildController(handler);

            await controller.DisableClientOnConnectionAsync(new FakeTenantApiAccess(), ConnectionId, ClientId,
                defaultNamespace: "default", CancellationToken.None);

            Assert.AreEqual(1, handler.Requests.Count);
            var req = handler.Requests[0];
            Assert.AreEqual(HttpMethod.Patch, req.Method);
            Assert.AreEqual(new Uri(new Uri(TenantBaseUri), $"connections/{ConnectionId}/clients"),
                req.RequestUri);
            StringAssert.Contains(req.Body, $"\"client_id\":\"{ClientId}\"");
            StringAssert.Contains(req.Body, "\"status\":false");
        }

        // ---- Non-2xx responses surface as exceptions (no benign handling) ----

        [TestMethod]
        public async Task EnableClientOnConnection_400_Throws()
        {
            var handler = new RecordingHandler((_, __) => new HttpResponseMessage(HttpStatusCode.BadRequest)
                { Content = new StringContent("{\"statusCode\":400,\"error\":\"Bad Request\"}") });
            var controller = BuildController(handler);

            await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
                controller.EnableClientOnConnectionAsync(new FakeTenantApiAccess(), ConnectionId, ClientId,
                    defaultNamespace: "default", CancellationToken.None));
        }

        [TestMethod]
        public async Task DisableClientOnConnection_403_Throws()
        {
            var handler = new RecordingHandler((_, __) => new HttpResponseMessage(HttpStatusCode.Forbidden)
                { Content = new StringContent("{\"statusCode\":403,\"error\":\"Forbidden\"}") });
            var controller = BuildController(handler);

            await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
                controller.DisableClientOnConnectionAsync(new FakeTenantApiAccess(), ConnectionId, ClientId,
                    defaultNamespace: "default", CancellationToken.None));
        }

        // ---- 401 triggers token regeneration and a single retry ----

        [TestMethod]
        public async Task EnableClientOnConnection_401Once_RegeneratesTokenAndRetriesOnce()
        {
            var calls = 0;
            var handler = new RecordingHandler((_, __) =>
            {
                calls++;
                return calls == 1
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("") }
                    : new HttpResponseMessage(HttpStatusCode.NoContent);
            });
            var controller = BuildController(handler);
            var tenant = new FakeTenantApiAccess { TokenSequence = new[] { "tok-1", "tok-2" } };

            await controller.EnableClientOnConnectionAsync(tenant, ConnectionId, ClientId,
                defaultNamespace: "default", CancellationToken.None);

            Assert.AreEqual(2, handler.Requests.Count, "Should retry once after 401");
            Assert.AreEqual("tok-1", handler.Requests[0].Authorization);
            Assert.AreEqual("tok-2", handler.Requests[1].Authorization);
            Assert.AreEqual(1, tenant.InvalidateCalls,
                "401 must evict the cached token so the retry gets a freshly-fetched one (H4)");
        }

        // ---- 5xx is surfaced (the existing retry-with-backoff wrapper lives upstream) ----

        [TestMethod]
        public async Task EnableClientOnConnection_5xx_Throws()
        {
            var handler = new RecordingHandler((_, __) => new HttpResponseMessage(HttpStatusCode.InternalServerError)
                { Content = new StringContent("{\"statusCode\":500,\"error\":\"server\"}") });
            var controller = BuildController(handler);

            await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
                controller.EnableClientOnConnectionAsync(new FakeTenantApiAccess(), ConnectionId, ClientId,
                    defaultNamespace: "default", CancellationToken.None));
        }

        // ---- 429 is surfaced as Auth0.Core.Exceptions.RateLimitApiException so the upstream
        //      RateLimitApiException catch in V1Controller / V1TenantEntityController can read
        //      X-RateLimit-Reset and requeue per Auth0's documented backoff window. ----

        [TestMethod]
        public async Task DisableClientOnConnection_429_ThrowsRateLimitApiException()
        {
            var resetEpoch = DateTimeOffset.UtcNow.AddSeconds(42).ToUnixTimeSeconds();
            var handler = new RecordingHandler((_, __) =>
            {
                var resp = new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("{\"statusCode\":429,\"error\":\"Too Many Requests\"}")
                };
                resp.Headers.TryAddWithoutValidation("X-RateLimit-Reset", resetEpoch.ToString());
                return resp;
            });
            var controller = BuildController(handler);

            var ex = await Assert.ThrowsExceptionAsync<global::Auth0.Core.Exceptions.RateLimitApiException>(() =>
                controller.DisableClientOnConnectionAsync(new FakeTenantApiAccess(), ConnectionId, ClientId,
                    defaultNamespace: "default", CancellationToken.None));

            Assert.IsNotNull(ex.RateLimit, "RateLimit must be populated so the upstream handler can schedule the requeue");
            Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(resetEpoch), ex.RateLimit!.Reset);
        }

        // ---- Orchestrator end-to-end: GET current → diff → PATCH adds, PATCH removes ----
        [TestMethod]
        public async Task ReconcileEnabledConnections_MixedAddAndRemove_IssuesPatchPerAffectedConnection()
        {
            const string KeepId = "con_keep";
            const string AddId = "con_add";
            const string RemoveId = "con_remove";

            var handler = new RecordingHandler((req, _) =>
            {
                if (req.Method == HttpMethod.Get)
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(BuildConnectionsBody(KeepId, RemoveId))
                    };
                if (req.Method == HttpMethod.Patch)
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });
            var controller = BuildController(handler);

            await controller.ReconcileEnabledConnections(new FakeTenantApiAccess(), ClientId,
                new[]
                {
                    new V1ConnectionReference { Id = KeepId },
                    new V1ConnectionReference { Id = AddId }
                },
                defaultNamespace: "default", CancellationToken.None);

            var patches = handler.Requests.Where(r => r.Method == HttpMethod.Patch).ToList();
            Assert.AreEqual(2, patches.Count, "Expected one PATCH per affected connection (add + remove).");

            var addPatch = patches.SingleOrDefault(p =>
                p.RequestUri!.AbsolutePath.EndsWith($"/connections/{AddId}/clients"));
            Assert.IsNotNull(addPatch, "Expected a PATCH for the added connection");
            StringAssert.Contains(addPatch!.Body, $"\"client_id\":\"{ClientId}\"");
            StringAssert.Contains(addPatch.Body, "\"status\":true");

            var removePatch = patches.SingleOrDefault(p =>
                p.RequestUri!.AbsolutePath.EndsWith($"/connections/{RemoveId}/clients"));
            Assert.IsNotNull(removePatch, "Expected a PATCH for the removed connection");
            StringAssert.Contains(removePatch!.Body, $"\"client_id\":\"{ClientId}\"");
            StringAssert.Contains(removePatch.Body, "\"status\":false");

            // Untouched 'keep' connection must not be re-issued.
            Assert.IsFalse(handler.Requests.Any(r =>
                r.Method == HttpMethod.Patch &&
                r.RequestUri!.AbsolutePath.EndsWith($"/connections/{KeepId}/clients")),
                "The unchanged 'keep' connection should not be PATCHed.");
        }

        // Single helper for assembling the GET /clients/{id}/connections response body so the
        // orchestration tests above stay focused on the diff/apply logic, not on Auth0's wire shape.
        private static string BuildConnectionsBody(params string[] connectionIds)
        {
            var ids = string.Join(",", connectionIds.Select(id => $"{{\"id\":\"{id}\"}}"));
            return $"{{\"connections\":[{ids}]}}";
        }

        // ---- helpers ----

        private static V1ClientController BuildController(HttpMessageHandler handler,
            ILogger<V1ClientController>? logger = null)
        {
            var factory = new SingleHandlerHttpClientFactory(handler);
            // DispatchProxy gives us a runtime stub for IKubernetesClient — the helpers under
            // test never call into it, so any invocation will throw via the proxy's interceptor.
            var kube = System.Reflection.DispatchProxy.Create<IKubernetesClient, ThrowingKubeProxy>();
            return new V1ClientController(
                kube: kube,
                requeue: (EntityRequeue<Alethic.Auth0.Operator.Models.V1Client>)((entity, ts) => { }),
                cache: new MemoryCache(new MemoryCacheOptions()),
                logger: logger ?? NullLogger<V1ClientController>.Instance,
                options: Microsoft.Extensions.Options.Options.Create(new OperatorOptions()),
                httpClientFactory: factory);
        }

        /// <summary>
        /// Captures formatted log messages so tests can assert observability contracts
        /// (e.g. the structured success log emitted on 2xx connection enable/disable).
        /// </summary>
        private sealed class RecordingLogger<T> : ILogger<T>
        {
            public List<string> Messages { get; } = new();

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Messages.Add(formatter(state, exception));
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }

        public class ThrowingKubeProxy : System.Reflection.DispatchProxy
        {
            protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
                throw new InvalidOperationException(
                    $"IKubernetesClient.{targetMethod?.Name} was unexpectedly invoked by the helpers under test.");
        }

        private sealed class SingleHandlerHttpClientFactory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;
            public SingleHandlerHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
            public HttpClient CreateClient(string name) => new HttpClient(_handler, disposeHandler: false);
        }

        private sealed class FakeTenantApiAccess : ITenantApiAccess
        {
            public string[] TokenSequence { get; set; } = new[] { "fake-token" };
            public int InvalidateCalls { get; private set; }
            private int _index;
            public Uri BaseUri => new Uri(TenantBaseUri);
            public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            {
                var tok = TokenSequence[Math.Min(_index, TokenSequence.Length - 1)];
                _index++;
                return Task.FromResult(tok);
            }
            public Task InvalidateAccessTokenAsync(CancellationToken cancellationToken = default)
            {
                InvalidateCalls++;
                return Task.CompletedTask;
            }
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;
            public List<RecordedRequest> Requests { get; } = new();

            public RecordingHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
            {
                _responder = responder;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var body = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                Requests.Add(new RecordedRequest(
                    request.Method,
                    request.RequestUri,
                    request.Headers.Authorization?.Parameter,
                    body));
                return _responder(request, cancellationToken);
            }
        }

        private sealed record RecordedRequest(HttpMethod Method, Uri? RequestUri, string? Authorization, string Body);

    }
}
