using System;

namespace Alethic.Auth0.Operator.Options
{

    /// <summary>
    /// Configuration for reconciliation behavior.
    /// </summary>
    public class ReconciliationOptions
    {

        /// <summary>
        /// The interval between periodic reconciliation cycles.
        /// </summary>
        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(60 * 60); // 1 hour

    }

}