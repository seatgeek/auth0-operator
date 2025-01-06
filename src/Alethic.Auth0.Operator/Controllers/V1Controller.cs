using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Entities;

using Auth0.AuthenticationApi;
using Auth0.AuthenticationApi.Models;
using Auth0.Core.Exceptions;
using Auth0.ManagementApi;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Entities;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

namespace Alethic.Auth0.Operator.Controllers
{

    public abstract class V1Controller<TEntity, TSpec, TStatus, TConf> : IEntityController<TEntity>
        where TEntity : IKubernetesObject<V1ObjectMeta>, V1Entity<TSpec, TStatus, TConf>
        where TSpec : V1EntitySpec<TConf>
        where TStatus : V1EntityStatus<TConf>
        where TConf : class
    {

        static readonly Newtonsoft.Json.JsonSerializer _newtonsoftJsonSerializer = Newtonsoft.Json.JsonSerializer.CreateDefault();

        readonly IMemoryCache _cache;
        readonly IKubernetesClient _kube;
        readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="kube"></param>
        /// <param name="util"></param>
        /// <param name="logger"></param>
        public V1Controller(IMemoryCache cache, IKubernetesClient kube, ILogger<V1ClientController> logger)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _kube = kube ?? throw new ArgumentNullException(nameof(kube));
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
        /// Gets the logger.
        /// </summary>
        protected ILogger Logger => _logger;

        /// <summary>
        /// Gets an active <see cref="ManagementApiClient"/> for the specified tenant reference.
        /// </summary>
        /// <param name="tenantRef"></param>
        /// <param name="defaultNamespace"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<IManagementApiClient> GetTenantApiClientAsync(V1TenantRef tenantRef, string defaultNamespace, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(tenantRef.Name))
                throw new InvalidOperationException($"Tenant reference has no name.");

            var ns = tenantRef.Namespace ?? defaultNamespace;
            if (string.IsNullOrWhiteSpace(ns))
                throw new InvalidOperationException($"Tenant reference has no discovered namesace.");

            var tenant = await _kube.GetAsync<V1Tenant>(tenantRef.Name, ns);
            if (tenant == null)
                throw new InvalidOperationException($"Tenant reference cannot be resolved.");

            return await GetTenantApiClientAsync(tenant, cancellationToken);
        }

        /// <summary>
        /// Gets an active <see cref="ManagementApiClient"/> for the specified tenant.
        /// </summary>
        /// <param name="tenant"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<IManagementApiClient> GetTenantApiClientAsync(V1Tenant tenant, CancellationToken cancellationToken)
        {
            var api = await _cache.GetOrCreateAsync((tenant.Namespace(), tenant.Name()), async entry =>
            {
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
                    throw new InvalidOperationException($"Tenant {tenant.Namespace()}/{tenant.Name()} has missing secret.");

                if (secret.Data.TryGetValue("clientId", out var clientIdBuf) == false)
                    throw new InvalidOperationException($"Tenant {tenant.Namespace()}/{tenant.Name()} has missing clientId value on secret.");

                if (secret.Data.TryGetValue("clientSecret", out var clientSecretBuf) == false)
                    throw new InvalidOperationException($"Tenant {tenant.Namespace()}/{tenant.Name()} has missing clientSecret value on secret.");

                // decode secret values
                var clientId = Encoding.UTF8.GetString(clientIdBuf);
                var clientSecret = Encoding.UTF8.GetString(clientSecretBuf);

                // retrieve authentication token
                var auth = new AuthenticationApiClient(new Uri($"https://{domain}"));
                var authToken = await auth.GetTokenAsync(new ClientCredentialsTokenRequest() { Audience = $"https://{domain}/api/v2/", ClientId = clientId, ClientSecret = clientSecret }, cancellationToken);
                if (authToken.AccessToken == null || authToken.AccessToken.Length == 0)
                    throw new InvalidOperationException($"Tenant {tenant.Namespace()}/{tenant.Name()} failed to retrieve management API token.");

                // contact API using token and domain
                var api = new ManagementApiClient(authToken.AccessToken, new Uri($"https://{domain}/api/v2/"));

                // cache API client for 1 minute
                entry.SetAbsoluteExpiration(TimeSpan.FromMinutes(1));
                return (IManagementApiClient)api;
            });

            if (api is null)
                throw new InvalidOperationException("Cannot retrieve tenant API client.");

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
        /// <param name="message"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected async Task ReconcileWarningAsync(TEntity entity, string message, CancellationToken cancellationToken)
        {
            await _kube.CreateAsync(new Eventsv1Event(
                    DateTime.Now,
                    metadata: new V1ObjectMeta(namespaceProperty: entity.Namespace(), generateName: "auth0"),
                    reportingController: "kubernetes.auth0.com/operator",
                    reportingInstance: Dns.GetHostName(),
                    regarding: entity.MakeObjectReference(),
                    action: "Reconcile",
                    type: "Warning",
                    reason: message),
                cancellationToken);
        }

        /// <summary>
        /// Updates the Deleting event to a warning.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="message"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected async Task DeletingWarningAsync(TEntity entity, string message, CancellationToken cancellationToken)
        {
            await _kube.CreateAsync(new Eventsv1Event(
                    DateTime.Now,
                    metadata: new V1ObjectMeta(namespaceProperty: entity.Namespace(), generateName: "auth0"),
                    reportingController: "kubernetes.auth0.com/operator",
                    reportingInstance: Dns.GetHostName(),
                    regarding: entity.MakeObjectReference(),
                    action: "Deleting",
                    type: "Warning",
                    reason: message),
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
        protected static TTo? TransformToNewtonsoftJson<TFrom, TTo>(TFrom? from)
            where TFrom : class
            where TTo : class
        {
            if (from == null)
                return null;

            return _newtonsoftJsonSerializer.Deserialize<TTo>(new JsonTextReader(new StringReader(System.Text.Json.JsonSerializer.Serialize(from))));
        }

        /// <summary>
        /// Transforms the given Newtonsoft JSON serializable object to a System.Text.Json serializable object.
        /// </summary>
        /// <typeparam name="TFrom"></typeparam>
        /// <typeparam name="TTo"></typeparam>
        /// <param name="from"></param>
        /// <returns></returns>
        protected static TTo? TransformToSystemTextJson<TFrom, TTo>(TFrom? from)
            where TFrom : class
            where TTo : class
        {
            if (from == null)
                return null;

            using var w = new StringWriter();
            _newtonsoftJsonSerializer.Serialize(w, from);
            return System.Text.Json.JsonSerializer.Deserialize<TTo>(w.ToString());
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
            try
            {
                Logger.LogInformation("Reconciling {EntityTypeName} {Namespace}/{Name}.", EntityTypeName, entity.Namespace(), entity.Name());

                if (entity.Spec.Conf == null)
                    throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} is missing configuration.");

                // does the actual work of reconciling
                await Reconcile(entity, cancellationToken);

                Logger.LogInformation("Reconciled {EntityTypeName} {Namespace}/{Name}.", EntityTypeName, entity.Namespace(), entity.Name());
                await ReconcileSuccessAsync(entity, cancellationToken);
            }
            catch (ErrorApiException e)
            {
                try
                {
                    Logger.LogError(e, "API error reconciling {EntityTypeName} {EntityNamespace}/{EntityName}: {Message}", EntityTypeName, entity.Namespace(), entity.Name(), e.ApiError.Message);
                    await DeletingWarningAsync(entity, e.ApiError.Message, cancellationToken);
                }
                catch (Exception e2)
                {
                    Logger.LogCritical(e2, "Unexpected exception creating event.");
                }
            }
            catch (RateLimitApiException e)
            {
                try
                {
                    Logger.LogError(e, "Rate limit hit reconciling {EntityTypeName} {EntityNamespace}/{EntityName}", EntityTypeName, entity.Namespace(), entity.Name());
                    await DeletingWarningAsync(entity, e.Message, cancellationToken);
                }
                catch (Exception e2)
                {
                    Logger.LogCritical(e2, "Unexpected exception creating event.");
                }

                throw;
            }
            catch (Exception e)
            {
                try
                {
                    Logger.LogError(e, "Unexpected exception reconciling {EntityTypeName} {EntityNamespace}/{EntityName}.", EntityTypeName, entity.Namespace(), entity.Name());
                    await DeletingWarningAsync(entity, e.Message, cancellationToken);
                }
                catch (Exception e2)
                {
                    Logger.LogCritical(e2, "Unexpected exception creating event.");
                }

                throw;
            }
        }

        /// <inheritdoc />
        public abstract Task DeletedAsync(TEntity entity, CancellationToken cancellationToken);

    }

}
