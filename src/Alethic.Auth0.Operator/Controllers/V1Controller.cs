using System;
using System.Collections.Generic;
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
        private const int MinRetryDelaySeconds = 60;
        private const int MaxRetryDelaySeconds = 300;
        protected const int MaxTenantChangeRetryAttempts = 2;

        /// <summary>
        /// Base delay (seconds) used by <see cref="GenerateExceptionRetryDelay"/> for the
        /// always-requeue-on-exception path in <see cref="ReconcileAsync"/>. The delay is
        /// "<see cref="ExceptionRetryBaseSeconds"/> ±25% jitter" — i.e. 22.5s–37.5s with the
        /// current 30s base.
        /// </summary>
        private const int ExceptionRetryBaseSeconds = 30;

        static readonly Newtonsoft.Json.JsonSerializer _newtonsoftJsonSerializer = Newtonsoft.Json.JsonSerializer.CreateDefault();
        static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new SimplePrimitiveHashtableConverter() } };

        /// <summary>
        /// Serializer used by <see cref="EmitAuth0ApiLog"/>. Matches the camelCase convention applied
        /// by <see cref="ILoggerExtensions.LogInformationJson"/> et al so investigators see a single
        /// consistent JSON shape across every structured-log path (including nested
        /// <see cref="DriftField"/> records).
        /// </summary>
        static readonly JsonSerializerOptions _structuredLogJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        static readonly SemaphoreSlim _getTenantApiClient = new(1);

        readonly EntityRequeue<TEntity> _requeue;

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
            _requeue = requeue ?? throw new ArgumentNullException(nameof(requeue));
        }

        /// <summary>
        /// Gets the type name of the entity used in messages.
        /// </summary>
        protected abstract string EntityTypeName { get; }

        /// <summary>
        /// Gets the requeue function for the entity controller.
        /// </summary>
        protected EntityRequeue<TEntity> Requeue => _requeue;

        /// <summary>
        /// Logs an Auth0 <em>read</em> API call (GET/list). Emits one structured-JSON entry at
        /// <see cref="LogLevel.Information"/> with <c>auth0ApiCallType:"read"</c> and no drift
        /// fields. The Read/Write split exists so the contract — every Write self-describes its
        /// drift context — is enforced by the type system instead of at runtime.
        /// </summary>
        protected void LogAuth0Read(
            string message,
            string entityType,
            string entityName,
            string entityNamespace,
            string purpose)
            => EmitAuth0ApiLog(message, Auth0ApiCallType.Read, entityType, entityName, entityNamespace, purpose, driftContext: null);

        /// <summary>
        /// Logs an Auth0 <em>write</em> API call (create/update/delete). Emits one structured-JSON
        /// entry at <see cref="LogLevel.Warning"/> carrying <c>reconciliationType</c>,
        /// <c>driftReason</c>, and <c>driftFields[]</c> derived from the required
        /// <paramref name="driftContext"/>. The non-nullable parameter makes "Write without context"
        /// a compile error — the runtime <c>ArgumentNullException</c> guard that previously enforced
        /// this is gone, so genuine <see cref="ArgumentException"/> bugs can still crash-loop visibly.
        /// </summary>
        protected void LogAuth0Write(
            string message,
            string entityType,
            string entityName,
            string entityNamespace,
            string purpose,
            DriftLogContext driftContext)
            => EmitAuth0ApiLog(message, Auth0ApiCallType.Write, entityType, entityName, entityNamespace, purpose, driftContext);

        private void EmitAuth0ApiLog(
            string message,
            Auth0ApiCallType apiCallType,
            string entityType,
            string entityName,
            string entityNamespace,
            string purpose,
            DriftLogContext? driftContext)
        {
            var logEntry = new Dictionary<string, object?>
            {
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["message"] = message,
                ["auth0ApiCallType"] = apiCallType.ToString().ToLowerInvariant(),
                ["entityTypeName"] = entityType,
                ["entityName"] = entityName,
                ["entityNamespace"] = entityNamespace,
                ["auth0ApiCallPurpose"] = purpose,
            };

            if (driftContext is not null)
            {
                // ReconciliationType + DriftChangeType both carry [JsonConverter(CamelCaseJsonStringEnumConverter)]
                // so the serializer emits camelCase ("first"/"drift"/"finalizer", "added"/"modified"/"removed").
                // Boxing the enum as object? lets the converter run instead of stringifying via .ToString().
                logEntry["reconciliationType"] = driftContext.ReconciliationType;
                logEntry["driftReason"] = driftContext.DriftReason;
                logEntry["driftFields"] = driftContext.DriftFields;
            }

            var jsonLog = System.Text.Json.JsonSerializer.Serialize(logEntry, _structuredLogJsonOptions);
            var level = apiCallType == Auth0ApiCallType.Write
                ? LogLevel.Warning
                : LogLevel.Information;
            Logger.Log(level, jsonLog);
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
                LogAuth0Read($"Getting Auth0 resource server by reference ID: {id}", "A0ResourceServer", id, defaultNamespace, "resolve_resource_server_reference");
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
        /// <returns>A tuple containing whether requeue is needed and the updated entity</returns>
        protected abstract Task<(bool needsRequeue, TEntity updatedEntity)> Reconcile(TEntity entity, CancellationToken cancellationToken);

        /// <inheritdoc />
        public virtual async Task ReconcileAsync(TEntity entity, CancellationToken cancellationToken)
        {
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

                var (needRequeue, updatedEntity) = await Reconcile(entity, cancellationToken);
                entity = updatedEntity; // Use the updated entity for subsequent operations
                
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Reconcile was cancelled (e.g. operator shutdown, controller restart, or an
                // outer caller cancelled the token). Do NOT swallow into Requeue — the host
                // owns the cancellation contract.
                throw;
            }
            catch (ErrorApiException e)
            {
                try
                {
                    var duration = DateTimeOffset.UtcNow - startTime;
                    var auth0StatusCode = (int)e.StatusCode;
                    var auth0RetryableHint = IsAuth0RetryableStatus(auth0StatusCode);
                    Logger.LogErrorJson($"Auth0 API error during reconciliation for {EntityTypeName} {entity.Namespace()}/{entity.Name()}: Status={e.StatusCode}, ErrorCode={e.ApiError?.ErrorCode}, Message={e.ApiError?.Message}, Duration={duration.TotalMilliseconds}ms", new {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        statusCode = e.StatusCode.ToString(),
                        // F4 (LA-RF-81): structured fields for Datadog facets — numeric status
                        // and a retryable hint (429/5xx) so investigators can filter without
                        // string-matching the status enum.
                        auth0StatusCode = auth0StatusCode,
                        auth0RetryableHint = auth0RetryableHint,
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

                // Always-requeue: the prior implementation logged-and-fell-through here,
                // which left the reconcile loop silent for the resource until a watch
                // event or operator restart fired. Schedule an explicit retry.
                var apiRetryDelay = GenerateExceptionRetryDelay();
                Logger.LogWarningJson($"Auth0 API error — rescheduling reconciliation after {apiRetryDelay}", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    rescheduleAfter = apiRetryDelay.ToString(),
                    operation = "requeue_after_api_error"
                });
                Requeue(entity, apiRetryDelay);
            }
            catch (RateLimitApiException e)
            {
                // calculate next attempt time, floored to one minute
                var baseDelay = e.RateLimit?.Reset is DateTimeOffset r ? r - DateTimeOffset.Now : TimeSpan.FromMinutes(1);
                if (baseDelay < TimeSpan.FromMinutes(1))
                    baseDelay = TimeSpan.FromMinutes(1);

                // F4 (LA-RF-81): floored value (seconds, no jitter) captured here so the
                // structured warn log surfaces what Auth0's authoritative reset hint dictated
                // before we layered jitter on top. Jitter is computed once below and used for
                // both the warn log and the actual Requeue call, to avoid multi-pod
                // thundering-herd on the same tenant.
                var requeueFloorSeconds = (int)Math.Floor(baseDelay.TotalSeconds);
                var rateLimitResetUtc = e.RateLimit?.Reset?.ToUniversalTime().ToString("o");

                // F4 (LA-RF-81): apply one-sided [1.0, 1.2) jitter after the floor; clamp
                // back to the floor if (defensively) it would fall below. Floor stays
                // authoritative as the minimum. Computed once so the structured log and
                // the Requeue call carry the same value.
                var jitteredDelay = ApplyRateLimitJitter(baseDelay, RateLimitRequeueFloor);
                var requeueAfterSeconds = (int)jitteredDelay.TotalSeconds;

                try
                {
                    var duration = DateTimeOffset.UtcNow - startTime;
                    Logger.LogWarningJson($"Auth0 rate limit exceeded for {EntityTypeName} {entity.Namespace()}/{entity.Name()}: Limit={e.RateLimit?.Limit}, Remaining={e.RateLimit?.Remaining}, Reset={e.RateLimit?.Reset}, Duration={duration.TotalMilliseconds}ms, RequeueAfter={jitteredDelay}", new {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        rateLimitLimit = e.RateLimit?.Limit,
                        rateLimitRemaining = e.RateLimit?.Remaining,
                        rateLimitReset = e.RateLimit?.Reset,
                        // F4 (LA-RF-81): facet-friendly fields.
                        //   `auth0RateLimitResetUtc`   — SDK-parsed reset hint as ISO-8601 UTC.
                        //   `auth0RequeueFloorSeconds` — floored pre-jitter delay (the floor
                        //                                Auth0's reset hint dictates).
                        //   `auth0RequeueAfterSeconds` — actual post-jitter delay used by
                        //                                Requeue(...). What the operator
                        //                                will *actually* wait.
                        auth0RateLimitResetUtc = rateLimitResetUtc,
                        auth0RequeueFloorSeconds = requeueFloorSeconds,
                        auth0RequeueAfterSeconds = requeueAfterSeconds,
                        rescheduleAfter = jitteredDelay.ToString(),
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

                Requeue(entity, jitteredDelay);
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
            catch (Exception e) when (!IsProgrammerBug(e) && !cancellationToken.IsCancellationRequested)
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

                // Always-requeue: prior implementation re-threw which produced a noisy
                // crash-loop-style failure with no scheduled retry for this resource.
                // Call Requeue with a jittered backoff so the operator self-heals after
                // transient K8s / dependency errors. Programmer-bug exception types are
                // filtered out above and continue to propagate so they crash-loop and
                // page on-call as expected.
                var retryDelay = GenerateExceptionRetryDelay();
                Logger.LogWarningJson($"Unexpected error — rescheduling reconciliation after {retryDelay}", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    rescheduleAfter = retryDelay.ToString(),
                    operation = "requeue_after_unexpected_exception"
                });
                Requeue(entity, retryDelay);
            }
        }

        /// <summary>
        /// Returns true for exception types that almost certainly indicate a programmer bug
        /// (null deref, bad cast, missing type) rather than a transient runtime / network / API
        /// condition. These propagate uncaught so the host's crash-loop / paging machinery
        /// handles them — a silent requeue would mask them and grow an unbounded retry queue.
        /// <para>
        /// <see cref="ArgumentException"/> (and its <see cref="ArgumentNullException"/> /
        /// <see cref="ArgumentOutOfRangeException"/> subclasses) is included: a controller passing
        /// a null/invalid argument to an internal helper is a wiring bug that requeue will never
        /// resolve. The previous build briefly dropped this entry to cushion a runtime
        /// <c>ArgumentNullException</c> from <c>LogAuth0ApiCall</c>'s mandatory-context guard;
        /// that guard has since been removed by splitting the funnel into
        /// <see cref="LogAuth0Read"/> / <see cref="LogAuth0Write"/>, so the original crash-loud
        /// invariant is restored.
        /// </para>
        /// </summary>
        private static bool IsProgrammerBug(Exception e) =>
            e is NullReferenceException
                or InvalidCastException
                or TypeLoadException
                or ArgumentException;

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
        /// Generates a random delay between MinRetryDelaySeconds and MaxRetryDelaySeconds for secret retry reconciliation.
        /// </summary>
        /// <returns>Random delay in seconds</returns>
        private int GenerateRandomSecretRetryDelay()
        {
            return GenerateRandomRetryDelay();
        }

        /// <summary>
        /// Generates a random delay between MinRetryDelaySeconds and MaxRetryDelaySeconds.
        /// Shared method to avoid code duplication for different retry scenarios.
        /// </summary>
        /// <returns>Random delay in seconds</returns>
        protected int GenerateRandomRetryDelay()
        {
            var random = new Random();
            return random.Next(MinRetryDelaySeconds, MaxRetryDelaySeconds + 1);
        }

        /// <summary>
        /// Generates the requeue delay used by the always-requeue-on-exception path in
        /// <see cref="ReconcileAsync"/>. Plain "<see cref="ExceptionRetryBaseSeconds"/>s
        /// ±25% jitter" — i.e. 22.5–37.5s with the current 30s base. No upper cap is
        /// applied: the jitter window is bounded by construction.
        /// </summary>
        protected TimeSpan GenerateExceptionRetryDelay()
        {
            // ±25% jitter: NextDouble() ∈ [0,1) → factor ∈ [0.75, 1.25)
            var jitterFactor = 0.75 + (Random.Shared.NextDouble() * 0.5);
            return TimeSpan.FromSeconds(ExceptionRetryBaseSeconds * jitterFactor);
        }

        /// <summary>
        /// Floor for the rate-limit requeue delay. The SDK's `X-RateLimit-Reset` hint is
        /// authoritative when present, but we never requeue sooner than this — defensive
        /// against missing/zero reset values and against the multi-pod thundering-herd risk
        /// where every pod racing the exact reset instant would re-hammer the API.
        /// </summary>
        internal static readonly TimeSpan RateLimitRequeueFloor = TimeSpan.FromMinutes(1);

        /// <summary>
        /// F4 (LA-RF-81): testability seam for jitter sampling. Tests override this on a
        /// <c>TestController</c> subclass to drive jitter deterministically (see
        /// <c>V1ControllerRateLimitObservabilityTests.TestController</c>). Production
        /// uses <see cref="Random.Shared"/>. Returns a value in <c>[0.0, 1.0)</c>.
        /// </summary>
        protected virtual double SampleRateLimitJitter() => Random.Shared.NextDouble();

        /// <summary>
        /// Applies one-sided <c>[1.0, 1.2)</c> jitter to <paramref name="baseDelay"/> and clamps
        /// the result back up to <paramref name="floor"/> if (for any reason) it would fall
        /// below. Jitter only spreads upward so we never requeue sooner than the SDK's reset
        /// hint. Floor stays authoritative as the absolute minimum requeue delay.
        /// </summary>
        protected TimeSpan ApplyRateLimitJitter(TimeSpan baseDelay, TimeSpan floor)
        {
            // factor ∈ [1.0, 1.2): SampleRateLimitJitter() ∈ [0,1) → 1.0 + (∈[0,1) * 0.2)
            var jitterFactor = 1.0 + (SampleRateLimitJitter() * 0.2);
            var jittered = TimeSpan.FromTicks((long)(baseDelay.Ticks * jitterFactor));
            return jittered < floor ? floor : jittered;
        }

        /// <summary>
        /// Auth0 status codes that are considered "retryable hint" for log facets — 429
        /// (rate-limit) and any 5xx (transient upstream). The hint is purely advisory for
        /// observability; the operator does not change retry behavior based on it.
        /// </summary>
        internal static bool IsAuth0RetryableStatus(int statusCode)
            => statusCode == 429 || (statusCode >= 500 && statusCode <= 599);
    }

}
