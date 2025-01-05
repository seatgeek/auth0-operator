using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Entities;
using Alethic.Auth0.Operator.Models.Client;

using Auth0.ManagementApi.Models;

using k8s.Models;

using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Rbac;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Alethic.Auth0.Operator.Controllers
{

    [EntityRbac(typeof(V1Client), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(Eventsv1Event), Verbs = RbacVerb.All)]
    public class V1ClientController : V1ControllerBase<V1Client>, IEntityController<V1Client>
    {

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="kube"></param>
        /// <param name="kube"></param>
        /// <param name="logger"></param>
        public V1ClientController(IMemoryCache cache, IKubernetesClient kube, ILogger<V1ClientController> logger) :
            base(cache, kube, logger)
        {

        }

        /// <inheritdoc />
        public override async Task ReconcileAsync(V1Client entity, CancellationToken cancellationToken)
        {
            try
            {
                Logger.LogInformation("Reconciling Client {Entity}.", entity);

                if (entity.Spec.TenantRef == null)
                    throw new InvalidOperationException($"Client {entity.Namespace()}:{entity.Name()} is missing a tenant reference.");

                var api = await GetTenantApiClientAsync(entity.Spec.TenantRef, entity.Namespace(), cancellationToken);
                if (api == null)
                    throw new InvalidOperationException($"Client {entity.Namespace()}:{entity.Name()} failed to retrieve API client.");

                if (entity.Spec.Conf == null)
                    throw new InvalidOperationException($"Client {entity.Namespace()}:{entity.Name()} is missing configuration.");

                if (string.IsNullOrWhiteSpace(entity.Spec.Conf.Name))
                    throw new InvalidOperationException($"Client {entity.Namespace()}:{entity.Name()} is missing a name.");

                // discover entity by name, or create
                if (string.IsNullOrWhiteSpace(entity.Status.Id))
                {
                    var list = await api.Clients.GetAllAsync(new GetClientsRequest() { Fields = "client_id,name" }, cancellationToken: cancellationToken);
                    var self = list.FirstOrDefault(i => i.Name == entity.Spec.Conf.Name);
                    if (self == null)
                    {
                        Logger.LogInformation("Client {Namespace}/{Name} could not be located, creating.", entity.Namespace(), entity.Name());

                        if (entity.Spec.Conf.ApplicationType == null)
                            throw new InvalidOperationException($"Client {entity.Namespace()}:{entity.Name()} is missing a value for application type.");

                        self = await api.Clients.CreateAsync(TransformToNewtonsoftJson<ClientConf, ClientCreateRequest>(entity.Spec.Conf), cancellationToken);
                        Logger.LogInformation("Client {Namespace}/{Name} created with {Id}", entity.Namespace(), entity.Name(), self.ClientId);
                        entity.Status.Id = self.ClientId;
                        await Kube.UpdateStatusAsync(entity, cancellationToken);
                    }
                }

                // update specified configuration
                if (entity.Spec.Conf is { } conf)
                    await api.Clients.UpdateAsync(entity.Status.Id, TransformToNewtonsoftJson<ClientConf, ClientUpdateRequest>(conf), cancellationToken);

                // retrieve and copy applied settings to status
                var settings = await api.Clients.GetAsync(entity.Status.Id, cancellationToken: cancellationToken);
                var lastConf = TransformToSystemTextJson<Client, ClientConf>(settings);
                entity.Status.LastConf = lastConf;
                await Kube.UpdateStatusAsync(entity, cancellationToken);

                Logger.LogInformation("Reconciled Client {Entity}.", entity);
                await ReconcileSuccessAsync(entity, cancellationToken);
            }
            catch (Exception e)
            {
                try
                {
                    Logger.LogError(e, "Unexpected exception updating Client.");
                    await ReconcileWarningAsync(entity, e.Message, cancellationToken);
                }
                catch
                {
                    Logger.LogCritical(e, "Unexpected exception creating event.");
                }
            }
        }

        /// <inheritdoc />
        public override Task DeletedAsync(V1Client entity, CancellationToken cancellationToken)
        {
            Logger.LogWarning("Unsupported operation deleting Client {Entity}.", entity);
            return Task.CompletedTask;
        }

    }

}
