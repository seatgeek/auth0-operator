namespace Alethic.Auth0.Operator.Entities
{

    public interface V1EntitySpec<TConf>
        where TConf : class
    {

        TConf? Conf { get; set; }

    }

}
