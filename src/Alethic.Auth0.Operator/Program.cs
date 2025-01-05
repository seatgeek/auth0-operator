using System;
using System.Threading.Tasks;

using Auth0.ManagementApi;

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
            builder.Services.AddTransient<Util>();
            var app = builder.Build();
            return app.RunAsync();
        }

    }

}
