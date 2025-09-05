using System.Linq;

namespace Alethic.Auth0.Operator.Models
{

    public interface V1Entity<TSpec, TStatus, TConf>
        where TSpec : V1EntitySpec<TConf>
        where TStatus : V1EntityStatus
        where TConf : class
    {

        /// <summary>
        /// Gets the policy set on the entity.
        /// </summary>
        /// <returns>Array of policy types applied to the entity</returns>
        public V1EntityPolicyType[] GetPolicy() => Spec.Policy ?? [
            V1EntityPolicyType.Create,
            V1EntityPolicyType.Update,
        ];

        /// <summary>
        /// Gets whether or not this entity has this policy applied.
        /// </summary>
        /// <param name="policy">The policy type to check for</param>
        /// <returns>True if the entity has the specified policy, false otherwise</returns>
        public bool HasPolicy(V1EntityPolicyType policy)
        {
            return GetPolicy().Contains(policy);
        }

        /// <summary>
        /// Gets the specification of the entity.
        /// </summary>
        TSpec Spec { get; }

        /// <summary>
        /// Gets the current status of the entity.
        /// </summary>
        TStatus Status { get; }

    }

}
