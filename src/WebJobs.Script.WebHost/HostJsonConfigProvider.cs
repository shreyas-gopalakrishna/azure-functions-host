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
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class HostJsonConfigProvider : IDisposable, IHostJsonConfigProvider
    {
        private readonly JsonSerializerSettings _concurrencySettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            }
        };

        private List<Type> _extensionOptionTypes;
        private IScriptHostManager _scriptHostManager;
        private bool _disposedValue;
        private List<string> _extensionConfigSectionNames;

        public HostJsonConfigProvider(IScriptHostManager scriptHostManager)
        {
            _scriptHostManager = scriptHostManager;
            _scriptHostManager.ActiveHostChanged += ActiveHostChanged;
        }

        public Dictionary<string, JObject> ExtensionOptions { get; private set; }

        public JObject ConcurrencyOption { get; private set; }

        public void RegisterOptionTypes(IServiceCollection services)
        {
            var extensionOptions = services.Where(p => p.ServiceType.IsGenericType && p.ServiceType.GetGenericTypeDefinition() == typeof(IConfigureOptions<>) && p.ImplementationFactory != null && p.ImplementationFactory.Target.ToString().StartsWith(typeof(WebJobsExtensionBuilderExtensions).FullName)).ToList();
            _extensionOptionTypes = extensionOptions.Select(p => p.ServiceType.GetGenericArguments()[0]).ToList();
            SetUpExtensionConfigSectionNames(services);
        }

        private void SetUpExtensionConfigSectionNames(IServiceCollection services)
        {
            _extensionConfigSectionNames = new List<string>();
            var extensionConfigProviders = services.Where(p => p.ServiceType.IsAssignableFrom(typeof(IExtensionConfigProvider)));
            MethodInfo fromExtensionGenericMethod = typeof(ExtensionInfo).GetMethod(nameof(ExtensionInfo.FromExtension));
            foreach (var extension in extensionConfigProviders)
            {
                // TODO - will we always have an implementation Type here?
                if (extension.ImplementationType != null)
                {
                    MethodInfo fromExtensionMethod = fromExtensionGenericMethod.MakeGenericMethod(extension.ImplementationType);
                    ExtensionInfo extensionInfo = (ExtensionInfo)fromExtensionMethod.Invoke(null, null);
                    // TODO: camel case
                    _extensionConfigSectionNames.Add(extensionInfo.ConfigurationSectionName);
                }
            }
        }

        private void ActiveHostChanged(object sender, ActiveHostChangedEventArgs e)
        {
            if (e.NewHost != null)
            {
                ExtensionOptions = GetExtensionOptions(_extensionOptionTypes, e.NewHost.Services);
                ConcurrencyOption = GetConcurrencyOption(e.NewHost.Services);
            }
        }

        private Dictionary<string, JObject> GetExtensionOptions(List<Type> extensionOptionTypes, IServiceProvider serviceProvider)
        {
            var configuration = serviceProvider.GetService<IConfiguration>();
            // for each of the extension options types identified, create a bound instance
            // and format to JObject
            Dictionary<string, JObject> result = new Dictionary<string, JObject>();
            foreach (Type optionsType in extensionOptionTypes)
            {
                // create the IOptions<T> type
                Type optionsWrapperType = typeof(IOptions<>).MakeGenericType(optionsType);
                object optionsWrapper = serviceProvider.GetService(optionsWrapperType);
                PropertyInfo valueProperty = optionsWrapperType.GetProperty("Value");

                // create the instance - this will cause configuration binding
                object options = valueProperty.GetValue(optionsWrapper);

                // format extension name based on convention from options name
                // TODO: format this properly
                int idx = optionsType.Name.LastIndexOf("Options");
                string name = optionsType.Name.Substring(0, 1).ToLower() + optionsType.Name.Substring(1, idx - 1);

                IOptionsFormatter optionsFormatter = options as IOptionsFormatter;
                if (optionsFormatter != null)
                {
                    result.Add(name, JObject.Parse(optionsFormatter.Format()).ToCamelCase());
                }
                else
                {
                    // We don't support the extensions that doesn't implement IOptionsFormatter.

                    // TODO : For Avoiding Breaking change of Durable Functions with Arc
                    // Once Durable Functions supoorts IOptionsFormatter, remove this line
                    if ("durableTask".Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(name, JObject.FromObject(options).ToCamelCase());
                    }
                }
            }

            return result;
        }

        private string GetExtensionConfigSectionName(string candidateName)
        {
            if (_extensionConfigSectionNames.Contains(candidateName))
            {
                return candidateName;
            }

            foreach (var sectionName in _extensionConfigSectionNames)
            {
                // TODO PluralizationService is not implemented on .NET only on .NET Framework.
                // For the current extensions, this implementation is ok.

                // candidate is plural but sectionName is not plural
                if (string.Equals($"{sectionName}s", candidateName))
                {
                    return sectionName;
                }
                // candidate is sigular but sectionName is plural
                if (string.Equals(sectionName, $"{candidateName}s"))
                {
                    return sectionName;
                }
            }
            return candidateName;
        }

        public JObject GetConcurrencyOption(IServiceProvider serviceProvider)
        {
            var configuration = serviceProvider.GetService<IConfiguration>();
            var concurrencyOption = new ConcurrencyOptions();
            var key = ConfigurationSectionNames.JobHost + ConfigurationPath.KeyDelimiter
                    + "concurrency";
            configuration.Bind(key, concurrencyOption);
            var json = JsonConvert.SerializeObject(concurrencyOption, _concurrencySettings);
            return JObject.Parse(json);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _scriptHostManager.ActiveHostChanged -= ActiveHostChanged;
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
