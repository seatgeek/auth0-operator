using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Models;

using KubeOps.Abstractions.Finalizer;

namespace Alethic.Auth0.Operator.Finalizers
{

    public class V1TenantFinalizer : IEntityFinalizer<V1Tenant>
    {

        public Task FinalizeAsync(V1Tenant entity, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

    }

}
