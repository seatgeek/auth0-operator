using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Extensions;
using Alethic.Auth0.Operator.Models;
using Alethic.Auth0.Operator.Services;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Queue;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

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
    }
}