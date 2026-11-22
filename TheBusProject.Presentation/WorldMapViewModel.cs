using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Helpers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Statistics;
using TheBusProject.Models.Routes;

namespace TheBusProject.Presentation
{
    /// <summary>A place on the map. U and V run from 0 to 1 across the (Web Mercator) world image.</summary>
    public class MapPlace
    {
        public double U { get; set; }
        public double V { get; set; }
        public string Label { get; set; }
        public bool IsHub { get; set; }
        public Station Station { get; set; }

        /// <summary>An station only a shown rival flies to (drawn smaller).</summary>
        public bool IsRivalOnly { get; set; }
    }

    /// <summary>A line on the map, split in pieces where it crosses the edge of the picture.</summary>
    public class MapLine
    {
        public List<List<double[]>> Pieces { get; set; } = new List<List<double[]>>();
        public bool IsCargo { get; set; }

        /// <summary>A rival's colour ("#rrggbb"); null for the player's own routes.</summary>
        public string Color { get; set; }
    }

    /// <summary>A line of the trip board: one of the selected coach's trips today.</summary>
    public class TripBoardRow
    {
        public string Departs { get; set; }
        public string Trip { get; set; }
        public string Route { get; set; }
        public string Arrives { get; set; }
        public string Status { get; set; }
        public string Result { get; set; }
        public bool IsAirborne { get; set; }
        public bool IsDone { get; set; }
        public bool IsCancelled { get; set; }
        public bool IsLoss { get; set; }
        public bool HasResult => !string.IsNullOrEmpty(Result);

        public string Key => Departs + Trip + Status + Result;
    }

    /// <summary>What the map shows of the other carriers.</summary>
    public class RivalView
    {
        public static readonly RivalView None = new RivalView();

        /// <summary>One carrier (all its routes and coach).</summary>
        public Carrier Carrier { get; set; }

        /// <summary>Every carrier flying from this station (their routes there and the coach on them).</summary>
        public Station Station { get; set; }

        /// <summary>The carriers that fly the player's routes (those routes, and the coach on them).</summary>
        public bool OnMyRoutes { get; set; }

        public string Key => Carrier != null ? "a:" + Carrier.Profile.IATACode + Carrier.Profile.Name : Station != null ? "p:" + Station.Profile.IATACode : OnMyRoutes ? "mine" : "none";
    }

    /// <summary>A line of the map's legend.</summary>
    public class MapLegendItem
    {
        public string Color { get; set; }
        public string Text { get; set; }
    }

    public class MapCoach
    {
        public FleetCoach Coach { get; set; }

        /// <summary>A rival's colour; null for the player's own coach.</summary>
        public string Color { get; set; }

        public bool IsMine => Color == null;

        /// <summary>The route of the current (or next) trip.</summary>
        public Route TripRoute { get; set; }
        public double U { get; set; }
        public double V { get; set; }
        /// <summary>Direction of travel in degrees on the picture, 0 = up (north), clockwise.</summary>
        public double Heading { get; set; }
        public bool InTrip { get; set; }
        public string Name { get; set; }
        public string Info { get; set; }

        /// <summary>Where the map last drew the coach (it moves between reads of the game); U and V until drawn.</summary>
        public double ScreenU
        {
            get { return double.IsNaN(_drawnU) ? U : _drawnU; }
            set { _drawnU = value; }
        }

        public double ScreenV
        {
            get { return double.IsNaN(_drawnV) ? V : _drawnV; }
            set { _drawnV = value; }
        }

        private double _drawnU = double.NaN, _drawnV = double.NaN;

        // the current (or next) trip, so the map can move the coach between two reads of the game
        public bool HasTrip { get; set; }
        public double Lat1 { get; set; }
        public double Lon1 { get; set; }
        public double Lat2 { get; set; }
        public double Lon2 { get; set; }
        public DateTime Takeoff { get; set; }
        public DateTime Landing { get; set; }

        private MapLine _path;

        /// <summary>The share of the trip flown at a game time (below 0 before takeoff, above 1 after landing).</summary>
        public double Fraction(DateTime t)
        {
            if (!HasTrip || Landing <= Takeoff) return InTrip ? 0.5 : 0;
            return (t - Takeoff).TotalSeconds/(Landing - Takeoff).TotalSeconds;
        }

        /// <summary>Where the coach is at a game time: on its trip's great circle while flying, else at the station it is at.</summary>
        public bool At(DateTime t, out double u, out double v, out double heading)
        {
            heading = Heading;
            if (!HasTrip)
            {
                u = U;
                v = V;
                return InTrip;
            }

            double fr = Fraction(t);
            if (fr <= 0 || fr >= 1)
            {
                double[] p = fr <= 0 ? WorldMapViewModel.Project(Lat1, Lon1) : WorldMapViewModel.Project(Lat2, Lon2);
                u = p[0];
                v = p[1];
                return false;
            }

            double[] here = WorldMapViewModel.OnTrip(this, fr), ahead = WorldMapViewModel.OnTrip(this, Math.Min(1, fr + 0.01));
            u = here[0];
            v = here[1];
            double du = ahead[0] - here[0];
            if (du > 0.5) du -= 1; else if (du < -0.5) du += 1;
            if (Math.Abs(du) + Math.Abs(ahead[1] - here[1]) > 1e-12) heading = Math.Atan2(du, -(ahead[1] - here[1]))*180/Math.PI;
            return true;
        }

        /// <summary>The path flown in the last `back` share of the trip up to a game time (for a trail behind the coach).</summary>
        public List<double[]> Trail(DateTime t, double back, int steps = 8)
        {
            var points = new List<double[]>();
            double fr = Fraction(t);
            if (!HasTrip || fr <= 0 || fr >= 1) return points;
            double from = Math.Max(0, fr - back);
            for (int i = 0; i <= steps; i++) points.Add(WorldMapViewModel.OnTrip(this, from + (fr - from)*i/steps));
            return points;
        }

        /// <summary>The whole trip, from departure to destination.</summary>
        public MapLine Path => HasTrip ? _path ?? (_path = WorldMapViewModel.MakeLine(Lat1, Lon1, Lat2, Lon2, false)) : null;
    }

    /// <summary>
    ///     The game time to show on the map between two reads of the game: it runs on at the speed the game is running,
    ///     so coach glide instead of jumping every half second, and it stops when the game is paused.
    /// </summary>
    public class MapClock
    {
        private readonly List<Tuple<double, DateTime>> _samples = new List<Tuple<double, DateTime>>();
        private DateTime _shown;
        private double _shownAt = double.NaN;
        private bool _paused = true;

        /// <summary>Game seconds per real second, from the last few seconds.</summary>
        public double Rate { get; private set; }

        public DateTime Latest { get; private set; }

        public bool Running => !_paused && Rate > 0;

        /// <summary>A read of the game: its time now, `wall` real seconds since some fixed moment.</summary>
        public void Sample(double wall, DateTime game, bool paused)
        {
            if (_samples.Count > 0 && game < _samples[_samples.Count - 1].Item2) _samples.Clear(); // a game was loaded
            _samples.Add(Tuple.Create(wall, game));
            while (_samples.Count > 2 && wall - _samples[0].Item1 > 3) _samples.RemoveAt(0);
            _paused = paused;
            Latest = game;
            var first = _samples[0];
            double dw = wall - first.Item1;
            Rate = paused || dw <= 0 ? 0 : Math.Max(0, (game - first.Item2).TotalSeconds/dw);
            if (_shown == default(DateTime) || game < _shown.AddHours(-12) || game > _shown.AddDays(2))
            {
                _shown = game; // the start, a loaded game or a fast-forward: no gliding across
                _shownAt = wall;
            }
        }

        /// <summary>The game time to draw at real time `wall`: it never runs backwards, and never far ahead of the game.</summary>
        public DateTime Now(double wall)
        {
            if (_samples.Count == 0) return Latest;
            if (double.IsNaN(_shownAt)) _shownAt = wall;
            double dt = Math.Max(0, Math.Min(0.5, wall - _shownAt));
            _shownAt = wall;
            if (!Running)
            {
                // paused: settle on the game's time
                if (_shown < Latest) _shown = Latest;
                return _shown;
            }

            var last = _samples[_samples.Count - 1];
            double ahead = Math.Min(1.0, Math.Max(0, wall - last.Item1));
            DateTime target = last.Item2.AddSeconds(Rate*ahead);
            // run at the game's speed, catching up (or slowing down) smoothly towards where the game should be
            double gap = (target - _shown).TotalSeconds;
            double step = Rate*dt + gap*Math.Min(1, 3*dt);
            if (Math.Abs(gap) > Rate*4 + 3600) step = gap; // too far off: jump
            if (step > 0) _shown = _shown.AddSeconds(step);
            return _shown;
        }
    }

    /// <summary>A world map with the stations, routes and coach of the carrier.</summary>
    public class WorldMapViewModel : ObservableObject
    {
        private bool _showRoutes = true, _showStations = true, _showCoach = true;
        private MapCoach _selected;
        private string _caption = "";

        public List<MapPlace> Places { get; private set; } = new List<MapPlace>();
        public List<MapLine> Lines { get; private set; } = new List<MapLine>();
        public List<MapCoach> Coach { get; private set; } = new List<MapCoach>();

        /// <summary>Colours for rival carriers, in order (none of them the player's blue, the cargo orange or the coach yellow).</summary>
        public static readonly string[] RivalColors = {"#d62728", "#2ca02c", "#9467bd", "#e377c2", "#17becf", "#8c564b", "#bcbd22", "#3d3d3d"};

        /// <summary>The choices of rivals to show; rebuilt once a game day, keeping the choice.</summary>
        public System.Collections.ObjectModel.ObservableCollection<NamedChoice<RivalView>> RivalChoices { get; } =
            new System.Collections.ObjectModel.ObservableCollection<NamedChoice<RivalView>>();

        /// <summary>Who's who on the map: the player and each rival shown, with their colour.</summary>
        public System.Collections.ObjectModel.ObservableCollection<MapLegendItem> Legend { get; } = new System.Collections.ObjectModel.ObservableCollection<MapLegendItem>();

        public bool HasLegend => Legend.Count > 1;

        private NamedChoice<RivalView> _rival;
        private Carrier _game;
        private DateTime _choicesDay;
        private bool _loadingChoices;

        public NamedChoice<RivalView> SelectedRival
        {
            get { return _rival; }
            set
            {
                if (value == null || _loadingChoices) return;
                if (SetProperty(ref _rival, value)) Reload();
            }
        }

        /// <summary>The game time the map draws, running on smoothly between reads of the game.</summary>
        public MapClock Clock { get; } = new MapClock();

        private static readonly System.Diagnostics.Stopwatch Wall = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>Real seconds for the clock.</summary>
        public static double WallSeconds => Wall.Elapsed.TotalSeconds;

        private bool _animate = true;

        /// <summary>Coach glide along their routes (instead of moving every half second) with a short trail behind them.</summary>
        public bool Animate
        {
            get { return _animate; }
            set { if (SetProperty(ref _animate, value)) Changed?.Invoke(); }
        }

        /// <summary>Raised after Reload so the view can redraw.</summary>
        public event Action Changed;

        public bool ShowRoutes
        {
            get { return _showRoutes; }
            set { if (SetProperty(ref _showRoutes, value)) Changed?.Invoke(); }
        }

        public bool ShowStations
        {
            get { return _showStations; }
            set { if (SetProperty(ref _showStations, value)) Changed?.Invoke(); }
        }

        public bool ShowCoach
        {
            get { return _showCoach; }
            set { if (SetProperty(ref _showCoach, value)) Changed?.Invoke(); }
        }

        public MapCoach Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    if (value == null) Follow = false;
                    LoadBoard(Clock.Latest == default(DateTime) ? GameObject.GetInstance().GameTime : Clock.Latest);
                    SyncPick();
                    OnPropertyChanged(nameof(SelectedText));
                    OnPropertyChanged(nameof(HasSelection));
                    Changed?.Invoke();
                }
            }
        }

        public bool HasSelection => _selected != null;

        /// <summary>The selected coach's trips today, in order, with their status by the game clock.</summary>
        public System.Collections.ObjectModel.ObservableCollection<TripBoardRow> TripBoard { get; } = new System.Collections.ObjectModel.ObservableCollection<TripBoardRow>();

        private string _boardTitle, _boardSummary;

        public string BoardTitle { get => _boardTitle; private set => SetProperty(ref _boardTitle, value); }
        public string BoardSummary { get => _boardSummary; private set => SetProperty(ref _boardSummary, value); }
        public bool HasBoardTrips => TripBoard.Count > 0;

        private static string Money(double v)
        {
            return (v < 0 ? "-$" : "+$") + Math.Abs(Math.Round(v)).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string HourOf(DateTime t, DateTime day)
        {
            string s = t.ToString("HH:mm");
            if (t.Date > day) s += " +1";
            else if (t.Date < day) s += " -1";
            return s;
        }

        /// <summary>Builds the trip board of the selected coach at game time `now`.</summary>
        public void LoadBoard(DateTime now)
        {
            var rows = new List<TripBoardRow>();
            FleetCoach f = _selected?.Coach;
            if (f == null)
            {
                BoardTitle = null;
                BoardSummary = null;
            }
            else
            {
                DateTime day = now.Date;
                List<FlownTrip> flown = TripRecords.Of(f);
                List<RouteTimeTableEntry> entries;
                try
                {
                    var season = GeneralHelpers.GetSeason(now);
                    entries = f.Routes.ToList()
                               .Where(r => r.Season == Models.General.Environment.Weather.Season.AllYear || r.Season == season)
                               .SelectMany(r => r.TimeTable.Entries.ToList())
                               .Where(e => e != null && e.Coach == f && e.Day == now.DayOfWeek)
                               .OrderBy(e => e.Time).ToList();
                }
                catch (InvalidOperationException)
                {
                    entries = new List<RouteTimeTableEntry>();
                }

                foreach (RouteTimeTableEntry e in entries)
                {
                    Station from = e.DepartureStation, to = e.Destination?.Station;
                    if (from == null || to == null) continue;
                    DateTime dep = day + e.Time;
                    DateTime arr = dep + MathHelpers.GetTripTime(from, to, f.Coach.Type);
                    // what really happened to it today (delays, passengers, profit)
                    FlownTrip rec = flown.LastOrDefault(x => x.Entry == e && Math.Abs((x.Takeoff - dep).TotalHours) < 12);
                    if (rec != null && !rec.Cancelled)
                    {
                        dep = rec.Takeoff;
                        arr = rec.Landing;
                    }

                    var row = new TripBoardRow
                    {
                        Departs = HourOf(day + e.Time, day), Trip = e.Destination.TripCode, Route = from.Profile.IATACode + " - " + to.Profile.IATACode, Arrives = HourOf(arr, day)
                    };
                    string carried = rec == null ? null : rec.IsCargo ? Math.Round(rec.Cargo) + " cargo" : rec.Passengers + "/" + rec.Seats + " passengers";
                    if (rec != null && rec.Cancelled)
                    {
                        row.Status = "Cancelled" + (rec.CancelReason != null ? " (" + rec.CancelReason + ")" : "");
                        row.IsCancelled = true;
                    }
                    else if (now < dep)
                    {
                        row.Status = rec != null && rec.DelayMinutes > 0 ? "Delayed " + rec.DelayMinutes + " min" : "Scheduled";
                    }
                    else if (now < arr)
                    {
                        row.Status = "In the air";
                        row.IsAirborne = true;
                        row.Result = carried;
                    }
                    else
                    {
                        row.Status = rec != null && rec.DelayMinutes > 0 ? "Landed, " + rec.DelayMinutes + " min late" : "Landed";
                        row.IsDone = true;
                        if (rec != null)
                        {
                            row.Result = carried + ", " + Money(rec.Profit);
                            row.IsLoss = rec.Profit < 0;
                        }
                    }
                    rows.Add(row);
                }

                BoardTitle = f.Name + " (" + f.Coach.Type.Name + ")" + (f.Coach.Carrier != null && !f.Coach.Carrier.IsHuman ? ", " + f.Coach.Carrier.Profile.Name : "") + ": trips " + now.ToString("dddd d MMMM");

                // today so far, and the last 7 days, from the trips that have landed
                var landed = flown.Where(x => !x.Cancelled && x.Landing <= now).ToList();
                var today = landed.Where(x => x.Landing.Date == day).ToList();
                var week = landed.Where(x => x.Landing > now.AddDays(-7)).ToList();
                var parts = new List<string>();
                if (rows.Count == 0) parts.Add("No trips today");
                if (today.Count > 0) parts.Add(string.Format("today so far {0} trip{1}, {2}", today.Count, today.Count == 1 ? "" : "s", Money(today.Sum(x => x.Profit))));
                if (week.Count > 0)
                {
                    var pax = week.Where(x => !x.IsCargo && x.Seats > 0).ToList();
                    string load = pax.Count > 0 ? ", " + (100.0*pax.Sum(x => x.Passengers)/pax.Sum(x => x.Seats)).ToString("0") + "% full" : "";
                    parts.Add(string.Format("last 7 days {0} trips{1}, {2}", week.Count, load, Money(week.Sum(x => x.Profit))));
                }
                int cancelled = flown.Count(x => x.Cancelled && x.Takeoff > now.AddDays(-7));
                if (cancelled > 0) parts.Add(cancelled + " cancelled");
                if (flown.Count == 0 && rows.Count > 0) parts.Add("results show as trips land (from when the game was started or loaded)");
                BoardSummary = string.Join("  |  ", parts) + (parts.Count > 0 ? "." : "") + " Profit is the trip's income less its own costs, before the coach's ownership.";
            }

            if (rows.Count != TripBoard.Count || rows.Where((r, i) => r.Key != TripBoard[i].Key).Any())
            {
                TripBoard.Clear();
                foreach (var r in rows) TripBoard.Add(r);
                OnPropertyChanged(nameof(HasBoardTrips));
            }
        }

        public string SelectedText => _selected == null
            ? "Click a coach on the map, or pick one of yours in the list, for details. Follow keeps it in the middle of the map."
            : (_follow ? "Following " : "") + _selected.Name + "  |  " + _selected.Info;

        private bool _follow;

        /// <summary>The map keeps the selected coach in the middle as it flies (moving the map by hand stops it).</summary>
        public bool Follow
        {
            get { return _follow; }
            set
            {
                if (value && _selected == null) value = false;
                if (SetProperty(ref _follow, value))
                {
                    OnPropertyChanged(nameof(SelectedText));
                    Changed?.Invoke();
                }
            }
        }

        /// <summary>The player's coach to pick from, by name (rebuilt when the fleet changes).</summary>
        public System.Collections.ObjectModel.ObservableCollection<NamedChoice<FleetCoach>> CoachChoices { get; } =
            new System.Collections.ObjectModel.ObservableCollection<NamedChoice<FleetCoach>>();

        private NamedChoice<FleetCoach> _pick;
        private bool _syncing;

        /// <summary>An coach picked in the list: selected and followed.</summary>
        public NamedChoice<FleetCoach> PickedCoach
        {
            get { return _pick; }
            set
            {
                if (!SetProperty(ref _pick, value) || _syncing || value == null) return;
                var a = Coach.FirstOrDefault(x => x.Coach == value.Value);
                if (a == null) return; // not delivered yet
                Selected = a;
                Follow = true;
            }
        }

        // the list shows the selected coach (or nothing for a rival's)
        private void SyncPick()
        {
            _syncing = true;
            var match = _selected == null ? null : CoachChoices.FirstOrDefault(c => c.Value == _selected.Coach);
            if (!ReferenceEquals(match, _pick))
            {
                _pick = match;
                OnPropertyChanged(nameof(PickedCoach));
            }
            _syncing = false;
        }

        private void LoadCoachChoices(List<FleetCoach> fleet, DateTime now)
        {
            var mine = fleet.Where(f => f.Coach.BuiltDate <= now).OrderBy(f => f.Name).ToList();
            if (mine.Count == CoachChoices.Count && mine.Select((f, i) => CoachChoices[i].Value == f).All(x => x)) return;
            _syncing = true;
            CoachChoices.Clear();
            foreach (var f in mine) CoachChoices.Add(new NamedChoice<FleetCoach> {Name = f.Name + " (" + f.Coach.Type.Name + ")", Value = f});
            _syncing = false;
            SyncPick();
        }

        public string Caption
        {
            get { return _caption; }
            private set { SetProperty(ref _caption, value); }
        }

        private static string Num(double v)
        {
            return v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>The text of the tooltip over an station: where it is, how big, the gates and the traffic, and what you fly from it.</summary>
        public string StationTip(MapPlace place)
        {
            var station = place?.Station;
            var game = GameObject.GetInstance();
            var carrier = game?.HumanCarrier;
            if (station == null || carrier == null)
            {
                return null;
            }

            try
            {
                var p = station.Profile;
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(p.IATACode + " / " + p.ICAOCode + "  " + p.Name + (place.IsHub ? "  (your hub)" : ""));
                sb.AppendLine(p.Town?.Name + ", " + p.Country?.Name);
                sb.AppendLine("Size " + p.Size.ToString().Replace('_', ' ') + "  |  longest runway " + DistanceFormatter.Metres(station.GetMaxRunwayLength()));

                int total = station.Terminals.GetNumberOfGates(Terminal.TerminalType.Passenger);
                int free = station.Terminals.GetFreeGates(Terminal.TerminalType.Passenger);
                int mine = station.GetCarrierContracts(carrier).Where(c => c.TerminalType == Terminal.TerminalType.Passenger).Sum(c => c.NumberOfGates);
                string gates = "Gates " + total + " (" + free + " free, " + mine + " yours)";
                int cargo = station.Terminals.GetNumberOfGates(Terminal.TerminalType.Cargo);
                if (cargo > 0)
                {
                    gates += "  |  cargo gates " + cargo + " (" + station.Terminals.GetFreeGates(Terminal.TerminalType.Cargo) + " free)";
                }
                sb.AppendLine(gates);

                int year = game.GameTime.Year;
                var pax = StatisticsTypes.GetStatisticsType("Passengers");
                var dep = StatisticsTypes.GetStatisticsType("Arrivals");
                double paxAll = station.Statistics.GetTotalValue(year, pax);
                double paxMine = station.Statistics.GetStatisticsValue(year, carrier, pax);
                double depAll = station.Statistics.GetTotalValue(year, dep);
                double depMine = station.Statistics.GetStatisticsValue(year, carrier, dep);
                sb.AppendLine("Passengers in " + year + ": " + Num(paxAll) + " (" + Num(paxMine) + " with you)");
                sb.AppendLine("Arrivals in " + year + ": " + Num(depAll) + " (" + Num(depMine) + " yours)");
                sb.AppendLine("Carriers here: " + station.CarrierContracts.Select(c => c.Carrier).Distinct().Count() + "  |  routes: " + StationHelpers.GetStationRoutes(station).Count);

                var mineRoutes = carrier.Routes.Where(r => r.Destination1 == station || r.Destination2 == station).ToList();
                if (mineRoutes.Count == 0)
                {
                    sb.Append("You run no route from here.");
                }
                else
                {
                    var dests = mineRoutes.Select(r => (r.Destination1 == station ? r.Destination2 : r.Destination1).Profile.IATACode).Distinct().ToList();
                    sb.Append("Your routes: " + mineRoutes.Count + " to " + string.Join(", ", dests.Take(8)) + (dests.Count > 8 ? " and " + (dests.Count - 8) + " more" : ""));
                }

                return sb.ToString();
            }
            catch (Exception)
            {
                return place.Label;
            }
        }

        /// <summary>The text of the tooltip over an coach.</summary>
        public string CoachTip(MapCoach a)
        {
            if (a == null)
            {
                return null;
            }

            try
            {
                var f = a.Coach;
                return a.Name + Environment.NewLine + a.Info + Environment.NewLine
                       + string.Format(
                           "Condition {0:0}%  |  pilots {1}/{2}  |  {3} route(s)", f.Coach.Condition, f.Pilots.Count, f.Coach.Type.CockpitCrew, f.Routes.Count)
                       + Environment.NewLine + "Speed " + DistanceFormatter.Speed(f.Coach.Type.CruisingSpeed) + "  |  range " + DistanceFormatter.Km(f.Coach.Type.Range);
            }
            catch (Exception)
            {
                return a.Name + Environment.NewLine + a.Info;
            }
        }

        public static double MercatorV(double latitude)
        {
            double lat = Math.Max(-85.05, Math.Min(85.05, latitude)) * Math.PI / 180;
            return (1 - Math.Log(Math.Tan(lat) + 1 / Math.Cos(lat)) / Math.PI) / 2;
        }

        public static double MercatorU(double longitude)
        {
            return (longitude + 180) / 360;
        }

        public static double[] Project(double lat, double lon)
        {
            return new[] { MercatorU(lon), MercatorV(lat) };
        }

        private static void Coords(Station a, out double lat, out double lon)
        {
            var g = a.Profile.Coordinates.ConvertToGeoCoordinate();
            lat = g.Latitude;
            lon = g.Longitude;
        }

        //a point on the great circle between two places, f from 0 to 1
        private static void Between(double lat1, double lon1, double lat2, double lon2, double f, out double lat, out double lon)
        {
            double p1 = lat1 * Math.PI / 180, l1 = lon1 * Math.PI / 180, p2 = lat2 * Math.PI / 180, l2 = lon2 * Math.PI / 180;
            double d = 2 * Math.Asin(Math.Sqrt(
                Math.Pow(Math.Sin((p2 - p1) / 2), 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Pow(Math.Sin((l2 - l1) / 2), 2)));
            if (d < 1e-9)
            {
                lat = lat1;
                lon = lon1;
                return;
            }

            double a = Math.Sin((1 - f) * d) / Math.Sin(d), b = Math.Sin(f * d) / Math.Sin(d);
            double x = a * Math.Cos(p1) * Math.Cos(l1) + b * Math.Cos(p2) * Math.Cos(l2);
            double y = a * Math.Cos(p1) * Math.Sin(l1) + b * Math.Cos(p2) * Math.Sin(l2);
            double z = a * Math.Sin(p1) + b * Math.Sin(p2);
            lat = Math.Atan2(z, Math.Sqrt(x * x + y * y)) * 180 / Math.PI;
            lon = Math.Atan2(y, x) * 180 / Math.PI;
        }

        /// <summary>The point a share `fr` of the way along the coach's trip.</summary>
        public static double[] OnTrip(MapCoach a, double fr)
        {
            double lat, lon;
            Between(a.Lat1, a.Lon1, a.Lat2, a.Lon2, fr, out lat, out lon);
            return Project(lat, lon);
        }

        private static MapLine MakeLine(Station a, Station b, bool cargo)
        {
            double la1, lo1, la2, lo2;
            Coords(a, out la1, out lo1);
            Coords(b, out la2, out lo2);
            return MakeLine(la1, lo1, la2, lo2, cargo);
        }

        public static MapLine MakeLine(double la1, double lo1, double la2, double lo2, bool cargo)
        {
            var line = new MapLine { IsCargo = cargo };
            var piece = new List<double[]>();
            double lastU = double.NaN;
            const int steps = 48;
            for (int i = 0; i <= steps; i++)
            {
                double lat, lon;
                Between(la1, lo1, la2, lo2, i / (double) steps, out lat, out lon);
                double[] p = Project(lat, lon);
                if (!double.IsNaN(lastU) && Math.Abs(p[0] - lastU) > 0.5)
                {
                    line.Pieces.Add(piece);
                    piece = new List<double[]>();
                }

                piece.Add(p);
                lastU = p[0];
            }

            line.Pieces.Add(piece);
            return line;
        }

        private static string Status(FleetCoach f)
        {
            return f.Status.ToString().Replace("To", "To ").Replace("On", "On ").Trim();
        }

        public static MapCoach Place(FleetCoach f, DateTime now)
        {
            var m = PlaceOnly(f, now, out Route route);
            if (m != null) m.TripRoute = route;
            return m;
        }

        private static MapCoach PlaceOnly(FleetCoach f, DateTime now, out Route route)
        {
            double lat, lon, heading = 0;
            bool flying = false;
            string info = Status(f);
            Trip trip = f.CurrentTrip;
            Station at = f.CurrentPosition ?? f.Homebase;
            route = null;

            try
            {
                if (trip != null && trip.Entry != null && f.Status != FleetCoach.CoachStatus.Stopped)
                {
                    route = trip.Entry.TimeTable?.Route;
                    Station from = trip.Entry.DepartureStation, to = trip.Entry.Destination.Station;
                    DateTime takeoff = trip.TripTime, landing = trip.GetExpectedLandingTime();
                    double la1, lo1, la2, lo2;
                    Coords(from, out la1, out lo1);
                    Coords(to, out la2, out lo2);
                    if (now >= takeoff && now < landing && landing > takeoff)
                    {
                        double fr = (now - takeoff).TotalSeconds / (landing - takeoff).TotalSeconds;
                        Between(la1, lo1, la2, lo2, fr, out lat, out lon);
                        double la3, lo3;
                        Between(la1, lo1, la2, lo2, Math.Min(1, fr + 0.01), out la3, out lo3);
                        double[] p0 = Project(lat, lon), p1 = Project(la3, lo3);
                        double du = p1[0] - p0[0];
                        if (du > 0.5) du -= 1; else if (du < -0.5) du += 1;
                        heading = Math.Atan2(du, -(p1[1] - p0[1])) * 180 / Math.PI;
                        flying = true;
                        info = "running " + from.Profile.IATACode + " - " + to.Profile.IATACode + (trip.Entry.Destination.TripCode != null ? " (" + trip.Entry.Destination.TripCode + ")" : "")
                               + ", " + (int) (fr * 100) + "% of the way, lands " + DateFormatter.DateTime(landing) + OnBoard(trip);
                        double[] pos = Project(lat, lon);
                        return WithTrip(Make(f, pos, heading, true, info), la1, lo1, la2, lo2, takeoff, landing);
                    }

                    if (now < takeoff && from != null)
                    {
                        at = from;
                        info = "at " + at.Profile.IATACode + ", departs " + DateFormatter.DateTime(takeoff) + " to " + to.Profile.IATACode;
                        // about to take off: the map can start it moving before the next read of the game
                        if (landing > takeoff) return WithTrip(Make(f, Project(la1, lo1), 0, false, info), la1, lo1, la2, lo2, takeoff, landing);
                    }
                    else if (now >= landing)
                    {
                        at = to;
                        info = "landed at " + at.Profile.IATACode;
                    }
                }
                else
                {
                    info = (f.Status == FleetCoach.CoachStatus.Stopped ? "stopped" : Status(f)) + " at " + at?.Profile.IATACode;
                }
            }
            catch (NullReferenceException)
            {
            }

            if (at == null)
            {
                return null;
            }

            Coords(at, out lat, out lon);
            return Make(f, Project(lat, lon), heading, flying, info);
        }

        // what the trip carries, as the tooltip and the followed coach show it
        private static string OnBoard(Trip trip)
        {
            try
            {
                if (trip.IsPassengerTrip())
                {
                    int pax = trip.GetTotalPassengers();
                    return pax > 0 ? ", " + pax + " passengers" : "";
                }
                if (trip.IsCargoTrip() && trip.Cargo > 0) return ", " + Math.Round(trip.Cargo) + " units of cargo";
            }
            catch (Exception)
            {
            }
            return "";
        }

        private static MapCoach WithTrip(MapCoach a, double la1, double lo1, double la2, double lo2, DateTime takeoff, DateTime landing)
        {
            a.HasTrip = true;
            a.Lat1 = la1;
            a.Lon1 = lo1;
            a.Lat2 = la2;
            a.Lon2 = lo2;
            a.Takeoff = takeoff;
            a.Landing = landing;
            return a;
        }

        private static MapCoach Make(FleetCoach f, double[] p, double heading, bool flying, string info)
        {
            return new MapCoach
            {
                Coach = f,
                U = p[0],
                V = p[1],
                Heading = heading,
                InTrip = flying,
                Name = f.Name + " (" + f.Coach.Type.Name + ")",
                Info = info
            };
        }

        // the rivals to choose from: none, those on the player's routes, each rival carrier (closest first), and the
        // player's stations
        private void LoadRivalChoices(Carrier me, DateTime now)
        {
            _choicesDay = now.Date;
            var choices = new List<NamedChoice<RivalView>>
            {
                new NamedChoice<RivalView> {Name = "No rivals", Value = RivalView.None},
                new NamedChoice<RivalView> {Name = "Rivals on my routes", Value = new RivalView {OnMyRoutes = true}}
            };
            try
            {
                foreach (RivalCarrier r in CompetitorService.Rivals(me).Take(40))
                {
                    string where = r.SharedRoutes.Count > 0 ? r.SharedRoutes.Count + (r.SharedRoutes.Count == 1 ? " shared route" : " shared routes") : r.SharedStations.Count + (r.SharedStations.Count == 1 ? " shared station" : " shared stations");
                    choices.Add(new NamedChoice<RivalView> {Name = r.Carrier.Profile.IATACode + " " + r.Carrier.Profile.Name + " (" + where + ")", Value = new RivalView {Carrier = r.Carrier}});
                }
                foreach (Station a in CompetitorService.MyStations(me).OrderBy(a => a.Profile.IATACode))
                {
                    choices.Add(new NamedChoice<RivalView> {Name = "Everyone at " + a.Profile.IATACode + " " + a.Profile.Town?.Name, Value = new RivalView {Station = a}});
                }
            }
            catch (InvalidOperationException)
            {
            }

            string keep = _rival?.Value.Key ?? RivalView.None.Key;
            // a choice added on request (an carrier or station not in the list) stays while it is chosen
            if (_rival != null && _rival.Value != RivalView.None && !choices.Any(c => c.Value.Key == keep) && (_rival.Value.Carrier == null || Carriers.GetAllCarriers().Contains(_rival.Value.Carrier)))
                choices.Add(_rival);
            if (choices.Select(c => c.Name + c.Value.Key).SequenceEqual(RivalChoices.Select(c => c.Name + c.Value.Key))) return;
            _loadingChoices = true;
            RivalChoices.Clear();
            foreach (var c in choices) RivalChoices.Add(c);
            _loadingChoices = false;
            _rival = RivalChoices.FirstOrDefault(c => c.Value.Key == keep) ?? RivalChoices[0];
            OnPropertyChanged(nameof(SelectedRival));
        }

        /// <summary>Chooses what the map shows of the rivals (for the tests and the competitor screen).</summary>
        public void ShowRivals(RivalView view)
        {
            var me = GameObject.GetInstance()?.HumanCarrier;
            if (me == null) return;
            if (RivalChoices.Count == 0 || !ReferenceEquals(_game, me)) LoadRivalChoices(me, GameObject.GetInstance().GameTime);
            var choice = RivalChoices.FirstOrDefault(c => c.Value.Key == view.Key);
            if (choice == null && (view.Carrier != null || view.Station != null))
            {
                // an carrier that doesn't fly to the player's stations, or another station: added to the list
                choice = new NamedChoice<RivalView>
                {
                    Name = view.Carrier != null ? view.Carrier.Profile.IATACode + " " + view.Carrier.Profile.Name : "Everyone at " + view.Station.Profile.IATACode + " " + view.Station.Profile.Town?.Name,
                    Value = view
                };
                _loadingChoices = true;
                RivalChoices.Add(choice);
                _loadingChoices = false;
            }
            SelectedRival = choice ?? RivalChoices[0];
        }

        private static bool SameCompany(Carrier a, Carrier b)
        {
            if (a == null || b == null) return false;
            if (a == b) return true;
            var pa = (a as Models.Carriers.Subsidiary.SubsidiaryCarrier)?.Carrier;
            var pb = (b as Models.Carriers.Subsidiary.SubsidiaryCarrier)?.Carrier;
            return pa == b || pb == a || (pa != null && pa == pb);
        }

        private static string PairKey(Route r)
        {
            string x = r.Destination1.Profile.IATACode, y = r.Destination2.Profile.IATACode;
            return (string.CompareOrdinal(x, y) < 0 ? x + y : y + x) + r.IsCargoRoute;
        }

        // the rivals' routes, stations and coach for the choice, one colour per carrier
        private static void AddRivals(Carrier me, RivalView view, DateTime now, List<MapPlace> places, List<MapLine> lines, List<MapCoach> coach, List<MapLegendItem> legend)
        {
            if (view.Carrier == null && view.Station == null && !view.OnMyRoutes) return;

            // the rival routes to show
            List<Route> routes;
            if (view.Carrier != null)
            {
                routes = view.Carrier.Routes.ToList();
            }
            else if (view.Station != null)
            {
                routes = RouteIndex.At(view.Station).Where(r => r.Carrier != null && !SameCompany(r.Carrier, me) && r.Carrier.Routes.Contains(r)).ToList();
            }
            else
            {
                var mine = new HashSet<string>(me.Routes.ToList().Where(r => r.Destination1 != null && r.Destination2 != null).Select(PairKey));
                routes = me.Routes.ToList().SelectMany(CompetitorService.RivalRoutes).Where(r => mine.Contains(PairKey(r))).Distinct().ToList();
            }
            routes = routes.Where(r => r.Destination1?.Profile?.Coordinates != null && r.Destination2?.Profile?.Coordinates != null && r.HasCoach).ToList();

            // colours: the carriers with the most routes shown first; beyond the palette, the last colour
            var carriers = routes.GroupBy(r => r.Carrier).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
            var colors = new Dictionary<Carrier, string>();
            for (int i = 0; i < carriers.Count; i++) colors[carriers[i]] = RivalColors[Math.Min(i, RivalColors.Length - 1)];

            var known = new HashSet<Station>(places.Select(p => p.Station));
            var seen = new HashSet<string>();
            var routeSet = new HashSet<Route>(routes);
            foreach (Route r in routes)
            {
                string color = colors[r.Carrier];
                if (seen.Add(color + PairKey(r)))
                {
                    var line = MakeLine(r.Destination1, r.Destination2, r.IsCargoRoute);
                    line.Color = color;
                    lines.Add(line);
                }
                foreach (Station a in new[] {r.Destination1, r.Destination2})
                {
                    if (!known.Add(a)) continue;
                    double lat, lon;
                    Coords(a, out lat, out lon);
                    places.Add(new MapPlace {U = MercatorU(lon), V = MercatorV(lat), Label = a.Profile.IATACode, Station = a, IsRivalOnly = true});
                }
            }

            // their coach flying (or about to fly) those routes
            var counted = new Dictionary<Carrier, int>();
            foreach (FleetCoach f in routes.SelectMany(r => r.GetCoaches()).Distinct())
            {
                if (f.Coach.BuiltDate > now) continue;
                MapCoach m = Place(f, now);
                if (m == null) continue;
                // between trips (no current trip): the route it is assigned to
                if (m.TripRoute == null) m.TripRoute = f.Routes.ToList().FirstOrDefault(routeSet.Contains);
                if (m.TripRoute == null || !routeSet.Contains(m.TripRoute)) continue;
                Carrier owner = m.TripRoute.Carrier;
                m.Color = colors[owner];
                m.Name = f.Name + " (" + f.Coach.Type.Name + "), " + owner.Profile.Name;
                coach.Add(m);
                int n;
                counted.TryGetValue(owner, out n);
                counted[owner] = n + 1;
            }

            for (int i = 0; i < carriers.Count; i++)
            {
                Carrier a = carriers[i];
                int n;
                counted.TryGetValue(a, out n);
                int rc = routes.Count(r => r.Carrier == a);
                string text = a.Profile.IATACode + " " + a.Profile.Name + ": " + rc + (rc == 1 ? " route, " : " routes, ") + n + " coach";
                if (i == RivalColors.Length - 1 && carriers.Count > RivalColors.Length)
                {
                    int rest = carriers.Count - i;
                    int restRoutes = carriers.Skip(i).Sum(x => routes.Count(r => r.Carrier == x));
                    text = rest + " more carriers: " + restRoutes + " routes";
                    legend.Add(new MapLegendItem {Color = RivalColors[i], Text = text});
                    break;
                }
                legend.Add(new MapLegendItem {Color = colors[a], Text = text});
            }
        }

        /// <summary>Rebuild the markers from the game. Safe to call on every refresh.</summary>
        public void Reload()
        {
            Carrier carrier = GameObject.GetInstance()?.HumanCarrier;
            if (carrier == null)
            {
                return;
            }

            DateTime now = GameObject.GetInstance().GameTime;
            // another game (loaded or started): the choices and the selection were the old game's objects
            if (!ReferenceEquals(_game, carrier))
            {
                _game = carrier;
                _choicesDay = default(DateTime);
                _loadingChoices = true;
                RivalChoices.Clear();
                _loadingChoices = false;
                _selected = null;
                _follow = false;
                OnPropertyChanged(nameof(Follow));
                OnPropertyChanged(nameof(Selected));
            }
            FleetCoach keep = _selected?.Coach;
            bool paused = true;
            try { paused = TheBusProject.Helpers.Workers.GameObjectWorker.GetInstance().IsPaused; } catch (Exception) { }
            Clock.Sample(WallSeconds, now, paused);

            List<FleetCoach> fleet;
            List<Route> routes;
            List<Station> stations;
            try
            {
                fleet = carrier.Fleet.ToList();
                routes = carrier.Routes.ToList();
                stations = carrier.Stations.ToList();
            }
            catch (InvalidOperationException)
            {
                return; // the game thread changed a list; try again on the next refresh
            }

            var hubs = new HashSet<Station>();
            try { hubs = new HashSet<Station>(carrier.GetHubs()); } catch (Exception) { }
            foreach (FleetCoach f in fleet.Where(f => f.Homebase != null)) hubs.Add(f.Homebase);

            var places = new List<MapPlace>();
            foreach (Station a in stations.Union(routes.SelectMany(r => new[] { r.Destination1, r.Destination2 })).Distinct())
            {
                if (a?.Profile?.Coordinates == null) continue;
                double lat, lon;
                Coords(a, out lat, out lon);
                places.Add(new MapPlace { U = MercatorU(lon), V = MercatorV(lat), Label = a.Profile.IATACode, IsHub = hubs.Contains(a), Station = a });
            }

            var lines = new List<MapLine>();
            var seen = new HashSet<string>();
            foreach (Route r in routes)
            {
                if (r.Destination1?.Profile?.Coordinates == null || r.Destination2?.Profile?.Coordinates == null) continue;
                string key = string.CompareOrdinal(r.Destination1.Profile.IATACode, r.Destination2.Profile.IATACode) < 0
                    ? r.Destination1.Profile.IATACode + r.Destination2.Profile.IATACode + r.IsCargoRoute
                    : r.Destination2.Profile.IATACode + r.Destination1.Profile.IATACode + r.IsCargoRoute;
                if (seen.Add(key)) lines.Add(MakeLine(r.Destination1, r.Destination2, r.IsCargoRoute));
            }

            var coach = fleet.Where(f => f.Coach.BuiltDate <= now).Select(f => Place(f, now)).Where(a => a != null).ToList();

            if (_choicesDay != now.Date || RivalChoices.Count == 0) LoadRivalChoices(carrier, now);
            var legend = new List<MapLegendItem> {new MapLegendItem {Color = "#145ac8", Text = carrier.Profile.IATACode + " " + carrier.Profile.Name + " (you)"}};
            try
            {
                AddRivals(carrier, _rival?.Value ?? RivalView.None, now, places, lines, coach, legend);
            }
            catch (InvalidOperationException)
            {
                // the game thread changed a list; the rivals come back on the next refresh
            }

            if (Legend.Count != legend.Count || Legend.Zip(legend, (x, y) => x.Text != y.Text || x.Color != y.Color).Any(d => d))
            {
                Legend.Clear();
                foreach (var l in legend) Legend.Add(l);
                OnPropertyChanged(nameof(HasLegend));
            }

            Places = places;
            Lines = lines;
            Coach = coach;
            // the same coach stays selected (and followed); one that left the map (sold, or a rival no longer shown) doesn't
            _selected = keep == null ? null : coach.FirstOrDefault(a => a.Coach == keep);
            if (_selected == null && _follow)
            {
                _follow = false;
                OnPropertyChanged(nameof(Follow));
            }
            LoadCoachChoices(fleet, now);
            SyncPick();
            LoadBoard(now);
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedText));
            int mine = coach.Count(a => a.IsMine);
            Caption = string.Format("{0} coach ({1} in the air), {2} routes, {3} stations", mine, coach.Count(a => a.IsMine && a.InTrip), lines.Count(l => l.Color == null), places.Count(p => !p.IsRivalOnly))
                      + (lines.Any(l => l.Color != null) ? string.Format("  |  rivals shown: {0} coach ({1} in the air), {2} routes", coach.Count - mine, coach.Count(a => !a.IsMine && a.InTrip), lines.Count(l => l.Color != null)) : "");
            Changed?.Invoke();
        }
    }
}
