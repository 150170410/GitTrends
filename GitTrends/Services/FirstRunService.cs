﻿using System;
using Xamarin.Essentials.Interfaces;
using Xamarin.Forms;

namespace GitTrends
{
    public class FirstRunService
    {
        readonly IPreferences _preferences;

        public FirstRunService(IPreferences preferences, GitHubAuthenticationService gitHubAuthenticationService)
        {
#if !AppStore
            UITestBackdoorService.PagePopped += HandlePagePopped;
#endif
            gitHubAuthenticationService.AuthorizeSessionCompleted += HandleAuthorizeSessionCompleted;
            gitHubAuthenticationService.DemoUserActivated += HandleDemoUserActivated;


            _preferences = preferences;
        }

        public bool IsFirstRun
        {
            get => _preferences.Get(nameof(IsFirstRun), true);
            private set => _preferences.Set(nameof(IsFirstRun), value);
        }

        void HandleDemoUserActivated(object sender, EventArgs e) => IsFirstRun = false;
        void HandleAuthorizeSessionCompleted(object sender, AuthorizeSessionCompletedEventArgs e) => IsFirstRun = false;

#if !AppStore
        void HandlePagePopped(object sender, Page e) => IsFirstRun = false;
#endif
    }
}
