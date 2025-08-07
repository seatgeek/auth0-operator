namespace Alethic.Auth0.Operator.Models
{

    public interface V1EntitySpec<TConf>
        where TConf : class
    {

        /// <summary>
        /// Version of configuration used for initial creation.
        /// </summary>
        TConf? Init { get; set; }

        /// <summary>
        /// Version of configuration used for periodic reconciliation.
        /// </summary>
        TConf? Conf { get; set; }

    }

}
