using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    /// <summary>What to do when the new type is too slow for an old coach's trips.</summary>
    public enum SlowerChoice
    {
        /// <summary>Shift the times; when the week is too short for that, keep the round trips that fit.</summary>
        ShiftOrKeep,
        KeepWhatFits,
        Skip
    }

    /// <summary>What happens to an old coach once its routes are taken over.</summary>
    public enum OldCoachChoice
    {
        Sell,
        ListForSale,
        KeepIdle
    }

    /// <summary>Where the coach come from when the fleet hasn't enough idle ones of the new type.</summary>
    public enum MissingSource
    {
        IdleOnly,
        BuyUsed,
        LeaseUsed,
        OrderNew,

        /// <summary>Used ones on the market first, new ones ordered for the rest.</summary>
        UsedThenNew
    }

    public class RenewalSettings
    {
        /// <summary>Whose fleet is renewed. Left unset it is the player's, which is what the renewal screen means.</summary>
        public Carrier Carrier { get; set; }

        public CoachType OldType { get; set; }
        public CoachType NewType { get; set; }
        public MissingSource Source { get; set; } = MissingSource.BuyUsed;

        /// <summary>For new coach: pay the down payment now and the rest on delivery (else all now).</summary>
        public bool DownPayment { get; set; }

        public SlowerChoice Slower { get; set; } = SlowerChoice.ShiftOrKeep;
        public OldCoachChoice AfterOld { get; set; } = OldCoachChoice.Sell;

        /// <summary>
        ///     Replace only the coaches at least this old, and only with ones younger than it. Left at nothing, the
        ///     whole type is replaced however old each coach is, which is what the renewal screen means by swapping
        ///     one type for another. Set, it means retiring what is worn out and keeping the rest, which is what a
        ///     carrier does year to year, and it is the only case where the new type may be the old one: a coach of
        ///     eighteen is most often replaced by a younger coach of the same model.
        /// </summary>
        public int OnlyOlderThan { get; set; }

        public bool UsesMarket => Source == MissingSource.BuyUsed || Source == MissingSource.LeaseUsed || Source == MissingSource.UsedThenNew;
        public bool Lease => Source == MissingSource.LeaseUsed;
        public bool Orders => Source == MissingSource.OrderNew || Source == MissingSource.UsedThenNew;
    }

    /// <summary>A type that can replace the old one, with where it can come from.</summary>
    public class RenewalTypeOption
    {
        public CoachType Type { get; set; }
        public int Idle { get; set; }
        public int ForSale { get; set; }
        public double UsedFrom { get; set; }

        /// <summary>The manufacturer builds it now (it can be ordered new).</summary>
        public bool Orderable { get; set; }

        public double NewPrice { get; set; }
    }

    /// <summary>One old coach and what the renewal does with it.</summary>
    public class RenewalRow
    {
        public FleetCoach Old { get; set; }

        /// <summary>An idle coach of the new type from the fleet, or null.</summary>
        public FleetCoach IdleNew { get; set; }

        /// <summary>An coach of the new type to buy or lease on the market, or null.</summary>
        public Coach MarketNew { get; set; }

        /// <summary>A new coach is ordered for this one: it takes over on delivery.</summary>
        public bool Ordered { get; set; }

        public bool Ok { get; set; }
        public ReplaceMode Mode { get; set; }
        public double Cost { get; set; }

        // the part of the row's text after the new coach is named (to finish ordered rows once the delivery is known)
        internal string Head, Fit;

        /// <summary>One line: "N101AA on ATL-MIA: N201AA (idle), same times" or why it is skipped.</summary>
        public string Text { get; set; }
    }

    public class RenewalPlan
    {
        public RenewalSettings Settings { get; set; }
        public List<RenewalRow> Rows { get; } = new List<RenewalRow>();
        public double Cost => Rows.Where(r => r.Ok).Sum(r => r.Cost);
        public int Replacing => Rows.Count(r => r.Ok && (r.IdleNew != null || r.MarketNew != null || r.Ordered));
        public int Buying => Rows.Count(r => r.Ok && r.MarketNew != null);
        public int Ordering => Rows.Count(r => r.Ok && r.Ordered);

        /// <summary>For the new coach: the whole price, what is paid now, and the delivery day.</summary>
        public double OrderTotal { get; set; }
        public double OrderNow { get; set; }
        public DateTime OrderDelivery { get; set; }
        public string Summary { get; set; }
    }

    /// <summary>
    ///     Fleet renewal: every coach of one type is replaced by an coach of another type, route by route, with the
    ///     same logic as replacing one coach on the route screen (same times, or shifted times / the round trips
    ///     that fit when the new type is slower). New coach come from the idle fleet first, then from the market. The
    ///     old coach are sold, listed for sale or kept idle afterwards.
    /// </summary>
    public static class FleetRenewalService
    {
        /// <summary>
        ///     The age at which a coach still earning its living is replaced. The FTA's useful life benchmark for an
        ///     over-the-road bus is 14 years and the average bus retires at about 15, so eighteen is already a
        ///     patient operator; it is kept below the 22 years a coach can still be sold at
        ///     (<see cref="Coaches.OldestForSale" />) so that the one coming off the fleet is still worth something.
        /// </summary>
        public const int RetireAt = 18;

        /// <summary>
        ///     Retires the carrier's worn-out coaches, one type at a time, and is what a computer carrier calls each
        ///     month. Nothing used to do this: Renew was reached only from the fleet renewal screen, so a human
        ///     player could replace a worn-out coach and no computer carrier ever did, and their coaches ran on to
        ///     26 and 28 years with no way off the fleet but going idle, a delivery payment they could not meet, or
        ///     the carrier failing outright.
        ///     <para>
        ///         One type a month, starting with the oldest coach still carrying passengers, so a carrier renews
        ///         steadily instead of re-equipping overnight. The replacement is paid for out of the carrier's own
        ///         money, so a carrier that cannot afford one keeps running what it has, which is also what happens
        ///         in life.
        ///     </para>
        /// </summary>
        public static List<string> RetireWornOut(Carrier carrier)
        {
            var nothing = new List<string>();
            if (carrier == null) return nothing;

            FleetCoach worst = carrier.DeliveredFleet
                                      .Where(f => f.Routes.Count > 0 && Usable(f) && f.Coach.GetAge() >= RetireAt)
                                      .Where(f => !CoachDealService.IsLeasedOut(f) && !CharterService.IsOnCharter(f))
                                      .Where(f => f.ReplacesOnDelivery == null)
                                      .OrderByDescending(f => f.Coach.GetAge())
                                      .FirstOrDefault();
            if (worst == null) return nothing;
            // something is already on its way for it
            if (carrier.Fleet.Any(f => f.ReplacesOnDelivery != null)) return nothing;

            CoachType oldType = worst.Coach.Type;

            // It has to be able to run the routes the worn coach runs. Without this the carrier kept settling on
            // a type that could not: 47 of the 99 lines one playthrough's renewals produced were the same
            // complaint that a station's bays were too short for the coach it had chosen, month after month.
            var mustServe = worst.Routes.ToList();
            Func<CoachType, bool> fitsTheRoutes = t => mustServe.All(
                r => RightKind(t, r)
                     && t.Range >= r.GetDistance()
                     && t.MinBayLength <= Math.Min(r.Destination1.GetMaxBayLength(), r.Destination2.GetMaxBayLength()));

            RenewalTypeOption pick = NewTypes(oldType, carrier, true, RetireAt)
                                     .Where(o => o.Idle > 0 || o.ForSale > 0)
                                     .Where(o => fitsTheRoutes(o.Type))
                                     .Where(o => o.Idle > 0 || (o.UsedFrom > 0 && o.UsedFrom <= carrier.Money*0.3))
                                     .OrderByDescending(o => o.Idle)
                                     .ThenBy(o => o.Type == oldType ? 0 : 1)
                                     .ThenBy(o => o.UsedFrom)
                                     .FirstOrDefault();
            if (pick == null) return nothing;

            return Renew(new RenewalSettings
                         {
                             Carrier = carrier,
                             OldType = oldType,
                             NewType = pick.Type,
                             Source = MissingSource.BuyUsed,
                             Slower = SlowerChoice.ShiftOrKeep,
                             AfterOld = OldCoachChoice.Sell,
                             OnlyOlderThan = RetireAt
                         });
        }

        private static string Code(Route r)
        {
            return r.Destination1.Profile.IATACode + "-" + r.Destination2.Profile.IATACode;
        }

        private static bool Usable(FleetCoach f)
        {
            return !CoachDealService.IsLeasedOut(f) && !CoachDealService.IsListed(f) && !CharterService.IsOnCharter(f);
        }

        private static bool RightKind(CoachType type, Route route)
        {
            CoachType.TypeOfCoach kind = type.TypeCoach;
            return kind == CoachType.TypeOfCoach.Passenger;
        }

        /// <summary>The coach types in the player's fleet, with how many of each.</summary>
        /// <summary>Whose fleet this is about: the carrier given, or the player's when none is.</summary>
        private static Carrier Whose(Carrier carrier)
        {
            return carrier ?? GameObject.GetInstance().HumanCarrier;
        }

        public static List<Tuple<CoachType, int>> FleetTypes(Carrier of = null)
        {
            Carrier carrier = Whose(of);
            return carrier.DeliveredFleet.GroupBy(f => f.Coach.Type).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Name)
                          .Select(g => Tuple.Create(g.Key, g.Count())).ToList();
        }

        private static bool KindMatches(CoachType oldType, CoachType t)
        {
            return t.TypeCoach == CoachType.TypeOfCoach.Passenger;
        }

        /// <summary>Whether the type is built now (can be ordered new), within a manufacturer contract.</summary>
        public static bool Orderable(Carrier carrier, CoachType t)
        {
            DateTime now = GameObject.GetInstance().GameTime;
            return t.Produced != null && t.Produced.From <= now && t.Produced.To >= now
                   && (carrier.Contract == null || carrier.Contract.Manufacturer == t.Manufacturer);
        }

        /// <summary>The delivery day of an order of `amount` coach (from the production rate, as on the new coach page).</summary>
        public static DateTime DeliveryDate(CoachType type, int amount)
        {
            DateTime now = GameObject.GetInstance().GameTime;
            var date = new DateTime(now.Year, now.Month, now.Day);
            int rate = Math.Max(1, type.ProductionRate);
            if (amount <= rate/4) return date.AddMonths(3);
            for (int i = (rate/4) + 1; i <= amount; i++)
            {
                date = date.AddDays(Math.Round((double) (365/rate), 0, MidpointRounding.AwayFromZero));
            }
            return date;
        }

        /// <summary>The price of ordering `amount` new coach of the type (quantity and contract discounts included).</summary>
        public static double OrderPrice(Carrier carrier, CoachType type, int amount)
        {
            double total = type.Price*amount*(1 - GeneralHelpers.GetCoachOrderDiscount(amount));
            if (carrier.Contract != null && carrier.Contract.Manufacturer == type.Manufacturer) total *= (100 - carrier.Contract.Discount)/100;
            return total;
        }

        /// <summary>What is paid when ordering: all of it, or the down payment (the rest on delivery).</summary>
        public static double OrderNow(double total, bool downPayment)
        {
            return downPayment ? total*(GameObject.GetInstance().Difficulty.PriceLevel/10) : total;
        }

        /// <summary>
        ///     The types that can replace the old one: idle in the fleet, on the
        ///     market, or built new now. With how many are idle and for sale, and the price new.
        /// </summary>
        /// <summary>
        ///     The types that could replace this one. <paramref name="includeSameType" /> keeps the old type among
        ///     them, for retiring worn-out coaches rather than swapping a type: a coach at the end of its working
        ///     life is most often replaced by a younger one of the same model. <paramref name="youngerThan" />, when
        ///     set, counts only coaches below that age, so an old coach is never offered as the cure for an old one.
        /// </summary>
        public static List<RenewalTypeOption> NewTypes(CoachType oldType, Carrier of = null, bool includeSameType = false, int youngerThan = 0)
        {
            Carrier carrier = Whose(of);
            DateTime now = GameObject.GetInstance().GameTime;
            Func<CoachType, bool> other = t => includeSameType || t != oldType;
            Func<int, bool> young = age => youngerThan <= 0 || age < youngerThan;
            var idle = carrier.DeliveredFleet.Where(f => f.Routes.Count == 0 && Usable(f) && other(f.Coach.Type) && young(f.Coach.GetAge()) && KindMatches(oldType, f.Coach.Type))
                              .GroupBy(f => f.Coach.Type).ToDictionary(g => g.Key, g => g.Count());
            var market = Coaches.GetCoachesForSale(a => a.Status == Coach.StatusTypes.Normal && other(a.Type) && KindMatches(oldType, a.Type) && a.BuiltDate <= now)
                                  .Where(a => young(a.GetAge()))
                                  .Where(a => carrier.Contract == null || carrier.Contract.Manufacturer == a.Type.Manufacturer)
                                  .GroupBy(a => a.Type).ToDictionary(g => g.Key, g => g.ToList());
            var built = CoachTypes.GetAllTypes().Where(t => other(t) && KindMatches(oldType, t) && Orderable(carrier, t)).ToList();
            return idle.Keys.Union(market.Keys).Union(built).Distinct()
                       .Select(t => new RenewalTypeOption
                       {
                           Type = t,
                           Idle = idle.TryGetValue(t, out int i) ? i : 0,
                           ForSale = market.TryGetValue(t, out var m) ? m.Count : 0,
                           UsedFrom = market.TryGetValue(t, out var m2) ? m2.Min(a => (double) a.GetPrice()) : 0,
                           Orderable = Orderable(carrier, t),
                           NewPrice = Orderable(carrier, t) ? OrderPrice(carrier, t, 1) : 0
                       })
                       .OrderByDescending(x => x.Idle).ThenByDescending(x => x.ForSale > 0).ThenBy(x => x.Type.Name).ToList();
        }

        /// <summary>The new coach from earlier renewals still waiting for delivery, one line each.</summary>
        public static List<string> Pending(Carrier of = null)
        {
            Carrier carrier = Whose(of);
            return carrier.Fleet.Where(f => f.ReplacesOnDelivery != null).OrderBy(f => f.Coach.BuiltDate)
                          .Select(f => f.Name + " (" + f.Coach.Type.Name + "), delivery " + f.Coach.BuiltDate.ToString("d MMM yyyy") + ": takes over "
                                       + (f.ReplacesOnDelivery.Routes.Count > 0 ? string.Join(", ", f.ReplacesOnDelivery.Routes.Select(Code)) + " from " : "from ")
                                       + f.ReplacesOnDelivery.Name)
                          .ToList();
        }

        private static double MarketCost(Carrier carrier, Coach a, bool lease)
        {
            double discount = carrier.Contract?.Discount ?? 0;
            return lease ? a.GetLeasingPrice()*2 : a.GetPrice()*((100 - discount)/100);
        }

        /// <summary>What a renewal would do, coach by coach (nothing is changed).</summary>
        public static RenewalPlan Plan(RenewalSettings s)
        {
            Carrier carrier = Whose(s?.Carrier);
            var plan = new RenewalPlan {Settings = s};
            // Replacing a type by itself only means something when it is the worn-out coaches being retired; the
            // screen's type swap would otherwise be a no-op dressed up as a plan.
            if (s.OldType == null || s.NewType == null || (s.OldType == s.NewType && s.OnlyOlderThan <= 0))
            {
                plan.Summary = "Pick the type to replace and the type to replace it with.";
                return plan;
            }

            // A coach that is itself past the age being retired is no cure for one, so when an age is given the
            // coach that takes over has to be younger than it.
            Func<int, bool> fitToTakeOver = age => s.OnlyOlderThan <= 0 || age < s.OnlyOlderThan;

            var idle = new Queue<FleetCoach>(carrier.DeliveredFleet.Where(f => f.Coach.Type == s.NewType && f.Routes.Count == 0 && Usable(f) && fitToTakeOver(f.Coach.GetAge())).OrderBy(f => f.Name));
            var market = new Queue<Coach>(Coaches.GetCoachesForSale(a => a.Status == Coach.StatusTypes.Normal && a.Type == s.NewType && a.BuiltDate <= GameObject.GetInstance().GameTime)
                                                      .Where(a => fitToTakeOver(a.GetAge()))
                                                      .Where(a => carrier.Contract == null || carrier.Contract.Manufacturer == a.Type.Manufacturer)
                                                      .OrderBy(a => a.GetAge()).ThenBy(a => a.GetPrice()));
            double money = carrier.Money;
            bool canOrder = s.Orders && Orderable(carrier, s.NewType);
            var waiting = carrier.Fleet.Where(f => f.ReplacesOnDelivery != null).ToDictionary(f => f.ReplacesOnDelivery, f => f);

            foreach (FleetCoach old in carrier.DeliveredFleet
                                              .Where(f => f.Coach.Type == s.OldType)
                                              .Where(f => s.OnlyOlderThan <= 0 || f.Coach.GetAge() >= s.OnlyOlderThan)
                                              .OrderBy(f => f.Name))
            {
                var row = new RenewalRow {Old = old};
                plan.Rows.Add(row);
                string head = old.Name;
                FleetCoach successor;
                if (waiting.TryGetValue(old, out successor))
                {
                    row.Text = head + ": skipped, " + successor.Name + " (" + successor.Coach.Type.Name + ") is ordered for it, delivery " + successor.Coach.BuiltDate.ToString("d MMM yyyy") + ".";
                    continue;
                }
                if (CoachDealService.IsLeasedOut(old) || CharterService.IsOnCharter(old))
                {
                    row.Text = head + ": skipped, it is " + (CoachDealService.IsLeasedOut(old) ? "leased out" : "on a charter") + ".";
                    continue;
                }
                if (old.Routes.Count == 0)
                {
                    row.Ok = true;
                    row.Text = head + ": no route, nothing to take over.";
                    continue;
                }
                head += " on " + string.Join(", ", old.Routes.Select(Code));

                // can the new type fly these routes at all?
                string cant = null;
                foreach (Route r in old.Routes)
                {
                    long bay = Math.Min(r.Destination1.GetMaxBayLength(), r.Destination2.GetMaxBayLength());
                    if (!RightKind(s.NewType, r)) cant = "the " + s.NewType.Name + " is the wrong kind for " + Code(r);
                    else if (s.NewType.Range < r.GetDistance()) cant = "the " + s.NewType.Name + "'s range is too short for " + Code(r);
                    else if (s.NewType.MinBayLength > bay) cant = "the bays of " + Code(r) + " are too short for the " + s.NewType.Name;
                    if (cant != null) break;
                }
                if (cant != null)
                {
                    row.Text = head + ": skipped, " + cant + ".";
                    continue;
                }

                // the coach that takes over
                FleetCoach idleNew = idle.Count > 0 ? idle.Peek() : null;
                Coach marketNew = idleNew == null && s.UsesMarket && market.Count > 0 ? market.Peek() : null;
                bool order = idleNew == null && marketNew == null && canOrder;
                if (idleNew == null && marketNew == null && !order)
                {
                    row.Text = head + ": skipped, no " + s.NewType.Name + " is idle in the fleet" + (s.UsesMarket ? " or for sale on the market" : "")
                               + (s.Orders ? (Orderable(carrier, s.NewType) ? "" : ", and it isn't built any more (or your manufacturer contract doesn't allow it)") : "") + ".";
                    continue;
                }
                CoachSpec spec = idleNew != null ? CoachSpec.Of(idleNew) : marketNew != null ? CoachSpec.Of(marketNew) : CoachSpec.OfType(s.NewType);

                // the trips
                var entries = old.Routes.SelectMany(r => r.TimeTable.Entries.Where(e => e.Coach == old)).ToList();
                string fit;
                if (CoachReplaceService.FirstClash(spec, entries) == null)
                {
                    row.Mode = ReplaceMode.SameTimes;
                    fit = "same times";
                }
                else if (s.Slower == SlowerChoice.Skip)
                {
                    row.Text = head + ": skipped, the " + s.NewType.Name + " is too slow for its trips.";
                    continue;
                }
                else if (old.Routes.Count > 1)
                {
                    row.Text = head + ": skipped, the " + s.NewType.Name + " is too slow for its trips on " + old.Routes.Count
                               + " routes together. Replace it route by route on the route screen.";
                    continue;
                }
                else
                {
                    Route r = old.Routes[0];
                    FitPlan shift = s.Slower == SlowerChoice.ShiftOrKeep ? CoachReplaceService.PlanShift(r, old, spec, new List<RouteTimeTableEntry>()) : null;
                    FitPlan keep = shift == null ? CoachReplaceService.PlanKeep(r, old, spec, new List<RouteTimeTableEntry>()) : null;
                    if (shift != null)
                    {
                        row.Mode = ReplaceMode.ShiftTimes;
                        fit = "slower: " + shift.Summary;
                    }
                    else if (keep != null)
                    {
                        row.Mode = ReplaceMode.KeepFitting;
                        fit = "slower: " + keep.Summary;
                    }
                    else
                    {
                        row.Text = head + ": skipped, the " + s.NewType.Name + " is too slow for its trips and no round trip fits.";
                        continue;
                    }
                }

                if (order)
                {
                    row.Ordered = true;
                    row.Head = head;
                    row.Fit = fit;
                }
                else if (marketNew != null)
                {
                    double cost = MarketCost(carrier, marketNew, s.Lease);
                    if (cost > money)
                    {
                        row.Text = head + ": skipped, not enough money to " + (s.Lease ? "lease" : "buy") + (plan.Buying > 0 ? " another " : " a ") + s.NewType.Name + ".";
                        continue;
                    }
                    money -= cost;
                    row.Cost = cost;
                    market.Dequeue();
                    row.MarketNew = marketNew;
                    row.Text = head + ": " + (s.Lease ? "lease" : "buy") + " " + marketNew.TailNumber + " (built " + marketNew.BuiltDate.Year + ") for "
                               + Math.Round(cost).ToString("N0") + (s.Lease ? " deposit" : "") + ", " + fit + ".";
                }
                else
                {
                    idle.Dequeue();
                    row.IdleNew = idleNew;
                    row.Text = head + ": " + idleNew.Name + " (idle in the fleet), " + fit + ".";
                }
                row.Ok = !row.Ordered;
            }

            // the new coach are one order: its price with the quantity discount, paid now (or the down payment); when the
            // money isn't enough, fewer are ordered
            var ordered = plan.Rows.Where(r => r.Ordered).ToList();
            while (ordered.Count > 0 && OrderNow(OrderPrice(carrier, s.NewType, ordered.Count), s.DownPayment) > money)
            {
                var last = ordered.Last();
                last.Ordered = false;
                last.Text = last.Head + ": skipped, not enough money to order another " + s.NewType.Name + ".";
                ordered.Remove(last);
            }
            if (ordered.Count > 0)
            {
                plan.OrderTotal = OrderPrice(carrier, s.NewType, ordered.Count);
                plan.OrderNow = OrderNow(plan.OrderTotal, s.DownPayment);
                plan.OrderDelivery = DeliveryDate(s.NewType, ordered.Count);
                foreach (var r in ordered)
                {
                    r.Ok = true;
                    r.Cost = plan.OrderNow/ordered.Count;
                    r.Text = r.Head + ": a new " + s.NewType.Name + " is ordered, delivery " + plan.OrderDelivery.ToString("d MMM yyyy") + "; it takes over then ("
                             + r.Fit + "), " + r.Old.Name + " runs until then.";
                }
            }

            int skipped = plan.Rows.Count(r => !r.Ok);
            string after = s.AfterOld == OldCoachChoice.Sell ? "sold (leased ones go back)" : s.AfterOld == OldCoachChoice.ListForSale ? "listed for sale" : "kept idle";
            plan.Summary = plan.Rows.Count == 0
                ? "You have no " + s.OldType.Name + "."
                : plan.Replacing + " of " + plan.Rows.Count + " " + s.OldType.Name + " replaced by " + s.NewType.Name
                  + (plan.Buying > 0 ? " (" + plan.Buying + " " + (s.Lease ? "leased" : "bought") + " for " + Math.Round(plan.Rows.Where(r => r.Ok && r.MarketNew != null).Sum(r => r.Cost)).ToString("N0") + ")" : "")
                  + (plan.Ordering > 0 ? " (" + plan.Ordering + " ordered new for " + Math.Round(plan.OrderTotal).ToString("N0")
                                         + (s.DownPayment ? ", " + Math.Round(plan.OrderNow).ToString("N0") + " now and the rest on delivery" : "")
                                         + ", delivery " + plan.OrderDelivery.ToString("d MMM yyyy") + ")" : "")
                  + "; the old ones without a route afterwards are " + after + "." + (skipped > 0 ? " " + skipped + " skipped (see the list)." : "");
            return plan;
        }

        private static Station HomebaseFor(Carrier carrier, FleetCoach old, CoachType type)
        {
            List<Station> bases = CarrierHelpers.GetHomebases(carrier, type.MinBayLength);
            if (old != null && (bases.Contains(old.Homebase) || old.Homebase?.GetMaxBayLength() >= type.MinBayLength)) return old.Homebase;
            return bases.FirstOrDefault();
        }

        // takes the old coach's drivers that may fly the new type, and hires the rest
        private static void Crew(FleetCoach old, FleetCoach nw)
        {
            string family = nw.Coach.Type.CoachFamily;
            foreach (var driver in old.Drivers.ToList())
            {
                if (nw.Drivers.Count >= nw.Coach.Type.DriverCrew) break;
                if (driver.Coaches == null || !driver.Coaches.Contains(family)) continue;
                old.RemoveDriver(driver);
                driver.Coach = nw;
                nw.AddDriver(driver);
            }
            if (nw.Drivers.Count < nw.Coach.Type.DriverCrew)
            {
                CarrierHelpers.HireCoachDrivers(nw);
            }
        }

        private static string DoneWithOld(Carrier carrier, FleetCoach old, OldCoachChoice choice)
        {
            if (old.Routes.Count > 0) return null;
            old.Status = FleetCoach.CoachStatus.Stopped;
            if (choice == OldCoachChoice.KeepIdle) return null;
            if (choice == OldCoachChoice.ListForSale)
            {
                string m;
                CoachDealService.List(old, 1.0, out m);
                return m;
            }

            if (old.Purchased == FleetCoach.PurchasedType.BoughtDownPayment)
            {
                return old.Name + " is not paid in full, so it is kept.";
            }
            long price = old.Purchased == FleetCoach.PurchasedType.Bought ? old.Coach.GetPrice() : 0;
            foreach (var driver in old.Drivers.ToList()) old.RemoveDriver(driver);
            carrier.RemoveCoach(old);
            if (price > 0)
            {
                CarrierHelpers.AddCarrierInvoice(carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, price);
                return old.Name + " sold for " + price.ToString("N0") + ".";
            }
            return old.Name + " went back to the lessor.";
        }

        /// <summary>Carries out the plan (worked out again first, as the fleet or the market may have changed). Returns what happened.</summary>
        public static List<string> Renew(RenewalSettings settings)
        {
            Carrier carrier = Whose(settings?.Carrier);
            RenewalPlan plan = Plan(settings);
            var report = new List<string>();
            DateTime now = GameObject.GetInstance().GameTime;
            var ordered = plan.Rows.Where(r => r.Ok && r.Ordered).ToList();
            if (ordered.Count > 0)
            {
                // one order for all of them: paid now (or the down payment, the rest on each delivery)
                CarrierHelpers.AddCarrierInvoice(carrier, now, Invoice.InvoiceType.Purchases, -plan.OrderNow);
                if (carrier.Contract != null && carrier.Contract.Manufacturer == settings.NewType.Manufacturer)
                {
                    carrier.Contract.PurchasedCoaches += ordered.Count;
                }
            }
            foreach (RenewalRow row in plan.Rows)
            {
                FleetCoach old = row.Old;
                if (!row.Ok)
                {
                    report.Add(row.Text);
                    continue;
                }

                if (row.Ordered)
                {
                    Station home = HomebaseFor(carrier, old, settings.NewType) ?? old.Homebase;
                    var coach = new Coach(Guid.NewGuid().ToString(), settings.NewType, carrier.Profile.Country.TailNumbers.GetNextTailNumber(carrier), plan.OrderDelivery);
                    Coaches.AddCoach(coach);
                    var nw = new FleetCoach(settings.DownPayment ? FleetCoach.PurchasedType.BoughtDownPayment : FleetCoach.PurchasedType.Bought, now, carrier, coach, home)
                    {
                        RemainingPayment = settings.DownPayment ? Math.Max(0, (plan.OrderTotal - plan.OrderNow)/ordered.Count) : 0,
                        ReplacesOnDelivery = old,
                        RenewalMode = (int) row.Mode,
                        RenewalAfterOld = (int) settings.AfterOld
                    };
                    carrier.AddCoach(nw);
                    report.Add("Ordered " + nw.Name + " (" + settings.NewType.Name + "), delivery " + plan.OrderDelivery.ToString("d MMM yyyy") + ": it takes over "
                               + string.Join(", ", old.Routes.Select(Code)) + " from " + old.Name + " then.");
                    continue;
                }

                if (old.Routes.Count > 0)
                {
                    FleetCoach nw = row.IdleNew;
                    if (nw == null)
                    {
                        Coach a = row.MarketNew;
                        if (a.Carrier != null)
                        {
                            report.Add(old.Name + ": skipped, " + a.TailNumber + " was sold in the meantime.");
                            continue;
                        }
                        Station home = HomebaseFor(carrier, old, a.Type);
                        if (home == null)
                        {
                            report.Add(old.Name + ": skipped, none of your stations can be the home base of a " + a.Type.Name + ".");
                            continue;
                        }
                        double cost = MarketCost(carrier, a, settings.Lease);
                        if (cost > carrier.Money)
                        {
                            report.Add(old.Name + ": skipped, not enough money left.");
                            continue;
                        }
                        if (settings.Lease)
                        {
                            if (CarrierHelpers.NeedsNewRegistration(carrier, a.TailNumber))
                            {
                                a.TailNumber = carrier.Profile.Country.TailNumbers.GetNextTailNumber(carrier);
                            }
                            carrier.AddCoach(FleetCoach.PurchasedType.Leased, a, home);
                            CarrierHelpers.AddCarrierInvoice(carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Rents, -cost);
                            nw = carrier.Fleet.Last(f => f.Coach == a);
                        }
                        else
                        {
                            nw = CarrierHelpers.BuyCoach(carrier, a, home, carrier.Contract?.Discount ?? 0);
                        }
                        if (carrier.Contract != null) carrier.Contract.PurchasedCoaches++;
                    }

                    Crew(old, nw);
                    var done = new List<string>();
                    foreach (Route route in old.Routes.ToList())
                    {
                        string m;
                        if (CoachReplaceService.Replace(route, old, nw, row.Mode, out m))
                        {
                            done.Add(Code(route));
                        }
                        else
                        {
                            report.Add(old.Name + " on " + Code(route) + ": " + m);
                        }
                    }
                    if (done.Count > 0)
                    {
                        report.Add(nw.Name + " (" + nw.Coach.Type.Name + ") took over " + string.Join(", ", done) + " from " + old.Name
                                   + (row.Mode == ReplaceMode.SameTimes ? "" : row.Mode == ReplaceMode.ShiftTimes ? ", times shifted" : ", keeping the round trips that fit") + "."
                                   + (nw.Status == FleetCoach.CoachStatus.Stopped ? " It has no full crew yet." : ""));
                    }
                }

                string after = DoneWithOld(carrier, old, settings.AfterOld);
                if (after != null) report.Add(after);
            }
            RouteIndex.Invalidate();
            return report;
        }

        /// <summary>
        ///     Every day: coach ordered in a renewal that have been delivered (and paid) take over the routes of the
        ///     coach they were ordered for, which is then sold, listed or kept idle as chosen. A news item says what
        ///     happened.
        /// </summary>
        public static void ProcessDeliveries()
        {
            var game = GameObject.GetInstance();
            Carrier carrier = game?.HumanCarrier;
            if (carrier == null) return;
            foreach (FleetCoach nw in carrier.Fleet.ToList())
            {
                if (nw.ReplacesOnDelivery == null || nw.Coach.BuiltDate > game.GameTime || nw.Purchased == FleetCoach.PurchasedType.BoughtDownPayment) continue;
                FleetCoach old = nw.ReplacesOnDelivery;
                nw.ReplacesOnDelivery = null;
                var mode = (ReplaceMode) nw.RenewalMode;
                var after = (OldCoachChoice) nw.RenewalAfterOld;
                var lines = new List<string>();
                if (!carrier.Fleet.Contains(old) || old.Routes.Count == 0)
                {
                    lines.Add(nw.Name + " (" + nw.Coach.Type.Name + ") has been delivered. " + old.Name + " has no routes any more, so there is nothing to take over: put "
                              + nw.Name + " on a route yourself.");
                }
                else
                {
                    Crew(old, nw);
                    foreach (Route route in old.Routes.ToList())
                    {
                        string m;
                        bool ok = CoachReplaceService.Replace(route, old, nw, mode, out m);
                        // the timetable may have changed since the order: try shifting, then keeping what fits
                        if (!ok && mode == ReplaceMode.SameTimes) ok = CoachReplaceService.Replace(route, old, nw, ReplaceMode.ShiftTimes, out m);
                        if (!ok && mode != ReplaceMode.KeepFitting) ok = CoachReplaceService.Replace(route, old, nw, ReplaceMode.KeepFitting, out m);
                        lines.Add(ok ? m : old.Name + " keeps " + Code(route) + ": " + m);
                    }
                    string done = DoneWithOld(carrier, old, after);
                    if (done != null) lines.Add(done);
                }
                RouteIndex.Invalidate();
                game.NewsBox.AddNews(new News(News.NewsType.FleetNews, game.GameTime, "Fleet renewal: " + nw.Name + " delivered", string.Join("\n", lines)));
            }
        }
    }
}
