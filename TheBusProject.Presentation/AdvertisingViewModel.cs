using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;

namespace TheBusProject.Presentation
{
    /// <summary>One level of a channel, shown with its price.</summary>
    public class AdLevelChoice
    {
        public AdvertisementType Type { get; set; }
        public string Text { get; set; }
        public override string ToString() { return Text; }
    }

    /// <summary>One advertising channel (newspapers, radio, TV, internet) and its level.</summary>
    public class AdChannelRow : ObservableObject
    {
        private readonly Action<AdvertisementType> _set;
        private AdLevelChoice _selected;

        public AdChannelRow(string name, List<AdLevelChoice> levels, AdLevelChoice selected, Action<AdvertisementType> set)
        {
            Name = name;
            Levels = levels;
            _selected = selected;
            _set = set;
        }

        public string Name { get; }
        public List<AdLevelChoice> Levels { get; }

        public AdLevelChoice Selected
        {
            get => _selected;
            set
            {
                if (value != null && SetProperty(ref _selected, value)) _set(value.Type);
            }
        }
    }

    /// <summary>A running campaign.</summary>
    public class AdCampaignRow
    {
        public AdCampaign Campaign { get; set; }
        public string Text { get; set; }
        public ICommand EndCommand { get; set; }
    }

    /// <summary>A country or station a campaign can aim at.</summary>
    public class AdTarget
    {
        public string Key { get; set; }
        public string Text { get; set; }
        public override string ToString() { return Text; }
    }

    /// <summary>My Carrier > Advertising: the channels, the reputation they bring and campaigns aimed at a place.</summary>
    public class AdvertisingViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _reputationText, _costText, _status, _pendingText, _search = "", _campaignText;
        private bool _stationTarget;
        private AdTarget _target;
        private int _size = 2, _months = 3;
        private Action _pending;

        public AdvertisingViewModel(Action changed)
        {
            _changed = changed;
            Channels = new ObservableCollection<AdChannelRow>();
            Campaigns = new ObservableCollection<AdCampaignRow>();
            Targets = new ObservableCollection<AdTarget>();
            StartCommand = new RelayCommand(AskStart, () => _target != null);
            ConfirmCommand = new RelayCommand(() => { var a = _pending; SetPending(null, null); a?.Invoke(); });
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            Reload();
        }

        public ObservableCollection<AdChannelRow> Channels { get; }
        public ObservableCollection<AdCampaignRow> Campaigns { get; }
        public ObservableCollection<AdTarget> Targets { get; }
        public List<string> Sizes { get; } = AdvertisingService.CampaignSizes.ToList();
        public List<int> MonthChoices { get; } = new List<int> { 1, 2, 3, 6, 12, 24 };
        public ICommand StartCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public string Explanation =>
            "Each channel costs money every month. Together they set the reputation your carrier moves toward " +
            "(50 with no advertising, up to 98 with every channel at Global); it moves a quarter of the way each month " +
            "and falls back when you stop. Reputation changes how often passengers pick you (85% at 0, 100% at 50, 115% at 100). " +
            "Advertising also adds a little demand on your routes every month.";

        public string ReputationText { get => _reputationText; private set => SetProperty(ref _reputationText, value); }
        public string CostText { get => _costText; private set => SetProperty(ref _costText, value); }
        public string Status { get => _status; private set => SetProperty(ref _status, value); }
        public string PendingText { get => _pendingText; private set => SetProperty(ref _pendingText, value); }
        public bool HasPending => _pending != null;
        public bool HasCampaigns => Campaigns.Count > 0;

        public bool StationTarget
        {
            get => _stationTarget;
            set
            {
                if (SetProperty(ref _stationTarget, value))
                {
                    OnPropertyChanged(nameof(CountryTarget));
                    LoadTargets();
                }
            }
        }

        public bool CountryTarget
        {
            get => !_stationTarget;
            set => StationTarget = !value;
        }

        public string Search
        {
            get => _search;
            set { if (SetProperty(ref _search, value ?? "")) LoadTargets(); }
        }

        public AdTarget Target
        {
            get => _target;
            set
            {
                if (SetProperty(ref _target, value))
                {
                    (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    UpdateCampaignText();
                }
            }
        }

        public string Size
        {
            get => AdvertisingService.CampaignSizes[_size - 1];
            set
            {
                int i = Array.IndexOf(AdvertisingService.CampaignSizes, value) + 1;
                if (i > 0 && i != _size)
                {
                    _size = i;
                    OnPropertyChanged();
                    UpdateCampaignText();
                }
            }
        }

        public int Months
        {
            get => _months;
            set { if (SetProperty(ref _months, value)) UpdateCampaignText(); }
        }

        public string CampaignText { get => _campaignText; private set => SetProperty(ref _campaignText, value); }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        private void SetPending(string text, Action action)
        {
            _pending = action;
            PendingText = text;
            OnPropertyChanged(nameof(HasPending));
        }

        public void Reload()
        {
            var carrier = GameObject.GetInstance()?.HumanCarrier;
            if (carrier == null) return;

            Channels.Clear();
            foreach (var channel in AdvertisingService.Channels())
            {
                var levels = AdvertisingService.Levels(channel)
                                               .Select(l => new AdLevelChoice
                                               {
                                                   Type = l,
                                                   Text = l.ReputationLevel == 0 ? "None" : l.Name + "  (" + Money(l.Price) + " a month)"
                                               })
                                               .ToList();
                var current = AdvertisingService.Current(carrier, channel);
                var selected = levels.FirstOrDefault(l => l.Type.ReputationLevel == current.ReputationLevel) ?? levels.FirstOrDefault();
                Channels.Add(new AdChannelRow(AdvertisingService.ChannelName(channel), levels, selected, t =>
                    {
                        AdvertisingService.Set(carrier, t);
                        UpdateTotals();
                        Status = AdvertisingService.ChannelName(t.Type) + ": " + (t.ReputationLevel == 0 ? "no advertising." : t.Name.ToLower() + " advertising.");
                    }));
            }

            Campaigns.Clear();
            var now = GameObject.GetInstance().GameTime;
            foreach (var c in AdvertisingService.Campaigns(carrier).Where(c => c.End > now).ToList())
            {
                var campaign = c;
                Campaigns.Add(new AdCampaignRow
                {
                    Campaign = c,
                    Text = string.Format(
                        "{0} campaign in {1}  |  +{2:0}% passenger share  |  {3} a month  |  until {4}",
                        AdvertisingService.CampaignSizes[Math.Max(1, Math.Min(3, c.Level)) - 1], AdvertisingService.TargetName(c),
                        AdvertisingService.CampaignBoost(c.Level)*100, Money(c.MonthlyCost), c.End.ToShortDateString()),
                    EndCommand = new RelayCommand(() => SetPending(
                        "Stop the campaign in " + AdvertisingService.TargetName(campaign) + "? Months already paid are not refunded.",
                        () =>
                            {
                                AdvertisingService.EndCampaign(carrier, campaign);
                                Status = "The campaign in " + AdvertisingService.TargetName(campaign) + " was stopped.";
                                Reload();
                                _changed?.Invoke();
                            }))
                });
            }
            OnPropertyChanged(nameof(HasCampaigns));
            UpdateTotals();
            if (Targets.Count == 0) LoadTargets();
        }

        private void UpdateTotals()
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            int target = AdvertisingService.TargetReputation(carrier);
            string trend = carrier.Reputation == target ? "steady" : carrier.Reputation < target ? "rising toward " + target : "falling toward " + target;
            ReputationText = string.Format(
                "Reputation {0} / 100 ({1}), {2}. Passengers pick you {3:0}% as often as a carrier with reputation 50.",
                carrier.Reputation, System.Text.RegularExpressions.Regex.Replace(carrier.GetReputation().ToString(), "(?<=[a-z])(?=[A-Z])", " ").ToLower(), trend, AdvertisingService.ReputationFactor(carrier)*100);
            double channels = AdvertisingService.ChannelsMonthlyCost(carrier), campaigns = AdvertisingService.CampaignsMonthlyCost(carrier);
            CostText = string.Format("Advertising costs {0} a month (channels {1}, campaigns {2}).", Money(channels + campaigns), Money(channels), Money(campaigns));
        }

        private void LoadTargets()
        {
            var carrier = GameObject.GetInstance()?.HumanCarrier;
            if (carrier == null) return;
            string s = _search.Trim();
            Targets.Clear();
            if (_stationTarget)
            {
                IEnumerable<Station> list = s.Length == 0
                    ? carrier.Stations.OrderBy(a => a.Profile.IATACode)
                    : Stations.GetAllStations().Where(a => a.Profile.IATACode.StartsWith(s, StringComparison.OrdinalIgnoreCase)
                                                            || a.Profile.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                              .OrderBy(a => string.Equals(a.Profile.IATACode, s, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(a => a.Profile.Name);
                foreach (var a in list.Take(60))
                    Targets.Add(new AdTarget { Key = a.Profile.IATACode, Text = a.Profile.IATACode + "  " + a.Profile.Name + ", " + a.Profile.Country?.Name });
            }
            else
            {
                var mine = new HashSet<Country>(carrier.Stations.Select(a => a.Profile.Country).Where(c => c != null));
                IEnumerable<Country> list = s.Length == 0
                    ? mine.OrderBy(c => c.Name)
                    : Countries.GetCountries().Where(c => c.Name != null && c.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(c => c.Name);
                foreach (var c in list.Take(60))
                    Targets.Add(new AdTarget { Key = c.Uid, Text = c.Name + (mine.Contains(c) ? "  (you run here)" : "") });
            }
            Target = Targets.FirstOrDefault();
        }

        private void UpdateCampaignText()
        {
            if (_target == null)
            {
                CampaignText = _stationTarget ? "Search for a station." : "Search for a country.";
                return;
            }
            double monthly = AdvertisingService.CampaignMonthlyCost(_stationTarget, _target.Key, _size);
            CampaignText = string.Format(
                "{0} campaign in {1} for {2} month(s): {3} a month ({4} in all). You get {5:0}% more of the passengers at {6} while it runs.",
                Size, _target.Text, _months, Money(monthly), Money(monthly*_months), AdvertisingService.CampaignBoost(_size)*100,
                _stationTarget ? "that station" : "its stations");
        }

        private void AskStart()
        {
            var target = _target;
            if (target == null) return;
            bool station = _stationTarget;
            int size = _size, months = _months;
            SetPending(CampaignText + " Start it?", () =>
                {
                    string m;
                    bool ok = AdvertisingService.StartCampaign(GameObject.GetInstance().HumanCarrier, station, target.Key, size, months, out m);
                    Reload();
                    Status = m;
                    if (ok) _changed?.Invoke();
                });
        }
    }
}
