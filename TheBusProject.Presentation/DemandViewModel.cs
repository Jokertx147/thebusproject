using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;

namespace TheBusProject.Presentation
{
    /// <summary>One destination on the demand page: how many passengers want to go there.</summary>
    public class DemandRow : ObservableObject
    {
        private int _passengers;

        public DemandRow(Station origin, Station destination, int passengers, bool domestic)
        {
            Origin = origin;
            Destination = destination;
            _passengers = passengers;
            OriginalPassengers = passengers;
            IsDomestic = domestic;
            Distance = MathHelpers.GetDistance(origin, destination);

            var carrier = GameObject.GetInstance().HumanCarrier;
            var p = destination.Profile;
            Name = p.IATACode + "  " + p.Name + ", " + p.Country?.Name;
            Kind = DemandViewModel.TypeName(p.Type) + ", " + DemandViewModel.SizeName(p.Size);
            HasContract = destination.CarrierContracts.Exists(c => c.Carrier == carrier);
            FreeGates = destination.Terminals.GetFreeGates(Terminal.TerminalType.Passenger);
            HasRoute = carrier.Routes.Any(
                r => (r.Destination1 == origin && r.Destination2 == destination) || (r.Destination1 == destination && r.Destination2 == origin));
        }

        public Station Origin { get; }
        public Station Destination { get; }
        public bool IsDomestic { get; }
        public double Distance { get; }
        public string Name { get; }
        public string Kind { get; }
        public bool HasContract { get; }
        public bool HasRoute { get; }
        public int FreeGates { get; }
        public int OriginalPassengers { get; }

        public bool IsChanged => _passengers != OriginalPassengers;

        public int Passengers
        {
            get { return _passengers; }
            set
            {
                value = Math.Max(0, Math.Min(ushort.MaxValue, value));
                if (SetProperty(ref _passengers, value))
                {
                    Origin.SetDestinationPassengersDemand(Destination, value);
                    Changed();
                }
            }
        }

        public string Numbers
        {
            get
            {
                return string.Format(
                    "{0} passengers  |  {1}{2}",
                    _passengers.ToString("N0"), DistanceFormatter.Km(Distance),
                    IsChanged ? "  |  edited" : "");
            }
        }

        public string Notes
        {
            get
            {
                var parts = new List<string> { IsDomestic ? "Domestic" : "International", Kind };
                if (HasRoute) parts.Add("you run this route");
                parts.Add(HasContract ? "you have gates there" : FreeGates > 0 ? FreeGates + " free gate(s)" : "no free gates");
                return string.Join("  |  ", parts);
            }
        }

        private void Changed()
        {
            OnPropertyChanged(nameof(Numbers));
            OnPropertyChanged(nameof(IsChanged));
        }
    }

    /// <summary>The station demand page: where people from one station want to go.</summary>
    public class DemandViewModel : ObservableObject
    {
        private readonly Action _changed;
        private readonly List<DemandRow> _rows = new List<DemandRow>();
        private List<Station> _all;
        private string _search = "";
        private StationItem _selected;
        private string _filter = "All";
        private string _sortBy = "Passengers";
        private string _destFilter = "";
        private DemandRow _selectedRow;
        private CreateRouteViewModel _create;
        private string _status;
        private string _summary;
        private int _scale = 120;
        private bool _onlyWithoutRoute;

        public DemandViewModel(Action changed)
        {
            _changed = changed;
            Stations = new ObservableCollection<StationItem>();
            Rows = new ObservableCollection<DemandRow>();
            Filters = new List<string> { "All", "Domestic", "International" };
            SortOptions = new List<string> { "Passengers", "Distance", "Name" };
            ScaleSelectedCommand = new RelayCommand(() => Scale(_selectedRow == null ? new List<DemandRow>() : new List<DemandRow> { _selectedRow }));
            ScaleShownCommand = new RelayCommand(() => Scale(Rows.ToList()));
            UndoCommand = new RelayCommand(Undo);
            CreateCommand = new RelayCommand(OpenCreate, () => _selectedRow != null);
            ApplySearch();
            Selected = Stations.FirstOrDefault();
        }

        public ObservableCollection<StationItem> Stations { get; }
        public ObservableCollection<DemandRow> Rows { get; }
        public List<string> Filters { get; }
        public List<string> SortOptions { get; }
        public List<int> ScaleChoices { get; } = new List<int> { 25, 50, 75, 90, 110, 120, 150, 200, 300 };

        public ICommand ScaleSelectedCommand { get; }
        public ICommand ScaleShownCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand CreateCommand { get; }

        public static string SizeName(GeneralHelpers.Size size)
        {
            return System.Text.RegularExpressions.Regex.Replace(size.ToString().Replace('_', ' '), "(?<=[a-z])(?=[A-Z])", " ").ToLower();
        }

        public static string TypeName(StationProfile.StationType type)
        {
            switch (type)
            {
                case StationProfile.StationType.LongHaulInternational: return "Long haul international";
                case StationProfile.StationType.ShortHaulInternational: return "Short haul international";
                case StationProfile.StationType.Domestic: return "Domestic";
                default: return "Regional";
            }
        }

        /// <summary>Filters the station list by code, name, town or country; empty shows your own stations.</summary>
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
                    Load();
                }
            }
        }

        /// <summary>Shows the demand of this station (used when another page picks an station).</summary>
        public void ShowStation(Station station)
        {
            if (station == null) return;
            var item = Stations.FirstOrDefault(a => a.Station == station);
            if (item == null)
            {
                var own = new HashSet<Station>(GameObject.GetInstance().HumanCarrier.Stations);
                item = new StationItem(station, own.Contains(station));
                Stations.Insert(0, item);
            }
            Selected = item;
        }

        public string Filter
        {
            get { return _filter; }
            set
            {
                if (SetProperty(ref _filter, value ?? "All"))
                {
                    ApplyRows();
                }
            }
        }

        public string SortBy
        {
            get { return _sortBy; }
            set
            {
                if (SetProperty(ref _sortBy, value ?? "Passengers"))
                {
                    ApplyRows();
                }
            }
        }

        /// <summary>Narrows the destinations by code, name or country.</summary>
        public string DestinationFilter
        {
            get { return _destFilter; }
            set
            {
                if (SetProperty(ref _destFilter, value ?? ""))
                {
                    ApplyRows();
                }
            }
        }

        public bool OnlyWithoutRoute
        {
            get { return _onlyWithoutRoute; }
            set
            {
                if (SetProperty(ref _onlyWithoutRoute, value))
                {
                    ApplyRows();
                }
            }
        }

        public DemandRow SelectedRow
        {
            get { return _selectedRow; }
            set
            {
                if (SetProperty(ref _selectedRow, value))
                {
                    (CreateCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public int ScalePercent
        {
            get { return _scale; }
            set { SetProperty(ref _scale, Math.Max(1, Math.Min(1000, value))); }
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

        public bool HasCreate => _create != null;

        public string Summary
        {
            get { return _summary; }
            private set { SetProperty(ref _summary, value); }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        public int RowCount => Rows.Count;

        /// <summary>Recalculates the page after the game changed (new routes, gates).</summary>
        public void Reload()
        {
            var keep = _selected?.Station;
            ApplySearch();
            if (keep != null)
            {
                ShowStation(keep);
                Load();
            }
        }

        private void ApplySearch()
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            if (carrier == null) return;
            var own = new HashSet<Station>(carrier.Stations);
            _all = _all ?? Models.Stations.Stations.GetAllStations().OrderBy(a => a.Profile.Name).ToList();

            IEnumerable<Station> list;
            string s = _search.Trim();
            if (s.Length == 0)
            {
                list = carrier.Stations.OrderBy(a => a.Profile.Name);
            }
            else
            {
                list = _all.Where(
                    a => a.Profile.IATACode.StartsWith(s, StringComparison.OrdinalIgnoreCase)
                         || a.Profile.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                         || (a.Profile.Town != null && a.Profile.Town.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                         || (a.Profile.Country != null && a.Profile.Country.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                     .OrderBy(a => string.Equals(a.Profile.IATACode, s, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                     .ThenBy(a => a.Profile.Name);
            }

            Stations.Clear();
            foreach (var a in list.Take(150))
            {
                Stations.Add(new StationItem(a, own.Contains(a)));
            }
        }

        private void Load()
        {
            _rows.Clear();
            if (_selected == null)
            {
                Summary = "";
                ApplyRows();
                return;
            }

            var origin = _selected.Station;
            var resolver = new CountryCurrentCountryResolver();
            var home = resolver.Convert(origin.Profile.Country);

            foreach (var d in origin.GetDestinationDemands())
            {
                if (d == null || d == origin || !GeneralHelpers.IsStationActive(d)) continue;
                int pax = origin.GetDestinationPassengersRate(d, CoachClass.ClassType.EconomyClass);
                if (pax == 0) continue;
                bool domestic = Equals(resolver.Convert(d.Profile.Country), home);
                _rows.Add(new DemandRow(origin, d, pax, domestic));
            }

            UpdateSummary();
            ApplyRows();
        }

        private void UpdateSummary()
        {
            if (_selected == null)
            {
                Summary = "";
                return;
            }
            var p = _selected.Station.Profile;
            var dom = _rows.Where(r => r.IsDomestic).ToList();
            var intl = _rows.Where(r => !r.IsDomestic).ToList();
            Summary = string.Format(
                "{0} {1}  |  {2}, {3}{4}" + Environment.NewLine +
                "Domestic: {5} destinations, {6} passengers" + Environment.NewLine +
                "International: {7} destinations, {8} passengers",
                p.IATACode, p.Name, TypeName(p.Type), SizeName(p.Size),
                _rows.Count == 0 ? "" : "",
                dom.Count, dom.Sum(r => r.Passengers).ToString("N0"),
                intl.Count, intl.Sum(r => r.Passengers).ToString("N0"));
        }

        private void ApplyRows()
        {
            IEnumerable<DemandRow> list = _rows;
            if (_filter == "Domestic") list = list.Where(r => r.IsDomestic);
            else if (_filter == "International") list = list.Where(r => !r.IsDomestic);
            if (_onlyWithoutRoute) list = list.Where(r => !r.HasRoute);

            string s = _destFilter.Trim();
            if (s.Length > 0)
            {
                list = list.Where(
                    r => r.Destination.Profile.IATACode.StartsWith(s, StringComparison.OrdinalIgnoreCase)
                         || r.Destination.Profile.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                         || (r.Destination.Profile.Country != null && r.Destination.Profile.Country.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            switch (_sortBy)
            {
                case "Distance": list = list.OrderBy(r => r.Distance); break;
                case "Name": list = list.OrderBy(r => r.Destination.Profile.Name); break;
                default: list = list.OrderByDescending(r => r.Passengers).ThenBy(r => r.Distance); break;
            }

            var keep = _selectedRow;
            Rows.Clear();
            foreach (var r in list)
            {
                Rows.Add(r);
            }
            SelectedRow = keep != null && Rows.Contains(keep) ? keep : null;
            OnPropertyChanged(nameof(RowCount));
        }

        private void Scale(List<DemandRow> rows)
        {
            if (rows.Count == 0)
            {
                Status = "Pick a destination first.";
                return;
            }
            double factor = _scale / 100.0;
            foreach (var r in rows)
            {
                r.Passengers = (int) Math.Round(r.Passengers * factor);
            }
            UpdateSummary();
            Status = string.Format("Changed the demand to {0} destination(s) to {1}%. It is kept in your save.", rows.Count, _scale);
        }

        private void Undo()
        {
            int changed = 0;
            foreach (var r in _rows.Where(r => r.IsChanged))
            {
                r.Passengers = r.OriginalPassengers;
                changed++;
            }
            UpdateSummary();
            Status = changed == 0 ? "Nothing to undo." : string.Format("Restored {0} destination(s).", changed);
        }

        private void OpenCreate()
        {
            if (_selected == null || _selectedRow == null) return;
            Create = new CreateRouteViewModel(_selected.Station, _selectedRow.Destination, OnCreateDone);
        }

        private void OnCreateDone(string message)
        {
            Create = null;
            if (message != null)
            {
                Status = message;
                _changed?.Invoke();
            }
        }
    }
}
