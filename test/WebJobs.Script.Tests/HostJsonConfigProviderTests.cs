// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Description;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Configuration;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class HostJsonConfigProviderTests
    {
        [Fact]
        public void OverwriteWithAppSettings_Success()
        {
            var settings = new Dictionary<string, string>
                {
                    { "AzureFunctionsJobHost:extensions:test:config1", "test1" },
                    { "AzureFunctionsJobHost:extensions:test:config2", "test2" }
                };
            var result = ExecuteHostJsonConfigProvider<TestExtensionConfigProvider, TestOptions>(settings);
            Assert.Equal(ReadFixture("TestBasicBindings.json"), result["test"].ToString());
        }

        [Fact]
        public void IrregularNamingConvention_Success()
        {
            var settings = new Dictionary<string, string>
            {
                { "AzureFunctionsJobHost:extensions:eventHubs:config1", "test1" },
                { "AzureFunctionsJobHost:extensions:eventHubs:config2", "test2" }
            };
            var result = ExecuteHostJsonConfigProvider<TestEventHubConfigProvider, EventHubOptions>(settings);
            Assert.Equal(ReadFixture("TestBasicBindings.json"), result["eventHubs"].ToString());
        }

        // TODO For Durable Functions, The spec of the durable functions are not clear.
        // Asking Chris abou it.
        [Fact]
        public void DurableFunctions_Success()
        {
        }

        [Fact]
        public void InCompliant_Extension_Without_ExtensionsAttribute_Success()
        {
            var settings = new Dictionary<string, string>
                {
                    { "AzureFunctionsJobHost:extensions:testNoExtensionAttributeConfigProvider:config1", "test1" },
                    { "AzureFunctionsJobHost:extensions:testNoExtensionAttributeConfigProvider:config2", "test2" }
                };
            var result = ExecuteHostJsonConfigProvider<TestNoExtensionAttributeConfigProvider, TestOptions>(settings);
            Assert.Equal(ReadFixture("TestBasicBindings.json"), result["testNoExtensionAttributeConfigProvider"].ToString());
        }

        [Fact]
        public void InCompliant_Extension_Without_IOptionFormatters_Ignored()
        {
            var settings = new Dictionary<string, string>
                {
                    { "AzureFunctionsJobHost:extensions:test:config1", "test1" },
                    { "AzureFunctionsJobHost:extensions:test:config2", "test2" }
                };
            var result = ExecuteHostJsonConfigProvider<TestExtensionConfigProvider, TestNoIOptionsFormatterOptions>(settings);
            Assert.Equal(0, result.Count);
        }

        [Fact]
        public void HostChange_Reflect_ThePayload_Success()
        {
            var fristSettings = new Dictionary<string, string>
            {
                { "AzureFunctionsJobHost:extensions:test:config1", "test1" },
                { "AzureFunctionsJobHost:extensions:test:config2", "test2" }
            };
            var firstHostContext = SetupHostJsonConfigProvider<TestExtensionConfigProvider, TestOptions>(fristSettings);
            firstHostContext.Manager.Raise(mock => mock.ActiveHostChanged += null, new ActiveHostChangedEventArgs(null, firstHostContext.Host));
            var firstResult = firstHostContext.Provider.ExtensionOptions;

            Assert.Equal(ReadFixture("TestBasicBindings.json"), firstResult["test"].ToString());

            var secondSettings = new Dictionary<string, string>
            {
                { "AzureFunctionsJobHost:extensions:eventHubs:config1", "test0" },
                { "AzureFunctionsJobHost:extensions:eventHubs:config2", "test2" }
            };
            var secondHostContext = SetupHostJsonConfigProvider<TestEventHubConfigProvider, EventHubOptions>(secondSettings, firstHostContext.Manager, firstHostContext.Provider);
            firstHostContext.Manager.Raise(mock => mock.ActiveHostChanged += null, new ActiveHostChangedEventArgs(firstHostContext.Host, secondHostContext.Host));
            var secondResult = secondHostContext.Provider.ExtensionOptions;

            Assert.Equal(1, secondResult.Count);  // Assert the old extension is not included.
            Assert.Equal(ReadFixture("TestModifiedBindings.json"), secondResult["eventHubs"].ToString());
        }

        [Fact]
        public void Multiple_Extensions_Works_Success()
        {
            var settings = new Dictionary<string, string>
            {
                { "AzureWebJobsConfigurationSection", "AzureFunctionsJobHost" },
                { "AzureFunctionsJobHost:extensions:test:config1", "test1" },
                { "AzureFunctionsJobHost:extensions:test:config2", "test2" },
                { "AzureFunctionsJobHost:extensions:eventHubs:config1", "test0" },
                { "AzureFunctionsJobHost:extensions:eventHubs:config2", "test2" }
            };

            var manager = new Mock<IScriptHostManager>();
            var provider = new HostJsonConfigProvider(manager.Object);
            var hostBuilder = new HostBuilder()
            .ConfigureAppConfiguration(b =>
            {
                b.AddInMemoryCollection(settings);
            })
            .ConfigureDefaultTestHost(b =>
            {
                b.AddExtension<TestExtensionConfigProvider>()
                .BindOptions<TestOptions>();
                b.AddExtension<TestEventHubConfigProvider>()
                .BindOptions<EventHubOptions>();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IHostJsonConfigProvider>(provider);
                provider.RegisterOptionTypes(services);
            });

            var host = hostBuilder.Build();
            var hostJsonConfigProvider = host.Services.GetService<IHostJsonConfigProvider>();
            manager.Raise(mock => mock.ActiveHostChanged += null, new ActiveHostChangedEventArgs(null, host));

            var result = hostJsonConfigProvider.ExtensionOptions;
            Assert.Equal(2, result.Count);
            Assert.Equal(ReadFixture("TestBasicBindings.json"), result["test"].ToString());
            Assert.Equal(ReadFixture("TestModifiedBindings.json"), result["eventHubs"].ToString());
        }

        [Fact]
        public void ConcurrencyOption_Success()
        {
            var settings = new Dictionary<string, string>
                {
                    { "AzureFunctionsJobHost:concurrency:dynamicConcurrencyEnabled", "true" },
                    { "AzureFunctionsJobHost:concurrency:maximumFunctionConcurrency", "20" },
                };
            var result = ExecuteHostJsonConfigProviderForConcurrency<TestExtensionConfigProvider, TestOptions>(settings);
            Assert.Equal(ReadFixture("TestBasicConcurrency.json"), result.ToString());
        }

        [Fact]
        public void ConcurrencyOption_Default_Success()
        {
            var settings = new Dictionary<string, string>();
            var result = ExecuteHostJsonConfigProviderForConcurrency<TestExtensionConfigProvider, TestOptions>(settings);
            Assert.Equal(ReadFixture("TestDefaultConcurrency.json"), result.ToString());
        }

        private Dictionary<string, JObject> ExecuteHostJsonConfigProvider<TExtension, TOptions>(Dictionary<string, string> settings) where TExtension : class, IExtensionConfigProvider where TOptions : class
        {
            // Use the same ConfigurationSection name as Azure Functions
            var hostContext = SetupHostJsonConfigProvider<TExtension, TOptions>(settings);

            hostContext.Manager.Raise(mock => mock.ActiveHostChanged += null, new ActiveHostChangedEventArgs(null, hostContext.Host));
            return hostContext.Provider.ExtensionOptions;
        }

        private JObject ExecuteHostJsonConfigProviderForConcurrency<TExtension, TOptions>(Dictionary<string, string> settings) where TExtension : class, IExtensionConfigProvider where TOptions : class
        {
            // Use the same ConfigurationSection name as Azure Functions
            var hostContext = SetupHostJsonConfigProvider<TExtension, TOptions>(settings);

            hostContext.Manager.Raise(mock => mock.ActiveHostChanged += null, new ActiveHostChangedEventArgs(null, hostContext.Host));
            return hostContext.Provider.ConcurrencyOption;
        }

        private TestHostContext SetupHostJsonConfigProvider<TExtension, TOptions>(Dictionary<string, string> settings, Mock<IScriptHostManager> manager = null, HostJsonConfigProvider provider = null) where TExtension : class, IExtensionConfigProvider where TOptions : class
        {
            settings["AzureWebJobsConfigurationSection"] = "AzureFunctionsJobHost";

            manager ??= new Mock<IScriptHostManager>();
            provider ??= new HostJsonConfigProvider(manager.Object);
            var hostBuilder = new HostBuilder()
            .ConfigureAppConfiguration(b =>
            {
                b.AddInMemoryCollection(settings);
            })
            .ConfigureDefaultTestHost(b =>
            {
                b.AddExtension<TExtension>()
                .BindOptions<TOptions>();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IHostJsonConfigProvider>(provider);
                provider.RegisterOptionTypes(services);
            });

            var host = hostBuilder.Build();
            var hostJsonConfigProvider = host.Services.GetService<IHostJsonConfigProvider>();
            return new TestHostContext() { Manager = manager, Provider = provider, Host = host };
        }

        public static string ReadFixture(string name)
        {
            var path = Path.Combine("TestFixture", "HostJsonConfigProviderTests", name);
            return File.ReadAllText(path);
        }

        private class TestHostContext
        {
            public Mock<IScriptHostManager> Manager { get; set; }

            public HostJsonConfigProvider Provider { get; set; }

            public IHost Host { get; set; }
        }

        // class definition TestProvider
        [Extension("Test")]
        private class TestExtensionConfigProvider : IExtensionConfigProvider
        {
            public TestExtensionConfigProvider()
            {
            }

            public void Initialize(ExtensionConfigContext context)
            {
            }
        }

        private class TestOptions : IOptionsFormatter
        {
            public string Config1 { get; set; } = "default";

            public string Config2 { get; set; } = "default";

            public string Config3 { get; set; } = "default";

            public string Format()
            {
                return JObject.FromObject(this).ToString();
            }
        }

        // class definition TestEventHubs
        [Extension("EventHubs", configurationSection: "EventHubs")]
        private class TestEventHubConfigProvider : IExtensionConfigProvider
        {
            public TestEventHubConfigProvider()
            {
            }

            public void Initialize(ExtensionConfigContext context)
            {
            }
        }

        private class EventHubOptions : IOptionsFormatter
        {
            public string Config1 { get; set; } = "default";

            public string Config2 { get; set; } = "default";

            public string Config3 { get; set; } = "default";

            public string Format()
            {
                return JObject.FromObject(this).ToString();
            }
        }

        // class definition DurableTask

        [Extension("DurableTask", "DurableTask")]
        private class TestDurableTaskConfigProvider : IExtensionConfigProvider
        {
            public TestDurableTaskConfigProvider()
            {
            }

            public void Initialize(ExtensionConfigContext context)
            {
            }
        }

        private class DurableTaskOptions
        {
            public string Config1 { get; set; } = "default";

            public string Config2 { get; set; } = "default";

            public string Config3 { get; set; } = "default";
        }

        // Non compliant extension
        // No Extension Section
        private class TestNoExtensionAttributeConfigProvider : IExtensionConfigProvider
        {
            public TestNoExtensionAttributeConfigProvider()
            {
            }

            public void Initialize(ExtensionConfigContext context)
            {
            }
        }

        // No IOptionsFormatter
        private class TestNoIOptionsFormatterOptions
        {
            public string Config1 { get; set; } = "default";

            public string Config2 { get; set; } = "default";

            public string Config3 { get; set; } = "default";
        }
    }

    public static class TestExtensions
    {
        public static IHostBuilder ConfigureDefaultTestHost(this IHostBuilder builder, Action<IWebJobsBuilder> configureWebJobs, params Type[] type)
        {
            return builder.ConfigureWebJobs(configureWebJobs);
        }
    }
}
