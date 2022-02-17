// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Azure.WebJobs.Script
{
    public class HostOptionsProvider : IHostOptionsProvider
    {
        private readonly JsonSerializerSettings _concurrencySettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            }
        };

        private readonly IEnumerable<IWebJobsExtensionOptionsConfiguration> _extensionOptionsConfigurations;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<HostOptionsProvider> _logger;
        private readonly IOptionsMonitor<ConcurrencyOptions> _concurrencyOptions;

        public HostOptionsProvider(IServiceProvider provider, IEnumerable<IWebJobsExtensionOptionsConfiguration> extensionOptionsConfigurations, IOptionsMonitor<ConcurrencyOptions> concurrencyOption, ILogger<HostOptionsProvider> logger)
        {
            _extensionOptionsConfigurations = extensionOptionsConfigurations;
            _serviceProvider = provider;
            _logger = logger;
            _concurrencyOptions = concurrencyOption;
        }

        public string GetPayload()
        {
            var payload = new JObject();
            var extensions = new JObject();
            var extensionOptions = GetExtensionOptions();
            foreach (var extension in extensionOptions)
            {
                extensions.Add(extension.Key, extension.Value);
            }
            if (extensionOptions.Count != 0)
            {
                payload.Add("extensions", extensions);
            }

            var concurrency = GetConcurrencyOption();
            payload.Add("concurrency", concurrency);
            return payload.ToString();
        }

        private Dictionary<string, JObject> GetExtensionOptions()
        {
            var configuration = _serviceProvider.GetService<IConfiguration>();
            // for each of the extension options types identified, create a bound instance
            // and format to JObject
            Dictionary<string, JObject> result = new Dictionary<string, JObject>();
            foreach (IWebJobsExtensionOptionsConfiguration extensionOptionsConfigration in _extensionOptionsConfigurations)
            {
                // create the IOptions<T> type
                Type optionsWrapperType = typeof(IOptions<>).MakeGenericType(extensionOptionsConfigration.OptionType);
                object optionsWrapper = _serviceProvider.GetService(optionsWrapperType);
                PropertyInfo valueProperty = optionsWrapperType.GetProperty("Value");

                // create the instance - this will cause configuration binding
                object options = valueProperty.GetValue(optionsWrapper);

                // get the section name from Extension attribute of IExtensionConfigProvider class
                string name = extensionOptionsConfigration.ExtensionInfo.ConfigurationSectionName.CamelCaseString();

                IOptionsFormatter optionsFormatter = options as IOptionsFormatter;
                if (optionsFormatter != null)
                {
                    result.Add(name, JObject.Parse(optionsFormatter.Format()).ToCamelCase());
                }
                else
                {
                    // We don't support the extensions that doesn't implement IOptionsFormatter.
                    _logger.LogInformation($"{extensionOptionsConfigration.OptionType} doesn't support IOptionsFormatter. Ignored.");
                }
            }

            return result;
        }

        private JObject GetConcurrencyOption()
        {
            var concurrencyOption = _concurrencyOptions.CurrentValue;
            var json = JsonConvert.SerializeObject(concurrencyOption, _concurrencySettings);
            return JObject.Parse(json);
        }
    }
}
