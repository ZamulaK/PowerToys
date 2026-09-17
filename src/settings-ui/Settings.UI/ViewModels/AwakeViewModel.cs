// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.Library.Helpers;

namespace Microsoft.PowerToys.Settings.UI.ViewModels
{
    public partial class AwakeViewModel : Observable
    {
        /// <summary>
        /// The intervals the Awake tray menu offers when the user has not customized the list.
        /// Mirrors Manager.GetDefaultTrayOptions() in the Awake module.
        /// </summary>
        private static readonly (uint Hours, uint Minutes)[] DefaultTrayIntervals =
        [
            (0, 30),
            (1, 0),
            (2, 0),
            (4, 0),
            (6, 0),
            (8, 0),
            (10, 0),
            (12, 0),
        ];

        public AwakeViewModel()
        {
            TrayIntervals.CollectionChanged += TrayIntervals_CollectionChanged;
        }

        public AwakeSettings ModuleSettings
        {
            get => _moduleSettings;
            set
            {
                if (_moduleSettings != value)
                {
                    _moduleSettings = value;
                    RefreshModuleSettings();
                    RefreshEnabledState();
                }
            }
        }

        public bool IsEnabled
        {
            get
            {
                if (_enabledStateIsGPOConfigured)
                {
                    return _enabledGPOConfiguration;
                }
                else
                {
                    return _isEnabled;
                }
            }

            set
            {
                if (_isEnabled != value)
                {
                    if (_enabledStateIsGPOConfigured)
                    {
                        // If it's GPO configured, shouldn't be able to change this state.
                        return;
                    }

                    _isEnabled = value;

                    RefreshEnabledState();

                    NotifyPropertyChanged();
                }
            }
        }

        public bool IsEnabledGpoConfigured
        {
            get => _enabledStateIsGPOConfigured;
            set
            {
                if (_enabledStateIsGPOConfigured != value)
                {
                    _enabledStateIsGPOConfigured = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public bool EnabledGPOConfiguration
        {
            get => _enabledGPOConfiguration;
            set
            {
                if (_enabledGPOConfiguration != value)
                {
                    _enabledGPOConfiguration = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public bool IsExpirationConfigurationEnabled => ModuleSettings.Properties.Mode == AwakeMode.EXPIRABLE && IsEnabled;

        public bool IsTimeConfigurationEnabled => ModuleSettings.Properties.Mode == AwakeMode.TIMED && IsEnabled;

        public bool IsScreenConfigurationPossibleEnabled => ModuleSettings.Properties.Mode != AwakeMode.PASSIVE && IsEnabled;

        public AwakeMode Mode
        {
            get => ModuleSettings.Properties.Mode;
            set
            {
                if (ModuleSettings.Properties.Mode != value)
                {
                    ModuleSettings.Properties.Mode = value;

                    if (value == AwakeMode.TIMED && IntervalMinutes == 0 && IntervalHours == 0)
                    {
                        // Handle the special case where both hours and minutes are zero.
                        // Otherwise, this will reset to passive very quickly in the UI.
                        ModuleSettings.Properties.IntervalMinutes = 1;
                        OnPropertyChanged(nameof(IntervalMinutes));
                    }
                    else if (value == AwakeMode.EXPIRABLE && ExpirationDateTime <= DateTimeOffset.Now)
                    {
                        // To make sure that we're not tracking expirable keep-awake in the past,
                        // let's make sure that every time it's enabled from the settings UI, it's
                        // five (5) minutes into the future.
                        ExpirationDateTime = DateTimeOffset.Now.AddMinutes(5);

                        // The expiration date/time is updated and will send the notification
                        // but we need to do this manually for the expiration time that is
                        // bound to the time control on the settings page.
                        OnPropertyChanged(nameof(ExpirationTime));
                    }

                    OnPropertyChanged(nameof(IsTimeConfigurationEnabled));
                    OnPropertyChanged(nameof(IsScreenConfigurationPossibleEnabled));
                    OnPropertyChanged(nameof(IsExpirationConfigurationEnabled));

                    NotifyPropertyChanged();
                }
            }
        }

        public bool KeepDisplayOn
        {
            get => ModuleSettings.Properties.KeepDisplayOn;
            set
            {
                if (ModuleSettings.Properties.KeepDisplayOn != value)
                {
                    ModuleSettings.Properties.KeepDisplayOn = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public uint IntervalHours
        {
            get => ModuleSettings.Properties.IntervalHours;
            set
            {
                if (ModuleSettings.Properties.IntervalHours != value)
                {
                    ModuleSettings.Properties.IntervalHours = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public uint IntervalMinutes
        {
            get => ModuleSettings.Properties.IntervalMinutes;
            set
            {
                if (ModuleSettings.Properties.IntervalMinutes != value)
                {
                    ModuleSettings.Properties.IntervalMinutes = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public DateTimeOffset ExpirationDateTime
        {
            get => ModuleSettings.Properties.ExpirationDateTime;
            set
            {
                if (ModuleSettings.Properties.ExpirationDateTime != value)
                {
                    ModuleSettings.Properties.ExpirationDateTime = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public TimeSpan ExpirationTime
        {
            get => ExpirationDateTime.TimeOfDay;
            set
            {
                if (ExpirationDateTime.TimeOfDay != value)
                {
                    ExpirationDateTime = new DateTime(ExpirationDateTime.Year, ExpirationDateTime.Month, ExpirationDateTime.Day, value.Hours, value.Minutes, value.Seconds);
                }
            }
        }

        /// <summary>
        /// Gets the editable list of "keep awake for" intervals offered by the Awake tray icon menu.
        /// Edits are written back to <see cref="AwakeProperties.CustomTrayTimes"/> and saved automatically.
        /// </summary>
        public ObservableCollection<AwakeTrayInterval> TrayIntervals { get; } = [];

        /// <summary>
        /// Converts the interval list to the dictionary the Awake module reads. Entries with a
        /// duration of zero are dropped and duplicate labels keep their first occurrence.
        /// </summary>
        public static Dictionary<string, uint> BuildTrayTimes(IEnumerable<AwakeTrayInterval> intervals)
        {
            Dictionary<string, uint> trayTimes = [];

            foreach (AwakeTrayInterval interval in intervals)
            {
                if (interval.TotalSeconds == 0)
                {
                    continue;
                }

                trayTimes.TryAdd(FormatTrayIntervalLabel(interval.Hours, interval.Minutes), interval.TotalSeconds);
            }

            return trayTimes;
        }

        /// <summary>
        /// Produces the text shown in the tray menu, e.g. "30 minutes", "1 hour" or "1 hour 30 minutes".
        /// </summary>
        public static string FormatTrayIntervalLabel(uint hours, uint minutes)
        {
            string hoursText = hours switch
            {
                0 => null,
                1 => Format(GetString("Awake_TrayInterval_Hour", "{0} hour"), hours),
                _ => Format(GetString("Awake_TrayInterval_Hours", "{0} hours"), hours),
            };

            string minutesText = minutes switch
            {
                0 => null,
                1 => Format(GetString("Awake_TrayInterval_Minute", "{0} minute"), minutes),
                _ => Format(GetString("Awake_TrayInterval_Minutes", "{0} minutes"), minutes),
            };

            if (hoursText != null && minutesText != null)
            {
                return Format(GetString("Awake_TrayInterval_HoursAndMinutes", "{0} {1}"), hoursText, minutesText);
            }

            return hoursText ?? minutesText ?? Format(GetString("Awake_TrayInterval_Minutes", "{0} minutes"), 0);
        }

        /// <summary>
        /// Appends a new interval. The new entry is one hour longer than the last one so it does not
        /// collide with an existing label (the tray menu keys entries by label).
        /// </summary>
        public void AddTrayInterval()
        {
            AwakeTrayInterval last = TrayIntervals.LastOrDefault();
            uint hours = last == null ? 0 : Math.Min(last.Hours + 1, AwakeTrayInterval.MaxHours);
            uint minutes = last == null ? 30 : last.Minutes;

            TrayIntervals.Add(new AwakeTrayInterval(hours, minutes));
        }

        public void RemoveTrayInterval(AwakeTrayInterval interval)
        {
            if (interval != null)
            {
                TrayIntervals.Remove(interval);
            }
        }

        /// <summary>
        /// Restores the built-in list (30 minutes, 1 hour, 2, 4, 6, 8, 10 and 12 hours).
        /// </summary>
        public void ResetTrayIntervals()
        {
            ReplaceTrayIntervals(DefaultTrayIntervals.Select(d => new AwakeTrayInterval(d.Hours, d.Minutes)));
            SaveTrayIntervals();
        }

        public void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
        {
            Logger.LogInfo($"Changed the property {propertyName}");
            OnPropertyChanged(propertyName);
        }

        public void RefreshEnabledState()
        {
            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(IsTimeConfigurationEnabled));
            OnPropertyChanged(nameof(IsScreenConfigurationPossibleEnabled));
            OnPropertyChanged(nameof(IsExpirationConfigurationEnabled));
        }

        public void RefreshModuleSettings()
        {
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(KeepDisplayOn));
            OnPropertyChanged(nameof(IntervalHours));
            OnPropertyChanged(nameof(IntervalMinutes));
            OnPropertyChanged(nameof(ExpirationDateTime));
            LoadTrayIntervals();
        }

        private static string Format(string format, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }

        private static string GetString(string resourceKey, string fallback)
        {
            try
            {
                string value = Helpers.ResourceLoaderInstance.ResourceLoader.GetString(resourceKey);
                return string.IsNullOrEmpty(value) ? fallback : value;
            }
            catch (Exception)
            {
                // Resource lookups are unavailable outside a packaged WinUI context (e.g. unit tests).
                return fallback;
            }
        }

        /// <summary>
        /// Rebuilds <see cref="TrayIntervals"/> from the module settings. An empty saved list means
        /// "use the defaults", which is what the Awake tray shows, so the defaults are shown here too.
        /// </summary>
        private void LoadTrayIntervals()
        {
            Dictionary<string, uint> saved = ModuleSettings?.Properties?.CustomTrayTimes;

            List<AwakeTrayInterval> intervals = saved == null || saved.Count == 0
                ? DefaultTrayIntervals.Select(d => new AwakeTrayInterval(d.Hours, d.Minutes)).ToList()
                : saved.Values.Select(AwakeTrayInterval.FromSeconds).ToList();

            // Avoid replacing the collection (and disturbing focus in the UI) when nothing changed,
            // which is the common case when the settings file is re-read after our own save.
            if (TrayIntervals.Select(i => (i.Hours, i.Minutes)).SequenceEqual(intervals.Select(i => (i.Hours, i.Minutes))))
            {
                return;
            }

            ReplaceTrayIntervals(intervals);
        }

        private void ReplaceTrayIntervals(IEnumerable<AwakeTrayInterval> intervals)
        {
            _isLoadingTrayIntervals = true;

            try
            {
                foreach (AwakeTrayInterval interval in TrayIntervals)
                {
                    interval.PropertyChanged -= TrayInterval_PropertyChanged;
                }

                TrayIntervals.Clear();

                foreach (AwakeTrayInterval interval in intervals)
                {
                    TrayIntervals.Add(interval);
                }
            }
            finally
            {
                _isLoadingTrayIntervals = false;
            }
        }

        private void SaveTrayIntervals()
        {
            if (ModuleSettings?.Properties == null)
            {
                return;
            }

            ModuleSettings.Properties.CustomTrayTimes = BuildTrayTimes(TrayIntervals);

            // The page listens for this and pushes the full module settings to the runner.
            NotifyPropertyChanged(nameof(TrayIntervals));
        }

        private void TrayIntervals_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (AwakeTrayInterval interval in e.OldItems)
                {
                    interval.PropertyChanged -= TrayInterval_PropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (AwakeTrayInterval interval in e.NewItems)
                {
                    interval.Label = FormatTrayIntervalLabel(interval.Hours, interval.Minutes);
                    interval.PropertyChanged += TrayInterval_PropertyChanged;
                }
            }

            if (!_isLoadingTrayIntervals)
            {
                SaveTrayIntervals();
            }
        }

        private void TrayInterval_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is not AwakeTrayInterval interval)
            {
                return;
            }

            if (e.PropertyName is nameof(AwakeTrayInterval.Hours) or nameof(AwakeTrayInterval.Minutes))
            {
                interval.Label = FormatTrayIntervalLabel(interval.Hours, interval.Minutes);

                if (!_isLoadingTrayIntervals)
                {
                    SaveTrayIntervals();
                }
            }
        }

        private bool _enabledStateIsGPOConfigured;
        private bool _enabledGPOConfiguration;
        private AwakeSettings _moduleSettings;
        private bool _isEnabled;
        private bool _isLoadingTrayIntervals;
    }
}
