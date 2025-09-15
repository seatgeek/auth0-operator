using System;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Options;

using KubeOps.Operator;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Alethic.Auth0.Operator
{

    public static class Program
    {

        public static Task Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);

            // Configure OperatorOptions first
            builder.Services.Configure<OperatorOptions>(builder.Configuration.GetSection("Auth0:Operator"));

            // Get OperatorOptions to configure leader election via environment variable
            var operatorOptions = new OperatorOptions();
            builder.Configuration.GetSection("Auth0:Operator").Bind(operatorOptions);

            // Configure leader election based on operatorOptions.LeaderElection
            if (operatorOptions.LeaderElection)
            {
                // Set leader election ID via environment variable (KubeOps 9.x approach)
                Environment.SetEnvironmentVariable("LEADER_ELECTION_ID", operatorOptions.LeaderElectionId);
                Environment.SetEnvironmentVariable("LEADER_ELECTION_DISABLED", "false");
            }
            else
            {
                // Disable leader election - all replicas will process resources independently
                Environment.SetEnvironmentVariable("LEADER_ELECTION_DISABLED", "true");
            }

            // Configure KubeOps operator with automatic leader election
            builder.Services.AddKubernetesOperator().RegisterComponents();

            builder.Services.AddMemoryCache();

            var app = builder.Build();
            return app.RunAsync();
        }

    }

}
