using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.General.Statistics;
using TheBusProject.Models.Drivers;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    /// <summary>One reason a rating is higher or lower than the neutral 50.</summary>
    public class RatingFactor
    {
        public RatingFactor(string text, double points)
        {
            Text = text;
            Points = points;
        }

        public string Text { get; }
        public double Points { get; }
    }

    /// <summary>Where one rating is heading this month, and why.</summary>
    public class RatingTarget
    {
        public RatingTarget(string name)
        {
            Name = name;
            Factors = new List<RatingFactor>();
        }

        public string Name { get; }
        public double Base { get; set; } = 50;
        public List<RatingFactor> Factors { get; }
        public string Tip { get; set; }

        public int Value => (int) Math.Round(Math.Max(0, Math.Min(100, Base + Factors.Sum(f => f.Points))));

        public void Add(string text, double points)
        {
            if (Math.Abs(points) >= 0.5) Factors.Add(new RatingFactor(text, Math.Round(points, 1)));
        }
    }

    /// <summary>
    ///     The five carrier ratings (customer happiness, employee happiness, maintenance, safety, security).
    ///     The original game filled them only from random events, which were switched off, so every carrier
    ///     stayed at 50. They are now worked out from what the carrier does, once a month, and move a third
    ///     of the way to the new value each month so one bad month does not wipe out a good record.
    /// </summary>
    public static class RatingsService
    {
        public const string Customer = "Customer happiness";
        public const string Employee = "Employee happiness";
        public const string Maintenance = "Maintenance";
        public const string Safety = "Safety";
        public const string Security = "Security";

        public static readonly string[] Names = { Customer, Employee, Maintenance, Safety, Security };

        private static double Clamp(double v, double min, double max)
        {
            return Math.Max(min, Math.Min(max, v));
        }

        private static bool HasFacility(Carrier carrier, string uid)
        {
            return carrier.Facilities != null && carrier.Facilities.Any(f => f != null && f.Uid == uid);
        }

        public static int Get(Carrier carrier, string name)
        {
            var r = carrier.Ratings;
            switch (name)
            {
                case Customer: return r.CustomerHappinessRating;
                case Employee: return r.EmployeeHappinessRating;
                case Maintenance: return r.MaintenanceRating;
                case Safety: return r.SafetyRating;
                default: return r.SecurityRating;
            }
        }

        public static int GetLastMonth(Carrier carrier, string name)
        {
            var r = carrier.Ratings;
            switch (name)
            {
                case Customer: return r.LastCustomerHappiness;
                case Employee: return r.LastEmployeeHappiness;
                case Maintenance: return r.LastMaintenance;
                case Safety: return r.LastSafety;
                default: return r.LastSecurity;
            }
        }

        /// <summary>The average of the five ratings.</summary>
        public static int Overall(Carrier carrier)
        {
            return (int) Math.Round(Names.Average(n => (double) Get(carrier, n)));
        }

        public static string Grade(int value)
        {
            if (value >= 85) return "Excellent";
            if (value >= 70) return "Good";
            if (value >= 50) return "Fair";
            if (value >= 35) return "Weak";
            return "Poor";
        }

        private static List<FleetCoach> Fleet(Carrier carrier)
        {
            return carrier.Fleet.ToList().Where(f => f?.Coach != null).ToList();
        }

        private static double OverdueShare(Carrier carrier, List<FleetCoach> fleet)
        {
            // only the player's carrier uses the maintenance check system
            if (!carrier.IsHuman || fleet.Count == 0) return 0;
            int overdue = 0;
            foreach (var f in fleet)
            {
                try
                {
                    if (Enum.GetValues(typeof (MaintenanceCheck)).Cast<MaintenanceCheck>().Any(c => MaintenanceService.IsOverdue(f, c)))
                        overdue++;
                }
                catch
                {
                    // a check that cannot be worked out counts as fine
                }
            }
            return (double) overdue/fleet.Count;
        }

        public static RatingTarget CustomerTarget(Carrier carrier)
        {
            var t = new RatingTarget(Customer);
            var year = GameObject.GetInstance().GameTime.Year;

            double trips = carrier.Statistics.GetStatisticsValue(year, StatisticsTypes.GetStatisticsType("Arrivals"));
            if (trips > 0)
            {
                double otp = carrier.Statistics.GetStatisticsValue(year, StatisticsTypes.GetStatisticsType("On-Time%"));
                t.Add(string.Format("On-time trips {0:0}% this year", otp), Clamp((otp - 80)*0.6, -15, 12));
            }

            var routes = carrier.Routes.ToList().OfType<PassengerRoute>().ToList();
            if (routes.Count > 0)
            {
                double shares = 0, priceRatios = 0;
                int priced = 0;
                foreach (var r in routes)
                {
                    var cls = r.GetRouteCoachClass(CoachClass.ClassType.EconomyClass);
                    if (cls == null) continue;
                    var facilities = cls.GetFacilities();
                    double max = facilities.Select(f => f.Type).Distinct()
                                           .Sum(type => RouteFacilities.GetFacilities(type).Select(x => x.ServiceLevel).DefaultIfEmpty(0).Max());
                    double have = facilities.Sum(f => f.ServiceLevel);
                    shares += max > 0 ? Clamp(have/max, 0, 1) : 0.5;

                    double suggested = PassengerHelpers.GetPassengerPrice(r.Destination1, r.Destination2);
                    if (suggested > 0 && cls.FarePrice > 0)
                    {
                        priceRatios += cls.FarePrice/suggested;
                        priced++;
                    }
                }
                double service = shares/routes.Count;
                t.Add(string.Format("On-board service is {0:0}% of the best available", service*100), Clamp((service - 0.4)*30, -12, 18));
                if (priced > 0)
                {
                    double ratio = priceRatios/priced;
                    t.Add(string.Format("Economy fares are {0:0}% of the usual price", ratio*100), Clamp((1 - ratio)*25, -15, 10));
                }
            }

            double fac = 0;
            if (HasFacility(carrier, "100")) fac += 4; // frequent flyer
            if (HasFacility(carrier, "105")) fac += 2; // online booking
            if (HasFacility(carrier, "106")) fac += 2; // online check-in
            if (HasFacility(carrier, "107")) fac += 3; // wi-fi
            if (HasFacility(carrier, "108")) fac += 3; // VIP
            if (HasFacility(carrier, "101")) fac += 1; // magazine
            if (HasFacility(carrier, "102")) fac += 1; // newspapers
            t.Add("Carrier services (frequent flyer, online booking, Wi-Fi, ...)", fac);

            var fleet = Fleet(carrier).Where(f => f.Coach.Type.TypeCoach == CoachType.TypeOfCoach.Passenger).ToList();
            if (fleet.Count > 0)
            {
                double age = fleet.Average(f => f.Coach.Age);
                t.Add(string.Format("Passenger fleet is {0:0.#} years old on average", age), Clamp((10 - age)*0.6, -8, 4));
            }

            t.Tip = "Fly on time, add food, drinks and entertainment on your routes (Routes > service), keep fares near the usual price and add carrier services such as a frequent flyer program.";
            return t;
        }

        public static RatingTarget EmployeeTarget(Carrier carrier)
        {
            var t = new RatingTarget(Employee);

            if (carrier.Drivers.Count > 0)
            {
                double morale = DriverMoraleService.Average(carrier);
                t.Add(string.Format("Driver morale {0:0} ({1})", morale, DriverMoraleService.MoodText(morale)), Clamp((morale - 65)*0.5, -20, 15));
            }

            var wages = FeeTypes.GetTypes(FeeType.EFeeType.Wage).Where(w => !w.Name.StartsWith("Cab")).ToList();
            if (wages.Count > 0 && carrier.Fees != null)
            {
                double ratio = wages.Average(w => w.DefaultValue > 0 ? carrier.Fees.GetValue(w)/w.DefaultValue : 1);
                t.Add(string.Format("Crew and staff wages are {0:0}% of normal", ratio*100), Clamp((ratio - 1)*40, -20, 15));
            }

            if (carrier.Money < 0) t.Add("The carrier is in debt", -8);

            t.Tip = "Pay drivers well (Staff > Drivers > pay), keep cabin, maintenance and support wages at or above normal (Finances > fees), and keep drivers running rather than sitting unassigned.";
            return t;
        }

        public static RatingTarget MaintenanceTarget(Carrier carrier)
        {
            var t = new RatingTarget(Maintenance);
            var fleet = Fleet(carrier);
            if (fleet.Count == 0)
            {
                t.Add("No coach yet", 0);
                t.Tip = "Buy coach first.";
                return t;
            }

            double condition = fleet.Average(f => f.Coach.Condition);
            t.Base = 0;
            t.Add(string.Format("Fleet condition {0:0}% on average", condition), condition);

            double overdue = OverdueShare(carrier, fleet);
            t.Add(string.Format("{0:0}% of coach have an overdue check", overdue*100), -overdue*30);

            if (HasFacility(carrier, "110")) t.Add("Large maintenance center", 6);
            else if (HasFacility(carrier, "109")) t.Add("Basic maintenance center", 3);

            t.Tip = "Do the A, B, C and D checks on time (My Carrier > Maintenance, or turn on automatic checks) and build a maintenance center.";
            return t;
        }

        public static RatingTarget SafetyTarget(Carrier carrier)
        {
            var t = new RatingTarget(Safety);
            var fleet = Fleet(carrier);
            t.Base = 55;
            if (fleet.Count > 0)
            {
                double condition = fleet.Average(f => f.Coach.Condition);
                t.Add(string.Format("Fleet condition {0:0}%", condition), Clamp((condition - 70)*0.5, -20, 15));

                double age = fleet.Average(f => f.Coach.Age);
                t.Add(string.Format("Fleet age {0:0.#} years", age), Clamp((15 - age)*0.7, -12, 6));

                t.Add("Coach with overdue checks", -OverdueShare(carrier, fleet)*20);
            }

            var ratings = DriverRatings.GetRatings().OrderBy(r => r.CostIndex).ToList();
            var drivers = carrier.Drivers.ToList().Where(p => p.Rating != null).ToList();
            if (ratings.Count > 1 && drivers.Count > 0)
            {
                double level = drivers.Average(p => (double) ratings.IndexOf(p.Rating))/(ratings.Count - 1);
                t.Add(string.Format("Driver ratings ({0:0}% of the top)", level*100), Clamp((level - 0.4)*20, -8, 12));
            }

            t.Tip = "Keep coach in good condition, replace old coach and hire or train higher rated drivers.";
            return t;
        }

        public static RatingTarget SecurityTarget(Carrier carrier)
        {
            var t = new RatingTarget(Security);
            t.Base = 50;

            var stations = carrier.Stations.ToList();
            if (stations.Count > 0)
            {
                int own = stations.Count(a => a.GetCurrentStationFacility(carrier, StationFacility.FacilityType.CheckIn)?.TypeLevel > 0);
                double share = (double) own/stations.Count;
                t.Add(string.Format("Own check-in at {0} of {1} stations", own, stations.Count), share*15);
            }

            if (HasFacility(carrier, "103") || HasFacility(carrier, "104") || HasFacility(carrier, "105"))
                t.Add("Booking system (passenger lists are checked)", 4);
            if (HasFacility(carrier, "106")) t.Add("Online check-in", 3);

            if (carrier.InsurancePolicies != null && carrier.InsurancePolicies.Count > 0)
                t.Add("Insured (insurers ask for security checks)", 5);

            int employee = carrier.Ratings.EmployeeHappinessRating;
            t.Add("Staff happiness", Clamp((employee - 50)*0.25, -10, 10));

            t.Tip = "Run your own check-in at your stations (Stations > facilities), use a booking and online check-in system, take out insurance and keep staff happy.";
            return t;
        }

        public static List<RatingTarget> Targets(Carrier carrier)
        {
            return new List<RatingTarget>
            {
                CustomerTarget(carrier), EmployeeTarget(carrier), MaintenanceTarget(carrier), SafetyTarget(carrier), SecurityTarget(carrier)
            };
        }

        private static int Step(int current, int target, bool first)
        {
            if (first) return target;
            double next = current + (target - current)/3.0;
            if (next > current && next - current < 1) next = current + 1;
            if (next < current && current - next < 1) next = current - 1;
            return (int) Math.Round(Clamp(next, 0, 100));
        }

        /// <summary>Moves the carrier's ratings toward this month's targets. Called once a month for every carrier.</summary>
        public static void ProcessMonth(Carrier carrier)
        {
            try
            {
                var r = carrier.Ratings;
                var t = Targets(carrier);
                bool first = !r.IsCalculated;

                r.LastCustomerHappiness = r.CustomerHappinessRating;
                r.LastEmployeeHappiness = r.EmployeeHappinessRating;
                r.LastMaintenance = r.MaintenanceRating;
                r.LastSafety = r.SafetyRating;
                r.LastSecurity = r.SecurityRating;

                r.CustomerHappinessRating = Step(r.CustomerHappinessRating, t[0].Value, first);
                r.EmployeeHappinessRating = Step(r.EmployeeHappinessRating, t[1].Value, first);
                r.MaintenanceRating = Step(r.MaintenanceRating, t[2].Value, first);
                r.SafetyRating = Step(r.SafetyRating, t[3].Value, first);
                r.SecurityRating = Step(r.SecurityRating, t[4].Value, first);
                if (first)
                {
                    r.LastCustomerHappiness = r.CustomerHappinessRating;
                    r.LastEmployeeHappiness = r.EmployeeHappinessRating;
                    r.LastMaintenance = r.MaintenanceRating;
                    r.LastSafety = r.SafetyRating;
                    r.LastSecurity = r.SecurityRating;
                }
                r.IsCalculated = true;
            }
            catch (Exception)
            {
                // a rating that cannot be worked out this month keeps its value
            }
        }

        /// <summary>How much passengers prefer the carrier: 0.75 at a customer rating of 0, 1 at 50 and 1.25 at 100.</summary>
        public static double CustomerFactor(Carrier carrier)
        {
            if (carrier?.Ratings == null) return 1;
            return 0.75 + Clamp(carrier.Ratings.CustomerHappinessRating, 0, 100)/200.0;
        }
    }
}
