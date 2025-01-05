namespace Alethic.Auth0.Operator.Entities
{

    public interface V1EntityStatus<TConf>
        where TConf : class
    {

        TConf? LastConf { get; set; }

    }

}
