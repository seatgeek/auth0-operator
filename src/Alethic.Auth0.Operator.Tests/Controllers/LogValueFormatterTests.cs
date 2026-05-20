using System.Collections;
using System.Collections.Generic;

using Alethic.Auth0.Operator.Controllers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Alethic.Auth0.Operator.Tests.Controllers
{
    /// <summary>
    /// Locks the materialize-once contract on
    /// <see cref="LogValueFormatter.FormatValueForLogging"/> for the <c>IEnumerable</c> branch.
    /// Previous implementation called <c>.Take(5).Select(...)</c> and <c>.Count()</c> on the same
    /// source, which re-enumerates lazy / one-shot enumerables. Regression-locked via
    /// <see cref="OneShotEnumerable"/>, which throws if its <c>GetEnumerator()</c> is called twice.
    /// </summary>
    [TestClass]
    public class LogValueFormatterTests
    {
        [TestMethod]
        public void FormatValueForLogging_IEnumerable_EnumeratesSourceOnlyOnce()
        {
            var source = new OneShotEnumerable(new object[] { "a", "b", "c" });

            var result = LogValueFormatter.FormatValueForLogging(source);

            Assert.AreEqual("[\"a\", \"b\", \"c\"]", result);
        }

        [TestMethod]
        public void FormatValueForLogging_IEnumerable_OverFiveItems_EnumeratesSourceOnlyOnce()
        {
            var source = new OneShotEnumerable(new object[] { 1, 2, 3, 4, 5, 6, 7 });

            var result = LogValueFormatter.FormatValueForLogging(source);

            StringAssert.Contains(result, "(total: 7 items)");
        }

        // R3 regression coverage: the IsSensitiveKey "token" rule must match OAuth secret tokens
        // (access_token / refresh_token / id_token) without over-matching legitimate non-secret
        // fields whose names start with "token_" (token_endpoint, token_endpoint_auth_method) or
        // contain "_token_" as a non-secret segment (access_token_lifetime_in_seconds).
        [DataTestMethod]
        [DataRow("access_token")]
        [DataRow("refresh_token")]
        [DataRow("id_token")]
        [DataRow("client_assertion")]
        [DataRow("client_assertion_signing_key")]
        [DataRow("signing_key")]
        [DataRow("certificate")]
        [DataRow("pfx")]
        // H1 / MR !3 rufus review — pfx must match as a complete _/.- bounded segment, covering
        // leading, middle, trailing, and whole-key positions.
        [DataRow("cert.pfx")]
        [DataRow("pfx_data")]
        [DataRow("oauth.pfx_blob")]
        public void IsSensitiveKey_RedactsSecretShapedKey(string key)
        {
            Assert.IsTrue(LogValueFormatter.IsSensitiveKey(key),
                $"Expected IsSensitiveKey(\"{key}\") to be true — secret-shaped key must redact.");
        }

        [DataTestMethod]
        [DataRow("token_endpoint")]
        [DataRow("token_endpoint_auth_method")]
        [DataRow("access_token_lifetime_in_seconds")]
        // H1 / MR !3 rufus review — keys whose name merely contains the three letters "pfx" as
        // a non-boundary substring must NOT redact (they're not pkcs12 blobs).
        [DataRow("prefix_url")]
        [DataRow("spfx_config")]
        public void IsSensitiveKey_DoesNotRedactNonSecretTokenNamedKey(string key)
        {
            Assert.IsFalse(LogValueFormatter.IsSensitiveKey(key),
                $"Expected IsSensitiveKey(\"{key}\") to be false — non-secret config key must not redact.");
        }

        /// <summary>
        /// Single-pass <see cref="IEnumerable"/> test helper. Throws on the second
        /// <see cref="GetEnumerator"/> call so any double-enumeration in production code fails fast.
        /// </summary>
        private sealed class OneShotEnumerable : IEnumerable
        {
            private readonly IEnumerable _inner;
            private bool _enumerated;

            public OneShotEnumerable(IEnumerable inner)
            {
                _inner = inner;
            }

            public IEnumerator GetEnumerator()
            {
                if (_enumerated)
                    Assert.Fail("OneShotEnumerable enumerated more than once — caller is double-enumerating the source.");
                _enumerated = true;
                return _inner.GetEnumerator();
            }
        }
    }
}
