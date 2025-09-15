using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Extensions;
using Alethic.Auth0.Operator.Models;
using Alethic.Auth0.Operator.Options;
using Alethic.Auth0.Operator.Services;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Queue;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Alethic.Auth0.Operator.Controllers
{
    /// <summary>
    /// Base class for V1 controllers that provides shared functionality for tenant API access management.
    /// </summary>
    public abstract class V1ControllerBase
    {
        /// <summary>
        /// Cache for TenantApiAccess instances to avoid recreating them unnecessarily.
        /// Key format: "{namespace}/{name}"
        /// </summary>
        protected static readonly ConcurrentDictionary<string, ITenantApiAccess> _tenantApiAccessCache = new();

        protected readonly IKubernetesClient _kube;
        protected readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the base controller.
        /// </summary>
        /// <param name="kube">The Kubernetes client</param>
        /// <param name="logger">Logger instance</param>
        protected V1ControllerBase(IKubernetesClient kube, ILogger logger)
        {
            _kube = kube ?? throw new ArgumentNullException(nameof(kube));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the Kubernetes API client.
        /// </summary>
        protected IKubernetesClient Kube => _kube;

        /// <summary>
        /// Gets the logger.
        /// </summary>
        protected ILogger Logger => _logger;

        /// <summary>
        /// Gets or creates a TenantApiAccess instance for the given tenant, with lazy loading and caching.
        /// </summary>
        /// <param name="tenant">The tenant to get API access for</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>TenantApiAccess instance</returns>
        protected async Task<ITenantApiAccess> GetOrCreateTenantApiAccessAsync(V1Tenant tenant, CancellationToken cancellationToken)
        {
            var cacheKey = $"{tenant.Namespace()}/{tenant.Name()}";

            if (_tenantApiAccessCache.TryGetValue(cacheKey, out var existingTenantApiAccess))
            {
                return existingTenantApiAccess;
            }

            var newTenantApiAccess = await TenantApiAccess.CreateAsync(tenant, Kube, Logger, cancellationToken);
            _tenantApiAccessCache.TryAdd(cacheKey, newTenantApiAccess);

            Logger.LogInformationJson($"Cached new TenantApiAccess for tenant {tenant.Namespace()}/{tenant.Name()}", new
            {
                tenantNamespace = tenant.Namespace(),
                tenantName = tenant.Name(),
                cacheKey = cacheKey
            });

            return newTenantApiAccess;
        }

        /// <summary>
        /// Checks if the entity should be processed based on partition filtering.
        /// </summary>
        /// <typeparam name="TEntity">The entity type</typeparam>
        /// <param name="entity">The entity to check</param>
        /// <param name="options">Operator configuration options</param>
        /// <param name="entityTypeName">The entity type name for logging</param>
        /// <returns>True if the entity should be processed, false otherwise</returns>
        protected bool ShouldProcessEntityByPartition<TEntity>(TEntity entity, IOptions<OperatorOptions> options, string entityTypeName)
            where TEntity : IKubernetesObject<V1ObjectMeta>
        {
            var configuredPartition = options.Value.Partition;
            var annotations = entity.Metadata?.Annotations;

            if (string.IsNullOrEmpty(configuredPartition))
            {
                return annotations == null || !annotations.ContainsKey(Constants.PartitionAnnotationKey);
            }

            if (annotations == null || !annotations.TryGetValue(Constants.PartitionAnnotationKey, out var entityPartition))
            {
                return false;
            }

            if (string.Equals(configuredPartition, entityPartition, StringComparison.Ordinal))
            {
                Logger.LogInformationJson($"MATCHING PARTITION RESOURCE FOUND: RESOURCE NAME={entity.Name()}, NAMESPACE={entity.Namespace()}, CRD TYPE={entityTypeName}, PARTITION ANNOTATION={Constants.PartitionAnnotationKey}={entityPartition}", new
                {
                    resourceName = entity.Name(),
                    resourceNamespace = entity.Namespace(),
                    crdType = entityTypeName,
                    partitionAnnotationKey = Constants.PartitionAnnotationKey,
                    partitionAnnotationValue = entityPartition
                });
                return true;
            }

            return false;
        }
    }
}