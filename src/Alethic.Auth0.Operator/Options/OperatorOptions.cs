namespace Alethic.Auth0.Operator.Options
{

    public class OperatorOptions
    {

        /// <summary>
        /// Limit the operator to resources within the specified namespace.
        /// TODO
        /// </summary>
        public string? Namespace { get; set; }

        /// <summary>
        /// Limit the operator to resources with the specified partition annotation value.
        /// When set, only resources with annotation 'kubernetes.auth0.com/partition' matching this value will be processed.
        /// </summary>
        public string? Partition { get; set; }

        /// <summary>
        /// Options related to reconciliation of resources.
        /// </summary>
        public ReconciliationOptions Reconciliation { get; set; } = new ReconciliationOptions();

    }

}
