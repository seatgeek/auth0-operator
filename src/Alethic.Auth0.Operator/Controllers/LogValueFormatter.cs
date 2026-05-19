using System.Collections;
using System.Linq;

namespace Alethic.Auth0.Operator.Controllers
{
    /// <summary>
    /// Shared value formatter for drift-log payloads. Promoted from the per-controller copies
    /// in <c>V1TenantController</c> and <c>V1TenantEntityController</c> so every Auth0 write log
    /// truncates identically. Caps strings at 100 chars, hashtables at 10 entries, arrays at 5
    /// items — bounded enough that no single drift entry blows past Datadog's per-line budget.
    /// </summary>
    internal static class LogValueFormatter
    {
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
                        .Select(entry => $"{entry.Key}: {FormatValueForLogging(entry.Value)}");
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
