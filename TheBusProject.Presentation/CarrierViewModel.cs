using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Helpers.Workers;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.General;
using TheBusProject.Models.Routes;

namespace TheBusProject.Presentation
{
    public class FleetItem
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
        public string Homebase { get; set; }
        public int Routes { get; set; }
        public bool IsIdle { get; set; }
        public bool IsOnOrder { get; set; }
        public string Crew { get; set; }
        public string Note { get; set; }
        public bool CanStart { get; set; }
        public bool CanStop { get; set; }
        public bool CanEditCabin { get; set; }
        public ICommand EditCabinCommand { get; set; }
        public ICommand StartCommand { get; set; }
        public ICommand StopCommand { get; set; }
        public bool CanSchedule { get; set; }
        public ICommand ScheduleCommand { get; set; }
    }

    public class RouteItem : ObservableObject
    {
        private string _balance;

        public Route Route { get; set; }
        public string Name { get; set; }
        public string Distance { get; set; }

        public string Balance
        {
            get { return _balance; }
            set { SetProperty(ref _balance, value); }
        }
    }

    public class TripItem
    {
        public string Trip { get; set; }
        public string Route { get; set; }
        public string When { get; set; }
        public string Coach { get; set; }
    }

    public class SpeedChoice
    {
        public string Name { get; set; }
        public GeneralHelpers.GameSpeedValue Value { get; set; }
        public override string ToString() { return Name; }
    }

    /// <summary>The main screen of a running game: clock, money, fleet and routes of the human carrier.</summary>
    public class CarrierViewModel : ObservableObject, IDisposable
    {
        private readonly ShellViewModel _shell;
        private string _date, _money, _status;
        private bool _isRunning;
        private SpeedChoice _speed;
        private string _signature;

        public CarrierViewModel(ShellViewModel shell)
        {
            _shell = shell;
            Fleet = new ObservableCollection<FleetItem>();
            Routes = new ObservableCollection<RouteItem>();
            Speeds = new[]
            {
                new SpeedChoice {Name = "Slowest (75 s / month)", Value = GeneralHelpers.GameSpeedValue.Slowest},
                new SpeedChoice {Name = "Slow (60 s / month)", Value = GeneralHelpers.GameSpeedValue.Slow},
                new SpeedChoice {Name = "Normal (45 s / month)", Value = GeneralHelpers.GameSpeedValue.Normal},
                new SpeedChoice {Name = "Fast (30 s / month)", Value = GeneralHelpers.GameSpeedValue.Fast},
                new SpeedChoice {Name = "Fastest (15 s / month)", Value = GeneralHelpers.GameSpeedValue.Fastest},
                new SpeedChoice {Name = "Turbo (as fast as possible)", Value = GeneralHelpers.GameSpeedValue.Turbo}
            };
            var current = TheBusProject.Infrastructure.Settings.GetInstance().GameSpeed;
            _speed = Speeds.First(s => s.Value == current);

            TogglePauseCommand = new RelayCommand(TogglePause);
            SkipDayCommand = new AsyncCommand(() => SkipAsync(g => g.AddDays(1)), () => !GameObjectWorker.GetInstance().IsSkipping);
            SkipWeekCommand = new AsyncCommand(() => SkipAsync(g => g.AddDays(7)), () => !GameObjectWorker.GetInstance().IsSkipping);
            SkipMonthCommand = new AsyncCommand(() => SkipAsync(g => new DateTime(g.Year, g.Month, 1).AddMonths(1)), () => !GameObjectWorker.GetInstance().IsSkipping);
            SaveCommand = new AsyncCommand(SaveAsync);
            GameSession.InGame = true;
            DateDisplay.Formatter = DateFormatter.Date;
            DateFormatter.Changed += OnDateFormatChanged;
            DistanceFormatter.Changed += OnDateFormatChanged;
            MainMenuCommand = new AsyncCommand(
                async () =>
                    {
                        Status = GameSession.AutoSaveOnExit ? "Saving..." : Status;
                        await System.Threading.Tasks.Task.Run(() => GameSession.AutoSaveIfNeeded());
                        GameSession.InGame = false;
                        GameSession.StopClock();
                        // the game changes the loaded data, so it is read again for the next game
                        TheBusProject.Infrastructure.GameDataLoader.Invalidate();
                        _shell.ShowMainMenu();
                    });
            Market = new MarketViewModel(OnGameChanged);
            NewCoach = new NewCoachViewModel(OnGameChanged);
            Stations = new StationsViewModel(OnGameChanged);
            Drivers = new DriversViewModel(OnGameChanged);
            Finance = new FinanceViewModel();
            StationManager = new StationManagerViewModel(OnGameChanged);
            Loans = new LoansViewModel(OnGameChanged);
            Maintenance = new MaintenanceViewModel(OnGameChanged);
            Charter = new CharterViewModel(OnGameChanged);
            Deals = new CoachDealsViewModel(OnGameChanged);
            Renewal = new FleetRenewalViewModel(OnGameChanged);
            License = new LicenseViewModel(OnGameChanged);
            Demand = new DemandViewModel(OnGameChanged);
            Ratings = new RatingsViewModel();
            Profile = new ProfileViewModel(OnGameChanged);
            Advertising = new AdvertisingViewModel(OnGameChanged);
            StationStats = new StationStatsViewModel();
            FleetPerformance = new FleetPerformanceViewModel();
            RouteProfits = new RouteProfitViewModel(route =>
                {
                    RoutesTab = 0;
                    SelectedRoute = Routes.FirstOrDefault(r => r.Route == route);
                });
            Ranking = new ProfitRankingViewModel(route =>
                {
                    Page = Array.IndexOf(PageNames, "Routes");
                    RoutesTab = 0;
                    SelectedRoute = Routes.FirstOrDefault(r => r.Route == route);
                });
            Competitors = new CompetitorsViewModel(route =>
                {
                    Page = Array.IndexOf(PageNames, "Routes");
                    RoutesTab = 0;
                    SelectedRoute = Routes.FirstOrDefault(r => r.Route == route);
                });
            CompareTypes = new CompareTypesViewModel();
            NewCoach.CompareRequested = t =>
                {
                    CompareTypes.AddType(t);
                    CoachesTab = 2;
                };
            Scenario = new ScenarioViewModel();
            Suggestions = new RouteSuggestionsViewModel(OnGameChanged, (a, b, f) =>
                {
                    // the planner with the suggestion's stations and coach, to change the schedule first
                    Stations.PlanRoute(a, b);
                    var pick = Stations.Create?.Coaches.FirstOrDefault(x => x.Coach == f);
                    if (pick != null) Stations.Create.SelectedCoach = pick;
                    Page = Array.IndexOf(PageNames, "Routes");
                    RoutesTab = 1;
                });
            Connections = new ConnectionsViewModel((a, b) =>
                {
                    Stations.PlanRoute(a, b);
                    RoutesTab = 1;
                });
            StationManager.StatsRequested = a =>
                {
                    StationStats.ShowStation(a);
                    StationsTab = 3;
                };
            GateContracts = new GateContractsViewModel(OnGameChanged);
            GateContracts.OpenStation = a =>
                {
                    StationManager.SelectStation(a);
                    StationsTab = 0;
                };
            StationManager.DemandRequested = a =>
                {
                    Demand.ShowStation(a);
                    StationsTab = 1;
                };
            Insurance = new InsuranceViewModel(OnGameChanged);
            Services = new CarrierServicesViewModel(OnGameChanged);
            TripSchools = new TripSchoolsViewModel(OnGameChanged);
            Contracts = new ContractsViewModel(OnGameChanged);
            Timetable = new TimetableViewModel(OnGameChanged);
            Alliances = new AlliancesViewModel(OnGameChanged);
            CarrierList = new CarriersViewModel();
            Codeshares = new CodesharesViewModel(OnGameChanged);
            Shares = new SharesViewModel(OnGameChanged);
            Subsidiaries = new SubsidiariesViewModel(OnGameChanged);
            Options = new OptionsViewModel(shell, null, MainMenuCommand);
            Options.MoneyChanged = () =>
                {
                    var me = GameObject.GetInstance().HumanCarrier;
                    if (me != null) Money = Convert.ToString(new ValueCurrencyFormatter().Convert(me.Money));
                    OnGameChanged();
                };
            Report = new CarrierReportViewModel(OpenReportItem);
            News = new NewsViewModel(link =>
            {
                // the monthly report: My Carrier > Monthly report, that month
                if (link is ReportLink rp)
                {
                    Report.ShowMonth(rp.Year, rp.Month);
                    Page = Array.IndexOf(PageNames, "My Carrier");
                    MyCarrierTab = ReportTabIndex;
                    return true;
                }
                // a rival alert: Carriers > Competitors, "My routes", that station pair
                if (!(link is RivalLink rl)) return false;
                bool found = Competitors.ShowRoute(rl.A, rl.B, rl.IsCargo);
                if (found)
                {
                    Page = Array.IndexOf(PageNames, "Carriers");
                    CarriersTab = 1;
                }
                return found;
            });
            Map = new WorldMapViewModel();
            Overview = new FleetOverviewViewModel(OnGameChanged, f => Refit = new CabinRefitViewModel(f, CloseRefit), OpenSchedule);
            Refresh();
        }

        /// <summary>The pages of the menu on the left, in the order of the buttons.</summary>
        public static readonly string[] PageNames =
            { "My Carrier", "Routes", "Coaches", "Stations", "Alliances", "Staff", "Carriers", "Trips", "News", "Options" };

        private int _page;
        private CabinRefitViewModel _refit;

        /// <summary>The cabin editor of an owned coach, or null when none is open.</summary>
        public CabinRefitViewModel Refit
        {
            get { return _refit; }
            private set
            {
                if (SetProperty(ref _refit, value))
                {
                    OnPropertyChanged(nameof(HasRefit));
                }
            }
        }

        public bool HasRefit => _refit != null;

        private void CloseRefit()
        {
            Refit = null;
            OnGameChanged();
        }

        public string[] Pages => PageNames;

        private void OnDateFormatChanged()
        {
            // the screens built their texts with the old format
            _financeDate = null;
            _signature = null;
            OnGameChanged();
        }

        private int _tripsTab;

        /// <summary>The sub page of Trips: 0 fleet, 1 timetable, 2 all trips.</summary>
        public int TripsTab
        {
            get { return _tripsTab; }
            set { SetProperty(ref _tripsTab, value); }
        }

        /// <summary>Go to the scheduler of an coach (Trips > Edit timetable).</summary>
        public void OpenSchedule(FleetCoach f)
        {
            Timetable.Select(f);
            Page = Array.IndexOf(PageNames, "Trips");
            TripsTab = 1;
        }

        /// <summary>The index of the page shown.</summary>
        public int Page
        {
            get { return _page; }
            set
            {
                if (value >= 0 && value < PageNames.Length && SetProperty(ref _page, value))
                {
                    OnPageShown(value);
                }
            }
        }

        // pages that show data the game changes on its own are read again when they are opened
        private void OnPageShown(int page)
        {
            switch (PageNames[page])
            {
                case "My Carrier":
                    FleetPerformance.Reload();
                    if (MyCarrierTab == ReportTabIndex) Report.Reload();
                    if (MyCarrierTab == StatisticsTabIndex) Statistics.Reload();
                    if (MyCarrierTab == RankingTabIndex) Ranking.Reload();
                    break;
                case "Stations": StationStats.Reload(); break;
                case "Routes": Connections.Reload(); RouteProfits.Reload(); if (RoutesTab == SuggestionsTabIndex) { var _ = Suggestions.Reload(); } break;
                case "Alliances": Alliances.Reload(); break;
                case "Carriers": CarrierList.Reload(); Codeshares.Reload(); Shares.Reload(); Subsidiaries.Reload(); Competitors.Reload(); break;
                case "News": News.Reload(); break;
                case "Options": Options.Reload(); break;
            }
        }

        public MarketViewModel Market { get; }
        public NewCoachViewModel NewCoach { get; }
        public StationsViewModel Stations { get; }
        public DriversViewModel Drivers { get; }
        public FinanceViewModel Finance { get; }
        public StationManagerViewModel StationManager { get; }
        public LoansViewModel Loans { get; }
        public MaintenanceViewModel Maintenance { get; }
        public CharterViewModel Charter { get; }
        public CoachDealsViewModel Deals { get; }
        public FleetRenewalViewModel Renewal { get; }
        public LicenseViewModel License { get; }
        public DemandViewModel Demand { get; }
        public RatingsViewModel Ratings { get; }
        public ProfileViewModel Profile { get; }
        public AdvertisingViewModel Advertising { get; }
        public StationStatsViewModel StationStats { get; }
        public FleetPerformanceViewModel FleetPerformance { get; }

        /// <summary>Routes > Route profits.</summary>
        public RouteProfitViewModel RouteProfits { get; }

        /// <summary>Carriers > Competitors.</summary>
        public CompetitorsViewModel Competitors { get; }
        public CompareTypesViewModel CompareTypes { get; }

        private int _coachesTab, _routesTab;

        // the My Carrier sub tabs (the Scenario tab counts while hidden)
        public const int ReportTabIndex = 1, FleetTabIndex = 3, FinancesTabIndex = 5;

        private int _myCarrierTab;

        /// <summary>The open sub tab under My Carrier: 0 overview, 1 monthly report, 2 scenario, 3 fleet, 4 fleet performance, 5 finances, ...</summary>
        public int MyCarrierTab
        {
            get { return _myCarrierTab; }
            set
            {
                if (SetProperty(ref _myCarrierTab, value) && value == ReportTabIndex) Report?.Reload();
                if (value == StatisticsTabIndex) Statistics?.Reload();
                if (value == RankingTabIndex) Ranking?.Reload();
            }
        }

        /// <summary>My Carrier > Monthly report.</summary>
        public CarrierReportViewModel Report { get; }

        /// <summary>My Carrier > Statistics (the last sub tab).</summary>
        public StatisticsViewModel Statistics { get; } = new StatisticsViewModel();

        public const int StatisticsTabIndex = 18;


        /// <summary>"1 route" or "4 routes": the counted things on the screens read properly either way.</summary>
        public static string Count(int n, string one, string many)
        {
            return n.ToString("#,0") + " " + (n == 1 ? one : many);
        }

        /// <summary>My Carrier > Best and worst: the routes, or the coach, that earn most and least.</summary>
        public ProfitRankingViewModel Ranking { get; }

        public const int RankingTabIndex = 19;

        // where a line of the monthly report leads
        private void OpenReportItem(ReportItem item)
        {
            if (item == null) return;
            switch (item.Kind)
            {
                case ReportItemKind.Route:
                    var row = Routes.FirstOrDefault(r => r.Route.Id == item.RouteId);
                    if (row == null) return;
                    Page = Array.IndexOf(PageNames, "Routes");
                    RoutesTab = 0;
                    SelectedRoute = row;
                    break;
                case ReportItemKind.Competitors:
                    if (item.A != null) Competitors.ShowRoute(item.A, item.B, item.IsCargo);
                    else if (item.Carrier != null) Competitors.ShowCarrier(item.Carrier);
                    Page = Array.IndexOf(PageNames, "Carriers");
                    CarriersTab = 1;
                    break;
                case ReportItemKind.Fleet:
                    MyCarrierTab = FleetTabIndex;
                    break;
                case ReportItemKind.IdleCoach:
                    Page = Array.IndexOf(PageNames, "Routes");
                    RoutesTab = SuggestionsTabIndex;
                    break;
                case ReportItemKind.Finances:
                    MyCarrierTab = FinancesTabIndex;
                    break;
            }
        }

        private int _carriersTab;

        /// <summary>The open sub tab under Carriers: 0 all carriers, 1 competitors, 2 codeshares, 3 shares, 4 subsidiaries.</summary>
        public int CarriersTab
        {
            get { return _carriersTab; }
            set { SetProperty(ref _carriersTab, value); }
        }

        /// <summary>The open sub tab under Routes: 0 my routes, 1 create route, 2 connections, 3 route profits.</summary>
        public int RoutesTab
        {
            get { return _routesTab; }
            set
            {
                if (SetProperty(ref _routesTab, value) && value == SuggestionsTabIndex) { var _ = Suggestions?.Reload(); }
            }
        }

        /// <summary>The Routes sub tab with the suggestions for idle coach.</summary>
        public const int SuggestionsTabIndex = 4;

        /// <summary>Routes > Suggestions: routes for the idle coach.</summary>
        public RouteSuggestionsViewModel Suggestions { get; }

        public ConnectionsViewModel Connections { get; }

        /// <summary>My Carrier > Scenario (only in scenario games).</summary>
        public ScenarioViewModel Scenario { get; }

        /// <summary>The open sub tab under Coaches: 0 new, 1 used, 2 compare.</summary>
        public int CoachesTab
        {
            get { return _coachesTab; }
            set { SetProperty(ref _coachesTab, value); }
        }
        public GateContractsViewModel GateContracts { get; }

        private string _logoPath;

        /// <summary>The carrier's logo file for the top bar, or null.</summary>
        public string LogoPath
        {
            get { return _logoPath; }
            private set { SetProperty(ref _logoPath, value); }
        }

        private int _stationsTab;

        /// <summary>The open sub tab under Stations: 0 manage, 1 demand.</summary>
        public int StationsTab
        {
            get { return _stationsTab; }
            set { SetProperty(ref _stationsTab, value); }
        }
        public InsuranceViewModel Insurance { get; }
        public CarrierServicesViewModel Services { get; }
        public TripSchoolsViewModel TripSchools { get; }
        public ContractsViewModel Contracts { get; }
        public TimetableViewModel Timetable { get; }
        public AlliancesViewModel Alliances { get; }
        public CarriersViewModel CarrierList { get; }
        public CodesharesViewModel Codeshares { get; }
        public SharesViewModel Shares { get; }
        public SubsidiariesViewModel Subsidiaries { get; }
        public OptionsViewModel Options { get; }
        public NewsViewModel News { get; }
        public FleetOverviewViewModel Overview { get; }
        public WorldMapViewModel Map { get; }

        private RouteItem _selectedRoute;
        private string _routeFilter = "";
        private RouteDetailViewModel _routeDetail;

        public RouteItem SelectedRoute
        {
            get { return _selectedRoute; }
            set
            {
                if (SetProperty(ref _selectedRoute, value))
                {
                    RouteDetail = value == null ? null : new RouteDetailViewModel(value.Route, OnGameChanged);
                }
            }
        }

        public RouteDetailViewModel RouteDetail
        {
            get { return _routeDetail; }
            private set
            {
                if (SetProperty(ref _routeDetail, value))
                {
                    OnPropertyChanged(nameof(HasRouteDetail));
                }
            }
        }

        public bool HasRouteDetail
        {
            get { return _routeDetail != null; }
        }
        private string _financeDate;
        private int _loanMonth;

        private void OnGameChanged()
        {
            Stations.ReloadAdvice();
            Drivers.Reload();
            StationManager?.Reload();
            Loans?.Reload();
            Maintenance?.Reload();
            Insurance?.Reload();
            Services?.Reload();
            TripSchools?.Reload();
            Contracts?.Reload();
            Charter?.Reload();
            Deals?.Reload();
            Renewal?.Reload();
            License?.Reload();
            Demand?.Reload();
            Ratings?.Reload();
            GateContracts?.Reload();
            if (GameObject.GetInstance()?.HumanCarrier != null) LogoPath = CarrierProfileService.GetLogo(GameObject.GetInstance().HumanCarrier);
            Timetable?.ReloadCoach();
            _financeDate = null;
            Refresh();
        }

        public SpeedChoice[] Speeds { get; }
        public ObservableCollection<FleetItem> Fleet { get; }
        public ObservableCollection<RouteItem> Routes { get; }

        /// <summary>
        ///     The routes actually shown in the list: all of them until something is typed in the filter box, and
        ///     then only the ones that call at a place matching what was typed. Kept separate from Routes so the
        ///     list is still only rebuilt when the carrier's routes really change, which is what stops the
        ///     selection and the half-typed fares from jumping about.
        /// </summary>
        public ObservableCollection<RouteItem> ShownRoutes { get; } = new ObservableCollection<RouteItem>();

        /// <summary>What the player has typed to narrow the route list: a code, a station or a town.</summary>
        public string RouteFilter
        {
            get { return _routeFilter; }
            set { if (SetProperty(ref _routeFilter, value)) ApplyRouteFilter(); }
        }

        /// <summary>"24 routes" or "3 of 24 routes", under the filter box.</summary>
        public string RouteFilterHint
        {
            get
            {
                if (Routes.Count == ShownRoutes.Count) return Count(Routes.Count, "route", "routes");
                return ShownRoutes.Count + " of " + Count(Routes.Count, "route", "routes");
            }
        }

        /// <summary>Does this route call anywhere the player has typed? Stops count, not just the two ends.</summary>
        private static bool Matches(RouteItem item, string what)
        {
            if (item?.Route == null) return false;
            foreach (var station in TheBusProject.Helpers.RouteStops.Calls(item.Route))
            {
                if (station?.Profile == null) continue;
                if (Has(station.Profile.IATACode, what)) return true;
                if (Has(station.Profile.Name, what)) return true;
                if (station.Profile.Town != null && Has(station.Profile.Town.Name, what)) return true;
            }

            return false;
        }

        /// <summary>
        ///     Does any word here start with what was typed? Anywhere-in-the-text matching looks reasonable until
        ///     you type DAL for Dallas and get Fort Lauderdale, Glendale and Scottsdale as well. A word start is
        ///     what a person means: "dal" finds Dallas, "fort" finds both the Forts, and neither finds Lauderdale.
        /// </summary>
        private static bool Has(string text, string what)
        {
            if (string.IsNullOrEmpty(text)) return false;
            bool atWordStart = true;
            for (int i = 0; i < text.Length; i++)
            {
                if (atWordStart && i + what.Length <= text.Length
                    && string.Compare(text, i, what, 0, what.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return true;
                }

                atWordStart = !char.IsLetterOrDigit(text[i]);
            }

            return false;
        }

        private void ApplyRouteFilter()
        {
            string what = (_routeFilter ?? "").Trim();
            ShownRoutes.Clear();
            foreach (var item in Routes)
            {
                if (what.Length == 0 || Matches(item, what)) ShownRoutes.Add(item);
            }

            OnPropertyChanged(nameof(RouteFilterHint));
        }
        public ObservableCollection<TripItem> Trips { get; } = new ObservableCollection<TripItem>();

        public string Summary { get; private set; }

        public ICommand TogglePauseCommand { get; }

        /// <summary>Fast-forward: one day, a week, or to the 1st of next month, as fast as the computer can, then pause.</summary>
        public ICommand SkipDayCommand { get; }
        public ICommand SkipWeekCommand { get; }
        public ICommand SkipMonthCommand { get; }

        private async System.Threading.Tasks.Task SkipAsync(Func<DateTime, DateTime> until)
        {
            var worker = GameObjectWorker.GetInstance();
            if (worker.IsSkipping) return;
            DateTime target = until(GameObject.GetInstance().GameTime.Date);
            Status = "Fast-forwarding to " + DateFormatter.Date(target) + "... (Pause stops it)";
            await System.Threading.Tasks.Task.Run(() =>
                {
                    GameSession.StopClock();
                    worker.SkipUntil(target);
                });
            Refresh();
        }
        public ICommand SaveCommand { get; }
        public ICommand MainMenuCommand { get; }

        private string _carrierName;
        public string CarrierName
        {
            get { return _carrierName; }
            private set { SetProperty(ref _carrierName, value); }
        }

        public string Date
        {
            get { return _date; }
            private set { SetProperty(ref _date, value); }
        }

        public string Money
        {
            get { return _money; }
            private set { SetProperty(ref _money, value); }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        public bool IsRunning
        {
            get { return _isRunning; }
            private set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    OnPropertyChanged(nameof(PauseText));
                }
            }
        }

        public string PauseText
        {
            get { return IsRunning ? "Pause" : "Play"; }
        }

        private bool _wasSkipping;

        public SpeedChoice Speed
        {
            get { return _speed; }
            set
            {
                if (value != null && SetProperty(ref _speed, value))
                {
                    TheBusProject.Infrastructure.Settings.GetInstance().SetGameSpeed(value.Value);
                }
            }
        }

        private void StartCoach(FleetCoach f)
        {
            Status = FleetOverviewViewModel.TryStart(f);
            Refresh();
        }

        private void StopCoach(FleetCoach f)
        {
            f.Status = FleetCoach.CoachStatus.Stopped;
            Status = f.Name + " stopped.";
            Refresh();
        }

        private void TogglePause()
        {
            var worker = GameObjectWorker.GetInstance();
            if (worker.IsPaused)
            {
                worker.Restart();
            }
            else
            {
                worker.Pause();
            }
            Refresh();
        }

        private async System.Threading.Tasks.Task SaveAsync()
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            string name = GameSession.SafeFileName(carrier?.Profile?.Name ?? "save");
            Status = "Saving...";
            await GameSession.SaveGameAsync(name);
            Status = "Saved as " + name;
            Refresh();
        }

        /// <summary>Called by the UI on a timer (the game thread changes the data, so the view must poll).</summary>
        public void Refresh()
        {
            var game = GameObject.GetInstance();
            var carrier = game?.HumanCarrier;
            if (carrier == null)
            {
                return;
            }

            if (CarrierName != carrier.Profile.Name || _logoPath == null) LogoPath = CarrierProfileService.GetLogo(carrier);
            CarrierName = carrier.Profile.Name;
            Date = DateFormatter.Date(game.GameTime);
            if (_financeDate != Date)
            {
                _financeDate = Date;
                Finance.Refresh();

                // loan payments are taken once a month
                if (_loanMonth != game.GameTime.Month)
                {
                    _loanMonth = game.GameTime.Month;
                    Scenario.Reload();
                    // a scenario that just ended: pause and tell the player once
                    string endTitle;
                    string endText = ScenarioViewModel.TakeEndMessage(out endTitle);
                    if (endText != null)
                    {
                        GameObjectWorker.GetInstance().Pause();
                        _shell.ShowMessage(endTitle, endText);
                    }
                    Loans.Reload();
                    Maintenance.Reload();
                    Insurance.Reload();
                    Contracts.Reload();
                    Ratings.Reload();
                    GateContracts.Reload();
                    Advertising.Reload();
                    StationStats.Reload();
                    FleetPerformance.Reload();
                    RouteProfits.Reload();
                }
                Charter.Reload();
                Deals.Reload();
                // a computer carrier may have sent a codeshare offer
                if (Codeshares.Offers.Count != CodeshareService.Offers(carrier).Count) Codeshares.Reload();
            }
            Money = Convert.ToString(new ValueCurrencyFormatter().Convert(carrier.Money));
            IsRunning = !GameObjectWorker.GetInstance().IsPaused;
            bool skipping = GameObjectWorker.GetInstance().IsSkipping;
            string skipResult = GameObjectWorker.GetInstance().TakeSkipResult();
            if (skipResult != null) Status = skipResult;
            if (skipping != _wasSkipping)
            {
                _wasSkipping = skipping;
                (SkipDayCommand as AsyncCommand)?.RaiseCanExecuteChanged();
                (SkipWeekCommand as AsyncCommand)?.RaiseCanExecuteChanged();
                (SkipMonthCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            }

            var fleet = carrier.Fleet.ToList();
            var routes = carrier.Routes.ToList();
            var entries = routes.SelectMany(r => r.TimeTable.Entries).ToList();
            string signature = string.Join(",", fleet.Select(f => f.Name + f.Status + f.Routes.Count + f.Drivers.Count + (f.Coach.BuiltDate > game.GameTime ? "u" : "")))
                               + "|" + routes.Count + "|" + entries.Count;
            if (signature != _signature)
            {
                _signature = signature;
                Timetable?.ReloadCoach();

                Fleet.Clear();
                foreach (var f in fleet)
                {
                    Fleet.Add(
                        new FleetItem
                        {
                            Name = f.Name,
                            Type = f.Coach?.Type?.Name,
                            Status = f.Coach.BuiltDate > game.GameTime
                                ? "Delivery " + DateFormatter.Date(f.Coach.BuiltDate)
                                : f.Status.ToString(),
                            Homebase = f.Homebase?.Profile.IATACode,
                            Routes = f.Routes.Count,
                            IsIdle = f.Routes.Count == 0 && f.Coach.BuiltDate <= game.GameTime,
                            IsOnOrder = f.Coach.BuiltDate > game.GameTime,
                            Crew = f.Drivers.Count + "/" + f.Coach.Type.DriverCrew + " drivers",
                            CanStart = f.Status == FleetCoach.CoachStatus.Stopped && f.Coach.BuiltDate <= game.GameTime,
                            CanStop = f.Status != FleetCoach.CoachStatus.Stopped && f.Coach.BuiltDate <= game.GameTime,
                            Note = f.Routes.Count == 0 ? "no route yet" : null,
                            CanEditCabin = f.Status == FleetCoach.CoachStatus.Stopped && f.Coach.Type is CoachPassengerType,
                            EditCabinCommand = new RelayCommand(() => Refit = new CabinRefitViewModel(f, CloseRefit)),
                            StartCommand = new RelayCommand(() => StartCoach(f)),
                            StopCommand = new RelayCommand(() => StopCoach(f)),
                            CanSchedule = f.Coach.BuiltDate <= game.GameTime,
                            ScheduleCommand = new RelayCommand(() => OpenSchedule(f))
                        });
                }

                Trips.Clear();
                foreach (var e in entries.OrderBy(e => e.Day == DayOfWeek.Sunday ? 7 : (int) e.Day).ThenBy(e => e.Time).Take(600))
                {
                    Trips.Add(
                        new TripItem
                        {
                            Trip = e.Destination?.TripCode,
                            Route = e.DepartureStation?.Profile.IATACode + " - " + e.Destination?.Station?.Profile.IATACode,
                            When = e.Day + " " + e.Time.ToString(@"hh\:mm"),
                            Coach = e.Coach?.Name
                        });
                }

                Summary = string.Format(
                    "{0}, {1}, {2}, {3}",
                    Count(fleet.Count, "coach", "coaches"),
                    Count(routes.Count, "route", "routes"),
                    Count(entries.Count, "weekly trip", "weekly trips"),
                    Count(carrier.Stations.Count, "station", "stations"));
                OnPropertyChanged(nameof(Summary));
            }

            RefreshRoutes(routes);
            _routeDetail?.RefreshNumbers();
            Overview?.Reload();
            Map?.Reload();
            News?.Poll();
        }

        // the route list is only rebuilt when routes are added or removed, so the selection and typing in the fare boxes stay
        private void RefreshRoutes(List<Route> routes)
        {
            var money = new ValueCurrencyFormatter();
            bool same = routes.Count == Routes.Count && routes.Select((r, i) => Routes[i].Route == r).All(x => x);

            if (!same)
            {
                var keep = _selectedRoute?.Route;
                Routes.Clear();
                foreach (var r in routes)
                {
                    Routes.Add(
                        new RouteItem
                        {
                            Route = r,
                            Name = r.Destination1.Profile.IATACode + " - " + r.Destination2.Profile.IATACode + (r.IsCargoRoute ? " (cargo)" : ""),
                            Distance = DistanceFormatter.Km(MathHelpers.GetDistance(r.Destination1, r.Destination2))
                        });
                }

                var again = Routes.FirstOrDefault(x => x.Route == keep);
                if (again != null)
                {
                    SelectedRoute = again;
                }
                else if (keep != null)
                {
                    SelectedRoute = null;
                }
            }

            foreach (var item in Routes)
            {
                item.Balance = "profit " + Convert.ToString(money.Convert(item.Route.Balance));
            }

            ApplyRouteFilter();
        }

        public void Dispose()
        {
            DateFormatter.Changed -= OnDateFormatChanged;
            DistanceFormatter.Changed -= OnDateFormatChanged;
        }
    }
}
