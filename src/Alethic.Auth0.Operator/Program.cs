using System;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Models;

using KubeOps.Operator;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Alethic.Auth0.Operator
{

    public static class Program
    {

        public static Task Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);
            builder.Services.AddKubernetesOperator().RegisterComponents();
            builder.Services.AddMemoryCache();

            builder.Services.Configure<ReconciliationConfig>(config =>
            {
                config.Interval = TimeSpan.FromSeconds(30);

                if (int.TryParse(Environment.GetEnvironmentVariable("RECONCILIATION_INTERVAL_SECONDS"), out var seconds))
                {
                    config.Interval = TimeSpan.FromSeconds(seconds);
                }
            });

            builder.Logging.AddSimpleConsole(options =>
            {
                options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
            });

            var app = builder.Build();
            return app.RunAsync();
        }

    }

}
