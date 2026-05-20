using System;

namespace Alethic.Auth0.Operator.Controllers
{
    /// <summary>
    /// Shared drift-comparison primitives consumed by both <see cref="V1TenantController"/>
    /// and <see cref="V1TenantEntityController{TEntity, TConf, TApi}"/>. Promoted from the
    /// per-controller copies so the two drift-detection paths cannot diverge silently —
    /// see decisions.md entry "2026-05-19 — Unified array-comparison semantics across all
    /// drift controllers" for the rationale (Auth0 reorders list-shaped fields like
    /// <c>enabled_locales</c> server-side; preserving order in comparison produces
    /// spurious drift).
    /// </summary>
    internal static class DriftComparison
    {
        /// <summary>
        /// Compares two collections as sets (order-insensitive) using a caller-supplied
        /// element-equality callback. O(n²) set-membership match — bounded by Auth0 DTO
        /// shapes which never exceed a few dozen entries per array field.
        /// </summary>
        /// <param name="leftArray">First collection materialized as an object array</param>
        /// <param name="rightArray">Second collection materialized as an object array</param>
        /// <param name="valuesEqual">Per-element equality predicate (typically the caller's
        /// own <c>AreValuesEqual</c> so nested hashtables / arrays recurse correctly).</param>
        /// <returns>True if the two arrays contain the same elements regardless of order.</returns>
        internal static bool AreArraysEqualOrderInsensitive(
            object[] leftArray,
            object[] rightArray,
            Func<object?, object?, bool> valuesEqual)
        {
            if (leftArray.Length != rightArray.Length)
                return false;

            foreach (var leftItem in leftArray)
            {
                bool foundMatch = false;
                foreach (var rightItem in rightArray)
                {
                    if (valuesEqual(leftItem, rightItem))
                    {
                        foundMatch = true;
                        break;
                    }
                }
                if (!foundMatch)
                    return false;
            }

            return true;
        }
    }
}
