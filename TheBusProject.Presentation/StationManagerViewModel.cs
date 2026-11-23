using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Environment;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.Routes;

namespace TheBusProject.Presentation
{
    /// <summary>One row of the station list, with the columns of the Windows station list and a quick lease button.</summary>
    public class StationRow : StationItem
    {
        public StationRow(Station station, bool own, Station near = null) : base(station, own, near)
        {
            var p = station.Profile;
            Name = p.Name;
            Codes = p.IATACode + "/" + p.ICAOCode;
            Town = p.Town?.Name;
            Country = p.Country?.Name;
            Type = System.Text.RegularExpressions.Regex.Replace(p.Type.ToString().Replace('_', ' '), "(?<=[a-z])(?=[A-Z])", " ");
            Size = DemandViewModel.SizeName(p.Size);
            int total = station.Terminals.GetNumberOfGates(Terminal.TerminalType.Passenger);
            FreeGates = station.Terminals.GetFreeGates(Terminal.TerminalType.Passenger);
            Gates = FreeGates + "/" + total;
            Bay = station.GetMaxBayLength();
            Carriers = station.CarrierContracts.Select(c => c.Carrier).Distinct().Count();
            Routes = StationHelpers.GetStationRoutes(station).Count;
            CanLease = !own && FreeGates > 0;
        }

        public string Name { get; }
        public string Codes { get; }
        public string Town { get; }
        public string Country { get; }
        public string Type { get; }
        public string Size { get; }
        public int FreeGates { get; }
        public string Gates { get; }
        public long Bay { get; }
        public int Carriers { get; }
        public int Routes { get; }
        public bool CanLease { get; }
        public ICommand LeaseCommand { get; set; }
    }

    public class ContractItem
    {
        public StationContract Contract { get; set; }
        public string Text { get; set; }
    }

    /// <summary>A computer carrier that has gates it doesn't need at the station.</summary>
    public class GateSaleOffer
    {
        public StationContract Contract { get; set; }
        public string Text { get; set; }
        public string BuyOneText { get; set; }
        public string BuyAllText { get; set; }
        public bool CanBuyAll { get; set; }
        public ICommand BuyOneCommand { get; set; }
        public ICommand BuyAllCommand { get; set; }
    }

    /// <summary>One bay of the station.</summary>
    public class BayItem
    {
        public Bay Bay { get; set; }
        public string Text { get; set; }
    }

    public class TerminalItem
    {
        public Terminal Terminal { get; set; }
        public string Text { get; set; }
        public bool CanBuy { get; set; }
        public bool CanSell { get; set; }
        public string ActionText { get; set; }
        public ICommand BuyCommand { get; set; }
        public ICommand SellCommand { get; set; }
    }

    public class FacilityItem
    {
        public StationFacility.FacilityType Type { get; set; }
        public string Name { get; set; }
        public string Current { get; set; }
        public string Next { get; set; }
        public string Building { get; set; }
        public bool CanBuy { get; set; }
        public ICommand BuyCommand { get; set; }
    }

    /// <summary>Stations tab: your stations (or any station you search for), gate contracts and facilities.</summary>
    /// <summary>A hub of some carrier at the selected station.</summary>
    public class HubItem
    {
        public string Carrier { get; set; }
        public string Type { get; set; }
        public string Effect { get; set; }
        public bool IsMine { get; set; }
        public ICommand RemoveCommand { get; set; }
    }

    /// <summary>A kind of hub the carrier could create at the selected station.</summary>
    public class HubOffer
    {
        public HubType Type { get; set; }
        public string Name { get; set; }
        public string Price { get; set; }
        public string Effect { get; set; }
        public string Reason { get; set; }
        public bool CanBuild { get; set; }
        public ICommand BuyCommand { get; set; }
    }

    public class StationManagerViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _search = "";
        private bool _onlyMine;
        private string _sortBy = "Name";
        private StationItem _selected;
        private List<Station> _all;
        private string _status;
        private ContractChoice _contractType;
        private int _gates = 1;
        private bool _confirmEnd;
        private ContractItem _selectedContract;
        private string _terminalName = "Terminal";
        private int _terminalGates = 10;
        private bool _terminalCargo;
        private bool _leaseCargo;
        private Action _pending;
        private string _pendingText;

        public StationManagerViewModel(Action changed)
        {
            _changed = changed;
            Stations = new ObservableCollection<StationItem>();
            Contracts = new ObservableCollection<ContractItem>();
            Facilities = new ObservableCollection<FacilityItem>();
            GateChoices = new ObservableCollection<int>();
            ContractTypes = Enum.GetValues(typeof(StationContract.ContractType))
                                .Cast<StationContract.ContractType>()
                                .Select(t => new ContractChoice { Type = t, Text = t.ToString() })
                                .ToList();
            _contractType = ContractTypes.First();

            LeaseCommand = new RelayCommand(Lease, () => _selected != null);
            EndContractCommand = new RelayCommand(() => ConfirmEnd = true, () => _selectedContract != null);
            ConfirmEndCommand = new RelayCommand(EndContract);
            Terminals = new ObservableCollection<TerminalItem>();
            Hubs = new ObservableCollection<HubItem>();
            HubOffers = new ObservableCollection<HubOffer>();
            TerminalGateChoices = new List<int> { 5, 10, 15, 20, 30, 40, 50 };
            BuildTerminalCommand = new RelayCommand(AskBuildTerminal, () => _selected != null);
            ConfirmPendingCommand = new RelayCommand(
                () =>
                    {
                        var action = _pending;
                        PendingText = null;
                        action?.Invoke();
                    });
            CancelPendingCommand = new RelayCommand(() => PendingText = null);
            CancelEndCommand = new RelayCommand(() => ConfirmEnd = false);

            ApplySearch();
            Selected = Stations.FirstOrDefault(a => a.IsOwn) ?? Stations.FirstOrDefault();
        }

        public ObservableCollection<StationItem> Stations { get; }
        public ObservableCollection<ContractItem> Contracts { get; }
        public ObservableCollection<HubItem> Hubs { get; }
        public ObservableCollection<HubOffer> HubOffers { get; }
        public string HubText { get; private set; }
        public bool HasHubs => Hubs.Count > 0;
        public bool HasHubOffers => HubOffers.Count > 0;
        public ObservableCollection<GateSaleOffer> GateOffers { get; } = new ObservableCollection<GateSaleOffer>();
        public bool HasGateOffers => GateOffers.Count > 0;
        public string GateOffersText { get; private set; }
        public ObservableCollection<FacilityItem> Facilities { get; }
        public ObservableCollection<int> GateChoices { get; }
        public ObservableCollection<TerminalItem> Terminals { get; }
        public List<int> TerminalGateChoices { get; }

        private int _leaseYears = 2;

        /// <summary>The length of a new gate contract in years (longer is cheaper per year).</summary>
        public int LeaseYears
        {
            get { return _leaseYears; }
            set { if (SetProperty(ref _leaseYears, value)) OnPropertyChanged(nameof(PriceText)); }
        }

        public List<int> LeaseYearChoices { get; } = GateContractService.Lengths.ToList();

        // ---- bays ----

        public ObservableCollection<BayItem> Bays { get; } = new ObservableCollection<BayItem>();
        public List<Bay.SurfaceType> BaySurfaces { get; } = BayService.Surfaces.ToList();
        private BayItem _selectedBay;
        private string _extendLength = "", _newBayLength = "3000", _newBayName = "";
        private Bay.SurfaceType _newBaySurface = Bay.SurfaceType.Concrete;
        private ICommand _extendBayCommand, _buildBayCommand;

        public BayItem SelectedBay
        {
            get { return _selectedBay; }
            set
            {
                if (SetProperty(ref _selectedBay, value))
                {
                    if (value != null && !value.Bay.IsExtending) ExtendLength = (value.Bay.Length + 500).ToString();
                    OnPropertyChanged(nameof(ExtendText));
                    OnPropertyChanged(nameof(HasBaySelection));
                }
            }
        }

        public bool HasBaySelection => _selectedBay != null;

        /// <summary>The new total length for the selected bay, in meters.</summary>
        public string ExtendLength
        {
            get { return _extendLength; }
            set { if (SetProperty(ref _extendLength, value ?? "")) OnPropertyChanged(nameof(ExtendText)); }
        }

        public string NewBayLength
        {
            get { return _newBayLength; }
            set { if (SetProperty(ref _newBayLength, value ?? "")) OnPropertyChanged(nameof(NewBayText)); }
        }

        public string NewBayName
        {
            get { return _newBayName; }
            set { SetProperty(ref _newBayName, value ?? ""); }
        }

        public Bay.SurfaceType NewBaySurface
        {
            get { return _newBaySurface; }
            set { if (SetProperty(ref _newBaySurface, value)) OnPropertyChanged(nameof(NewBayText)); }
        }

        private static long ParseMeters(string text)
        {
            long v;
            return long.TryParse((text ?? "").Replace(",", "").Replace(".", "").Replace("m", "").Trim(), out v) ? v : 0;
        }

        public string ExtendText
        {
            get
            {
                if (_selected == null || _selectedBay == null) return "Pick a bay above to extend it.";
                var r = _selectedBay.Bay;
                long to = ParseMeters(_extendLength);
                string blocker = BayService.GetExtendBlocker(_selected.Station, r, to);
                if (blocker != null) return blocker;
                // which of the player's coach the new length is still too short for
                var stillShort = BayService.TooShortFor(to, false).Concat(BayService.TooShortFor(to, true)).ToList();
                return string.Format(
                    "{0:N0} m to {1:N0} m: {2}, about {3} days. The bay stays in use meanwhile.",
                    r.Length, to, Money(BayService.ExtensionPrice(_selected.Station, r, to)), BayService.ExtensionDays(to - r.Length))
                       + (stillShort.Count > 0 ? " Still too short for your " + string.Join(", ", stillShort) + "." : " Long enough for all your coach.");
            }
        }

        public string NewBayText
        {
            get
            {
                if (_selected == null) return "";
                long length = ParseMeters(_newBayLength);
                string blocker = BayService.GetBuildBlocker(Human, _selected.Station, length, _newBayName);
                if (blocker != null) return blocker;
                return string.Format(
                    "{0:N0} m {1}: {2}, about {3} days to build.",
                    length, _newBaySurface.ToString().ToLower(), Money(BayService.NewPrice(_selected.Station, length, _newBaySurface)), BayService.NewDays(length));
            }
        }

        public ICommand ExtendBayCommand => _extendBayCommand ?? (_extendBayCommand = new RelayCommand(AskExtendBay));
        public ICommand BuildBayCommand => _buildBayCommand ?? (_buildBayCommand = new RelayCommand(AskBuildBay));

        private void LoadBays(Station station)
        {
            var keep = _selectedBay?.Bay;
            Bays.Clear();
            foreach (var r in station.Bays.OrderByDescending(x => x.Length))
            {
                Bays.Add(new BayItem
                {
                    Bay = r,
                    Text = string.Format(
                        "{0}  |  {1:N0} m  |  {2}  |  {3}{4}",
                        r.Name, r.Length, r.Type == Bay.BayType.Helipad ? "helipad" : r.Surface.ToString().ToLower(),
                        BayService.Status(r), r.BuiltBy != null && r.BuiltBy.IsHuman ? "  |  paid by you" : "")
                });
            }
            _selectedBay = Bays.FirstOrDefault(x => x.Bay == keep);
            OnPropertyChanged(nameof(SelectedBay));
            OnPropertyChanged(nameof(HasBaySelection));
            OnPropertyChanged(nameof(ExtendText));
            OnPropertyChanged(nameof(NewBayText));
        }

        private void AskExtendBay()
        {
            if (_selected == null || _selectedBay == null)
            {
                Status = "Pick a bay first.";
                return;
            }
            var station = _selected.Station;
            var r = _selectedBay.Bay;
            long to = ParseMeters(_extendLength);
            string blocker = BayService.GetExtendBlocker(station, r, to);
            if (blocker != null)
            {
                Status = blocker;
                return;
            }
            double price = BayService.ExtensionPrice(station, r, to);
            if (price > Human.Money)
            {
                Status = "You can't afford that (" + Money(price) + ").";
                return;
            }
            _pending = () =>
                {
                    string m;
                    BayService.Extend(Human, station, r, to, out m);
                    LoadDetail();
                    Status = m;
                    _changed();
                };
            PendingText = string.Format("Extend {0} at {1} to {2:N0} m for {3}?", r.Name, station.Profile.IATACode, to, Money(price));
        }

        private void AskBuildBay()
        {
            if (_selected == null) return;
            var station = _selected.Station;
            long length = ParseMeters(_newBayLength);
            string name = _newBayName;
            var surface = _newBaySurface;
            string blocker = BayService.GetBuildBlocker(Human, station, length, name);
            if (blocker != null)
            {
                Status = blocker;
                return;
            }
            double price = BayService.NewPrice(station, length, surface);
            if (price > Human.Money)
            {
                Status = "You can't afford that (" + Money(price) + ").";
                return;
            }
            _pending = () =>
                {
                    string m;
                    BayService.Build(Human, station, length, surface, name, out m);
                    NewBayName = "";
                    ApplySearch();
                    Selected = Stations.FirstOrDefault(a => a.Station == station);
                    LoadDetail();
                    Status = m;
                    _changed();
                };
            PendingText = string.Format(
                "Build a {0:N0} m {1} bay at {2} for {3}? It takes about {4} days.",
                length, surface.ToString().ToLower(), station.Profile.IATACode, Money(price), BayService.NewDays(length));
        }

        /// <summary>Selects an station (used by other pages).</summary>
        public void SelectStation(Station station)
        {
            if (station == null) return;
            var row = Stations.FirstOrDefault(a => a.Station == station);
            if (row == null)
            {
                _search = station.Profile.IATACode;
                OnPropertyChanged(nameof(Search));
                ApplySearch();
                row = Stations.FirstOrDefault(a => a.Station == station);
            }
            Selected = row;
        }
        public ICommand BuildTerminalCommand { get; }
        public ICommand ConfirmPendingCommand { get; }
        public ICommand CancelPendingCommand { get; }

        public string TerminalName
        {
            get { return _terminalName; }
            set { SetProperty(ref _terminalName, value); }
        }

        public int TerminalGates
        {
            get { return _terminalGates; }
            set { SetProperty(ref _terminalGates, value); OnPropertyChanged(nameof(TerminalPriceText)); }
        }

        /// <summary>Lease cargo gates instead of passenger gates.</summary>
        public bool LeaseCargo
        {
            get { return _leaseCargo; }
            set
            {
                if (SetProperty(ref _leaseCargo, value))
                {
                    if (_selected != null)
                    {
                        RefreshGateChoices(_selected.Station);
                    }

                    OnPropertyChanged(nameof(LeaseHeader));
                }
            }
        }

        public string LeaseHeader
        {
            get { return _leaseCargo ? "Lease cargo gates" : "Lease passenger gates"; }
        }

        private Terminal.TerminalType LeaseKind
        {
            get { return _leaseCargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger; }
        }

        private void RefreshGateChoices(Station station)
        {
            GateChoices.Clear();
            int free = station.Terminals.GetFreeGates(LeaseKind);
            for (int i = 1; i <= Math.Min(free, 8); i++)
            {
                GateChoices.Add(i);
            }

            _gates = GateChoices.Count > 0 ? GateChoices[0] : 0;
            OnPropertyChanged(nameof(Gates));
            OnPropertyChanged(nameof(PriceText));
        }

        /// <summary>Build a cargo terminal instead of a passenger terminal.</summary>
        public bool TerminalCargo
        {
            get { return _terminalCargo; }
            set { SetProperty(ref _terminalCargo, value); }
        }

        public string TerminalPriceText
        {
            get
            {
                if (_selected == null)
                {
                    return "";
                }

                var a = _selected.Station;
                return string.Format(
                    "Price {0}, ready in {1} days. Your own terminal gives you gates at a lower yearly cost once it is finished.",
                    Money(TerminalPrice(a, _terminalGates)), _terminalGates * 10 + 60);
            }
        }

        /// <summary>A question to confirm (build, buy, sell); empty when nothing is pending.</summary>
        public string PendingText
        {
            get { return _pendingText; }
            private set
            {
                if (SetProperty(ref _pendingText, value))
                {
                    if (value == null)
                    {
                        _pending = null;
                    }

                    OnPropertyChanged(nameof(HasPending));
                }
            }
        }

        public bool HasPending
        {
            get { return _pendingText != null; }
        }
        public List<ContractChoice> ContractTypes { get; }

        public ICommand LeaseCommand { get; }
        public ICommand EndContractCommand { get; }
        public ICommand ConfirmEndCommand { get; }
        public ICommand CancelEndCommand { get; }

        /// <summary>Show only the stations where you have a contract.</summary>
        public bool OnlyMine
        {
            get { return _onlyMine; }
            set
            {
                if (SetProperty(ref _onlyMine, value))
                {
                    ApplySearch();
                }
            }
        }

        public List<string> SortChoices { get; } =
            new List<string> { "Name", "Distance", "Country", "Size", "Bay", "Free gates", "Carriers", "Routes" };

        /// <summary>The Distance sort is chosen, so the box for the station to measure from is shown.</summary>
        public bool SortByDistance => _sortBy == "Distance";

        private string _near = "";
        private Station _nearStation;

        /// <summary>
        ///     The station the Distance sort measures from (a code, a name, a town, a state). Answers "what is near my
        ///     hub" and "which station is closest to there".
        /// </summary>
        public string Near
        {
            get { return _near; }
            set
            {
                if (SetProperty(ref _near, value ?? ""))
                {
                    _all = _all ?? Models.Stations.Stations.GetAllStations().OrderBy(a => a.Profile.Name).ToList();
                    _nearStation = StationSearch.Resolve(_near, _all);
                    OnPropertyChanged(nameof(NearStatus));
                    ApplySearch();
                }
            }
        }

        /// <summary>The station the list is measured from, or null.</summary>
        public Station NearStation => _nearStation;

        public string NearStatus
        {
            get
            {
                if (!SortByDistance) return "";
                if (_near.Trim().Length == 0) return "Name a station to measure from.";
                if (_nearStation == null) return "No station found for \"" + _near.Trim() + "\".";
                return "Nearest first from " + _nearStation.Profile.IATACode + "  " + _nearStation.Profile.Name + ".";
            }
        }

        public string SortBy
        {
            get { return _sortBy; }
            set
            {
                if (SetProperty(ref _sortBy, value ?? "Name"))
                {
                    OnPropertyChanged(nameof(SortByDistance));
                    OnPropertyChanged(nameof(NearStatus));
                    ApplySearch();
                }
            }
        }

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
                    ConfirmEnd = false;
                    PendingText = null;
                    LoadDetail();
                    (LeaseCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (BuildTerminalCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string Title { get; private set; }
        public string Info { get; private set; }

        /// <summary>Set by the carrier page: opens the demand page for an station.</summary>
        public Action<Station> DemandRequested { get; set; }

        /// <summary>Set by the carrier page: opens the traffic page for an station.</summary>
        public Action<Station> StatsRequested { get; set; }

        private ICommand _showStats;
        public ICommand ShowStatsCommand => _showStats ?? (_showStats = new RelayCommand(() => { if (_selected != null) StatsRequested?.Invoke(_selected.Station); }));

        private ICommand _showDemand;
        public ICommand ShowDemandCommand => _showDemand ?? (_showDemand = new RelayCommand(() => { if (_selected != null) DemandRequested?.Invoke(_selected.Station); }));

        /// <summary>The station categories: Regional, Domestic and the two international kinds.</summary>
        public List<NamedChoice<StationProfile.StationType>> StationTypes { get; } = new List<NamedChoice<StationProfile.StationType>>
        {
            new NamedChoice<StationProfile.StationType> { Name = "Regional", Value = StationProfile.StationType.Regional },
            new NamedChoice<StationProfile.StationType> { Name = "Domestic", Value = StationProfile.StationType.Domestic },
            new NamedChoice<StationProfile.StationType> { Name = "Short haul international", Value = StationProfile.StationType.ShortHaulInternational },
            new NamedChoice<StationProfile.StationType> { Name = "Long haul international", Value = StationProfile.StationType.LongHaulInternational }
        };

        private NamedChoice<StationProfile.StationType> _stationType;
        public bool HasSelection => _selected != null;

        public NamedChoice<StationProfile.StationType> StationType
        {
            get { return _stationType; }
            set
            {
                if (value == null || _selected == null || _stationType == value) return;
                var station = _selected.Station;
                if (station.Profile.Type == value.Value) { SetProperty(ref _stationType, value); return; }
                station.Profile.Type = value.Value;
                station.PlayerEdited = true;
                SetProperty(ref _stationType, value);
                ApplySearch();
                Selected = Stations.FirstOrDefault(a => a.Station == station);
                Status = station.Profile.IATACode + " is now a " + value.Name.ToLower() + " station.";
                _changed();
            }
        }
        public string GateSummary { get; private set; }

        public ContractChoice ContractType
        {
            get { return _contractType; }
            set { SetProperty(ref _contractType, value); OnPropertyChanged(nameof(PriceText)); }
        }

        public int Gates
        {
            get { return _gates; }
            set { SetProperty(ref _gates, value); OnPropertyChanged(nameof(PriceText)); }
        }

        public string PriceText
        {
            get
            {
                if (_selected == null || _contractType == null || _gates < 1)
                {
                    return "";
                }

                double yearly = StationHelpers.GetYearlyContractPayment(_selected.Station, _contractType.Type, _gates, _leaseYears);
                return "Yearly payment for a " + _leaseYears + " year contract: " + Money(yearly) + ". Contracts renew by themselves; see Stations > Contracts.";
            }
        }

        public ContractItem SelectedContract
        {
            get { return _selectedContract; }
            set
            {
                if (SetProperty(ref _selectedContract, value))
                {
                    ConfirmEnd = false;
                    (EndContractCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(EndText));
                }
            }
        }

        public bool ConfirmEnd
        {
            get { return _confirmEnd; }
            private set
            {
                if (SetProperty(ref _confirmEnd, value))
                {
                    OnPropertyChanged(nameof(NotConfirmEnd));
                    OnPropertyChanged(nameof(EndText));
                }
            }
        }

        public bool NotConfirmEnd
        {
            get { return !_confirmEnd; }
        }

        public string EndText
        {
            get
            {
                return _selectedContract == null
                    ? ""
                    : "Ending this contract costs a penalty of " + Money(Penalty(_selectedContract.Contract)) + ". Continue?";
            }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        private static Carrier Human
        {
            get { return GameObject.GetInstance().HumanCarrier; }
        }

        private static double Penalty(StationContract c)
        {
            return ((c.YearlyPayment / 12) * c.MonthsLeft) / 10;
        }

        /// <summary>Refreshes the lists after something changed elsewhere (a route, an coach).</summary>
        public void Reload()
        {
            LoadDetail();
        }

        private void ApplySearch()
        {
            var own = new HashSet<Station>(Human.Stations);
            _all = _all ?? Models.Stations.Stations.GetAllStations().OrderBy(a => a.Profile.Name).ToList();

            IEnumerable<Station> list = _onlyMine ? Human.Stations.OrderBy(a => a.Profile.Name) : _all;
            string s = _search.Trim();
            if (s.Length > 0)
            {
                list = list.Where(a => StationSearch.Matches(a, s));
            }

            Station near = SortByDistance ? _nearStation : null;
            switch (_sortBy)
            {
                case "Distance":
                    // without an station to measure from there is nothing to sort by, so the list stays by name
                    if (near != null)
                        list = list.Where(a => a.Profile.Coordinates != null)
                                   .OrderBy(a => a == near ? 0 : 1)
                                   .ThenBy(a => MathHelpers.GetDistance(near, a));
                    break;
                case "Country":
                    list = list.OrderBy(a => a.Profile.Country?.Name).ThenBy(a => a.Profile.Name);
                    break;
                case "Size":
                    list = list.OrderByDescending(a => (int) a.Profile.Size).ThenBy(a => a.Profile.Name);
                    break;
                case "Bay":
                    list = list.OrderByDescending(a => a.GetMaxBayLength()).ThenBy(a => a.Profile.Name);
                    break;
                case "Free gates":
                    list = list.OrderByDescending(a => a.Terminals.GetFreeGates(Terminal.TerminalType.Passenger))
                               .ThenBy(a => a.Profile.Name);
                    break;
                case "Carriers":
                    list = list.OrderByDescending(a => a.CarrierContracts.Select(c => c.Carrier).Distinct().Count())
                               .ThenBy(a => a.Profile.Name);
                    break;
                case "Routes":
                    list = list.OrderByDescending(a => StationHelpers.GetStationRoutes(a).Count).ThenBy(a => a.Profile.Name);
                    break;
            }

            Stations.Clear();
            foreach (var a in list.Take(300))
            {
                var row = new StationRow(a, own.Contains(a), near);
                row.LeaseCommand = new RelayCommand(() => QuickLease(row));
                Stations.Add(row);
            }
        }

        //the plus button of the Windows station list: two gates with the chosen contract type
        private void QuickLease(StationRow row)
        {
            var station = row.Station;
            if (!row.CanLease || station.Terminals.GetFreeGates(Terminal.TerminalType.Passenger) < 1)
            {
                Status = "There are no free gates to lease at " + station.Profile.IATACode + ".";
                return;
            }

            int gates = Math.Min(2, station.Terminals.GetFreeGates(Terminal.TerminalType.Passenger));
            double yearly = StationHelpers.GetYearlyContractPayment(station, _contractType.Type, gates, 2);
            RouteCreator.SignContract(station, _contractType.Type);
            Status = string.Format(
                "Leased {0} gate(s) at {1} ({2} contract, {3}/year).",
                gates, station.Profile.Name, _contractType.Text, Money(yearly));

            string status = Status;
            ApplySearch();
            Selected = Stations.FirstOrDefault(a => a.Station == station);
            LoadDetail();
            _changed();
            Status = status;
        }

        private void LoadDetail()
        {
            Contracts.Clear();
            GateOffers.Clear();
            Facilities.Clear();
            GateChoices.Clear();
            Terminals.Clear();
            Hubs.Clear();
            HubOffers.Clear();

            if (_selected == null)
            {
                HubText = "";
                OnPropertyChanged(nameof(HubText));
                OnPropertyChanged(nameof(HasHubs));
                OnPropertyChanged(nameof(HasHubOffers));
                Title = Info = GateSummary = "";
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Info));
                OnPropertyChanged(nameof(GateSummary));
                return;
            }

            var station = _selected.Station;
            var carrier = Human;
            var now = GameObject.GetInstance().GameTime;

            _stationType = StationTypes.First(t => t.Value == station.Profile.Type);
            OnPropertyChanged(nameof(StationType));
            OnPropertyChanged(nameof(HasSelection));

            Title = station.Profile.IATACode + "  " + station.Profile.Name;
            Info = string.Format(
                "{0}, {1}  |  size {2}  |  longest bay {3} m",
                station.Profile.Town?.Name, station.Profile.Country?.Name, DemandViewModel.SizeName(station.Profile.Size), station.GetMaxBayLength());

            int total = station.Terminals.GetNumberOfGates(Terminal.TerminalType.Passenger);
            int free = station.Terminals.GetFreeGates(Terminal.TerminalType.Passenger);
            int mine = station.GetCarrierContracts(carrier)
                              .Where(c => c.TerminalType == Terminal.TerminalType.Passenger)
                              .Sum(c => c.NumberOfGates);
            GateSummary = string.Format("Passenger gates: {0} in total, {1} free, {2} leased by you", total, free, mine);

            int cargoTotal = station.Terminals.GetNumberOfGates(Terminal.TerminalType.Cargo);
            if (cargoTotal > 0 || station.GetCarrierContracts(carrier).Any(c => c.TerminalType == Terminal.TerminalType.Cargo))
            {
                GateSummary += string.Format(
                    "{0}Cargo gates: {1} in total, {2} free, {3} leased by you",
                    Environment.NewLine,
                    cargoTotal,
                    station.Terminals.GetFreeGates(Terminal.TerminalType.Cargo),
                    station.GetCarrierContracts(carrier)
                           .Where(c => c.TerminalType == Terminal.TerminalType.Cargo)
                           .Sum(c => c.NumberOfGates));
            }
            else
            {
                GateSummary += Environment.NewLine + "No cargo terminal here. Build one under Terminals to run cargo.";
            }

            foreach (var c in station.GetCarrierContracts(carrier))
            {
                Contracts.Add(
                    new ContractItem
                    {
                        Contract = c,
                        Text = string.Format(
                            "{0}  |  {1} gate(s) ({2})  |  {3} months left  |  {4}/year",
                            c.Type, c.NumberOfGates, c.TerminalType, c.MonthsLeft, Money(c.YearlyPayment))
                    });
            }

            LoadGateOffers(station);

            RefreshGateChoices(station);

            LoadTerminals(station);
            LoadBays(station);
            OnPropertyChanged(nameof(TerminalPriceText));

            // facilities can only be bought where the carrier has a contract
            bool hasContract = Contracts.Count > 0;
            foreach (StationFacility.FacilityType type in Enum.GetValues(typeof(StationFacility.FacilityType)))
            {
                // cargo facilities only make sense where there is a cargo terminal
                if (type == StationFacility.FacilityType.Cargo
                    && !station.Terminals.StationTerminals.Exists(t => t.Type == Terminal.TerminalType.Cargo))
                {
                    continue;
                }

                var current = station.GetCurrentStationFacility(carrier, type);
                var building = station.GetCarrierBuildingFacility(carrier, type);
                var next = StationFacilities.GetFacilities(type)
                                            .Where(f => f.TypeLevel > (current?.TypeLevel ?? 0))
                                            .OrderBy(f => f.TypeLevel)
                                            .FirstOrDefault();

                double price = next == null ? 0 : FacilityPrice(station, next);
                var captured = next;

                Facilities.Add(
                    new FacilityItem
                    {
                        Type = type,
                        Name = type.ToString(),
                        Current = current == null || current.TypeLevel == 0 ? "none" : current.Name,
                        Next = next == null ? "(best level reached)" : next.Name + " for " + Money(price),
                        Building = building == null ? null : "building " + building.Name,
                        CanBuy = hasContract && next != null && building == null,
                        BuyCommand = new RelayCommand(() => BuyFacility(station, captured))
                    });
            }

            LoadHubs(station);

            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Info));
            OnPropertyChanged(nameof(GateSummary));
        }

        public static string HubEffect(HubType.TypeOfHub type)
        {
            switch (type)
            {
                case HubType.TypeOfHub.Hub: return "+20% passenger demand on trips to this station";
                case HubType.TypeOfHub.RegionalHub: return "+20% demand on trips from the same part of the country";
                case HubType.TypeOfHub.FocusCity: return "+15% demand, and a place for the drivers to change over";
                case HubType.TypeOfHub.FortressHub: return "+30% passenger demand on trips to this station";
                default: return "";
            }
        }

        // why the carrier can't create this kind of hub here, or null when it can (the same rules as the game)
        private static string HubReason(Carrier carrier, Station station, HubType type, double price)
        {
            if (station.HasHub(carrier))
            {
                return "You already have a hub here.";
            }

            if ((station.GetCurrentStationFacility(carrier, StationFacility.FacilityType.Service)?.TypeLevel ?? 0) <= 0)
            {
                return "Needs a service center of yours here (Facilities).";
            }

            if (carrier.Money <= price)
            {
                return "You need more than " + Money(price) + ".";
            }

            var terminalType = carrier.CarrierRouteFocus == Route.RouteType.Cargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger;
            double allGates = station.Terminals.GetNumberOfGates(terminalType);
            double percent = allGates > 0 ? 100.0 * station.Terminals.GetNumberOfGates(carrier) / allGates : 0;
            int limit = (int) carrier.GetCarrierValue() + 1;
            int total = carrier.GetHubs().Count;

            switch (type.Type)
            {
                case HubType.TypeOfHub.RegionalHub:
                    if (station.Profile.Size != GeneralHelpers.Size.Large && station.Profile.Size != GeneralHelpers.Size.Medium)
                        return "Only at medium or large stations (not smaller or the very biggest ones).";
                    if (station.GetHubs().Count >= 7)
                        return "This station already has the most hubs it can take (7).";
                    break;
                case HubType.TypeOfHub.FortressHub:
                    if (station.Profile.Size <= GeneralHelpers.Size.Medium)
                        return "Only at large stations.";
                    if (percent <= 70)
                        return "You need more than 70% of the gates here (you have " + (int) percent + "%).";
                    if (total >= limit)
                        return "You have as many hubs as your carrier value allows (" + limit + ").";
                    break;
                case HubType.TypeOfHub.Hub:
                    if (percent <= 20)
                        return "You need more than 20% of the gates here (you have " + (int) percent + "%).";
                    if (total >= limit)
                        return "You have as many hubs as your carrier value allows (" + limit + ").";
                    if (station.GetHubs(HubType.TypeOfHub.Hub).Count >= (int) station.Profile.Size)
                        return "This station has no room for another plain hub.";
                    break;
            }

            return CarrierHelpers.CanCreateHub(carrier, station, type) ? null : "You can't create this kind of hub here.";
        }

        private void LoadHubs(Station station)
        {
            var carrier = Human;
            foreach (var h in station.GetHubs())
            {
                var hub = h;
                bool mine = hub.Carrier == carrier;
                Hubs.Add(
                    new HubItem
                    {
                        Carrier = hub.Carrier.Profile.Name,
                        Type = hub.Type.Name,
                        Effect = HubEffect(hub.Type.Type),
                        IsMine = mine,
                        RemoveCommand = mine ? new RelayCommand(() => AskRemoveHub(station, hub)) : null
                    });
            }

            foreach (var t in HubTypes.GetHubTypes().OrderBy(t => t.Price))
            {
                var type = t;
                double price = StationHelpers.GetHubPrice(station, type);
                string reason = HubReason(carrier, station, type, price);
                HubOffers.Add(
                    new HubOffer
                    {
                        Type = type,
                        Name = type.Name,
                        Price = Money(price),
                        Effect = HubEffect(type.Type),
                        Reason = reason,
                        CanBuild = reason == null,
                        BuyCommand = new RelayCommand(() => AskCreateHub(station, type, price))
                    });
            }

            HubText = Hubs.Count == 0
                ? "No carrier has a hub here."
                : station.HasHub(carrier) ? "You have a hub here." : "You have no hub here.";
            OnPropertyChanged(nameof(HubText));
            OnPropertyChanged(nameof(HasHubs));
            OnPropertyChanged(nameof(HasHubOffers));
        }

        private void AskCreateHub(Station station, HubType type, double price)
        {
            string reason = HubReason(Human, station, type, price);
            if (reason != null)
            {
                Status = reason;
                return;
            }

            _pending = () =>
                {
                    if (HubReason(Human, station, type, price) != null)
                    {
                        return;
                    }

                    station.AddHub(new Hub(Human, type));
                    CarrierHelpers.AddCarrierInvoice(
                        Human, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -price);
                    Status = type.Name + " created at " + station.Profile.IATACode + ". " + HubEffect(type.Type) + ".";
                    LoadDetail();
                    _changed();
                };
            PendingText = string.Format("Create a {0} at {1} for {2}? {3}.", type.Name.ToLower(), station.Profile.IATACode, Money(price), HubEffect(type.Type));
        }

        private void AskRemoveHub(Station station, Hub hub)
        {
            _pending = () =>
                {
                    station.RemoveHub(hub);
                    Status = "The " + hub.Type.Name.ToLower() + " at " + station.Profile.IATACode + " was closed (no refund).";
                    LoadDetail();
                    _changed();
                };
            PendingText = "Close your " + hub.Type.Name.ToLower() + " at " + station.Profile.IATACode + "? You get no money back.";
        }

        private static double TerminalPrice(Station station, int gates)
        {
            return gates * station.GetTerminalGatePrice() + station.GetTerminalPrice();
        }

        private void LoadTerminals(Station station)
        {
            var carrier = Human;
            var now = GameObject.GetInstance().GameTime;

            foreach (var t in station.Terminals.GetTerminals())
            {
                bool mine = t.Carrier == carrier;
                string owner = mine ? "yours" : t.Carrier == null ? "station" : t.Carrier.Profile.Name;
                string state = t.DeliveryDate > now ? "ready " + DateFormatter.Date(t.DeliveryDate) : "in use";
                var terminal = t;

                bool buyable = t.DeliveryDate <= now && t.IsBuyable;

                int totalRented = station.CarrierContracts.Sum(c => c.NumberOfGates);
                bool free = station.Terminals.GetNumberOfGates(t.Type) - t.Gates.NumberOfGates >= totalRented;
                bool constructing = t.DeliveryDate > now;

                Terminals.Add(
                    new TerminalItem
                    {
                        Terminal = t,
                        Text = string.Format("{0}  |  {1}  |  {2} gates  |  {3}  |  {4}", t.Name, t.Type, t.Gates.NumberOfGates, owner, state),
                        CanBuy = buyable,
                        CanSell = mine && (constructing || free),
                        ActionText = constructing ? "Cancel" : "Sell",
                        BuyCommand = new RelayCommand(() => AskBuyTerminal(station, terminal)),
                        SellCommand = new RelayCommand(() => AskSellTerminal(station, terminal))
                    });
            }
        }

        // the computer carriers' spare gates at the station
        private void LoadGateOffers(Station station)
        {
            double perGate = GateTrading.GetPricePerGate(station);

            foreach (var contract in station.CarrierContracts.Where(c => GateTrading.IsTradable(station, c)).ToList())
            {
                int spare = GateTrading.GetSpareGates(station, contract);
                if (spare < 1)
                {
                    continue;
                }

                var captured = contract;
                var offer = new GateSaleOffer
                {
                    Contract = contract,
                    Text = string.Format(
                        "{0} can spare {1} of its {2} gate(s)  |  {3} per gate",
                        contract.Carrier.Profile.Name, spare, contract.NumberOfGates, Money(perGate)),
                    BuyOneText = "Buy 1",
                    BuyAllText = "Buy " + spare,
                    CanBuyAll = spare > 1
                };
                offer.BuyOneCommand = new RelayCommand(() => AskBuyGates(station, captured, 1));
                offer.BuyAllCommand = new RelayCommand(() => AskBuyGates(station, captured, spare));
                GateOffers.Add(offer);
            }

            GateOffersText = GateOffers.Count == 0
                ? "No carrier has gates to spare here."
                : "You pay the carrier a transfer fee per gate, then pay the normal rent for a " + _contractType.Text + " contract (the type chosen under Lease gates).";
            OnPropertyChanged(nameof(HasGateOffers));
            OnPropertyChanged(nameof(GateOffersText));
        }

        private void AskBuyGates(Station station, StationContract contract, int gates)
        {
            double price = GateTrading.GetPricePerGate(station) * gates;
            if (price > Human.Money)
            {
                Status = "You can't afford " + gates + " gate(s) (" + Money(price) + ").";
                return;
            }

            var type = _contractType.Type;
            _pending = () =>
                {
                    string message;
                    GateTrading.BuyGates(station, contract, gates, type, out message);
                    Status = message;
                    ApplySearch();
                    Selected = Stations.FirstOrDefault(a => a.Station == station);
                    LoadDetail();
                    _changed();
                    Status = message;
                };
            PendingText = string.Format(
                "Buy {0} gate(s) from {1} at {2} for {3}? You get a {4} contract for them.",
                gates, contract.Carrier.Profile.Name, station.Profile.IATACode, Money(price), type);
        }

        private void AskBuildTerminal()
        {
            if (_selected == null)
            {
                return;
            }

            var station = _selected.Station;
            double price = TerminalPrice(station, _terminalGates);

            if (price > Human.Money)
            {
                Status = "You can't afford a terminal here (" + Money(price) + ").";
                return;
            }

            string name = string.IsNullOrWhiteSpace(_terminalName) ? "Terminal" : _terminalName.Trim();
            var type = _terminalCargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger;
            int gates = _terminalGates;
            _pending = () => BuildTerminal(station, name, gates, type, price);
            PendingText = string.Format("Build {0} ({1} gates, {2}) at {3} for {4}?", name, gates, type, station.Profile.IATACode, Money(price));
        }

        private void BuildTerminal(Station station, string name, int gates, Terminal.TerminalType type, double price)
        {
            var carrier = Human;
            var delivery = GameObject.GetInstance().GameTime.AddDays(gates * 10 + 60);
            station.AddTerminal(new Terminal(station, carrier, name, gates, delivery, type));
            CarrierHelpers.AddCarrierInvoice(
                carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -price);

            Status = name + " ordered, ready " + DateFormatter.Date(delivery) + ".";
            LoadDetail();
            _changed();
        }

        private void AskBuyTerminal(Station station, Terminal terminal)
        {
            double price = terminal.Gates.NumberOfGates * station.GetTerminalGatePrice() + station.GetTerminalPrice();
            if (price > Human.Money)
            {
                Status = "You can't afford this terminal (" + Money(price) + ").";
                return;
            }

            _pending = () =>
                {
                    terminal.PurchaseTerminal(Human);
                    CarrierHelpers.AddCarrierInvoice(
                        Human, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -price);
                    Status = "You bought " + terminal.Name + ".";
                    LoadDetail();
                    _changed();
                };
            PendingText = "Buy " + terminal.Name + " (" + terminal.Gates.NumberOfGates + " gates) for " + Money(price) + "?";
        }

        private void AskSellTerminal(Station station, Terminal terminal)
        {
            var now = GameObject.GetInstance().GameTime;

            if (terminal.DeliveryDate > now)
            {
                _pending = () =>
                    {
                        station.RemoveTerminal(terminal);
                        Status = "Construction of " + terminal.Name + " cancelled.";
                        LoadDetail();
                        _changed();
                    };
                PendingText = "Cancel the construction of " + terminal.Name + "? The money is not refunded.";
                return;
            }

            double price = (terminal.Gates.NumberOfGates * station.GetTerminalGatePrice() + station.GetTerminalPrice()) / 2;
            _pending = () =>
                {
                    var contract = station.CarrierContracts.FirstOrDefault(
                        c => c.Carrier == terminal.Carrier && c.NumberOfGates == terminal.Gates.NumberOfGates);

                    CarrierHelpers.AddCarrierInvoice(Human, now, Invoice.InvoiceType.Purchases, price);
                    terminal.Carrier = null;

                    if (contract != null)
                    {
                        contract.YearlyPayment = StationHelpers.GetYearlyContractPayment(
                            station, StationContract.ContractType.Full, terminal.Gates.NumberOfGates, 20);
                    }

                    Status = "Sold " + terminal.Name + " for " + Money(price) + ". You keep the gates on a lease.";
                    LoadDetail();
                    _changed();
                };
            PendingText = "Sell " + terminal.Name + " for " + Money(price) + "? You keep the gates, but now pay a lease for them.";
        }

        private static double FacilityPrice(Station station, StationFacility facility)
        {
            double price = facility.Price;
            if (station.Profile.Country != Human.Profile.Country)
            {
                price *= 1.25;
            }

            return price;
        }

        private void BuyFacility(Station station, StationFacility facility)
        {
            var carrier = Human;
            double price = FacilityPrice(station, facility);

            if (station.GetCarrierBuildingFacility(carrier, facility.Type) != null)
            {
                Status = "A " + facility.Type + " facility is already being built here.";
                return;
            }

            if (price > carrier.Money)
            {
                Status = "You can't afford " + facility.Name + " (" + Money(price) + ").";
                return;
            }

            CarrierHelpers.AddCarrierInvoice(
                carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -price);
            station.AddStationFacility(carrier, facility, GameObject.GetInstance().GameTime.AddDays(facility.BuildingDays));

            Status = facility.Name + " ordered, ready in " + facility.BuildingDays + " days.";
            LoadDetail();
            _changed();
        }

        private void Lease()
        {
            if (_selected == null)
            {
                return;
            }

            var station = _selected.Station;
            var carrier = Human;

            var kind = LeaseKind;
            if (_gates < 1 || station.Terminals.GetFreeGates(kind) < _gates)
            {
                Status = "There are no free " + (_leaseCargo ? "cargo " : "") + "gates to lease at " + station.Profile.IATACode + ".";
                return;
            }

            double yearly = StationHelpers.GetYearlyContractPayment(station, _contractType.Type, _gates, _leaseYears);

            if (RouteCreator.NeedsContract(station, kind))
            {
                // new contract with the chosen gates and length
                string signed;
                GateContractService.Sign(carrier, station, _contractType.Type, kind, _gates, _leaseYears, out signed);
                Status = signed;
            }
            else
            {
                StationHelpers.RentGates(
                    station, carrier, _contractType.Type, kind, _gates, _leaseYears);
                Status = _gates + " more gate(s) leased at " + station.Profile.IATACode + " (" + Money(yearly) + "/year).";
            }

            ApplySearch();
            Selected = Stations.FirstOrDefault(a => a.Station == station);
            LoadDetail();
            _changed();
        }

        private void EndContract()
        {
            var item = _selectedContract;
            if (item == null || _selected == null)
            {
                return;
            }

            var station = _selected.Station;
            var carrier = Human;
            var contract = item.Contract;
            double penalty = Penalty(contract);

            var others = station.CarrierContracts.Where(a => a.Carrier == carrier && a != contract).ToList();
            if (!StationHelpers.CanFillRoutesEntries(station, carrier, others, Weather.Season.AllYear))
            {
                Status = "You can't end this contract: your trips at " + station.Profile.IATACode
                         + " would no longer fit. Remove or move those trips first.";
                ConfirmEnd = false;
                return;
            }

            if (contract.Terminal != null)
            {
                Status = "This contract belongs to your own terminal and can't be ended here.";
                ConfirmEnd = false;
                return;
            }

            CarrierHelpers.AddCarrierInvoice(
                carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -penalty);
            station.RemoveCarrierContract(contract);

            for (int i = 0; i < contract.NumberOfGates; i++)
            {
                var gate = station.Terminals.GetGates().FirstOrDefault(g => g.Carrier == carrier);
                if (gate != null)
                {
                    gate.Carrier = null;
                }
            }

            Status = "Contract ended (penalty " + Money(penalty) + ").";
            ConfirmEnd = false;
            ApplySearch();
            Selected = Stations.FirstOrDefault(a => a.Station == station);
            LoadDetail();
            _changed();
        }
    }
}
