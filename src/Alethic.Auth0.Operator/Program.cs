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

            builder.Services.Configure<ReconciliationConfig>(
                builder.Configuration.GetSection("ReconciliationConfig"));

            // Allows default .NET console output when running manually
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ENABLE_SIMPLE_CONSOLE_LOGGING")))
            {
                builder.Logging.AddSimpleConsole(options =>
                {
                    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                });
            }

            var app = builder.Build();
            return app.RunAsync();
        }

    }

}
