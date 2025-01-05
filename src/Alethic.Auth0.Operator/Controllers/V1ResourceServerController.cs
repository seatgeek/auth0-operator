using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Entities;
using Alethic.Auth0.Operator.Models.ResourceServer;

using Auth0.ManagementApi.Models;

using k8s.Models;

using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Rbac;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Alethic.Auth0.Operator.Controllers
{

    [EntityRbac(typeof(V1Tenant), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(V1ResourceServer), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1Secret), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(Eventsv1Event), Verbs = RbacVerb.All)]
    public class V1ResourceServerController : V1ControllerBase<V1ResourceServer>, IEntityController<V1ResourceServer>
    {

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="kube"></param>
        /// <param name="logger"></param>
        public V1ResourceServerController(IMemoryCache cache, IKubernetesClient kube, ILogger<V1ClientController> logger) :
            base(cache, kube, logger)
        {

        }

        /// <inheritdoc />
        public override async Task ReconcileAsync(V1ResourceServer entity, CancellationToken cancellationToken)
        {
            try
            {
                Logger.LogInformation("Reconciling ResourceServer {Entity}.", entity);

                if (entity.Spec.TenantRef == null)
                    throw new InvalidOperationException($"ResourceServer {entity.Namespace()}:{entity.Name()} is missing a tenant reference.");

                var api = await GetTenantApiClientAsync(entity.Spec.TenantRef, entity.Namespace(), cancellationToken);
                if (api == null)
                    throw new InvalidOperationException($"ResourceServer {entity.Namespace()}:{entity.Name()} failed to retrieve API client.");

                if (entity.Spec.Conf == null)
                    throw new InvalidOperationException($"ResourceServer {entity.Namespace()}:{entity.Name()} is missing configuration.");

                if (string.IsNullOrWhiteSpace(entity.Spec.Conf.Name))
                    throw new InvalidOperationException($"ResourceServer {entity.Namespace()}:{entity.Name()} is missing a name.");

                // discover entity by name, or create
                if (string.IsNullOrWhiteSpace(entity.Status.Id))
                {
                    var list = await api.ResourceServers.GetAllAsync(new ResourceServerGetRequest(), cancellationToken: cancellationToken);
                    var self = list.FirstOrDefault(i => i.Identifier == entity.Spec.Conf.Name);
                    if (self == null)
                    {
                        Logger.LogInformation("ResourceServer {Namespace}/{Name} could not be located, creating.", entity.Namespace(), entity.Name());

                        self = await api.ResourceServers.CreateAsync(TransformToNewtonsoftJson<ResourceServerConf, ResourceServerCreateRequest>(entity.Spec.Conf), cancellationToken);
                        Logger.LogInformation("ResourceServer {Namespace}/{Name} created with {Id}", entity.Namespace(), entity.Name(), self.Id);
                        entity.Status.Id = self.Id;
                        await Kube.UpdateStatusAsync(entity, cancellationToken);
                    }
                }

                // update specified configuration
                if (entity.Spec.Conf is { } conf)
                    await api.ResourceServers.UpdateAsync(entity.Status.Id, TransformToNewtonsoftJson<ResourceServerConf, ResourceServerUpdateRequest>(conf), cancellationToken);

                // retrieve and copy applied settings to status
                var settings = await api.ResourceServers.GetAsync(entity.Status.Id, cancellationToken: cancellationToken);
                var lastConf = TransformToSystemTextJson<ResourceServer, ResourceServerConf>(settings);
                entity.Status.LastConf = lastConf;
                await Kube.UpdateStatusAsync(entity, cancellationToken);

                Logger.LogInformation("Reconciled ResourceServer {Entity}.", entity);
                await ReconcileSuccessAsync(entity, cancellationToken);
            }
            catch (Exception e)
            {
                try
                {
                    Logger.LogError(e, "Unexpected exception updating ResourceServer.");
                    await ReconcileWarningAsync(entity, e.Message, cancellationToken);
                }
                catch
                {
                    Logger.LogCritical(e, "Unexpected exception creating event.");
                }
            }
        }

        /// <inheritdoc />
        public override async Task DeletedAsync(V1ResourceServer entity, CancellationToken cancellationToken)
        {
            try
            {
                if (entity.Spec.TenantRef == null)
                    throw new InvalidOperationException($"ResourceServer {entity.Namespace()}:{entity.Name()} is missing a tenant reference.");

                var api = await GetTenantApiClientAsync(entity.Spec.TenantRef, entity.Namespace(), cancellationToken);
                if (api == null)
                    throw new InvalidOperationException($"ResourceServer {entity.Namespace()}:{entity.Name()} failed to retrieve API client.");

                if (string.IsNullOrWhiteSpace(entity.Status.Id))
                {
                    Logger.LogWarning($"ResourceServer {entity.Namespace()}:{entity.Name()} has no known ID, skipping delete.");
                    return;
                }

                await api.ResourceServers.DeleteAsync(entity.Status.Id, cancellationToken: cancellationToken);
            }
            catch (Exception e)
            {
                try
                {
                    Logger.LogError(e, "Unexpected exception deleting ResourceServer.");
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
