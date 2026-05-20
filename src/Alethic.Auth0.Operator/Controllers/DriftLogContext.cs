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
    /// <see cref="V1Controller{TEntity, TSpec, TStatus, TConf}.LogAuth0Write"/>.
    /// </summary>
    public sealed class CamelCaseJsonStringEnumConverter : JsonStringEnumConverter
    {
        public CamelCaseJsonStringEnumConverter() : base(JsonNamingPolicy.CamelCase) { }
    }

    /// <summary>
    /// Categorizes why a write to Auth0 is being issued. Allows downstream observability tooling
    /// (Datadog dashboards in particular) to distinguish between green-field creates, drift-driven
    /// updates, and cascading deletes — all of which currently flow through the single
    /// <see cref="V1Controller{TEntity, TSpec, TStatus, TConf}.LogAuth0Write"/> funnel.
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
    /// <para>
    /// <see cref="AfterValue"/> is intentionally <c>null</c> on the first-reconciliation
    /// synthesis path (<see cref="V1TenantEntityController{TEntity,TConf}"/>'s
    /// first-reconcile fields list), even though the desired value is known. The
    /// downstream consumer there is <see cref="V1ClientController"/>'s selective-update
    /// routing, which inspects only <see cref="FieldPath"/>. The drift-path
    /// (<c>GetDriftFieldDetails</c>) populates both <see cref="BeforeValue"/> and
    /// <see cref="AfterValue"/>. Don't "fix" the asymmetry without re-checking the
    /// selective-update consumer.
    /// </para>
    /// </summary>
    public sealed record DriftField
    {
        /// <summary>
        /// Secret-shaped substrings that, if observed in <see cref="BeforeValue"/> /
        /// <see cref="AfterValue"/>, indicate the upstream redaction pass
        /// (<see cref="LogValueFormatter.IsSensitiveKey"/>) missed a sensitive value. Failing
        /// loud at construction is cheaper than discovering it on a Datadog log line.
        /// </summary>
        private static readonly string[] SecretShapedSubstrings =
        {
            "client_secret",
            "bind_credentials",
            "api_secret",
            "signing_cert",
            "private_key",
        };

        /// <summary>
        /// Exact rendered values that legitimately contain a <see cref="SecretShapedSubstrings"/>
        /// marker but are typed Auth0 OAuth enum members, not secrets. The leading/trailing quotes
        /// are intentional — <see cref="LogValueFormatter.FormatValueForLogging"/> wraps string
        /// values in double quotes, so matching on the quoted form prevents this allowlist from
        /// accidentally letting a free-form user-controlled string field (e.g. a connection
        /// description containing the literal "client_secret_post") slip past the redaction guard.
        /// <para>
        /// Source: <see cref="Alethic.Auth0.Operator.Core.Models.Client.TokenEndpointAuthMethod"/>
        /// declares <c>client_secret_post</c> and <c>client_secret_basic</c>. <c>client_secret_jwt</c>
        /// is a valid Auth0-returned value (OIDC core spec) that our enum doesn't currently model
        /// but Auth0 may emit on read — allowlist it pre-emptively so a future enum addition or a
        /// raw before-value from Auth0 doesn't false-trip the guard.
        /// </para>
        /// </summary>
        private static readonly HashSet<string> SecretShapedAllowedExactValues = new(StringComparer.Ordinal)
        {
            "\"client_secret_post\"",
            "\"client_secret_basic\"",
            "\"client_secret_jwt\"",
        };

        public string FieldPath { get; }

        public DriftChangeType ChangeType { get; }

        public string? BeforeValue { get; }

        public string? AfterValue { get; }

        public DriftField(
            string FieldPath,
            DriftChangeType ChangeType,
            string? BeforeValue,
            string? AfterValue)
        {
            EnsureNoSecretShape(nameof(BeforeValue), BeforeValue);
            EnsureNoSecretShape(nameof(AfterValue), AfterValue);

            this.FieldPath = FieldPath;
            this.ChangeType = ChangeType;
            this.BeforeValue = BeforeValue;
            this.AfterValue = AfterValue;
        }

        private static void EnsureNoSecretShape(string paramName, string? value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            // Allowlist exact rendered forms of typed OAuth enum members whose string value
            // legitimately contains a SecretShapedSubstrings marker. Matching the *quoted* form
            // (as produced by LogValueFormatter.FormatValueForLogging) ensures we don't allowlist
            // a user-controlled string field that merely contains the same substring.
            if (SecretShapedAllowedExactValues.Contains(value))
                return;

            foreach (var marker in SecretShapedSubstrings)
            {
                if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"DriftField.{paramName} contains the secret-shaped marker '{marker}'. " +
                        $"The upstream redaction pass missed this value — fix LogValueFormatter.IsSensitiveKey " +
                        $"(or the caller's RedactedOrFormat wrapper) rather than letting it reach the log payload.");
            }
        }
    }

    /// <summary>
    /// The why-we-wrote-to-Auth0 context attached to every <see cref="Auth0ApiCallType.Write"/>
    /// call. Required for Write calls (the non-nullable parameter on
    /// <see cref="V1Controller{TEntity, TSpec, TStatus, TConf}.LogAuth0Write"/> enforces this at
    /// compile time); not used by Read calls.
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
