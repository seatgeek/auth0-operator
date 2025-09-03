using System.Threading.Tasks;

using Alethic.Auth0.Operator.Options;

using k8s;

using KubeOps.Operator;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Alethic.Auth0.Operator
{

    public static class Program
    {

        public static Task Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);
            builder.Services.AddKubernetesOperator().RegisterComponents();
            builder.Services.AddMemoryCache();
            builder.Services.Configure<OperatorOptions>(builder.Configuration.GetSection("Auth0:Operator"));
            
            // Register native Kubernetes client for Server-Side Apply operations
            builder.Services.AddSingleton<IKubernetes>(provider =>
            {
                var config = KubernetesClientConfiguration.IsInCluster() 
                    ? KubernetesClientConfiguration.InClusterConfig()
                    : KubernetesClientConfiguration.BuildConfigFromConfigFile();
                return new Kubernetes(config);
            });

            var app = builder.Build();
            return app.RunAsync();
        }

    }

}
