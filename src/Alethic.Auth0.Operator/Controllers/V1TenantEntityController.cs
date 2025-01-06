using System;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Entities;

using Auth0.Core.Exceptions;
using Auth0.ManagementApi;

using k8s;
using k8s.Models;

using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Alethic.Auth0.Operator.Controllers
{

    public abstract class V1TenantEntityController<TEntity, TSpec, TStatus, TConf> : V1Controller<TEntity, TSpec, TStatus, TConf>
        where TEntity : IKubernetesObject<V1ObjectMeta>, V1TenantEntity<TSpec, TStatus, TConf>
        where TSpec : V1TenantEntitySpec<TConf>
        where TStatus : V1TenantEntityStatus<TConf>
        where TConf : class
    {

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="kube"></param>
        /// <param name="logger"></param>
        public V1TenantEntityController(IMemoryCache cache, IKubernetesClient kube, ILogger<V1ClientController> logger) :
            base(cache, kube, logger)
        {

        }

        /// <summary>
        /// Attempts to perform a get operation through the API.
        /// </summary>
        /// <param name="api"></param>
        /// <param name="id"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected abstract Task<TConf?> GetApi(IManagementApiClient api, string id, CancellationToken cancellationToken);

        /// <summary>
        /// Attempts to locate a matching API element by the given configuration.
        /// </summary>
        /// <param name="api"></param>
        /// <param name="conf"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected abstract Task<string?> FindApi(IManagementApiClient api, TConf conf, CancellationToken cancellationToken);

        /// <summary>
        /// Performs a validation on the <paramref name="conf"/> parameter for usage in create operations.
        /// </summary>
        /// <param name="conf"></param>
        /// <returns></returns>
        protected abstract string? ValidateCreateConf(TConf conf);

        /// <summary>
        /// Attempts to perform a creation through the API. If successful returns the new ID value.
        /// </summary>
        /// <param name="api"></param>
        /// <param name="conf"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected abstract Task<string> CreateApi(IManagementApiClient api, TConf conf, CancellationToken cancellationToken);

        /// <summary>
        /// Attempts to perform an update through the API.
        /// </summary>
        /// <param name="api"></param>
        /// <param name="id"></param>
        /// <param name="conf"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected abstract Task UpdateApi(IManagementApiClient api, string id, TConf conf, CancellationToken cancellationToken);

        /// <inheritdoc />
        protected override async Task Reconcile(TEntity entity, CancellationToken cancellationToken)
        {
            if (entity.Spec.TenantRef == null)
                throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}:{entity.Name()}: missing a tenant reference.");

            if (entity.Spec.Conf == null)
                throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}:{entity.Name()}: missing configuration.");

            var api = await GetTenantApiClientAsync(entity.Spec.TenantRef, entity.Namespace(), cancellationToken);
            if (api == null)
                throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}:{entity.Name()}: failed to retrieve API client.");

            // discover entity by name, or create
            if (string.IsNullOrWhiteSpace(entity.Status.Id))
            {
                var self = await FindApi(api, entity.Spec.Conf, cancellationToken);
                if (self == null)
                {
                    Logger.LogInformation("{EntityTypeName} {Namespace}/{Name}: could not be located, creating.", EntityTypeName, entity.Namespace(), entity.Name());

                    // check for validation before create
                    if (ValidateCreateConf(entity.Spec.Conf) is string msg)
                        throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}:{entity.Name()} is invalid: {msg}");

                    entity.Status.Id = await CreateApi(api, entity.Spec.Conf, cancellationToken);
                    Logger.LogInformation("{EntityTypeName} {Namespace}/{Name}: created with {Id}", EntityTypeName, entity.Namespace(), entity.Name(), entity.Status.Id);
                    await Kube.UpdateStatusAsync(entity, cancellationToken);
                }
                else
                {
                    entity.Status.Id = self;
                    Logger.LogInformation("{EntityTypeName} {Namespace}/{Name}: loaded with {Id}", EntityTypeName, entity.Namespace(), entity.Name(), entity.Status.Id);
                    await Kube.UpdateStatusAsync(entity, cancellationToken);
                }
            }

            if (string.IsNullOrWhiteSpace(entity.Status.Id))
                throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}/{entity.Name()}: missing an existing ID.");

            // update specified configuration
            if (entity.Spec.Conf is { } conf)
                await UpdateApi(api, entity.Status.Id, conf, cancellationToken);

            // retrieve and copy last known configuration
            entity.Status.LastConf = await GetApi(api, entity.Status.Id, cancellationToken: cancellationToken);
            await Kube.UpdateStatusAsync(entity, cancellationToken);
        }

        /// <summary>
        /// Implement this method to delete a specific entity from the API.
        /// </summary>
        /// <param name="api"></param>
        /// <param name="id"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected abstract Task DeleteApi(IManagementApiClient api, string id, CancellationToken cancellationToken);

        /// <inheritdoc />
        public override sealed async Task DeletedAsync(TEntity entity, CancellationToken cancellationToken)
        {
            try
            {
                if (entity.Spec.TenantRef == null)
                    throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}/{entity.Name()}: is missing a tenant reference.");

                var api = await GetTenantApiClientAsync(entity.Spec.TenantRef, entity.Namespace(), cancellationToken);
                if (api == null)
                    throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}/{entity.Name()}: failed to retrieve API client.");

                if (string.IsNullOrWhiteSpace(entity.Status.Id))
                {
                    Logger.LogWarning("{EntityTypeName} {EntityNamespace}/{EntityName}: no known ID, skipping delete.", EntityTypeName, entity.Namespace(), entity.Name());
                    return;
                }

                var self = await GetApi(api, entity.Status.Id, cancellationToken);
                if (self is null)
                {
                    Logger.LogWarning("{EntityTypeName} {EntityNamespace}/{EntityName}: already been deleted, skipping delete.", EntityTypeName, entity.Namespace(), entity.Name());
                    return;
                }

                await DeleteApi(api, entity.Status.Id, cancellationToken);
            }
            catch (ErrorApiException e)
            {
                try
                {
                    Logger.LogError(e, "API error deleting {EntityTypeName} {EntityNamespace}/{EntityName}: {Message}", EntityTypeName, entity.Namespace(), entity.Name(), e.ApiError.Message);
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
                    Logger.LogError(e, "Rate limit hit deleting {EntityTypeName} {EntityNamespace}/{EntityName}", EntityTypeName, entity.Namespace(), entity.Name());
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
                    Logger.LogError(e, "Unexpected exception deleting {EntityTypeName} {EntityNamespace}/{EntityName}.", EntityTypeName, entity.Namespace(), entity.Name());
                    await DeletingWarningAsync(entity, e.Message, cancellationToken);
                }
                catch (Exception e2)
                {
                    Logger.LogCritical(e2, "Unexpected exception creating event.");
                }

                throw;
            }
        }

    }

}
