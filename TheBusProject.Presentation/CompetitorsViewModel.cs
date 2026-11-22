using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.Routes;

namespace TheBusProject.Presentation
{
    public class CompetitorRow
    {
        public string Title { get; set; }
        public string Line1 { get; set; }
        public string Line2 { get; set; }

        /// <summary>A short flag shown before the title (empty for none).</summary>
        public string Badge { get; set; }

        public bool HasBadge => !string.IsNullOrEmpty(Badge);

        /// <summary>A warning (a rival undercuts the player) rather than plain information.</summary>
        public bool IsWarning { get; set; }

        public Route MyRoute { get; set; }
        public Func<List<string>> Detail { get; set; }
    }

    /// <summary>Carriers > Competitors: who flies the player's routes and stations, their fares and loads, and their recent moves.</summary>
    public class CompetitorsViewModel : ObservableObject
    {
        public const string ByCarrier = "Rival carriers", ByRoute = "My routes", ByStation = "My stations", ByMove = "Recent moves";

        private readonly Action<Route> _open;
        private string _show = ByCarrier, _filter, _summary, _detailTitle;
        private CompetitorRow _selected;

        public CompetitorsViewModel(Action<Route> open)
        {
            _open = open;
            LoadFilters();
            OpenCommand = new RelayCommand(() => { if (_selected?.MyRoute != null) _open?.Invoke(_selected.MyRoute); });
            RefreshCommand = new RelayCommand(Reload);
        }

        public List<string> ShowChoices { get; } = new List<string> {ByCarrier, ByRoute, ByStation, ByMove};

        public ObservableCollection<string> FilterChoices { get; } = new ObservableCollection<string> {"All"};
        public ObservableCollection<CompetitorRow> Rows { get; } = new ObservableCollection<CompetitorRow>();
        public ObservableCollection<string> DetailLines { get; } = new ObservableCollection<string>();
        public ICommand OpenCommand { get; }
        public ICommand RefreshCommand { get; }

        public string Show
        {
            get => _show;
            set
            {
                if (SetProperty(ref _show, value))
                {
                    LoadFilters();
                    Reload();
                }
            }
        }

        public string Filter
        {
            get => _filter;
            set
            {
                if (value != null && SetProperty(ref _filter, value)) Reload();
            }
        }

        public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
        public string DetailTitle { get => _detailTitle; private set => SetProperty(ref _detailTitle, value); }
        public bool HasSelection => _selected != null;
        public bool CanOpenRoute => _selected?.MyRoute != null;

        public CompetitorRow Selected
        {
            get => _selected;
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    OnPropertyChanged(nameof(HasSelection));
                    OnPropertyChanged(nameof(CanOpenRoute));
                    LoadDetail();
                }
            }
        }

        /// <summary>Shows "My routes" with the player's route on that station pair selected (from a rival alert in the news). False when the player no longer flies it.</summary>
        public bool ShowRoute(string a, string b, bool cargo)
        {
            if (_show != ByRoute)
            {
                _show = ByRoute;
                OnPropertyChanged(nameof(Show));
                LoadFilters();
            }
            else if (_filter != FilterChoices[0])
            {
                _filter = FilterChoices[0];
                OnPropertyChanged(nameof(Filter));
            }
            Reload();
            CompetitorRow row = Rows.FirstOrDefault(r => r.MyRoute != null && (r.MyRoute.Type == Route.RouteType.Cargo) == cargo && OnPair(r.MyRoute, a, b))
                                ?? Rows.FirstOrDefault(r => r.MyRoute != null && OnPair(r.MyRoute, a, b));
            Selected = row;
            return row != null;
        }

        /// <summary>Shows "Rival carriers" with that carrier selected (from the monthly report).</summary>
        public bool ShowCarrier(Carrier carrier)
        {
            if (_show != ByCarrier)
            {
                _show = ByCarrier;
                OnPropertyChanged(nameof(Show));
                LoadFilters();
            }
            else if (_filter != FilterChoices[0])
            {
                _filter = FilterChoices[0];
                OnPropertyChanged(nameof(Filter));
            }
            Reload();
            CompetitorRow row = Rows.FirstOrDefault(r => r.Title == Name(carrier));
            Selected = row;
            return row != null;
        }

        private static bool OnPair(Route r, string a, string b)
        {
            string x = r.Destination1?.Profile.IATACode, y = r.Destination2?.Profile.IATACode;
            return (x == a && y == b) || (x == b && y == a);
        }

        private void LoadFilters()
        {
            FilterChoices.Clear();
            switch (_show)
            {
                case ByCarrier:
                    foreach (var f in new[] {"All", "On my routes", "At my stations only"}) FilterChoices.Add(f);
                    break;
                case ByRoute:
                    foreach (var f in new[] {"All", "With rivals", "A rival is cheaper", "No rivals"}) FilterChoices.Add(f);
                    break;
                case ByStation:
                    FilterChoices.Add("All");
                    break;
                default:
                    foreach (var f in new[] {"Last 30 days", "Last 90 days", "Last 180 days", "On my routes only"}) FilterChoices.Add(f);
                    break;
            }
            _filter = FilterChoices[0];
            OnPropertyChanged(nameof(Filter));
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        private static string N(int n, string one, string many = null)
        {
            return n + " " + (n == 1 ? one : many ?? one + "s");
        }

        private static string Pct(double v)
        {
            return (v*100).ToString("0") + "%";
        }

        private static string Code(Station a)
        {
            return a?.Profile.IATACode ?? "?";
        }

        private static string Pair(Route r)
        {
            return Code(r.Destination1) + " - " + Code(r.Destination2);
        }

        private static string Pair(Station a, Station b)
        {
            return Code(a) + " - " + Code(b);
        }

        private static string Name(Carrier a)
        {
            return a == null ? "?" : a.Profile.IATACode + " " + a.Profile.Name;
        }

        private static string Load(double load)
        {
            return load > 0 ? Pct(load) + " full" : "load not known yet";
        }

        private static string FareWord(Route r)
        {
            return r.Type == Route.RouteType.Cargo ? "rate" : "fare";
        }

        public void Reload()
        {
            var me = GameObject.GetInstance()?.HumanCarrier;
            if (me == null) return;
            var keep = _selected?.Title;
            Rows.Clear();
            try
            {
                switch (_show)
                {
                    case ByCarrier: LoadCarriers(me); break;
                    case ByRoute: LoadRoutes(me); break;
                    case ByStation: LoadStations(me); break;
                    default: LoadMoves(me); break;
                }
            }
            catch (InvalidOperationException)
            {
                Summary = "The game changed while reading; press Refresh.";
            }
            _selected = null;
            Selected = Rows.FirstOrDefault(r => r.Title == keep) ?? Rows.FirstOrDefault();
            OnPropertyChanged(nameof(Selected));
        }

        // ---- rival carriers

        private void LoadCarriers(Carrier me)
        {
            List<RivalCarrier> rivals = CompetitorService.Rivals(me);
            if (_filter == "On my routes") rivals = rivals.Where(r => r.SharedRoutes.Count > 0).ToList();
            if (_filter == "At my stations only") rivals = rivals.Where(r => r.SharedRoutes.Count == 0).ToList();
            foreach (RivalCarrier r in rivals)
            {
                RivalCarrier rival = r;
                string meet = (r.SharedRoutes.Count > 0 ? r.SharedRoutes.Count + " of your routes, " : "") + r.SharedStations.Count + " of your stations";
                var line2 = new List<string>();
                if (r.FareLevel > 0) line2.Add("fares " + r.FareLevel.ToString("0.00") + " of the market");
                if (r.FareAgainstMine > 0) line2.Add(r.FareAgainstMine < 1 ? Pct(1 - r.FareAgainstMine) + " cheaper than you where you meet" : Pct(r.FareAgainstMine - 1) + " dearer than you where you meet");
                if (r.Load > 0) line2.Add(Pct(r.Load) + " full");
                if (r.LastMonthIncome > 0) line2.Add("last month " + Money(r.LastMonthResult) + " (" + Pct(r.LastMonthResult/r.LastMonthIncome) + " margin)");
                Rows.Add(new CompetitorRow
                {
                    Title = Name(r.Carrier),
                    Line1 = meet + "  |  " + r.Fleet + " coach, " + N(r.Routes, "route") + ", " + r.Carrier.Mentality.ToString().ToLowerInvariant() + " style",
                    Line2 = string.Join("  |  ", line2),
                    Badge = r.FareAgainstMine > 0 && r.FareAgainstMine < 0.97 ? "CHEAPER" : null,
                    IsWarning = true,
                    Detail = () => CarrierDetail(me, rival)
                });
            }
            int onRoutes = rivals.Count(r => r.SharedRoutes.Count > 0);
            Summary = rivals.Count == 0
                ? "No other carrier runs your routes or from your stations."
                : string.Format("{0} from your stations; {1} of them also {2} your routes.", rivals.Count == 1 ? "1 carrier runs" : rivals.Count + " carriers run", onRoutes, onRoutes == 1 ? "runs" : "run");
        }

        private List<string> CarrierDetail(Carrier me, RivalCarrier r)
        {
            var lines = new List<string>();
            if (r.SharedRoutes.Count > 0)
            {
                lines.Add("Your routes they run too:");
                foreach (RouteRivals s in r.SharedRoutes.OrderBy(x => Pair(x.Mine)))
                {
                    foreach (RivalOnRoute x in s.Rivals.Where(x => x.Carrier == r.Carrier))
                    {
                        lines.Add(string.Format("  {0}: them {1} ({2}, {3} a week, {4})  |  you {5} ({6}, {7} a week)",
                            Pair(s.Mine), Money(x.Fare), Load(x.Load), x.WeeklyTrips, x.Coach, Money(s.MyFare), Load(s.MyLoad), s.MyWeeklyTrips));
                    }
                }
            }
            if (r.SharedStations.Count > 0)
            {
                lines.Add("Your stations they run from:");
                foreach (Station ap in r.SharedStations.OrderBy(Code))
                {
                    int routes = RouteIndex.At(ap).Count(x => x.Carrier == r.Carrier && x.HasCoach);
                    int gates = 0;
                    try { gates = ap.GetCarrierContracts(r.Carrier).Sum(c => c.NumberOfGates); } catch (Exception) { }
                    lines.Add(string.Format("  {0} {1}: {2}, {3}", Code(ap), ap.Profile.Town?.Name, N(routes, "route"), N(gates, "gate")));
                }
            }
            var moves = CompetitorService.Moves(me, 180).Where(m => m.Carrier == r.Carrier).Take(30).ToList();
            lines.Add(moves.Count == 0 ? "No moves at your stations in the last 180 days." : "Their moves at your stations:");
            lines.AddRange(moves.Select(m => "  " + MoveText(m, me)));
            return lines;
        }

        // ---- my routes

        private void LoadRoutes(Carrier me)
        {
            List<RouteRivals> all = CompetitorService.ForRoutes(me);
            IEnumerable<RouteRivals> shown = all;
            switch (_filter)
            {
                case "With rivals": shown = all.Where(r => r.Rivals.Count > 0); break;
                case "A rival is cheaper": shown = all.Where(Undercut); break;
                case "No rivals": shown = all.Where(r => r.Rivals.Count == 0); break;
            }
            foreach (RouteRivals r in shown.OrderByDescending(r => r.Rivals.Count).ThenBy(r => Pair(r.Mine)))
            {
                RouteRivals rr = r;
                string word = FareWord(r.Mine);
                Rows.Add(new CompetitorRow
                {
                    Title = Pair(r.Mine) + (r.Mine.Type == Route.RouteType.Cargo ? "  (cargo)" : ""),
                    Line1 = r.Rivals.Count == 0
                        ? "No rival runs it"
                        : r.Rivals.Count + (r.Rivals.Count == 1 ? " rival: " : " rivals: ") + string.Join(", ", r.Rivals.Take(4).Select(x => x.Carrier.Profile.IATACode + " " + Money(x.Fare) + " (" + (x.Load > 0 ? Pct(x.Load) : "?") + ")")) + (r.Rivals.Count > 4 ? " ..." : ""),
                    Line2 = "you " + Money(r.MyFare) + " " + word + " (" + Load(r.MyLoad) + ", " + N(r.MyWeeklyTrips, "trip") + " a week)" + (r.MarketFare > 0 ? "  |  market " + Money(r.MarketFare) : ""),
                    Badge = Undercut(r) ? "CHEAPER RIVAL" : r.Rivals.Count == 0 ? "ALONE" : null,
                    IsWarning = Undercut(r),
                    MyRoute = r.Mine,
                    Detail = () => RouteDetail(me, rr)
                });
            }
            int with = all.Count(r => r.Rivals.Count > 0), under = all.Count(Undercut);
            Summary = all.Count == 0
                ? "You have no routes yet."
                : string.Format("{0}: {1} with rivals, {2} where a rival is cheaper than you, {3} you run alone.", N(all.Count, "route"), with, under, all.Count - with);
        }

        private static bool Undercut(RouteRivals r)
        {
            return r.MyFare > 0 && r.Rivals.Any(x => x.Fare > 0 && x.Fare < r.MyFare*0.97);
        }

        private List<string> RouteDetail(Carrier me, RouteRivals r)
        {
            string word = FareWord(r.Mine);
            var lines = new List<string>
            {
                string.Format("You: {0} {1}, {2}, {3} a week ({4})", Money(r.MyFare), word, Load(r.MyLoad), N(r.MyWeeklyTrips, "trip"), r.Mine.GetCoaches().Count == 0 ? "no coach yet" : string.Join(", ", r.Mine.GetCoaches().Select(a => a.Coach.Type.Name).Distinct()))
            };
            if (r.MarketFare > 0) lines.Add("Market fare: " + Money(r.MarketFare));
            if (r.Rivals.Count == 0) lines.Add("No other carrier runs this pair.");
            foreach (RivalOnRoute x in r.Rivals)
            {
                string against = r.MyFare > 0 && x.Fare > 0 ? (x.Fare < r.MyFare ? ", " + Pct(1 - x.Fare/r.MyFare) + " cheaper than you" : x.Fare > r.MyFare ? ", " + Pct(x.Fare/r.MyFare - 1) + " dearer than you" : ", the same as you") : "";
                lines.Add(string.Format("{0}: {1} {2}{3}, {4}, {5} a week ({6})", Name(x.Carrier), Money(x.Fare), word, against, Load(x.Load), N(x.WeeklyTrips, "trip"), x.Coach));
            }
            int total = r.MyWeeklyTrips + r.Rivals.Sum(x => x.WeeklyTrips);
            if (r.Rivals.Count > 0 && total > 0) lines.Add("Your share of the trips on the pair: " + Pct((double) r.MyWeeklyTrips/total));
            var moves = CompetitorService.Moves(me, 180).Where(m => SamePair(m, r.Mine)).ToList();
            if (moves.Count > 0)
            {
                lines.Add("Rivals' moves on this pair:");
                lines.AddRange(moves.Select(m => "  " + MoveText(m, me)));
            }
            return lines;
        }

        private static bool SamePair(RivalMove m, Route r)
        {
            return (m.A == r.Destination1 && m.B == r.Destination2) || (m.A == r.Destination2 && m.B == r.Destination1);
        }

        // ---- my stations

        private void LoadStations(Carrier me)
        {
            List<StationRivals> all = CompetitorService.ForStations(me).OrderByDescending(a => a.Carriers.Count).ThenBy(a => Code(a.Station)).ToList();
            foreach (StationRivals a in all)
            {
                StationRivals ar = a;
                int routes = a.Carriers.Sum(x => x.Item2);
                Rows.Add(new CompetitorRow
                {
                    Title = Code(a.Station) + "  " + a.Station.Profile.Name,
                    Line1 = string.Format("you: {0}, {1}  |  {2} with {3}  |  {4}, {5} free", N(a.MyRoutes, "route"), N(a.MyGates, "gate"), N(a.Carriers.Count, "other carrier"), N(routes, "route"), N(a.Gates, "gate"), a.FreeGates),
                    Line2 = a.Carriers.Count == 0 ? "No other carrier runs from here" : "biggest: " + string.Join(", ", a.Carriers.Take(4).Select(x => x.Item1.Profile.IATACode + " " + N(x.Item2, "route"))),
                    Badge = a.Carriers.Count == 0 ? "ALONE" : a.FreeGates == 0 ? "NO FREE GATES" : null,
                    IsWarning = a.FreeGates == 0 && a.Carriers.Count > 0,
                    Detail = () => StationDetail(me, ar)
                });
            }
            Summary = all.Count == 0
                ? "You run from no station yet."
                : string.Format("{0}: {1} where other carriers run too, {2} with no free gates left.", N(all.Count, "station"), all.Count(a => a.Carriers.Count > 0), all.Count(a => a.FreeGates == 0));
        }

        private List<string> StationDetail(Carrier me, StationRivals a)
        {
            var lines = new List<string>
            {
                a.Station.Profile.Name + ", " + a.Station.Profile.Town?.Name + ", " + a.Station.Profile.Country?.Name,
                string.Format("You: {0}, {1}. Gates at the station: {2} ({3} free).", N(a.MyRoutes, "route"), N(a.MyGates, "gate"), a.Gates, a.FreeGates)
            };
            if (a.Carriers.Count == 0) lines.Add("No other carrier runs from here.");
            else lines.Add("Other carriers here:");
            foreach (var x in a.Carriers) lines.Add(string.Format("  {0}: {1}, {2}", Name(x.Item1), N(x.Item2, "route"), N(x.Item3, "gate")));
            var moves = CompetitorService.Moves(me, 180).Where(m => m.A == a.Station || m.B == a.Station).Take(30).ToList();
            if (moves.Count > 0)
            {
                lines.Add("Rivals' moves here:");
                lines.AddRange(moves.Select(m => "  " + MoveText(m, me)));
            }
            return lines;
        }

        // ---- recent moves

        private void LoadMoves(Carrier me)
        {
            int days = _filter == "Last 30 days" ? 30 : _filter == "Last 90 days" ? 90 : 180;
            var myPairs = new HashSet<string>(me.Routes.ToList().Select(r => Key(r.Destination1, r.Destination2)));
            List<RivalMove> moves = CompetitorService.Moves(me, days);
            var myStations = new HashSet<Station>(CompetitorService.MyStations(me));
            if (_filter == "On my routes only") moves = moves.Where(m => myPairs.Contains(Key(m.A, m.B))).ToList();
            foreach (RivalMove m in moves.Take(500))
            {
                RivalMove move = m;
                bool onMine = myPairs.Contains(Key(m.A, m.B));
                Rows.Add(new CompetitorRow
                {
                    Title = DateFormatter.Date(m.Date) + "  " + MoveText(m, me, false),
                    Line1 = onMine ? "You run " + Pair(m.A, m.B) + " too" : "At your station " + string.Join(" and ", new[] {m.A, m.B}.Where(myStations.Contains).Select(Code)),
                    Badge = onMine ? "YOUR ROUTE" : null,
                    IsWarning = onMine && (m.Kind == RivalMoveKind.Opened || m.Kind == RivalMoveKind.FareCut),
                    MyRoute = onMine ? me.Routes.ToList().FirstOrDefault(r => Key(r.Destination1, r.Destination2) == Key(m.A, m.B) && r.IsCargoRoute == m.IsCargo) : null,
                    Detail = () => MoveDetail(me, move)
                });
            }
            Summary = string.Format("{0} by rivals at your stations in the last {1} days: {2} routes opened, {3} given up, {4} fare cuts, {5} fare rises. (Routes given up and fare changes are counted from when the game was started or loaded.)",
                N(moves.Count, "move"), days, moves.Count(m => m.Kind == RivalMoveKind.Opened), moves.Count(m => m.Kind == RivalMoveKind.Dropped),
                moves.Count(m => m.Kind == RivalMoveKind.FareCut), moves.Count(m => m.Kind == RivalMoveKind.FareRise));
        }

        private static string Key(Station a, Station b)
        {
            string x = Code(a), y = Code(b);
            return string.CompareOrdinal(x, y) < 0 ? x + y : y + x;
        }

        private string MoveText(RivalMove m, Carrier me, bool withDate = true)
        {
            string who = m.Carrier?.Profile.IATACode ?? "?";
            string pair = Pair(m.A, m.B) + (m.IsCargo ? " (cargo)" : "");
            string what;
            switch (m.Kind)
            {
                case RivalMoveKind.Opened: what = who + " opened " + pair; break;
                case RivalMoveKind.Dropped: what = who + " gave up " + pair; break;
                case RivalMoveKind.FareCut: what = who + " cut " + (m.IsCargo ? "its rate" : "its fares") + " on " + pair + " by " + Pct(1 - m.Factor); break;
                default: what = who + " raised " + (m.IsCargo ? "its rate" : "its fares") + " on " + pair + " by " + Pct(m.Factor - 1); break;
            }
            return withDate ? DateFormatter.Date(m.Date) + ": " + what : what;
        }

        private List<string> MoveDetail(Carrier me, RivalMove m)
        {
            var lines = new List<string> {MoveText(m, me), Name(m.Carrier)};
            Route mine = me.Routes.ToList().FirstOrDefault(r => Key(r.Destination1, r.Destination2) == Key(m.A, m.B) && r.IsCargoRoute == m.IsCargo);
            if (mine != null)
            {
                var rr = CompetitorService.ForRoutes(me).FirstOrDefault(x => x.Mine == mine);
                if (rr != null) lines.AddRange(RouteDetail(me, rr));
            }
            else
            {
                Route theirs = RouteIndex.Direct(m.A, m.B).FirstOrDefault(r => r.Carrier == m.Carrier);
                if (theirs != null)
                {
                    lines.Add(string.Format("They run it {0} times a week ({1}) at {2}.", theirs.TimeTable.Entries.Count, string.Join(", ", theirs.GetCoaches().Select(a => a.Coach.Type.Name).Distinct()), Money(theirs is CargoRoute c ? c.PricePerUnit : theirs is PassengerRoute p ? AIPricingService.EconomyFare(p) : 0)));
                }
                lines.Add("You don't run this pair.");
            }
            return lines;
        }

        private void LoadDetail()
        {
            DetailLines.Clear();
            DetailTitle = _selected?.Title;
            if (_selected?.Detail == null) return;
            try
            {
                foreach (string line in _selected.Detail()) DetailLines.Add(line);
            }
            catch (InvalidOperationException)
            {
                DetailLines.Add("The game changed while reading; press Refresh.");
            }
        }
    }
}
