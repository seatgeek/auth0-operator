using System;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Entities;
using Alethic.Auth0.Operator.Models.Tenant;

using Auth0.ManagementApi.Models;

using k8s.Models;

using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Rbac;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Alethic.Auth0.Operator.Controllers
{

    [EntityRbac(typeof(V1Tenant), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1Secret), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(Eventsv1Event), Verbs = RbacVerb.All)]
    public class V1TenantController : V1ControllerBase<V1Tenant>, IEntityController<V1Tenant>
    {

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="kube"></param>
        /// <param name="logger"></param>
        public V1TenantController(IMemoryCache cache, IKubernetesClient kube, ILogger<V1ClientController> logger) :
            base(cache, kube, logger)
        {

        }

        /// <inheritdoc />
        public override async Task ReconcileAsync(V1Tenant entity, CancellationToken cancellationToken)
        {
            try
            {
                Logger.LogInformation("Reconciling Tenant {Entity}.", entity);

                var api = await GetTenantApiClientAsync(entity, cancellationToken);

                // update specified configuration
                if (entity.Spec.Conf is { } conf)
                    await api.TenantSettings.UpdateAsync(TransformToNewtonsoftJson<TenantConf, TenantSettingsUpdateRequest>(conf), cancellationToken);

                // retrieve and copy applied settings to status
                var settings = await api.TenantSettings.GetAsync(cancellationToken: cancellationToken);
                var lastConf = TransformToSystemTextJson<TenantSettings, TenantConf>(settings);
                entity.Status.LastConf = lastConf;
                await Kube.UpdateStatusAsync(entity, cancellationToken);

                Logger.LogInformation("Reconciled Tenant {Entity}.", entity);
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
        public override Task DeletedAsync(V1Tenant entity, CancellationToken cancellationToken)
        {
            Logger.LogWarning("Unsupported operation deleting entity {Entity}.", entity);
            return Task.CompletedTask;
        }

    }

}
