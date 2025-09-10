using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Core.Extensions;
using Alethic.Auth0.Operator.Core.Models;
using Alethic.Auth0.Operator.Extensions;
using Alethic.Auth0.Operator.Models;

using Auth0.Core.Exceptions;
using Auth0.ManagementApi;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Queue;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

namespace Alethic.Auth0.Operator.Controllers
{
    /// <summary>
    /// Represents the type of Auth0 API call being performed.
    /// </summary>
    public enum Auth0ApiCallType
    {
        /// <summary>
        /// Read operation - fetches data from Auth0 without modifying it.
        /// </summary>
        Read,
        
        /// <summary>
        /// Write operation - creates, updates, or deletes data in Auth0.
        /// </summary>
        Write
    }

    public abstract class V1Controller<TEntity, TSpec, TStatus, TConf> : V1ControllerBase, IEntityController<TEntity>
        where TEntity : IKubernetesObject<V1ObjectMeta>, V1Entity<TSpec, TStatus, TConf>
        where TSpec : V1EntitySpec<TConf>
        where TStatus : V1EntityStatus
        where TConf : class
    {
        private const int MinSecretRetryDelaySeconds = 10;
        private const int MaxSecretRetryDelaySeconds = 20;

        static readonly Newtonsoft.Json.JsonSerializer _newtonsoftJsonSerializer = Newtonsoft.Json.JsonSerializer.CreateDefault();
        static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new SimplePrimitiveHashtableConverter() } };
        static readonly SemaphoreSlim _getTenantApiClient = new(1);

        readonly IKubernetesClient _kube;
        readonly EntityRequeue<TEntity> _requeue;
        readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="kube"></param>
        /// <param name="requeue"></param>
        /// <param name="cache"></param>
        /// <param name="logger"></param>
        public V1Controller(IKubernetesClient kube, EntityRequeue<TEntity> requeue, IMemoryCache cache, ILogger logger)
            : base(kube, logger)
        {
            _kube = kube ?? throw new ArgumentNullException(nameof(kube));
            _requeue = requeue ?? throw new ArgumentNullException(nameof(requeue));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the type name of the entity used in messages.
        /// </summary>
        protected abstract string EntityTypeName { get; }

        /// <summary>
        /// Gets the Kubernetes API client.
        /// </summary>
        protected IKubernetesClient Kube => _kube;

        /// <summary>
        /// Gets the requeue function for the entity controller.
        /// </summary>
        protected EntityRequeue<TEntity> Requeue => _requeue;

        /// <summary>
        /// Gets the logger.
        /// </summary>
        protected ILogger Logger => _logger;

        /// <summary>
        /// Logs Auth0 API call information in JSON format immediately before the API call is made.
        /// </summary>
        /// <param name="message">The content of the log message</param>
        /// <param name="apiCallType">The type of API call</param>
        /// <param name="entityType">The Auth0 entity type being operated on</param>
        /// <param name="entityName">The name of the Kubernetes entity</param>
        /// <param name="entityNamespace">The namespace of the Kubernetes entity</param>
        /// <param name="purpose">The purpose of this Auth0 API invocation for better observability</param>
        protected void LogAuth0ApiCall(string message, Auth0ApiCallType apiCallType, string entityType, string entityName, string entityNamespace, string purpose)
        {
            var logEntry = new
            {
                timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                message = message,
                auth0ApiCallType = apiCallType.ToString().ToLowerInvariant(),
                entityTypeName = entityType,
                entityName = entityName,
                entityNamespace = entityNamespace,
                auth0ApiCallPurpose = purpose
            };
            
            var jsonLog = System.Text.Json.JsonSerializer.Serialize(logEntry);
            Logger.LogInformation(jsonLog);
        }

        /// <summary>
        /// Attempts to resolve the secret document referenced by the secret reference.
        /// </summary>
        /// <param name="secretRef"></param>
        /// <param name="defaultNamespace"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<V1Secret?> ResolveSecretRef(V1SecretReference? secretRef, string defaultNamespace, CancellationToken cancellationToken)
        {
            if (secretRef is null)
                return null;

            if (string.IsNullOrWhiteSpace(secretRef.Name))
                throw new InvalidOperationException($"Secret reference {secretRef} has no name.");

            var ns = string.IsNullOrEmpty(secretRef.NamespaceProperty) ? defaultNamespace : secretRef.NamespaceProperty;
            if (string.IsNullOrWhiteSpace(ns))
                throw new InvalidOperationException($"Secret reference {secretRef} has no discovered namesace.");

            var secret = await _kube.GetAsync<V1Secret>(secretRef.Name, ns, cancellationToken);
            if (secret is null)
                return null;

            return secret;
        }

        /// <summary>
        /// Attempts to resolve the tenant document referenced by the tenant reference.
        /// </summary>
        /// <param name="tenantRef"></param>
        /// <param name="defaultNamespace"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<V1Tenant?> ResolveTenantRef(V1TenantReference? tenantRef, string defaultNamespace, CancellationToken cancellationToken)
        {
            if (tenantRef is null)
                return null;

            if (string.IsNullOrWhiteSpace(tenantRef.Name))
                throw new InvalidOperationException($"Tenant reference {tenantRef} has no name.");

            var ns = string.IsNullOrEmpty(tenantRef.Namespace) ? defaultNamespace : tenantRef.Namespace;
            if (string.IsNullOrWhiteSpace(ns))
                throw new InvalidOperationException($"Tenant reference {tenantRef} has no discovered namesace.");

            var tenant = await _kube.GetAsync<V1Tenant>(tenantRef.Name, ns, cancellationToken);
            if (tenant is null)
                throw new RetryException($"Tenant reference {tenantRef} cannot be resolved.");

            return tenant;
        }

        /// <summary>
        /// Extracts tenant domain from a tenant entity with defensive coding for cache salt usage.
        /// </summary>
        /// <param name="entity">Entity to extract tenant domain from</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Tenant domain for cache salt, or "unknown-tenant" if unavailable</returns>
        protected async Task<string> GetTenantDomainForCacheSalt(TEntity? entity, CancellationToken cancellationToken)
        {
            const string fallbackDomain = "unknown-tenant";

            try
            {
                // Use dynamic access to get Spec.TenantRef since we can't constrain TEntity properly
                // This is safe because all entities using this method have the same structure
                var spec = (entity as dynamic)?.Spec;
                var tenantRef = spec?.TenantRef as V1TenantReference;
                
                if (tenantRef == null)
                {
                    return fallbackDomain;
                }

                var tenant = await ResolveTenantRef(tenantRef, entity.Namespace(), cancellationToken);
                return tenant?.Spec?.Auth?.Domain ?? fallbackDomain;
            }
            catch (Exception)
            {
                // If tenant resolution fails, fall back to a safe default
                return fallbackDomain;
            }
        }

        /// <summary>
        /// Attempts to resolve the client document referenced by the client reference.
        /// </summary>
        /// <param name="api"></param>
        /// <param name="clientRef"></param>
        /// <param name="defaultNamespace"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<V1Client?> ResolveClientRef(IManagementApiClient api, V1ClientReference? clientRef, string defaultNamespace, CancellationToken cancellationToken)
        {
            if (clientRef is null)
                return null;

            if (string.IsNullOrWhiteSpace(clientRef.Name))
                throw new InvalidOperationException($"Client reference has no name.");

            var ns = string.IsNullOrEmpty(clientRef.Namespace) ? defaultNamespace : clientRef.Namespace;
            if (string.IsNullOrWhiteSpace(ns))
                throw new InvalidOperationException($"Client reference has no discovered namesace.");

            var client = await _kube.GetAsync<V1Client>(clientRef.Name, ns, cancellationToken);
            if (client is null)
                throw new RetryException($"Client reference cannot be resolved.");

            return client;
        }

        /// <summary>
        /// Attempts to resolve the client reference to client ID.
        /// </summary>
        /// <param name="api"></param>
        /// <param name="clientRef"></param>
        /// <param name="defaultNamespace"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected async Task<string?> ResolveClientRefToId(IManagementApiClient api, V1ClientReference? clientRef, string defaultNamespace, CancellationToken cancellationToken)
        {
            if (clientRef is null)
                return null;

            if (clientRef.Id is { } id && string.IsNullOrWhiteSpace(id) == false)
                return id;

            Logger.LogDebugJson($"Attempting to resolve ClientRef {clientRef.Namespace}/{clientRef.Name}.", new { 
                clientRefNamespace = clientRef.Namespace, 
                clientRefName = clientRef.Name 
            });

            var client = await ResolveClientRef(api, clientRef, defaultNamespace, cancellationToken);
            if (client is null)
                throw new InvalidOperationException($"Could not resolve ClientRef {clientRef}.");
            if (string.IsNullOrWhiteSpace(client.Status.Id))
                throw new RetryException($"Referenced Client {client.Namespace()}/{client.Name()} has not been reconciled.");

            Logger.LogDebugJson($"Resolved ClientRef {clientRef.Namespace}/{clientRef.Name} to {client.Status.Id}.", new { 
                clientRefNamespace = clientRef.Namespace, 
                clientRefName = clientRef.Name, 
                resolvedId = client.Status.Id 
            });
            return client.Status.Id;
        }

        /// <summary>
        /// Attempts to resolve the resource server document referenced by the resource server reference.
        /// </summary>
        /// <param name="api"></param>
        /// <param name="resourceServerRef"></param>
        /// <param name="defaultNamespace"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<V1ResourceServer?> ResolveResourceServerRef(IManagementApiClient api, V1ResourceServerReference? resourceServerRef, string defaultNamespace, CancellationToken cancellationToken)
        {
            if (resourceServerRef is null)
                return null;

            var ns = string.IsNullOrEmpty(resourceServerRef.Namespace) ? defaultNamespace : resourceServerRef.Namespace;
            if (string.IsNullOrWhiteSpace(ns))
                throw new InvalidOperationException($"ResourceServer reference has no namespace.");

            if (string.IsNullOrWhiteSpace(resourceServerRef.Name))
                throw new InvalidOperationException($"ResourceServer reference has no name.");

            var resourceServer = await _kube.GetAsync<V1ResourceServer>(resourceServerRef.Name, ns, cancellationToken);
            if (resourceServer is null)
                throw new RetryException($"ResourceServer reference cannot be resolved.");

            return resourceServer;
        }

        /// <summary>
        /// Attempts to resolve the list of client references to client IDs.
        /// </summary>
        /// <param name="api"></param>
        /// <param name="reference"></param>
        /// <param name="defaultNamespace"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected async Task<string?> ResolveResourceServerRefToIdentifier(IManagementApiClient api, V1ResourceServerReference? reference, string defaultNamespace, CancellationToken cancellationToken)
        {
            if (reference is null)
                return null;

            // identifier is specified directly by reference
            if (reference.Identifier is { } identifier && string.IsNullOrWhiteSpace(identifier) == false)
                return identifier;

            // id is specified by reference, lookup identifier
            if (reference.Id is { } id && string.IsNullOrWhiteSpace(id) == false)
            {
                LogAuth0ApiCall($"Getting Auth0 resource server by reference ID: {id}", Auth0ApiCallType.Read, "A0ResourceServer", id, defaultNamespace, "resolve_resource_server_reference");
                var self = await api.ResourceServers.GetAsync(id, cancellationToken);
                if (self is null)
                    throw new InvalidOperationException($"Failed to resolve ResourceServer reference {id}.");

                return self.Identifier;
            }

            Logger.LogInformationJson($"Attempting to resolve ResourceServer reference {reference.Namespace}/{reference.Name}.", new { 
                resourceServerNamespace = reference.Namespace, 
                resourceServerName = reference.Name 
            });

            var resourceServer = await ResolveResourceServerRef(api, reference, defaultNamespace, cancellationToken);
            if (resourceServer is null)
                throw new InvalidOperationException($"Could not resolve ResourceServerRef {reference}.");

            if (resourceServer.Status.Identifier is null)
                throw new RetryException($"Referenced ResourceServer {resourceServer.Namespace()}/{resourceServer.Name()} has not been reconcilled.");

            Logger.LogInformationJson($"Resolved ResourceServer reference {reference.Namespace}/{reference.Name} to {resourceServer.Status.Identifier}.", new { 
                resourceServerNamespace = reference.Namespace, 
                resourceServerName = reference.Name, 
                resolvedIdentifier = resourceServer.Status.Identifier 
            });
            return resourceServer.Status.Identifier;
        }

        /// <summary>
        /// Gets an active <see cref="ManagementApiClient"/> for the specified tenant.
        /// IMPORTANT: Do not cache the returned client, call this method each time you need a client.
        /// </summary>
        /// <param name="tenant"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<IManagementApiClient> GetTenantApiClientAsync(V1Tenant tenant, CancellationToken cancellationToken)
        {
            Logger.LogInformationJson($"Retrieving Auth0 API client for tenant {tenant.Namespace()}/{tenant.Name()}", new { 
                tenantNamespace = tenant.Namespace(), 
                tenantName = tenant.Name() 
            });
            
            await _getTenantApiClient.WaitAsync(cancellationToken);
            try
            {
                Logger.LogInformationJson($"Creating new Auth0 API client for tenant {tenant.Namespace()}/{tenant.Name()}", new { 
                    tenantNamespace = tenant.Namespace(), 
                    tenantName = tenant.Name() 
                });
                
                var tenantApiAccess = await GetOrCreateTenantApiAccessAsync(tenant, cancellationToken);
                var accessToken = await tenantApiAccess.GetAccessTokenAsync(cancellationToken);
                
                var api = new ManagementApiClient(accessToken, tenantApiAccess.BaseUri);
                
                return api;
            }
            finally
            {
                _getTenantApiClient.Release();
            }
        }

        /// <summary>
        /// Updates the Reconcile event to a warning.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected async Task ReconcileSuccessAsync(TEntity entity, CancellationToken cancellationToken)
        {
            try
            {
                await _kube.CreateAsync(new Eventsv1Event(
                        DateTime.Now,
                        metadata: new V1ObjectMeta(namespaceProperty: entity.Namespace(), generateName: "auth0"),
                        reportingController: "kubernetes.auth0.com/operator",
                        reportingInstance: Dns.GetHostName(),
                        regarding: entity.MakeObjectReference(),
                        action: "Reconcile",
                        type: "Normal",
                        reason: "Success"),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"Failed to create Kubernetes success event for {EntityTypeName} {entity.Namespace()}/{entity.Name()}: {ex.Message}", new { 
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(), 
                    entityName = entity.Name(), 
                    errorMessage = ex.Message 
                }, ex);
                // Don't rethrow - event creation failure shouldn't block reconciliation
            }
        }

        /// <summary>
        /// Updates the Reconcile event to a warning.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="reason"></param>
        /// <param name="note"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected async Task ReconcileWarningAsync(TEntity entity, string reason, string note, CancellationToken cancellationToken)
        {
            try
            {
                await _kube.CreateAsync(new Eventsv1Event(
                        DateTime.Now,
                        metadata: new V1ObjectMeta(namespaceProperty: entity.Namespace(), generateName: "auth0"),
                        reportingController: "kubernetes.auth0.com/operator",
                        reportingInstance: Dns.GetHostName(),
                        regarding: entity.MakeObjectReference(),
                        action: "Reconcile",
                        type: "Warning",
                        reason: reason,
                        note: note),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"Failed to create Kubernetes warning event for {EntityTypeName} {entity.Namespace()}/{entity.Name()}: {ex.Message}", new { 
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(), 
                    entityName = entity.Name(), 
                    errorMessage = ex.Message 
                }, ex);
                // Don't rethrow - event creation failure shouldn't block reconciliation
            }
        }

        /// <summary>
        /// Updates the Deleting event to a warning.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="reason"></param>
        /// <param name="note"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected async Task DeletingWarningAsync(TEntity entity, string reason, string note, CancellationToken cancellationToken)
        {
            try
            {
                await _kube.CreateAsync(new Eventsv1Event(
                        DateTime.Now,
                        metadata: new V1ObjectMeta(namespaceProperty: entity.Namespace(), generateName: "auth0"),
                        reportingController: "kubernetes.auth0.com/operator",
                        reportingInstance: Dns.GetHostName(),
                        regarding: entity.MakeObjectReference(),
                        action: "Deleting",
                        type: "Warning",
                        reason: reason,
                        note: note),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"Failed to create Kubernetes deleting warning event for {EntityTypeName} {entity.Namespace()}/{entity.Name()}: {ex.Message}", new { 
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(), 
                    entityName = entity.Name(), 
                    errorMessage = ex.Message 
                }, ex);
                // Don't rethrow - event creation failure shouldn't block deletion
            }
        }

        /// <summary>
        /// Transforms the given Newtonsoft JSON serializable object to a System.Text.Json serializable object.
        /// </summary>
        /// <typeparam name="TFrom"></typeparam>
        /// <typeparam name="TTo"></typeparam>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <returns></returns>
        [return: NotNullIfNotNull(nameof(from))]
        protected static TTo? TransformToNewtonsoftJson<TFrom, TTo>(TFrom? from)
            where TFrom : class
            where TTo : class
        {
            if (from == null)
                return null;

            var to = _newtonsoftJsonSerializer.Deserialize<TTo>(new JsonTextReader(new StringReader(System.Text.Json.JsonSerializer.Serialize(from, _jsonSerializerOptions))));
            if (to is null)
                throw new InvalidOperationException();

            return to;
        }

        /// <summary>
        /// Transforms the given Newtonsoft JSON serializable object to a System.Text.Json serializable object.
        /// </summary>
        /// <typeparam name="TTo"></typeparam>
        /// <param name="from"></param>
        /// <returns></returns>
        [return: NotNullIfNotNull(nameof(from))]
        protected static TTo? TransformToSystemTextJson<TTo>(object? from)
            where TTo : class
        {
            if (from == null)
                return null;

            using var w = new StringWriter();
            _newtonsoftJsonSerializer.Serialize(w, from);

            var to = System.Text.Json.JsonSerializer.Deserialize<TTo>(w.ToString(), _jsonSerializerOptions);
            if (to is null)
                throw new InvalidOperationException();

            return to;
        }

        /// <summary>
        /// Implement this method to attempt the reconcillation.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="cancellationToken"></param>
        protected abstract Task<bool> Reconcile(TEntity entity, CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task ReconcileAsync(TEntity entity, CancellationToken cancellationToken)
        {
            if (entity.Name() != "mt-avalta-test")
            {
                return;
            }
            
            var startTime = DateTimeOffset.UtcNow;
            Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} starting reconciliation at {startTime}", new { 
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(), 
                entityName = entity.Name(), 
                startTime = startTime 
            });
            
            try
            {
                if (entity.Spec.Conf == null)
                {
                    Logger.LogErrorJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} is missing configuration", new { 
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(), 
                        entityName = entity.Name() 
                    });
                    throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} is missing configuration.");
                }

                var needRequeue = await Reconcile(entity, cancellationToken);
                if (needRequeue)
                {
                    Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} reconciliation requested requeue", new { 
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(), 
                        entityName = entity.Name() 
                    });
                    
                    var retryDelaySeconds = GenerateRandomSecretRetryDelay();
                    ScheduleSecretRetryReconciliation(entity, retryDelaySeconds);
                }

                var duration = DateTimeOffset.UtcNow - startTime;
                Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} reconciliation completed successfully in {duration.TotalMilliseconds}ms", new { 
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(), 
                    entityName = entity.Name(), 
                    durationMs = duration.TotalMilliseconds 
                });
                await ReconcileSuccessAsync(entity, cancellationToken);
            }
            catch (ErrorApiException e)
            {
                try
                {
                    var duration = DateTimeOffset.UtcNow - startTime;
                    Logger.LogErrorJson($"Auth0 API error during reconciliation for {EntityTypeName} {entity.Namespace()}/{entity.Name()}: Status={e.StatusCode}, ErrorCode={e.ApiError?.ErrorCode}, Message={e.ApiError?.Message}, Duration={duration.TotalMilliseconds}ms", new { 
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(), 
                        entityName = entity.Name(), 
                        statusCode = e.StatusCode.ToString(),
                        errorCode = e.ApiError?.ErrorCode,
                        apiErrorMessage = e.ApiError?.Message,
                        durationMs = duration.TotalMilliseconds 
                    }, e);
                    await ReconcileWarningAsync(entity, "ApiError", e.ApiError?.Message ?? e.Message, cancellationToken);
                }
                catch (Exception e2)
                {
                    Logger.LogCriticalJson($"Unexpected exception creating event for {EntityTypeName} {entity.Namespace()}/{entity.Name()}", new { 
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(), 
                        entityName = entity.Name() 
                    }, e2);
                }
            }
            catch (RateLimitApiException e)
            {
                try
                {
                    var duration = DateTimeOffset.UtcNow - startTime;
                    Logger.LogWarningJson($"Auth0 rate limit exceeded for {EntityTypeName} {entity.Namespace()}/{entity.Name()}: Limit={e.RateLimit?.Limit}, Remaining={e.RateLimit?.Remaining}, Reset={e.RateLimit?.Reset}, Duration={duration.TotalMilliseconds}ms", new { 
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(), 
                        entityName = entity.Name(), 
                        rateLimitLimit = e.RateLimit?.Limit,
                        rateLimitRemaining = e.RateLimit?.Remaining,
                        rateLimitReset = e.RateLimit?.Reset,
                        durationMs = duration.TotalMilliseconds 
                    });
                    await ReconcileWarningAsync(entity, "RateLimit", e.ApiError?.Message ?? e.Message, cancellationToken);
                }
                catch (Exception e2)
                {
                    Logger.LogCriticalJson($"Unexpected exception creating event for {EntityTypeName} {entity.Namespace()}/{entity.Name()}", new { 
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(), 
                        entityName = entity.Name() 
                    }, e2);
                }

                // calculate next attempt time, floored to one minute
                var n = e.RateLimit?.Reset is DateTimeOffset r ? r - DateTimeOffset.Now : TimeSpan.FromMinutes(1);
                if (n < TimeSpan.FromMinutes(1))
                    n = TimeSpan.FromMinutes(1);

                Logger.LogWarningJson($"Rate limit exceeded, rescheduling reconciliation after {n}", new { 
                    rescheduleAfter = n.ToString() 
                });
                Requeue(entity, n);
            }
            catch (RetryException e)
            {
                try
                {
                    var duration = DateTimeOffset.UtcNow - startTime;
                    Logger.LogWarningJson($"Retry required for {EntityTypeName} {entity.Namespace()}/{entity.Name()}: {e.Message}, Duration={duration.TotalMilliseconds}ms", new { 
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(), 
                        entityName = entity.Name(), 
                        retryMessage = e.Message,
                        durationMs = duration.TotalMilliseconds 
                    });
                    await ReconcileWarningAsync(entity, "Retry", e.Message, cancellationToken);
                }
                catch (Exception e2)
                {
                    Logger.LogCriticalJson($"Unexpected exception creating event for {EntityTypeName} {entity.Namespace()}/{entity.Name()}", new { 
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(), 
                        entityName = entity.Name() 
                    }, e2);
                }

                Logger.LogWarningJson($"Retry exception occurred, rescheduling reconciliation after {TimeSpan.FromMinutes(1)}", new { 
                    rescheduleAfter = TimeSpan.FromMinutes(1).ToString() 
                });
                Requeue(entity, TimeSpan.FromMinutes(1));
            }
            catch (Exception e)
            {
                try
                {
                    var duration = DateTimeOffset.UtcNow - startTime;
                    Logger.LogErrorJson($"Unexpected error reconciling {EntityTypeName} {entity.Namespace()}/{entity.Name()}: {e.GetType().Name} - {e.Message}, Duration={duration.TotalMilliseconds}ms", new { 
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(), 
                        entityName = entity.Name(), 
                        exceptionType = e.GetType().Name,
                        errorMessage = e.Message,
                        durationMs = duration.TotalMilliseconds 
                    }, e);
                    await ReconcileWarningAsync(entity, "Unknown", e.Message, cancellationToken);
                }
                catch (Exception e2)
                {
                    Logger.LogCriticalJson($"Unexpected exception creating event for {EntityTypeName} {entity.Namespace()}/{entity.Name()}", new { 
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(), 
                        entityName = entity.Name() 
                    }, e2);
                }

                throw;
            }
        }

        /// <inheritdoc />
        public abstract Task DeletedAsync(TEntity entity, CancellationToken cancellationToken);

        /// <summary>
        /// Schedules a follow-up reconciliation for secret creation.
        /// </summary>
        /// <param name="entity">The client entity</param>
        /// <param name="delaySeconds">Delay in seconds before reconciliation</param>
        private void ScheduleSecretRetryReconciliation(TEntity entity, int delaySeconds)
        {
            Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} client credentials not available, scheduling reconciliation retry in {delaySeconds}s for secret creation", new
            {
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(),
                entityName = entity.Name(),
                retryDelaySeconds = delaySeconds
            });
            
            Requeue(entity, TimeSpan.FromSeconds(delaySeconds));
        }
        
        /// <summary>
        /// Generates a random delay between MinSecretRetryDelaySeconds and MaxSecretRetryDelaySeconds for secret retry reconciliation.
        /// </summary>
        /// <returns>Random delay in seconds</returns>
        private int GenerateRandomSecretRetryDelay()
        {
            var random = new Random();
            return random.Next(MinSecretRetryDelaySeconds, MaxSecretRetryDelaySeconds + 1);
        }
    }

}
