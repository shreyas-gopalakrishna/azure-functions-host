// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ScmHostingConfigurationsTest
    {
        private readonly TestEnvironment _environment;
        private readonly TestLoggerProvider _loggerProvider;
        private readonly LoggerFactory _loggerFactory;

        public ScmHostingConfigurationsTest()
        {
            _environment = new TestEnvironment();
            _loggerProvider = new TestLoggerProvider();
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddProvider(_loggerProvider);
            _environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, "testId");
        }

        [Fact]
        public void ScmHostingConfigurations_Registered_ByDefault()
        {
            IHost host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost()
                .Build();

            var conf = host.Services.GetService<ScmHostingConfigurations>();
            Assert.NotNull(conf);
        }

        [Fact]
        public async Task IngonresIfFileDoesNotExists()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ScmHostingConfigurations conf = new ScmHostingConfigurations(_environment, _loggerFactory, "C:\\somedir\test.txt", DateTime.Now.AddMilliseconds(1), TimeSpan.FromMinutes(5));
                Assert.False(conf.FunctionsWorkerDynamicConcurrencyEnabled);

                await Task.Delay(500);
                var test = conf.FunctionsWorkerDynamicConcurrencyEnabled;

                Assert.Single(_loggerProvider.GetAllLogMessages().Where(x => x.FormattedMessage.Contains("ScmHostingConfigurations does not exists")));
            }
        }

        [Fact]
        public async Task UpdatesConfiguration()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using (TempDirectory tempDir = new TempDirectory())
                {
                    string fileName = Path.Combine(tempDir.Path, "settings.txt");
                    File.WriteAllText(fileName, "key1=value1,key2=value2");

                    ScmHostingConfigurations conf = new ScmHostingConfigurations(_environment, _loggerFactory, fileName, DateTime.Now.AddMilliseconds(1), TimeSpan.FromMinutes(5));
                    Assert.False(conf.FunctionsWorkerDynamicConcurrencyEnabled);

                    await Task.Delay(500);
                    var test = conf.FunctionsWorkerDynamicConcurrencyEnabled;

                    Assert.Single(_loggerProvider.GetAllLogMessages().Where(x => x.FormattedMessage.StartsWith("Updaiting ScmHostingConfigurations")));
                }
            }
        }

        [Fact]
        public async Task WorksAsExpected()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using (TempDirectory tempDir = new TempDirectory())
                {
                    string fileName = Path.Combine(tempDir.Path, "settings.txt");
                    File.WriteAllText(fileName, $"key1=value1,{RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled}=false");

                    ScmHostingConfigurations conf = new ScmHostingConfigurations(_environment, _loggerFactory, fileName, DateTime.Now.AddMilliseconds(100), TimeSpan.FromMilliseconds(100));
                    Assert.False(conf.FunctionsWorkerDynamicConcurrencyEnabled);

                    File.WriteAllText(fileName, $"key1=value1,{RpcWorkerConstants.FunctionsWorkerDynamicConcurrencyEnabled}=true");
                    await Task.Delay(500);
                    Assert.True(conf.FunctionsWorkerDynamicConcurrencyEnabled);

                    File.WriteAllText(fileName, "key1=value1");
                    await Task.Delay(500);
                    Assert.False(conf.FunctionsWorkerDynamicConcurrencyEnabled);

                    Assert.True(_loggerProvider.GetAllLogMessages().Where(x => x.FormattedMessage.StartsWith("Updaiting ScmHostingConfigurations")).Count() == 3);
                }
            }
        }

        [Fact]
        public void ParsesConfig()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using (TempDirectory tempDir = new TempDirectory())
                {
                    string fileName = Path.Combine(tempDir.Path, "settings.txt");
                    File.WriteAllText(fileName, "ENABLE_FEATUREX=1,A=B,TimeOut=123");
                    ScmHostingConfigurations conf = new ScmHostingConfigurations(_environment, _loggerFactory, fileName, DateTime.Now.AddMilliseconds(1), TimeSpan.FromMilliseconds(100));
                    conf.GetValue("test"); // to run Parse
                    Assert.Equal(3, conf.Config.Count);
                    Assert.Equal("1", conf.GetValue("ENABLE_FEATUREX"));
                    Assert.Equal("B", conf.GetValue("A"));
                    Assert.Equal("123", conf.GetValue("TimeOut"));
                }
            }
        }

        [Theory]
        [InlineData("", 0)]
        [InlineData(null, 0)]
        [InlineData(" ", 0)]
        [InlineData("flag1=value1", 1)]
        [InlineData("x=y,", 1)]
        [InlineData("x=y,a=", 1)]
        [InlineData("abcd", 0)]
        public void ReturnsExpectedConfig(string config, int configCount)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using (TempDirectory tempDir = new TempDirectory())
                {
                    string fileName = Path.Combine(tempDir.Path, "settings.txt");
                    File.WriteAllText(fileName, config);
                    ScmHostingConfigurations conf = new ScmHostingConfigurations(_environment, _loggerFactory, fileName, DateTime.Now.AddMilliseconds(1), TimeSpan.FromMilliseconds(100));
                    conf.GetValue("test"); // to run Parse
                    Assert.True(conf.Config.Count == configCount);
                }
            }
        }
    }
}
