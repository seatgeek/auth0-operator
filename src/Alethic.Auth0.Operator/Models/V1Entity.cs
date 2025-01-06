namespace Alethic.Auth0.Operator.Models
{

    public interface V1Entity<TSpec, TStatus, TConf>
        where TSpec : V1EntitySpec<TConf>
        where TStatus : V1EntityStatus
        where TConf : class
    {

        TSpec Spec { get; }

        TStatus Status { get; }

    }

}
