﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AsyncAwaitBestPractices;
using AsyncAwaitBestPractices.MVVM;
using Autofac;
using GitTrends.Mobile.Shared;
using GitTrends.Shared;
using Refit;
using Xamarin.Forms;

namespace GitTrends
{
    public class RepositoryViewModel : BaseViewModel
    {
        readonly WeakEventManager<PullToRefreshFailedEventArgs> _pullToRefreshFailedEventManager = new WeakEventManager<PullToRefreshFailedEventArgs>();
        readonly RepositoryDatabase _repositoryDatabase;
        readonly GitHubAuthenticationService _gitHubAuthenticationService;
        readonly GitHubGraphQLApiService _gitHubGraphQLApiService;
        readonly SortingService _sortingService;
        readonly GitHubApiV3Service _gitHubApiV3Service;

        bool _isRefreshing;
        string _searchBarText = "";
        IReadOnlyList<Repository> _repositoryList = Enumerable.Empty<Repository>().ToList();
        IReadOnlyList<Repository> _visibleRepositoryList = Enumerable.Empty<Repository>().ToList();

        public RepositoryViewModel(RepositoryDatabase repositoryDatabase,
                                    GitHubAuthenticationService gitHubAuthenticationService,
                                    GitHubGraphQLApiService gitHubGraphQLApiService,
                                    AnalyticsService analyticsService,
                                    SortingService sortingService,
                                    GitHubApiV3Service gitHubApiV3Service,
                                    NotificationService notificationService) : base(analyticsService)
        {
            _repositoryDatabase = repositoryDatabase;
            _gitHubAuthenticationService = gitHubAuthenticationService;
            _gitHubGraphQLApiService = gitHubGraphQLApiService;
            _sortingService = sortingService;
            _gitHubApiV3Service = gitHubApiV3Service;

            PullToRefreshCommand = new AsyncCommand(() => ExecutePullToRefreshCommand(GitHubAuthenticationService.Alias));
            FilterRepositoriesCommand = new Command<string>(SetSearchBarText);
            SortRepositoriesCommand = new Command<SortingOption>(ExecuteSortRepositoriesCommand);

            notificationService.SortingOptionRequested += HandleSortingOptionRequested;
            gitHubAuthenticationService.LoggedOut += HandleGitHubAuthenticationServiceLoggedOut;
            gitHubAuthenticationService.AuthorizeSessionCompleted += HandleAuthorizeSessionCompleted;
            gitHubAuthenticationService.DemoUserActivated += HandleDemoUserActivated;
        }

        public event EventHandler<PullToRefreshFailedEventArgs> PullToRefreshFailed
        {
            add => _pullToRefreshFailedEventManager.AddEventHandler(value);
            remove => _pullToRefreshFailedEventManager.RemoveEventHandler(value);
        }

        public ICommand PullToRefreshCommand { get; }
        public ICommand FilterRepositoriesCommand { get; }
        public ICommand SortRepositoriesCommand { get; }

        public IReadOnlyList<Repository> VisibleRepositoryList
        {
            get => _visibleRepositoryList;
            set => SetProperty(ref _visibleRepositoryList, value);
        }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            set => SetProperty(ref _isRefreshing, value);
        }

        async Task ExecutePullToRefreshCommand(string repositoryOwner)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            _gitHubAuthenticationService.AuthorizeSessionStarted += HandleAuthorizeSessionStarted;
            _gitHubAuthenticationService.LoggedOut += HandleLoggedOut;

            AnalyticsService.Track("Refresh Triggered", "Sorting Option", _sortingService.CurrentOption.ToString());

            try
            {
                await foreach (var retrievedRepositories in _gitHubGraphQLApiService.GetRepositories(repositoryOwner, cancellationTokenSource.Token).ConfigureAwait(false))
                {
                    //Only display the first update to avoid unncessary work on the UIThread
                    var shouldUpdateVisibleRepositoryList = !VisibleRepositoryList.Any();
                    AddRepositoriesToCollection(retrievedRepositories, _searchBarText, shouldUpdateVisibleRepositoryList);
                }

                var completedRepoitories = new List<Repository>();
                await foreach (var retrievedRepositoryWithViewsAndClonesData in _gitHubApiV3Service.UpdateRepositoriesWithViewsAndClonesData(_repositoryList.ToList(), cancellationTokenSource.Token).ConfigureAwait(false))
                {
                    _repositoryDatabase.SaveRepository(retrievedRepositoryWithViewsAndClonesData).SafeFireAndForget();
                    completedRepoitories.Add(retrievedRepositoryWithViewsAndClonesData);

                    //Batch the VisibleRepositoryList Updates to avoid overworking the UI Thread
                    if (!GitHubAuthenticationService.IsDemoUser && completedRepoitories.Count > 20)
                    {
                        AddRepositoriesToCollection(completedRepoitories, _searchBarText);
                        completedRepoitories.Clear();
                    }
                }

                //Add Remaining Repositories to VisibleRepositoryList
                AddRepositoriesToCollection(completedRepoitories, _searchBarText);
            }
            catch (ApiException e) when (e.StatusCode is HttpStatusCode.Unauthorized)
            {
                OnPullToRefreshFailed(new LoginExpiredPullToRefreshEventArgs());

                await _gitHubAuthenticationService.LogOut().ConfigureAwait(false);
                await _repositoryDatabase.DeleteAllData().ConfigureAwait(false);

                VisibleRepositoryList = Enumerable.Empty<Repository>().ToList();
            }
            catch (ApiException)
            {
                var repositoryList = await _repositoryDatabase.GetRepositories().ConfigureAwait(false);

                SetRepositoriesCollection(repositoryList, _searchBarText);
            }
            catch (Exception e)
            {
                AnalyticsService.Report(e);
                OnPullToRefreshFailed(new ErrorPullToRefreshEventArgs(e.Message));
            }
            finally
            {
                _gitHubAuthenticationService.LoggedOut -= HandleLoggedOut;
                _gitHubAuthenticationService.AuthorizeSessionStarted -= HandleAuthorizeSessionStarted;

                if (cancellationTokenSource.IsCancellationRequested)
                    UpdateListForLoggedOutUser();

                IsRefreshing = false;
            }

            void HandleLoggedOut(object sender, EventArgs e) => cancellationTokenSource.Cancel();
            void HandleAuthorizeSessionStarted(object sender, EventArgs e) => cancellationTokenSource.Cancel();
        }

        void ExecuteSortRepositoriesCommand(SortingOption option)
        {
            if (_sortingService.CurrentOption == option)
                _sortingService.IsReversed = !_sortingService.IsReversed;
            else
                _sortingService.IsReversed = false;

            _sortingService.CurrentOption = option;

            AnalyticsService.Track("SortingOption Changed", new Dictionary<string, string>
            {
                { nameof(SortingService.CurrentOption), _sortingService.CurrentOption.ToString() },
                { nameof(SortingService.IsReversed), _sortingService.IsReversed.ToString() }
            });

            UpdateVisibleRepositoryList(_searchBarText, _sortingService.CurrentOption, _sortingService.IsReversed);
        }

        void SetRepositoriesCollection(in IEnumerable<Repository> repositories, string searchBarText)
        {
            _repositoryList = repositories.ToList();

            UpdateVisibleRepositoryList(searchBarText, _sortingService.CurrentOption, _sortingService.IsReversed);
        }

        void AddRepositoriesToCollection(IEnumerable<Repository> repositories, string searchBarText, bool shouldUpdateVisibleRepositoryList = true)
        {
            var updatedRepositoryList = _repositoryList.Concat(repositories);
            _repositoryList = RepositoryService.RemoveForksAndDuplicates(updatedRepositoryList).ToList();

            if (shouldUpdateVisibleRepositoryList)
                UpdateVisibleRepositoryList(searchBarText, _sortingService.CurrentOption, _sortingService.IsReversed);
        }

        void UpdateVisibleRepositoryList(in string searchBarText, in SortingOption sortingOption, in bool isReversed)
        {
            var filteredRepositoryList = GetRepositoriesFilteredBySearchBar(_repositoryList, searchBarText);

            VisibleRepositoryList = SortingService.SortRepositories(filteredRepositoryList, sortingOption, isReversed).ToList();
        }

        void UpdateListForLoggedOutUser()
        {
            _repositoryList = Enumerable.Empty<Repository>().ToList();
            UpdateVisibleRepositoryList(string.Empty, _sortingService.CurrentOption, _sortingService.IsReversed);
        }

        IEnumerable<Repository> GetRepositoriesFilteredBySearchBar(in IEnumerable<Repository> repositories, string searchBarText)
        {
            if (string.IsNullOrWhiteSpace(searchBarText))
                return repositories;

            return repositories.Where(x => x.Name.Contains(searchBarText, StringComparison.OrdinalIgnoreCase));
        }

        void SetSearchBarText(string text)
        {
            if (EqualityComparer<string>.Default.Equals(_searchBarText, text))
                return;

            _searchBarText = text;

            if (_repositoryList.Any())
                UpdateVisibleRepositoryList(_searchBarText, _sortingService.CurrentOption, _sortingService.IsReversed);
        }

        //Work-around because ContentPage.OnAppearing does not fire after `ContentPage.PushModalAsync()`
        void HandleAuthorizeSessionCompleted(object sender, AuthorizeSessionCompletedEventArgs e) => IsRefreshing |= e.IsSessionAuthorized;

        void HandleDemoUserActivated(object sender, EventArgs e) => IsRefreshing = true;

        void HandleGitHubAuthenticationServiceLoggedOut(object sender, EventArgs e) => UpdateListForLoggedOutUser();

        void HandleSortingOptionRequested(object sender, SortingOption sortingOption) => SortRepositoriesCommand.Execute(sortingOption);

        void OnPullToRefreshFailed(PullToRefreshFailedEventArgs pullToRefreshFailedEventArgs) =>
            _pullToRefreshFailedEventManager.HandleEvent(this, pullToRefreshFailedEventArgs, nameof(PullToRefreshFailed));
    }
}
