using System.Collections.Generic;
using Auth0.ManagementApi.Models;

namespace Alethic.Auth0.Operator.Controllers
{
    internal class ClientConnectionsResponse
    {
        [Newtonsoft.Json.JsonProperty("connections")]
        public List<Connection> Connections { get; set; } = new List<Connection>();
    }

    internal class Auth0ErrorResponse
    {
        [Newtonsoft.Json.JsonProperty("statusCode")]
        public int StatusCode { get; set; }

        [Newtonsoft.Json.JsonProperty("error")]
        public string? Error { get; set; }

        [Newtonsoft.Json.JsonProperty("message")]
        public string? Message { get; set; }

        [Newtonsoft.Json.JsonProperty("errorCode")]
        public string? ErrorCode { get; set; }
    }
}
