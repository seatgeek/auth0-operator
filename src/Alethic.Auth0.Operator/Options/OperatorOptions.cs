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
        /// Enable or disable leader election for high-availability deployments.
        /// When false, all replicas will process resources independently without coordination.
        /// Useful for partition-based deployments, development/testing, or single replica scenarios.
        /// Default: true
        /// </summary>
        public bool LeaderElection { get; set; } = true;

        /// <summary>
        /// The leader election ID used for high-availability operator deployments.
        /// This determines the name of the Kubernetes resource (ConfigMap or Lease) used to hold the leader lock.
        /// Only used when LeaderElection is true.
        /// Default: 'auth0-operator-leader-election'
        /// </summary>
        public string LeaderElectionId { get; set; } = "auth0-operator-leader-election";

        /// <summary>
        /// Options related to reconciliation of resources.
        /// </summary>
        public ReconciliationOptions Reconciliation { get; set; } = new ReconciliationOptions();

    }

}
