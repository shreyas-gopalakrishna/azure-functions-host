// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    internal class ScmHostingConfigurations : IScmHostingConfigurations
    {
        private readonly TimeSpan _updateInterval = TimeSpan.FromMinutes(10);
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;

        private string _configsFile =
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\SiteExtensions\kudu\ScmHostingConfigurations.txt");

        private DateTime _configsTTL = DateTime.UtcNow.AddMinutes(1); // first sync will be in 1 minute
        private Dictionary<string, string> _configs;
        private bool _failed = false;

        public ScmHostingConfigurations(IEnvironment environment, ILoggerFactory loggerFactory)
        {
            _environment = environment;
            _logger = loggerFactory.CreateLogger<ScmHostingConfigurations>() ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        // For tests
        internal ScmHostingConfigurations(IEnvironment environment, ILoggerFactory loggerFactory, string configsFile, DateTime configsTTL, TimeSpan updateInterval)
            : this(environment, loggerFactory)
        {
            _configsFile = configsFile;
            _configsTTL = configsTTL;
            _updateInterval = updateInterval;
        }

        // for tests
        internal Dictionary<string, string> Config => _configs;

        public bool FunctionsWorkerDynamicConcurrencyEnabled
        {
            get
            {
                return bool.TryParse(GetValue(RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled, null), out bool boolValue) ? boolValue : false;
            }
        }

        public string GetValue(string key, string defaultValue = null)
        {
            // Once 
            if (_failed)
            {
                return defaultValue;
            }

            // ScmHostingConfigurations supported only on Winodws (all SKUs)
            if (!_environment.IsWindowsAzureManagedHosting() || _failed)
            {
                return defaultValue;
            }

            var configs = _configs;
            if (configs == null || DateTime.UtcNow > _configsTTL)
            {
                _configsTTL = DateTime.UtcNow.Add(_updateInterval);

                try
                {
                    if (FileUtility.FileExists(_configsFile))
                    {
                        var settings = FileUtility.ReadAllText(_configsFile);
                        configs = Parse(settings);
                        _configs = configs;
                        _logger.LogInformation($"Updaiting ScmHostingConfigurations '{settings}'");
                    }
                    else
                    {
                        _logger.LogDebug("ScmHostingConfigurations does not exists");
                        _failed = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Exception while getting ScmHostingConfigurations");
                    _failed = true;
                }
            }

            return (configs == null || !configs.TryGetValue(key, out string value)) ? defaultValue : value;
        }

        private static Dictionary<string, string> Parse(string settings)
        {
            return string.IsNullOrEmpty(settings)
                ? new Dictionary<string, string>()
                : settings
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries))
                    .Where(a => a.Length == 2)
                    .ToDictionary(a => a[0], a => a[1], StringComparer.OrdinalIgnoreCase);
        }
    }
}
