using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Controllers;
using Alethic.Auth0.Operator.Options;
using Alethic.Auth0.Operator.Services;

using KubeOps.Abstractions.Queue;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Auth0.Operator.Tests.Controllers
{
    /// <summary>
    /// Locks the contract of <see cref="V1Controller{TEntity,TSpec,TStatus,TConf}.LogAuth0ApiCall"/>:
    ///
    /// R1 — exactly one log entry per call, at the right level (Warning for Write, Information for Read).
    /// R2 — Write calls carry <c>reconciliationType</c> / <c>driftReason</c> / <c>driftFields[]</c>
    ///      derived from a required <see cref="DriftLogContext"/>; Read calls omit them; passing a
    ///      null context for a Write is a programmer error that throws at runtime.
    /// </summary>
    [TestClass]
    public class LogAuth0ApiCallTests
    {
        // V1ClientController is the most fully-wired concrete controller in the test bench, so we
        // reuse it to reach the protected LogAuth0ApiCall via reflection rather than building a
        // throwaway controller hierarchy just for this test class.
        private static readonly MethodInfo _logMethod = typeof(V1ClientController)
            .GetMethod("LogAuth0ApiCall", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("LogAuth0ApiCall not found on V1ClientController");

        // --- R1: exactly-one-log per call, correct level ---

        [TestMethod]
        public void ReadCall_EmitsExactlyOneEntry_AtInformationLevel()
        {
            var (controller, logger) = BuildController();

            Invoke(controller, "test read", Auth0ApiCallType.Read, "A0Client", "n", "ns", "p", driftContext: null);

            Assert.AreEqual(1, logger.Entries.Count, "Read call must emit exactly one log entry");
            Assert.AreEqual(LogLevel.Information, logger.Entries[0].Level);
        }

        [TestMethod]
        public void WriteCall_EmitsExactlyOneEntry_AtWarningLevel()
        {
            var (controller, logger) = BuildController();

            Invoke(controller, "test write", Auth0ApiCallType.Write, "A0Client", "n", "ns", "create_client",
                driftContext: DriftLogContext.FirstReconciliation());

            Assert.AreEqual(1, logger.Entries.Count, "Write call must emit exactly one log entry (regression for duplicate-log bug)");
            Assert.AreEqual(LogLevel.Warning, logger.Entries[0].Level);
        }

        // --- R2: write-log carries reconciliationType / driftReason / driftFields ---

        [TestMethod]
        public void WriteCall_DriftPath_SerializesModifiedAndRemovedFields()
        {
            var (controller, logger) = BuildController();
            var fields = new List<DriftField>
            {
                new("name", DriftChangeType.Modified, BeforeValue: "\"old\"", AfterValue: "\"new\""),
                new("description", DriftChangeType.Modified, BeforeValue: "\"a\"", AfterValue: "\"b\""),
                new("enabled_clients", DriftChangeType.Removed, BeforeValue: "[...]", AfterValue: null),
            };
            var ctx = DriftLogContext.Drift(fields);

            Invoke(controller, "msg", Auth0ApiCallType.Write, "A0Connection", "n", "ns", "update_connection", driftContext: ctx);

            using var doc = JsonDocument.Parse(logger.Entries.Single().Message);
            var root = doc.RootElement;
            Assert.AreEqual("drift", root.GetProperty("reconciliationType").GetString());
            Assert.AreEqual("0 added, 2 modified, 1 removed", root.GetProperty("driftReason").GetString());

            var df = root.GetProperty("driftFields");
            Assert.AreEqual(3, df.GetArrayLength());
            Assert.AreEqual("name", df[0].GetProperty("fieldPath").GetString());
            Assert.AreEqual("modified", df[0].GetProperty("changeType").GetString());
            Assert.AreEqual("\"old\"", df[0].GetProperty("beforeValue").GetString());
            Assert.AreEqual("\"new\"", df[0].GetProperty("afterValue").GetString());

            Assert.AreEqual("removed", df[2].GetProperty("changeType").GetString());
            Assert.AreEqual(JsonValueKind.Null, df[2].GetProperty("afterValue").ValueKind);
        }

        [TestMethod]
        public void WriteCall_DriftPath_SerializesAddedField_BeforeIsNull()
        {
            var (controller, logger) = BuildController();
            var ctx = DriftLogContext.Drift(new List<DriftField>
            {
                new("new_field", DriftChangeType.Added, BeforeValue: null, AfterValue: "\"hello\""),
            });

            Invoke(controller, "msg", Auth0ApiCallType.Write, "A0Connection", "n", "ns", "update_connection", driftContext: ctx);

            using var doc = JsonDocument.Parse(logger.Entries.Single().Message);
            var df = doc.RootElement.GetProperty("driftFields");
            Assert.AreEqual("added", df[0].GetProperty("changeType").GetString());
            Assert.AreEqual(JsonValueKind.Null, df[0].GetProperty("beforeValue").ValueKind);
            Assert.AreEqual("\"hello\"", df[0].GetProperty("afterValue").GetString());
        }

        [TestMethod]
        public void WriteCall_FirstReconciliation_EmptyDrift_DescriptiveReason()
        {
            var (controller, logger) = BuildController();

            Invoke(controller, "msg", Auth0ApiCallType.Write, "A0Client", "n", "ns", "create_client",
                driftContext: DriftLogContext.FirstReconciliation());

            using var doc = JsonDocument.Parse(logger.Entries.Single().Message);
            var root = doc.RootElement;
            Assert.AreEqual("first", root.GetProperty("reconciliationType").GetString());
            Assert.AreEqual(0, root.GetProperty("driftFields").GetArrayLength());
            StringAssert.Contains(root.GetProperty("driftReason").GetString()!, "first reconciliation");
        }

        [TestMethod]
        public void WriteCall_Finalizer_EmptyDrift_DescriptiveReason()
        {
            var (controller, logger) = BuildController();

            Invoke(controller, "msg", Auth0ApiCallType.Write, "A0Client", "n", "ns", "delete_client",
                driftContext: DriftLogContext.FinalizerDelete());

            using var doc = JsonDocument.Parse(logger.Entries.Single().Message);
            var root = doc.RootElement;
            Assert.AreEqual("finalizer", root.GetProperty("reconciliationType").GetString());
            Assert.AreEqual(0, root.GetProperty("driftFields").GetArrayLength());
            StringAssert.Contains(root.GetProperty("driftReason").GetString()!, "kubernetes entity deleted");
        }

        [TestMethod]
        public void ReadCall_OmitsDriftFields()
        {
            var (controller, logger) = BuildController();

            Invoke(controller, "msg", Auth0ApiCallType.Read, "A0Client", "n", "ns", "get_client", driftContext: null);

            using var doc = JsonDocument.Parse(logger.Entries.Single().Message);
            var root = doc.RootElement;
            Assert.IsFalse(root.TryGetProperty("reconciliationType", out _), "Read calls must not emit reconciliationType");
            Assert.IsFalse(root.TryGetProperty("driftReason", out _), "Read calls must not emit driftReason");
            Assert.IsFalse(root.TryGetProperty("driftFields", out _), "Read calls must not emit driftFields");
        }

        // --- R2 enforcement: programmer error to write without context ---

        [TestMethod]
        public void WriteCall_WithoutDriftContext_ThrowsArgumentNullException()
        {
            var (controller, _) = BuildController();

            try
            {
                Invoke(controller, "msg", Auth0ApiCallType.Write, "A0Client", "n", "ns", "create_client", driftContext: null);
                Assert.Fail("Expected ArgumentNullException for Write without DriftLogContext");
            }
            catch (TargetInvocationException tex)
            {
                Assert.IsInstanceOfType<ArgumentNullException>(tex.InnerException,
                    "Inner exception should be ArgumentNullException, was " + tex.InnerException?.GetType().Name);
            }
        }

        // --- R2 success-counterpart log on IssueAtomicMembershipChangeAsync ---

        [TestMethod]
        public async Task EnableClient_PreWriteLog_GoesThroughLogAuth0ApiCallWriteFunnel()
        {
            // Every membership-change write must show up in the central LogAuth0ApiCall(Write,...)
            // funnel so it counts in Datadog's @auth0ApiCallType:write tally and carries the
            // canonical DriftLogContext payload shape. The post-success info-log is the outcome
            // marker only — drift context lives on the pre-write entry as the single source of truth.
            var handler = new SingleResponseHandler((_, __) =>
                new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("") });
            var logger = new RecordingLogger();
            var controller = BuildClientControllerForMembership(handler, logger);

            await controller.EnableClientOnConnectionAsync(new FakeTenantApiAccess(), "con_x", "client_x",
                "default", "test-client", CancellationToken.None);

            var writeEntry = logger.Entries
                .FirstOrDefault(e => e.Level == LogLevel.Warning && e.Message.Contains("\"auth0ApiCallType\":\"write\""));
            Assert.AreNotEqual(default, writeEntry, "Expected a pre-write Warning entry from LogAuth0ApiCall for the membership change");

            using var doc = JsonDocument.Parse(writeEntry.Message);
            var root = doc.RootElement;
            Assert.AreEqual("write", root.GetProperty("auth0ApiCallType").GetString());
            Assert.AreEqual("enable_client_on_connection", root.GetProperty("auth0ApiCallPurpose").GetString());
            Assert.AreEqual("drift", root.GetProperty("reconciliationType").GetString());
            StringAssert.Contains(root.GetProperty("driftReason").GetString()!, "enable con_x");

            var df = root.GetProperty("driftFields");
            Assert.AreEqual(1, df.GetArrayLength());
            Assert.AreEqual("spec.conf.enabled_connections[con_x]", df[0].GetProperty("fieldPath").GetString());
            Assert.AreEqual("added", df[0].GetProperty("changeType").GetString());
        }

        [TestMethod]
        public async Task EnableClient_SuccessLog_IsOutcomeOnly_DoesNotDuplicateDriftPayload()
        {
            var handler = new SingleResponseHandler((_, __) =>
                new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("") });
            var logger = new RecordingLogger();
            var controller = BuildClientControllerForMembership(handler, logger);

            await controller.EnableClientOnConnectionAsync(new FakeTenantApiAccess(), "con_x", "client_x",
                "default", "test-client", CancellationToken.None);

            var successMsg = logger.Entries.Select(e => e.Message)
                .FirstOrDefault(m => m.Contains("\"operation\":\"enable_client_on_connection\"") && m.Contains("succeeded"));
            Assert.IsNotNull(successMsg, "Expected the structured success-log for enable_client_on_connection");

            using var doc = JsonDocument.Parse(successMsg!);
            var root = doc.RootElement;
            Assert.AreEqual("enable_client_on_connection", root.GetProperty("operation").GetString());
            // Drift payload now lives on the pre-write LogAuth0ApiCall entry — must not be duplicated here.
            Assert.IsFalse(root.TryGetProperty("driftFields", out _), "Success log must not duplicate driftFields");
            Assert.IsFalse(root.TryGetProperty("driftReason", out _), "Success log must not duplicate driftReason");
            Assert.IsFalse(root.TryGetProperty("reconciliationType", out _), "Success log must not duplicate reconciliationType");
        }

        [TestMethod]
        public async Task DisableClient_PreWriteLog_GoesThroughLogAuth0ApiCallWriteFunnel()
        {
            var handler = new SingleResponseHandler((_, __) =>
                new HttpResponseMessage(HttpStatusCode.NoContent));
            var logger = new RecordingLogger();
            var controller = BuildClientControllerForMembership(handler, logger);

            await controller.DisableClientOnConnectionAsync(new FakeTenantApiAccess(), "con_y", "client_y",
                "default", "test-client", CancellationToken.None);

            var writeEntry = logger.Entries
                .FirstOrDefault(e => e.Level == LogLevel.Warning && e.Message.Contains("\"auth0ApiCallType\":\"write\""));
            Assert.AreNotEqual(default, writeEntry, "Expected a pre-write Warning entry from LogAuth0ApiCall for the membership change");

            using var doc = JsonDocument.Parse(writeEntry.Message);
            var root = doc.RootElement;
            Assert.AreEqual("disable_client_on_connection", root.GetProperty("auth0ApiCallPurpose").GetString());
            var df = root.GetProperty("driftFields");
            Assert.AreEqual("removed", df[0].GetProperty("changeType").GetString());
        }

        // --- helpers ---

        private static void Invoke(V1ClientController controller, string message, Auth0ApiCallType type,
            string entityType, string entityName, string entityNamespace, string purpose, DriftLogContext? driftContext)
        {
            _logMethod.Invoke(controller, new object?[]
            {
                message, type, entityType, entityName, entityNamespace, purpose, driftContext,
            });
        }

        private static (V1ClientController, RecordingLogger) BuildController()
        {
            var logger = new RecordingLogger();
            return (BuildClientControllerForMembership(
                new SingleResponseHandler((_, __) => new HttpResponseMessage(HttpStatusCode.OK)),
                logger), logger);
        }

        private static V1ClientController BuildClientControllerForMembership(HttpMessageHandler handler, ILogger<V1ClientController> logger)
        {
            var factory = new SingleHandlerHttpClientFactory(handler);
            var kube = System.Reflection.DispatchProxy.Create<IKubernetesClient, ThrowingKubeProxy>();
            return new V1ClientController(
                kube: kube,
                requeue: (EntityRequeue<Alethic.Auth0.Operator.Models.V1Client>)((_, _) => { }),
                cache: new MemoryCache(new MemoryCacheOptions()),
                logger: logger,
                options: Microsoft.Extensions.Options.Options.Create(new OperatorOptions()),
                httpClientFactory: factory);
        }

        internal sealed class RecordingLogger : ILogger<V1ClientController>
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new();
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }

        public class ThrowingKubeProxy : System.Reflection.DispatchProxy
        {
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
                throw new InvalidOperationException(
                    $"IKubernetesClient.{targetMethod?.Name} was unexpectedly invoked.");
        }

        private sealed class SingleHandlerHttpClientFactory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;
            public SingleHandlerHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
            public HttpClient CreateClient(string name) => new HttpClient(_handler, disposeHandler: false);
        }

        private sealed class SingleResponseHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;
            public SingleResponseHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
            {
                _responder = responder;
            }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(_responder(request, cancellationToken));
        }

        private sealed class FakeTenantApiAccess : ITenantApiAccess
        {
            public Uri BaseUri => new Uri("https://example.us.auth0.com/api/v2/");
            public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult("fake-token");
            public Task InvalidateAccessTokenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        }
    }
}
