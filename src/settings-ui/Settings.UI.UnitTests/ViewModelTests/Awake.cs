// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;

using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ViewModelTests
{
    [TestClass]
    public class Awake
    {
        private static readonly List<string> DefaultLabels = ["30 minutes", "1 hour", "2 hours", "4 hours", "6 hours", "8 hours", "10 hours", "12 hours"];

        private AwakeViewModel _viewModel;
        private int _trayIntervalSaves;

        [TestInitialize]
        public void SetUp()
        {
            _trayIntervalSaves = 0;
            _viewModel = new AwakeViewModel();
            _viewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(AwakeViewModel.TrayIntervals))
                {
                    _trayIntervalSaves++;
                }
            };
        }

        [TestMethod]
        [DataRow(0u, 30u, "30 minutes")]
        [DataRow(0u, 1u, "1 minute")]
        [DataRow(1u, 0u, "1 hour")]
        [DataRow(2u, 0u, "2 hours")]
        [DataRow(1u, 30u, "1 hour 30 minutes")]
        [DataRow(0u, 0u, "0 minutes")]
        public void FormatTrayIntervalLabel_ProducesReadableLabel(uint hours, uint minutes, string expected)
        {
            Assert.AreEqual(expected, AwakeViewModel.FormatTrayIntervalLabel(hours, minutes));
        }

        [TestMethod]
        public void FromSeconds_RoundsPartialMinutesUp()
        {
            AwakeTrayInterval interval = AwakeTrayInterval.FromSeconds(90);

            Assert.AreEqual(0u, interval.Hours);
            Assert.AreEqual(2u, interval.Minutes);

            interval = AwakeTrayInterval.FromSeconds(5400);

            Assert.AreEqual(1u, interval.Hours);
            Assert.AreEqual(30u, interval.Minutes);
        }

        [TestMethod]
        public void TrayIntervals_ShowDefaultsWhenNothingIsSaved()
        {
            _viewModel.ModuleSettings = new AwakeSettings();

            CollectionAssert.AreEqual(DefaultLabels, _viewModel.TrayIntervals.Select(i => i.Label).ToList());
            Assert.AreEqual(0, _trayIntervalSaves, "Loading must not write settings.");
            Assert.AreEqual(0, _viewModel.ModuleSettings.Properties.CustomTrayTimes.Count, "Loading must leave the saved list untouched.");
        }

        [TestMethod]
        public void TrayIntervals_LoadFromSavedCustomTrayTimes()
        {
            AwakeSettings settings = new();
            settings.Properties.CustomTrayTimes["15 minutes"] = 900;
            settings.Properties.CustomTrayTimes["4 hours"] = 14400;

            _viewModel.ModuleSettings = settings;

            CollectionAssert.AreEqual(
                new List<string> { "15 minutes", "4 hours" },
                _viewModel.TrayIntervals.Select(i => i.Label).ToList());
            Assert.AreEqual(0, _trayIntervalSaves);
        }

        [TestMethod]
        public void AddTrayInterval_AppendsOneHourMoreThanLastAndSaves()
        {
            _viewModel.ModuleSettings = new AwakeSettings();

            _viewModel.AddTrayInterval();

            Assert.AreEqual(DefaultLabels.Count + 1, _viewModel.TrayIntervals.Count);
            Assert.AreEqual("13 hours", _viewModel.TrayIntervals[^1].Label);
            Assert.AreEqual(1, _trayIntervalSaves);

            Dictionary<string, uint> saved = _viewModel.ModuleSettings.Properties.CustomTrayTimes;
            CollectionAssert.AreEqual(DefaultLabels.Concat(["13 hours"]).ToList(), saved.Keys.ToList());
            CollectionAssert.AreEqual(new List<uint> { 1800, 3600, 7200, 14400, 21600, 28800, 36000, 43200, 46800 }, saved.Values.ToList());
        }

        [TestMethod]
        public void EditingAnInterval_UpdatesLabelAndSaves()
        {
            _viewModel.ModuleSettings = new AwakeSettings();

            _viewModel.TrayIntervals[2].Minutes = 45;

            Assert.AreEqual("2 hours 45 minutes", _viewModel.TrayIntervals[2].Label);
            Assert.AreEqual(1, _trayIntervalSaves);
            Assert.AreEqual(9900u, _viewModel.ModuleSettings.Properties.CustomTrayTimes["2 hours 45 minutes"]);
        }

        [TestMethod]
        public void RemoveTrayInterval_RemovesAndSaves()
        {
            _viewModel.ModuleSettings = new AwakeSettings();

            _viewModel.RemoveTrayInterval(_viewModel.TrayIntervals[0]);

            Assert.AreEqual(DefaultLabels.Count - 1, _viewModel.TrayIntervals.Count);
            Assert.AreEqual(1, _trayIntervalSaves);
            CollectionAssert.AreEqual(DefaultLabels.Skip(1).ToList(), _viewModel.ModuleSettings.Properties.CustomTrayTimes.Keys.ToList());
        }

        [TestMethod]
        public void ResetTrayIntervals_RestoresDefaultsAndSaves()
        {
            AwakeSettings settings = new();
            settings.Properties.CustomTrayTimes["15 minutes"] = 900;
            _viewModel.ModuleSettings = settings;

            _viewModel.ResetTrayIntervals();

            CollectionAssert.AreEqual(DefaultLabels, _viewModel.ModuleSettings.Properties.CustomTrayTimes.Keys.ToList());
            Assert.AreEqual(1, _trayIntervalSaves);
        }

        [TestMethod]
        public void BuildTrayTimes_DropsZeroDurationsAndDuplicateLabels()
        {
            List<AwakeTrayInterval> intervals =
            [
                new AwakeTrayInterval(0, 0),
                new AwakeTrayInterval(0, 30),
                new AwakeTrayInterval(0, 30),
                new AwakeTrayInterval(1, 0),
            ];

            Dictionary<string, uint> trayTimes = AwakeViewModel.BuildTrayTimes(intervals);

            CollectionAssert.AreEqual(new List<string> { "30 minutes", "1 hour" }, trayTimes.Keys.ToList());
        }

        [TestMethod]
        public void ReloadingUnchangedSettings_KeepsExistingIntervalInstances()
        {
            _viewModel.ModuleSettings = new AwakeSettings();
            _viewModel.AddTrayInterval();
            AwakeTrayInterval first = _viewModel.TrayIntervals[0];

            _viewModel.ModuleSettings = (AwakeSettings)_viewModel.ModuleSettings.Clone();

            Assert.AreSame(first, _viewModel.TrayIntervals[0]);
            Assert.AreEqual(1, _trayIntervalSaves);
        }
    }
}
