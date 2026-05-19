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
        /// IMPORTANT: Do not cache the token yourself, call this method each time you need a token.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A valid access token</returns>
        Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Evicts the cached access token, forcing the next <see cref="GetAccessTokenAsync"/> call
        /// to obtain a fresh token from Auth0. Call this after a server-side rejection (e.g. 401)
        /// to recover from a token Auth0 considers invalid even though its local expiration has not yet elapsed.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        Task InvalidateAccessTokenAsync(CancellationToken cancellationToken = default);
    }
}