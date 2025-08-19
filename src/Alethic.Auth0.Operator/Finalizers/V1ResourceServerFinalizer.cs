using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Controllers;
using Alethic.Auth0.Operator.Models;

using k8s.Models;
using KubeOps.Abstractions.Finalizer;

using Microsoft.Extensions.Logging;

namespace Alethic.Auth0.Operator.Finalizers
{

    public class V1ResourceServerFinalizer : IEntityFinalizer<V1ResourceServer>
    {

        readonly V1ResourceServerController _controller;
        readonly ILogger<V1ResourceServerFinalizer> _logger;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="controller"></param>
        /// <param name="logger"></param>
        public V1ResourceServerFinalizer(V1ResourceServerController controller, ILogger<V1ResourceServerFinalizer> logger)
        {
            _controller = controller ?? throw new System.ArgumentNullException(nameof(controller));
            _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
        }



        /// <inheritdoc />
        public async Task FinalizeAsync(V1ResourceServer entity, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("ResourceServer finalizer starting cleanup for {Namespace}/{Name}", entity.Namespace(), entity.Name());

                // Call the controller's DeletedAsync method to perform Auth0 cleanup
                await _controller.DeletedAsync(entity, cancellationToken);

                _logger.LogInformation("ResourceServer finalizer completed cleanup for {Namespace}/{Name}", entity.Namespace(), entity.Name());
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "ResourceServer finalizer failed for {Namespace}/{Name}: {Message}", entity.Namespace(), entity.Name(), ex.Message);
                throw;
            }
        }

    }

}
