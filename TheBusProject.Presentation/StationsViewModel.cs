using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using TheBusProject.Helpers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General.Countries.Towns;
using TheBusProject.Models.General;
using TheBusProject.Models.Routes;

namespace TheBusProject.Presentation
{
    public class StationItem
    {
        public StationItem(Station station, bool own, Station near = null)
        {
            Station = station;
            Text = station.Profile.IATACode + "  " + station.Profile.Name + ", " + station.Profile.Country?.Name;
            IsOwn = own;
            if (near != null && near != station && station.Profile.Coordinates != null && near.Profile.Coordinates != null)
            {
                Distance = MathHelpers.GetDistance(near, station);
                DistanceText = DistanceFormatter.Km(Distance.Value);
                Details = DistanceText + " from " + near.Profile.IATACode;
            }
            else if (near == station) Details = "the station you are measuring from";
        }

        public Station Station { get; }
        public string Text { get; }
        public bool IsOwn { get; }

        /// <summary>How far from the station the list is sorted around, when there is one.</summary>
        public double? Distance { get; }

        /// <summary>Just the distance, for a narrow column.</summary>
        public string DistanceText { get; } = "";

        public string Details { get; }
        public bool HasDetails => !string.IsNullOrEmpty(Details);
    }

    public class AdviceItem
    {
        public AdviceItem(RouteAdvice a)
        {
            Advice = a;
            Destination = a.Destination;
            var f = new ValueCurrencyFormatter();
            Name = a.Destination.Profile.IATACode + "  " + a.Destination.Profile.Name + ", " + a.Destination.Profile.Country?.Name;
            Numbers = string.Format(
                "{0}  |  {1} pax/day  |  {2} competitor(s)  |  ~{3}/day{4}",
                DistanceFormatter.Km(a.Distance), a.DailyDemand, a.Competitors, f.Convert(a.RevenuePerDay),
                a.FleetCanFly ? "" : "  |  no coach of yours can run it");
            Notes = a.Advice;
        }

        public RouteAdvice Advice { get; }
        public Station Destination { get; }
        public string Name { get; }
        public string Numbers { get; }
        public string Notes { get; }
    }

    /// <summary>Stations tab: pick an station, see the advisor's suggested routes, create one.</summary>
    /// <summary>An station found when searching for any destination, not only the recommended ones.</summary>
    public class DestinationItem
    {
        public DestinationItem(Station origin, Station station)
        {
            Station = station;
            double km = MathHelpers.GetDistance(origin, station);
            Distance = km;
            Name = station.Profile.IATACode + "  " + station.Profile.Name + ", " + station.Profile.Country?.Name;
            Details = DistanceFormatter.Km(km);
        }

        public Station Station { get; }
        public double Distance { get; }
        public string Name { get; }
        public string Details { get; }
    }

    public class StationsViewModel : ObservableObject
    {
        private string _destSearch = "";
        private DestinationItem _selectedDestination;
        private readonly Action _changed;
        private string _search = "";
        private StationItem _selected;
        private AdviceItem _selectedAdvice;
        private CreateRouteViewModel _create;
        private string _status;
        private bool _busy;
        private List<Station> _all;
        private string _near = "";
        private Station _nearStation;

        public StationsViewModel(Action changed)
        {
            _changed = changed;
            Stations = new ObservableCollection<StationItem>();
            Advice = new ObservableCollection<AdviceItem>();
            Destinations = new ObservableCollection<DestinationItem>();
            ApplySearch();
            Selected = Stations.FirstOrDefault();
        }

        public ObservableCollection<StationItem> Stations { get; }
        public ObservableCollection<AdviceItem> Advice { get; }
        public ObservableCollection<DestinationItem> Destinations { get; }

        /// <summary>
        ///     Sorts the stations by how far they are from this one, nearest first (a code, a name, a town; empty sorts
        ///     by name again). Useful for finding what is near a hub, or which of your stations is closest to somewhere.
        /// </summary>
        public string Near
        {
            get { return _near; }
            set
            {
                if (SetProperty(ref _near, value ?? ""))
                {
                    _nearStation = Resolve(_near);
                    OnPropertyChanged(nameof(NearStatus));
                    OnPropertyChanged(nameof(HasNear));
                    ApplySearch();
                }
            }
        }

        /// <summary>The station the list is sorted around, or null.</summary>
        public Station NearStation => _nearStation;

        public bool HasNear => !string.IsNullOrEmpty(_near.Trim());

        public string NearStatus
        {
            get
            {
                if (!HasNear) return "";
                if (_nearStation == null) return "No station found for \"" + _near.Trim() + "\".";
                return "Nearest first from " + _nearStation.Profile.IATACode + "  " + _nearStation.Profile.Name + ".";
            }
        }

        /// <summary>The station a typed code or name means: an exact code wins, else the first match by name.</summary>
        public Station Resolve(string text)
        {
            _all = _all ?? Models.Stations.Stations.GetAllStations().OrderBy(a => a.Profile.Name).ToList();
            return StationSearch.Resolve(text, _all);
        }

        /// <summary>Search any station as the destination of a new route (code, name or country), recommended or not.</summary>
        public string DestinationSearch
        {
            get { return _destSearch; }
            set
            {
                if (SetProperty(ref _destSearch, value ?? ""))
                {
                    OnPropertyChanged(nameof(IsSearchingDestinations));
                    OnPropertyChanged(nameof(ShowAdvice));
                    ApplyDestinationSearch();
                }
            }
        }

        public bool IsSearchingDestinations => _destSearch.Trim().Length > 0;
        public bool ShowAdvice => !IsSearchingDestinations;

        public DestinationItem SelectedDestination
        {
            get { return _selectedDestination; }
            set
            {
                if (SetProperty(ref _selectedDestination, value) && value != null && _selected != null && value.Station != _selected.Station)
                {
                    Create = new CreateRouteViewModel(_selected.Station, value.Station, OnCreateDone, false);
                }
            }
        }

        private void ApplyDestinationSearch()
        {
            Destinations.Clear();
            _selectedDestination = null;
            OnPropertyChanged(nameof(SelectedDestination));
            if (_selected == null || !IsSearchingDestinations)
            {
                return;
            }

            _all = _all ?? Models.Stations.Stations.GetAllStations().OrderBy(a => a.Profile.Name).ToList();
            string s = _destSearch.Trim();
            var origin = _selected.Station;
            var found = _all.Where(a => a != origin && a.Profile.Coordinates != null && Matches(a, s))
                            .Select(a => new DestinationItem(origin, a))
                            .OrderBy(d => string.Equals(d.Station.Profile.IATACode, s, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                            .ThenBy(d => d.Distance)
                            .Take(80);
            foreach (var d in found)
            {
                Destinations.Add(d);
            }
        }


        /// <summary>Whether the search text matches the station (code, name, town, state or country).</summary>
        public static bool Matches(Station a, string s) => StationSearch.Matches(a, s);

        /// <summary>Filters by IATA code, name, town, state or country; empty shows the carrier's own stations.</summary>
        public string Search
        {
            get { return _search; }
            set
            {
                if (SetProperty(ref _search, value ?? ""))
                {
                    ApplySearch();
                }
            }
        }

        public StationItem Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    Create = null;
                    ApplyDestinationSearch();
                    LoadAdvice();
                }
            }
        }

        public string AdviceTitle
        {
            get { return "Suggested routes"; }
        }

        public AdviceItem SelectedAdvice
        {
            get { return _selectedAdvice; }
            set
            {
                if (SetProperty(ref _selectedAdvice, value) && value != null && _selected != null)
                {
                    Create = new CreateRouteViewModel(_selected.Station, value.Destination, OnCreateDone, false);
                }
            }
        }

        public CreateRouteViewModel Create
        {
            get { return _create; }
            private set
            {
                if (SetProperty(ref _create, value))
                {
                    OnPropertyChanged(nameof(HasCreate));
                }
            }
        }

        public bool HasCreate
        {
            get { return _create != null; }
        }

        public bool IsBusy
        {
            get { return _busy; }
            private set { SetProperty(ref _busy, value); }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        /// <summary>Opens the new route form for two stations (used by the connections page).</summary>
        public void PlanRoute(Station from, Station to)
        {
            var item = Stations.FirstOrDefault(a => a.Station == from);
            if (item != null) Selected = item;
            Create = new CreateRouteViewModel(from, to, OnCreateDone, false);
        }

        private void OnCreateDone(string message)
        {
            Create = null;
            if (message != null)
            {
                Status = message;
                _changed();
            }
            _selectedAdvice = null;
            OnPropertyChanged(nameof(SelectedAdvice));
            _selectedDestination = null;
            OnPropertyChanged(nameof(SelectedDestination));
        }

        private void ApplySearch()
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            var own = new HashSet<Station>(carrier.Stations);
            _all = _all ?? Models.Stations.Stations.GetAllStations().OrderBy(a => a.Profile.Name).ToList();

            IEnumerable<Station> list;
            if (_search.Trim().Length == 0)
            {
                list = carrier.Stations.OrderBy(a => a.Profile.Name);
            }
            else
            {
                string s = _search.Trim();
                list = _all.Where(a => Matches(a, s));
            }

            // sorted around an station when one is given, so the nearest 150 are the ones kept
            if (_nearStation != null)
                list = list.Where(a => a.Profile.Coordinates != null)
                           .OrderBy(a => a == _nearStation ? 0 : 1)
                           .ThenBy(a => MathHelpers.GetDistance(_nearStation, a));

            Stations.Clear();
            foreach (var a in list.Take(150))
            {
                Stations.Add(new StationItem(a, own.Contains(a), _nearStation));
            }
        }

        /// <summary>Recalculates the suggestions (after the fleet or routes changed).</summary>
        public void ReloadAdvice()
        {
            // the carrier's kind decides the kind of route (it can change on the profile page while there are no routes)
            var me = GameObject.GetInstance()?.HumanCarrier;
            LoadAdvice(false);
        }

        private int _loadId;

        private async void LoadAdvice(bool announce = true)
        {
            Advice.Clear();
            if (_selected == null)
            {
                return;
            }

            var station = _selected.Station;
            int id = ++_loadId;
            IsBusy = true;
            if (announce)
            {
                Status = "Looking for routes from " + station.Profile.IATACode + "...";
            }

            List<AdviceItem> advice;
            try
            {
                advice = await Task.Run(
                    () => RouteAdvisor.GetAdvice(station).Select(a => new AdviceItem(a)).ToList());
            }
            catch (Exception e)
            {
                IsBusy = false;
                Status = "Could not work out route advice: " + e.Message;
                return;
            }

            if (id != _loadId)
            {
                return; // a newer request replaced this one
            }

            IsBusy = false;
            if (_selected == null || _selected.Station != station)
            {
                return; // the player picked another station meanwhile
            }

            foreach (var a in advice)
            {
                Advice.Add(a);
            }

            if (announce || advice.Count == 0)
            {
                Status = advice.Count == 0
                    ? "No routes to suggest from " + station.Profile.IATACode + "."
                    : null;
            }
        }
    }
}
