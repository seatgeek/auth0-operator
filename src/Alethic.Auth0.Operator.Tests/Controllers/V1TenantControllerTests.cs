using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Alethic.Auth0.Operator.Controllers;
using Alethic.Auth0.Operator.Core.Models.Tenant;
using Alethic.Auth0.Operator.Models;
using Alethic.Auth0.Operator.Options;

using KubeOps.Abstractions.Queue;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

namespace Alethic.Auth0.Operator.Tests.Controllers
{
    /// <summary>
    /// Regression guard for F1 / LA-RF-81: A0Tenant drift detection must NOT include
    /// <c>sandbox_versions_available</c>, an Auth0-managed read-only catalog field that
    /// Auth0 mutates independently of operator writes. Including it caused spurious
    /// "DRIFT DETECTED" events on ~295 A0Tenant reconciles over a 7-day Datadog window,
    /// triggering full-config PATCHes that contributed to insufficient_scope chains downstream.
    /// <para>
    /// The tests below drive the controller end-to-end through its private
    /// <c>HasConfigurationChanged</c> method via reflection on a properly-constructed
    /// instance (M2 / LA-RF-81 code review). This is functionally equivalent to a public
    /// API call and replaces the previous brittle <c>RuntimeHelpers.GetUninitializedObject</c>
    /// pattern, which depended on <c>GetIncludedFields</c> having no state — true today,
    /// silently breakable tomorrow.
    /// </para>
    /// </summary>
    [TestClass]
    public class V1TenantControllerTests
    {
        // ============================================================================
        // 1. Two TenantConfs differing only in SandboxVersionsAvailable are equivalent
        //    from HasConfigurationChanged's point of view — i.e. drift NOT detected.
        // ============================================================================

        [TestMethod]
        public void HasConfigurationChanged_TwoTenantConfsDifferingOnlyInSandboxVersionsAvailable_ReturnsFalse()
        {
            var controller = BuildController();

            // The `lastConf` Hashtable simulates what Auth0 returned (transformed through
            // TransformToSystemTextJson<Hashtable>). Both confs have identical sandbox_version
            // (the writable field). Only sandbox_versions_available differs — that's the
            // Auth0-managed read-only catalog field that must be filtered out by GetIncludedFields.
            var lastConf = new Hashtable
            {
                ["friendly_name"] = "Acme",
                ["sandbox_version"] = "18",
                ["sandbox_versions_available"] = new object[] { "16", "18" },
            };

            var desiredConf = new TenantConf
            {
                FriendlyName = "Acme",
                SandboxVersion = "18",
                SandboxVersionsAvailable = new[] { "16", "18", "20" }, // <-- differs
            };

            var (changed, driftFields) = InvokeHasConfigurationChanged(controller, lastConf, desiredConf);

            Assert.IsFalse(changed,
                $"HasConfigurationChanged must return false when the two configs differ only in " +
                $"sandbox_versions_available (an Auth0-managed read-only field). Drift fields: " +
                $"[{string.Join(", ", driftFields.Select(f => f.FieldPath))}]");
            Assert.AreEqual(0, driftFields.Count,
                $"Drift list must be empty; got [{string.Join(", ", driftFields.Select(f => f.FieldPath))}].");
        }

        // ============================================================================
        // 2. sandbox_version (singular) drift IS detected — sanity-check that we didn't
        //    accidentally over-filter and wipe out the writable sibling field.
        // ============================================================================

        [TestMethod]
        public void HasConfigurationChanged_TwoTenantConfsDifferingOnlyInSandboxVersion_ReturnsTrue()
        {
            var controller = BuildController();

            var lastConf = new Hashtable
            {
                ["friendly_name"] = "Acme",
                ["sandbox_version"] = "16",
            };

            var desiredConf = new TenantConf
            {
                FriendlyName = "Acme",
                SandboxVersion = "18", // <-- differs
            };

            var (changed, driftFields) = InvokeHasConfigurationChanged(controller, lastConf, desiredConf);

            Assert.IsTrue(changed,
                "HasConfigurationChanged must detect drift on the user-writable sandbox_version " +
                "field; otherwise the user's spec value can never be reconciled back to Auth0.");
            Assert.IsTrue(driftFields.Any(f => f.FieldPath == "sandbox_version"),
                $"Expected sandbox_version in drift list; got [{string.Join(", ", driftFields.Select(f => f.FieldPath))}].");
        }

        // ---- Test scaffolding ----

        private static V1TenantController BuildController()
        {
            var kube = new Mock<IKubernetesClient>(MockBehavior.Loose).Object;
            EntityRequeue<V1Tenant> requeue = (_, __) => { };
            var cache = new MemoryCache(new MemoryCacheOptions());
            var options = Microsoft.Extensions.Options.Options.Create(new OperatorOptions());
            return new V1TenantController(
                kube,
                requeue,
                cache,
                NullLogger<V1TenantController>.Instance,
                options);
        }

        /// <summary>
        /// Calls the private <c>HasConfigurationChanged(V1Tenant, Hashtable?, TenantConf, out List&lt;DriftField&gt;)</c>
        /// via reflection. Reflection is used here only because the production method is
        /// private; the test exercises real instance state (logger, included-field filter,
        /// hashtable normalization, drift computation) end-to-end.
        /// </summary>
        private static (bool changed, List<DriftField> driftFields) InvokeHasConfigurationChanged(
            V1TenantController controller, Hashtable? lastConf, TenantConf desiredConf)
        {
            var method = typeof(V1TenantController).GetMethod(
                "HasConfigurationChanged",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "V1TenantController.HasConfigurationChanged(V1Tenant, Hashtable?, TenantConf, out List<DriftField>) not found via reflection.");

            var entity = new V1Tenant
            {
                Metadata = new k8s.Models.V1ObjectMeta { Name = "t1", NamespaceProperty = "default" },
                Spec = new V1Tenant.SpecDef { Conf = desiredConf },
                Status = new V1Tenant.StatusDef(),
            };

            var args = new object?[] { entity, lastConf, desiredConf, null };
            var result = (bool)method!.Invoke(controller, args)!;
            var drift = (List<DriftField>)args[3]!;
            return (result, drift);
        }
    }
}
