using System;

namespace Alethic.Auth0.Operator.Models
{
    /// <summary>
    /// Configuration for reconciliation behavior.
    /// </summary>
    public class ReconciliationConfig
    {
        /// <summary>
        /// The interval between periodic reconciliation cycles.
        /// </summary>
        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
    }
}