namespace Alethic.Auth0.Operator.Entities
{

    public interface V1TenantEntity<TSpec, TStatus, TConf> : V1Entity<TSpec, TStatus, TConf>
        where TSpec : V1TenantEntitySpec<TConf>
        where TStatus : V1TenantEntityStatus<TConf>
        where TConf : class
    {



    }

}
