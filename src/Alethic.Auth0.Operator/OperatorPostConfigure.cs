using System;

using Alethic.Auth0.Operator.Options;

using KubeOps.Abstractions.Builder;

using Microsoft.Extensions.Options;

namespace Alethic.Auth0.Operator
{

    class OperatorPostConfigure : IPostConfigureOptions<OperatorSettings>
    {

        readonly IOptions<OperatorOptions> _options;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="options"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public OperatorPostConfigure(IOptions<OperatorOptions> options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc />
        public void PostConfigure(string? name, OperatorSettings options)
        {
            // inherit namespace value specified on Auth0 options
            if (_options.Value.Namespace is not null)
                options.Namespace = _options.Value.Namespace;
        }

    }

}