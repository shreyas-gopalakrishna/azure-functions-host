// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public interface IHostJsonConfigProvider
    {
        Dictionary<string, JObject> ExtensionOptions { get; }

        JObject ConcurrencyOption { get; }

        void RegisterOptionTypes(IServiceCollection services);
    }
}
