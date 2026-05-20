using System;

using Alethic.Auth0.Operator.Controllers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Auth0.Operator.Tests.Controllers
{
    /// <summary>
    /// Locks the <see cref="DriftField.EnsureNoSecretShape"/> guard contract against the recurring
    /// false-positive that fired on the <c>waad</c>-connection enum value
    /// <c>token_endpoint_auth_method</c>. The guard substring-matches on <c>"client_secret"</c> to
    /// catch unredacted secrets, but the same substring is a valid OAuth enum value
    /// (<c>client_secret_post</c>/<c>_basic</c>/<c>_jwt</c>). The fix allowlists the *exact quoted*
    /// rendered form so user-controlled string fields that merely contain the same substring still
    /// trip the guard (we'd rather fail loud than under-redact).
    /// </summary>
    [TestClass]
    public class DriftFieldTests
    {
        [TestMethod]
        public void Constructor_AllowsTokenEndpointAuthMethodEnumValues()
        {
            // Both quoted enum values must construct without throwing — this is the production
            // false-positive on avfc-azuread-test-eu that we're unblocking.
            var field = new DriftField(
                "token_endpoint_auth_method",
                DriftChangeType.Modified,
                "\"client_secret_basic\"",
                "\"client_secret_post\"");

            Assert.AreEqual("token_endpoint_auth_method", field.FieldPath);
            Assert.AreEqual("\"client_secret_basic\"", field.BeforeValue);
            Assert.AreEqual("\"client_secret_post\"", field.AfterValue);
        }

        [TestMethod]
        public void Constructor_AllowsClientSecretJwtEnumValue()
        {
            // client_secret_jwt is not currently in our TokenEndpointAuthMethod enum but is a
            // valid Auth0-returned value (OIDC core spec). Pre-emptive allowlist.
            var field = new DriftField(
                "token_endpoint_auth_method",
                DriftChangeType.Modified,
                "\"client_secret_basic\"",
                "\"client_secret_jwt\"");

            Assert.AreEqual("\"client_secret_jwt\"", field.AfterValue);
        }

        [TestMethod]
        public void Constructor_StillThrowsOnUnredactedHashtableRendering()
        {
            // The "real miss" case the guard is meant to catch: a hashtable rendering that contains
            // an unredacted client_secret key. Not in the allowlist (different shape — no surrounding
            // quotes wrapping just the enum string). Must still throw.
            Assert.ThrowsException<InvalidOperationException>(() =>
                new DriftField(
                    "options",
                    DriftChangeType.Modified,
                    null,
                    "{client_secret: redacted, something: else}"));
        }

        [TestMethod]
        public void Constructor_StillThrowsOnUserStringContainingMarker()
        {
            // A user-controlled string field (e.g. description) whose value happens to contain the
            // literal marker as a substring must still throw — we'd rather fail loud on a
            // misclassified user-string than under-redact. The allowlist matches exact quoted
            // values only, so an embedded marker inside a longer quoted string trips the guard.
            Assert.ThrowsException<InvalidOperationException>(() =>
                new DriftField(
                    "description",
                    DriftChangeType.Added,
                    null,
                    "\"users.client_secret_post is bad\""));
        }
    }
}
