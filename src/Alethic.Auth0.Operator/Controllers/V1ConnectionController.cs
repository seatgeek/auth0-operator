using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Entities;
using Alethic.Auth0.Operator.Models.Client;
using Alethic.Auth0.Operator.Models.Connection;

using Auth0.ManagementApi.Models;
using Auth0.ManagementApi.Paging;

using k8s.Models;

using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Rbac;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Alethic.Auth0.Operator.Controllers
{

    [EntityRbac(typeof(V1Tenant), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(V1Connection), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1Secret), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(Eventsv1Event), Verbs = RbacVerb.All)]
    public class V1ConnectionController :
        V1TenantEntityController<V1Connection, V1Connection.SpecDef, V1Connection.StatusDef, ConnectionConf>,
        IEntityController<V1Connection>
    {

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="kube"></param>
        /// <param name="logger"></param>
        public V1ConnectionController(IMemoryCache cache, IKubernetesClient kube, ILogger<V1ClientController> logger) :
            base(cache, kube, logger)
        {

        }

        /// <inheritdoc />
        public override async Task ReconcileAsync(V1Connection entity, CancellationToken cancellationToken)
        {
            try
            {
                Logger.LogInformation("Reconciling Connection {Entity}.", entity);

                if (entity.Spec.TenantRef == null)
                    throw new InvalidOperationException($"Connection {entity.Namespace()}:{entity.Name()} is missing a tenant reference.");

                var api = await GetTenantApiClientAsync(entity.Spec.TenantRef, entity.Namespace(), cancellationToken);
                if (api == null)
                    throw new InvalidOperationException($"Connection {entity.Namespace()}:{entity.Name()} failed to retrieve API client.");

                if (entity.Spec.Conf == null)
                    throw new InvalidOperationException($"Connection {entity.Namespace()}:{entity.Name()} is missing configuration.");

                if (string.IsNullOrWhiteSpace(entity.Spec.Conf.Name))
                    throw new InvalidOperationException($"Connection {entity.Namespace()}:{entity.Name()} is missing a name.");

                // discover entity by name, or create
                if (string.IsNullOrWhiteSpace(entity.Status.Id))
                {
                    var list = await api.Connections.GetAllAsync(new GetConnectionsRequest() { Fields = "id,name" }, (PaginationInfo?)null, cancellationToken: cancellationToken);
                    var self = list.FirstOrDefault(i => i.Name == entity.Spec.Conf.Name);
                    if (self == null)
                    {
                        Logger.LogInformation("Connection {Namespace}/{Name} could not be located, creating.", entity.Namespace(), entity.Name());

                        self = await api.Connections.CreateAsync(TransformToNewtonsoftJson<ConnectionConf, ConnectionCreateRequest>(entity.Spec.Conf), cancellationToken);
                        Logger.LogInformation("Connection {Namespace}/{Name} created with {Id}", entity.Namespace(), entity.Name(), self.Id);
                        entity.Status.Id = self.Id;
                        await Kube.UpdateStatusAsync(entity, cancellationToken);
                    }
                }

                // update specified configuration
                if (entity.Spec.Conf is { } conf)
                    await api.Connections.UpdateAsync(entity.Status.Id, TransformToNewtonsoftJson<ConnectionConf, ConnectionUpdateRequest>(conf), cancellationToken);

                // retrieve and copy applied settings to status
                var settings = await api.Connections.GetAsync(entity.Status.Id, cancellationToken: cancellationToken);
                var lastConf = TransformToSystemTextJson<Connection, ConnectionConf>(settings);
                entity.Status.LastConf = lastConf;
                await Kube.UpdateStatusAsync(entity, cancellationToken);

                Logger.LogInformation("Reconciled Connection {Entity}.", entity);
                await ReconcileSuccessAsync(entity, cancellationToken);
            }
            catch (Exception e)
            {
                try
                {
                    Logger.LogError(e, "Unexpected exception updating Connection.");
                    await ReconcileWarningAsync(entity, e.Message, cancellationToken);
                }
                catch
                {
                    Logger.LogCritical(e, "Unexpected exception creating Connection.");
                }
            }
        }

        /// <inheritdoc />
        public override async Task DeletedAsync(V1Connection entity, CancellationToken cancellationToken)
        {
            try
            {
                if (entity.Spec.TenantRef == null)
                    throw new InvalidOperationException($"Connection {entity.Namespace()}:{entity.Name()} is missing a tenant reference.");

                var api = await GetTenantApiClientAsync(entity.Spec.TenantRef, entity.Namespace(), cancellationToken);
                if (api == null)
                    throw new InvalidOperationException($"Connection {entity.Namespace()}:{entity.Name()} failed to retrieve API client.");

                if (string.IsNullOrWhiteSpace(entity.Status.Id))
                {
                    Logger.LogWarning($"Connection {entity.Namespace()}:{entity.Name()} has no known ID, skipping delete.");
                    return;
                }

                await api.Connections.DeleteAsync(entity.Status.Id, cancellationToken: cancellationToken);
            }
            catch (Exception e)
            {
                try
                {
                    Logger.LogError(e, "Unexpected exception deleting Connection.");
                    await DeletingWarningAsync(entity, e.Message, cancellationToken);
                }
                catch
                {
                    Logger.LogCritical(e, "Unexpected exception creating event.");
                }
            }
        }

    }

}
