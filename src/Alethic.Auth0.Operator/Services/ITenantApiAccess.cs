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
        /// Gets the current access token for the Auth0 Management API, or null if not available
        /// </summary>
        string? AccessToken { get; }

        /// <summary>
        /// Gets whether a valid access token is available
        /// </summary>
        bool HasValidToken { get; }

        /// <summary>
        /// Requests regeneration of the access token (e.g., when the current token is expired)
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The new access token</returns>
        Task<string> RegenerateTokenAsync(CancellationToken cancellationToken = default);
    }
}