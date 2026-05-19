using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Auth0.Operator.Tests.Controllers
{
    /// <summary>
    /// Regression guard for F7 / LA-RF-81: the connection-update write path must thread the real
    /// <c>entity.Namespace()</c> through to the <c>LogAuth0Write</c> call — not the hard-coded
    /// <c>"unknown"</c> literal that previously caused every Datadog
    /// <c>auth0ApiCallPurpose:"update_connection"</c> log to render <c>entityNamespace:"unknown"</c>.
    /// <para>
    /// The <c>Update</c> method receives <c>defaultNamespace</c> from
    /// <see cref="V1TenantEntityController{TEntity,TConf}.PerformUpdate"/>, which passes
    /// <c>entity.Namespace()</c>. Asserting at the call-site source-text level (rather than via
    /// SDK mocking) is the lightest-weight guard that survives signature refactors and catches
    /// future copy-paste regressions of the literal.
    /// </para>
    /// </summary>
    [TestClass]
    public class V1ConnectionControllerWriteLogTests
    {
        private static string ReadConnectionControllerSource()
        {
            // The test runs from the test assembly's bin dir; walk up to repo root, then into the
            // operator project. This keeps the test self-contained and avoids new test-data plumbing.
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Alethic.Auth0.Operator.sln")))
                dir = dir.Parent;

            Assert.IsNotNull(dir, "Could not locate repo root (Alethic.Auth0.Operator.sln) by walking up from test bin dir.");

            var sourcePath = Path.Combine(
                dir!.FullName,
                "src", "Alethic.Auth0.Operator", "Controllers", "V1ConnectionController.cs");
            Assert.IsTrue(File.Exists(sourcePath), $"Expected source file at {sourcePath}.");

            return File.ReadAllText(sourcePath);
        }

        [TestMethod]
        public void UpdateConnection_LogAuth0WriteCall_PassesDefaultNamespace_NotLiteralUnknown()
        {
            var source = ReadConnectionControllerSource();

            // Match the LogAuth0Write call for update_connection. The signature is:
            //   LogAuth0Write(message, entityType, entityName, entityNamespace, purpose, driftContext)
            // We assert the 4th positional argument (entityNamespace) is `defaultNamespace`, and
            // the 5th is the literal "update_connection".
            var pattern = new Regex(
                @"LogAuth0Write\([^,]+,\s*""A0Connection""\s*,\s*[^,]+,\s*(?<ns>[^,]+),\s*""update_connection""",
                RegexOptions.Singleline);

            var matches = pattern.Matches(source).Cast<Match>().ToList();
            Assert.AreEqual(1, matches.Count, "Expected exactly one LogAuth0Write call for update_connection.");

            var ns = matches[0].Groups["ns"].Value.Trim();
            Assert.AreEqual("defaultNamespace", ns,
                $"LogAuth0Write for update_connection must pass defaultNamespace, got '{ns}'. " +
                "The hard-coded \"unknown\" literal caused entityNamespace to render as \"unknown\" on every " +
                "Datadog auth0ApiCallPurpose:\"update_connection\" log line.");
        }

        [TestMethod]
        public void ResetConnectionMetadata_LogAuth0WriteCall_PassesDefaultNamespace_NotLiteralUnknown()
        {
            var source = ReadConnectionControllerSource();

            var pattern = new Regex(
                @"LogAuth0Write\([^,]+,\s*""A0Connection""\s*,\s*[^,]+,\s*(?<ns>[^,]+),\s*""reset_connection_metadata""",
                RegexOptions.Singleline);

            var matches = pattern.Matches(source).Cast<Match>().ToList();
            Assert.AreEqual(1, matches.Count, "Expected exactly one LogAuth0Write call for reset_connection_metadata.");

            var ns = matches[0].Groups["ns"].Value.Trim();
            Assert.AreEqual("defaultNamespace", ns,
                $"LogAuth0Write for reset_connection_metadata must pass defaultNamespace, got '{ns}'.");
        }
    }
}
