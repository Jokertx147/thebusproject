using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.Routes;

namespace TheBusProject.Presentation
{
    public class CoachChoice
    {
        public string Name { get; set; }
        public FleetCoach Coach { get; set; }
        public override string ToString() { return Name; }
    }

    public class ContractChoice
    {
        public StationContract.ContractType Type { get; set; }
        public string Text { get; set; }
        public override string ToString() { return Text; }
    }

    /// <summary>Offer to lease gates at an station where the carrier has none free.</summary>
    public class GateOffer : ObservableObject
    {
        private readonly Station _station;
        private readonly Action _signed;
        private readonly Terminal.TerminalType _terminalType;
        private ContractChoice _selected;

        public GateOffer(Station station, Action signed, Terminal.TerminalType terminalType = Terminal.TerminalType.Passenger)
        {
            _station = station;
            _signed = signed;
            _terminalType = terminalType;
            string kind = terminalType == Terminal.TerminalType.Cargo ? "cargo " : "";
            var f = new ValueCurrencyFormatter();
            int gates = Math.Min(2, RouteCreator.GetFreeGates(station, terminalType));
            CanLease = gates > 0;
            Options = new List<ContractChoice>();

            if (CanLease)
            {
                foreach (StationContract.ContractType t in Enum.GetValues(typeof(StationContract.ContractType)))
                {
                    double yearly = StationHelpers.GetYearlyContractPayment(station, t, gates, 2);
                    Options.Add(new ContractChoice { Type = t, Text = string.Format("{0}  ({1}/year, {2} gate(s), 2 years)", t, f.Convert(yearly), gates) });
                }
                _selected = Options.First();
            }

            Title = station.Profile.IATACode + " " + station.Profile.Name;
            Text = CanLease
                ? "You have no free " + kind + "gate at " + Title + ". Lease gates there to create the route."
                : terminalType == Terminal.TerminalType.Cargo
                    ? "There are no free cargo gates at " + Title + " to lease. Build a cargo terminal there first (Stations tab > Build a terminal)."
                    : "There are no free gates at " + Title + " to lease.";
            LeaseCommand = new RelayCommand(Lease, () => CanLease);
        }

        public string Title { get; }
        public string Text { get; }
        public bool CanLease { get; }
        public List<ContractChoice> Options { get; }
        public ICommand LeaseCommand { get; }

        public ContractChoice SelectedOption
        {
            get { return _selected; }
            set { SetProperty(ref _selected, value); }
        }

        private void Lease()
        {
            if (_selected == null)
            {
                return;
            }

            RouteCreator.SignContract(_station, _selected.Type, _terminalType);
            _signed();
        }
    }

    /// <summary>Create a passenger or cargo route between two stations, with an optional schedule for one coach.</summary>
    public class CreateRouteViewModel : ObservableObject
    {
        private readonly Station _origin, _destination;
        private readonly Action<string> _done;
        private readonly bool _cargo;
        private Station _chosenStop;
        private Station _chosenCall;
        private readonly Route _existing;
        private readonly bool _move;
        private string _cargoPrice;
        private bool _updating;
        private ServiceTier _tier = ServiceTier.Standard;
        private CoachChoice _coach;
        private OpsType _operation = OpsType.Regular;
        private int _turnaround = 60;
        private int _trips = 1;
        private string _startTime;
        private bool _startCoach = true;
        private string _info;
        private bool _scheduleEnabled, _detailEnabled;

        /// <summary>Assign an coach (and a schedule) to a route that was created without one.</summary>
        public CreateRouteViewModel(Route existing, Action<string> done, bool move = false)
            : this(existing.Destination1, existing.Destination2, done, false, existing, move)
        {
        }

        public CreateRouteViewModel(Station origin, Station destination, Action<string> done, bool cargo = false, Route existing = null, bool move = false)
        {
            _existing = existing;
            _move = move;
            _origin = origin;
            _destination = destination;
            _done = done;
            _cargo = cargo;

            Title = existing != null
                ? string.Format(move ? "Move {0} - {1} to another coach" : "Assign a coach to {0} - {1}", origin.Profile.IATACode, destination.Profile.IATACode)
                : string.Format("New route {0} - {1}", origin.Profile.IATACode, destination.Profile.IATACode);
            Tiers = Enum.GetValues(typeof(ServiceTier)).Cast<ServiceTier>().ToList();
            Operations = new List<OpsType> { OpsType.Regular, OpsType.Whole_Day };
            Turnarounds = RouteCreator.TurnaroundOptions;
            Trips = new ObservableCollection<int>();
            StartTimes = new ObservableCollection<string>();
            Offers = new ObservableCollection<GateOffer>();

            Coaches = new List<CoachChoice>();
            if (existing == null)
            {
                Coaches.Add(new CoachChoice { Name = "(assign a coach later)" });
            }

            Coaches.AddRange(
                RouteCreator.GetCandidateCoaches(origin, destination, Route.RouteType.Passenger)
                            .Select(f => new CoachChoice { Name = f.Name + " - " + f.Coach.Type.Name, Coach = f }));
            _coach = existing == null
                ? (Coaches.Count > 1 ? Coaches[1] : Coaches[0])
                : Coaches.FirstOrDefault();

            try
            {
                var why = RouteCreator.WhyNot(origin, destination, Route.RouteType.Passenger);
                CantFly = why.Count == 0 ? null : "Can't run it: " + string.Join(" ", why);
            }
            catch (Exception ex)
            {
                Infrastructure.StartupLog.Write("Route planner: listing the coach that can't run failed: " + ex);
            }

            Stops = new ObservableCollection<Station>();
            Stops.CollectionChanged += (s, e) => StopsChanged();
            StopChoices = new List<Station>();
            RefreshStopChoices();
            AddStopCommand = new RelayCommand(AddStop);
            RemoveStopCommand = new RelayCommand(RemoveStop);
            FillStopsCommand = new RelayCommand(FillStops);

            CreateCommand = new RelayCommand(Create);
            CancelCommand = new RelayCommand(() => _done(null));

            SetDefaultTurnaround();
            RefreshSchedule();
            RefreshGates();
        }

        public string Title { get; }

        /// <summary>The places the service calls at on the way, in the order it calls at them.</summary>
        public ObservableCollection<Station> Stops { get; private set; }

        /// <summary>The stations that could be added as a stop: the ones roughly on the way, least detour first.</summary>
        public List<Station> StopChoices { get; private set; }

        public ICommand AddStopCommand { get; private set; }

        public ICommand RemoveStopCommand { get; private set; }

        /// <summary>Puts in the stops the service needs to be legal and within the coach's range.</summary>
        public ICommand FillStopsCommand { get; private set; }

        /// <summary>The stop the player has picked in the list of choices.</summary>
        public Station ChosenStop
        {
            get { return _chosenStop; }
            set { SetProperty(ref _chosenStop, value); }
        }

        /// <summary>The call the player has picked out of the service, to take it off again.</summary>
        public Station ChosenCall
        {
            get { return _chosenCall; }
            set { SetProperty(ref _chosenCall, value); }
        }

        /// <summary>The service as it stands: MIA - LIT - PHX - LAX, with each leg's length.</summary>
        public string StopsText
        {
            get
            {
                var calls = new List<Station> { _origin };
                calls.AddRange(Stops);
                calls.Add(_destination);
                if (calls.Count == 2)
                {
                    return "Non-stop, " + MathHelpers.GetDistance(_origin, _destination).ToString("#,0") + " km.";
                }

                var parts = new List<string>();
                double total = 0;
                for (int i = 0; i < calls.Count - 1; i++)
                {
                    double km = MathHelpers.GetDistance(calls[i], calls[i + 1]);
                    total += km;
                    parts.Add(calls[i].Profile.IATACode + " - " + calls[i + 1].Profile.IATACode + " " + km.ToString("#,0") + " km");
                }

                // what somebody riding the whole line pays, which is one fare for the journey and not the legs
                // added up: the difference is the point of running a service rather than a string of routes
                double endToEnd = PricingService.MarketFare(total);
                double added = 0;
                for (int i = 0; i < calls.Count - 1; i++)
                    added += PricingService.MarketFare(MathHelpers.GetDistance(calls[i], calls[i + 1]));

                return string.Join(", ", parts) + "; " + total.ToString("#,0") + " km in all. "
                       + calls[0].Profile.IATACode + " to " + calls[calls.Count - 1].Profile.IATACode
                       + " rides through on one ticket at about " + endToEnd.ToString("$#,0")
                       + ", not the " + added.ToString("$#,0") + " the legs come to separately.";
            }
        }

        /// <summary>
        ///     Where the coach stops to let everybody stretch their legs, leg by leg. Needs a coach chosen, since
        ///     how often a driver has to stop depends on how fast it goes.
        /// </summary>
        public string BreaksText
        {
            get
            {
                if (Coach == null || Coach.Coach == null) return "";

                var calls = new List<Station> { _origin };
                calls.AddRange(Stops);
                calls.Add(_destination);

                var lines = new List<string>();
                for (int i = 0; i < calls.Count - 1; i++)
                {
                    var leg = new PassengerRoute("breaks", calls[i], calls[i + 1], GameObject.GetInstance().GameTime, 0);
                    var rests = RouteStops.BreaksAlong(leg, Coach.Coach.Type);
                    if (rests.Count == 0) continue;
                    lines.Add(calls[i].Profile.IATACode + " - " + calls[i + 1].Profile.IATACode + ": "
                              + RouteStops.DescribeBreaks(leg, Coach.Coach.Type));
                }

                if (lines.Count == 0) return "No leg is long enough to need a rest break.";
                return "Rest breaks: " + string.Join("; ", lines) + ".";
            }
        }

        /// <summary>Why the service as it stands can't be run, or null.</summary>
        public string StopsProblem
        {
            get
            {
                FleetCoach f = Coach;
                return RouteStops.WhyNot(_origin, _destination, Stops.ToList(), f == null ? null : f.Coach,
                                         f == null ? 1 : Math.Max(1, f.NumberOfDrivers));
            }
        }

        public bool HasStopsProblem
        {
            get { return !string.IsNullOrEmpty(StopsProblem); }
        }

        /// <summary>
        ///     The stations worth offering as a stop: the ones a coach would hardly go out of its way for, the
        ///     smallest detour first.
        /// </summary>
        private void RefreshStopChoices()
        {
            var calls = new List<Station> { _origin };
            calls.AddRange(Stops);
            calls.Add(_destination);
            double straight = MathHelpers.GetDistance(_origin, _destination);
            double allowed = Math.Max(120, straight*0.25);

            var choices = new List<KeyValuePair<Station, double>>();
            foreach (Station a in Stations.GetAllActiveStations())
            {
                if (calls.Contains(a)) continue;
                double best = double.MaxValue;
                for (int i = 0; i < calls.Count - 1; i++)
                {
                    double detour = MathHelpers.GetDistance(calls[i], a)
                                    + MathHelpers.GetDistance(a, calls[i + 1])
                                    - MathHelpers.GetDistance(calls[i], calls[i + 1]);
                    if (detour < best) best = detour;
                }

                if (best <= allowed) choices.Add(new KeyValuePair<Station, double>(a, best));
            }

            StopChoices = choices.OrderBy(x => x.Value).Select(x => x.Key).Take(40).ToList();
            _chosenStop = StopChoices.FirstOrDefault();
            OnPropertyChanged("StopChoices");
            OnPropertyChanged("ChosenStop");
        }

        /// <summary>Puts the chosen station into the service, in the place along the road where it belongs.</summary>
        private void AddStop()
        {
            if (_chosenStop == null || Stops.Count >= RouteStops.Max) return;

            var calls = new List<Station> { _origin };
            calls.AddRange(Stops);
            calls.Add(_destination);
            int at = 0;
            double best = double.MaxValue;
            for (int i = 0; i < calls.Count - 1; i++)
            {
                double detour = MathHelpers.GetDistance(calls[i], _chosenStop)
                                + MathHelpers.GetDistance(_chosenStop, calls[i + 1])
                                - MathHelpers.GetDistance(calls[i], calls[i + 1]);
                if (detour < best)
                {
                    best = detour;
                    at = i;
                }
            }

            Stops.Insert(at, _chosenStop);
        }

        private void FillStops()
        {
            // without a coach picked yet, work to the best one in the yard: the player can pick afterwards
            FleetCoach f = Coach
                           ?? GameObject.GetInstance().HumanCarrier.DeliveredFleet
                                        .Where(x => x.Routes.Count == 0)
                                        .OrderByDescending(x => x.Coach.Range)
                                        .FirstOrDefault()
                           ?? GameObject.GetInstance().HumanCarrier.DeliveredFleet.OrderByDescending(x => x.Coach.Range).FirstOrDefault();
            if (f == null) return;
            var filled = RouteStops.Suggest(_origin, _destination, f.Coach, Stops.ToList(), Math.Max(1, f.NumberOfDrivers));
            Stops.Clear();
            foreach (Station a in filled) Stops.Add(a);
        }

        /// <summary>
        ///     Which coaches can work the service as it now stands. A stop shortens the longest leg, so a coach
        ///     that couldn't run the whole thing in one go may be able to work it with the stops in.
        /// </summary>
        private void RefreshCoachChoices()
        {
            if (_existing != null) return;

            var calls = new List<Station> { _origin };
            calls.AddRange(Stops);
            calls.Add(_destination);

            FleetCoach had = Coach;
            var list = new List<CoachChoice> { new CoachChoice { Name = "(assign a coach later)" } };
            list.AddRange(
                RouteCreator.GetCandidateCoaches(calls, Route.RouteType.Passenger)
                            .Select(f => new CoachChoice { Name = f.Name + " - " + f.Coach.Type.Name, Coach = f }));
            Coaches = list;
            _coach = list.FirstOrDefault(c => c.Coach == had) ?? (list.Count > 1 ? list[1] : list[0]);
            OnPropertyChanged("Coaches");
            OnPropertyChanged("SelectedCoach");
        }

        private void RemoveStop()
        {
            if (_chosenCall != null && Stops.Contains(_chosenCall)) Stops.Remove(_chosenCall);
            else if (Stops.Count > 0) Stops.RemoveAt(Stops.Count - 1);
        }

        private void StopsChanged()
        {
            RefreshCoachChoices();
            RefreshStopChoices();
            OnPropertyChanged("StopsText");
            OnPropertyChanged("BreaksText");
            OnPropertyChanged("StopsProblem");
            OnPropertyChanged("HasStopsProblem");
            RefreshSchedule();
            RefreshGates();
        }


        /// <summary>The player's idle coach that can't fly the route, and why (bay or range), or null.</summary>
        public string CantFly { get; }

        public bool HasCantFly => !string.IsNullOrEmpty(CantFly);
        public List<ServiceTier> Tiers { get; }
        public List<CoachChoice> Coaches { get; private set; }
        public List<OpsType> Operations { get; }
        public List<int> Turnarounds { get; }
        public ObservableCollection<int> Trips { get; }
        public ObservableCollection<string> StartTimes { get; }
        public ObservableCollection<GateOffer> Offers { get; }
        public ICommand CreateCommand { get; }
        public ICommand CancelCommand { get; }

        public bool IsCargo
        {
            get { return _cargo; }
        }

        public bool IsPassenger
        {
            get { return !_cargo; }
        }

        public bool IsNewRoute
        {
            get { return _existing == null; }
        }

        public bool ShowTier
        {
            get { return !_cargo && _existing == null; }
        }

        public bool ShowPrice
        {
            get { return _cargo && _existing == null; }
        }

        public string CreateLabel
        {
            get { return _existing == null ? "Create route" : _move ? "Move route" : "Assign coach"; }
        }

        /// <summary>The price per unit of cargo the route starts with.</summary>
        public string CargoPrice
        {
            get { return _cargoPrice; }
            set { SetProperty(ref _cargoPrice, value); }
        }

        public string CoachLabel
        {
            get { return _cargo ? "Cargo coach (only coach without routes)" : "Coach (only coach without routes)"; }
        }

        private Terminal.TerminalType TerminalKind
        {
            get { return _cargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger; }
        }

        public ServiceTier Tier
        {
            get { return _tier; }
            set { SetProperty(ref _tier, value); }
        }

        public CoachChoice SelectedCoach
        {
            get { return _coach; }
            set
            {
                if (value != null && SetProperty(ref _coach, value))
                {
                    SetDefaultTurnaround();
                    RefreshSchedule();
                    OnPropertyChanged("BreaksText");
                }
            }
        }

        public OpsType Operation
        {
            get { return _operation; }
            set
            {
                if (SetProperty(ref _operation, value))
                {
                    RefreshSchedule();
                }
            }
        }

        public int Turnaround
        {
            get { return _turnaround; }
            set
            {
                if (SetProperty(ref _turnaround, value))
                {
                    RefreshSchedule();
                }
            }
        }

        public int RoundTrips
        {
            get { return _trips; }
            set
            {
                if (SetProperty(ref _trips, value))
                {
                    RefreshStartTimes();
                    RefreshForecast();
                }
            }
        }

        public string StartTime
        {
            get { return _startTime; }
            set { SetProperty(ref _startTime, value); }
        }

        public bool StartCoach
        {
            get { return _startCoach; }
            set { SetProperty(ref _startCoach, value); }
        }

        public string Info
        {
            get { return _info; }
            private set { SetProperty(ref _info, value); }
        }

        /// <summary>True when an coach is chosen, so the schedule options apply.</summary>
        public bool ScheduleEnabled
        {
            get { return _scheduleEnabled; }
            private set { SetProperty(ref _scheduleEnabled, value); }
        }

        /// <summary>False on long-haul routes, where the schedule is weekly and made automatically.</summary>
        public bool DetailEnabled
        {
            get { return _detailEnabled; }
            private set { SetProperty(ref _detailEnabled, value); }
        }

        public bool HasOffers
        {
            get { return Offers.Count > 0; }
        }

        private FleetCoach Coach
        {
            get { return _coach?.Coach; }
        }

        private void SetDefaultTurnaround()
        {
            _updating = true;
            int minutes = Coach == null ? 60 : (int)FleetCoachHelpers.GetMinTimeBetweenTrips(Coach).TotalMinutes;
            int best = RouteCreator.TurnaroundOptions.FirstOrDefault(o => o >= minutes);
            _turnaround = best == 0 ? RouteCreator.TurnaroundOptions.Last() : best;
            OnPropertyChanged(nameof(Turnaround));
            _updating = false;
        }

        private void RefreshSchedule()
        {
            if (_updating)
            {
                return;
            }

            ScheduleEnabled = Coach != null;
            Trips.Clear();
            StartTimes.Clear();

            if (Coach == null)
            {
                DetailEnabled = false;
                if (_existing != null)
                {
                    Info = "None of your " + (_cargo ? "cargo " : "") + "coach without a route can run this route (range or bay). Buy one under Coaches.";
                    return;
                }

                Info = Coaches.Count == 1
                    ? "None of your " + (_cargo ? "cargo " : "") + "coach can run this route (range, bay, or already on a route). The route is created without a schedule; assign a coach later."
                    : "The route is created without a schedule; assign a coach later.";
                return;
            }

            var type = Coach.Coach.Type;
            if (RouteCreator.IsLongRoute(_origin, _destination, type, _turnaround))
            {
                DetailEnabled = false;
                Info = "This is a long-haul route, so a weekly schedule is created.";
                return;
            }

            DetailEnabled = true;
            int max = RouteCreator.GetMaxRoundTrips(_origin, _destination, type, _turnaround, _operation);
            for (int i = 1; i <= max; i++)
            {
                Trips.Add(i);
            }

            // the default is the number of round trips the forecast says earns the most a day (passenger routes),
            // not the most the coach can fly
            _suggested = null;
            int pick = max;
            if (!_cargo && GameObject.GetInstance()?.HumanCarrier != null)
            {
                try
                {
                    List<RouteForecast> all;
                    _suggested = RouteForecastService.Best(GameObject.GetInstance().HumanCarrier, _origin, _destination, Coach, max, out all, _move ? _existing : null);
                    pick = _suggested.RoundTrips;
                }
                catch (Exception ex)
                {
                    Infrastructure.StartupLog.Write("Route forecast failed: " + ex);
                    _suggested = null;
                }
            }

            _updating = true;
            _trips = pick;
            OnPropertyChanged(nameof(RoundTrips));
            _updating = false;

            RefreshStartTimes();
            RefreshForecast();
        }

        private RouteForecast _suggested;
        private string _forecast;

        /// <summary>The forecast at the chosen number of round trips (null for cargo, long-haul or no coach).</summary>
        public RouteForecast CurrentForecast { get; private set; }

        /// <summary>The forecast at the suggested number of round trips.</summary>
        public RouteForecast SuggestedForecast => _suggested;

        /// <summary>What the route is expected to do at the chosen number of round trips, and the suggestion.</summary>
        public string Forecast
        {
            get { return _forecast; }
            private set
            {
                if (SetProperty(ref _forecast, value)) OnPropertyChanged(nameof(HasForecast));
            }
        }

        public bool HasForecast => !string.IsNullOrEmpty(_forecast);

        private static string Cash(double v)
        {
            return (v < 0 ? "-$" : "$") + Math.Abs(Math.Round(v)).ToString("N0");
        }

        private void RefreshForecast()
        {
            CurrentForecast = null;
            if (_cargo || Coach == null || _suggested == null || !DetailEnabled || GameObject.GetInstance()?.HumanCarrier == null)
            {
                Forecast = null;
                return;
            }
            RouteForecast now;
            try
            {
                now = RouteForecastService.Forecast(GameObject.GetInstance().HumanCarrier, _origin, _destination, Coach, Math.Max(1, _trips), _move ? _existing : null);
            }
            catch (Exception ex)
            {
                Infrastructure.StartupLog.Write("Route forecast failed: " + ex);
                Forecast = null;
                return;
            }
            CurrentForecast = now;
            string text = string.Format("Forecast at {0} round trip{1} a day: about {2:0}% full ({3:0} of {4} seats), {5} income and {6} costs a trip, {7} a day.",
                now.RoundTrips, now.RoundTrips == 1 ? "" : "s", now.Load*100, now.PassengersPerTrip, now.Seats, Cash(now.RevenuePerTrip), Cash(now.CostPerTrip), Cash(now.ProfitPerDay));
            if (_suggested.ProfitPerDay < 0)
            {
                text += string.Format(" This coach is likely to lose money here however often it runs (the least loss is at {0} a day, {1} a day){2}. A different coach or destination may do better.",
                    _suggested.RoundTrips, Cash(_suggested.ProfitPerDay), _suggested.Rivals > 0 ? ": " + _suggested.Rivals + " other route" + (_suggested.Rivals == 1 ? "" : "s") + " already run the pair" : "");
            }
            else if (_suggested.RoundTrips != now.RoundTrips)
            {
                text += string.Format(" Suggested: {0} a day (about {1:0}% full, {2} a day).", _suggested.RoundTrips, _suggested.Load*100, Cash(_suggested.ProfitPerDay));
            }
            else
            {
                text += " This is the suggested number: the most profit a day.";
            }
            Forecast = text + " An estimate: rivals change fares and schedules, and connecting passengers come on top.";
        }

        private void RefreshStartTimes()
        {
            if (_updating || Coach == null)
            {
                return;
            }

            StartTimes.Clear();
            foreach (TimeSpan t in RouteCreator.GetStartTimes(
                _origin, _destination, Coach.Coach.Type, _turnaround, _trips, _operation))
            {
                StartTimes.Add(t.ToString(@"hh\:mm"));
            }

            // A short run goes first thing; a long one goes at night. Nine hours from Dallas leaving at six in
            // the morning arrives in the middle of the afternoon, which nobody wants and which wastes a day at
            // both ends. Leaving at nine at night it arrives for breakfast, and the passenger saves a hotel. The
            // player can pick any of the others; this is only which one is waiting for them.
            double oneWay = MathHelpers.GetTripTime(_origin, _destination, Coach.Coach.Type).TotalHours;
            StartTime = oneWay >= 7
                            ? StartTimes.FirstOrDefault(t => string.CompareOrdinal(t, "20:00") >= 0 && string.CompareOrdinal(t, "23:00") <= 0)
                              ?? StartTimes.FirstOrDefault()
                            : StartTimes.FirstOrDefault();
            Info = "";
        }

        private void RefreshGates()
        {
            Offers.Clear();
            // a bay is needed wherever the coach calls, not just at the two ends
            var where = new List<Station> { _origin };
            if (Stops != null) where.AddRange(Stops);
            where.Add(_destination);
            foreach (Station a in where.Distinct())
            {
                if (!StationHelpers.HasFreeGates(a, GameObject.GetInstance().HumanCarrier, TerminalKind))
                {
                    if (RouteCreator.NeedsContract(a, TerminalKind))
                    {
                        Offers.Add(new GateOffer(a, RefreshGates, TerminalKind));
                    }
                    else
                    {
                        Info = "All your gates at " + a.Profile.Name + " are in use. Free a gate or extend your contract there first.";
                    }
                }
            }

            OnPropertyChanged(nameof(HasOffers));
        }

        private void AssignExisting()
        {
            if (Coach == null)
            {
                Info = "Pick a coach first.";
                return;
            }

            var request = new NewRouteRequest
            {
                Coach = Coach,
                StartCoach = _startCoach,
                Operation = _operation,
                TurnaroundMinutes = _turnaround,
                RoundTripsPerDay = _trips < 1 ? 1 : _trips,
                StartTime = _startTime == null ? new TimeSpan(6, 0, 0) : TimeSpan.Parse(_startTime),
                Stops = Stops.ToList()
            };

            string message;
            if (!(_move ? RouteCreator.MoveRoute(_existing, request, out message) : RouteCreator.AssignCoach(_existing, request, out message)))
            {
                Info = message;
                return;
            }

            _done(message);
        }

        private void Create()
        {
            RefreshGates();
            if (Offers.Count > 0)
            {
                Info = "Lease the missing gates first (see above).";
                return;
            }

            if (_existing != null)
            {
                AssignExisting();
                return;
            }

            double cargoPrice = 0;
            if (_cargo
                && (!double.TryParse(
                        (_cargoPrice ?? "").Replace(',', '.'),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out cargoPrice)
                    || cargoPrice <= 0))
            {
                Info = "The price per unit must be a number above 0 (for example 8.50).";
                return;
            }

            var request = new NewRouteRequest
            {
                RouteType = Route.RouteType.Passenger,
                CargoPrice = cargoPrice,
                Origin = _origin,
                Destination = _destination,
                Tier = _tier,
                Coach = Coach,
                StartCoach = _startCoach,
                Operation = _operation,
                TurnaroundMinutes = _turnaround,
                RoundTripsPerDay = _trips < 1 ? 1 : _trips,
                StartTime = _startTime == null ? new TimeSpan(6, 0, 0) : TimeSpan.Parse(_startTime),
                Stops = Stops.ToList()
            };

            string message;
            var route = RouteCreator.Create(request, out message);
            if (route == null)
            {
                Info = message;
                return;
            }

            _done(message);
        }
    }
}
