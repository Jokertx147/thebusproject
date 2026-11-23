using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Carriers.CarrierCooperation;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Environment;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.General.Holidays;
using TheBusProject.Models.General.Statistics;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    //the helper class for a turn for a day
    public class DayTurnHelpers
    {
        #region Static Fields

        // Random.Shared is safe to use from the parallel carrier threads (a shared new Random() is not)
        private static Random Rnd => Random.Shared;

        #endregion

        #region Public Methods and Operators

        public static void SimulateCarrierTrips(Carrier carrier)
        {
            var sw = new Stopwatch();
            sw.Start();

            foreach (
                FleetCoach coach in carrier.Fleet.FindAll(f => f.Status != FleetCoach.CoachStatus.Stopped)) //Parallel.ForEach(
                //    carrier.Fleet.FindAll(f => f.Status != FleetCoach.CoachStatus.Stopped),
                //    coach =>
            {
                if (coach.Coach.Carrier == null)
                {
                    // an coach in the fleet must belong to the carrier (a lost link used to stop the whole game)
                    TheBusProject.Infrastructure.StartupLog.Write("Coach " + coach.Name + " was in the fleet of " + carrier.Profile.Name + " without an owner; fixed.");
                    coach.Coach.Carrier = carrier;
                }

                if (coach.CurrentTrip != null)
                {
                    //Boolean stopoverRoute = coach.CurrentTrip.Entry.MainEntry != null;

                    using (Profiler.Measure("trip: landing"))
                        SimulateLanding(coach);
                }

                FleetCoach coach1 = coach;
                IOrderedEnumerable<RouteTimeTableEntry> dayEntries =
                    coach.Routes.Where(r => r.StartDate <= GameObject.GetInstance().GameTime)
                            .SelectMany(r => r.TimeTable.GetEntries(GameObject.GetInstance().GameTime.DayOfWeek))
                            .Where(
                                e =>
                                e.Coach == coach1
                                && (e.TimeTable.Route.Season == Weather.Season.AllYear
                                    || e.TimeTable.Route.Season
                                    == GeneralHelpers.GetSeason(GameObject.GetInstance().GameTime)))
                            .OrderBy(e => e.Time);

                long pfE = Profiler.Start();
                var dayList = dayEntries.ToList();
                Profiler.Stop("trip: the day's timetable", pfE);
                bool ferryOk = !carrier.IsHuman || !carrier.AutoFerry || FerryService.BeforeTrips(coach, dayList);
                if (ferryOk && (GameObject.GetInstance().GameTime > coach.GroundedToDate || GameObject.GetInstance().GameTime <= coach.CoveredUntil))
                {
                    foreach (RouteTimeTableEntry entry in dayList)
                    {
                        if (entry.TimeTable.Route.HasStopovers)
                        {
                            SimulateStopoverTrip(entry);
                        }
                        else
                        {
                            SimulateTrip(entry);
                        }
                    }
                    using (Profiler.Measure("trip: service check"))
                        CheckForService(coach);
                }
            } //);

            sw.Stop();
        }

        #endregion

        #region Methods

        private static void CheckForService(FleetCoach coach)
        {
            const double serviceCheck = 500000000;
            double sinceLastService = coach.Coach.Flown - coach.Coach.LastServiceCheck;

            if (sinceLastService > serviceCheck)
            {
                SimulateService(coach);
            }
        }

        private static void CreatePassengersHappiness(FleetCoach coach)
        {
            const int serviceLevel = 0;
            //coach.Route.DrinksFacility.ServiceLevel + coach.Route.FoodFacility.ServiceLevel + coach.Coach.Coach.getFacility(CoachFacility.FacilityType.Audio).ServiceLevel + coach.Coach.Coach.getFacility(CoachFacility.FacilityType.Seat).ServiceLevel + coach.Coach.Coach.getFacility(CoachFacility.FacilityType.Video).ServiceLevel;
            int happyValue = coach.CurrentTrip.IsOnTime ? 10 : 20;
            happyValue -= (serviceLevel/25);
            for (int i = 0; i < coach.CurrentTrip.GetTotalPassengers(); i++)
            {
                bool isHappy = Rnd.Next(100) > happyValue;

                if (isHappy)
                {
                    PassengerHelpers.AddPassengerHappiness(coach.Coach.Carrier);
                }
            }
        }

        //simulates a trip with stopovers

        //simulates a trip
        private static void SimulateTrip(RouteTimeTableEntry entry)
        {
            FleetCoach coach = entry.Coach;
            coach.LastTripDate = GameObject.GetInstance().GameTime.Date;

            if (entry.TimeTable.Route.HasStopovers || coach.CurrentTrip is StopoverTrip)
            {
                if (coach.CurrentTrip == null || ((StopoverTrip) coach.CurrentTrip).IsLastTrip)
                {
                    coach.CurrentTrip = new StopoverTrip(entry);
                }

                ((StopoverTrip) coach.CurrentTrip).SetNextEntry();
            }
            else
            {
                coach.CurrentTrip = new Trip(entry);
            }

            long pfDelay = Profiler.Start();
            KeyValuePair<FleetCoachHelpers.DelayType, int> delayedMinutes =
                FleetCoachHelpers.GetDelayedMinutes(coach);
            Profiler.Stop("trip: delay", pfDelay);

            //cancelled/delay
            if (delayedMinutes.Value
                >= Convert.ToInt16(coach.Coach.Carrier.GetCarrierPolicy("Cancellation Minutes").PolicyValue))
            {
                if (coach.Coach.Carrier.IsHuman)
                {
                    Trip trip = coach.CurrentTrip;

                    switch (delayedMinutes.Key)
                    {
                        case FleetCoachHelpers.DelayType.CoachProblems:
                            GameObject.GetInstance()
                                      .NewsBox.AddNews(
                                          new News(
                                              News.NewsType.TripNews,
                                              GameObject.GetInstance().GameTime,
                                              Translator.GetInstance().GetString("News", "1004"),
                                              string.Format(
                                                  Translator.GetInstance().GetString("News", "1004", "message"),
                                                  trip.Entry.Destination.TripCode,
                                                  trip.Entry.DepartureStation.Profile.IATACode,
                                                  trip.Entry.Destination.Station.Profile.IATACode)));
                            break;
                        case FleetCoachHelpers.DelayType.BadWeather:
                            GameObject.GetInstance()
                                      .NewsBox.AddNews(
                                          new News(
                                              News.NewsType.TripNews,
                                              GameObject.GetInstance().GameTime,
                                              Translator.GetInstance().GetString("News", "1005"),
                                              string.Format(
                                                  Translator.GetInstance().GetString("News", "1005", "message"),
                                                  trip.Entry.Destination.TripCode,
                                                  trip.Entry.DepartureStation.Profile.IATACode,
                                                  trip.Entry.Destination.Station.Profile.IATACode)));
                            break;
                    }
                }
                coach.Coach.Carrier.Statistics.AddStatisticsValue(
                    GameObject.GetInstance().GameTime.Year,
                    StatisticsTypes.GetStatisticsType("Cancellations"),
                    1);

                double cancellationPercent =
                    coach.Coach.Carrier.Statistics.GetStatisticsValue(
                        GameObject.GetInstance().GameTime.Year,
                        StatisticsTypes.GetStatisticsType("Cancellations"))
                    /(coach.Coach.Carrier.Statistics.GetStatisticsValue(
                        GameObject.GetInstance().GameTime.Year,
                        StatisticsTypes.GetStatisticsType("Arrivals"))
                      + coach.Coach.Carrier.Statistics.GetStatisticsValue(
                          GameObject.GetInstance().GameTime.Year,
                          StatisticsTypes.GetStatisticsType("Cancellations")));
                coach.Coach.Carrier.Statistics.SetStatisticsValue(
                    GameObject.GetInstance().GameTime.Year,
                    StatisticsTypes.GetStatisticsType("Cancellation%"),
                    cancellationPercent*100);

                TripRecords.Cancelled(coach, coach.CurrentTrip, delayedMinutes.Key == FleetCoachHelpers.DelayType.BadWeather ? "bad weather" : "coach problems");
                coach.CurrentTrip = null;
            }
            else
            {
                coach.CurrentTrip.AddDelayMinutes(delayedMinutes.Value);

                if (coach.CurrentTrip.Entry.MainEntry == null)
                {
                    if (coach.CurrentTrip.IsPassengerTrip())
                    {
                        var classes = new List<CoachClass>(coach.Coach.Classes);
                        foreach (CoachClass aClass in classes)
                        {
                            if (aClass == null) continue; // the cabin was being changed on another thread
                            RouteCoachClass rac = ((PassengerRoute) coach.CurrentTrip.Entry.TimeTable.Route).GetRouteCoachClass(aClass.Type);
                            coach.CurrentTrip.Classes.Add(
                                new TripCoachClass(
                                    ((PassengerRoute) coach.CurrentTrip.Entry.TimeTable.Route).GetRouteCoachClass
                                        (aClass.Type),
                                    PassengerHelpers.GetTripPassengers(coach, aClass.Type)));

                            //coach.CurrentTrip.Classes.Add(new TripCoachClass(((PassengerRoute)coach.CurrentTrip.Entry.TimeTable.Route).getRouteCoachClass(aClass.Type),0));
                        }
                    }
                    if (coach.CurrentTrip.IsCargoTrip())
                    {
                        coach.CurrentTrip.Cargo = PassengerHelpers.GetTripCargo(coach);
                    }
                }
                // departures: the carrier, the coach, the station it leaves and the route
                TripStatistics.TakeOff(coach);

                if (coach.CurrentTrip.ExpectedLanding.ToShortDateString()
                    == GameObject.GetInstance().GameTime.ToShortDateString())
                {
                    using (Profiler.Measure("trip: landing"))
                        SimulateLanding(coach);
                }
            }
        }

        //simulates the service of a trip

        //simulates the landing of a trip
        private static void SimulateLanding(FleetCoach coach)
        {
            long pfL = Profiler.Start();
            DateTime landingTime =
                coach.CurrentTrip.TripTime.Add(
                    MathHelpers.GetTripTime(
                        coach.CurrentTrip.Entry.DepartureStation.Profile.Coordinates.ConvertToGeoCoordinate(),
                        coach.CurrentTrip.Entry.Destination.Station.Profile.Coordinates.ConvertToGeoCoordinate(),
                        coach.Coach.Type));
            double fdistance = MathHelpers.GetDistance(
                coach.CurrentTrip.GetDepartureStation(),
                coach.CurrentTrip.Entry.Destination.Station);

            TimeSpan triptime = landingTime.Subtract(coach.CurrentTrip.TripTime);
            const double groundTaxPerPassenger = 5;

            double tax = 0;

            if (coach.CurrentTrip.Entry.Destination.Station.Profile.Country
                != coach.CurrentTrip.GetDepartureStation().Profile.Country)
            {
                tax = 2*tax;
            }

            double ticketsIncome = 0;
            double feesIncome = 0;
            double mealExpenses = 0;
            double fuelExpenses = 0;

            if (coach.CurrentTrip.IsPassengerTrip())
            {
                tax = groundTaxPerPassenger*coach.CurrentTrip.GetTotalPassengers();
                fuelExpenses = FleetCoachHelpers.GetFuelExpenses(coach, fdistance);

                // a seat on a leg of a service is not worth the leg's fare to everybody in it: see
                // Route.ThroughFareFactor, which is 1 on a route that runs non-stop
                var legRoute = (PassengerRoute) coach.CurrentTrip.Entry.TimeTable.Route;
                ticketsIncome += coach.CurrentTrip.Classes.Sum(
                    fac => fac.Passengers*legRoute.GetRouteCoachClass(fac.CoachClass.Type).FarePrice*legRoute.ThroughFareFactor);

                FeeType employeeDiscountType = FeeTypes.GetType("Employee Discount");
                double employeesDiscount = coach.Coach.Carrier.Fees.GetValue(employeeDiscountType);

                double totalDiscount = ticketsIncome*(employeeDiscountType.Percentage/100.0)
                                       *(employeesDiscount/100.0);
                ticketsIncome = ticketsIncome - totalDiscount;

                feesIncome += (from feeType in FeeTypes.GetTypes(FeeType.EFeeType.Fee) where GameObject.GetInstance().GameTime.Year >= feeType.FromYear from fac in coach.CurrentTrip.Classes let percent = 0.10 let maxValue = Convert.ToDouble(feeType.Percentage)*(1 + percent) let minValue = Convert.ToDouble(feeType.Percentage)*(1 - percent) let value = Convert.ToDouble(Rnd.Next((int) minValue, (int) maxValue))/100 select fac.Passengers*value*coach.Coach.Carrier.Fees.GetValue(feeType)).Sum();

                foreach (TripCoachClass fac in coach.CurrentTrip.Classes)
                {
                    foreach (
                        RouteFacility facility in
                            ((PassengerRoute) coach.CurrentTrip.Entry.TimeTable.Route).GetRouteCoachClass(
                                fac.CoachClass.Type).GetFacilities())
                    {
                        if (facility.EType == RouteFacility.ExpenseType.Fixed)
                        {
                            mealExpenses += fac.Passengers*facility.ExpensePerPassenger;
                        }
                        else
                        {
                            FeeType feeType = facility.FeeType;
                            const double percent = 0.10;
                            double maxValue = Convert.ToDouble(feeType.Percentage)*(1 + percent);
                            double minValue = Convert.ToDouble(feeType.Percentage)*(1 - percent);

                            double value = Convert.ToDouble(Rnd.Next((int) minValue, (int) maxValue))/100;

                            mealExpenses -= fac.Passengers*value*coach.Coach.Carrier.Fees.GetValue(feeType);
                        }
                    }
                }
            }
            if (coach.CurrentTrip.IsCargoTrip())
            {
                tax = groundTaxPerPassenger*coach.CurrentTrip.Cargo;
                fuelExpenses = FleetCoachHelpers.GetFuelExpenses(coach, fdistance);

                ticketsIncome = coach.CurrentTrip.Cargo*coach.CurrentTrip.GetCargoPrice();
            }

            pfL = Profiler.Lap("landing: income and costs", pfL);
            Station dest = coach.CurrentTrip.Entry.Destination.Station;
            Station dept = coach.CurrentTrip.Entry.DepartureStation;

            double dist = MathHelpers.GetDistance(
                dest.Profile.Coordinates.ConvertToGeoCoordinate(),
                dept.Profile.Coordinates.ConvertToGeoCoordinate());

            coach.Data.AddOperatingValue(new OperatingValue("Tickets", GameObject.GetInstance().GameTime.Year, GameObject.GetInstance().GameTime.Month, ticketsIncome));
            coach.Data.AddOperatingValue(new OperatingValue("In-trip Services", GameObject.GetInstance().GameTime.Year, GameObject.GetInstance().GameTime.Month, feesIncome));

            coach.Data.AddOperatingValue(new OperatingValue("Fuel Expenses", GameObject.GetInstance().GameTime.Year, GameObject.GetInstance().GameTime.Month, -fuelExpenses));


            double expenses = fuelExpenses + StationHelpers.GetLandingFee(dest, coach.Coach) + tax;
            if (!double.IsNaN(expenses))
                coach.Data.AddOperatingValue(new OperatingValue("Station fees and taxes", GameObject.GetInstance().GameTime.Year, GameObject.GetInstance().GameTime.Month, -(StationHelpers.GetLandingFee(dest, coach.Coach) + tax)));

            if (double.IsNaN(expenses))
            {
                expenses = 0;
            }

            if (double.IsNaN(ticketsIncome) || ticketsIncome < 0)
            {
                ticketsIncome = 0;
            }

            pfL = Profiler.Lap("landing: operating values", pfL);
            FleetCoachHelpers.SetTripStats(coach);
            pfL = Profiler.Lap("landing: trip statistics", pfL);

            long stationIncome = Convert.ToInt64(StationHelpers.GetLandingFee(dest, coach.Coach));
            dest.Income += stationIncome;

            Carrier carrier = coach.Coach.Carrier;

            IEnumerable<CodeshareAgreement> agreements =
                carrier.Codeshares.Where(
                    c => c.Carrier1 == carrier || c.Type == CodeshareAgreement.CodeshareType.BothWays);

            foreach (CodeshareAgreement agreement in agreements)
            {
                Carrier tCarrier = agreement.Carrier1 == carrier ? agreement.Carrier2 : agreement.Carrier1;

                double agreementIncome = ticketsIncome*(CodeshareAgreement.TicketSalePercent/100);

                CarrierHelpers.AddCarrierInvoice(
                    tCarrier,
                    GameObject.GetInstance().GameTime,
                    Invoice.InvoiceType.Tickets,
                    agreementIncome);

                // the partner's share of the tickets comes out of the operating carrier's income
                CarrierHelpers.AddCarrierInvoice(
                    carrier,
                    GameObject.GetInstance().GameTime,
                    Invoice.InvoiceType.Tickets,
                    -agreementIncome);
                coach.CurrentTrip.Entry.TimeTable.Route.AddRouteInvoice(
                    new Invoice(GameObject.GetInstance().GameTime, Invoice.InvoiceType.Tickets, -agreementIncome));
            }

            CarrierHelpers.AddCarrierInvoice(
                carrier,
                GameObject.GetInstance().GameTime,
                Invoice.InvoiceType.TripExpenses,
                -expenses);
            CarrierHelpers.AddCarrierInvoice(
                carrier,
                GameObject.GetInstance().GameTime,
                Invoice.InvoiceType.Tickets,
                ticketsIncome);
            CarrierHelpers.AddCarrierInvoice(
                carrier,
                GameObject.GetInstance().GameTime,
                Invoice.InvoiceType.OnTripIncome,
                -mealExpenses);
            CarrierHelpers.AddCarrierInvoice(
                carrier,
                GameObject.GetInstance().GameTime,
                Invoice.InvoiceType.Fees,
                feesIncome);

            coach.CurrentTrip.Entry.TimeTable.Route.AddRouteInvoice(
                new Invoice(GameObject.GetInstance().GameTime, Invoice.InvoiceType.TripExpenses, -expenses));
            coach.CurrentTrip.Entry.TimeTable.Route.AddRouteInvoice(
                new Invoice(GameObject.GetInstance().GameTime, Invoice.InvoiceType.Tickets, ticketsIncome));
            coach.CurrentTrip.Entry.TimeTable.Route.AddRouteInvoice(
                new Invoice(GameObject.GetInstance().GameTime, Invoice.InvoiceType.OnTripIncome, -mealExpenses));
            coach.CurrentTrip.Entry.TimeTable.Route.AddRouteInvoice(
                new Invoice(GameObject.GetInstance().GameTime, Invoice.InvoiceType.Fees, feesIncome));

            pfL = Profiler.Lap("landing: invoices", pfL);
            double wages = 0;

            if (coach.CurrentTrip.IsPassengerTrip())
            {
                int cabinCrew = ((CoachPassengerType) coach.Coach.Type).Attendants;

                wages = cabinCrew*triptime.TotalHours
                        *coach.Coach.Carrier.Fees.GetValue(FeeTypes.GetType("Cabin Wage"));
                // +(coach.CurrentTrip.Entry.TimeTable.Route.getTotalAttendants() * coach.Coach.Carrier.Fees.getValue(FeeTypes.GetType("Cabin kilometer rate")) * fdistance) + (coach.Coach.Type.DriverCrew * coach.Coach.Carrier.Fees.getValue(FeeTypes.GetType("Cab kilometer rate")) * fdistance);
                // holiday pay is 50% more; the carrier is charged what the route shows
                HolidayYearEvent holiday = HolidayYear.GetHoliday(
                    carrier.Profile.Country,
                    GameObject.GetInstance().GameTime);

                if (holiday != null
                    && (holiday.Holiday.Travel == Holiday.TravelType.Both
                        || holiday.Holiday.Travel == Holiday.TravelType.Normal))
                {
                    wages = wages*1.50;
                }
                CarrierHelpers.AddCarrierInvoice(
                    carrier,
                    GameObject.GetInstance().GameTime,
                    Invoice.InvoiceType.Wages,
                    -wages);

                coach.CurrentTrip.Entry.TimeTable.Route.AddRouteInvoice(
                    new Invoice(GameObject.GetInstance().GameTime, Invoice.InvoiceType.Wages, -wages));

                pfL = Profiler.Lap("landing: wages", pfL);
                CreatePassengersHappiness(coach);
                pfL = Profiler.Lap("landing: happiness", pfL);
            }

            coach.Statistics.AddStatisticsValue(
                GameObject.GetInstance().GameTime.Year,
                StatisticsTypes.GetStatisticsType("Coach_Income"),
                ticketsIncome - expenses - mealExpenses + feesIncome - wages);
            pfL = Profiler.Lap("landing: coach statistics", pfL);
            double tripCosts = TripCosts.Charge(coach, coach.CurrentTrip.Entry.TimeTable.Route, fdistance, triptime.TotalHours, ticketsIncome);
            TripRecords.Landed(coach, landingTime, ticketsIncome + feesIncome, ticketsIncome + feesIncome - mealExpenses - expenses - wages - tripCosts);
            TripLog.Add(coach, fdistance, triptime.TotalHours, ticketsIncome, feesIncome, mealExpenses, fuelExpenses,
                          StationHelpers.GetLandingFee(dest, coach.Coach), tax, wages);

            pfL = Profiler.Lap("landing: trip costs and log", pfL);
            coach.Coach.Flown += fdistance;

            if (coach.CurrentTrip.IsPassengerTrip())
            {
                foreach (
                    Cooperation cooperation in
                        coach.CurrentTrip.Entry.Destination.Station.Cooperations.Where(c => c.Carrier == carrier))
                {
                    double incomePerPax = MathHelpers.GetRandomDoubleNumber(
                        cooperation.Type.IncomePerPax*0.9,
                        cooperation.Type.IncomePerPax*1.1);

                    double incomeFromCooperation = Convert.ToDouble(coach.CurrentTrip.GetTotalPassengers())
                                                   *incomePerPax;

                    CarrierHelpers.AddCarrierInvoice(
                        carrier,
                        GameObject.GetInstance().GameTime,
                        Invoice.InvoiceType.OnTripIncome,
                        incomeFromCooperation);
                }
            }

            if (coach.Coach.Carrier.IsHuman && Infrastructure.Settings.GetInstance().MailsOnLandings)
            {
                GameObject.GetInstance()
                          .NewsBox.AddNews(
                              new News(
                                  News.NewsType.TripNews,
                                  GameObject.GetInstance().GameTime,
                                  $"{coach.Name} landed",
                                  $"Your coach [LI coach={coach.Coach.TailNumber}] has landed in [LI station={dest.Profile.IATACode}], {dest.Profile.Country.Name} with {coach.CurrentTrip.GetTotalPassengers()} passengers.\nThe coach flow from [LI station={dept.Profile.IATACode}], {dept.Profile.Country.Name}"));
            }

            Profiler.Lap("landing: the rest", pfL);
            if (coach.CurrentTrip is StopoverTrip && !((StopoverTrip) coach.CurrentTrip).IsLastTrip)
            {
                SimulateTrip(coach.CurrentTrip.Entry);
            }
            else
            {
                coach.CurrentTrip = null;
            }
        }

        private static void SimulateService(FleetCoach coach)
        {
            const double servicePrice = 100000;

            coach.Coach.LastServiceCheck = coach.Coach.Flown;

            CarrierHelpers.AddCarrierInvoice(
                coach.Coach.Carrier,
                GameObject.GetInstance().GameTime,
                Invoice.InvoiceType.Maintenances,
                -servicePrice);

            coach.Statistics.AddStatisticsValue(
                GameObject.GetInstance().GameTime.Year,
                StatisticsTypes.GetStatisticsType("Coach_Income"),
                -servicePrice);

            coach.GroundedToDate = GameObject.GetInstance().GameTime.AddDays(90);
        }

        private static void SimulateStopoverTrip(RouteTimeTableEntry mainEntry)
        {
            //List<Route> routes = mainEntry.TimeTable.Route.Stopovers.SelectMany(s => s.Legs).ToList();

            //Boolean isInbound = mainEntry.DepartureStation == mainEntry.TimeTable.Route.Destination2;

            SimulateTrip(mainEntry);
            /*
            if (isInbound)
                routes.Reverse();
            TimeSpan time = mainEntry.Time;


            foreach (Route route in routes)
            {
                RouteTimeTable timetable = new RouteTimeTable(route);

                //inbound
                if (isInbound)
                {
                    RouteTimeTableEntry entry = new RouteTimeTableEntry(timetable, mainEntry.Day, time, new RouteEntryDestination(route.Destination1, mainEntry.Destination.TripCode));

                    time = entry.TimeTable.Route.getTripTime(mainEntry.Coach.Coach.Type).Add(RouteTimeTable.MinTimeBetweenTrips); //getTripTime ( 737-900ER SBY-BOS-CPH-AAR)
                    entry.Coach = mainEntry.Coach;
                    entry.MainEntry = mainEntry;

                    SimulateTrip(entry);
                }
                //outbound
                else
                {
                    RouteTimeTableEntry entry = new RouteTimeTableEntry(timetable, mainEntry.Day, time, new RouteEntryDestination(route.Destination2, mainEntry.Destination.TripCode));
                    entry.Coach = mainEntry.Coach;
                    entry.MainEntry = mainEntry;

                    time = time.Add(entry.TimeTable.Route.getTripTime(mainEntry.Coach.Coach.Type)).Add(RouteTimeTable.MinTimeBetweenTrips);

                    SimulateTrip(entry);
                }



            }
            */
        }

        #endregion

        //simulates all trip for an carrier for the current day

        /*
        //sets the statistics from a take off
        private static void SetTakeoffStatistics(FleetCoach coach)
        {
            Station station = coach.CurrentTrip.Entry.DepartureStation;

            station.Statistics.addStatisticsValue(GameObject.GetInstance().GameTime.Year, coach.Coach.Carrier, StatisticsTypes.GetStatisticsType("Departures"), 1);

            double destPassengers = station.Statistics.getStatisticsValue(GameObject.GetInstance().GameTime.Year, coach.Coach.Carrier, StatisticsTypes.GetStatisticsType("Passengers"));
            double destDepartures = station.Statistics.getStatisticsValue(GameObject.GetInstance().GameTime.Year, coach.Coach.Carrier, StatisticsTypes.GetStatisticsType("Arrivals"));
            station.Statistics.setStatisticsValue(GameObject.GetInstance().GameTime.Year, coach.Coach.Carrier, StatisticsTypes.GetStatisticsType("Passengers%"), (int)(destPassengers / destDepartures));


            coach.Coach.Carrier.Statistics.addStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Departures"), 1);
            coach.Statistics.addStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Departures"), 1);
            
            foreach (CoachClass aClass in coach.Coach.Classes)
            {
                RouteCoachClass raClass = coach.CurrentTrip.Entry.TimeTable.Route.getRouteCoachClass(aClass.Type);

                coach.CurrentTrip.Entry.TimeTable.Route.Statistics.addStatisticsValue(raClass, StatisticsTypes.GetStatisticsType("Departures"), 1);
            }
            double coachPassengers = coach.Statistics.getStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Passengers"));
            double coachDepartures = coach.Statistics.getStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Departures"));
            coach.Statistics.setStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Passengers%"), (int)(coachPassengers / coachDepartures));


            double carrierPassengers = coach.Coach.Carrier.Statistics.getStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Passengers"));
            double carrierDepartures = coach.Coach.Carrier.Statistics.getStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Departures"));
            coach.Coach.Carrier.Statistics.setStatisticsValue(GameObject.GetInstance().GameTime.Year, StatisticsTypes.GetStatisticsType("Passengers%"), (int)(carrierPassengers / carrierDepartures));

        }*/
        //creates the happiness for a landed route coach
    }
}