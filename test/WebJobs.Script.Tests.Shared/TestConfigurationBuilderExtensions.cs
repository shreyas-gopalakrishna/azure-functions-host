// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public static class TestConfigurationBuilderExtensions
    {
        private const string ConfigFile = "appsettings.tests.json";
        private static string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azurefunctions", ConfigFile);

        public static IConfigurationBuilder AddTestSettings(this IConfigurationBuilder builder) => builder.AddJsonFile(configPath, true);

        // appsettings.tests.json settings can be overwritten by a later IConfigurationSource IF the overriding setting value is not null or empty.
        // If the overriding setting value is null or empty, IConfigurationBuilder will use the value from the previous source.
        // For integration tests that require a null value setting override (ex. no AzureWebJobsStorage setting), call this method from
        // TestFunctionHost's Action<IConfigurationBuilder> parameters (WebHost AND ScriptHost levels.)
        public static IConfigurationBuilder RemoveTestSettings(this IConfigurationBuilder builder)
        {
            var testConfigSource = builder.Sources.FirstOrDefault(s => (s as JsonConfigurationSource)?.Path == ConfigFile);
            builder.Sources.Remove(testConfigSource);
            return builder;
        }
    }
}
