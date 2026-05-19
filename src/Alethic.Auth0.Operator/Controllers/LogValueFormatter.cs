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
        /// <c>signing_cert</c>, <c>kerberos</c>, <c>private_key</c>, <c>api_key</c>, plus any token
        /// field. False-positives (e.g. a user-facing description that happens to contain the word
        /// <c>password</c>) are acceptable; false-negatives are not.
        /// </summary>
        public static bool IsSensitiveKey(string? key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            // Lowercase once; substring matching keeps the rule resilient to nested-key prefixes
            // (e.g. "kerberos.principal", "ldap.bind_password", "saml.signing_cert").
            var lower = key.ToLowerInvariant();
            return lower.Contains("secret", StringComparison.Ordinal)
                || lower.Contains("password", StringComparison.Ordinal)
                || lower.Contains("credential", StringComparison.Ordinal)
                || lower.Contains("signing_cert", StringComparison.Ordinal)
                || lower.Contains("kerberos", StringComparison.Ordinal)
                || lower.Contains("private_key", StringComparison.Ordinal)
                || lower.Contains("api_key", StringComparison.Ordinal)
                || lower.Contains("token", StringComparison.Ordinal);
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
