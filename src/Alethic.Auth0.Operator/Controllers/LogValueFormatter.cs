using System;
using System.Collections;
using System.Linq;

namespace Alethic.Auth0.Operator.Controllers
{
    /// <summary>
    /// Shared value formatter for drift-log payloads. Promoted from the per-controller copies
    /// in <c>V1TenantController</c> and <c>V1TenantEntityController</c> so every Auth0 write log
    /// truncates identically. Caps strings at 100 chars, hashtables at 10 entries, arrays at 5
    /// items — bounded enough that no single drift entry blows past Datadog's per-line budget.
    /// <para>
    /// Sensitive-key redaction: when walking a <see cref="Hashtable"/>, any entry whose key
    /// matches <see cref="IsSensitiveKey"/> (database/AD/LDAP/SAML/OAuth secrets, signing keys,
    /// kerberos blobs, etc.) is replaced with <c>(redacted)</c> before its value is serialized.
    /// This keeps the structured drift-log JSON from shipping Auth0 secrets to Datadog when a
    /// connection's <c>options</c> hashtable drifts.
    /// </para>
    /// </summary>
    internal static class LogValueFormatter
    {
        /// <summary>
        /// Placeholder written into the drift-log payload in place of a sensitive value.
        /// Kept identical to what the <see cref="DriftField"/> constructor guard expects so a
        /// regression that skips redaction trips the guard instead of leaking the secret.
        /// </summary>
        internal const string RedactedPlaceholder = "(redacted)";

        /// <summary>
        /// Returns true if <paramref name="key"/> names an Auth0 / connection-options field whose
        /// value is sensitive (a secret, credential, signing key, etc.) and must never be written
        /// to the structured drift-log payload. Matching is case-insensitive substring matching —
        /// covers <c>client_secret</c>, <c>bind_credentials</c>, <c>password</c>, <c>api_secret</c>,
        /// <c>signing_cert</c>, <c>signing_key</c>, <c>kerberos</c>, <c>private_key</c>,
        /// <c>api_key</c>, <c>client_assertion</c>, <c>certificate</c>, <c>pfx</c>, plus the three
        /// OAuth token fields (<c>access_token</c>, <c>refresh_token</c>, <c>id_token</c>) when
        /// they appear as the trailing <c>_</c>- or <c>.</c>-bounded segment of the key. The
        /// token rule is intentionally narrow — a plain <c>"token"</c> substring would over-match
        /// legitimate non-secret fields like <c>token_endpoint</c>,
        /// <c>token_endpoint_auth_method</c>, and <c>access_token_lifetime_in_seconds</c>.
        /// False-positives (e.g. a user-facing description that happens to contain the word
        /// <c>password</c>) are acceptable; false-negatives are not.
        /// </summary>
        public static bool IsSensitiveKey(string? key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            // Lowercase once; substring matching keeps the rule resilient to nested-key prefixes
            // (e.g. "kerberos.principal", "ldap.bind_password", "saml.signing_cert",
            // "oauth.access_token").
            var lower = key.ToLowerInvariant();
            return lower.Contains("secret", StringComparison.Ordinal)
                || lower.Contains("password", StringComparison.Ordinal)
                || lower.Contains("credential", StringComparison.Ordinal)
                || lower.Contains("signing_cert", StringComparison.Ordinal)
                || lower.Contains("signing_key", StringComparison.Ordinal)
                || lower.Contains("kerberos", StringComparison.Ordinal)
                || lower.Contains("private_key", StringComparison.Ordinal)
                || lower.Contains("api_key", StringComparison.Ordinal)
                || lower.Contains("client_assertion", StringComparison.Ordinal)
                || lower.Contains("certificate", StringComparison.Ordinal)
                || lower.Contains("pfx", StringComparison.Ordinal)
                || IsOAuthTokenKey(lower);
        }

        /// <summary>
        /// Narrow OAuth-token matcher: the three secret-bearing token names
        /// (<c>access_token</c>, <c>refresh_token</c>, <c>id_token</c>) match only when they
        /// appear as the trailing <c>_</c>-bounded segment of the key (or are the entire key).
        /// This keeps nested keys like <c>oauth.access_token</c> redacted while letting
        /// non-secret config keys like <c>token_endpoint</c>, <c>token_endpoint_auth_method</c>,
        /// and <c>access_token_lifetime_in_seconds</c> through unredacted.
        /// </summary>
        private static bool IsOAuthTokenKey(string lower)
        {
            return EndsWithSegment(lower, "access_token")
                || EndsWithSegment(lower, "refresh_token")
                || EndsWithSegment(lower, "id_token");
        }

        /// <summary>
        /// Returns true when <paramref name="key"/> equals <paramref name="segment"/> or ends
        /// with <c>{anything}{separator}{segment}</c> where separator is <c>_</c> or <c>.</c>.
        /// </summary>
        private static bool EndsWithSegment(string key, string segment)
        {
            if (key.Length < segment.Length)
                return false;
            if (key.Length == segment.Length)
                return key.Equals(segment, StringComparison.Ordinal);
            if (!key.EndsWith(segment, StringComparison.Ordinal))
                return false;
            var sep = key[key.Length - segment.Length - 1];
            return sep == '_' || sep == '.';
        }

        public static string FormatValueForLogging(object? value)
        {
            if (value is null)
                return "(null)";

            // IMPORTANT: Check Hashtable before IEnumerable since Hashtable implements IEnumerable.
            switch (value)
            {
                case string stringValue:
                    var quotedStr = $"\"{stringValue}\"";
                    return quotedStr.Length > 100 ? $"\"{stringValue[..95]}...\"" : quotedStr;

                case bool boolean:
                    return boolean.ToString().ToLowerInvariant();

                case int or long or float or double or decimal:
                    return value.ToString() ?? "(null)";

                case Hashtable hashtable:
                    var entries = hashtable.Cast<DictionaryEntry>().Take(10)
                        .Select(entry => IsSensitiveKey(entry.Key?.ToString())
                            ? $"{entry.Key}: {RedactedPlaceholder}"
                            : $"{entry.Key}: {FormatValueForLogging(entry.Value)}");
                    var hashPreview = string.Join(", ", entries);
                    return hashtable.Count > 10
                        ? $"{{{hashPreview}, ...}} (total: {hashtable.Count} fields)"
                        : $"{{{hashPreview}}}";

                case IEnumerable enumerable when value is not string:
                    // Materialize once: every IEnumerable reaching this formatter is a bounded
                    // Auth0 SDK DTO collection (tens of entries, never thousands), so .ToList() is
                    // safe and necessary — the previous double-enumeration pattern (Take + Count)
                    // would re-execute lazy / one-shot enumerables or throw on the second pass.
                    var materialized = enumerable.Cast<object>().ToList();
                    var items = materialized.Take(5).Select(FormatValueForLogging);
                    var arrayPreview = string.Join(", ", items);
                    return materialized.Count > 5
                        ? $"[{arrayPreview}, ...] (total: {materialized.Count} items)"
                        : $"[{arrayPreview}]";

                default:
                    var objectStr = value.ToString() ?? "(null)";
                    var typeName = value.GetType().Name;
                    return objectStr.Length > 80
                        ? $"({typeName}) {objectStr[..75]}..."
                        : $"({typeName}) {objectStr}";
            }
        }
    }
}
