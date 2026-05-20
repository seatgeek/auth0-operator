using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;

using Alethic.Auth0.Operator.Controllers;
using Alethic.Auth0.Operator.Models;
using Alethic.Auth0.Operator.Options;
using Alethic.Auth0.Operator.Tests.TestSupport;

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
    /// Regression guard for F7 / LA-RF-81: the connection write paths must thread the real
    /// <c>entity.Namespace()</c> (passed through as <c>defaultNamespace</c>) into the
    /// <c>LogAuth0Write</c> funnel — not the hard-coded <c>"unknown"</c> literal that
    /// previously caused every Datadog <c>auth0ApiCallPurpose:"update_connection"</c> /
    /// <c>"reset_connection_metadata"</c> log line to render <c>entityNamespace:"unknown"</c>.
    /// <para>
    /// M3 (LA-RF-81 code review) replaced the previous source-text regex with this behavioural
    /// test. We invoke the protected <c>LogAuth0Write</c> directly via reflection on a real
    /// <see cref="V1ConnectionController"/> instance — same funnel the production
    /// <c>update_connection</c> and <c>reset_connection_metadata</c> call sites use — and
    /// assert the captured JSON payload carries the namespace we passed in.
    /// </para>
    /// </summary>
    [TestClass]
    public class V1ConnectionControllerWriteLogTests
    {
        [TestMethod]
        public void LogAuth0Write_PassesEntityNamespaceThroughForUpdateConnection()
            => AssertLogAuth0WriteRoutesNamespace(purpose: "update_connection", expectedNamespace: "team-alpha");

        [TestMethod]
        public void LogAuth0Write_PassesEntityNamespaceThroughForResetConnectionMetadata()
            => AssertLogAuth0WriteRoutesNamespace(purpose: "reset_connection_metadata", expectedNamespace: "team-bravo");

        private static void AssertLogAuth0WriteRoutesNamespace(string purpose, string expectedNamespace)
        {
            var capturingLogger = new CapturingLogger();
            var controller = BuildController(capturingLogger);

            // LogAuth0Write is protected on V1Controller<TEntity,TSpec,TStatus,TConf>.
            // Reflect onto its closed-generic base to invoke it on the V1ConnectionController
            // instance — exactly the same funnel the production update path uses.
            var method = FindLogAuth0Write(controller.GetType());
            Assert.IsNotNull(method, "LogAuth0Write(message, entityType, entityName, entityNamespace, purpose, driftContext) not found on V1ConnectionController.");

            var driftContext = DriftLogContext.FirstReconciliation();
            method!.Invoke(controller, new object?[]
            {
                "Updating Auth0 connection with ID: cn_test",
                "A0Connection",
                "test-connection",
                expectedNamespace,
                purpose,
                driftContext,
            });

            var jsonEntry = capturingLogger.Entries
                .Where(e => e.Level == LogLevel.Warning) // LogAuth0Write emits at Warning
                .Select(e => TryParseJson(e.Message))
                .FirstOrDefault(d => d is not null
                                     && d.RootElement.TryGetProperty("auth0ApiCallPurpose", out var p)
                                     && p.GetString() == purpose);

            Assert.IsNotNull(jsonEntry, $"Expected a structured Warning log carrying auth0ApiCallPurpose={purpose}.");

            var root = jsonEntry!.RootElement;
            Assert.IsTrue(root.TryGetProperty("entityNamespace", out var nsProp),
                $"LogAuth0Write payload for purpose={purpose} must include `entityNamespace`.");
            Assert.AreEqual(expectedNamespace, nsProp.GetString(),
                $"LogAuth0Write for purpose={purpose} must thread `defaultNamespace` through to `entityNamespace`. " +
                $"Got '{nsProp.GetString()}', expected '{expectedNamespace}'. " +
                "The hard-coded \"unknown\" literal previously caused entityNamespace to render as \"unknown\" " +
                "on every Datadog log line for these purposes.");
            Assert.AreEqual("A0Connection", root.GetProperty("entityTypeName").GetString());
        }

        private static MethodInfo? FindLogAuth0Write(Type startType)
        {
            for (var t = startType; t != null; t = t.BaseType)
            {
                var method = t.GetMethod(
                    "LogAuth0Write",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (method is not null)
                    return method;
            }
            return null;
        }

        private static V1ConnectionController BuildController(ILogger logger)
        {
            var kube = new Mock<IKubernetesClient>(MockBehavior.Loose).Object;
            EntityRequeue<V1Connection> requeue = (_, __) => { };
            var cache = new MemoryCache(new MemoryCacheOptions());
            var options = Microsoft.Extensions.Options.Options.Create(new OperatorOptions());
            return new V1ConnectionController(
                kube,
                requeue,
                cache,
                new TypedLoggerAdapter<V1ConnectionController>(logger),
                options);
        }

        private static JsonDocument? TryParseJson(string message)
        {
            try { return JsonDocument.Parse(message); }
            catch { return null; }
        }
    }
}
