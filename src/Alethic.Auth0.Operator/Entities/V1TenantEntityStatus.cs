namespace Alethic.Auth0.Operator.Entities
{

    public interface V1TenantEntityStatus<TConf> : V1EntityStatus<TConf>
        where TConf : class
    {

        string? Id { get; set; }

    }

}
