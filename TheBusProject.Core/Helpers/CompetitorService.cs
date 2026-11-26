using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    public enum RivalMoveKind
    {
        Opened,
        Dropped,
        FareCut,
        FareRise
    }

    /// <summary>Something a rival carrier did: a route opened or given up, or a fare changed.</summary>
    public class RivalMove
    {
        public DateTime Date { get; set; }
        public Carrier Carrier { get; set; }
        public Station A { get; set; }
        public Station B { get; set; }
        public RivalMoveKind Kind { get; set; }
        public string RouteId { get; set; }

        /// <summary>The fare change as a share (0.92 = 8% cheaper); 0 for routes.</summary>
        public double Factor { get; set; }
    }

    /// <summary>A rival move as a saved game keeps it (the stations by code: they are found again on loading).</summary>
    public class SavedRivalMove
    {
        public DateTime Date;
        public Carrier Carrier;
        public string A, B;
        public int Kind;
        public string RouteId;
        public double Factor;
    }

    /// <summary>What a rival alert in the news points to: the station pair on the competitor screen.</summary>
    public class RivalLink
    {
        public string A, B, Carrier;
    }

    /// <summary>A rival's trips on one of the player's station pairs.</summary>
    public class RivalOnRoute
    {
        public Route Route { get; set; }
        public Carrier Carrier => Route.Carrier;

        /// <summary>Economy fare (passengers) or price per unit (cargo).</summary>
        public double Fare { get; set; }

        /// <summary>Share of seats filled last month (or this month so far); 0 when not known yet.</summary>
        public double Load { get; set; }

        public int WeeklyTrips { get; set; }
        public string Coach { get; set; }
    }

    /// <summary>One of the player's routes and the rivals on its station pair.</summary>
    public class RouteRivals
    {
        public Route Mine { get; set; }
        public double MyFare { get; set; }
        public double MyLoad { get; set; }
        public int MyWeeklyTrips { get; set; }
        public double MarketFare { get; set; }
        public List<RivalOnRoute> Rivals { get; set; } = new List<RivalOnRoute>();
    }

    /// <summary>A rival carrier: where it meets the player and how it is doing.</summary>
    public class RivalCarrier
    {
        public Carrier Carrier { get; set; }

        /// <summary>The player's routes it also flies (the same station pair).</summary>
        public List<RouteRivals> SharedRoutes { get; set; } = new List<RouteRivals>();

        /// <summary>The player's stations it flies from.</summary>
        public List<Station> SharedStations { get; set; } = new List<Station>();

        public int Fleet { get; set; }
        public int Routes { get; set; }

        /// <summary>Last month's operating result and income (both 0 when not known yet).</summary>
        public double LastMonthResult { get; set; }
        public double LastMonthIncome { get; set; }

        /// <summary>Its fares against the market fare on its passenger routes (1 = the market fare; 0 when unknown).</summary>
        public double FareLevel { get; set; }

        /// <summary>Its fares against the player's on the routes they share (1 = the same; 0 when none).</summary>
        public double FareAgainstMine { get; set; }

        public double Load { get; set; }
    }

    /// <summary>One of the player's stations: who else flies from it.</summary>
    public class StationRivals
    {
        public Station Station { get; set; }
        public int MyRoutes { get; set; }
        public int MyGates { get; set; }
        public int Gates { get; set; }
        public int FreeGates { get; set; }

        /// <summary>The other carriers here: their routes from the station, and their gates.</summary>
        public List<Tuple<Carrier, int, int>> Carriers { get; set; } = new List<Tuple<Carrier, int, int>>();
    }

    /// <summary>
    ///     What the player's competitors do: who flies the player's routes and stations, their fares and loads against
    ///     the player's, how each rival carrier is doing, and their recent moves (routes opened and given up, fare
    ///     changes). Routes given up and fare changes are remembered from when the game was started or loaded.
    /// </summary>
    public static class CompetitorService
    {
        private const int KeepDays = 180;
        private const int MaxMoves = 5000;
        private static readonly List<RivalMove> Log = new List<RivalMove>();

        /// <summary>How many moves are remembered (for the tests).</summary>
        public static int Remembered
        {
            get { lock (Log) return Log.Count; }
        }

        public static void Clear()
        {
            lock (LastAlert) LastAlert.Clear();
            lock (Log) Log.Clear();
        }

        /// <summary>The remembered moves, for a saved game.</summary>
        public static List<SavedRivalMove> ToSave()
        {
            lock (Log)
            {
                return Log.Where(m => m.Carrier != null && m.A != null && m.B != null)
                          .Select(m => new SavedRivalMove {Date = m.Date, Carrier = m.Carrier, A = m.A.Profile.IATACode, B = m.B.Profile.IATACode, Kind = (int) m.Kind, RouteId = m.RouteId, Factor = m.Factor})
                          .ToList();
            }
        }

        /// <summary>The moves of a loaded game (an older save has none: they are remembered from then on).</summary>
        public static int Restore(List<SavedRivalMove> saved)
        {
            lock (Log)
            {
                Log.Clear();
                if (saved == null) return 0;
                foreach (SavedRivalMove m in saved.OrderBy(x => x.Date))
                {
                    Station a = Stations.GetStation(m.A), b = Stations.GetStation(m.B);
                    if (m.Carrier == null || a == null || b == null || !Enum.IsDefined(typeof (RivalMoveKind), m.Kind)) continue;
                    Log.Add(new RivalMove {Date = m.Date, Carrier = m.Carrier, A = a, B = b, Kind = (RivalMoveKind) m.Kind, RouteId = m.RouteId, Factor = m.Factor});
                }
                if (Log.Count > MaxMoves) Log.RemoveRange(0, Log.Count - MaxMoves);
                return Log.Count;
            }
        }

        private static void Add(RivalMove m)
        {
            lock (Log)
            {
                Log.Add(m);
                DateTime oldest = m.Date.AddDays(-KeepDays);
                if (Log.Count > MaxMoves || Log[0].Date < oldest) Log.RemoveAll(x => x.Date < oldest);
                if (Log.Count > MaxMoves) Log.RemoveRange(0, Log.Count - MaxMoves);
            }
        }

        /// <summary>A computer carrier opened a route.</summary>
        public static void RecordOpened(Route route)
        {
            if (route?.Carrier == null || route.Carrier.IsHuman) return;
            var m = new RivalMove {Date = GameObject.GetInstance().GameTime, Carrier = route.Carrier, A = route.Destination1, B = route.Destination2, Kind = RivalMoveKind.Opened, RouteId = route.Id};
            Add(m);
            Alert(m, route, 0);
        }

        /// <summary>A computer carrier gave up a route.</summary>
        public static void RecordDropped(Carrier carrier, Route route)
        {
            if (carrier == null || carrier.IsHuman || route == null) return;
            var m = new RivalMove {Date = GameObject.GetInstance().GameTime, Carrier = carrier, A = route.Destination1, B = route.Destination2, Kind = RivalMoveKind.Dropped, RouteId = route.Id};
            Add(m);
            Alert(m, route, 0);
        }

        /// <summary>A computer carrier changed its fares on a route by `factor`.</summary>
        public static void RecordFare(Carrier carrier, Route route, double factor, double load = 0)
        {
            if (carrier == null || carrier.IsHuman || route == null || Math.Abs(factor - 1) < 0.005) return;
            var m = new RivalMove
            {
                Date = GameObject.GetInstance().GameTime, Carrier = carrier, A = route.Destination1, B = route.Destination2,
                Kind = factor < 1 ? RivalMoveKind.FareCut : RivalMoveKind.FareRise, RouteId = route.Id, Factor = factor
            };
            Add(m);
            if (m.Kind == RivalMoveKind.FareCut) Alert(m, route, load);
        }

        // ---- alerts: the player hears when a rival opens, undercuts or gives up one of the player's routes ----

        /// <summary>The days before the same rival's same kind of move on the same station pair is told again.</summary>
        public const int AlertDays = 30;

        /// <summary>A fare cut is told only when it is at least this deep (0.97 = 3%) and leaves the rival cheaper than the player.</summary>
        public const double AlertCut = 0.97;

        /// <summary>For the tests: alerts sent, and alerts held back because the same one was sent lately.</summary>
        public static int AlertsSent, AlertsHeld;

        private static readonly Dictionary<string, DateTime> LastAlert = new Dictionary<string, DateTime>();

        // the player's routes on the move's station pair, of the same kind (passenger or cargo)
        private static List<Route> PlayerRoutesOn(RivalMove m, Carrier human)
        {
            return RouteIndex.Direct(m.A, m.B)
                             .Where(r => r.Carrier != null && SameCompany(r.Carrier, human) && r.Carrier.Routes.Contains(r))
                             .Distinct().ToList();
        }

        private static void Alert(RivalMove m, Route route, double load)
        {
            try
            {
                var game = GameObject.GetInstance();
                Carrier human = game?.HumanCarrier;
                if (human == null || m.A == null || m.B == null || SameCompany(m.Carrier, human)) return;
                if (Infrastructure.Settings.GetInstance().NoRivalAlerts) return;
                List<Route> mine = PlayerRoutesOn(m, human);
                if (mine.Count == 0) return;
                Route my = mine[0];

                string body;
                var f = new ValueCurrencyFormatter();
                string pair = m.A.Profile.IATACode + "-" + m.B.Profile.IATACode;
                string what = "economy fare";
                string name = m.Carrier.Profile.Name;
                switch (m.Kind)
                {
                    case RivalMoveKind.Opened:
                        body = string.Format("{0} opened a {1} route between {2} and {3}: {4} trips a week with {5}, at {6}. {7}",
                                             name, "passenger", m.A.Profile.Name, m.B.Profile.Name, route.TimeTable.Entries.Count, CoachOf(route),
                                             Price(f, Fare(route)),
                                             my.TimeTable.Entries.Count == 0 ? "Your route there has no trips yet; your " + what + " is " + Price(f, Fare(my)) + "."
                                                 : "You run it " + my.TimeTable.Entries.Count + " times a week at " + Price(f, Fare(my)) + ".");
                        break;
                    case RivalMoveKind.FareCut:
                        double theirs = Fare(route), yours = Fare(my);
                        if (m.Factor > AlertCut || theirs <= 0 || yours <= 0 || theirs >= yours) return;
                        body = string.Format("{0} cut its {1} on {2} by {3:0}% to {4}, below your {5}.{6}",
                                             name, what, pair, (1 - m.Factor)*100, f.Convert(theirs), f.Convert(yours),
                                             load > 0 ? string.Format(" Their {0} were {1:0}% full.", "trips", load*100) : "");
                        break;
                    case RivalMoveKind.Dropped:
                        int left = RivalRoutes(my).Count;
                        body = string.Format("{0} gave up its {1} route on {2}. {3}", name, "passenger", pair,
                                             left == 0 ? "You run it alone now." : left == 1 ? "One rival still runs it." : left + " rivals still run it.");
                        break;
                    default:
                        return;
                }

                // one alert per rival, station pair and kind of move in a month
                string a = string.CompareOrdinal(m.A.Profile.IATACode, m.B.Profile.IATACode) <= 0 ? m.A.Profile.IATACode : m.B.Profile.IATACode;
                string b = a == m.A.Profile.IATACode ? m.B.Profile.IATACode : m.A.Profile.IATACode;
                string key = m.Carrier.Profile.IATACode + "|" + a + "|" + b + "|" + m.Kind;
                lock (LastAlert)
                {
                    DateTime last;
                    if (LastAlert.TryGetValue(key, out last) && (m.Date - last).TotalDays < AlertDays && m.Date >= last)
                    {
                        AlertsHeld++;
                        return;
                    }
                    LastAlert[key] = m.Date;
                    AlertsSent++;
                }

                string subject = m.Kind == RivalMoveKind.Opened ? name + " now runs " + pair
                    : m.Kind == RivalMoveKind.FareCut ? name + " undercuts you on " + pair
                    : name + " leaves " + pair;
                var news = new News(News.NewsType.RivalNews, m.Date, subject, body)
                {
                    ActionObject = new RivalLink {A = m.A.Profile.IATACode, B = m.B.Profile.IATACode, Carrier = name}
                };
                lock (game.NewsBox) game.NewsBox.AddNews(news);
            }
            catch (Exception)
            {
                // an alert never stops the game day (another carrier may change its routes meanwhile)
            }
        }

        private static string Price(ValueCurrencyFormatter f, double fare)
        {
            return fare > 0 ? f.Convert(fare).ToString() : "a fare not set yet";
        }

        private static bool SameCompany(Carrier a, Carrier b)
        {
            if (a == null || b == null) return false;
            if (a == b) return true;
            var pa = (a as Models.Carriers.Subsidiary.SubsidiaryCarrier)?.Carrier;
            var pb = (b as Models.Carriers.Subsidiary.SubsidiaryCarrier)?.Carrier;
            return pa == b || pb == a || (pa != null && pa == pb);
        }

        private static double Fare(Route r)
        {
            if (r is PassengerRoute p) return AIPricingService.EconomyFare(p);
            return 0;
        }

        private static double Load(Route r)
        {
            try
            {
                return ConnectionService.MonthLoad(r);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static string CoachOf(Route r)
        {
            var types = r.GetCoaches().Select(a => a.Coach.Type.Name).Distinct().ToList();
            if (types.Count == 0) return "no coach";
            return string.Join(", ", types.Take(2)) + (types.Count > 2 ? " and " + (types.Count - 2) + " more" : "");
        }

        private static bool Flying(Route r)
        {
            return r.Carrier != null && r.HasCoach && r.Carrier.Routes.Contains(r);
        }

        /// <summary>The other carriers' flying routes on a route's station pair.</summary>
        public static List<Route> RivalRoutes(Route mine)
        {
            return RouteIndex.Direct(mine.Destination1, mine.Destination2)
                             .Where(r => r != mine && !SameCompany(r.Carrier, mine.Carrier) && Flying(r))
                             .Distinct().ToList();
        }

        /// <summary>The player's routes, each with the rivals on its station pair (those without rivals too).</summary>
        public static List<RouteRivals> ForRoutes(Carrier me)
        {
            var result = new List<RouteRivals>();
            foreach (Route mine in me.Routes.ToList())
            {
                if (mine.Destination1 == null || mine.Destination2 == null) continue;
                var row = new RouteRivals
                {
                    Mine = mine, MyFare = Fare(mine), MyLoad = Load(mine), MyWeeklyTrips = mine.TimeTable.Entries.Count,
                    MarketFare = mine is PassengerRoute ? PricingService.MarketFare(mine.Destination1, mine.Destination2, Models.Coaches.CoachClass.ClassType.EconomyClass) : 0
                };
                foreach (Route r in RivalRoutes(mine))
                {
                    row.Rivals.Add(new RivalOnRoute {Route = r, Fare = Fare(r), Load = Load(r), WeeklyTrips = r.TimeTable.Entries.Count, Coach = CoachOf(r)});
                }
                row.Rivals = row.Rivals.OrderByDescending(x => x.WeeklyTrips).ToList();
                result.Add(row);
            }
            return result;
        }

        /// <summary>The stations the player flies from or to.</summary>
        public static List<Station> MyStations(Carrier me)
        {
            return me.Routes.ToList().SelectMany(r => new[] {r.Destination1, r.Destination2}).Concat(me.Stations.ToList()).Where(a => a != null).Distinct().ToList();
        }

        /// <summary>The player's stations, each with the other carriers flying from it.</summary>
        public static List<StationRivals> ForStations(Carrier me)
        {
            var result = new List<StationRivals>();
            foreach (Station ap in MyStations(me))
            {
                var routes = RouteIndex.At(ap).Where(Flying).ToList();
                var row = new StationRivals
                {
                    Station = ap,
                    MyRoutes = me.Routes.ToList().Count(r => r.Destination1 == ap || r.Destination2 == ap),
                    Gates = ap.Terminals.GetNumberOfGates(Terminal.TerminalType.Passenger),
                    FreeGates = ap.Terminals.GetFreeGates(Terminal.TerminalType.Passenger),
                    MyGates = ap.GetCarrierContracts(me).Sum(c => c.NumberOfGates)
                };
                foreach (var g in routes.Where(r => !SameCompany(r.Carrier, me)).GroupBy(r => r.Carrier).OrderByDescending(g => g.Count()))
                {
                    int gates = 0;
                    try { gates = ap.GetCarrierContracts(g.Key).Sum(c => c.NumberOfGates); } catch (Exception) { }
                    row.Carriers.Add(Tuple.Create(g.Key, g.Count(), gates));
                }
                result.Add(row);
            }
            return result;
        }

        /// <summary>Last month's operating result and income of an carrier (as the finances count them: no purchases or loans).</summary>
        public static void LastMonth(Carrier carrier, out double result, out double income)
        {
            DateTime last = GameObject.GetInstance().GameTime.AddMonths(-1);
            income = 0;
            double costs = 0;
            foreach (Invoice.InvoiceType t in Enum.GetValues(typeof(Invoice.InvoiceType)))
            {
                if (t == Invoice.InvoiceType.Total || t == Invoice.InvoiceType.Purchases || t == Invoice.InvoiceType.Loans) continue;
                double v = carrier.GetInvoicesAmountMonth(last.Year, last.Month, t);
                if (v > 0) income += v;
                else costs += v;
            }
            result = income + costs;
        }

        /// <summary>The carriers that fly the player's routes or from the player's stations, the closest competitors first.</summary>
        public static List<RivalCarrier> Rivals(Carrier me)
        {
            var byCarrier = new Dictionary<Carrier, RivalCarrier>();
            Func<Carrier, RivalCarrier> get = a =>
            {
                RivalCarrier r;
                if (!byCarrier.TryGetValue(a, out r)) byCarrier[a] = r = new RivalCarrier {Carrier = a};
                return r;
            };
            foreach (RouteRivals rr in ForRoutes(me))
            {
                foreach (Carrier a in rr.Rivals.Select(x => x.Carrier).Distinct()) get(a).SharedRoutes.Add(rr);
            }
            foreach (StationRivals ar in ForStations(me))
            {
                foreach (var a in ar.Carriers) get(a.Item1).SharedStations.Add(ar.Station);
            }
            foreach (RivalCarrier r in byCarrier.Values)
            {
                Carrier a = r.Carrier;
                List<Route> routes = a.Routes.ToList();
                r.Fleet = a.Fleet.Count;
                r.Routes = routes.Count;
                double result, income;
                LastMonth(a, out result, out income);
                r.LastMonthResult = result;
                r.LastMonthIncome = income;
                var ratios = routes.OfType<PassengerRoute>().Where(x => x.HasCoach).Select(x =>
                    {
                        double m = PricingService.MarketFare(x.Destination1, x.Destination2, Models.Coaches.CoachClass.ClassType.EconomyClass);
                        double f = AIPricingService.EconomyFare(x);
                        return m > 0 && f > 0 ? f/m : 0;
                    }).Where(x => x > 0).ToList();
                r.FareLevel = ratios.Count > 0 ? ratios.Average() : 0;
                var against = r.SharedRoutes.Where(s => s.MyFare > 0).SelectMany(s => s.Rivals.Where(x => x.Carrier == a && x.Fare > 0).Select(x => x.Fare/s.MyFare)).ToList();
                r.FareAgainstMine = against.Count > 0 ? against.Average() : 0;
                var loads = routes.Where(x => x.HasCoach).Select(Load).Where(x => x > 0).ToList();
                r.Load = loads.Count > 0 ? loads.Average() : 0;
            }
            return byCarrier.Values.OrderByDescending(r => r.SharedRoutes.Count).ThenByDescending(r => r.SharedStations.Count).ThenByDescending(r => r.Fleet).ToList();
        }

        /// <summary>
        ///     Rivals' moves in the last `days` days at the player's stations, newest first: routes opened (also those
        ///     opened before the game was loaded, from the routes' start dates), routes given up and fare changes.
        /// </summary>
        public static List<RivalMove> Moves(Carrier me, int days)
        {
            DateTime now = GameObject.GetInstance().GameTime, from = now.AddDays(-days);
            var mine = new HashSet<Station>(MyStations(me));
            List<RivalMove> moves;
            lock (Log) moves = Log.Where(m => m.Date >= from && (mine.Contains(m.A) || mine.Contains(m.B)) && !SameCompany(m.Carrier, me)).ToList();

            // routes opened that the log doesn't have (opened before the game was loaded)
            var logged = new HashSet<string>(moves.Where(m => m.Kind == RivalMoveKind.Opened).Select(m => m.RouteId));
            foreach (Station ap in mine)
            {
                foreach (Route r in RouteIndex.At(ap))
                {
                    if (r.Carrier == null || SameCompany(r.Carrier, me) || r.StartDate < from || r.StartDate > now || logged.Contains(r.Id)) continue;
                    logged.Add(r.Id);
                    moves.Add(new RivalMove {Date = r.StartDate, Carrier = r.Carrier, A = r.Destination1, B = r.Destination2, Kind = RivalMoveKind.Opened, RouteId = r.Id});
                }
            }
            return moves.OrderByDescending(m => m.Date).ToList();
        }
    }
}
