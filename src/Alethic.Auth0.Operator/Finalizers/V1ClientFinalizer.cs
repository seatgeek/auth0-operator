using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Controllers;
using Alethic.Auth0.Operator.Models;

using KubeOps.Abstractions.Finalizer;

using Microsoft.Extensions.Logging;

namespace Alethic.Auth0.Operator.Finalizers
{

    public class V1ClientFinalizer : IEntityFinalizer<V1Client>
    {

        readonly V1ClientController _controller;
        readonly ILogger<V1ClientFinalizer> _logger;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="controller"></param>
        /// <param name="logger"></param>
        public V1ClientFinalizer(V1ClientController controller, ILogger<V1ClientFinalizer> logger)
        {
            _controller = controller ?? throw new System.ArgumentNullException(nameof(controller));
            _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the current UTC timestamp formatted for logging.
        /// </summary>
        protected static string UtcTimestamp => System.DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        /// <inheritdoc />
        public async Task FinalizeAsync(V1Client entity, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("{UtcTimestamp} - Client finalizer starting cleanup for {Namespace}/{Name}", UtcTimestamp, entity.Namespace(), entity.Name());

                // Call the controller's DeletedAsync method to perform Auth0 cleanup
                await _controller.DeletedAsync(entity, cancellationToken);

                _logger.LogInformation("{UtcTimestamp} - Client finalizer completed cleanup for {Namespace}/{Name}", UtcTimestamp, entity.Namespace(), entity.Name());
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "{UtcTimestamp} - Client finalizer failed for {Namespace}/{Name}: {Message}", UtcTimestamp, entity.Namespace(), entity.Name(), ex.Message);
                throw;
            }
        }

    }

}
