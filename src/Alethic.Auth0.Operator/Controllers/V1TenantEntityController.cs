using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Extensions;
using Alethic.Auth0.Operator.Models;
using Alethic.Auth0.Operator.Options;
using Alethic.Auth0.Operator.Services;

using Auth0.Core.Exceptions;
using Auth0.ManagementApi;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Queue;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Alethic.Auth0.Operator.Controllers
{

    public abstract class V1TenantEntityController<TEntity, TSpec, TStatus, TConf> : V1Controller<TEntity, TSpec, TStatus, TConf>
        where TEntity : IKubernetesObject<V1ObjectMeta>, V1TenantEntity<TSpec, TStatus, TConf>
        where TSpec : V1TenantEntitySpec<TConf>
        where TStatus : V1TenantEntityStatus
        where TConf : class
    {

        readonly IOptions<OperatorOptions> _options;
        static readonly ConcurrentDictionary<string, ITenantApiAccess> _tenantApiAccessCache = new();

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="kube">The Kubernetes client</param>
        /// <param name="requeue">Entity requeue service for scheduling reconciliation</param>
        /// <param name="cache">Memory cache for API responses</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="options">Operator configuration options</param>
        public V1TenantEntityController(IKubernetesClient kube, EntityRequeue<TEntity> requeue, IMemoryCache cache, ILogger logger, IOptions<OperatorOptions> options) :
            base(kube, requeue, cache, logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

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
        /// Attempts to perform a get operation through the API.
        /// </summary>
        /// <param name="api">Auth0 Management API client</param>
        /// <param name="id">The entity ID to retrieve</param>
        /// <param name="defaultNamespace">Default namespace for the entity</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The retrieved entity as a hashtable, or null if not found</returns>
        protected abstract Task<Hashtable?> Get(IManagementApiClient api, string id, string defaultNamespace, CancellationToken cancellationToken);

        /// <summary>
        /// Attempts to locate a matching API element by the given configuration.
        /// </summary>
        /// <param name="api">Auth0 Management API client</param>
        /// <param name="entity">The entity to find</param>
        /// <param name="spec">Entity specification with configuration</param>
        /// <param name="defaultNamespace">Default namespace for the entity</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The ID of the matching entity, or null if not found</returns>
        protected abstract Task<string?> Find(IManagementApiClient api, TEntity entity, TSpec spec, string defaultNamespace, CancellationToken cancellationToken);

        /// <summary>
        /// Performs a validation on the <paramref name="conf"/> parameter for usage in create operations.
        /// </summary>
        /// <param name="conf">The configuration to validate</param>
        /// <returns>Error message if validation fails, null if valid</returns>
        protected abstract string? ValidateCreate(TConf conf);

        /// <summary>
        /// Attempts to perform a creation through the API. If successful returns the new ID value.
        /// </summary>
        /// <param name="api">Auth0 Management API client</param>
        /// <param name="conf">Configuration for the new entity</param>
        /// <param name="defaultNamespace">Default namespace for the entity</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The ID of the created entity</returns>
        protected abstract Task<string> Create(IManagementApiClient api, TConf conf, string defaultNamespace, CancellationToken cancellationToken);

        /// <summary>
        /// Attempts to perform an update through the API.
        /// </summary>
        /// <param name="api">Auth0 Management API client</param>
        /// <param name="id">The entity ID to update</param>
        /// <param name="last">The last known configuration state</param>
        /// <param name="conf">The new configuration to apply</param>
        /// <param name="defaultNamespace">Default namespace for the entity</param>
        /// <param name="tenantApiAccess">Tenant API access for credentials and tokens</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A task representing the asynchronous update operation</returns>
        protected abstract Task Update(IManagementApiClient api, string id, Hashtable? last, TConf conf, List<string> driftingFields, string defaultNamespace, ITenantApiAccess tenantApiAccess, CancellationToken cancellationToken);


        /// <summary>
        /// Gets the list of fields to include in drift detection when using IncludeSpecificFields mode.
        /// </summary>
        /// <returns>Array of field names to include in comparison</returns>
        protected virtual string[] GetIncludedFields() => Array.Empty<string>();

        /// <summary>
        /// Gets the list of fields to exclude from drift detection when using ExcludeSpecificFields mode.
        /// This is combined with the default volatile fields list.
        /// </summary>
        /// <returns>Array of field names to exclude from comparison</returns>
        protected virtual string[] GetExcludedFields() => Array.Empty<string>();

        /// <summary>
        /// Determines if the entity controller requires an Auth0 API fetch.
        /// This method allows entity-specific controllers to provide their own logic for determining
        /// whether an Auth0 API call should be made during reconciliation.
        /// </summary>
        /// <param name="entity">The entity being reconciled</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A tuple containing whether fetch is required and the reason if it is</returns>
        protected virtual Task<(bool RequiresFetch, string? Reason)> RequiresAuth0Fetch(TEntity entity, CancellationToken cancellationToken)
        {
            return Task.FromResult<(bool RequiresFetch, string? Reason)>((false, null));
        }

        /// <inheritdoc />
        protected override async Task Reconcile(TEntity entity, CancellationToken cancellationToken)
        {
            var (tenant, api) = await SetupReconciliationContext(entity, cancellationToken);
            var tenantApiAccess = await GetOrCreateTenantApiAccessAsync(tenant, cancellationToken);

            entity = await EnsureEntityHasId(entity, api, cancellationToken);

            var lastConf = await RetrieveCurrentAuth0State(entity, api, tenantApiAccess, cancellationToken);

            lastConf = await ApplyUpdatesIfNeeded(entity, tenantApiAccess, api, lastConf, cancellationToken);

            await FinalizeReconciliation(entity, api, lastConf, cancellationToken);
        }

        private async Task<(V1Tenant tenant, IManagementApiClient api)> SetupReconciliationContext(TEntity entity, CancellationToken cancellationToken)
        {
            EnsureValidTenantRef(entity);
            EnsureSpecConfExists(entity);

            var tenant = await ResolveTenantRef(entity, cancellationToken);
            if (tenant is null)
                throw new InvalidOperationException($"Failed to setup reconciliation context for {EntityTypeName} {entity.Namespace()}/{entity.Name()} - missing a tenant (cannot resolve TenantRef).");

            var api = await GetTenantApiClientAsync(tenant, cancellationToken);
            if (api is null)
            {
                Logger.LogErrorJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} failed to retrieve API client.", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name()
                });
                throw new InvalidOperationException($"Failed to setup reconciliation context for {EntityTypeName} {entity.Namespace()}/{entity.Name()} - failed to retrieve API client.");
            }

            // ensure we hold a reference to the tenant
            var md = entity.EnsureMetadata();
            var an = md.EnsureAnnotations();
            an["kubernetes.auth0.com/tenant-uid"] = tenant.Uid();

            return (tenant, api);
        }

        private async Task<TEntity> EnsureEntityHasId(TEntity entity, IManagementApiClient api, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(entity.Status.Id))
            {
                return await ValidateExistingEntityId(entity);
            }

            Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} has not yet been reconciled, checking if entity exists in Auth0.", new
            {
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(),
                entityName = entity.Name()
            });

            var entityId = await Find(api, entity, entity.Spec, entity.Namespace(), cancellationToken);

            if (entityId is null)
            {
                return await CreateNewEntity(entity, api, cancellationToken);
            }

            return await AssociateWithExistingEntity(entity, entityId, cancellationToken);
        }

        private Task<TEntity> ValidateExistingEntityId(TEntity entity)
        {
            if (string.IsNullOrWhiteSpace(entity.Status.Id))
            {
                var errorMessage = $"Failed to validate existing entity ID for {EntityTypeName} {entity.Namespace()}/{entity.Name()} - ID is still not set after attempting to find or create entity.";
                Logger.LogErrorJson(errorMessage, new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name()
                });
                throw new InvalidOperationException(errorMessage);
            }
            return Task.FromResult(entity);
        }

        private async Task<TEntity> CreateNewEntity(TEntity entity, IManagementApiClient api, CancellationToken cancellationToken)
        {
            Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} could not be located, creating.", new
            {
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(),
                entityName = entity.Name()
            });

            ValidateCreatePolicy(entity);
            var init = ValidateAndGetInitConfiguration(entity);

            entity.Status.Id = await Create(api, init, entity.Namespace(), cancellationToken);
            Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} created with {entity.Status.Id}", new
            {
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(),
                entityName = entity.Name(),
                createdId = entity.Status.Id
            });

            return await UpdateKubernetesStatus(entity, "creation", cancellationToken);
        }

        private void ValidateCreatePolicy(TEntity entity)
        {
            if (entity.HasPolicy(V1EntityPolicyType.Create) == false)
            {
                Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} does not support creation.", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name()
                });
                throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} does not support creation.");
            }
        }

        private TConf ValidateAndGetInitConfiguration(TEntity entity)
        {
            var init = entity.Spec.Init ?? entity.Spec.Conf;
            if (init is null)
                throw new InvalidOperationException($"Failed to validate and get init configuration for {EntityTypeName} {entity.Namespace()}/{entity.Name()} - has no Init or Conf configuration.");

            if (ValidateCreate(init) is string msg)
            {
                Logger.LogErrorJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} is invalid: {msg}", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    validationMessage = msg
                });
                throw new InvalidOperationException($"Failed to validate and get init configuration for {EntityTypeName} {entity.Namespace()}/{entity.Name()} - is invalid: {msg}");
            }
            return init;
        }

        private async Task<TEntity> AssociateWithExistingEntity(TEntity entity, string entityId, CancellationToken cancellationToken)
        {
            entity.Status.Id = entityId;
            Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} found with {entity.Status.Id}", new
            {
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(),
                entityName = entity.Name(),
                foundId = entity.Status.Id
            });

            return await UpdateKubernetesStatus(entity, "finding entity", cancellationToken);
        }

        private async Task<TEntity> UpdateKubernetesStatus(TEntity entity, string operation, CancellationToken cancellationToken)
        {
            try
            {
                return await Kube.UpdateStatusAsync(entity, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} failed to update Kubernetes status after {operation}: {ex.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    operation,
                    errorMessage = ex.Message
                }, ex);
                throw;
            }
        }

        private async Task<Hashtable?> RetrieveCurrentAuth0State(TEntity entity, IManagementApiClient api, ITenantApiAccess tenantApiAccess, CancellationToken cancellationToken)
        {
            var (needsAuth0Fetch, reason) = await DetermineIfAuth0FetchIsNeeded(entity, cancellationToken);

            if (needsAuth0Fetch)
            {
                return await FetchFromAuth0WithValidation(entity, api, tenantApiAccess, reason, cancellationToken);
            }

            return GetCachedConfiguration(entity);
        }

        private async Task<(bool needsAuth0Fetch, string reason)> DetermineIfAuth0FetchIsNeeded(TEntity entity, CancellationToken cancellationToken)
        {
            var isFirstReconciliation = entity.Status.LastConf is null;
            var hasLocalChanges = entity.Spec.Conf is { } currentConf && !isFirstReconciliation && HasConfigurationChangedQuiet(entity, entity.Status.LastConf, currentConf);
            var (entityControllerRequiresFetch, entityControllerReason) = await RequiresAuth0Fetch(entity, cancellationToken);
            var needsAuth0Fetch = hasLocalChanges || isFirstReconciliation || entityControllerRequiresFetch;

            var reason = isFirstReconciliation ? "first reconciliation" :
                        hasLocalChanges ? "local configuration changes detected" :
                        entityControllerRequiresFetch ? $"requested by the entity controller: {entityControllerReason}" :
                        "unknown";

            return (needsAuth0Fetch, reason);
        }

        private async Task<Hashtable?> FetchFromAuth0WithValidation(TEntity entity, IManagementApiClient api, ITenantApiAccess tenantApiAccess, string reason, CancellationToken cancellationToken)
        {
            Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} checking if entity exists in Auth0 with ID {entity.Status.Id} (reason: {reason})", new
            {
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(),
                entityName = entity.Name(),
                entityId = entity.Status.Id,
                checkReason = reason
            });

            if (entity.Status.Id is null)
            {
                var errorMessage = $"Failed to fetch {EntityTypeName} from Auth0 - {EntityTypeName} {entity.Namespace()}/{entity.Name()} has no ID in status.";
                Logger.LogErrorJson(errorMessage, new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name()
                });
                throw new InvalidOperationException(errorMessage);
            }
            var lastConf = await Get(api, entity.Status.Id, entity.Namespace(), cancellationToken);
            if (lastConf is null)
            {
                await HandleMissingAuth0Entity(entity, cancellationToken);
                return lastConf;
            }

            // Allow derived controllers to enrich the Auth0 state (e.g., client controllers can add connection data)
            lastConf = await EnrichAuth0State(entity, api, tenantApiAccess, lastConf, cancellationToken);

            return lastConf;
        }

        /// <summary>
        /// Virtual method that allows derived controllers to enrich the Auth0 state after retrieval.
        /// Base implementation returns the state unchanged.
        /// Override in derived controllers to add entity-specific enrichment logic.
        /// </summary>
        /// <param name="entity">The entity being processed</param>
        /// <param name="api">The Auth0 Management API client</param>
        /// <param name="tenantApiAccess">Tenant API access for credentials and tokens</param>
        /// <param name="auth0State">The current Auth0 state hashtable</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The enriched Auth0 state</returns>
        protected virtual async Task<Hashtable> EnrichAuth0State(TEntity entity, IManagementApiClient api, ITenantApiAccess tenantApiAccess, Hashtable auth0State, CancellationToken cancellationToken)
        {
            // Base implementation - no enrichment needed
            await Task.CompletedTask;
            return auth0State;
        }

        private async Task HandleMissingAuth0Entity(TEntity entity, CancellationToken cancellationToken)
        {
            Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} not found in Auth0, clearing status and scheduling recreation", new
            {
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(),
                entityName = entity.Name()
            });

            entity.Status.LastConf = null;
            entity.Status.Id = null;

            await UpdateKubernetesStatus(entity, "reset", cancellationToken);
            throw new RetryException($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} has missing API object, invalidating.");
        }

        private Hashtable? GetCachedConfiguration(TEntity entity)
        {
            Logger.LogDebugJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} no local configuration changes detected - skipping Auth0 API call and update", new
            {
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(),
                entityName = entity.Name()
            });
            return entity.Status.LastConf;
        }

        private async Task<Hashtable?> ApplyUpdatesIfNeeded(TEntity entity, ITenantApiAccess tenantApiAccess, IManagementApiClient api, Hashtable? lastConf, CancellationToken cancellationToken)
        {
            if (!entity.HasPolicy(V1EntityPolicyType.Update))
            {
                LogUpdateNotSupported(entity);
                return lastConf;
            }

            if (entity.Spec.Conf is not { } conf)
            {
                return lastConf;
            }

            var (needsUpdate, driftingFields) = DetermineIfUpdateIsNeeded(entity, lastConf, conf);

            if (needsUpdate)
            {
                return await PerformUpdate(entity, tenantApiAccess, api, lastConf, conf, driftingFields, cancellationToken);
            }

            return lastConf;
        }

        private void LogUpdateNotSupported(TEntity entity)
        {
            Logger.LogDebugJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} does not support update.", new
            {
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(),
                entityName = entity.Name()
            });
        }

        private (bool needsUpdate, List<string> driftingFields) DetermineIfUpdateIsNeeded(TEntity entity, Hashtable? lastConf, TConf conf)
        {
            var isFirstReconciliation = entity.Status.LastConf is null;

            bool needsUpdate;
            List<string> driftingFields;
            if (isFirstReconciliation)
            {
                var (needsUpdateResult, driftingFieldsResult) = HasConfigurationChanged(entity, lastConf, conf);
                needsUpdate = needsUpdateResult;
                driftingFields = driftingFieldsResult;
                LogFirstReconciliationDecision(entity, needsUpdate);
            }
            else
            {
                var (needsUpdateResult, driftingFieldsResult) = HasConfigurationChanged(entity, entity.Status.LastConf, conf);
                needsUpdate = needsUpdateResult;
                driftingFields = driftingFieldsResult;
                LogSubsequentReconciliationDecision(entity, needsUpdate);
            }

            return (needsUpdate, driftingFields);
        }

        private void LogFirstReconciliationDecision(TEntity entity, bool needsUpdate)
        {
            if (needsUpdate)
            {
                Logger.LogWarningJson($"*** {EntityTypeName} {entity.Namespace()}/{entity.Name()} DRIFT DETECTED *** First reconciliation - configuration drift detected between Auth0 and desired state - applying updates", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    reconciliationType = "first",
                    action = "applying updates",
                    driftDetected = true
                });
            }
            else
            {
                Logger.LogDebugJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} first reconciliation - Auth0 state matches desired state - skipping update", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    reconciliationType = "first",
                    action = "skipping update"
                });
            }
        }

        private void LogSubsequentReconciliationDecision(TEntity entity, bool needsUpdate)
        {
            if (needsUpdate)
            {
                Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} local configuration changes detected - applying updates to Auth0", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    changeType = "local configuration changes",
                    action = "applying updates"
                });
            }
            else
            {
                Logger.LogDebugJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} no local configuration changes detected - skipping update", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    changeType = "no changes",
                    action = "skipping update"
                });
            }
        }

        private async Task<Hashtable> PerformUpdate(TEntity entity, ITenantApiAccess tenantApiAccess, IManagementApiClient api, Hashtable? lastConf, TConf conf, List<string> driftingFields, CancellationToken cancellationToken)
        {
            await Update(api, entity.Status.Id ?? throw new InvalidOperationException($"Entity {entity.Namespace()}/{entity.Name()} has no ID in status."), lastConf, conf, driftingFields, entity.Namespace(), tenantApiAccess, cancellationToken);

            // Update lastConf to reflect the applied configuration to prevent false drift detection
            var appliedJson = TransformToNewtonsoftJson<TConf, object>(conf);
            return TransformToSystemTextJson<Hashtable>(appliedJson);
        }

        private async Task FinalizeReconciliation(TEntity entity, IManagementApiClient api, Hashtable? lastConf, CancellationToken cancellationToken)
        {
            await ApplyStatus(api, entity, lastConf ?? new Hashtable(), entity.Namespace(), cancellationToken);
            await UpdateKubernetesStatus(entity, "applying configuration", cancellationToken);
            ScheduleNextReconciliation(entity);
        }

        private void ScheduleNextReconciliation(TEntity entity)
        {
            var interval = _options.Value.Reconciliation.Interval;
            Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} scheduling next reconciliation in {interval.TotalSeconds}s", new
            {
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(),
                entityName = entity.Name(),
                intervalSeconds = interval.TotalSeconds
            });
            Requeue(entity, interval);
        }

        private async Task<V1Tenant?> ResolveTenantRef(TEntity entity, CancellationToken cancellationToken)
        {
            var tenant = await ResolveTenantRef(entity.Spec.TenantRef, entity.Namespace(), cancellationToken);
            if (tenant is null)
            {
                var errorMessage = $"Failed to resolve tenant ref for {EntityTypeName} {entity.Namespace()}/{entity.Name()} - missing a tenant.";
                Logger.LogErrorJson(errorMessage, new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name()
                });
                throw new InvalidOperationException(errorMessage);
            }

            return tenant;
        }

        private void EnsureSpecConfExists(TEntity entity)
        {
            if (entity.Spec.Conf is null)
            {
                Logger.LogErrorJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} missing configuration.", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name()
                });
                throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} missing configuration.");
            }
        }

        private void EnsureValidTenantRef(TEntity entity)
        {
            if (entity.Spec.TenantRef is null)
            {
                Logger.LogErrorJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} missing a tenant reference.", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name()
                });
                throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} missing a tenant reference.");
            }
        }


        /// <summary>
        /// Applies any modification to the entity status just before saving it.
        /// </summary>
        /// <param name="api">Auth0 Management API client</param>
        /// <param name="entity">The entity being processed</param>
        /// <param name="lastConf">The last known configuration from Auth0</param>
        /// <param name="defaultNamespace">Default namespace for the entity</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A task representing the asynchronous status application operation</returns>
        protected virtual Task ApplyStatus(IManagementApiClient api, TEntity entity, Hashtable lastConf, string defaultNamespace, CancellationToken cancellationToken)
        {
            entity.Status.LastConf = lastConf;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Implement this method to delete a specific entity from the API.
        /// </summary>
        /// <param name="api">Auth0 Management API client</param>
        /// <param name="id">The entity ID to delete</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A task representing the asynchronous delete operation</returns>
        protected abstract Task Delete(IManagementApiClient api, string id, CancellationToken cancellationToken);

        /// <inheritdoc />
        public override sealed async Task DeletedAsync(TEntity entity, CancellationToken cancellationToken)
        {
            try
            {
                if (entity.Spec.TenantRef is null)
                    throw new InvalidOperationException($"Failed to delete {EntityTypeName} {entity.Namespace()}/{entity.Name()} - missing a tenant reference.");

                var tenant = await ResolveTenantRef(entity.Spec.TenantRef, entity.Namespace(), cancellationToken);
                if (tenant is null)
                    throw new InvalidOperationException($"Failed to delete {EntityTypeName} {entity.Namespace()}/{entity.Name()} - missing a tenant.");

                var api = await GetTenantApiClientAsync(tenant, cancellationToken);
                if (api is null)
                    throw new InvalidOperationException($"Failed to delete {EntityTypeName} {entity.Namespace()}/{entity.Name()} - failed to retrieve API client.");

                if (string.IsNullOrWhiteSpace(entity.Status.Id))
                {
                    Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} has no known ID, skipping delete (reason: entity was never successfully created in Auth0)", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        operation = "delete",
                        reason = "no_known_id",
                        skipped = true
                    });
                    return;
                }

                var self = await Get(api, entity.Status.Id, entity.Namespace(), cancellationToken);
                if (self is null)
                {
                    Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} with ID {entity.Status.Id} not found in Auth0, skipping delete (reason: already deleted externally)", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        entityId = entity.Status.Id,
                        operation = "delete",
                        reason = "already_deleted_externally",
                        skipped = true
                    });
                    return;
                }

                // reject deletion if disallowed by policy
                if (entity.HasPolicy(V1EntityPolicyType.Delete) == false)
                {
                    Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} does not support delete (reason: Delete policy not enabled)", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        operation = "delete",
                        reason = "delete_policy_not_enabled",
                        supported = false
                    });
                }
                else
                {
                    Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} initiating deletion from Auth0 with ID: {entity.Status.Id} (reason: Kubernetes entity was deleted)", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        entityId = entity.Status.Id,
                        operation = "delete",
                        reason = "kubernetes_entity_deleted",
                        status = "initiating"
                    });
                    await Delete(api, entity.Status.Id, cancellationToken);
                    Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} deletion completed successfully", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        operation = "delete",
                        status = "completed_successfully"
                    });
                }
            }
            catch (ErrorApiException e)
            {
                try
                {
                    Logger.LogErrorJson($"API error deleting {EntityTypeName} {entity.Namespace()}/{entity.Name()}: {e.ApiError?.Message}", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        operation = "delete",
                        errorType = "api_error",
                        errorMessage = e.ApiError?.Message
                    }, e);
                    await DeletingWarningAsync(entity, "ApiError", e.ApiError?.Message ?? "", cancellationToken);
                }
                catch (Exception e2)
                {
                    Logger.LogCriticalJson($"Unexpected exception creating event: {e2.Message}", new
                    {
                        operation = "create_event",
                        errorType = "unexpected_exception",
                        errorMessage = e2.Message
                    }, e2);
                }
            }
            catch (RateLimitApiException e)
            {
                try
                {
                    Logger.LogErrorJson($"Rate limit hit deleting {EntityTypeName} {entity.Namespace()}/{entity.Name()}: {e.ApiError?.Message ?? e.Message}", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        operation = "delete",
                        errorType = "rate_limit",
                        errorMessage = e.ApiError?.Message ?? e.Message
                    }, e);
                    await DeletingWarningAsync(entity, "RateLimit", e.ApiError?.Message ?? "", cancellationToken);
                }
                catch (Exception e2)
                {
                    Logger.LogCriticalJson($"Unexpected exception creating event: {e2.Message}", new
                    {
                        operation = "create_event",
                        errorType = "unexpected_exception",
                        errorMessage = e2.Message
                    }, e2);
                }

                // calculate next attempt time, floored to one minute
                var n = e.RateLimit?.Reset is DateTimeOffset r ? r - DateTimeOffset.Now : TimeSpan.FromMinutes(1);
                if (n < TimeSpan.FromMinutes(1))
                    n = TimeSpan.FromMinutes(1);

                Logger.LogInformationJson($"Rescheduling delete after {n}", new
                {
                    operation = "reschedule_delete",
                    timespan = n
                });
                Requeue(entity, n);
            }
            catch (RetryException e)
            {
                try
                {
                    Logger.LogErrorJson($"Retry hit deleting {EntityTypeName} {entity.Namespace()}/{entity.Name()}: {e.Message}", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        operation = "delete",
                        errorType = "retry",
                        errorMessage = e.Message
                    }, e);
                    await DeletingWarningAsync(entity, "Retry", e.Message, cancellationToken);
                }
                catch (Exception e2)
                {
                    Logger.LogCriticalJson($"Unexpected exception creating event: {e2.Message}", new
                    {
                        operation = "create_event",
                        errorType = "unexpected_exception",
                        errorMessage = e2.Message
                    }, e2);
                }

                Logger.LogInformationJson($"Rescheduling delete after {TimeSpan.FromMinutes(1)}", new
                {
                    operation = "reschedule_delete",
                    timespan = TimeSpan.FromMinutes(1)
                });
                Requeue(entity, TimeSpan.FromMinutes(1));
            }
            catch (Exception e)
            {
                try
                {
                    Logger.LogErrorJson($"Unexpected exception deleting {EntityTypeName} {entity.Namespace()}/{entity.Name()}: {e.Message}", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        operation = "delete",
                        errorType = "unexpected_exception",
                        errorMessage = e.Message
                    }, e);
                    await DeletingWarningAsync(entity, "Unknown", e.Message, cancellationToken);
                }
                catch (Exception e2)
                {
                    Logger.LogCriticalJson($"Unexpected exception creating event: {e2.Message}", new
                    {
                        operation = "create_event",
                        errorType = "unexpected_exception",
                        errorMessage = e2.Message
                    }, e2);
                }

                throw;
            }
        }

        /// <summary>
        /// Determines if the configuration has changed by comparing the last known state with the desired configuration (without logging).
        /// This version is used for internal checks where logging drift details would be redundant.
        /// </summary>
        /// <param name="entity">The entity</param>
        /// <param name="lastConf">The last known configuration from Auth0</param>
        /// <param name="desiredConf">The desired configuration from the Kubernetes spec</param>
        /// <returns>True if changes are detected, false if configurations match</returns>
        private bool HasConfigurationChangedQuiet(TEntity entity, Hashtable? lastConf, TConf desiredConf)
        {
            try
            {
                if (lastConf is null)
                {
                    Logger.LogDebugJson($"{EntityTypeName} no previous configuration available - assuming changes exist", new
                    {
                        entityTypeName = EntityTypeName,
                        configurationStatus = "no_previous_config",
                        assumingChanges = true
                    });
                    return true;
                }

                // Convert desired configuration to Auth0 API format for comparison
                var desiredJson = TransformToNewtonsoftJson<TConf, object>(desiredConf);
                var desiredHashtable = TransformToSystemTextJson<Hashtable>(desiredJson);

                // Filter fields based on the entity's drift detection configuration
                var filteredLast = FilterFieldsForComparison(lastConf);
                var filteredDesired = FilterFieldsForComparison(desiredHashtable);

                // Compare the filtered configurations but do NOT log drift details
                var result = !AreHashtablesEqual(filteredLast, filteredDesired);

                return result;
            }
            catch (Exception ex)
            {
                Logger.LogWarningJson($"{EntityTypeName} error comparing configurations, assuming changes exist: {ex.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    operation = "configuration_comparison_quiet",
                    errorMessage = ex.Message,
                    assumingChanges = true
                });

                return true; // Safe fallback: assume changes exist
            }
        }

        /// <summary>
        /// Determines if the configuration has changed by comparing the last known state with the desired configuration.
        /// This version logs detailed drift information when changes are detected.
        /// </summary>
        /// <param name="entity">The entity</param>
        /// <param name="lastConf">The last known configuration from Auth0</param>
        /// <param name="desiredConf">The desired configuration from the Kubernetes spec</param>
        /// <returns>True if changes are detected, false if configurations match</returns>
        private (bool configurationChanged, List<string> driftingFields) HasConfigurationChanged(TEntity entity, Hashtable? lastConf, TConf desiredConf)
        {
            try
            {
                // Convert desired configuration to Auth0 API format for comparison
                var desiredHashtable = new Hashtable();
                if (desiredConf is not null)
                {
                    var desiredJson = TransformToNewtonsoftJson<TConf, object>(desiredConf);
                    desiredHashtable = TransformToSystemTextJson<Hashtable>(desiredJson);
                }


                if (lastConf is null)
                {
                    Logger.LogDebugJson($"{EntityTypeName} no previous configuration available - assuming changes exist", new
                    {
                        entityTypeName = EntityTypeName,
                        configurationStatus = "no_previous_config",
                        assumingChanges = true
                    });

                    // return all fields from desiredConf
                    var desiredConfFields = desiredHashtable.Keys.Cast<string>().ToList();
                    return (true, desiredConfFields);
                }

                // Filter fields based on the entity's drift detection configuration
                var filteredLast = FilterFieldsForComparison(lastConf);
                var filteredDesired = FilterFieldsForComparison(desiredHashtable);

                // Compare the filtered configurations
                var result = !AreHashtablesEqual(filteredLast, filteredDesired);

                var driftFieldDetails = new List<DriftFieldDetails>();
                if (result)
                {
                    driftFieldDetails = GetDriftFieldDetails(entity, filteredLast, filteredDesired);
                    LogConfigurationDifferences(entity, driftFieldDetails);
                }

                return (result, driftFieldDetails.Select(d => d.FieldName!).ToList());
            }
            catch (Exception ex)
            {
                Logger.LogWarningJson($"{EntityTypeName} error comparing configurations, assuming changes exist: {ex.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    operation = "configuration_comparison",
                    errorMessage = ex.Message,
                    assumingChanges = true
                });
                return (true, new List<string>()); // Safe fallback: assume changes exist
            }
        }

        /// <summary>
        /// Applies entity-specific post-processing to filtered configuration for comparison.
        /// Override in derived classes to add custom filtering logic.
        /// </summary>
        /// <param name="filtered">The already filtered configuration hashtable</param>
        /// <returns>The hashtable with entity-specific filtering applied</returns>
        protected virtual Hashtable PostProcessFilteredConfiguration(Hashtable filtered)
        {
            return filtered;
        }

        /// <summary>
        /// Filters fields for comparison based on the entity's drift detection configuration.
        /// Always attempts to read GetIncludedFields first, then applies GetExcludedFields.
        /// Supports nested field exclusions using dot notation (e.g., "options.userid_attribute").
        /// </summary>
        /// <param name="config">The configuration hashtable to filter</param>
        /// <returns>A new hashtable with fields filtered according to included and excluded fields</returns>
        private Hashtable FilterFieldsForComparison(Hashtable config)
        {
            var filtered = new Hashtable();
            var includedFields = GetIncludedFields();
            var excludedFields = GetExcludedFields();

            // If included fields are specified, only include those fields
            if (includedFields.Length > 0)
            {
                var includedFieldsSet = new HashSet<string>(includedFields, StringComparer.OrdinalIgnoreCase);
                foreach (DictionaryEntry entry in config)
                {
                    if (entry.Key is string key && includedFieldsSet.Contains(key))
                    {
                        filtered[key] = entry.Value;
                    }
                }
            }
            else
            {
                // Include all fields initially
                foreach (DictionaryEntry entry in config)
                {
                    filtered[entry.Key] = entry.Value;
                }
            }

            // Then apply excluded fields if any are specified
            if (excludedFields.Length > 0)
            {
                // Separate top-level and nested field exclusions
                var topLevelExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var nestedExclusions = new List<string>();

                foreach (var field in excludedFields)
                {
                    if (field.Contains('.'))
                    {
                        nestedExclusions.Add(field);
                    }
                    else
                    {
                        topLevelExclusions.Add(field);
                    }
                }

                // Remove top-level excluded fields
                var keysToRemove = new List<object>();
                foreach (DictionaryEntry entry in filtered)
                {
                    if (entry.Key is string key && topLevelExclusions.Contains(key))
                    {
                        keysToRemove.Add(entry.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    filtered.Remove(key);
                }

                // Handle nested field exclusions
                foreach (var nestedField in nestedExclusions)
                {
                    var parts = nestedField.Split('.', 2);
                    if (parts.Length == 2)
                    {
                        var parentKey = parts[0];
                        var childKey = parts[1];

                        if (filtered.ContainsKey(parentKey) && filtered[parentKey] is Hashtable parentHash)
                        {
                            // Create a copy of the parent hashtable without the excluded child field
                            var filteredParent = new Hashtable();
                            foreach (DictionaryEntry entry in parentHash)
                            {
                                if (entry.Key is string key && !key.Equals(childKey, StringComparison.OrdinalIgnoreCase))
                                {
                                    filteredParent[entry.Key] = entry.Value;
                                }
                            }
                            filtered[parentKey] = filteredParent;
                        }
                    }
                }
            }

            return PostProcessFilteredConfiguration(filtered);
        }


        /// <summary>
        /// Performs deep comparison of two hashtables to detect configuration differences.
        /// </summary>
        /// <param name="left">First hashtable to compare</param>
        /// <param name="right">Second hashtable to compare</param>
        /// <returns>True if hashtables are equal, false otherwise</returns>
        private static bool AreHashtablesEqual(Hashtable left, Hashtable right)
        {
            if (left.Count != right.Count)
                return false;

            foreach (DictionaryEntry entry in left)
            {
                if (!right.ContainsKey(entry.Key))
                    return false;

                if (!AreValuesEqual(entry.Value, right[entry.Key]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Compares two values for equality, handling nested hashtables and arrays.
        /// </summary>
        /// <param name="left">First value to compare</param>
        /// <param name="right">Second value to compare</param>
        /// <returns>True if values are equal, false otherwise</returns>
        private static bool AreValuesEqual(object? left, object? right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left is null || right is null)
                return false;

            // Handle nested hashtables
            if (left is Hashtable leftHash && right is Hashtable rightHash)
                return AreHashtablesEqual(leftHash, rightHash);

            // Handle arrays
            if (left is IEnumerable leftEnum && right is IEnumerable rightEnum &&
                !(left is string) && !(right is string))
            {
                var leftArray = leftEnum.Cast<object>().ToArray();
                var rightArray = rightEnum.Cast<object>().ToArray();

                if (leftArray.Length != rightArray.Length)
                    return false;

                for (int i = 0; i < leftArray.Length; i++)
                {
                    if (!AreValuesEqual(leftArray[i], rightArray[i]))
                        return false;
                }

                return true;
            }

            // Use standard equality comparison
            return left.Equals(right);
        }

        private enum DriftFieldType
        {
            Added,
            Modified,
            Removed
        }

        private class DriftFieldDetails
        {
            public DriftFieldType FieldType { get; set; }
            public string? FieldName { get; set; }
            public object? OldValue { get; set; }
            public object? NewValue { get; set; }

            public override string ToString()
            {
                return $"{FieldName} = {OldValue} → {NewValue}";
            }
        }

        private List<DriftFieldDetails> GetDriftFieldDetails(TEntity entity, Hashtable last, Hashtable desired)
        {
            var driftFieldDetails = new List<DriftFieldDetails>();

            // Check for added or modified fields
            foreach (DictionaryEntry entry in desired)
            {
                var key = entry.Key.ToString()!;
                if (!last.ContainsKey(entry.Key))
                {
                    driftFieldDetails.Add(new DriftFieldDetails { FieldType = DriftFieldType.Added, FieldName = key, OldValue = null, NewValue = entry.Value });
                }
                else if (!AreValuesEqual(entry.Value, last[entry.Key]))
                {
                    var oldValue = FormatValueForLogging(last[entry.Key]);
                    var newValue = FormatValueForLogging(entry.Value);
                    driftFieldDetails.Add(new DriftFieldDetails { FieldType = DriftFieldType.Modified, FieldName = key, OldValue = oldValue, NewValue = newValue });
                }
            }

            // Check for removed fields
            foreach (DictionaryEntry entry in last)
            {
                if (!desired.ContainsKey(entry.Key))
                {
                    var key = entry.Key.ToString()!;
                    driftFieldDetails.Add(new DriftFieldDetails { FieldType = DriftFieldType.Removed, FieldName = key, OldValue = entry.Value, NewValue = null });
                }
            }

            return driftFieldDetails;
        }

        /// <summary>
        /// Logs detected configuration differences with detailed field-by-field changes.
        /// </summary>
        /// <param name="entity">The entity</param>
        /// <param name="driftFieldDetails">The drift field details</param>
        private void LogConfigurationDifferences(TEntity entity, List<DriftFieldDetails> driftFieldDetails)
        {
            var addedFields = driftFieldDetails.Where(d => d.FieldType == DriftFieldType.Added).ToList();
            var modifiedFields = driftFieldDetails.Where(d => d.FieldType == DriftFieldType.Modified).ToList();
            var removedFields = driftFieldDetails.Where(d => d.FieldType == DriftFieldType.Removed).ToList();

            // Log changes by category with detailed before/after values
            if (addedFields.Count > 0)
            {
                Logger.LogWarningJson($"*** {EntityTypeName} {entity.Namespace()}/{entity.Name()} CONFIGURATION CHANGES - ADDED fields: {string.Join(", ", addedFields)}", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    changeType = "ADDED",
                    addedFields,
                    fieldCount = addedFields.Count
                });
            }

            if (modifiedFields.Count > 0)
            {
                Logger.LogWarningJson($"*** {EntityTypeName} {entity.Namespace()}/{entity.Name()} CONFIGURATION CHANGES - MODIFIED fields: {string.Join(", ", modifiedFields)}", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    changeType = "MODIFIED",
                    modifiedFields,
                    fieldCount = modifiedFields.Count
                });
            }

            if (removedFields.Count > 0)
            {
                Logger.LogWarningJson($"*** {EntityTypeName} {entity.Namespace()}/{entity.Name()} CONFIGURATION CHANGES - REMOVED fields: {string.Join(", ", removedFields)}", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    changeType = "REMOVED",
                    removedFields,
                    fieldCount = removedFields.Count
                });
            }

            // Also log a summary count
            var totalChanges = addedFields.Count + modifiedFields.Count + removedFields.Count;
            if (totalChanges > 0)
            {
                Logger.LogWarningJson($"*** {EntityTypeName} {entity.Namespace()}/{entity.Name()} CONFIGURATION DRIFT DETECTED *** {totalChanges} field changes ({addedFields.Count} added, {modifiedFields.Count} modified, {removedFields.Count} removed)", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    operation = "drift_detection",
                    totalChanges,
                    addedCount = addedFields.Count,
                    modifiedCount = modifiedFields.Count,
                    removedCount = removedFields.Count,
                    driftDetected = true
                });
            }
        }

        /// <summary>
        /// Formats a value for logging, providing detailed representation with truncation for long values.
        /// </summary>
        /// <param name="value">Value to format</param>
        /// <returns>Formatted string representation with type information</returns>
        private static string FormatValueForLogging(object? value)
        {
            if (value is null)
                return "(null)";

            // Handle different value types with more detail
            switch (value)
            {
                case string stringValue:
                    var quotedStr = $"\"{stringValue}\"";
                    return quotedStr.Length > 100 ? $"\"{stringValue[..95]}...\"" : quotedStr;

                case bool boolean:
                    return boolean.ToString().ToLowerInvariant();

                case int or long or float or double or decimal:
                    return value.ToString() ?? "(null)";

                case IEnumerable enumerable when value is not string:
                    var items = enumerable.Cast<object>().Take(5).Select(FormatValueForLogging);
                    var arrayPreview = string.Join(", ", items);
                    var count = enumerable.Cast<object>().Count();
                    return count > 5 ? $"[{arrayPreview}, ...] (total: {count} items)" : $"[{arrayPreview}]";

                case Hashtable hashtable:
                    var entries = hashtable.Cast<DictionaryEntry>().Take(3)
                        .Select(entry => $"{entry.Key}: {FormatValueForLogging(entry.Value)}");
                    var hashPreview = string.Join(", ", entries);
                    return hashtable.Count > 3 ? $"{{{hashPreview}, ...}} (total: {hashtable.Count} fields)" : $"{{{hashPreview}}}";

                default:
                    var objectStr = value.ToString() ?? "(null)";
                    var typeName = value.GetType().Name;
                    return objectStr.Length > 80 ? $"({typeName}) {objectStr[..75]}..." : $"({typeName}) {objectStr}";
            }
        }

    }

}
