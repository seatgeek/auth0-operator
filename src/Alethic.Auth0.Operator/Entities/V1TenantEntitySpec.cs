namespace Alethic.Auth0.Operator.Entities
{

    public interface V1TenantEntitySpec<TConf> : V1EntitySpec<TConf>
        where TConf : class
    {

        V1TenantRef? TenantRef { get; set; }

    }

}
