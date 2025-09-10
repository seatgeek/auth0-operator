using System;
using System.Threading;
using System.Threading.Tasks;

namespace Alethic.Auth0.Operator.Services
{
    /// <summary>
    /// Provides abstracted access to tenant API credentials and token management
    /// </summary>
    public interface ITenantApiAccess
    {
        /// <summary>
        /// Gets the base URI for the Auth0 Management API for this tenant
        /// </summary>
        Uri BaseUri { get; }

        /// <summary>
        /// Gets the current access token for the Auth0 Management API.
        /// Automatically refreshes the token if it has reached 90% of its expiration time.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A valid access token</returns>
        Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    }
}