﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AsyncAwaitBestPractices.MVVM;
using GitTrends.Mobile.Shared;
using GitTrends.Shared;
using Xamarin.Forms;

namespace GitTrends
{
    class TrendsViewModel : BaseViewModel
    {
        const string _emptyDataViewText_NoTrafficYet = "No traffic yet";

        readonly GitHubApiV3Service _gitHubApiV3Service;

        bool _isFetchingData = true;
        bool _isViewsSeriesVisible, _isUniqueViewsSeriesVisible, _isClonesSeriesVisible, _isUniqueClonesSeriesVisible;

        string _viewsStatisticsText = string.Empty;
        string _uniqueViewsStatisticsText = string.Empty;
        string _clonesStatisticsText = string.Empty;
        string _uniqueClonesStatisticsText = string.Empty;
        string _emptyDataViewText = string.Empty;

        List<DailyViewsModel>? _dailyViewsList;
        List<DailyClonesModel>? _dailyClonesList;

        public TrendsViewModel(GitHubApiV3Service gitHubApiV3Service,
                                AnalyticsService analyticsService,
                                TrendsChartSettingsService trendsChartSettingsService) : base(analyticsService)
        {
            _gitHubApiV3Service = gitHubApiV3Service;

            IsViewsSeriesVisible = trendsChartSettingsService.ShouldShowViewsByDefault;
            IsUniqueViewsSeriesVisible = trendsChartSettingsService.ShouldShowUniqueViewsByDefault;
            IsClonesSeriesVisible = trendsChartSettingsService.ShouldShowClonesByDefault;
            IsUniqueClonesSeriesVisible = trendsChartSettingsService.ShouldShowUniqueClonesByDefault;

            ViewsCardTappedCommand = new Command(() => IsViewsSeriesVisible = !IsViewsSeriesVisible);
            UniqueViewsCardTappedCommand = new Command(() => IsUniqueViewsSeriesVisible = !IsUniqueViewsSeriesVisible);
            ClonesCardTappedCommand = new Command(() => IsClonesSeriesVisible = !IsClonesSeriesVisible);
            UniqueClonesCardTappedCommand = new Command(() => IsUniqueClonesSeriesVisible = !IsUniqueClonesSeriesVisible);

            FetchDataCommand = new AsyncCommand<(Repository Repository, CancellationToken CancellationToken)>(tuple => ExecuteFetchDataCommand(tuple.Repository, tuple.CancellationToken));
        }

        public ICommand ViewsCardTappedCommand { get; }
        public ICommand UniqueViewsCardTappedCommand { get; }
        public ICommand ClonesCardTappedCommand { get; }
        public ICommand UniqueClonesCardTappedCommand { get; }

        public ICommand FetchDataCommand { get; }

        public double DailyViewsClonesMinValue { get; } = 0;

        public bool IsEmptyDataViewVisible => !IsChartVisible && !IsFetchingData;
        public bool IsChartVisible => DailyViewsList.Sum(x => x.TotalViews + x.TotalUniqueViews) + DailyClonesList.Sum(x => x.TotalClones + x.TotalUniqueClones) > 0;
        public DateTime MinDateValue => DateTimeService.GetMinimumLocalDateTime(DailyViewsList, DailyClonesList);
        public DateTime MaxDateValue => DateTimeService.GetMaximumLocalDateTime(DailyViewsList, DailyClonesList);

        public double DailyViewsClonesMaxValue
        {
            get
            {
                const int minimumValue = 20;

                var dailyViewMaxValue = DailyViewsList.Any() ? DailyViewsList.Max(x => x.TotalViews) : 0;
                var dailyClonesMaxValue = DailyClonesList.Any() ? DailyClonesList.Max(x => x.TotalClones) : 0;

                return Math.Max(Math.Max(dailyViewMaxValue, dailyClonesMaxValue), minimumValue);
            }
        }

        public string EmptyDataViewText
        {
            get => _emptyDataViewText;
            set => SetProperty(ref _emptyDataViewText, value);
        }

        public string ViewsStatisticsText
        {
            get => _viewsStatisticsText;
            set => SetProperty(ref _viewsStatisticsText, value);
        }

        public string UniqueViewsStatisticsText
        {
            get => _uniqueViewsStatisticsText;
            set => SetProperty(ref _uniqueViewsStatisticsText, value);
        }

        public string ClonesStatisticsText
        {
            get => _clonesStatisticsText;
            set => SetProperty(ref _clonesStatisticsText, value);
        }

        public string UniqueClonesStatisticsText
        {
            get => _uniqueClonesStatisticsText;
            set => SetProperty(ref _uniqueClonesStatisticsText, value);
        }

        public bool IsViewsSeriesVisible
        {
            get => _isViewsSeriesVisible;
            set => SetProperty(ref _isViewsSeriesVisible, value);
        }

        public bool IsUniqueViewsSeriesVisible
        {
            get => _isUniqueViewsSeriesVisible;
            set => SetProperty(ref _isUniqueViewsSeriesVisible, value);
        }

        public bool IsClonesSeriesVisible
        {
            get => _isClonesSeriesVisible;
            set => SetProperty(ref _isClonesSeriesVisible, value);
        }

        public bool IsUniqueClonesSeriesVisible
        {
            get => _isUniqueClonesSeriesVisible;
            set => SetProperty(ref _isUniqueClonesSeriesVisible, value);
        }

        public bool IsFetchingData
        {
            get => _isFetchingData;
            set => SetProperty(ref _isFetchingData, value, () =>
            {
                OnPropertyChanged(nameof(IsEmptyDataViewVisible));
                OnPropertyChanged(nameof(IsChartVisible));
            });
        }

        public List<DailyViewsModel> DailyViewsList
        {
            get => _dailyViewsList ??= Enumerable.Empty<DailyViewsModel>().ToList();
            set => SetProperty(ref _dailyViewsList, value, UpdateDailyViewsListPropertiesChanged);
        }

        public List<DailyClonesModel> DailyClonesList
        {
            get => _dailyClonesList ??= Enumerable.Empty<DailyClonesModel>().ToList();
            set => SetProperty(ref _dailyClonesList, value, UpdateDailyClonesListPropertiesChanged);
        }

        async Task ExecuteFetchDataCommand(Repository repository, CancellationToken cancellationToken)
        {
            IReadOnlyList<DailyViewsModel> repositoryViews;
            IReadOnlyList<DailyClonesModel> repositoryClones;

            var minimumTimeTask = Task.Delay(2000);

            try
            {
                if (repository.DailyClonesList.Any() && repository.DailyViewsList.Any())
                {
                    repositoryViews = repository.DailyViewsList;
                    repositoryClones = repository.DailyClonesList;

                    await minimumTimeTask.ConfigureAwait(false);
                }
                else
                {
                    IsFetchingData = true;

                    var getRepositoryViewStatisticsTask = _gitHubApiV3Service.GetRepositoryViewStatistics(repository.OwnerLogin, repository.Name, cancellationToken);
                    var getRepositoryCloneStatisticsTask = _gitHubApiV3Service.GetRepositoryCloneStatistics(repository.OwnerLogin, repository.Name, cancellationToken);

                    await Task.WhenAll(getRepositoryViewStatisticsTask, getRepositoryCloneStatisticsTask).ConfigureAwait(false);

                    var repositoryViewsResponse = await getRepositoryViewStatisticsTask.ConfigureAwait(false);
                    var repositoryClonesResponse = await getRepositoryCloneStatisticsTask.ConfigureAwait(false);

                    repositoryViews = repositoryViewsResponse.DailyViewsList;
                    repositoryClones = repositoryClonesResponse.DailyClonesList;
                }

                EmptyDataViewText = _emptyDataViewText_NoTrafficYet;
            }
            catch (Exception e)
            {
                repositoryViews = Enumerable.Empty<DailyViewsModel>().ToList();
                repositoryClones = Enumerable.Empty<DailyClonesModel>().ToList();

                EmptyDataViewText = EmptyDataView.UnableToRetrieveDataText;

                AnalyticsService.Report(e);
            }
            finally
            {
                //Display the Activity Indicator for a minimum time to ensure consistant UX 
                await minimumTimeTask.ConfigureAwait(false);
                IsFetchingData = false;
            }

            DailyViewsList = repositoryViews.OrderBy(x => x.Day).ToList();
            DailyClonesList = repositoryClones.OrderBy(x => x.Day).ToList();

            ViewsStatisticsText = repositoryViews.Sum(x => x.TotalViews).ConvertToAbbreviatedText();
            UniqueViewsStatisticsText = repositoryViews.Sum(x => x.TotalUniqueViews).ConvertToAbbreviatedText();

            ClonesStatisticsText = repositoryClones.Sum(x => x.TotalClones).ConvertToAbbreviatedText();
            UniqueClonesStatisticsText = repositoryClones.Sum(x => x.TotalUniqueClones).ConvertToAbbreviatedText();

            PrintDays();
        }

        void UpdateDailyClonesListPropertiesChanged()
        {
            OnPropertyChanged(nameof(IsEmptyDataViewVisible));
            OnPropertyChanged(nameof(IsChartVisible));

            OnPropertyChanged(nameof(DailyViewsClonesMaxValue));
            OnPropertyChanged(nameof(DailyViewsClonesMinValue));

            OnPropertyChanged(nameof(MinDateValue));
            OnPropertyChanged(nameof(MaxDateValue));
        }

        void UpdateDailyViewsListPropertiesChanged()
        {
            OnPropertyChanged(nameof(IsEmptyDataViewVisible));
            OnPropertyChanged(nameof(IsChartVisible));

            OnPropertyChanged(nameof(DailyViewsClonesMaxValue));
            OnPropertyChanged(nameof(DailyViewsClonesMaxValue));

            OnPropertyChanged(nameof(MinDateValue));
            OnPropertyChanged(nameof(MaxDateValue));
        }

        [Conditional("DEBUG")]
        void PrintDays()
        {
            Debug.WriteLine("Clones");
            foreach (var cloneDay in DailyClonesList.Select(x => x.Day))
                Debug.WriteLine(cloneDay);

            Debug.WriteLine("");

            Debug.WriteLine("Views");
            foreach (var viewDay in DailyViewsList.Select(x => x.Day))
                Debug.WriteLine(viewDay);
        }
    }
}
