using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Entities;
using Alethic.Auth0.Operator.Models.Connection;

using Auth0.ManagementApi;
using Auth0.ManagementApi.Models;
using Auth0.ManagementApi.Paging;

using k8s.Models;

using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Rbac;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Alethic.Auth0.Operator.Controllers
{

    [EntityRbac(typeof(V1Tenant), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(V1Connection), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1Secret), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(Eventsv1Event), Verbs = RbacVerb.All)]
    public class V1ConnectionController :
        V1TenantEntityController<V1Connection, V1Connection.SpecDef, V1Connection.StatusDef, ConnectionConf>,
        IEntityController<V1Connection>
    {

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="kube"></param>
        /// <param name="logger"></param>
        public V1ConnectionController(IMemoryCache cache, IKubernetesClient kube, ILogger<V1ClientController> logger) :
            base(cache, kube, logger)
        {

        }

        /// <inheritdoc />
        protected override string EntityTypeName => "Connection";

        /// <inheritdoc />
        protected override async Task<IDictionary?> GetApi(IManagementApiClient api, string id, CancellationToken cancellationToken)
        {
            var self = await api.Connections.GetAsync(id, cancellationToken: cancellationToken);
            if (self == null)
                return null;

            return TransformToSystemTextJson<Connection, IDictionary>(self);
        }

        /// <inheritdoc />
        protected override async Task<string?> FindApi(IManagementApiClient api, ConnectionConf conf, CancellationToken cancellationToken)
        {
            var list = await api.Connections.GetAllAsync(new GetConnectionsRequest() { Fields = "id,name" }, pagination: (PaginationInfo?)null, cancellationToken: cancellationToken);
            var self = list.FirstOrDefault(i => i.Name == conf.Name);
            return self?.Id;
        }

        /// <inheritdoc />
        protected override string? ValidateCreateConf(ConnectionConf conf)
        {
            return null;
        }

        /// <summary>
        /// Attempts to resolve the list of client references to client IDs.
        /// </summary>
        /// <param name="refs"></param>
        /// <param name="defaultNamespace"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        async Task<string[]?> ResolveClientRefsToId(V1ClientRef[]? refs, string defaultNamespace, CancellationToken cancellationToken)
        {
            if (refs is null)
                return Array.Empty<string>();

            var l = new List<string>(refs.Length);

            foreach (var i in refs)
            {
                if (i.Id is { } id)
                {
                    l.Add(id);
                }
                else
                {
                    Logger.LogDebug($"Attempting to resolve client reference {i.Namespace}/{i.Name}.");

                    var client = await ResolveClientRef(i, defaultNamespace, cancellationToken);
                    if (client is null)
                        throw new InvalidOperationException($"Could not resolve ClientRef {i}.");

                    if (client.Status.Id is null)
                        throw new InvalidOperationException($"Referenced Client {client.Namespace()}/{client.Name()} has not been reconcilled.");

                    Logger.LogDebug($"Resolved client reference {i.Namespace}/{i.Name} to {client.Status.Id}.");
                    l.Add(client.Status.Id);
                }
            }

            return l.ToArray();
        }

        /// <inheritdoc />
        protected override async Task<string> CreateApi(IManagementApiClient api, ConnectionConf conf, string defaultNamespace, CancellationToken cancellationToken)
        {
            var req = new ConnectionCreateRequest();
            req.Name = conf.Name;
            req.Strategy = conf.Strategy;
            req.DisplayName = conf.DisplayName;
            req.Options = conf.Options;
            req.Metadata = conf.Metadata;
            req.Realms = conf.Realms;
            req.IsDomainConnection = conf.IsDomainConnection ?? false;
            req.ShowAsButton = conf.ShowAsButton;
            req.EnabledClients = await ResolveClientRefsToId(conf.EnabledClients, defaultNamespace, cancellationToken);

            var self = await api.Connections.CreateAsync(TransformToNewtonsoftJson<ConnectionConf, ConnectionCreateRequest>(conf), cancellationToken);
            if (self is null)
                throw new InvalidOperationException();

            return self.Id;
        }

        /// <inheritdoc />
        protected override async Task UpdateApi(IManagementApiClient api, string id, ConnectionConf conf, string defaultNamespace, CancellationToken cancellationToken)
        {
            var req = new ConnectionUpdateRequest();
            req.DisplayName = conf.DisplayName;
            req.Options = conf.Options;
            req.Metadata = conf.Metadata;
            req.Realms = conf.Realms;
            req.IsDomainConnection = conf.IsDomainConnection ?? false;
            req.ShowAsButton = conf.ShowAsButton;
            req.EnabledClients = await ResolveClientRefsToId(conf.EnabledClients, defaultNamespace, cancellationToken);

            await api.Connections.UpdateAsync(id, req, cancellationToken);
        }

        /// <inheritdoc />
        protected override Task DeleteApi(IManagementApiClient api, string id, CancellationToken cancellationToken)
        {
            return api.Connections.DeleteAsync(id, cancellationToken);
        }

    }

}
