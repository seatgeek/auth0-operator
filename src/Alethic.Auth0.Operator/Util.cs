using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Entities;

using Auth0.AuthenticationApi;
using Auth0.AuthenticationApi.Models;
using Auth0.ManagementApi;

using k8s.Models;

using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

namespace Alethic.Auth0.Operator
{

    public class Util
    {

        readonly IMemoryCache _cache;
        readonly IKubernetesClient _kube;
        readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="kube"></param>
        /// <param name="logger"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public Util(IMemoryCache cache, IKubernetesClient kube, ILogger<Util> logger)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _kube = kube ?? throw new ArgumentNullException(nameof(kube));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

    }

}
