namespace Alethic.Auth0.Operator
{
    /// <summary>
    /// Constants used throughout the Auth0 operator.
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Annotation key for partition filtering.
        /// Resources with this annotation will only be processed if the annotation value matches the configured partition.
        /// </summary>
        public const string PartitionAnnotationKey = "kubernetes.auth0.com/partition";
    }
}