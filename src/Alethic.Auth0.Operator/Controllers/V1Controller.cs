﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Core.Extensions;
using Alethic.Auth0.Operator.Core.Models;
using Alethic.Auth0.Operator.Models;

using Auth0.AuthenticationApi;
using Auth0.AuthenticationApi.Models;
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

    public abstract class V1Controller<TEntity, TSpec, TStatus, TConf> : IEntityController<TEntity>
        where TEntity : IKubernetesObject<V1ObjectMeta>, V1Entity<TSpec, TStatus, TConf>
        where TSpec : V1EntitySpec<TConf>
        where TStatus : V1EntityStatus
        where TConf : class
    {

        static readonly Newtonsoft.Json.JsonSerializer _newtonsoftJsonSerializer = Newtonsoft.Json.JsonSerializer.CreateDefault();
        static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new SimplePrimitiveHashtableConverter() } };

        readonly IKubernetesClient _kube;
        readonly EntityRequeue<TEntity> _requeue;
        readonly IMemoryCache _cache;
        readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="kube"></param>
        /// <param name="requeue"></param>
        /// <param name="cache"></param>
        /// <param name="logger"></param>
        public V1Controller(IKubernetesClient kube, EntityRequeue<TEntity> requeue, IMemoryCache cache, ILogger logger)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
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

            var ns = secretRef.NamespaceProperty ?? defaultNamespace;
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

            var ns = tenantRef.Namespace ?? defaultNamespace;
            if (string.IsNullOrWhiteSpace(ns))
                throw new InvalidOperationException($"Tenant reference {tenantRef} has no discovered namesace.");

            var tenant = await _kube.GetAsync<V1Tenant>(tenantRef.Name, ns, cancellationToken);
            if (tenant is null)
                throw new RetryException($"Tenant reference {tenantRef} cannot be resolved.");

            return tenant;
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

            var ns = clientRef.Namespace ?? defaultNamespace;
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

            Logger.LogDebug("Attempting to resolve ClientRef {Namespace}/{Name}.", clientRef.Namespace, clientRef.Name);

            var client = await ResolveClientRef(api, clientRef, defaultNamespace, cancellationToken);
            if (client is null)
                throw new InvalidOperationException($"Could not resolve ClientRef {clientRef}.");
            if (string.IsNullOrWhiteSpace(client.Status.Id))
                throw new RetryException($"Referenced Client {client.Namespace()}/{client.Name()} has not been reconciled.");

            Logger.LogDebug("Resolved ClientRef {Namespace}/{Name} to {Id}.", clientRef.Namespace, clientRef.Name, client.Status.Id);
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

            var ns = resourceServerRef.Namespace ?? defaultNamespace;
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
                var self = await api.ResourceServers.GetAsync(id, cancellationToken);
                if (self is null)
                    throw new InvalidOperationException($"Failed to resolve ResourceServer reference {id}.");

                return self.Identifier;
            }

            Logger.LogInformation("Attempting to resolve ResourceServer reference {Namespace}/{Name}.", reference.Namespace, reference.Name);

            var resourceServer = await ResolveResourceServerRef(api, reference, defaultNamespace, cancellationToken);
            if (resourceServer is null)
                throw new InvalidOperationException($"Could not resolve ResourceServerRef {reference}.");

            if (resourceServer.Status.Identifier is null)
                throw new RetryException($"Referenced ResourceServer {resourceServer.Namespace()}/{resourceServer.Name()} has not been reconcilled.");

            Logger.LogInformation("Resolved ResourceServer reference {Namespace}/{Name} to {Identifier}.", reference.Namespace, reference.Name, resourceServer.Status.Identifier);
            return resourceServer.Status.Identifier;
        }

        /// <summary>
        /// Gets an active <see cref="ManagementApiClient"/> for the specified tenant.
        /// </summary>
        /// <param name="tenant"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<IManagementApiClient> GetTenantApiClientAsync(V1Tenant tenant, CancellationToken cancellationToken)
        {
            Logger.LogInformation("Retrieving Auth0 API client for tenant {TenantNamespace}/{TenantName}", tenant.Namespace(), tenant.Name());
            
            var api = await _cache.GetOrCreateAsync((tenant.Namespace(), tenant.Name()), async entry =>
            {
                Logger.LogInformation("Creating new Auth0 API client for tenant {TenantNamespace}/{TenantName}", tenant.Namespace(), tenant.Name());
                var domain = tenant.Spec.Auth?.Domain;
                if (string.IsNullOrWhiteSpace(domain))
                    throw new InvalidOperationException($"Tenant {tenant.Namespace()}/{tenant.Name()} has no authentication domain.");

                var secretRef = tenant.Spec.Auth?.SecretRef;
                if (secretRef == null)
                    throw new InvalidOperationException($"Tenant {tenant.Namespace()}/{tenant.Name()} has no authentication secret.");

                if (string.IsNullOrWhiteSpace(secretRef.Name))
                    throw new InvalidOperationException($"Tenant {tenant.Namespace()}/{tenant.Name()} has no secret name.");

                var secret = _kube.Get<V1Secret>(secretRef.Name, secretRef.NamespaceProperty ?? tenant.Namespace());
                if (secret == null)
                    throw new RetryException($"Tenant {tenant.Namespace()}/{tenant.Name()} has missing secret.");

                if (secret.Data.TryGetValue("clientId", out var clientIdBuf) == false)
                    throw new RetryException($"Tenant {tenant.Namespace()}/{tenant.Name()} has missing clientId value on secret.");

                if (secret.Data.TryGetValue("clientSecret", out var clientSecretBuf) == false)
                    throw new RetryException($"Tenant {tenant.Namespace()}/{tenant.Name()} has missing clientSecret value on secret.");

                // decode secret values
                var clientId = Encoding.UTF8.GetString(clientIdBuf);
                var clientSecret = Encoding.UTF8.GetString(clientSecretBuf);

                Logger.LogInformation("Authenticating with Auth0 domain {Domain} for tenant {TenantNamespace}/{TenantName}", domain, tenant.Namespace(), tenant.Name());
                // retrieve authentication token
                var auth = new AuthenticationApiClient(new Uri($"https://{domain}"));
                var authToken = await auth.GetTokenAsync(new ClientCredentialsTokenRequest() { Audience = $"https://{domain}/api/v2/", ClientId = clientId, ClientSecret = clientSecret }, cancellationToken);
                if (authToken.AccessToken == null || authToken.AccessToken.Length == 0)
                {
                    Logger.LogError("Failed to retrieve management API token for tenant {TenantNamespace}/{TenantName} from domain {Domain}", tenant.Namespace(), tenant.Name(), domain);
                    throw new RetryException($"Tenant {tenant.Namespace()}/{tenant.Name()} failed to retrieve management API token.");
                }

                // contact API using token and domain
                var api = new ManagementApiClient(authToken.AccessToken, new Uri($"https://{domain}/api/v2/"));

                // cache API client for 1 minute
                entry.SetAbsoluteExpiration(TimeSpan.FromMinutes(1));
                Logger.LogInformation("Successfully created and cached Auth0 API client for tenant {TenantNamespace}/{TenantName}", tenant.Namespace(), tenant.Name());
                return (IManagementApiClient)api;
            });

            if (api is null)
            {
                Logger.LogError("API client is null after cache operation for tenant {TenantNamespace}/{TenantName}", tenant.Namespace(), tenant.Name());
                throw new InvalidOperationException("Cannot retrieve tenant API client.");
            }

            Logger.LogInformation("Successfully retrieved Auth0 API client for tenant {TenantNamespace}/{TenantName}", tenant.Namespace(), tenant.Name());
            return api;
        }

        /// <summary>
        /// Updates the Reconcile event to a warning.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected async Task ReconcileSuccessAsync(TEntity entity, CancellationToken cancellationToken)
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
        protected abstract Task Reconcile(TEntity entity, CancellationToken cancellationToken);

        /// <inheritdoc />
        public async Task ReconcileAsync(TEntity entity, CancellationToken cancellationToken)
        {
            var startTime = DateTimeOffset.UtcNow;
            Logger.LogInformation("{EntityTypeName} {Namespace}/{Name} starting reconciliation at {StartTime}", EntityTypeName, entity.Namespace(), entity.Name(), startTime);
            
            try
            {
                if (entity.Spec.Conf == null)
                {
                    Logger.LogError("{EntityTypeName} {Namespace}/{Name} is missing configuration", EntityTypeName, entity.Namespace(), entity.Name());
                    throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} is missing configuration.");
                }

                // does the actual work of reconciling
                await Reconcile(entity, cancellationToken);

                var duration = DateTimeOffset.UtcNow - startTime;
                Logger.LogInformation("{EntityTypeName} {Namespace}/{Name} reconciliation completed successfully in {Duration}ms", EntityTypeName, entity.Namespace(), entity.Name(), duration.TotalMilliseconds);
                await ReconcileSuccessAsync(entity, cancellationToken);
            }
            catch (ErrorApiException e)
            {
                try
                {
                    var duration = DateTimeOffset.UtcNow - startTime;
                    Logger.LogError(e, "Auth0 API error during reconciliation for {EntityTypeName} {EntityNamespace}/{EntityName}: Status={StatusCode}, ErrorCode={ErrorCode}, Message={Message}, Duration={Duration}ms", 
                        EntityTypeName, entity.Namespace(), entity.Name(), e.StatusCode, e.ApiError?.ErrorCode, e.ApiError?.Message, duration.TotalMilliseconds);
                    await ReconcileWarningAsync(entity, "ApiError", e.ApiError?.Message ?? e.Message, cancellationToken);
                }
                catch (Exception e2)
                {
                    Logger.LogCritical(e2, "Unexpected exception creating event for {EntityTypeName} {EntityNamespace}/{EntityName}", EntityTypeName, entity.Namespace(), entity.Name());
                }
            }
            catch (RateLimitApiException e)
            {
                try
                {
                    var duration = DateTimeOffset.UtcNow - startTime;
                    Logger.LogWarning(e, "Auth0 rate limit exceeded for {EntityTypeName} {EntityNamespace}/{EntityName}: Limit={Limit}, Remaining={Remaining}, Reset={Reset}, Duration={Duration}ms", 
                        EntityTypeName, entity.Namespace(), entity.Name(), 
                        e.RateLimit?.Limit, e.RateLimit?.Remaining, e.RateLimit?.Reset, duration.TotalMilliseconds);
                    await ReconcileWarningAsync(entity, "RateLimit", e.ApiError?.Message ?? e.Message, cancellationToken);
                }
                catch (Exception e2)
                {
                    Logger.LogCritical(e2, "Unexpected exception creating event for {EntityTypeName} {EntityNamespace}/{EntityName}", EntityTypeName, entity.Namespace(), entity.Name());
                }

                // calculate next attempt time, floored to one minute
                var n = e.RateLimit?.Reset is DateTimeOffset r ? r - DateTimeOffset.Now : TimeSpan.FromMinutes(1);
                if (n < TimeSpan.FromMinutes(1))
                    n = TimeSpan.FromMinutes(1);

                Logger.LogWarning("Rate limit exceeded, rescheduling reconciliation after {TimeSpan}", n);
                Requeue(entity, n);
            }
            catch (RetryException e)
            {
                try
                {
                    var duration = DateTimeOffset.UtcNow - startTime;
                    Logger.LogWarning(e, "Retry required for {EntityTypeName} {EntityNamespace}/{EntityName}: {Message}, Duration={Duration}ms", EntityTypeName, entity.Namespace(), entity.Name(), e.Message, duration.TotalMilliseconds);
                    await ReconcileWarningAsync(entity, "Retry", e.Message, cancellationToken);
                }
                catch (Exception e2)
                {
                    Logger.LogCritical(e2, "Unexpected exception creating event for {EntityTypeName} {EntityNamespace}/{EntityName}", EntityTypeName, entity.Namespace(), entity.Name());
                }

                Logger.LogWarning("Retry exception occurred, rescheduling reconciliation after {TimeSpan}", TimeSpan.FromMinutes(1));
                Requeue(entity, TimeSpan.FromMinutes(1));
            }
            catch (Exception e)
            {
                try
                {
                    var duration = DateTimeOffset.UtcNow - startTime;
                    Logger.LogError(e, "Unexpected error reconciling {EntityTypeName} {EntityNamespace}/{EntityName}: {ExceptionType} - {Message}, Duration={Duration}ms", 
                        EntityTypeName, entity.Namespace(), entity.Name(), e.GetType().Name, e.Message, duration.TotalMilliseconds);
                    await ReconcileWarningAsync(entity, "Unknown", e.Message, cancellationToken);
                }
                catch (Exception e2)
                {
                    Logger.LogCritical(e2, "Unexpected exception creating event for {EntityTypeName} {EntityNamespace}/{EntityName}", EntityTypeName, entity.Namespace(), entity.Name());
                }

                throw;
            }
        }

        /// <inheritdoc />
        public abstract Task DeletedAsync(TEntity entity, CancellationToken cancellationToken);

    }

}
