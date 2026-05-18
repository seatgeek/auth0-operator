using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Controllers;
using Alethic.Auth0.Operator.Core.Models.Client;
using Alethic.Auth0.Operator.Models;
using Alethic.Auth0.Operator.Options;

using Auth0.Core.Exceptions;

using k8s.Autorest;
using k8s.Models;

using KubeOps.Abstractions.Queue;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

namespace Alethic.Auth0.Operator.Tests.Controllers
{
    /// <summary>
    /// Coverage for the hot-fix that targets the status-write 409 race + always-requeue
    /// behaviour added in <c>V1TenantEntityController.UpdateKubernetesStatus</c> and
    /// <c>V1Controller.ReconcileAsync</c>. See:
    /// <list type="bullet">
    /// <item>findings/2026-05-18-status-write-409-loses-side-effect-and-stalls-reconcile.md</item>
    /// <item>plans/2026-05-18__11-00-03 - auth0-operator - status-write-409-and-always-requeue-hotfix.md</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class V1ControllerRetryAndRequeueTests
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

        private static HttpOperationException Make409() =>
            new HttpOperationException("conflict")
            {
                Response = new HttpResponseMessageWrapper(
                    new HttpResponseMessage(HttpStatusCode.Conflict), "{}"),
            };

        // ============================================================================
        // 1. UpdateKubernetesStatus retries on 409 and succeeds — exercises the helper
        //    via a thin test controller derived from V1Controller<V1Client, ...>.
        // ============================================================================

        [TestMethod]
        public async Task UpdateKubernetesStatus_Retries_On_409_And_Succeeds()
        {
            var entity = MakeClient(resourceVersion: "rv-stale");
            var refetched = MakeClient(resourceVersion: "rv-fresh");

            V1Client? capturedSecondArg = null;
            var kube = new Mock<IKubernetesClient>(MockBehavior.Strict);
            // Hand-rolled sequence: 1st call throws 409, 2nd call captures + succeeds.
            // Moq's SetupSequence(...).Returns(...) doesn't expose a lambda overload that
            // captures the invocation arguments, so we drive the sequence via Callback.
            var callCount = 0;
            kube.Setup(k => k.UpdateStatusAsync(It.IsAny<V1Client>(), It.IsAny<CancellationToken>()))
                .Returns((V1Client e, CancellationToken _) =>
                {
                    callCount++;
                    if (callCount == 1)
                        throw Make409();
                    capturedSecondArg = e;
                    return Task.FromResult(e);
                });
            kube.Setup(k => k.GetAsync<V1Client>(entity.Name(), entity.Namespace(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(refetched);

            var controller = new TestController(kube.Object, requeue: (_, __) => { });
            await controller.InvokeUpdateKubernetesStatusAsync(entity, "test", CancellationToken.None);

            // M1: the second invocation must receive the SAME local entity reference the
            // caller passed in (not the refetched object). The retry is "carry resourceVersion
            // forward onto local"; the caller's desired Status object is preserved by reference.
            Assert.IsNotNull(capturedSecondArg);
            Assert.AreSame(entity, capturedSecondArg,
                "Retry must reuse the caller's local entity — never substitute the refetched object.");
            Assert.AreEqual("rv-fresh", capturedSecondArg!.Metadata!.ResourceVersion,
                "Retry must advance the local entity's resourceVersion from the refetched object.");
            Assert.AreSame(entity.Status, capturedSecondArg.Status,
                "Retry must preserve the caller's Status object reference.");
            kube.Verify(k => k.UpdateStatusAsync(It.IsAny<V1Client>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        // ============================================================================
        // 2. UpdateKubernetesStatus exhausts retries → throws original 409.
        // ============================================================================

        [TestMethod]
        public async Task UpdateKubernetesStatus_Throws_After_Retry_Exhaustion()
        {
            var entity = MakeClient();
            var refetched = MakeClient(resourceVersion: "rv-fresh");

            var kube = new Mock<IKubernetesClient>(MockBehavior.Strict);
            kube.Setup(k => k.UpdateStatusAsync(It.IsAny<V1Client>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(Make409());
            kube.Setup(k => k.GetAsync<V1Client>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(refetched);

            var controller = new TestController(kube.Object, requeue: (_, __) => { });

            var ex = await Assert.ThrowsExceptionAsync<HttpOperationException>(() =>
                controller.InvokeUpdateKubernetesStatusAsync(entity, "test", CancellationToken.None));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.Response?.StatusCode);

            // 1 initial attempt + 3 retries = 4 total UpdateStatusAsync calls.
            kube.Verify(k => k.UpdateStatusAsync(It.IsAny<V1Client>(), It.IsAny<CancellationToken>()),
                Times.Exactly(4));
        }

        // ============================================================================
        // 3. ReconcileAsync schedules requeue on generic Exception.
        // ============================================================================

        [TestMethod]
        public async Task ReconcileAsync_Schedules_Requeue_On_Generic_Exception()
        {
            var entity = MakeClient();
            var requeueCalls = new List<(V1Client entity, TimeSpan delay)>();

            var kube = new Mock<IKubernetesClient>(MockBehavior.Loose);
            var controller = new TestController(
                kube.Object,
                requeue: (e, d) => requeueCalls.Add((e, d)),
                reconcileImpl: (_, __) => throw new InvalidOperationException("simulated transient downstream error"));

            await controller.ReconcileAsync(entity, CancellationToken.None);

            Assert.AreEqual(1, requeueCalls.Count, "ReconcileAsync must always schedule a requeue on unexpected exceptions.");
            Assert.AreSame(entity, requeueCalls[0].entity);
            // M3: tighten delay assertion to [22.5s, 37.5s] (30s ±25% jitter).
            var d = requeueCalls[0].delay;
            Assert.IsTrue(d >= TimeSpan.FromSeconds(22.5) && d <= TimeSpan.FromSeconds(37.5),
                $"Requeue delay must be 30s ±25% jitter; got {d}.");
        }

        // ============================================================================
        // 4. ReconcileAsync schedules requeue on ErrorApiException (Auth0 API failure).
        // ============================================================================

        [TestMethod]
        public async Task ReconcileAsync_Schedules_Requeue_On_ErrorApiException()
        {
            var entity = MakeClient();
            var requeueCalls = new List<(V1Client entity, TimeSpan delay)>();

            var kube = new Mock<IKubernetesClient>(MockBehavior.Loose);
            var controller = new TestController(
                kube.Object,
                requeue: (e, d) => requeueCalls.Add((e, d)),
                reconcileImpl: (_, __) => throw new ErrorApiException(
                    HttpStatusCode.BadGateway,
                    new ApiError { Message = "simulated upstream failure" }));

            await controller.ReconcileAsync(entity, CancellationToken.None);

            Assert.AreEqual(1, requeueCalls.Count,
                "ReconcileAsync must schedule a requeue on Auth0 ErrorApiException (prior impl logged and fell through).");
            var d = requeueCalls[0].delay;
            Assert.IsTrue(d >= TimeSpan.FromSeconds(22.5) && d <= TimeSpan.FromSeconds(37.5),
                $"Requeue delay must be 30s ±25% jitter; got {d}.");
        }

        // ============================================================================
        // M2: end-to-end status-write retry-exhaustion → requeue (the original incident).
        // ============================================================================

        [TestMethod]
        public async Task ReconcileAsync_Schedules_Requeue_On_Status_Write_Retry_Exhaustion()
        {
            var entity = MakeClient();
            var requeueCalls = new List<(V1Client entity, TimeSpan delay)>();

            var kube = new Mock<IKubernetesClient>(MockBehavior.Loose);
            // Status writes consistently 409.
            kube.Setup(k => k.UpdateStatusAsync(It.IsAny<V1Client>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(Make409());
            kube.Setup(k => k.GetAsync<V1Client>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(MakeClient(resourceVersion: "rv-fresh"));

            // Reconcile path: call UpdateKubernetesStatus directly to simulate the
            // FinalizeReconciliation → UpdateKubernetesStatus path that triggered the live
            // incident. The retry helper exhausts (4 attempts) and re-throws into the
            // outer catch chain.
            var controller = new TestController(
                kube.Object,
                requeue: (e, d) => requeueCalls.Add((e, d)),
                reconcileImpl: (self, ent) => self.InvokeUpdateKubernetesStatusForReconcile(ent));

            await controller.ReconcileAsync(entity, CancellationToken.None);

            Assert.AreEqual(1, requeueCalls.Count,
                "Retry exhaustion must funnel through the outer catch and produce exactly one Requeue.");
            var d = requeueCalls[0].delay;
            Assert.IsTrue(d >= TimeSpan.FromSeconds(22.5) && d <= TimeSpan.FromSeconds(37.5),
                $"Requeue delay must be 30s ±25% jitter; got {d}.");

            // And confirm the inner helper actually ran the full 4-attempt cycle.
            kube.Verify(k => k.UpdateStatusAsync(It.IsAny<V1Client>(), It.IsAny<CancellationToken>()),
                Times.Exactly(4));
        }

        // ============================================================================
        // L2: ComputeKubeConflictRetryDelayMs clamps gracefully when attempt > table size.
        // ============================================================================

        [TestMethod]
        public void ComputeKubeConflictRetryDelayMs_ClampsWhenAttemptExceedsTable()
        {
            // Table has 3 entries (100, 400, 1600 ms). Any attempt > 3 must clamp to the
            // last entry and stay within ±25% jitter — i.e. [1200, 2000] ms.
            for (int attempt = 4; attempt <= 10; attempt++)
            {
                var d = ReflectionAccessor.ComputeKubeConflictRetryDelayMs(attempt);
                Assert.IsTrue(d >= 1200 && d <= 2000,
                    $"attempt={attempt}: expected delay clamped to ~1600ms ±25%; got {d}ms.");
            }

            // Attempt <= 0 must coerce to attempt=1 (~100ms ±25% → [75, 125] ms).
            for (int attempt = -2; attempt <= 1; attempt++)
            {
                var d = ReflectionAccessor.ComputeKubeConflictRetryDelayMs(attempt);
                Assert.IsTrue(d >= 75 && d <= 125,
                    $"attempt={attempt}: expected delay coerced to attempt=1 (~100ms ±25%); got {d}ms.");
            }
        }

        // ---- Test scaffolding ----

        /// <summary>
        /// Reflection bridge to the internal static <c>ComputeKubeConflictRetryDelayMs</c>
        /// on the open-generic <c>V1TenantEntityController&lt;,,,&gt;</c>. Walks the
        /// concrete <c>TestController</c>'s base type chain to find the closed generic
        /// that exposes the method.
        /// </summary>
        private static class ReflectionAccessor
        {
            private static readonly System.Reflection.MethodInfo _method =
                typeof(TestController).BaseType! // V1TenantEntityController<V1Client,...>
                    .GetMethod("ComputeKubeConflictRetryDelayMs",
                        System.Reflection.BindingFlags.Static |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public)
                ?? throw new InvalidOperationException("ComputeKubeConflictRetryDelayMs not found");

            public static int ComputeKubeConflictRetryDelayMs(int attempt) =>
                (int)_method.Invoke(null, new object[] { attempt })!;
        }

        /// <summary>
        /// Thin test-only controller derived from <see cref="V1TenantEntityController{,,,}"/>
        /// closed over <see cref="V1Client"/>. Lets tests:
        /// <list type="bullet">
        /// <item>invoke the protected <c>UpdateKubernetesStatus</c> helper directly;</item>
        /// <item>override the abstract <c>Reconcile</c> method with a per-test delegate so we
        /// can drive the catch chain in <c>V1Controller.ReconcileAsync</c> deterministically.</item>
        /// </list>
        /// </summary>
        private sealed class TestController : V1TenantEntityController<V1Client, V1Client.SpecDef, V1Client.StatusDef, ClientConf>
        {
            private readonly Func<TestController, V1Client, Task>? _reconcileImpl;

            public TestController(
                IKubernetesClient kube,
                EntityRequeue<V1Client> requeue,
                Func<TestController, V1Client, Task>? reconcileImpl = null)
                : base(
                    kube,
                    requeue,
                    new MemoryCache(new MemoryCacheOptions()),
                    NullLogger<TestController>.Instance,
                    global::Microsoft.Extensions.Options.Options.Create(new OperatorOptions()))
            {
                _reconcileImpl = reconcileImpl;
            }

            protected override string EntityTypeName => "A0Client";

            // -- Abstract pass-throughs (test never exercises Auth0 path). --

            protected override Task<System.Collections.Hashtable?> Get(global::Auth0.ManagementApi.IManagementApiClient api, string id, string defaultNamespace, CancellationToken cancellationToken)
                => Task.FromResult<System.Collections.Hashtable?>(null);

            protected override Task<string?> Find(global::Auth0.ManagementApi.IManagementApiClient api, V1Client entity, V1Client.SpecDef spec, string defaultNamespace, CancellationToken cancellationToken)
                => Task.FromResult<string?>(null);

            protected override string? ValidateCreate(ClientConf conf) => null;

            protected override Task<string> Create(global::Auth0.ManagementApi.IManagementApiClient api, ClientConf conf, string defaultNamespace, CancellationToken cancellationToken)
                => Task.FromResult(string.Empty);

            protected override Task Update(global::Auth0.ManagementApi.IManagementApiClient api, string id, System.Collections.Hashtable? last, ClientConf conf, IReadOnlyList<DriftField> driftFields, string defaultNamespace, Alethic.Auth0.Operator.Services.ITenantApiAccess tenantApiAccess, DriftLogContext driftContext, CancellationToken cancellationToken)
                => Task.CompletedTask;

            protected override Task Delete(global::Auth0.ManagementApi.IManagementApiClient api, string id, CancellationToken cancellationToken)
                => Task.CompletedTask;

            // -- Reconcile override: by default no-op; tests inject custom behaviour. --

            protected override async Task<(bool needsRequeue, V1Client updatedEntity)> Reconcile(V1Client entity, CancellationToken cancellationToken)
            {
                if (_reconcileImpl is not null)
                    await _reconcileImpl(this, entity);
                return (false, entity);
            }

            // -- Test hooks. --

            public Task<V1Client> InvokeUpdateKubernetesStatusAsync(V1Client entity, string operation, CancellationToken ct)
                => UpdateKubernetesStatus(entity, operation, ct);

            /// <summary>
            /// Used by the end-to-end status-write-exhaustion test: simulates the production
            /// FinalizeReconciliation path's call to UpdateKubernetesStatus from inside
            /// the Reconcile method body.
            /// </summary>
            public async Task InvokeUpdateKubernetesStatusForReconcile(V1Client entity)
            {
                await UpdateKubernetesStatus(entity, "test_finalize_reconciliation", CancellationToken.None);
            }
        }
    }
}
