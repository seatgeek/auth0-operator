using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Controllers;
using Alethic.Auth0.Operator.Models;

using k8s.Models;
using KubeOps.Abstractions.Finalizer;

namespace Alethic.Auth0.Operator.Finalizers
{

    public class V1ClientFinalizer : IEntityFinalizer<V1Client>
    {

        readonly V1ClientController _controller;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="controller"></param>
        public V1ClientFinalizer(V1ClientController controller)
        {
            _controller = controller ?? throw new System.ArgumentNullException(nameof(controller));
        }



        /// <inheritdoc />
        public async Task FinalizeAsync(V1Client entity, CancellationToken cancellationToken)
        {
            // Call the controller's DeletedAsync method to perform Auth0 cleanup
            await _controller.DeletedAsync(entity, cancellationToken);
        }

    }

}
