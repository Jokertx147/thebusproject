using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;

namespace TheBusProject.Presentation
{
    public class OverviewRow
    {
        public FleetCoach Coach { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Cabin { get; set; }
        public string Status { get; set; }
        public string Homebase { get; set; }
        public string Ownership { get; set; }
        public string Age { get; set; }
        public string Condition { get; set; }
        public int Routes { get; set; }
        public bool IsIdle { get; set; }
        public bool IsOnOrder { get; set; }
        public string Crew { get; set; }
        public string Value { get; set; }
        public double ValueNumber { get; set; }
        public int AgeNumber { get; set; }
        public double ConditionNumber { get; set; }
    }

    public class OverviewRouteLine
    {
        public string Name { get; set; }
        public string Balance { get; set; }
        public string Filling { get; set; }
    }

    /// <summary>The list of the coach the carrier owns, with details and the actions to start, stop, refit and sell them.</summary>
    public class FleetOverviewViewModel : ObservableObject
    {
        public static readonly string[] SortNames = { "Name", "Type", "Age", "Condition", "Value", "Routes" };

        private readonly Action _changed;
        private readonly Action<FleetCoach> _refit;
        private readonly Action<FleetCoach> _schedule;
        private string _search = "", _sortBy = "Name", _status, _pendingText, _signature;
        private OverviewRow _selected;
        private Action _pending;

        public FleetOverviewViewModel(Action changed, Action<FleetCoach> refit, Action<FleetCoach> schedule = null)
        {
            _changed = changed;
            _refit = refit;
            _schedule = schedule;
            ScheduleCommand = new RelayCommand(() => { if (_selected != null) _schedule?.Invoke(_selected.Coach); });
            Rows = new ObservableCollection<OverviewRow>();
            Details = new ObservableCollection<string>();
            RouteLines = new ObservableCollection<OverviewRouteLine>();
            FerryTargets = new ObservableCollection<NamedChoice<Station>>();
            FerryCommand = new RelayCommand(AskFerry);
            StartCommand = new RelayCommand(() => Do(f => Status = TryStart(f)));
            StopCommand = new RelayCommand(() => Do(f => { f.Status = FleetCoach.CoachStatus.Stopped; Status = f.Name + " stopped."; }));
            CabinCommand = new RelayCommand(() => { if (_selected != null) _refit?.Invoke(_selected.Coach); });
            SellCommand = new RelayCommand(AskSell);
            ConfirmCommand = new RelayCommand(() => { var p = _pending; ClearPending(); p?.Invoke(); });
            CancelCommand = new RelayCommand(ClearPending);
            Reload();
        }

        public ObservableCollection<OverviewRow> Rows { get; }
        public ObservableCollection<string> Details { get; }
        public ObservableCollection<OverviewRouteLine> RouteLines { get; }
        public string[] Sorts => SortNames;

        public ObservableCollection<NamedChoice<Station>> FerryTargets { get; }
        public ICommand FerryCommand { get; }
        private NamedChoice<Station> _ferryTarget;
        public string FerryHint { get; private set; }

        public NamedChoice<Station> FerryTarget
        {
            get { return _ferryTarget; }
            set { if (SetProperty(ref _ferryTarget, value)) UpdateFerryHint(); }
        }

        private void LoadFerryTargets()
        {
            var keep = _ferryTarget?.Value;
            FerryTargets.Clear();
            var carrier = GameObject.GetInstance().HumanCarrier;
            var f = _selected?.Coach;
            if (carrier != null && f?.CurrentPosition != null)
            {
                var set = new List<Station>(carrier.Stations);
                if (f.Homebase != null && !set.Contains(f.Homebase)) set.Add(f.Homebase);
                foreach (var a in set.Where(a => a != f.CurrentPosition).OrderBy(a => MathHelpers.GetDistance(f.CurrentPosition, a)))
                {
                    FerryTargets.Add(new NamedChoice<Station> { Name = a.Profile.IATACode + " " + a.Profile.Name + " (" + DistanceFormatter.Km(MathHelpers.GetDistance(f.CurrentPosition, a)) + ")", Value = a });
                }
            }
            _ferryTarget = FerryTargets.FirstOrDefault(c => c.Value == keep) ?? FerryTargets.FirstOrDefault();
            OnPropertyChanged(nameof(FerryTarget));
            UpdateFerryHint();
        }

        private void UpdateFerryHint()
        {
            var f = _selected?.Coach;
            string hint = null;
            if (f != null)
            {
                string blocker = FerryService.GetManualBlocker(f);
                if (blocker != null) hint = "Cannot ferry: " + blocker + ".";
                else if (_ferryTarget != null)
                {
                    var plan = FerryService.Plan(f, f.CurrentPosition, _ferryTarget.Value);
                    hint = plan == null
                        ? "Cannot ferry there: " + FerryService.GetBlocker(f, f.CurrentPosition, _ferryTarget.Value) + "."
                        : string.Format("{0} stop(s), {1}, about {2:0.0} hours, fuel about {3}.{4}", plan.Count, DistanceFormatter.Km(FerryService.PlanKm(f.CurrentPosition, plan)), FerryService.PlanHours(f, f.CurrentPosition, plan), Money(FerryService.PlanCost(f, f.CurrentPosition, plan)),
                            plan.Count > 1 ? " Via " + string.Join(", ", plan.Take(plan.Count - 1).Select(a => a.Profile.IATACode)) + "." : "");
                }
                else hint = "You have no other station to ferry to.";
            }
            FerryHint = hint;
            OnPropertyChanged(nameof(FerryHint));
        }

        private void AskFerry()
        {
            var f = _selected?.Coach;
            var to = _ferryTarget?.Value;
            if (f == null || to == null) return;
            string blocker = FerryService.GetManualBlocker(f);
            if (blocker != null)
            {
                Status = "Cannot ferry " + f.Name + ": " + blocker + ".";
                return;
            }
            var plan = FerryService.Plan(f, f.CurrentPosition, to);
            if (plan == null)
            {
                Status = "Cannot ferry there: " + FerryService.GetBlocker(f, f.CurrentPosition, to) + ".";
                return;
            }
            PendingText = string.Format("Ferry {0} from {1} to {2}? {3}", f.Name, f.CurrentPosition.Profile.IATACode, to.Profile.IATACode, FerryHint);
            _pending = () =>
            {
                string m;
                FerryService.Start(f, to, out m);
                Status = m;
                _changed?.Invoke();
                Reload();
            };
        }

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand CabinCommand { get; }
        public ICommand ScheduleCommand { get; }
        public bool CanSchedule => _selected != null && _selected.Coach.Coach.BuiltDate <= GameObject.GetInstance().GameTime;
        public ICommand SellCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }

        public string Search
        {
            get { return _search; }
            set { if (SetProperty(ref _search, value ?? "")) Reload(); }
        }

        public string SortBy
        {
            get { return _sortBy; }
            set { if (value != null && SetProperty(ref _sortBy, value)) Reload(); }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        public string PendingText
        {
            get { return _pendingText; }
            private set { if (SetProperty(ref _pendingText, value)) OnPropertyChanged(nameof(HasPending)); }
        }

        public bool HasPending => _pendingText != null;
        public bool HasSelection => _selected != null;
        public bool HasRows => Rows.Count > 0;
        public string Title { get; private set; }

        public bool CanStart => _selected != null && _selected.Coach.Status == FleetCoach.CoachStatus.Stopped
                                && _selected.Coach.Coach.BuiltDate <= GameObject.GetInstance().GameTime;
        public bool CanStop => _selected != null && _selected.Coach.Status != FleetCoach.CoachStatus.Stopped
                               && _selected.Coach.Coach.BuiltDate <= GameObject.GetInstance().GameTime;
        public bool CanCabin => _selected != null && _selected.Coach.Status == FleetCoach.CoachStatus.Stopped
                                && _selected.Coach.Coach.Type is CoachPassengerType;
        public bool CanSell => _selected != null;

        /// <summary>An idle coach scheduled to fly from another station gets a ferry trip there first (fuel cost, takes time).</summary>
        public bool AutoFerry
        {
            get { return GameObject.GetInstance().HumanCarrier?.AutoFerry ?? true; }
            set
            {
                var carrier = GameObject.GetInstance().HumanCarrier;
                if (carrier == null || carrier.AutoFerry == value) return;
                carrier.AutoFerry = value;
                OnPropertyChanged(nameof(AutoFerry));
                Status = value ? "Automatic ferry trips are on." : "Automatic ferry trips are off: coach just start from the first station of their schedule.";
            }
        }
        public string SellText { get; private set; } = "Sell";

        public OverviewRow Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    ClearPending();
                    LoadDetails();
                }
            }
        }

        /// <summary>Starts an coach; returns what happened as a sentence.</summary>
        public static string TryStart(FleetCoach f)
        {
            if (f.Coach.BuiltDate > GameObject.GetInstance().GameTime)
            {
                return f.Name + " has not been delivered yet (" + DateFormatter.Date(f.Coach.BuiltDate) + ").";
            }
            if (f.Routes.Count == 0)
            {
                return f.Name + " has no route. Create a route for it first.";
            }
            if (f.Drivers.Count < f.Coach.Type.DriverCrew)
            {
                return f.Name + " needs " + f.Coach.Type.DriverCrew + " drivers (it has " + f.Drivers.Count + "). Hire them under Staff > Drivers.";
            }
            f.Status = GameObject.GetInstance().DayRoundEnabled
                ? FleetCoach.CoachStatus.OnRoute
                : FleetCoach.CoachStatus.ToRouteStart;
            return f.Name + " is running.";
        }

        private void Do(Action<FleetCoach> action)
        {
            if (_selected == null) return;
            action(_selected.Coach);
            _changed?.Invoke();
        }

        private void ClearPending()
        {
            _pending = null;
            PendingText = null;
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        private void AskSell()
        {
            if (_selected == null) return;
            var f = _selected.Coach;
            if (CoachDealService.IsLeasedOut(f))
            {
                Status = f.Name + " is leased out. End the lease under Coach deals first.";
                return;
            }
            if (f.Status != FleetCoach.CoachStatus.Stopped)
            {
                Status = "Stop " + f.Name + " first: a running coach can't be sold.";
                return;
            }

            if (f.Purchased == FleetCoach.PurchasedType.Bought)
            {
                long price = f.Coach.GetPrice();
                PendingText = "Sell " + f.Name + " (" + f.Coach.Type.Name + ") for " + Money(price) + "? Its routes are removed.";
                _pending = () => Sell(f, price);
            }
            else if (f.Purchased == FleetCoach.PurchasedType.Leased)
            {
                PendingText = "Return the leased " + f.Name + " (" + f.Coach.Type.Name + ") to the lessor? Its routes are removed.";
                _pending = () => Sell(f, 0);
            }
            else
            {
                PendingText = "Give up " + f.Name + " (" + f.Coach.Type.Name + ")? It is not paid in full, so nothing is paid back. Its routes are removed.";
                _pending = () => Sell(f, 0);
            }
        }

        private void Sell(FleetCoach f, long price)
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            if (f.Status != FleetCoach.CoachStatus.Stopped || !carrier.Fleet.Contains(f))
            {
                return;
            }

            foreach (var route in f.Routes.ToList())
            {
                f.RemoveRoute(route);
            }

            carrier.RemoveCoach(f);
            if (price > 0)
            {
                CarrierHelpers.AddCarrierInvoice(carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, price);
            }

            foreach (var driver in f.Drivers.ToList())
            {
                f.RemoveDriver(driver);
            }

            Status = price > 0 ? f.Name + " sold for " + Money(price) + "." : f.Name + " is gone.";
            _selected = null;
            _signature = null;
            _changed?.Invoke();
        }

        /// <summary>Read the fleet again (cheap when nothing changed).</summary>
        public void Reload()
        {
            var game = GameObject.GetInstance();
            var carrier = game?.HumanCarrier;
            if (carrier == null) return;

            var keep = _selected?.Coach;
            var fleet = carrier.Fleet.ToList();
            string term = _search.Trim().ToLowerInvariant();

            var rows = fleet.Select(f => MakeRow(f, game)).Where(
                r => term.Length == 0 || (r.Name + " " + r.Type + " " + r.Homebase).ToLowerInvariant().Contains(term)).ToList();

            switch (_sortBy)
            {
                case "Type": rows = rows.OrderBy(r => r.Type).ThenBy(r => r.Name).ToList(); break;
                case "Age": rows = rows.OrderBy(r => r.AgeNumber).ThenBy(r => r.Name).ToList(); break;
                case "Condition": rows = rows.OrderBy(r => r.ConditionNumber).ThenBy(r => r.Name).ToList(); break;
                case "Value": rows = rows.OrderByDescending(r => r.ValueNumber).ThenBy(r => r.Name).ToList(); break;
                case "Routes": rows = rows.OrderByDescending(r => r.Routes).ThenBy(r => r.Name).ToList(); break;
                default: rows = rows.OrderBy(r => r.Name).ToList(); break;
            }

            string signature = string.Join(";", rows.Select(r => r.Name + r.Status + r.Routes + r.Crew + r.Cabin + r.Condition + r.Value));
            if (signature == _signature) return;
            _signature = signature;

            Rows.Clear();
            foreach (var r in rows) Rows.Add(r);
            _selected = Rows.FirstOrDefault(r => r.Coach == keep);
            OnPropertyChanged(nameof(Selected));
            OnPropertyChanged(nameof(HasRows));
            int idle = rows.Count(r => r.IsIdle);
            Title = fleet.Count + " coaches, total value " + Money(rows.Sum(r => r.ValueNumber)) + (idle > 0 ? "  |  " + idle + " idle (no route)" : "") + (rows.Count(r => r.IsOnOrder) > 0 ? "  |  " + rows.Count(r => r.IsOnOrder) + " awaiting delivery" : "");
            OnPropertyChanged(nameof(Title));
            LoadDetails();
        }

        private static OverviewRow MakeRow(FleetCoach f, GameObject game)
        {
            var a = f.Coach;
            string ownership;
            switch (f.Purchased)
            {
                case FleetCoach.PurchasedType.Leased: ownership = "Leased, " + Money(a.GetLeasingPrice()) + " / month"; break;
                case FleetCoach.PurchasedType.BoughtDownPayment:
                    ownership = f.RemainingPayment > 0 ? "Down payment, " + Money(f.RemainingPayment) + " to pay" : "Down payment";
                    break;
                default: ownership = "Bought"; break;
            }

            long value = a.GetPrice();
            return new OverviewRow
            {
                Coach = f,
                Name = f.Name,
                Type = a.Type.Name,
                Cabin = a.CabinConfiguration,
                Status = a.BuiltDate > game.GameTime ? "Delivery " + DateFormatter.Date(a.BuiltDate) : FerryService.IsFerrying(f) ? "Ferry to " + f.FerryTo.Profile.IATACode : CoachDealService.IsLeasedOut(f) ? "Leased to " + f.LeaseOutTo.Profile.IATACode : CoachDealService.IsListed(f) ? "For sale" : CharterService.IsOnCharter(f) ? "On charter" : f.Status.ToString(),
                Homebase = f.Homebase?.Profile.IATACode,
                Ownership = ownership,
                Age = a.Age + " y",
                AgeNumber = a.Age,
                Condition = string.Format("{0:0}%", a.Condition),
                ConditionNumber = a.Condition,
                Routes = f.Routes.Count,
                IsIdle = f.Routes.Count == 0 && a.BuiltDate <= game.GameTime && !CoachDealService.IsLeasedOut(f) && !CharterService.IsOnCharter(f),
                IsOnOrder = a.BuiltDate > game.GameTime,
                Crew = f.Drivers.Count + "/" + a.Type.DriverCrew,
                Value = Money(value),
                ValueNumber = value
            };
        }

        private void LoadDetails()
        {
            Details.Clear();
            RouteLines.Clear();
            var r = _selected;
            if (r != null)
            {
                var f = r.Coach;
                var a = f.Coach;
                Details.Add("Position: " + FerryService.Diagnose(f));
                Details.Add("Registration: " + a.TailNumber + (a.Registered != null ? " (" + a.Registered.Name + ")" : ""));
                Details.Add("Type: " + a.Type.Manufacturer?.Name + " " + a.Type.Name);
                Details.Add("Built: " + DateFormatter.Date(a.BuiltDate) + ", age " + a.Age + " years");
                Details.Add("Acquired: " + DateFormatter.Date(f.PurchasedDate) + " (" + r.Ownership + ")");
                Details.Add("Homebase: " + f.Homebase?.Profile.Name);
                Details.Add("Condition: " + r.Condition + ", run " + DistanceFormatter.Km(a.Flown) + ", " + DistanceFormatter.Km(a.Flown - a.LastServiceCheck) + " since the last service");
                Details.Add("Range: " + DistanceFormatter.Km(a.Range) + ", cruising speed " + DistanceFormatter.Speed(a.CruisingSpeed)
                            + ", bay " + DistanceFormatter.Metres(a.MinBayLength));
                if (a.EngineType != null) Details.Add("Engine: " + EngineTypes.FullName(a.EngineType) + (EngineTypes.Describe(a.EngineType) == "" ? "" : " (" + EngineTypes.Describe(a.EngineType) + ")"));
                Details.Add("Drivers: " + r.Crew);
                Details.Add("Value: " + r.Value);

                foreach (var c in a.Classes.OrderBy(c => (int) c.Type).ToList())
                {
                    if (c.SeatingCapacity <= 0 && true) continue;
                    string facilities = string.Join(", ", c.GetFacilities().Select(x => x.Name));
                    Details.Add(c.Type + ": " + c.SeatingCapacity + " seats" + (facilities.Length > 0 ? " (" + facilities + ")" : ""));
                }

                var money = new ValueCurrencyFormatter();
                foreach (var route in f.Routes.ToList())
                {
                    // this used to print the route's whole life as a fraction, which on a route whose first coach
                    // is still on the road is nought over nought: "NaN% full". It now says so, and otherwise shows
                    // the month, which is what every other screen means by full.
                    string filling;
                    try
                    {
                        filling = ConnectionService.HasCarried(route)
                                      ? string.Format("{0:0}% full", ConnectionService.MonthLoad(route)*100)
                                      : "no trips finished yet";
                    }
                    catch (Exception) { filling = "-"; }
                    RouteLines.Add(
                        new OverviewRouteLine
                        {
                            Name = route.Destination1.Profile.IATACode + " - " + route.Destination2.Profile.IATACode,
                            Balance = "profit " + Convert.ToString(money.Convert(route.Balance)),
                            Filling = filling
                        });
                }
            }

            LoadFerryTargets();
            SellText = r == null ? "Sell" : r.Coach.Purchased == FleetCoach.PurchasedType.Bought ? "Sell coach" : "Return coach";
            OnPropertyChanged(nameof(SellText));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(CanCabin));
            OnPropertyChanged(nameof(CanSchedule));
            OnPropertyChanged(nameof(CanSell));
        }
    }
}
