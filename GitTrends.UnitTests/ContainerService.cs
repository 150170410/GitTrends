﻿using System;
using GitTrends.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xamarin.Essentials.Interfaces;

namespace GitTrends.UnitTests
{
    public class ContainerService
    {
        readonly static Lazy<ServiceProvider> _serviceProviderHolder = new Lazy<ServiceProvider>(CreateContainer);

        public static ServiceProvider Container => _serviceProviderHolder.Value;

        static ServiceProvider CreateContainer()
        {
            var services = new ServiceCollection();

            //GitTrends Services
            services.AddSingleton<AzureFunctionsApiService>();
            services.AddSingleton<BackgroundFetchService>();
            services.AddSingleton<DeepLinkingService>();
            services.AddSingleton<GitHubApiV3Service>();
            services.AddSingleton<GitHubAuthenticationService>();
            services.AddSingleton<GitHubGraphQLApiService>();
            services.AddSingleton<GitHubUserService>();
            services.AddSingleton<FavIconService>();
            services.AddSingleton<NotificationService>();
            services.AddSingleton<ReferringSitesDatabase>();
            services.AddSingleton<RepositoryDatabase>();
            services.AddSingleton<SortingService>();

            //Mocks
            services.AddSingleton<IAnalyticsService, MockAnalyticsService>();
            services.AddSingleton<IAppInfo, MockAppInfo>();
            services.AddSingleton<IBrowser, MockBrowser>();
            services.AddSingleton<IFileSystem, MockFileSystem>();
            services.AddSingleton<IEmail, MockEmail>();
            services.AddSingleton<ILauncher, MockLauncher>();
            services.AddSingleton<IMainThread, MockMainThread>();
            services.AddSingleton<ISecureStorage, MockSecureStorage>();
            services.AddSingleton<IPreferences, MockPreferences>();

            return services.BuildServiceProvider();
        }
    }
}
