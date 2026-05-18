using System.Collections.Generic;
using Auth0.ManagementApi.Models;

namespace Alethic.Auth0.Operator.Controllers
{
    internal class ClientConnectionsResponse
    {
        [Newtonsoft.Json.JsonProperty("connections")]
        public List<Connection> Connections { get; set; } = new List<Connection>();
    }
}
