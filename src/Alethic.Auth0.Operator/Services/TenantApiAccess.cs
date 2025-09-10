using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alethic.Auth0.Operator.Extensions;
using Alethic.Auth0.Operator.Models;
using Auth0.AuthenticationApi;
using Auth0.AuthenticationApi.Models;
using KubeOps.KubernetesClient;
using Microsoft.Extensions.Logging;
using k8s.Models;

namespace Alethic.Auth0.Operator.Services
{
    /// <summary>
    /// Cached tenant credentials and token information
    /// </summary>
    internal class CachedTenantCredentials
    {
        public required string Domain { get; init; }
        public required Uri BaseUri { get; init; }
        public required string ClientId { get; init; }
        public required string ClientSecret { get; init; }
        public required string TenantUid { get; init; }
        public string? AccessToken { get; set; }
        public DateTime? TokenExpiration { get; set; }
    }

    /// <summary>
    /// Implementation of ITenantApiAccess for a single specific tenant
    /// </summary>
    public class TenantApiAccess : ITenantApiAccess
    {
        
        private readonly CachedTenantCredentials _credentials;
        private readonly ILogger _logger;
        private readonly string _tenantNamespace;
        private readonly string _tenantName;
        private readonly SemaphoreSlim accessTokenSemaphore = new(1, 1);

        private TenantApiAccess(CachedTenantCredentials credentials, ILogger logger, string tenantNamespace, string tenantName)
        {
            _credentials = credentials;
            _logger = logger;
            _tenantNamespace = tenantNamespace;
            _tenantName = tenantName;
        }

        /// <inheritdoc />
        public Uri BaseUri => _credentials.BaseUri;

        /// <inheritdoc />
        public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            await accessTokenSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (!string.IsNullOrEmpty(_credentials.AccessToken) && 
                    _credentials.TokenExpiration.HasValue)
                {
                    var tokenExpiration = _credentials.TokenExpiration.Value;
                    var now = DateTime.UtcNow;
                    
                    if (tokenExpiration > now)
                    {
                        _logger.LogDebugJson($"Using existing valid token for tenant {_tenantNamespace}/{_tenantName}", new 
                        { 
                            tenantNamespace = _tenantNamespace, 
                            tenantName = _tenantName,
                            tokenExpiresAt = tokenExpiration,
                            timeUntilExpiration = (tokenExpiration - now).TotalMinutes
                        });
                        return _credentials.AccessToken;
                    }
                    else
                    {
                        _logger.LogWarningJson($"Token for tenant {_tenantNamespace}/{_tenantName} has reached 90% of expiration time - refreshing", new 
                        { 
                            tenantNamespace = _tenantNamespace, 
                            tenantName = _tenantName,
                            tokenExpiresAt = tokenExpiration,
                            expiredMinutesAgo = (now - tokenExpiration).TotalMinutes
                        });
                    }
                }

                return await RegenerateTokenAsync(cancellationToken);
            }
            finally
            {
                accessTokenSemaphore.Release();
            }
            
            async Task<string> RegenerateTokenAsync(CancellationToken cancellationToken)
            {
                _logger.LogInformationJson($"Generating new access token for tenant {_tenantNamespace}/{_tenantName}", new 
                { 
                    tenantNamespace = _tenantNamespace, 
                    tenantName = _tenantName,
                    domain = _credentials.Domain
                });

                // Get Auth0 Management API token
                var auth = new AuthenticationApiClient(new Uri($"https://{_credentials.Domain}"));
                var authToken = await auth.GetTokenAsync(new ClientCredentialsTokenRequest() 
                { 
                    Audience = $"https://{_credentials.Domain}/api/v2/", 
                    ClientId = _credentials.ClientId, 
                    ClientSecret = _credentials.ClientSecret 
                }, cancellationToken);
                
                if (authToken.AccessToken == null || authToken.AccessToken.Length == 0)
                {
                    _logger.LogErrorJson($"Failed to retrieve management API token for tenant {_tenantNamespace}/{_tenantName}", new 
                    { 
                        tenantNamespace = _tenantNamespace, 
                        tenantName = _tenantName,
                        domain = _credentials.Domain
                    });
                    
                    throw new InvalidOperationException($"Tenant {_tenantNamespace}/{_tenantName} failed to retrieve management API token.");
                }

                // Update cached token with expiration (use 90% of the token lifetime for safety)
                _credentials.AccessToken = authToken.AccessToken;
                _credentials.TokenExpiration = DateTime.UtcNow.AddSeconds(authToken.ExpiresIn * 0.9);

                _logger.LogInformationJson($"Successfully generated access token for tenant {_tenantNamespace}/{_tenantName}", new 
                { 
                    tenantNamespace = _tenantNamespace, 
                    tenantName = _tenantName,
                    tokenExpiresAt = _credentials.TokenExpiration.Value,
                    tokenLifetimeSeconds = authToken.ExpiresIn
                });

                return authToken.AccessToken;
            }
        }

        /// <summary>
        /// Creates a TenantApiAccess instance for the specified tenant
        /// </summary>
        /// <param name="tenant">The tenant</param>
        /// <param name="kube">Kubernetes client</param>
        /// <param name="logger">Logger</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>TenantApiAccess instance</returns>
        public static Task<TenantApiAccess> CreateAsync(V1Tenant tenant, IKubernetesClient kube, ILogger logger, CancellationToken cancellationToken = default)
        {
            var domain = tenant.Spec.Auth?.Domain;
            if (string.IsNullOrWhiteSpace(domain))
                throw new InvalidOperationException($"Tenant {tenant.Namespace()}/{tenant.Name()} has no authentication domain.");

            var secretRef = tenant.Spec.Auth?.SecretRef;
            if (secretRef == null)
                throw new InvalidOperationException($"Tenant {tenant.Namespace()}/{tenant.Name()} has no authentication secret.");

            if (string.IsNullOrWhiteSpace(secretRef.Name))
                throw new InvalidOperationException($"Tenant {tenant.Namespace()}/{tenant.Name()} has no secret name.");

            var secret = kube.Get<V1Secret>(secretRef.Name, string.IsNullOrEmpty(secretRef.NamespaceProperty) ? tenant.Namespace() : secretRef.NamespaceProperty);
            if (secret == null)
                throw new InvalidOperationException($"Tenant {tenant.Namespace()}/{tenant.Name()} has missing secret.");

            if (secret.Data.TryGetValue("clientId", out var clientIdBuf) == false)
                throw new InvalidOperationException($"Tenant {tenant.Namespace()}/{tenant.Name()} has missing clientId value on secret.");

            if (secret.Data.TryGetValue("clientSecret", out var clientSecretBuf) == false)
                throw new InvalidOperationException($"Tenant {tenant.Namespace()}/{tenant.Name()} has missing clientSecret value on secret.");

            var clientId = Encoding.UTF8.GetString(clientIdBuf);
            var clientSecret = Encoding.UTF8.GetString(clientSecretBuf);

            var credentials = new CachedTenantCredentials
            {
                Domain = domain,
                BaseUri = new Uri($"https://{domain}/api/v2/"),
                ClientId = clientId,
                ClientSecret = clientSecret,
                TenantUid = tenant.Uid()
            };

            logger.LogInformationJson($"Created TenantApiAccess for tenant {tenant.Namespace()}/{tenant.Name()}", new 
            { 
                tenantNamespace = tenant.Namespace(), 
                tenantName = tenant.Name(),
                tenantUid = tenant.Uid(),
                domain = domain
            });

            return Task.FromResult(new TenantApiAccess(credentials, logger, tenant.Namespace(), tenant.Name()));
        }
    }
}