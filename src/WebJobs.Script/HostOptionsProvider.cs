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
        private readonly JsonSerializer _serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
        });

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

            var concurrency = GetConcurrencyOptions();
            payload.Add("concurrency", concurrency);
            return payload.ToString();
        }

        private Dictionary<string, JObject> GetExtensionOptions()
        {
            var configuration = _serviceProvider.GetService<IConfiguration>();
            // for each of the extension options types identified, create a bound instance
            // and format to JObject
            Dictionary<string, JObject> result = new Dictionary<string, JObject>();
            foreach (IWebJobsExtensionOptionsConfiguration extensionOptionsConfiguration in _extensionOptionsConfigurations)
            {
                // create the IOptions<T> type
                Type optionsWrapperType = typeof(IOptions<>).MakeGenericType(extensionOptionsConfiguration.OptionType);
                object optionsWrapper = _serviceProvider.GetService(optionsWrapperType);
                if (optionsWrapper != null)
                {
                    PropertyInfo valueProperty = optionsWrapperType.GetProperty("Value");

                    // create the instance - this will cause configuration binding
                    object options = valueProperty.GetValue(optionsWrapper);

                    // get the section name from Extension attribute of IExtensionConfigProvider class
                    string name = extensionOptionsConfiguration.ExtensionInfo.ConfigurationSectionName.CamelCaseString();

                    IOptionsFormatter optionsFormatter = options as IOptionsFormatter;
                    if (optionsFormatter != null)
                    {
                        result.Add(name, JObject.Parse(optionsFormatter.Format()).ToCamelCase());
                    }
                }
            }

            return result;
        }

        private JObject GetConcurrencyOptions()
        {
            var concurrencyOptions = _concurrencyOptions.CurrentValue;
            return JObject.FromObject(concurrencyOptions, _serializer);
        }
    }
}
