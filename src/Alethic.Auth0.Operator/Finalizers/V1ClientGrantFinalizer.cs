using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Controllers;
using Alethic.Auth0.Operator.Models;

using k8s.Models;
using KubeOps.Abstractions.Finalizer;

namespace Alethic.Auth0.Operator.Finalizers
{

    public class V1ClientGrantFinalizer : IEntityFinalizer<V1ClientGrant>
    {

        readonly V1ClientGrantController _controller;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="controller"></param>
        public V1ClientGrantFinalizer(V1ClientGrantController controller)
        {
            _controller = controller ?? throw new System.ArgumentNullException(nameof(controller));
        }



        /// <inheritdoc />
        public async Task FinalizeAsync(V1ClientGrant entity, CancellationToken cancellationToken)
        {
            // Call the controller's DeletedAsync method to perform Auth0 cleanup
            await _controller.DeletedAsync(entity, cancellationToken);
        }

    }

}
