using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Security
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, nameof(AuthenticationLevelHandlerTests))]
    public class AuthenticationLevelHandlerTests
    // TODO: This should be a more general E2E test suite - maybe for SWA? Or combine with storage/secret E3E tests?
    {
        public static IEnumerable<object[]> GetData()
        {
            yield return new object[] { @"TestScripts\CSharp", RpcWorkerConstants.DotNetLanguageWorkerName,
                new Dictionary<string, string>() { ["AzureWebJobsStorage"] = string.Empty }};

            // test when AzureWebJobsSecretStorageType=None
            // test when AzureWebJobsSecretStorageType= something else
            // test when AzureWebJobsSecretStorageType is not set

            // test for Node scenarios too
        }

        [Theory]
        [MemberData(nameof(GetData))]
        public async Task NoStorage_ReturnsExpected(string scriptRoot, string runtime, IDictionary<string, string> config)
        {
            // this initialization fails because ensuring the host has started up requires calling an admin endpoint
            // need to update the TestFunctionHost.GetHostStatusAsync() method to use JWT bearer auth if secrets not enabled
            using (var fixture = new AuthenticationLevelHandlerTestFixture(scriptRoot, runtime, config))
            {
                // call here to assert there's no connection string?

                var code = "Puffin";

                var response = await fixture.Host.HttpClient.GetAsync($"api/HttpTrigger?code={code}");
                response.EnsureSuccessStatusCode();
            }
        }
    }

    public class AuthenticationLevelHandlerTestFixture : IDisposable
    {
        public TestFunctionHost Host;
        private bool _disposed;

        public AuthenticationLevelHandlerTestFixture(string scriptRoot, string testRuntime, IDictionary<string, string> webHostConfig)
        {
            string scriptPath = Path.Combine(Environment.CurrentDirectory, scriptRoot);
            string logPath = Path.Combine(Path.GetTempPath(), @"Functions");
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, testRuntime);
            Environment.SetEnvironmentVariable("AzureWebEncryptionKey", "0F75CA46E7EBDD39E4CA6B074D1F9A5972B849A55F91A248");

            Host = new TestFunctionHost(scriptPath, logPath,
                configureScriptHostServices: s =>
                {
                    s.Configure<ScriptJobHostOptions>(o =>
                    {
                        o.Functions = new[] { "HttpTrigger" };
                    });
                },
                configureWebHostServices: s =>
                {
                    var iSecretManagerProvider = s.FirstOrDefault(d => d.ServiceType == typeof(ISecretManagerProvider));
                    s.Remove(iSecretManagerProvider);
                    s.TryAddSingleton<ISecretManagerProvider, DefaultSecretManagerProvider>();
                },
                configureWebHostAppConfiguration: configBuilder =>
                {
                    configBuilder.RemoveTestSettings();
                },
                configureScriptHostAppConfiguration: configBuilder =>
                {
                    configBuilder.RemoveTestSettings();
                });

            TestHelpers.WaitForWebHost(Host.HttpClient);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Host?.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
