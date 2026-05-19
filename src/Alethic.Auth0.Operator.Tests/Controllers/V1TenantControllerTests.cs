using System.Linq;
using System.Reflection;

using Alethic.Auth0.Operator.Controllers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Auth0.Operator.Tests.Controllers
{
    /// <summary>
    /// Regression guard for F1 / LA-RF-81: A0Tenant drift detection must NOT include
    /// <c>sandbox_versions_available</c>, which is an Auth0-managed read-only catalog field that
    /// Auth0 mutates independently of operator writes. Including it caused spurious
    /// "DRIFT DETECTED" events to fire on ~295 A0Tenant reconciles over a 7-day Datadog window,
    /// triggering full-config PATCHes that contributed to insufficient_scope chains downstream.
    /// <para>
    /// The test reflects on <c>V1TenantController.GetIncludedFields()</c> directly. Because the
    /// upstream filter <c>FilterFieldsForComparison</c> keeps only keys whose name is in
    /// <c>GetIncludedFields()</c>, a field absent from that list mathematically cannot appear in
    /// the resulting <c>driftFields</c> list — so this reflection-level assertion is functionally
    /// equivalent to (and tighter than) driving <c>HasConfigurationChanged</c> end-to-end.
    /// </para>
    /// <para>
    /// This test must pass on this branch and MUST FAIL if <c>sandbox_versions_available</c> is
    /// re-added to <see cref="V1TenantController.GetIncludedFields"/>.
    /// </para>
    /// </summary>
    [TestClass]
    public class V1TenantControllerTests
    {
        [TestMethod]
        public void GetIncludedFields_DoesNotIncludeSandboxVersionsAvailable()
        {
            var includedFields = InvokeGetIncludedFields();

            CollectionAssert.DoesNotContain(
                includedFields,
                "sandbox_versions_available",
                "sandbox_versions_available is an Auth0-managed read-only catalog field that mutates " +
                "independently of operator writes. Including it in drift detection produces spurious " +
                "DRIFT DETECTED events. If this assertion fails, see the LA-RF-81 plan and the " +
                "Datadog investigation that motivated its removal before re-adding it.");
        }

        [TestMethod]
        public void GetIncludedFields_StillIncludesSandboxVersion()
        {
            // sandbox_version (the singular, writable spec field) MUST remain in drift detection.
            // It's the user-controllable choice between the available sandbox runtime versions,
            // and the operator owns reconciling it to spec.
            var includedFields = InvokeGetIncludedFields();

            CollectionAssert.Contains(
                includedFields,
                "sandbox_version",
                "sandbox_version (singular, writable) is the user-controllable spec field; " +
                "it must remain in drift detection. Don't conflate it with the read-only " +
                "sandbox_versions_available catalog field.");
        }

        private static string[] InvokeGetIncludedFields()
        {
            // Construct a default V1TenantController via a parameter-less helper would require
            // a full DI graph. We only need the method's *return value*, which is a pure function
            // of the type — reflect on the protected instance method via an uninitialized
            // instance, which is sufficient because GetIncludedFields has no state dependencies.
            var controller = (V1TenantController)System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(typeof(V1TenantController));

            var method = typeof(V1TenantController).GetMethod(
                "GetIncludedFields",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "V1TenantController.GetIncludedFields not found via reflection.");

            return (string[])method!.Invoke(controller, null)!;
        }
    }
}
