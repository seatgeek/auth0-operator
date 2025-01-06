using Alethic.Auth0.Operator.Core.Models;

namespace Alethic.Auth0.Operator.Models
{

    public interface V1TenantEntitySpec<TConf> : V1EntitySpec<TConf>
        where TConf : class
    {

        V1TenantRef? TenantRef { get; set; }

    }

}
