// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class HostJsonManager : IHostJsonManager
    {
        private readonly IHostJsonConfigProvider _configProvider;
        private readonly ILogger<HostJsonManager> _logger;

        public HostJsonManager(IHostJsonConfigProvider provider, ILogger<HostJsonManager> logger)
        {
            _configProvider = provider;
            _logger = logger;
        }

        public string GetHostJsonPayload()
        {
            var payload = new JObject();
            var extensions = new JObject();
            foreach (var extension in _configProvider.ExtensionOptions)
            {
                extensions.Add(extension.Key, extension.Value);
            }

            if (payload.Children().Count() == 0)
            {
                _logger.LogWarning("No extensions section found on the HostJsonPayload.");
            }

            payload.Add("extensions", extensions);

            var concurrency = _configProvider.ConcurrencyOption;
            if (concurrency.Children().Count() == 0)
            {
                _logger.LogWarning("No concurrency section found on the HostJsonPayload.");
            }
            payload.Add("concurrency", _configProvider.ConcurrencyOption);
            return payload.ToString();
        }
    }
}
