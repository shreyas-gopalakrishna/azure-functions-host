﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class DefaultSecretManagerProvider : ISecretManagerProvider
    {
        private const string FileStorage = "Files";
        private readonly ILoggerFactory _loggerFactory;
        private readonly IMetricsLogger _metricsLogger;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _options;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IConfiguration _configuration;
        private readonly IEnvironment _environment;
        private readonly HostNameProvider _hostNameProvider;
        private readonly StartupContextProvider _startupContextProvider;
        private readonly IAzureStorageProvider _azureStorageProvider;
        private Lazy<ISecretManager> _secretManagerLazy;
        private Lazy<bool> _secretsEnabledLazy;

        public DefaultSecretManagerProvider(IOptionsMonitor<ScriptApplicationHostOptions> options, IHostIdProvider hostIdProvider,
            IConfiguration configuration, IEnvironment environment, ILoggerFactory loggerFactory, IMetricsLogger metricsLogger, HostNameProvider hostNameProvider, StartupContextProvider startupContextProvider, IAzureStorageProvider azureStorageProvider)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _options = options ?? throw new ArgumentNullException(nameof(options));
            _hostIdProvider = hostIdProvider ?? throw new ArgumentNullException(nameof(hostIdProvider));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _hostNameProvider = hostNameProvider ?? throw new ArgumentNullException(nameof(hostNameProvider));
            _startupContextProvider = startupContextProvider ?? throw new ArgumentNullException(nameof(startupContextProvider));

            _loggerFactory = loggerFactory;
            _metricsLogger = metricsLogger ?? throw new ArgumentNullException(nameof(metricsLogger));
            _secretManagerLazy = new Lazy<ISecretManager>(Create);
            _secretsEnabledLazy = new Lazy<bool>(GetSecretsEnabled);

            // When these options change (due to specialization), we need to reset the secret manager.
            options.OnChange(_ => ResetSecretManager());

            _azureStorageProvider = azureStorageProvider;
        }

        public bool SecretsEnabled
        {
            get
            {
                if (_secretManagerLazy.IsValueCreated)
                {
                    return true;
                }
                return _secretsEnabledLazy.Value;
            }
        }

        public ISecretManager Current => _secretManagerLazy.Value;

        private void ResetSecretManager()
        {
            Interlocked.Exchange(ref _secretsEnabledLazy, new Lazy<bool>(GetSecretsEnabled));
            Interlocked.Exchange(ref _secretManagerLazy, new Lazy<ISecretManager>(Create));
        }

        private ISecretManager Create() => SecretsEnabled ? new SecretManager(CreateSecretsRepository(), _loggerFactory.CreateLogger<SecretManager>(), _metricsLogger, _hostNameProvider, _startupContextProvider) : null;

        internal ISecretsRepository CreateSecretsRepository()
        {
            ISecretsRepository repository = null;
            var repositoryType = GetSecretsRepositoryType();

            if (repositoryType == typeof(FileSystemSecretsRepository))
            {
                repository = new FileSystemSecretsRepository(_options.CurrentValue.SecretsPath, _loggerFactory.CreateLogger<FileSystemSecretsRepository>(), _environment);
            }
            else if (repositoryType == typeof(KeyVaultSecretsRepository))
            {
                string azureWebJobsSecretStorageKeyVaultName = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultName);
                string azureWebJobsSecretStorageKeyVaultConnectionString = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageKeyVaultConnectionString);

                repository = new KeyVaultSecretsRepository(Path.Combine(_options.CurrentValue.SecretsPath, "Sentinels"),
                                                           azureWebJobsSecretStorageKeyVaultName,
                                                           azureWebJobsSecretStorageKeyVaultConnectionString,
                                                           _loggerFactory.CreateLogger<KeyVaultSecretsRepository>(),
                                                           _environment);
            }
            else if (repositoryType == typeof(KubernetesSecretsRepository))
            {
                repository = new KubernetesSecretsRepository(_environment, new SimpleKubernetesClient(_environment, _loggerFactory.CreateLogger<SimpleKubernetesClient>()));
            }
            else if (repositoryType == typeof(BlobStorageSasSecretsRepository))
            {
                string secretStorageSas = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageSas);
                string siteSlotName = _environment.GetAzureWebsiteUniqueSlotName() ?? _hostIdProvider.GetHostIdAsync(CancellationToken.None).GetAwaiter().GetResult();
                repository = new BlobStorageSasSecretsRepository(Path.Combine(_options.CurrentValue.SecretsPath, "Sentinels"),
                                                                 secretStorageSas,
                                                                 siteSlotName,
                                                                 _loggerFactory.CreateLogger<BlobStorageSasSecretsRepository>(),
                                                                 _environment,
                                                                 _azureStorageProvider);
            }
            else if (repositoryType == typeof(BlobStorageSecretsRepository))
            {
                string siteSlotName = _environment.GetAzureWebsiteUniqueSlotName() ?? _hostIdProvider.GetHostIdAsync(CancellationToken.None).GetAwaiter().GetResult();
                repository = new BlobStorageSecretsRepository(Path.Combine(_options.CurrentValue.SecretsPath, "Sentinels"),
                                                              ConnectionStringNames.Storage,
                                                              siteSlotName,
                                                              _loggerFactory.CreateLogger<BlobStorageSecretsRepository>(),
                                                              _environment,
                                                              _azureStorageProvider);
            }

            ILogger logger = _loggerFactory.CreateLogger<DefaultSecretManagerProvider>();
            logger.LogInformation("Resolved secret storage provider {provider}", repository?.Name);

            return repository;
        }

        /// <summary>
        /// Determines the repository Type to use based on configured settings.
        /// </summary>
        /// <remarks>
        /// For scenarios where the app isn't configured for key storage (e.g. no AzureWebJobsSecretStorageType explicitly configured,
        /// no storage connection string for default blob storage, etc.). Note that it's still possible for the creation of the repository
        /// to fail due to invalid values. This method just does preliminary config checks to determine the Type.
        /// </remarks>
        /// <returns>The repository Type; null if no storage provider determined.</returns>
        internal Type GetSecretsRepositoryType()
        {
            string secretStorageType = Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageType);
            string secretStorageSas = _environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsSecretStorageSas);

            if (secretStorageType != null && secretStorageType.Equals(FileStorage, StringComparison.OrdinalIgnoreCase))
            {
                return typeof(FileSystemSecretsRepository);
            }
            else if (secretStorageType != null && secretStorageType.Equals("keyvault", StringComparison.OrdinalIgnoreCase))
            {
                return typeof(KeyVaultSecretsRepository);
            }
            else if (secretStorageType != null && secretStorageType.Equals("kubernetes", StringComparison.OrdinalIgnoreCase))
            {
                return typeof(KubernetesSecretsRepository);
            }
            else if (secretStorageSas != null)
            {
                return typeof(BlobStorageSasSecretsRepository);
            }
            else if (_azureStorageProvider.ConnectionExists(ConnectionStringNames.Storage))
            {
                return typeof(BlobStorageSecretsRepository);
            }
            else
            {
                return null;
            }
        }

        internal bool GetSecretsEnabled()
        {
            return GetSecretsRepositoryType() != null;
        }
    }
}