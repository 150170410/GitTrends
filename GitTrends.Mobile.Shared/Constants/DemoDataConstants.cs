﻿using System;

namespace GitTrends.Mobile.Shared
{
    static class DemoDataConstants
    {
        public const string Alias = "Demo";
        public const string Name = "Demo User";
        public const string AvatarUrl = "https://avatars3.githubusercontent.com/u/61480020?s=400&v=4";
        public const int RepoCount = 50;
        public const int ReferringSitesCount = 11;

        const string _loremIpsum = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum ";

        static readonly Random _random = new Random((int)DateTime.Now.Ticks);

        public static string GetRandomText()
        {
            var startIndex = _random.Next(_loremIpsum.Length / 2);
            var length = _random.Next(_loremIpsum.Length - 1 - startIndex);

            return _loremIpsum.Substring(startIndex, length);
        }

        public static int GetRandomNumber() => _random.Next(100);
    }
}
