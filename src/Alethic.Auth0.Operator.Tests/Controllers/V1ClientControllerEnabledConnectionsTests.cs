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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Auth0.Operator.Tests.Controllers
{
    /// <summary>
    /// Coverage for <see cref="V1ClientController"/>'s per-client connection enable/disable
    /// helpers, which replaced the deprecated <c>enabled_clients</c> PATCH flow on
    /// <c>PATCH /api/v2/connections/{id}</c>.
    ///
    /// New endpoints exercised here:
    /// - <c>POST /api/v2/connections/{connectionId}/clients</c> with body <c>{"client_id":"..."}</c>
    /// - <c>DELETE /api/v2/connections/{connectionId}/clients/{clientId}</c>
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

        // ---- Golden path: enable issues POST and 2xx clears ----

        [TestMethod]
        public async Task EnableClientOnConnection_ClientNotYetEnabled_IssuesPostAndSucceeds()
        {
            var handler = new RecordingHandler((req, _) =>
                new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("") });
            var controller = BuildController(handler);
            var tenant = new FakeTenantApiAccess();

            await controller.EnableClientOnConnectionAsync(tenant, ConnectionId, ClientId, CancellationToken.None);

            Assert.AreEqual(1, handler.Requests.Count);
            var req = handler.Requests[0];
            Assert.AreEqual(HttpMethod.Post, req.Method);
            Assert.AreEqual(new Uri(new Uri(TenantBaseUri), $"connections/{ConnectionId}/clients"),
                req.RequestUri);
            StringAssert.Contains(req.Body, $"\"client_id\":\"{ClientId}\"");
        }

        // ---- Benign 409 ("already enabled") swallowed as no-op ----

        [TestMethod]
        public async Task EnableClientOnConnection_AlreadyEnabled_BenignErrorCode_IsNoOp()
        {
            var body = "{\"statusCode\":409,\"error\":\"Conflict\",\"message\":\"Client already enabled\"," +
                       "\"errorCode\":\"connection_client_already_enabled\"}";
            var handler = new RecordingHandler((_, __) => new HttpResponseMessage(HttpStatusCode.Conflict)
                { Content = new StringContent(body) });
            var controller = BuildController(handler);

            await controller.EnableClientOnConnectionAsync(new FakeTenantApiAccess(), ConnectionId, ClientId,
                CancellationToken.None);

            Assert.AreEqual(1, handler.Requests.Count);
        }

        // ---- Non-benign 409 (e.g. validation) surfaces as an exception ----

        [TestMethod]
        public async Task EnableClientOnConnection_409WithUnrelatedErrorCode_Throws()
        {
            var body = "{\"statusCode\":409,\"error\":\"Conflict\",\"message\":\"Validation failed\"," +
                       "\"errorCode\":\"validation_error\"}";
            var handler = new RecordingHandler((_, __) => new HttpResponseMessage(HttpStatusCode.Conflict)
                { Content = new StringContent(body) });
            var controller = BuildController(handler);

            await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
                controller.EnableClientOnConnectionAsync(new FakeTenantApiAccess(), ConnectionId, ClientId,
                    CancellationToken.None));
        }

        // ---- Golden path: disable issues DELETE and 204 clears ----

        [TestMethod]
        public async Task DisableClientOnConnection_ClientEnabled_IssuesDeleteAndSucceeds()
        {
            var handler = new RecordingHandler((_, __) =>
                new HttpResponseMessage(HttpStatusCode.NoContent));
            var controller = BuildController(handler);

            await controller.DisableClientOnConnectionAsync(new FakeTenantApiAccess(), ConnectionId, ClientId,
                CancellationToken.None);

            Assert.AreEqual(1, handler.Requests.Count);
            var req = handler.Requests[0];
            Assert.AreEqual(HttpMethod.Delete, req.Method);
            Assert.AreEqual(new Uri(new Uri(TenantBaseUri), $"connections/{ConnectionId}/clients/{ClientId}"),
                req.RequestUri);
        }

        // ---- Benign 404 ("not currently enabled") swallowed as no-op ----

        [TestMethod]
        public async Task DisableClientOnConnection_NotEnabled_BenignErrorCode_IsNoOp()
        {
            var body = "{\"statusCode\":404,\"error\":\"Not Found\",\"message\":\"Client not enabled\"," +
                       "\"errorCode\":\"connection_client_not_found\"}";
            var handler = new RecordingHandler((_, __) => new HttpResponseMessage(HttpStatusCode.NotFound)
                { Content = new StringContent(body) });
            var controller = BuildController(handler);

            await controller.DisableClientOnConnectionAsync(new FakeTenantApiAccess(), ConnectionId, ClientId,
                CancellationToken.None);

            Assert.AreEqual(1, handler.Requests.Count);
        }

        // ---- Non-benign 404 (wrong connection_id) surfaces ----

        [TestMethod]
        public async Task DisableClientOnConnection_404WithUnrelatedErrorCode_Throws()
        {
            var body = "{\"statusCode\":404,\"error\":\"Not Found\",\"message\":\"Connection not found\"," +
                       "\"errorCode\":\"connection_not_found\"}";
            var handler = new RecordingHandler((_, __) => new HttpResponseMessage(HttpStatusCode.NotFound)
                { Content = new StringContent(body) });
            var controller = BuildController(handler);

            await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
                controller.DisableClientOnConnectionAsync(new FakeTenantApiAccess(), ConnectionId, ClientId,
                    CancellationToken.None));
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
                    : new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("") };
            });
            var controller = BuildController(handler);
            var tenant = new FakeTenantApiAccess { TokenSequence = new[] { "tok-1", "tok-2" } };

            await controller.EnableClientOnConnectionAsync(tenant, ConnectionId, ClientId, CancellationToken.None);

            Assert.AreEqual(2, handler.Requests.Count, "Should retry once after 401");
            Assert.AreEqual("tok-1", handler.Requests[0].Authorization);
            Assert.AreEqual("tok-2", handler.Requests[1].Authorization);
            Assert.AreEqual(1, tenant.InvalidateCalls,
                "401 must evict the cached token so the retry gets a freshly-fetched one (H4)");
        }

        // ---- Concurrent add + remove of same (connection, client): both calls happen, atomic per call ----

        [TestMethod]
        public async Task AddAndRemove_SamePair_BothCallsIssuedDeterministically()
        {
            var handler = new RecordingHandler((req, _) =>
                new HttpResponseMessage(req.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.NoContent));
            var controller = BuildController(handler);
            var tenant = new FakeTenantApiAccess();

            // No mutex any more — fire concurrently and assert both calls completed.
            await Task.WhenAll(
                controller.EnableClientOnConnectionAsync(tenant, ConnectionId, ClientId, CancellationToken.None),
                controller.DisableClientOnConnectionAsync(tenant, ConnectionId, ClientId, CancellationToken.None));

            Assert.AreEqual(2, handler.Requests.Count);
            Assert.IsTrue(handler.Requests.Any(r => r.Method == HttpMethod.Post));
            Assert.IsTrue(handler.Requests.Any(r => r.Method == HttpMethod.Delete));
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
                    CancellationToken.None));
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
                    CancellationToken.None));

            Assert.IsNotNull(ex.RateLimit, "RateLimit must be populated so the upstream handler can schedule the requeue");
            Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(resetEpoch), ex.RateLimit!.Reset);
        }

        // ---- Orchestrator end-to-end: GET current → diff → POST adds, DELETE removes ----
        [TestMethod]
        public async Task ReconcileEnabledConnections_MixedAddAndRemove_IssuesPostAndDelete()
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
                if (req.Method == HttpMethod.Post)
                    return new HttpResponseMessage(HttpStatusCode.Created);
                if (req.Method == HttpMethod.Delete)
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

            var post = handler.Requests.SingleOrDefault(r => r.Method == HttpMethod.Post);
            var delete = handler.Requests.SingleOrDefault(r => r.Method == HttpMethod.Delete);

            Assert.IsNotNull(post, "Expected a POST for the added connection");
            Assert.IsTrue(post!.RequestUri!.AbsolutePath.EndsWith($"/connections/{AddId}/clients"));
            StringAssert.Contains(post.Body, $"\"client_id\":\"{ClientId}\"");

            Assert.IsNotNull(delete, "Expected a DELETE for the removed connection");
            Assert.IsTrue(delete!.RequestUri!.AbsolutePath.EndsWith($"/connections/{RemoveId}/clients/{ClientId}"));

            // Untouched 'keep' connection must not be re-issued.
            Assert.IsFalse(handler.Requests.Any(r =>
                r.Method != HttpMethod.Get &&
                r.RequestUri!.AbsolutePath.Contains($"/connections/{KeepId}/")));
        }

        // Single helper for assembling the GET /clients/{id}/connections response body so the
        // orchestration tests above stay focused on the diff/apply logic, not on Auth0's wire shape.
        private static string BuildConnectionsBody(params string[] connectionIds)
        {
            var ids = string.Join(",", connectionIds.Select(id => $"{{\"id\":\"{id}\"}}"));
            return $"{{\"connections\":[{ids}]}}";
        }

        // ---- helpers ----

        private static V1ClientController BuildController(HttpMessageHandler handler)
        {
            var factory = new SingleHandlerHttpClientFactory(handler);
            // DispatchProxy gives us a runtime stub for IKubernetesClient — the helpers under
            // test never call into it, so any invocation will throw via the proxy's interceptor.
            var kube = System.Reflection.DispatchProxy.Create<IKubernetesClient, ThrowingKubeProxy>();
            return new V1ClientController(
                kube: kube,
                requeue: (EntityRequeue<Alethic.Auth0.Operator.Models.V1Client>)((entity, ts) => { }),
                cache: new MemoryCache(new MemoryCacheOptions()),
                logger: NullLogger<V1ClientController>.Instance,
                options: Microsoft.Extensions.Options.Options.Create(new OperatorOptions()),
                httpClientFactory: factory);
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
