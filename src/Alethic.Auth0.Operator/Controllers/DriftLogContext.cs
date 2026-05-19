using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Controllers
{
    /// <summary>
    /// Forces camelCase enum serialization for the log-payload enums in this file so the JSON shape
    /// is uniform across <c>reconciliationType</c> and <c>driftFields[].changeType</c>. Without this
    /// the default <see cref="JsonStringEnumConverter"/> emits PascalCase, which historically clashed
    /// with the manual <c>.ToLowerInvariant()</c> path used for <c>reconciliationType</c> in
    /// <see cref="V1Controller{TEntity, TSpec, TStatus, TConf}.LogAuth0ApiCall"/>.
    /// </summary>
    internal sealed class CamelCaseJsonStringEnumConverter : JsonStringEnumConverter
    {
        public CamelCaseJsonStringEnumConverter() : base(JsonNamingPolicy.CamelCase) { }
    }

    /// <summary>
    /// Categorizes why a write to Auth0 is being issued. Allows downstream observability tooling
    /// (Datadog dashboards in particular) to distinguish between green-field creates, drift-driven
    /// updates, and cascading deletes — all of which currently flow through the single
    /// <see cref="V1Controller{TEntity, TSpec, TStatus, TConf}.LogAuth0ApiCall"/> funnel.
    /// </summary>
    [JsonConverter(typeof(CamelCaseJsonStringEnumConverter))]
    public enum ReconciliationType
    {
        /// <summary>First reconciliation - no Auth0 entity exists yet, applying full configuration.</summary>
        First,

        /// <summary>Existing Auth0 entity, configuration drift detected against desired state.</summary>
        Drift,

        /// <summary>Kubernetes CR deleted - cascading delete to Auth0.</summary>
        Finalizer,
    }

    /// <summary>
    /// How a single field differs between Auth0 (before) and the Kubernetes spec (after).
    /// </summary>
    [JsonConverter(typeof(CamelCaseJsonStringEnumConverter))]
    public enum DriftChangeType
    {
        /// <summary>Field exists in desired state, missing from Auth0.</summary>
        Added,

        /// <summary>Field exists in both, values differ.</summary>
        Modified,

        /// <summary>Field exists in Auth0, missing from desired state.</summary>
        Removed,
    }

    /// <summary>
    /// One field-level diff between Auth0 state and the desired Kubernetes spec.
    /// Values are pre-truncated via <see cref="LogValueFormatter.FormatValueForLogging"/> so the
    /// serialized log entry stays under the per-line budget.
    /// </summary>
    public sealed record DriftField(
        string FieldPath,
        DriftChangeType ChangeType,
        string? BeforeValue,
        string? AfterValue);

    /// <summary>
    /// The why-we-wrote-to-Auth0 context attached to every <see cref="Auth0ApiCallType.Write"/>
    /// call. Required for Write calls (enforced at runtime by <c>LogAuth0ApiCall</c>); always null
    /// for Read calls.
    /// </summary>
    public sealed record DriftLogContext(
        ReconciliationType ReconciliationType,
        IReadOnlyList<DriftField> DriftFields,
        string DriftReason)
    {
        /// <summary>
        /// Convenience for the "first reconciliation - create branch" case. Drift fields are
        /// undefined here (there's no prior Auth0 state to diff against).
        /// </summary>
        public static DriftLogContext FirstReconciliation() => new(
            ReconciliationType.First,
            Array.Empty<DriftField>(),
            "first reconciliation - applying full configuration");

        /// <summary>
        /// Convenience for the "finalizer - cascading delete" case.
        /// </summary>
        public static DriftLogContext FinalizerDelete() => new(
            ReconciliationType.Finalizer,
            Array.Empty<DriftField>(),
            "kubernetes entity deleted - cascading delete to Auth0");

        /// <summary>
        /// Convenience for the drift-update branch. Builds the human-readable summary from
        /// the field counts so every drift log emits the same shape of summary.
        /// </summary>
        public static DriftLogContext Drift(IReadOnlyList<DriftField> fields)
        {
            var added = fields.Count(f => f.ChangeType == DriftChangeType.Added);
            var modified = fields.Count(f => f.ChangeType == DriftChangeType.Modified);
            var removed = fields.Count(f => f.ChangeType == DriftChangeType.Removed);
            var reason = $"{added} added, {modified} modified, {removed} removed";
            return new DriftLogContext(ReconciliationType.Drift, fields, reason);
        }
    }
}
