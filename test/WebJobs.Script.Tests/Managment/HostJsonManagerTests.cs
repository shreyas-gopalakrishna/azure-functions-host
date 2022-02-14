// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    public class HostJsonManagerTests
    {
        [Fact]
        public void GetHostJsonPayloadTest()
        {
            Dictionary<string, JObject> expected = new Dictionary<string, JObject>
            {
                { "http", GetExpectedExtensionsPayload() }
            };
            var bag = GetHostJsonManagerBag(expected, GetExpectedConcurrencyPayload());
            var result = bag.HostJsonManager.GetHostJsonPayload();
            Assert.Equal(GetExpectedHostJsonPayload(), result);
        }

        [Fact]
        public void NoExtensionSectionFoundTest()
        {
            var bag = GetHostJsonManagerBag(new Dictionary<string, JObject>(), GetExpectedConcurrencyPayload());
            var result = bag.HostJsonManager.GetHostJsonPayload();
            var log = bag.LoggerProvider.GetAllLogMessages();
            Assert.Equal("No extensions section found on the HostJsonPayload.", log[0].FormattedMessage);
        }

        private HostJsonManagerBag GetHostJsonManagerBag(Dictionary<string, JObject> extensionsSection, JObject concurrencySection)
        {
            var configProviderMock = new Mock<IHostJsonConfigProvider>();
            configProviderMock.Setup(p => p.ExtensionOptions).Returns(extensionsSection);
            configProviderMock.Setup(p => p.ConcurrencyOption).Returns(concurrencySection);

            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            return new HostJsonManagerBag
            {
                HostJsonManager = new HostJsonManager(configProviderMock.Object, loggerFactory.CreateLogger<HostJsonManager>()),
                LoggerProvider = loggerProvider
            };
        }

        // TODO If the concurrency section

        private static JObject GetExpectedExtensionsPayload()
        {
            return ReadPayloadFileByJObject("ExpectedHttpExtensionsPayload.json");
        }

        private static JObject GetExpectedConcurrencyPayload()
        {
             return ReadPayloadFileByJObject("ExpectedConcurrencyPayload.json");
        }

        private static string GetExpectedHostJsonPayload()
        {
            return ReadPayloadFile("ExpectedHostJsonPayload.json");
        }

        private static JObject ReadPayloadFileByJObject(string fileName)
        {
            var content = ReadPayloadFile(fileName);
            return JObject.Parse(content);
        }

        private static string ReadPayloadFile(string fileName)
        {
            return File.ReadAllText(Path.Join("Managment", "Payloads", fileName));
        }

        private class HostJsonManagerBag
        {
            public IHostJsonManager HostJsonManager { get; set; }

            public TestLoggerProvider LoggerProvider { get; set; }
        }
    }
}
