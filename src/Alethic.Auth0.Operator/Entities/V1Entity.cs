namespace Alethic.Auth0.Operator.Entities
{

    public interface V1Entity<TSpec, TStatus, TConf>
        where TSpec : V1EntitySpec<TConf>
        where TStatus : V1EntityStatus<TConf>
        where TConf : class
    {

        TSpec Spec { get; }

        TStatus Status { get; }

    }

}
