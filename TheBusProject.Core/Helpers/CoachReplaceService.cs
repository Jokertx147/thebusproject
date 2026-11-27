using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    /// <summary>An coach that could take over another coach's trips on a route, or why it can't.</summary>
    public class ReplacementOption
    {
        public FleetCoach Coach { get; set; }
        public bool CanReplace { get; set; }

        /// <summary>"free", "also runs ATL-MIA", or why it can't.</summary>
        public string Note { get; set; }

        /// <summary>When the coach is too slow for the timetable: the trips moved later so they all fit (null when that can't work).</summary>
        public FitPlan Shift { get; set; }

        /// <summary>When the coach is too slow for the timetable: only the round trips that fit at their times (null when none do).</summary>
        public FitPlan Keep { get; set; }
    }

    /// <summary>How the trips of a route are changed so a slower coach can fly them.</summary>
    public class FitPlan
    {
        /// <summary>New departure (minute of the week, Sunday 00:00 = 0) for the trips that move.</summary>
        public Dictionary<RouteTimeTableEntry, int> Moves { get; } = new Dictionary<RouteTimeTableEntry, int>();

        /// <summary>Trips that are dropped.</summary>
        public List<RouteTimeTableEntry> Drop { get; } = new List<RouteTimeTableEntry>();

        public int Total { get; set; }
        public string Summary { get; set; }
    }

    /// <summary>What decides whether an coach can fly a timetable: its type (speed) and its turnaround (seats).</summary>
    public class CoachSpec
    {
        public CoachType Type { get; set; }
        public double TurnaroundMinutes { get; set; }

        public static CoachSpec Of(FleetCoach f)
        {
            return new CoachSpec {Type = f.Coach.Type, TurnaroundMinutes = FleetCoachHelpers.GetMinTimeBetweenTrips(f).TotalMinutes};
        }

        /// <summary>For an coach not built yet (an order): the type with its usual cabin.</summary>
        public static CoachSpec OfType(CoachType t)
        {
            return new CoachSpec {Type = t, TurnaroundMinutes = FleetCoachHelpers.GetMinTimeBetweenTrips(t).TotalMinutes};
        }

        /// <summary>For an coach that isn't in the fleet yet (on the market).</summary>
        public static CoachSpec Of(Coach a)
        {
            return new CoachSpec {Type = a.Type, TurnaroundMinutes = FleetCoachHelpers.GetMinTimeBetweenTrips(a).TotalMinutes};
        }
    }

    public enum ReplaceMode
    {
        SameTimes,
        ShiftTimes,
        KeepFitting
    }

    /// <summary>
    ///     Puts another coach of the player's fleet on a route in place of one that flies it (N101AA to N103AA): the
    ///     new coach takes over exactly the trips the old one had on the route, at the same times, and the old one
    ///     keeps its other routes (or stands idle). The new coach may fly other routes too, when the trips fit
    ///     between its own.
    /// </summary>
    public static class CoachReplaceService
    {
        private static bool RightKind(FleetCoach f, Route route)
        {
            CoachType.TypeOfCoach kind = f.Coach.Type.TypeCoach;
            return kind == CoachType.TypeOfCoach.Passenger;
        }

        private static string Code(Route r)
        {
            return r.Destination1.Profile.IATACode + "-" + r.Destination2.Profile.IATACode;
        }

        private static int WeekMinute(RouteTimeTableEntry e)
        {
            return (int) e.Day*24*60 + (int) e.Time.TotalMinutes;
        }

        /// <summary>
        ///     Whether the coach can fly all the given trips: each one, with the trip time of this coach type
        ///     and its turnaround, must end before the next one of the week starts. Returns the first clash, or null.
        /// </summary>
        public static string FirstClash(FleetCoach coach, List<RouteTimeTableEntry> entries)
        {
            return FirstClash(CoachSpec.Of(coach), entries);
        }

        public static string FirstClash(CoachSpec coach, List<RouteTimeTableEntry> entries)
        {
            var list = entries.OrderBy(WeekMinute).ToList();
            if (list.Count < 2) return null;
            TimeSpan turnaround = TimeSpan.FromMinutes(coach.TurnaroundMinutes);
            const int week = 7*24*60;
            for (int i = 0; i < list.Count; i++)
            {
                RouteTimeTableEntry e = list[i];
                RouteTimeTableEntry next = list[(i + 1)%list.Count];
                int start = WeekMinute(e);
                int nextStart = WeekMinute(next) + (i + 1 == list.Count ? week : 0);
                double needs = e.TimeTable.Route.GetTripTime(coach.Type).TotalMinutes + turnaround.TotalMinutes;
                if (start + needs > nextStart)
                {
                    return e.Day + " " + e.Time.ToString(@"hh\:mm") + " from " + e.DepartureStation.Profile.IATACode + " (" + Code(e.TimeTable.Route)
                           + ") needs " + Math.Ceiling(needs) + " min with the turnaround, and the next trip leaves after " + (nextStart - start) + " min";
                }
            }
            return null;
        }

        private const int Week = 7*24*60;

        private static int Need(CoachSpec coach, RouteTimeTableEntry e)
        {
            return (int) Math.Ceiling(e.TimeTable.Route.GetTripTime(coach.Type).TotalMinutes + coach.TurnaroundMinutes);
        }

        private static string When(int weekMinute)
        {
            int m = ((weekMinute%Week) + Week)%Week;
            return (DayOfWeek) (m/1440) + " " + (m%1440/60).ToString("00") + ":" + (m%60).ToString("00");
        }

        // the coach's trips on its other routes, as busy stretches of the week (also a week before and after)
        private static List<Tuple<int, int>> Busy(CoachSpec to, IEnumerable<RouteTimeTableEntry> own)
        {
            var list = new List<Tuple<int, int>>();
            foreach (RouteTimeTableEntry e in own)
            {
                int s = WeekMinute(e), need = Need(to, e);
                for (int w = -1; w <= 1; w++) list.Add(Tuple.Create(s + w*Week, s + need + w*Week));
            }
            return list;
        }

        // the first moment from `start` on when a trip of `need` minutes doesn't overlap any busy stretch
        private static int FreeFrom(int start, int need, List<Tuple<int, int>> busy)
        {
            for (int guard = 0; guard < 500; guard++)
            {
                var hit = busy.FirstOrDefault(b => start < b.Item2 && b.Item1 < start + need);
                if (hit == null) return start;
                start = hit.Item2;
            }
            return start;
        }

        private static List<RouteTimeTableEntry> Moving(Route route, FleetCoach from)
        {
            return route.TimeTable.Entries.Where(e => e.Coach == from).OrderBy(WeekMinute).ToList();
        }

        private static List<RouteTimeTableEntry> Own(FleetCoach to)
        {
            return to.Routes.SelectMany(r => r.TimeTable.Entries.Where(e => e.Coach == to)).ToList();
        }

        /// <summary>
        ///     All the trips, in the same order, each moved later only as much as needed for the slower coach (and
        ///     around its trips on other routes). Null when the week is too short for all of them.
        /// </summary>
        public static FitPlan PlanShift(Route route, FleetCoach from, FleetCoach to)
        {
            return PlanShift(route, from, CoachSpec.Of(to), Own(to));
        }

        /// <summary>As above for an coach described by its type and turnaround, with its trips on other routes.</summary>
        public static FitPlan PlanShift(Route route, FleetCoach from, CoachSpec to, List<RouteTimeTableEntry> own)
        {
            if (route.HasStopovers) return null;
            var moving = Moving(route, from);
            if (moving.Count == 0) return null;
            var busy = Busy(to, own);
            var plan = new FitPlan {Total = moving.Count};
            int prevEnd = int.MinValue, first = 0, maxShift = 0, moved = 0;
            for (int i = 0; i < moving.Count; i++)
            {
                RouteTimeTableEntry e = moving[i];
                int need = Need(to, e);
                int start = FreeFrom(Math.Max(WeekMinute(e), prevEnd), need, busy);
                if (i == 0) first = start;
                if (start != WeekMinute(e))
                {
                    plan.Moves[e] = start;
                    moved++;
                    maxShift = Math.Max(maxShift, start - WeekMinute(e));
                }
                prevEnd = start + need;
            }
            // the last trip of the week must be done before the first one of the next week
            if (prevEnd > first + Week) return null;
            if (moved == 0) return null;
            plan.Summary = "moves " + moved + " of the " + moving.Count + " weekly trips later, by up to " + (maxShift >= 60 ? (maxShift/60) + " h " + (maxShift%60) + " min" : maxShift + " min")
                           + "; the last one leaves " + When(plan.Moves.Count > 0 ? plan.Moves.Values.Max() : WeekMinute(moving.Last()));
            return plan;
        }

        /// <summary>
        ///     The round trips that fit at their times for the slower coach (one out and back is kept or dropped
        ///     together, so the coach ends where the next one starts). Null when none fits.
        /// </summary>
        public static FitPlan PlanKeep(Route route, FleetCoach from, FleetCoach to)
        {
            return PlanKeep(route, from, CoachSpec.Of(to), Own(to));
        }

        public static FitPlan PlanKeep(Route route, FleetCoach from, CoachSpec to, List<RouteTimeTableEntry> own)
        {
            if (route.HasStopovers) return null;
            var moving = Moving(route, from);
            if (moving.Count == 0) return null;
            var busy = Busy(to, own);

            // out and back pairs (a trip and the next one from the other end)
            var trips = new List<List<RouteTimeTableEntry>>();
            for (int i = 0; i < moving.Count; i++)
            {
                if (i + 1 < moving.Count && moving[i + 1].DepartureStation != moving[i].DepartureStation)
                {
                    trips.Add(new List<RouteTimeTableEntry> {moving[i], moving[i + 1]});
                    i++;
                }
                else
                {
                    trips.Add(new List<RouteTimeTableEntry> {moving[i]});
                }
            }

            var kept = new List<List<RouteTimeTableEntry>>();
            int prevEnd = int.MinValue;
            foreach (var trip in trips)
            {
                int end = prevEnd;
                bool fits = true;
                foreach (RouteTimeTableEntry e in trip)
                {
                    int s = WeekMinute(e), need = Need(to, e);
                    if (s < end || FreeFrom(s, need, busy) != s)
                    {
                        fits = false;
                        break;
                    }
                    end = s + need;
                }
                if (!fits) continue;
                kept.Add(trip);
                prevEnd = end;
            }
            // the week wraps: drop trips from the end until the last one is done before the first one next week
            while (kept.Count > 1)
            {
                var last = kept.Last().Last();
                if (WeekMinute(last) + Need(to, last) <= WeekMinute(kept[0][0]) + Week) break;
                kept.RemoveAt(kept.Count - 1);
            }
            if (kept.Count == 0) return null;

            var keep = new HashSet<RouteTimeTableEntry>(kept.SelectMany(t => t));
            var plan = new FitPlan {Total = moving.Count};
            plan.Drop.AddRange(moving.Where(e => !keep.Contains(e)));
            if (plan.Drop.Count == 0) return null;
            plan.Summary = "keeps " + keep.Count + " of the " + moving.Count + " weekly trips at their times and drops " + plan.Drop.Count + " (whole round trips) that don't fit";
            return plan;
        }

        /// <summary>
        ///     The coaches of the route's own carrier that could take over the trips of `from`, the ones that fit
        ///     first. It used to look at the player's fleet whoever the route belonged to, which was harmless while
        ///     only the player's screens called it and wrong the moment a computer carrier did.
        /// </summary>
        public static List<ReplacementOption> Options(Route route, FleetCoach from)
        {
            Carrier carrier = route?.Carrier ?? GameObject.GetInstance().HumanCarrier;
            var moving = route.TimeTable.Entries.Where(e => e.Coach == from).ToList();
            double distance = MathHelpers.GetDistance(route.Destination1, route.Destination2);
            long bay = Math.Min(route.Destination1.GetMaxBayLength(), route.Destination2.GetMaxBayLength());
            var onRoute = route.GetCoaches();

            var list = new List<ReplacementOption>();
            foreach (FleetCoach f in carrier.DeliveredFleet.Where(f => f != from && !onRoute.Contains(f) && RightKind(f, route)).OrderBy(f => f.Name))
            {
                var o = new ReplacementOption {Coach = f};
                if (CoachDealService.IsLeasedOut(f)) o.Note = "leased out";
                else if (CoachDealService.IsListed(f)) o.Note = "for sale";
                else if (CharterService.IsOnCharter(f)) o.Note = "on a charter";
                else if (f.Coach.Range < distance) o.Note = "range too short (" + f.Coach.Range.ToString("N0") + " km)";
                else if (f.Coach.MinBayLength > bay) o.Note = "bay too short";
                else
                {
                    var own = f.Routes.SelectMany(r => r.TimeTable.Entries.Where(e => e.Coach == f)).ToList();
                    string clash = FirstClash(f, own.Concat(moving).ToList());
                    if (clash != null)
                    {
                        o.Note = f.Routes.Count == 0 ? "too slow for this timetable" : "the trips don't fit between its own";
                        o.Shift = PlanShift(route, from, f);
                        o.Keep = PlanKeep(route, from, f);
                        if (o.Shift != null || o.Keep != null)
                        {
                            o.Note += o.Shift != null ? " (the times can be shifted)" : " (some trips can be kept)";
                        }
                    }
                    else
                    {
                        o.CanReplace = true;
                        o.Note = f.Routes.Count == 0 ? "free" : "also runs " + string.Join(", ", f.Routes.Select(Code));
                    }
                }
                list.Add(o);
            }
            return list.OrderByDescending(o => o.CanReplace).ThenByDescending(o => o.Shift != null || o.Keep != null).ThenBy(o => o.Coach.Name).ToList();
        }

        /// <summary>Puts `to` on the route in place of `from`, on the same trips. False with the reason when it can't.</summary>
        public static bool Replace(Route route, FleetCoach from, FleetCoach to, out string message)
        {
            return Replace(route, from, to, ReplaceMode.SameTimes, out message);
        }

        /// <summary>
        ///     Puts `to` on the route in place of `from`: on the same trips, with the times shifted so a slower coach
        ///     fits, or keeping only the round trips that fit. False with the reason when it can't.
        /// </summary>
        public static bool Replace(Route route, FleetCoach from, FleetCoach to, ReplaceMode mode, out string message)
        {
            // The route's own carrier, not the player's. This read the player's fleet whoever owned the route, so
            // a computer carrier replacing a worn-out coach was told "That isn't one of your routes." and nothing
            // happened: 371 of the 425 lines a playthrough's renewals produced were that one message.
            Carrier carrier = route?.Carrier;
            message = "";
            if (route == null || carrier == null || !carrier.Routes.Contains(route))
            {
                message = "That isn't one of your routes.";
                return false;
            }
            if (from == null || !route.GetCoaches().Contains(from))
            {
                message = "Pick a coach that runs this route.";
                return false;
            }
            if (to == null)
            {
                message = "Pick the coach to put on the route.";
                return false;
            }

            ReplacementOption option = Options(route, from).FirstOrDefault(o => o.Coach == to);
            if (option == null)
            {
                message = to.Name + " can't take over: it already runs this route, isn't delivered yet, or is the wrong kind of coach.";
                return false;
            }
            FitPlan plan = mode == ReplaceMode.ShiftTimes ? option.Shift : mode == ReplaceMode.KeepFitting ? option.Keep : null;
            if (mode == ReplaceMode.SameTimes && !option.CanReplace || mode != ReplaceMode.SameTimes && plan == null)
            {
                var own = to.Routes.SelectMany(r => r.TimeTable.Entries.Where(e => e.Coach == to)).ToList();
                string clash = FirstClash(to, own.Concat(route.TimeTable.Entries.Where(e => e.Coach == from)).ToList());
                bool timing = option.Note.Contains("slow") || option.Note.Contains("fit");
                string why = mode == ReplaceMode.ShiftTimes ? "the week is too short to fit all the trips even with the times shifted"
                    : mode == ReplaceMode.KeepFitting ? (option.CanReplace ? "all the trips fit already" : "no round trip fits at its time")
                    : option.Note + (timing && clash != null ? " (" + clash + ")" : "");
                message = to.Name + " can't take over from " + from.Name + ": " + why + ".";
                return false;
            }

            string changed = "";
            if (plan != null)
            {
                foreach (var move in plan.Moves)
                {
                    int m = ((move.Value%Week) + Week)%Week;
                    move.Key.Day = (DayOfWeek) (m/1440);
                    move.Key.Time = new TimeSpan(0, m%1440, 0);
                }
                foreach (RouteTimeTableEntry e in plan.Drop)
                {
                    route.TimeTable.Entries.Remove(e);
                }
                changed = " " + char.ToUpper(plan.Summary[0]) + plan.Summary.Substring(1) + ".";
            }

            int trips = 0;
            foreach (RouteTimeTableEntry e in route.TimeTable.Entries.Where(e => e.Coach == from).ToList())
            {
                e.Coach = to;
                trips++;
            }
            from.Routes.Remove(route);
            if (!to.Routes.Contains(route)) to.AddRoute(route);

            string fromNow;
            if (from.Routes.Count == 0)
            {
                from.Status = FleetCoach.CoachStatus.Stopped;
                fromNow = from.Name + " has no route now.";
            }
            else
            {
                fromNow = from.Name + " keeps running " + string.Join(", ", from.Routes.Select(Code)) + ".";
            }

            string toNow;
            if (to.Status != FleetCoach.CoachStatus.Stopped)
            {
                toNow = to.Name + " is running it.";
            }
            else if (to.Drivers.Count >= to.Coach.Type.DriverCrew)
            {
                to.Status = GameObject.GetInstance().DayRoundEnabled ? FleetCoach.CoachStatus.OnRoute : FleetCoach.CoachStatus.ToRouteStart;
                toNow = to.Name + " is running it.";
            }
            else
            {
                toNow = to.Name + " has no full driver crew yet, so it stays stopped until it has one.";
            }

            RouteIndex.Invalidate();
            message = to.Name + " takes over " + from.Name + "'s " + trips + " weekly trips on " + Code(route)
                      + (plan == null ? ", at the same times." : "." + changed) + " " + toNow + " " + fromNow;
            return true;
        }
    }
}
