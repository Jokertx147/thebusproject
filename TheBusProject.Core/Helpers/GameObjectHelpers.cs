using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using TheBusProject.Helpers.Workers;
using TheBusProject.Infrastructure;
using TheBusProject.Infrastructure.Enums;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Carriers.CarrierCooperation;
using TheBusProject.Models.Carriers.Subsidiary;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;
using TheBusProject.Models.General.Environment;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.General.HistoricEvents;
using TheBusProject.Models.General.Holidays;
using TheBusProject.Models.General.Statistics;
using TheBusProject.Models.Passengers;
using TheBusProject.Models.Drivers;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    //the helper class for the game object
    public class GameObjectHelpers
    {
        #region Static Fields

        // Random.Shared is safe to use from the parallel carrier threads (a shared new Random() is not)
        private static Random Rnd => Random.Shared;

        private static DateTime _lastTime;

        #endregion

        #region Public Methods and Operators

        /// <summary>The route demand of the game that was just created; the clock starts when it has finished.</summary>
        public static Task DemandCreation { get; private set; } = Task.CompletedTask;

        public static void CreateGame(StartDataObject startData)
        {
            Infrastructure.StartupLog.Write("CreateGame: start");
            if (startData.RealData)
            {
                List<Carrier> notRealCarriers = Carriers.GetCarriers(a => !a.Profile.IsReal && a != startData.Carrier);
                List<Manufacturer> notRealManufacturers = Manufacturers.GetManufacturers(m => !m.IsReal);
                List<CoachType> notRealCoaches =
                    CoachTypes.GetTypes(a => notRealManufacturers.Contains(a.Manufacturer));

                foreach (Carrier notRealCoach in notRealCarriers)
                {
                    Carriers.RemoveCarrier(notRealCoach);
                }

                foreach (CoachType coach in notRealCoaches)
                {
                    CoachTypes.RemoveType(coach);
                }
            }

            int startYear = startData.Year;
            int opponents = startData.NumberOfOpponents;
            Carrier carrier = startData.Carrier;
            Continent continent = startData.Continent;
            Region region = startData.Region;

            GameTimeZone gtz = startData.TimeZone;
            GameObject.GetInstance().DayRoundEnabled = startData.UseDayTurns;
            GameObject.GetInstance().TimeZone = gtz;
            GameObject.GetInstance().Difficulty = startData.Difficulty;
            GameObject.GetInstance().GameTime = new DateTime(startYear, 1, 1);
            GameObject.GetInstance().StartDate = GameObject.GetInstance().GameTime;
            //sets the fuel price
            GameObject.GetInstance().FuelPrice =
                Inflations.GetInflation(GameObject.GetInstance().GameTime.Year).FuelPrice;

            carrier.Profile.Country = startData.HomeCountry ?? carrier.Profile.Country ?? startData.Station?.Profile.Country;
            carrier.Profile.CEO = startData.CEO;

            GameObject.GetInstance().SetHumanCarrier(carrier);
            GameObject.GetInstance().MainCarrier = GameObject.GetInstance().HumanCarrier;

            // set it every time, so a currency from a game played earlier in the same session does not carry over
            GameObject.GetInstance().CurrencyCountry = startData.LocalCurrency ? carrier.Profile.Country : null;
            // AppSettings.GetInstance().resetCurrencyFormat();

            Infrastructure.StartupLog.Write("CreateGame: human carrier set");
            Station station = startData.Station;

            StationHelpers.RentGates(station, carrier, StationContract.ContractType.Full,
                                     Terminal.TerminalType.Passenger, 2);

            StationFacility checkinFacility =
                StationFacilities.GetFacilities(StationFacility.FacilityType.CheckIn).Find(f => f.TypeLevel == 1);
            StationFacility facility =
                StationFacilities.GetFacilities(StationFacility.FacilityType.Service)
                                 .Find((f => f.TypeLevel == 1));

            station.AddStationFacility(
                GameObject.GetInstance().HumanCarrier,
                facility,
                GameObject.GetInstance().GameTime);
            station.AddStationFacility(
                GameObject.GetInstance().HumanCarrier,
                checkinFacility,
                GameObject.GetInstance().GameTime);

            if (continent.Uid != "100" || region.Uid != "100")
            {
                List<Carrier> carriers =
                    Carriers.GetCarriers(
                        a =>
                        (a.Profile.Country?.Region == region
                        || (region.Uid == "100" && continent.HasRegion(a.Profile.Country?.Region)))
                        && a.Profile.Founded <= startYear && a.Profile.Folded > startYear);
                List<Station> stations =
                    Stations.GetStations(
                        a =>
                        (a.Profile.Country?.Region == region
                        || (region.Uid == "100" && continent.HasRegion(a.Profile.Country?.Region)))
                        && a.Profile.Period.From.Year <= startYear && a.Profile.Period.To.Year > startYear);

                Stations.Clear();
                foreach (Station a in stations)
                {
                    Stations.AddStation(a);
                }

                Carriers.Clear();
                foreach (Carrier a in carriers)
                {
                    Carriers.AddCarrier(a);
                }
            }

            Infrastructure.StartupLog.Write("CreateGame: region filtering done, creating carrier demand");
            PassengerHelpers.CreateCarrierDestinationDemand();
            Infrastructure.StartupLog.Write("CreateGame: carrier demand done, creating start up coaches");

            CoachHelpers.CreateStartUpCoaches();
            Infrastructure.StartupLog.Write("CreateGame: start up coaches done, setting up opponents");

            if (startData.RandomOpponents || startData.Opponents == null)
            {
                Setup.SetupMainGame(opponents, startData.SameRegion);
            }
            else
            {
                Setup.SetupMainGame(startData.Opponents, startData.NumberOfOpponents);
            }
            Infrastructure.StartupLog.Write("CreateGame: opponents done");
            List<Station> heliports = Stations.GetStations(a => a.Bays.Exists(r => r.Type == Bay.BayType.Helipad));

            if (startData.InternationalStations)
            {
                List<Station> intlStations = Stations.GetAllStations(a => a.Profile.Type == StationProfile.StationType.LongHaulInternational
                                                                          || a.Profile.Type == StationProfile.StationType.ShortHaulInternational);

                const int minStationsPerRegion = 5;
                foreach (Region stationRegion in Regions.GetRegions())
                {
                    IEnumerable<Station> usedStations = Carriers.GetAllCarriers().SelectMany(a => a.Stations);

                    int countRegionStations = intlStations.Count(a => a.Profile.Country.Region == stationRegion);
                    if (countRegionStations < minStationsPerRegion)
                    {
                        IEnumerable<Station> regionStations =
                            Stations.GetStations(stationRegion)
                                    .Where(a => !intlStations.Contains(a))
                                    .OrderByDescending(a => a.Profile.Size)
                                    .Take(minStationsPerRegion - countRegionStations);

                        intlStations.AddRange(regionStations);
                    }

                    if (startData.SelectedCountries != null)
                    {
                        foreach (Country country in startData.SelectedCountries)
                        {
                            Country country1 = country;
                            List<Station> countryStations =
                                Stations.GetAllStations(
                                    a => (new CountryCurrentCountryResolver().Convert(a.Profile.Country) as Country) == country1 &&
                                         (a.Profile.Type == StationProfile.StationType.Regional || a.Profile.Type == StationProfile.StationType.Domestic));

                            foreach (Station cstation in countryStations)
                                if (!intlStations.Contains(cstation))
                                    intlStations.Add(cstation);
                        }
                    }

                    intlStations.AddRange(usedStations);
                    intlStations.AddRange(heliports);

                    Stations.Clear();

                    foreach (Station majorStation in intlStations.Distinct())
                    {
                        Stations.AddStation(majorStation);
                    }
                }
            }
            if (startData.MajorStations)
            {
                List<Station> majorStations =
                    Stations.GetAllStations(
                        a =>
                        a.Profile.Size == GeneralHelpers.Size.Largest || a.Profile.Size == GeneralHelpers.Size.Large
                        || a.Profile.Size == GeneralHelpers.Size.VeryLarge
                        || a.Profile.Size == GeneralHelpers.Size.Medium);
                IEnumerable<Station> usedStations = Carriers.GetAllCarriers().SelectMany(a => a.Stations);

                const int minStationsPerRegion = 5;
                foreach (Region stationRegion in Regions.GetRegions())
                {
                    int countRegionStations = majorStations.Count(a => a.Profile.Country.Region == stationRegion);
                    if (countRegionStations < minStationsPerRegion)
                    {
                        IEnumerable<Station> regionStations =
                            Stations.GetStations(stationRegion)
                                    .Where(a => !majorStations.Contains(a))
                                    .OrderByDescending(a => a.Profile.Size)
                                    .Take(minStationsPerRegion - countRegionStations);

                        majorStations.AddRange(regionStations);
                    }
                }

                if (startData.SelectedCountries != null)
                {
                    foreach (Country country in startData.SelectedCountries)
                    {
                        Country country1 = country;
                        List<Station> countryStations =
                            Stations.GetAllStations(
                                a => (new CountryCurrentCountryResolver().Convert(a.Profile.Country) as Country) == country1 &&
                                     (a.Profile.Size == GeneralHelpers.Size.Smallest || a.Profile.Size == GeneralHelpers.Size.VerySmall
                                      || a.Profile.Size == GeneralHelpers.Size.Smallest));

                        foreach (Station cstation in countryStations)
                            if (!majorStations.Contains(cstation))
                                majorStations.Add(cstation);
                    }
                }

                majorStations.AddRange(usedStations);
                majorStations.AddRange(heliports);

                Stations.Clear();

                foreach (Station majorStation in majorStations.Distinct())
                {
                    Stations.AddStation(majorStation);
                }
            }

            Infrastructure.StartupLog.Write("CreateGame: stations reduced, creating holidays");
            carrier.MarketFocus = startData.Focus;

            GeneralHelpers.CreateHolidays(GameObject.GetInstance().GameTime.Year);

            // the clock must not run before the route demand exists, otherwise months pass with empty trips
            Infrastructure.StartupLog.Write("CreateGame: game worker waits for the demand");
            GameObjectWorker.GetInstance().IsPaused = true;

            GameObject.GetInstance()
                      .NewsBox.AddNews(
                          new News(
                              News.NewsType.StandardNews,
                              GameObject.GetInstance().GameTime,
                              Translator.GetInstance().GetString("News", "1001"),
                              string.Format(
                                  Translator.GetInstance().GetString("News", "1001", "message"),
                                  GameObject.GetInstance().HumanCarrier.Profile.CEO,
                                  GameObject.GetInstance().HumanCarrier.Profile.IATACode)));

            Action action = () =>
                {
                    var swPax = new Stopwatch();
                    swPax.Start();

                    try
                    {
                        PassengerHelpers.CreateDestinationDemand();
                    }
                    catch (Exception ex)
                    {
                        Infrastructure.StartupLog.Write("CreateGame: creating the demand failed: " + ex);
                    }

                    Console.WriteLine(@"Demand have been created in {0} ms.", swPax.ElapsedMilliseconds);
                    swPax.Stop();

                    Infrastructure.StartupLog.Write("CreateGame: demand ready after " + swPax.ElapsedMilliseconds + " ms, starting game worker");
                    if (startData.IsPaused)
                    {
                        GameObjectWorker.GetInstance().StartPaused();
                    }
                    else
                    {
                        GameObjectWorker.GetInstance().Start();
                    }
                };

            Infrastructure.StartupLog.Write("CreateGame: finished, demand creation continues in background");
            DemandCreation = Task.Factory.StartNew(action);
            //Task.Run(action);
            //Task t2 = Task.Factory.StartNew(action, "passengers");
        }

        public static int GetPassengers(FleetCoach coach, CoachClass.ClassType type)
        {
            using (Profiler.Measure("trip: passengers (per class)"))
                return PassengerHelpers.GetTripPassengers(coach, type);
        }

        public static void SetHistoricEventInfluence(HistoricEventInfluence e, Boolean onEndDate)
        {
            double value = onEndDate ? -e.Value : e.Value;

            switch (e.Type)
            {
                case HistoricEventInfluence.InfluenceType.PassengerDemand:
                    PassengerHelpers.ChangePaxDemand(value);
                    break;
                case HistoricEventInfluence.InfluenceType.FuelPrices:
                    double percent = (100 - value)/100;
                    GameObject.GetInstance().FuelPrice = GameObject.GetInstance().FuelPrice*percent;
                    break;
                case HistoricEventInfluence.InfluenceType.Stocks:
                    break;
            }
        }

        public static void SimulateLanding(FleetCoach coach)
        {
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

            if (coach.CurrentTrip.Entry.Destination.Station.Profile.Country.Name
                != coach.CurrentTrip.GetDepartureStation().Profile.Country.Name)
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

                ticketsIncome += coach.CurrentTrip.Classes.Sum(fac => fac.Passengers*((PassengerRoute) coach.CurrentTrip.Entry.TimeTable.Route).GetRouteCoachClass(fac.CoachClass.Type).FarePrice);

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

            Station dest = coach.CurrentTrip.Entry.Destination.Station;
            Station dept = coach.CurrentTrip.Entry.DepartureStation;

            double dist = MathHelpers.GetDistance(
                dest.Profile.Coordinates.ConvertToGeoCoordinate(),
                dept.Profile.Coordinates.ConvertToGeoCoordinate());

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

            coach.Data.AddOperatingValue(new OperatingValue("Tickets", GameObject.GetInstance().GameTime.Year, GameObject.GetInstance().GameTime.Month, ticketsIncome));
            coach.Data.AddOperatingValue(new OperatingValue("In-trip Services", GameObject.GetInstance().GameTime.Year, GameObject.GetInstance().GameTime.Month, feesIncome));

            coach.Data.AddOperatingValue(new OperatingValue("Fuel Expenses", GameObject.GetInstance().GameTime.Year, GameObject.GetInstance().GameTime.Month, -fuelExpenses));

            FleetCoachHelpers.SetTripStats(coach);

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

                CreatePassengersHappiness(coach);
            }

            coach.Statistics.AddStatisticsValue(
                GameObject.GetInstance().GameTime.Year,
                StatisticsTypes.GetStatisticsType("Coach_Income"),
                ticketsIncome - expenses - mealExpenses + feesIncome - wages);

            double tripCosts = TripCosts.Charge(coach, coach.CurrentTrip.Entry.TimeTable.Route, fdistance, triptime.TotalHours, ticketsIncome);
            TripRecords.Landed(coach, landingTime, ticketsIncome + feesIncome, ticketsIncome + feesIncome - mealExpenses - expenses - wages - tripCosts);

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

            SetNextTrip(coach);

            CheckForService(coach);
        }

        //simulates a "turn"
        public static void SimulateTurn()
        {
            if (GameObject.GetInstance().DayRoundEnabled)
            {
                var sw = new Stopwatch();
                sw.Start();

                GameObject.GetInstance().GameTime = GameObject.GetInstance().GameTime.AddDays(1);

                long pfD = Profiler.Start();
                DoDailyUpdate();
                Profiler.Stop("day: daily update", pfD);

                if (MathHelpers.IsNewMonth(GameObject.GetInstance().GameTime))
                {
                    pfD = Profiler.Start();
                    DoMonthlyUpdate();
                    Profiler.Stop("day: monthly update", pfD);
                }

                if (MathHelpers.IsNewYear(GameObject.GetInstance().GameTime))
                {
                    DoYearlyUpdate();
                }
                Parallel.ForEach(
                    Carriers.GetAllCarriers(),
                    carrier =>
                        {
                            double balance = carrier.Money;

                            long pfDay = Profiler.Start();
                            if (!carrier.IsHuman)
                            {
                                AIHelpers.UpdateCPUCarrier(carrier);
                            }
                            Profiler.Stop("day: computer carrier decisions", pfDay);

                            pfDay = Profiler.Start();
                            DayTurnHelpers.SimulateCarrierTrips(carrier);
                            Profiler.Stop("day: trips", pfDay);

                            double income =
                                carrier.Invoices.MonthlyInvoices.Where(
                                    i =>
                                    i.Day == GameObject.GetInstance().GameTime.Day && i.Month == GameObject.GetInstance().GameTime.Month && i.Year == GameObject.GetInstance().GameTime.Year &&
                                    i.Amount > 0).Sum(i => i.Amount);
                            double expenses =
                                carrier.Invoices.MonthlyInvoices.Where(
                                    i =>
                                    i.Day == GameObject.GetInstance().GameTime.Day && i.Month == GameObject.GetInstance().GameTime.Month && i.Year == GameObject.GetInstance().GameTime.Year &&
                                    i.Amount < 0).Sum(i => i.Amount);

                            carrier.DailyOperatingBalanceHistory.Add(
                                new KeyValuePair<DateTime, KeyValuePair<double, double>>(
                                    GameObject.GetInstance().GameTime,
                                    new KeyValuePair<double, double>(Math.Abs(income), Math.Abs(expenses))));
                        });

                sw.Stop();
            }
            else
            {
                var sw = new Stopwatch();
                sw.Start();
                GameObject.GetInstance().GameTime =
                    GameObject.GetInstance().GameTime.AddMinutes(Infrastructure.Settings.GetInstance().MinutesPerTurn);

                CalibrateTime();

                if (MathHelpers.IsNewDay(GameObject.GetInstance().GameTime))
                {
                    DoDailyUpdate();
                }

                if (MathHelpers.IsNewYear(GameObject.GetInstance().GameTime))
                {
                    DoYearlyUpdate();
                }

                Parallel.ForEach(
                    Carriers.GetAllCarriers(),
                    carrier =>
                        {
                            if (GameObject.GetInstance().GameTime.Minute == 0
                                && GameObject.GetInstance().GameTime.Hour == carrier.Stations.Count%24)
                            {
                                if (!carrier.IsHuman)
                                {
                                    AIHelpers.UpdateCPUCarrier(carrier);
                                }
                            }

                            foreach (FleetCoach coach in carrier.Fleet)
                            {
                                UpdateCoach(coach);
                            }
                        });

                if (MathHelpers.IsNewMonth(GameObject.GetInstance().GameTime))
                {
                    DoMonthlyUpdate();
                }
                sw.Stop();
            }
        }

        #endregion

        #region Methods

        private static void CalibrateTime()
        {
            if (Infrastructure.Settings.GetInstance().MinutesPerTurn == 60 && GameObject.GetInstance().GameTime.Minute != 0)
            {
                GameObject.GetInstance().GameTime =
                    GameObject.GetInstance().GameTime.AddMinutes(-GameObject.GetInstance().GameTime.Minute);
            }

            if (Infrastructure.Settings.GetInstance().MinutesPerTurn == 30 && GameObject.GetInstance().GameTime.Minute == 15)
            {
                GameObject.GetInstance().GameTime = GameObject.GetInstance().GameTime.AddMinutes(15);
            }
        }

        private static void CheckForService(FleetCoach coach)
        {
            const double serviceCheck = 500000000;
            double sinceLastService = coach.Coach.Flown - coach.Coach.LastServiceCheck;

            if (sinceLastService > serviceCheck)
            {
                coach.Status = FleetCoach.CoachStatus.OnService;
                coach.CurrentTrip.Entry.Destination = new RouteEntryDestination(coach.Homebase, "Service", null);
            }
        }

        // the "Clear stats" setting: the routes' recent figures and the stations' passengers by destination (the computer
        // carriers judge a route by its recent load). The carriers' statistics are kept by year and no longer cleared (a
        // month's clearing left the "yearly" figures with this month only); stations keep their last years.
        private static void ClearAllUsedStats()
        {
            Stations.GetAllStations().ForEach(a => a.ClearDestinationPassengerStatistics());
            Stations.GetAllStations().ForEach(a => a.ClearDestinationCargoStatistics());
            CarrierHelpers.ClearRoutesStatistics();
            TripStatistics.PruneStationStatistics();
        }

        //do the daily update

        //creates the monthly summary report for the human carrier
        private static void CreateMontlySummary()
        {
            Carrier carrier = GameObject.GetInstance().HumanCarrier;

            string monthName = GameObject.GetInstance()
                                         .GameTime.AddMonths(-1)
                                         .ToString("MMMM", CultureInfo.InvariantCulture);

            string summary = "[HEAD=Routes Summary]\n";

            IOrderedEnumerable<Route> routes =
                carrier.Routes.OrderByDescending(
                    r =>
                    r.GetBalance(GameObject.GetInstance().GameTime.AddMonths(-1), GameObject.GetInstance().GameTime));
            Station homeStation = carrier.Stations[0];

            foreach (Route route in routes)
            {
                double monthBalance = route.GetBalance(
                    GameObject.GetInstance().GameTime.AddMonths(-1),
                    GameObject.GetInstance().GameTime);
                summary +=
                    $"[WIDTH=300 {route.Destination1.Profile.Name}-{route.Destination2.Profile.Name}]Balance in month: {new ValueCurrencyFormatter().Convert(monthBalance)}\n";
            }

            summary += "\n\n";

            summary += "[HEAD=Destinations Advice]\n";

            Station largestDestination;

            largestDestination =
                homeStation.GetDestinationDemands()
                           .Where(
                               a =>
                               a != null && GeneralHelpers.IsStationActive(a)
                               && !carrier.Routes.Exists(
                                   r =>
                                   (r.Destination1 == homeStation && r.Destination2 == a)
                                   || (r.Destination2 == homeStation && r.Destination1 == a)))
                           .OrderByDescending(
                               a => homeStation.GetDestinationPassengersRate(a, CoachClass.ClassType.EconomyClass))
                           .FirstOrDefault();

            if (largestDestination != null)
            {
                summary +=
                    $"The largest destination in terms of demand from [LI station={homeStation.Profile.IATACode}] where you don't have a route, is [LI station={largestDestination.Profile.IATACode}]";
            }

            summary += "\n[HEAD=Fleet Summary]\n";

            int fleetSize = GameObject.GetInstance().HumanCarrier.DeliveredFleet.Count;
            int inorderFleetSize = GameObject.GetInstance().HumanCarrier.Fleet.Count - fleetSize;

            int coachesWithoutRoute = GameObject.GetInstance().HumanCarrier.DeliveredFleet.Count(f => !f.HasRoute);

            summary += $"[WIDTH=300 Fleet Size:]{fleetSize}\n";
            summary += $"[WIDTH=300 Coaches in Order:]{inorderFleetSize}\n";
            summary += $"[WIDTH=300 Coaches Without Routes:]{coachesWithoutRoute}\n";

            GameObject.GetInstance()
                      .NewsBox.AddNews(
                          new News(
                              News.NewsType.CarrierNews,
                              GameObject.GetInstance().GameTime,
                              $"{monthName} {GameObject.GetInstance().GameTime.AddMonths(-1).Year} Summary",
                              summary));
            // Translator.GetInstance().GetString("News", "1003"), string.Format(Translator.GetInstance().GetString("News", "1003", "message"), coach.Coach.TailNumber, station.Profile.IATACode)));
        }

        //updates an coach

        //creates the happiness for a landed route coach
        private static void CreatePassengersHappiness(FleetCoach coach)
        {
            const int serviceLevel = 0;
            //coach.Route.DrinksFacility.ServiceLevel + coach.Route.FoodFacility.ServiceLevel + coach.Coach.Coach.getFacility(CoachFacility.FacilityType.Audio).ServiceLevel + coach.Coach.Coach.getFacility(CoachFacility.FacilityType.Seat).ServiceLevel + coach.Coach.Coach.getFacility(CoachFacility.FacilityType.Video).ServiceLevel;
            int happyValue = coach.CurrentTrip.IsOnTime ? 10 : 20;
            happyValue -= (serviceLevel/25);
            for (int i = 0; i < coach.CurrentTrip.GetTotalPassengers(); i++)
            {
                Boolean isHappy = Rnd.Next(100) > happyValue;

                if (isHappy)
                {
                    PassengerHelpers.AddPassengerHappiness(coach.Coach.Carrier);
                }
            }
        }

        private static void DoDailyUpdate()
        {
            BayService.ProcessDaily();

            // coach ordered in a fleet renewal take over their routes on delivery
            try
            {
                FleetRenewalService.ProcessDeliveries();
            }
            catch (Exception ex)
            {
                Infrastructure.StartupLog.Write("Fleet renewal deliveries failed: " + ex);
            }

            //Clear stats when it on daily update
            if (Infrastructure.Settings.GetInstance().ClearStats == Intervals.Daily)
            {
                ClearAllUsedStats();
            }

            //Auto save when it on daily
            if (Infrastructure.Settings.GetInstance().AutoSave == Intervals.Daily)
            {
                SerializedLoadSaveHelpers.SaveGame("autosave");
            }

            //Clearing stats as an RAM work-a-round
            Stations.GetAllStations().ForEach(a => a.ClearDestinationPassengerStatistics());
            Stations.GetAllStations().ForEach(a => a.ClearDestinationCargoStatistics());

            List<Carrier> humanCarriers = Carriers.GetCarriers(a => a.IsHuman);

            //Console.WriteLine(GameObject.GetInstance().GameTime.ToShortDateString() + ": " + DateTime.Now.Subtract(LastTime).TotalMilliseconds + " ms." + " : routes: " + totalRoutes + " coaches on route: " + totalCoachesOnRoute);

            _lastTime = DateTime.Now;
            //changes the fuel prices 
            double fuelDiff = Inflations.GetInflation(GameObject.GetInstance().GameTime.Year + 1).FuelPrice
                              - Inflations.GetInflation(GameObject.GetInstance().GameTime.Year).FuelPrice;
            double fuelPrice = (Rnd.NextDouble()*(fuelDiff/4));

            GameObject.GetInstance().FuelPrice =
                Inflations.GetInflation(GameObject.GetInstance().GameTime.Year).FuelPrice + fuelPrice;
            //checks for stations due to close in 14 days
            List<Station> closingStations =
                Stations.GetAllStations(
                    a =>
                    a.Profile.Period.To.ToShortDateString()
                    == GameObject.GetInstance().GameTime.AddDays(14).ToShortDateString());
            List<Station> openingStations =
                Stations.GetAllStations(
                    a =>
                    a.Profile.Period.From.ToShortDateString()
                    == GameObject.GetInstance().GameTime.AddDays(14).ToShortDateString());

            foreach (Station station in closingStations)
            {
                Station reallocatedStation = openingStations.Find(a => a.Profile.Town == station.Profile.Town);

                if (reallocatedStation == null)
                {
                    GameObject.GetInstance()
                              .NewsBox.AddNews(
                                  new News(
                                      News.NewsType.StationNews,
                                      GameObject.GetInstance().GameTime,
                                      "Station closing",
                                      $"The station [LI station={station.Profile.IATACode}]({new StationCodeFormatter().Convert(station)}) is closing in 14 days.\n\rPlease move all routes to another destination."));
                }
                else
                {
                    GameObject.GetInstance()
                              .NewsBox.AddNews(
                                  new News(
                                      News.NewsType.StationNews,
                                      GameObject.GetInstance().GameTime,
                                      "Station closing",
                                      string.Format(
                                          "The station [LI station={0}]({1}) is closing in 14 days.\n\rThe station will be replaced by {2}({3}) and all gates and routes from {0} will be reallocated to {2}.",
                                          station.Profile.IATACode,
                                          new StationCodeFormatter().Convert(station),
                                          reallocatedStation.Profile.Name,
                                          new StationCodeFormatter().Convert(reallocatedStation))));
                }

                CalendarItems.AddCalendarItem(
                    new CalendarItem(
                        CalendarItem.ItemType.StationClosing,
                        station.Profile.Period.To,
                        "Station closing",
                        $"{station.Profile.Name}, {((Country) new CountryCurrentCountryResolver().Convert(station.Profile.Country)).Name}"));
            }

            foreach (Station station in openingStations)
            {
                GameObject.GetInstance()
                          .NewsBox.AddNews(
                              new News(
                                  News.NewsType.StationNews,
                                  GameObject.GetInstance().GameTime,
                                  "Station opening",
                                  $"A new station {station.Profile.Name}({new StationCodeFormatter().Convert(station)}) is opening in 14 days in {station.Profile.Town.Name}, {((Country) new CountryCurrentCountryResolver().Convert(station.Profile.Country)).Name}."));
                CalendarItems.AddCalendarItem(
                    new CalendarItem(
                        CalendarItem.ItemType.StationOpening,
                        station.Profile.Period.From,
                        "Station opening",
                        $"{station.Profile.Name}, {((Country) new CountryCurrentCountryResolver().Convert(station.Profile.Country)).Name}"));
            }
            //checks for new stations which are opening
            List<Station> openedStations =
                Stations.GetAllStations(
                    a =>
                    a.Profile.Period.From.ToShortDateString()
                    == GameObject.GetInstance().GameTime.ToShortDateString());
            List<Station> closedStations =
                Stations.GetAllStations(
                    a =>
                    a.Profile.Period.To.ToShortDateString() == GameObject.GetInstance().GameTime.ToShortDateString());

            //checks for stations which are closing down
            foreach (Station station in closedStations)
            {
                //check for station which are reallocated 
                Station reallocatedStation = openedStations.Find(a => a.Profile.Town == station.Profile.Town);

                if (reallocatedStation != null)
                {
                    IEnumerable<Carrier> carriers =
                        new List<Carrier>(from c in station.CarrierContracts select c.Carrier).Distinct();
                    foreach (Carrier carrier in carriers)
                    {
                        CarrierHelpers.ReallocateStation(station, reallocatedStation, carrier);

                        if (carrier.IsHuman)
                        {
                            GameObject.GetInstance()
                                      .NewsBox.AddNews(
                                          new News(
                                              News.NewsType.StationNews,
                                              GameObject.GetInstance().GameTime,
                                              "Station operations changed",
                                              $"All your gates, routes and facilities has been moved from {station.Profile.Name}({new StationCodeFormatter().Convert(station)}) to [LI station={reallocatedStation.Profile.IATACode}]({new StationCodeFormatter().Convert(reallocatedStation)})"));
                        }
                    }
                }

                GameObject.GetInstance()
                          .NewsBox.AddNews(
                              new News(
                                  News.NewsType.StationNews,
                                  GameObject.GetInstance().GameTime,
                                  "Station closed",
                                  $"The station {station.Profile.Name}({new StationCodeFormatter().Convert(station)}) has now been closed. \n\rAll routes to and from the stations has been cancelled."));

                Station station1 = station;
                IEnumerable<Route> obsoleteRoutes = (from r in Carriers.GetAllCarriers().SelectMany(a => a.Routes)
                                                     where r.Destination1 == station1 || r.Destination2 == station1
                                                     select r);

                foreach (Route route in obsoleteRoutes)
                {
                    route.Banned = true;

                    foreach (FleetCoach coach in route.GetCoaches())
                    {
                        if (coach.Homebase == station)
                        {
                            if (coach.Coach.Carrier.IsHuman)
                            {
                                if (UserInterface.ChooseHomebase != null)
                                {
                                    coach.Homebase = UserInterface.ChooseHomebase(coach);
                                }
                                else
                                {
                                    AIHelpers.SetCoachHomebase(coach);
                                }
                            }
                            else
                            {
                                AIHelpers.SetCoachHomebase(coach);
                            }
                        }
                    }
                }
            }
            //checks for new coach types for purchase
            foreach (
                CoachType aType in
                    CoachTypes.GetTypes(
                        a =>
                        a.Produced.From.ToShortDateString() == GameObject.GetInstance().GameTime.ToShortDateString())
                )
            {
                GameObject.GetInstance()
                          .NewsBox.AddNews(
                              new News(
                                  News.NewsType.CoachNews,
                                  GameObject.GetInstance().GameTime,
                                  "New coach type available",
                                  $"{aType.Manufacturer.Name} has finished the design of {aType.Name} and it is now available for purchase"));

                if (
                    !CarrierFacilities.GetFacilities(f => f is DriverTrainingFacility)
                                      .Exists(f => ((DriverTrainingFacility) f).CoachFamily == aType.CoachFamily))
                {
                    CarrierFacilities.AddFacility(
                        new DriverTrainingFacility(
                            "carrierfacilities",
                            aType.CoachFamily,
                            9000,
                            1000,
                            GameObject.GetInstance().GameTime.Year,
                            0,
                            0,
                            aType.CoachFamily));
                }
            }
            //checks for coach types which are out of production
            foreach (
                CoachType aType in
                    CoachTypes.GetTypes(
                        a => a.Produced.To.ToShortDateString() == GameObject.GetInstance().GameTime.ToShortDateString())
                )
            {
                GameObject.GetInstance()
                          .NewsBox.AddNews(
                              new News(
                                  News.NewsType.CoachNews,
                                  GameObject.GetInstance().GameTime,
                                  "Coach type out of production",
                                  $"{aType.Manufacturer.Name} has taken {aType.Name} out of production"));

                Boolean lastFromManufacturer =
                    CoachTypes.GetAllTypes().Count(t => (t.Manufacturer == aType.Manufacturer
                                                            && t.Produced.To > GameObject.GetInstance().GameTime)) == 0;

                if (lastFromManufacturer)
                {
                    CoachType type = aType;
                    IEnumerable<Carrier> manufacturerContracts =
                        Carriers.GetAllCarriers()
                                .Where(a => a.Contract != null && a.Contract.Manufacturer == type.Manufacturer);

                    foreach (Carrier contractedCarrier in manufacturerContracts)
                    {
                        contractedCarrier.Contract = null;
                    }
                }
            }
            //checks for station facilities for the human carrier
            IEnumerable<CarrierStationFacility> humanStationFacilities =
                (from f in humanCarriers.SelectMany(ai => ai.Stations.SelectMany(a => a.GetStationFacilities(ai)))
                 where f.FinishedDate.ToShortDateString() == GameObject.GetInstance().GameTime.ToShortDateString()
                 select f);

            foreach (CarrierStationFacility facility in humanStationFacilities)
            {
                GameObject.GetInstance()
                          .NewsBox.AddNews(
                              new News(
                                  News.NewsType.StationNews,
                                  GameObject.GetInstance().GameTime,
                                  "Station facility",
                                  $"Your station facility {facility.Facility.Name} at [LI station={facility.Station.Profile.IATACode}] is now finished building"));
                facility.FinishedDate = GameObject.GetInstance().GameTime;
            }
            //checks for changed trip restrictions
            foreach (
                TripRestriction restriction in
                    TripRestrictions.GetRestrictions()
                                      .FindAll(
                                          r =>
                                          r.StartDate.ToShortDateString() == GameObject.GetInstance().GameTime.ToShortDateString()
                                          || r.EndDate.ToShortDateString()
                                          == GameObject.GetInstance().GameTime.ToShortDateString()))
            {
                string restrictionNewsText = "";
                if (restriction.Type == TripRestriction.RestrictionType.Trips)
                {
                    restrictionNewsText = string.Format(restriction.StartDate.ToShortDateString()
                                                        == GameObject.GetInstance().GameTime.ToShortDateString() ? "All trips from {0} to {1} have been banned" : "The ban for all trips from {0} to {1} have been lifted", restriction.From.Name, restriction.To.Name);
                }
                if (restriction.Type == TripRestriction.RestrictionType.Carriers)
                {
                    restrictionNewsText = string.Format(restriction.StartDate.ToShortDateString()
                                                        == GameObject.GetInstance().GameTime.ToShortDateString() ? "All carriers running from {0} running to {1} have been blacklisted" : "The blacklist on all carriers from {0} running to {1} have been lifted", restriction.From.Name, restriction.To.Name);
                }
                if (restriction.StartDate.ToShortDateString() == GameObject.GetInstance().GameTime.ToShortDateString())
                {
                    if (restriction.Type == TripRestriction.RestrictionType.Trips)
                    {
                        IEnumerable<Route> bannedRoutes = (from r in Carriers.GetAllCarriers().SelectMany(a => a.Routes)
                                                           where
                                                               TripRestrictions.HasRestriction(
                                                                   r.Destination1.Profile.Country,
                                                                   r.Destination2.Profile.Country,
                                                                   GameObject.GetInstance().GameTime)
                                                           select r);

                        foreach (Route route in bannedRoutes)
                        {
                            route.Banned = true;
                        }
                    }
                }
                GameObject.GetInstance()
                          .NewsBox.AddNews(
                              new News(
                                  News.NewsType.StandardNews,
                                  GameObject.GetInstance().GameTime,
                                  "Trip restriction",
                                  restrictionNewsText));
            }
            //checks for historic events
            foreach (HistoricEvent e in HistoricEvents.GetHistoricEvents(GameObject.GetInstance().GameTime))
            {
                GameObject.GetInstance()
                          .NewsBox.AddNews(
                              new News(News.NewsType.StandardNews, GameObject.GetInstance().GameTime, e.Name, e.Text));

                foreach (HistoricEventInfluence influence in e.Influences)
                {
                    SetHistoricEventInfluence(influence, false);
                }
            }
            //checks for historic events influences ending
            foreach (
                HistoricEventInfluence influence in
                    HistoricEvents.GetHistoricEventInfluences(GameObject.GetInstance().GameTime))
            {
                SetHistoricEventInfluence(influence, true);
            }

            //updates stations
            //Parallel.ForEach(
            //    Stations.GetAllActiveStations(),
            //    station =>
            foreach (Station station in Stations.GetAllActiveStations())
            {
                //StationHelpers.CreateStationWeather(station);

                if (Infrastructure.Settings.GetInstance().MailsOnBadWeather
                    && humanCarriers.SelectMany(a => a.Stations.FindAll(aa => aa == station)).Any()
                    && station.Weather != null
                    && station.Weather[station.Weather.Length - 1] != null
                    && (station.Weather[station.Weather.Length - 1].WindSpeed == Weather.eWindSpeed.ViolentStorm
                        || station.Weather[station.Weather.Length - 1].WindSpeed == Weather.eWindSpeed.Hurricane))
                {
                    GameObject.GetInstance()
                              .NewsBox.AddNews(
                                  new News(
                                      News.NewsType.StationNews,
                                      GameObject.GetInstance().GameTime,
                                      Translator.GetInstance().GetString("News", "1002"),
                                      string.Format(
                                          Translator.GetInstance().GetString("News", "1002", "message"),
                                          station.Profile.IATACode,
                                          GameObject.GetInstance().GameTime.AddDays(station.Weather.Length - 1).DayOfWeek)));
                }
                // chs, 2011-01-11 changed for delivery of terminals
                foreach (Terminal terminal in station.Terminals.GetTerminals())
                {
                    if (terminal.DeliveryDate.Year == GameObject.GetInstance().GameTime.Year
                        && terminal.DeliveryDate.Month == GameObject.GetInstance().GameTime.Month
                        && terminal.DeliveryDate.Day == GameObject.GetInstance().GameTime.Day)
                    {
                        if (terminal.Carrier == null)
                        {
                            GameObject.GetInstance()
                                      .NewsBox.AddNews(
                                          new News(
                                              News.NewsType.StationNews,
                                              GameObject.GetInstance().GameTime,
                                              "Construction of terminal",
                                              $"[LI station={station.Profile.IATACode}], {station.Profile.Country.Name} has build a new terminal with {terminal.Gates.NumberOfGates} gates"));
                        }

                        if (terminal.Carrier != null && terminal.Carrier.IsHuman)
                        {
                            GameObject.GetInstance()
                                      .NewsBox.AddNews(
                                          new News(
                                              News.NewsType.StationNews,
                                              GameObject.GetInstance().GameTime,
                                              "Construction of terminal",
                                              $"Your terminal at [LI station={station.Profile.IATACode}], {station.Profile.Country.Name} is now finished and ready for use."));
                        }

                        if (terminal.Carrier != null)
                        {
                            Terminal terminal1 = terminal;
                            IEnumerable<StationContract> oldContracts = new List<StationContract>(station.GetCarrierContracts(terminal.Carrier)).Where(c => c.TerminalType == terminal1.Type);

                            var stationContracts = oldContracts as StationContract[] ?? oldContracts.ToArray();
                            if (stationContracts.Any())
                            {
                                int totalGates = stationContracts.Sum(c => c.NumberOfGates);

                                int gatesDiff = totalGates - terminal.Gates.NumberOfGates;

                                if (gatesDiff > 0)
                                {
                                    int length = stationContracts.Max(c => c.Length);
                                    var newContract = new StationContract(
                                        terminal.Carrier,
                                        station,
                                        StationContract.ContractType.Full,
                                        terminal.Type,
                                        GameObject.GetInstance().GameTime,
                                        gatesDiff,
                                        length,
                                        StationHelpers.GetYearlyContractPayment(
                                            station,
                                            StationContract.ContractType.Full,
                                            gatesDiff,
                                            length)/2,
                                        true);

                                    StationHelpers.AddCarrierContract(newContract);
                                }

                                foreach (StationContract oldContract in stationContracts)
                                {
                                    station.RemoveCarrierContract(oldContract);

                                    for (int i = 0; i < oldContract.NumberOfGates; i++)
                                    {
                                        Gate oldGate =
                                            station.Terminals.GetGates().First(g => terminal != null && g.Carrier == terminal.Carrier);
                                        oldGate.Carrier = null;
                                    }
                                }
                            }
                            double yearlyPayment = StationHelpers.GetYearlyContractPayment(
                                station,
                                StationContract.ContractType.Full,
                                terminal.Gates.NumberOfGates,
                                20);

                            StationHelpers.AddCarrierContract(
                                new StationContract(
                                    terminal.Carrier,
                                    station,
                                    StationContract.ContractType.Full,
                                    terminal.Type,
                                    GameObject.GetInstance().GameTime,
                                    terminal.Gates.NumberOfGates,
                                    20,
                                    yearlyPayment*0.75,
                                    true));

                            if (
                                terminal.Station.GetStationFacility(
                                    terminal.Carrier,
                                    StationFacility.FacilityType.CheckIn).TypeLevel == 0)
                            {
                                StationFacility checkinFacility =
                                    StationFacilities.GetFacilities(StationFacility.FacilityType.CheckIn)
                                                     .Find(f => f.TypeLevel == 1);

                                terminal.Station.AddStationFacility(
                                    terminal.Carrier,
                                    checkinFacility,
                                    GameObject.GetInstance().GameTime);
                            }
                        }
                    }
                        //new gates in an existing terminal
                    else
                    {
                        int numberOfNewGates =
                            terminal.Gates.GetGates()
                                    .Count(
                                        g =>
                                        g.DeliveryDate.ToShortDateString()
                                        == GameObject.GetInstance().GameTime.ToShortDateString());

                        if (numberOfNewGates > 0)
                        {
                            GameObject.GetInstance()
                                      .NewsBox.AddNews(
                                          new News(
                                              News.NewsType.StationNews,
                                              GameObject.GetInstance().GameTime,
                                              "Expansion of terminal",
                                              $"[LI station={station.Profile.IATACode}], {station.Profile.Country.Name} has expanded {terminal.Name} with {numberOfNewGates} gates"));

                            double yearlyPayment = StationHelpers.GetYearlyContractPayment(
                                station,
                                StationContract.ContractType.Full,
                                numberOfNewGates + terminal.Gates.NumberOfGates,
                                20);

                            StationContract terminalContract =
                                station.CarrierContracts.Find(c => c.Terminal != null && c.Terminal == terminal);

                            if (terminalContract != null)
                            {
                                terminalContract.NumberOfGates += numberOfNewGates;
                                terminalContract.YearlyPayment = yearlyPayment;

                                for (int i = 0; i < numberOfNewGates; i++)
                                {
                                    Gate newGate = station.Terminals.GetGates().First(g => g.Carrier == null);
                                    newGate.Carrier = terminalContract.Carrier;
                                }
                            }
                        }
                    }
                    //expired contracts
                    var carrierContracts =
                        new List<StationContract>(
                            station.CarrierContracts.FindAll(
                                c =>
                                c.ExpireDate.ToShortDateString()
                                == GameObject.GetInstance().GameTime.ToShortDateString()));

                    foreach (StationContract contract in carrierContracts)
                    {
                        if (contract.AutoRenew)
                        {
                            contract.ContractDate = GameObject.GetInstance().GameTime;
                            contract.ExpireDate = GameObject.GetInstance().GameTime.AddYears(contract.Length);

                            if (contract.Carrier.IsHuman)
                            {
                                GameObject.GetInstance()
                                          .NewsBox.AddNews(
                                              new News(
                                                  News.NewsType.StationNews,
                                                  GameObject.GetInstance().GameTime,
                                                  "Station contract renewed",
                                                  $"Your contract for {contract.NumberOfGates} gates at [LI station={contract.Station.Profile.IATACode}], {contract.Station.Profile.Country.Name} is now been renewed"));
                            }
                        }
                        else
                        {
                            for (int i = 0; i < contract.NumberOfGates; i++)
                            {
                                Gate gate =
                                    station.Terminals.GetGates().First(g => contract != null && g.Carrier == contract.Carrier);
                                gate.Carrier = null;
                            }

                            if (contract.Carrier.IsHuman)
                            {
                                int totalContractGates =
                                    station.CarrierContracts.Where(c => c.Carrier.IsHuman).Sum(c => c.NumberOfGates);

                                var carrierRoutes =
                                    new List<Route>(StationHelpers.GetStationRoutes(station, contract.Carrier));

                                var remainingContracts =
                                    new List<StationContract>(
                                        station.CarrierContracts.FindAll(
                                            c => c.Carrier == contract.Carrier && c != contract));

                                Boolean canFillRoutes = StationHelpers.CanFillRoutesEntries(
                                    station,
                                    contract.Carrier,
                                    remainingContracts,
                                    Weather.Season.AllYear);

                                if (!canFillRoutes)
                                {
                                    GameObject.GetInstance()
                                              .NewsBox.AddNews(
                                                  new News(
                                                      News.NewsType.StationNews,
                                                      GameObject.GetInstance().GameTime,
                                                      "Station contract expired",
                                                      $"Your contract for {contract.NumberOfGates} gates at [LI station={contract.Station.Profile.IATACode}], {contract.Station.Profile.Country.Name} is now expired, and a number of routes has been cancelled"));

                                    int currentRoute = 0;
                                    while (!canFillRoutes)
                                    {
                                        Route routeToDelete = carrierRoutes[currentRoute];

                                        foreach (FleetCoach fCoach in routeToDelete.GetCoaches())
                                        {
                                            fCoach.Status = FleetCoach.CoachStatus.Stopped;
                                            fCoach.RemoveRoute(routeToDelete);
                                        }

                                        contract.Carrier.RemoveRoute(routeToDelete);

                                        currentRoute++;

                                        canFillRoutes = StationHelpers.CanFillRoutesEntries(
                                            station,
                                            contract.Carrier,
                                            remainingContracts,
                                            Weather.Season.AllYear);
                                    }
                                }
                                else
                                {
                                    GameObject.GetInstance()
                                              .NewsBox.AddNews(
                                                  new News(
                                                      News.NewsType.StationNews,
                                                      GameObject.GetInstance().GameTime,
                                                      "Station contract expired",
                                                      $"Your contract for {contract.NumberOfGates} gates at [LI station={contract.Station.Profile.IATACode}], {contract.Station.Profile.Country.Name} is now expired"));
                                }

                                station.RemoveCarrierContract(contract);
                            }
                            else
                            {
                                int numberOfRoutes = StationHelpers.GetStationRoutes(station, contract.Carrier).Count;

                                if (numberOfRoutes > 0)
                                {
                                    contract.ContractDate = GameObject.GetInstance().GameTime;
                                    contract.ExpireDate = GameObject.GetInstance().GameTime.AddYears(contract.Length);
                                }
                                else
                                {
                                    station.RemoveCarrierContract(contract);
                                }
                            }
                        }
                    }
                }
                //checks for station expansions
                foreach (StationExpansion expansion in station.Profile.Expansions.Where(e => e.Date.ToShortDateString() == GameObject.GetInstance().GameTime.ToShortDateString()))
                {
                    StationHelpers.SetStationExpansion(station, expansion);
                }
            } //);
            //checks for coaches for the human carrier
            foreach (
                FleetCoach coach in
                    humanCarriers.SelectMany(
                        a =>
                        a.Fleet.FindAll(
                            f =>
                            f.Coach.BuiltDate == GameObject.GetInstance().GameTime
                            && f.Purchased != FleetCoach.PurchasedType.BoughtDownPayment)))
            {
                if (coach.Coach.Carrier == GameObject.GetInstance().HumanCarrier)
                {
                    GameObject.GetInstance()
                              .NewsBox.AddNews(
                                  new News(
                                      News.NewsType.FleetNews,
                                      GameObject.GetInstance().GameTime,
                                      "Delivery of coach",
                                      $"Your new coach [LI coach={coach.Coach.TailNumber}] as been delivered to your fleet.\nThe coach is currently at [LI station={coach.Homebase.Profile.IATACode}], {coach.Homebase.Profile.Country.Name}"));
                }
                else
                {
                    GameObject.GetInstance()
                              .NewsBox.AddNews(
                                  new News(
                                      News.NewsType.FleetNews,
                                      GameObject.GetInstance().GameTime,
                                      "Delivery of coach",
                                      $"The new coach [LI coach={coach.Coach.TailNumber}] as been delivered for [LI carrier={coach.Coach.Carrier.Profile.IATACode}].\nThe coach is currently at [LI station={coach.Homebase.Profile.IATACode}], {coach.Homebase.Profile.Country.Name}"));
                }
            }

            //Parallel.ForEach(
            //    Carriers.GetAllCarriers(),
            //    carrier =>
            foreach (Carrier carrier in Carriers.GetAllCarriers())

            {
                lock (carrier.Fleet)
                {
                    var fleet = new List<FleetCoach>(carrier.Fleet);
                    foreach (FleetCoach coach in
                        fleet.FindAll(
                            a =>
                            a != null
                            && a.Coach.BuiltDate.ToShortDateString()
                               == GameObject.GetInstance().GameTime.ToShortDateString()
                            && a.Purchased == FleetCoach.PurchasedType.BoughtDownPayment))
                    {
                        double deliveryPayment = coach.RemainingPayment > 0
                                                     ? coach.RemainingPayment
                                                     : coach.Coach.Type.Price;

                        if (carrier.Money >= deliveryPayment)
                        {
                            CarrierHelpers.AddCarrierInvoice(
                                carrier,
                                GameObject.GetInstance().GameTime,
                                Invoice.InvoiceType.Purchases,
                                -deliveryPayment);
                            coach.RemainingPayment = 0;
                            coach.Purchased = FleetCoach.PurchasedType.Bought;
                        }
                        else
                        {
                            carrier.RemoveCoach(coach);

                            if (carrier.IsHuman)
                            {
                                GameObject.GetInstance()
                                          .NewsBox.AddNews(
                                              new News(
                                                  News.NewsType.FleetNews,
                                                  GameObject.GetInstance().GameTime,
                                                  "Delivery of coach",
                                                  $"Your new coach {coach.Name} can't be delivered to your fleet.\nYou don't have enough money to purchase it."));
                            }
                        }
                    }
                }

                if (carrier.Contract != null
                    && carrier.Contract.ExpireDate.ToShortDateString()
                    == GameObject.GetInstance().GameTime.ToShortDateString())
                {
                    int missingCoaches = carrier.Contract.Coaches - carrier.Contract.PurchasedCoaches;

                    if (missingCoaches > 0)
                    {
                        double missingFee = (carrier.Contract.GetTerminationFee()/(carrier.Contract.Length*2))
                                            *missingCoaches;
                        CarrierHelpers.AddCarrierInvoice(
                            carrier,
                            GameObject.GetInstance().GameTime,
                            Invoice.InvoiceType.Purchases,
                            -missingFee);

                        if (carrier.IsHuman)
                        {
                            GameObject.GetInstance()
                                      .NewsBox.AddNews(
                                          new News(
                                              News.NewsType.FleetNews,
                                              GameObject.GetInstance().GameTime,
                                              "Contract expired",
                                              $"Your contract with {carrier.Contract.Manufacturer.Name} has now expired.\nYou didn't purchased enough coaches with costs a fee of {missingFee:C} for missing {missingCoaches} coaches"));
                        }
                    }
                    else if (carrier.IsHuman)
                    {
                        GameObject.GetInstance()
                                  .NewsBox.AddNews(
                                      new News(
                                          News.NewsType.FleetNews,
                                          GameObject.GetInstance().GameTime,
                                          "Contract expired",
                                          $"Your contract with {carrier.Contract.Manufacturer.Name} has now expired."));
                    }

                    carrier.Contract = null;
                }
                //checks for students educated
                IEnumerable<DriverStudent> educatedStudents =
                    carrier.TripSchools.SelectMany(
                        f =>
                        f.Students.FindAll(
                            s =>
                            s.EndDate.ToShortDateString()
                            == GameObject.GetInstance().GameTime.ToShortDateString()));

                foreach (DriverStudent student in educatedStudents)
                {
                    var driver = new Driver(student.Profile, GameObject.GetInstance().GameTime, student.Rating);

                    if (student.CoachFamily != "")
                    {
                        driver.AddCoachFamily(student.CoachFamily);
                    }

                    student.Instructor.RemoveStudent(student);
                    student.Instructor.TripSchool.RemoveStudent(student);
                    student.Instructor = null;

                    carrier.AddDriver(driver);
                    driver.CarrierSignedDate = GameObject.GetInstance().GameTime;

                    if (carrier.IsHuman)
                    {
                        GameObject.GetInstance()
                                  .NewsBox.AddNews(
                                      new News(
                                          News.NewsType.TripNews,
                                          GameObject.GetInstance().GameTime,
                                          Translator.GetInstance().GetString("News", "1006"),
                                          string.Format(
                                              Translator.GetInstance().GetString("News", "1006", "message"),
                                              driver.Profile.Name)));
                    }
                }

                IEnumerable<Driver> trainedDrivers =
                    carrier.Drivers.Where(
                        p =>
                        p.Training != null
                        && p.Training.EndDate.ToShortDateString()
                        == GameObject.GetInstance().GameTime.ToShortDateString());

                foreach (Driver driver in trainedDrivers)
                {
                    driver.AddCoachFamily(driver.Training.CoachFamily);

                    if (carrier.IsHuman)
                    {
                        GameObject.GetInstance()
                                  .NewsBox.AddNews(
                                      new News(
                                          News.NewsType.TripNews,
                                          GameObject.GetInstance().GameTime,
                                          Translator.GetInstance().GetString("News", "1015"),
                                          string.Format(
                                              Translator.GetInstance().GetString("News", "1015", "message"),
                                              driver.Profile.Name,
                                              driver.Training.CoachFamily)));
                    }

                    driver.Training = null;
                }

                //new graduates and drivers back from training go to coach that need a driver, when the player wants it
                if (carrier.IsHuman && carrier.AutoAssignDrivers)
                {
                    TripSchoolService.AssignIdleDrivers(carrier);
                }
            } //);
            //checks for mergers
            foreach (CarrierMerger merger in CarrierMergers.GetCarrierMergers(GameObject.GetInstance().GameTime))
            {
                if (merger.Type == CarrierMerger.MergerType.Merger)
                {
                    CarrierHelpers.SwitchCarrier(merger.Carrier2, merger.Carrier1);

                    Carriers.RemoveCarrier(merger.Carrier2);

                    if (merger.NewName != null && merger.NewName.Length > 1)
                    {
                        merger.Carrier1.Profile.Name = merger.NewName;
                    }
                }
                if (merger.Type == CarrierMerger.MergerType.Subsidiary)
                {
                    string oldLogo = merger.Carrier2.Profile.Logo;

                    var sCarrier = new SubsidiaryCarrier(
                        merger.Carrier1,
                        merger.Carrier2.Profile,
                        merger.Carrier2.Mentality,
                        merger.Carrier2.MarketFocus,
                        merger.Carrier2.License,
                        merger.Carrier2.CarrierRouteFocus);

                    CarrierHelpers.SwitchCarrier(merger.Carrier2, merger.Carrier1);

                    merger.Carrier1.AddSubsidiaryCarrier(sCarrier);

                    Carriers.RemoveCarrier(merger.Carrier2);

                    sCarrier.Profile.Logos = merger.Carrier2.Profile.Logos;
                    sCarrier.Profile.Color = merger.Carrier2.Profile.Color;
                }

                GameObject.GetInstance()
                          .NewsBox.AddNews(
                              new News(
                                  News.NewsType.CarrierNews,
                                  GameObject.GetInstance().GameTime,
                                  "Carrier merger",
                                  merger.Name));
            }
            /*
            //does monthly budget work
           DateTime budgetExpires = GameObject.GetInstance().HumanCarrier.Budget.BudgetExpires;
            if (budgetExpires <= GameObject.GetInstance().GameTime.AddDays(30))
            {
                GameObject.GetInstance().NewsBox.addNews(new News(News.NewsType.Carrier_News, GameObject.GetInstance().GameTime, "Budget Expires Soon", "Your budget will expire within the next 30 days. Please go to the budget screen and adjust it as needed and click 'Apply'."));
            }
            else if (budgetExpires <= GameObject.GetInstance().GameTime)
            {
                //GraphicsModel.PageModel.PageFinancesModel.PageFinances.ResetValues();
            }

            if (GameObject.GetInstance().HumanCarrier.Money < GameObject.GetInstance().HumanCarrier.Budget.TotalBudget / 12)
            {
                WPFMessageBox.Show("Low Cash!", "Your current cash is less than your budget deduction for the month! Decrease your budget immediately or you will be negative!", WPFMessageBoxButtons.Ok);
            }
            else { GameObject.GetInstance().HumanCarrier.Money -= GameObject.GetInstance().HumanCarrier.Budget.TotalBudget / 12; }
             * */

            //check for insurance settlements and do maintenance
            foreach (Carrier a in Carriers.GetAllCarriers())
            {
                CarrierHelpers.CheckInsuranceSettlements(a);

                var coaches = new List<FleetCoach>(a.Fleet);
                foreach (FleetCoach coach in coaches)
                {
                    if (coach != null)
                    {
                        FleetCoachHelpers.DoMaintenance(coach);
                        FleetCoachHelpers.RestoreMaintRoutes(coach);
                    }
                }

                if (a == GameObject.GetInstance().HumanCarrier)
                {
                    CharterService.ProcessDaily(a);
                    FerryService.ProcessDaily(a);
                    CoachDealService.ProcessDaily(a);
                    MaintenanceService.ProcessAuto(a);
                    CharterService.Refresh();
                }

                //checks for special contracts
                var sContracts = new List<SpecialContract>(a.SpecialContracts.Where(s => s.Date <= GameObject.GetInstance().GameTime));

                foreach (SpecialContract sc in sContracts)
                {
                    if (sc.Date.ToShortDateString() == GameObject.GetInstance().GameTime.ToShortDateString() && sc.Type.IsFixedDate)
                    {
                        foreach (SpecialContractRoute route in sc.Type.Routes)
                        {
                            PassengerHelpers.ChangePaxDemand(route.Departure, route.Destination, (int) route.PassengersPerDay);

                            if (route.BothWays)
                                PassengerHelpers.ChangePaxDemand(route.Destination, route.Departure, (int) route.PassengersPerDay);
                        }
                    }
                    if (CarrierHelpers.CheckSpecialContract(sc))
                    {
                        a.SpecialContracts.Remove(sc);

                        if (sc.Type.IsFixedDate)
                        {
                            foreach (SpecialContractRoute route in sc.Type.Routes)
                            {
                                PassengerHelpers.ChangePaxDemand(route.Departure, route.Destination, -(int) route.PassengersPerDay);

                                if (route.BothWays)
                                    PassengerHelpers.ChangePaxDemand(route.Destination, route.Departure, -(int) route.PassengersPerDay);
                            }
                        }
                    }
                }
            }

            if (GameObject.GetInstance().GameTime.Day%7 == 0)
            {
                foreach (Carrier carrier in Carriers.GetAllCarriers())
                    carrier.OverallScore += StatisticsHelpers.GetWeeklyScore(carrier);
                // GameObject.GetInstance().HumanCarrier.OverallScore +=
                //   StatisticsHelpers.GetWeeklyScore(GameObject.GetInstance().HumanCarrier);
            }
            //checks for new special contract types
            List<SpecialContractType> randomSpecialContracts = SpecialContractTypes.GetRandomTypes();
            List<SpecialContractType> fixedSpecialContracts =
                SpecialContractTypes.GetTypes().FindAll(s => s.IsFixedDate && GameObject.GetInstance().GameTime.ToShortDateString() == s.Period.From.AddMonths(-1).ToShortDateString());

            var existingContracts = new List<SpecialContractType>(GameObject.GetInstance().Contracts);

            foreach (SpecialContractType sct in existingContracts)
            {
                if (sct.LastDate.AddMonths(1).ToShortDateString() == GameObject.GetInstance().GameTime.ToShortDateString())
                    GameObject.GetInstance().Contracts.Remove(sct);
                else
                {
                    Boolean taken = false;
                    int i = 0;

                    while (!taken && i < Carriers.GetCarriers(a => !a.IsHuman).Count)
                    {
                        Carrier carrier = Carriers.GetCarriers(a => !a.IsHuman)[i];
                        taken = AIHelpers.WillTakeSpecialContract(carrier, sct);

                        if (taken)
                        {
                            DateTime startdate = sct.IsFixedDate ? sct.Period.From : GameObject.GetInstance().GameTime;

                            var sc = new SpecialContract(sct, startdate, carrier);
                            carrier.SpecialContracts.Add(sc);

                            GameObject.GetInstance().Contracts.Remove(sct);
                        }

                        i++;
                    }
                }
            }

            foreach (SpecialContractType sct in fixedSpecialContracts)
            {
                if (sct.LastDate == DateTime.MinValue)
                    sct.LastDate = GameObject.GetInstance().GameTime;

                if (!GameObject.GetInstance().Contracts.Contains(sct) && !Carriers.GetAllCarriers().Any(al => al.SpecialContracts.Any(c => c.Type == sct)))
                    GameObject.GetInstance().Contracts.Add(sct);
            }

            foreach (SpecialContractType sct in randomSpecialContracts)
            {
                if (sct.LastDate == DateTime.MinValue)
                    sct.LastDate = GameObject.GetInstance().GameTime;

                int monthsSinceLast = MathHelpers.GetAgeMonths(sct.LastDate);

                int monthsFrequency = 12/sct.Frequency;

                //mf = 12, ms = 1 => procent = lille, mf = 6, ms = 6 => procent = medium, mf = 1, ms = 12 => procent = høj

                int value = 100 - (monthsSinceLast - monthsFrequency);

                //a contract type that an carrier already holds is not offered again until it is finished
                Boolean held = Carriers.GetAllCarriers().Any(al => al.SpecialContracts.Any(c => c.Type == sct));
                Boolean createContract = !held && !GameObject.GetInstance().Contracts.Contains(sct) && Rnd.Next(value) == 0;

                if (createContract)
                {
                    sct.LastDate = GameObject.GetInstance().GameTime;

                    GameObject.GetInstance().Contracts.Add(sct);
                }
            }
        }

        //removes the intrip magazine, the newspaper service and the magazines and newspapers on routes after 2020
        private static void RemovePrintedMedia()
        {
            int year = GameObject.GetInstance().GameTime.Year;

            if (year <= CarrierFacilities.PrintedMediaLastYear)
            {
                return;
            }

            foreach (Carrier carrier in Carriers.GetAllCarriers().ToList())
            {
                carrier.Facilities.RemoveAll(f => CarrierFacilities.IsDiscontinued(f, year));

                foreach (Route route in carrier.Routes.ToList())
                {
                    var passengerRoute = route as PassengerRoute;

                    if (passengerRoute == null)
                    {
                        continue;
                    }

                    foreach (RouteCoachClass routeClass in passengerRoute.Classes)
                    {
                        foreach (RouteFacility.FacilityType type in new[]
                        {
                            RouteFacility.FacilityType.Magazines,
                            RouteFacility.FacilityType.Newspapers
                        })
                        {
                            RouteFacility current = routeClass.GetFacility(type);
                            RouteFacility none = RouteFacilities.GetBasicFacility(type);

                            if (current != null && none != null && current != none)
                            {
                                routeClass.AddFacility(none);
                            }
                        }
                    }
                }
            }
        }

        // computer controlled carriers that really stopped flying close down in the year their data says so.
        // The carrier the player controls is never closed, so a player can keep an carrier running past its real end.
        private static void CloseDefunctCarriers()
        {
            try
            {
                DateTime now = GameObject.GetInstance().GameTime;

                var defunct = Carriers.GetAllCarriers()
                                      .Where(
                                          a =>
                                          !a.IsHuman && !(a is SubsidiaryCarrier) && a.Profile.Folded <= now.Year
                                          && now.Month >= GetClosureMonth(a)
                                          && !CarrierMergers.GetCarrierMergers()
                                                            .Any(m => m.Carrier2 == a && m.Date >= now))
                                      .ToList();

                foreach (Carrier carrier in defunct)
                {
                    CeaseOperations(carrier);
                }
            }
            catch (Exception e)
            {
                Infrastructure.StartupLog.Write("CloseDefunctCarriers failed: " + e);
            }
        }

        // spreads the closures over the year, the same month every time for the same carrier
        private static int GetClosureMonth(Carrier carrier)
        {
            int sum = 0;
            foreach (char c in carrier.Profile.IATACode ?? carrier.Profile.Name ?? "")
            {
                sum += c;
            }

            return 1 + sum%12;
        }

        private static void CeaseOperations(Carrier carrier)
        {
            // routes and the coaches flying them
            foreach (Route route in new List<Route>(carrier.Routes))
            {
                carrier.RemoveRoute(route);

                if (route.HasCoach)
                {
                    route.GetCoaches().ForEach(a => a.RemoveRoute(route));
                }
            }

            // the fleet goes to the used market
            foreach (FleetCoach coach in new List<FleetCoach>(carrier.Fleet))
            {
                carrier.RemoveCoach(coach);
            }

            foreach (Driver driver in new List<Driver>(carrier.Drivers))
            {
                carrier.RemoveDriver(driver);
            }

            foreach (Alliance alliance in new List<Alliance>(carrier.Alliances))
            {
                alliance.RemoveMember(carrier);
            }

            foreach (CodeshareAgreement agreement in new List<CodeshareAgreement>(carrier.Codeshares))
            {
                agreement.Carrier1.RemoveCodeshareAgreement(agreement);
                agreement.Carrier2.RemoveCodeshareAgreement(agreement);
            }

            // gates and station facilities
            foreach (Station station in new List<Station>(carrier.Stations))
            {
                foreach (StationContract contract in station.GetCarrierContracts(carrier))
                {
                    for (int i = 0; i < contract.NumberOfGates; i++)
                    {
                        Gate gate = station.Terminals.GetGates().FirstOrDefault(g => g.Carrier == carrier);

                        if (gate != null)
                        {
                            gate.Carrier = null;
                        }
                    }

                    station.RemoveCarrierContract(contract);
                }

                foreach (StationFacility facility in station.GetCurrentStationFacilities(carrier).ToList())
                {
                    StationFacility none =
                        StationFacilities.GetFacilities(facility.Type).Find(f => f.TypeLevel == 0);

                    if (none != null)
                    {
                        station.AddStationFacility(carrier, none, GameObject.GetInstance().GameTime);
                    }
                }

                carrier.RemoveStation(station);
            }

            foreach (SubsidiaryCarrier subsidiary in new List<SubsidiaryCarrier>(carrier.Subsidiaries))
            {
                CarrierHelpers.MakeSubsidiaryCarrierIndependent(subsidiary);
            }

            foreach (CarrierMerger merger in
                CarrierMergers.GetCarrierMergers().Where(m => m.Carrier1 == carrier || m.Carrier2 == carrier).ToList())
            {
                CarrierMergers.RemoveCarrierMerger(merger);
            }

            Carriers.RemoveCarrier(carrier);

            GameObject.GetInstance()
                      .NewsBox.AddNews(
                          new News(
                              News.NewsType.CarrierNews,
                              GameObject.GetInstance().GameTime,
                              "Carrier ceased operations",
                              $"{carrier.Profile.Name} has ceased operations."));
        }

        /// <summary>
        ///     Each carrier's and route's past months into one amount per type (see Invoices.CompactBefore): the
        ///     current month keeps its days. Monthly, and when a game is loaded.
        /// </summary>
        public static int CompactInvoices()
        {
            var now = GameObject.GetInstance().GameTime;
            int removed = 0;
            foreach (Carrier carrier in Carriers.GetAllCarriers().ToList())
            {
                lock (carrier.Invoices) removed += carrier.Invoices.CompactBefore(now.Year, now.Month);
                foreach (Route route in carrier.Routes.ToList())
                {
                    removed += route.GetInvoices().CompactBefore(now.Year, now.Month);
                    foreach (var stop in route.Stopovers.ToList())
                        foreach (var leg in stop.Legs.ToList())
                            removed += leg.GetInvoices().CompactBefore(now.Year, now.Month);
                }
            }
            return removed;
        }

        private static void DoMonthlyUpdate()
        {
            // the month just ended: month-end figures and the player's report (before its invoices are put together)
            CarrierReportService.MonthEnded();
            CompactInvoices();
            RemovePrintedMedia();

            CloseDefunctCarriers();

            //Clear stats when it on monthly
            if (Infrastructure.Settings.GetInstance().ClearStats == Intervals.Monthly)
            {
                ClearAllUsedStats();
            }

            //Auto save when it on monthly
            if (Infrastructure.Settings.GetInstance().AutoSave == Intervals.Monthly)
            {
                SerializedLoadSaveHelpers.SaveGame("autosave");
            }

            // The second-hand market turns over. What comes off it is everything too old to have a market at all,
            // plus a twelfth of the rest, because a coach that does not sell does not stand on the forecourt for
            // ever. What goes on is enough to bring the market back to the size it should be, aged against this
            // year rather than the year the game began.
            //
            // The old code added three coaches a carrier every month, about ninety, and took off only what was
            // over thirty years old, which cannot happen until two decades in: a game of eight years ended with
            // 1,654 coaches standing unsold. Topping up to a target without any churn is the opposite mistake and
            // was tried first: the market then sat at its target but never made anything new, so the same stock
            // aged in place and after eight years not one coach on offer was under ten years old.
            var leaving = new List<Coach>(Coaches.GetCoachesTooOldToSell());
            var standing = Coaches.GetCoachesForSale();

            int churn = standing.Count/12;
            for (int i = 0; i < churn && standing.Count > 0; i++)
            {
                int k = Rnd.Next(standing.Count);
                leaving.Add(standing[k]);
                standing.RemoveAt(k);
            }

            foreach (Coach coach in leaving)
            {
                Coaches.RemoveCoach(coach);
            }

            int numberOfCoaches = Math.Max(0,
                                           CoachHelpers.ForSalePerCarrier*Carriers.GetNumberOfCarriers()
                                           - Coaches.GetCoachesForSale().Count);

            for (int i = 0; i < numberOfCoaches; i++)
            {
                Coaches.AddCoach(CoachHelpers.CreateUsedCoach());
            }
            //checks for new stations which are opening
            List<Station> openedStations =
                Stations.GetAllStations(
                    a =>
                    a.Profile.Period.From.ToShortDateString()
                    == GameObject.GetInstance().GameTime.ToShortDateString());
            List<Station> closedStations =
                Stations.GetAllStations(
                    a =>
                    a.Profile.Period.To.ToShortDateString() == GameObject.GetInstance().GameTime.ToShortDateString());

            foreach (Station station in openedStations)
            {
                if (closedStations.Find(a => a.Profile.Town == station.Profile.Town) != null)
                {
                    StationHelpers.ReallocateStation(
                        closedStations.Find(a => a.Profile.Town == station.Profile.Town),
                        station);
                }
                else
                {
                    PassengerHelpers.CreateDestinationPassengers(station);
                }

                Station station1 = station;
                foreach (
                    Station dStation in
                        Stations.GetStations(
                            a =>
                            a != station1 && a.Profile.Town != station1.Profile.Town
                            && MathHelpers.GetDistance(
                                a.Profile.Coordinates.ConvertToGeoCoordinate(),
                                station1.Profile.Coordinates.ConvertToGeoCoordinate()) > 25))
                {
                    PassengerHelpers.CreateDestinationPassengers(dStation, station);
                }

                Station station2 = station;
                int count =
                    Stations.GetStations(
                        a =>
                        a.Profile.Town == station2.Profile.Town && station2 != a
                        && a.Terminals.GetNumberOfGates(GameObject.GetInstance().MainCarrier) > 0).Count;

                if (count == 1)
                {
                    Station station3 = station;
                    Station allocateFromStation =
                        Stations.GetStations(
                            a =>
                            a.Profile.Town == station3.Profile.Town && station3 != a
                            && a.Terminals.GetNumberOfGates(GameObject.GetInstance().HumanCarrier) > 0).First();
                    GameObject.GetInstance()
                              .NewsBox.AddNews(
                                  new News(
                                      News.NewsType.StationNews,
                                      GameObject.GetInstance().GameTime,
                                      "New station opened",
                                      $"A new station [LI station={station.Profile.IATACode}]({new StationCodeFormatter().Convert(station)}) is opened in {station.Profile.Town.Name}, {((Country) new CountryCurrentCountryResolver().Convert(station.Profile.Country)).Name}.\n\rYou can reallocate all your operations from {allocateFromStation.Profile.Name}({new StationCodeFormatter().Convert(allocateFromStation)}) for free within the next 30 days"));
                }
                else
                {
                    GameObject.GetInstance()
                              .NewsBox.AddNews(
                                  new News(
                                      News.NewsType.StationNews,
                                      GameObject.GetInstance().GameTime,
                                      "New station opened",
                                      $"A new station [LI station={station.Profile.IATACode}]({new StationCodeFormatter().Convert(station)}) is opened in {station.Profile.Town.Name}, {((Country) new CountryCurrentCountryResolver().Convert(station.Profile.Country)).Name}"));
                }
            }
            //pays all codeshare agreements (one way)
            IEnumerable<CodeshareAgreement> agreements =
                Carriers.GetAllCarriers()
                        .SelectMany(a => a.Codeshares)
                        .Where(c => c.Type == CodeshareAgreement.CodeshareType.OneWay)
                        .Distinct(); // each agreement is on both carriers' lists: pay it once

            foreach (CodeshareAgreement agreement in agreements)
            {
                double amount = CarrierHelpers.GetCodesharingPrice(agreement);
                CarrierHelpers.AddCarrierInvoice(
                    agreement.Carrier1,
                    GameObject.GetInstance().GameTime,
                    Invoice.InvoiceType.CarrierExpenses,
                    -amount);
                CarrierHelpers.AddCarrierInvoice(
                    agreement.Carrier2,
                    GameObject.GetInstance().GameTime,
                    Invoice.InvoiceType.CarrierExpenses,
                    amount);
            }
            //check if drivers are retireing
            const int retirementAge = Driver.RetirementAge;

            //Parallel.ForEach(
            //    Carriers.GetAllCarriers(),
            //    carrier =>
            foreach (Carrier carrier in Carriers.GetAllCarriers())
            {
                List<Driver> driversToRetire =
                    carrier.Drivers.FindAll(
                        p =>
                        p.Profile.Birthdate.AddYears(retirementAge).AddMonths(-1)
                        < GameObject.GetInstance().GameTime);
                var driversToRetirement =
                    new List<Driver>(
                        carrier.Drivers.FindAll(
                            p => p.Profile.Birthdate.AddYears(retirementAge) < GameObject.GetInstance().GameTime));

                IEnumerable<Instructor> instructorsToRetire =
                    carrier.TripSchools.SelectMany(f => f.Instructors)
                           .Where(
                               i =>
                               i.Profile.Birthdate.AddYears(retirementAge).AddMonths(-1)
                               < GameObject.GetInstance().GameTime);
                var instructorsToRetirement =
                    new List<Instructor>(
                        carrier.TripSchools.SelectMany(f => f.Instructors)
                               .Where(i => i.Profile.Birthdate.AddYears(retirementAge) < GameObject.GetInstance().GameTime));

                foreach (Driver driver in driversToRetire)
                {
                    if (carrier.IsHuman)
                    {
                        GameObject.GetInstance()
                                  .NewsBox.AddNews(
                                      new News(
                                          News.NewsType.TripNews,
                                          GameObject.GetInstance().GameTime,
                                          Translator.GetInstance().GetString("News", "1007"),
                                          string.Format(
                                              Translator.GetInstance().GetString("News", "1007", "message"),
                                              driver.Profile.Name,
                                              driver.Profile.Age + 1)));
                    }
                    else
                    {
                        if (driver.Coach != null)
                        {
                            if (Drivers.GetNumberOfUnassignedDrivers() == 0)
                            {
                                GeneralHelpers.CreateDrivers(10);
                            }

                            Driver newDriver =
                                Drivers.GetUnassignedDrivers()
                                      .Find(
                                          p =>
                                          p.Rating == driver.Rating
                                          && p.Coaches.Contains(driver.Coach.Coach.Type.CoachFamily));

                            if (newDriver == null)
                            {
                                GeneralHelpers.CreateDrivers(4, driver.Coach.Coach.Type.CoachFamily);
                                Driver driver1 = driver;
                                newDriver =
                                    Drivers.GetUnassignedDrivers(
                                        p => p.Coaches.Contains(driver1.Coach.Coach.Type.CoachFamily))[0];
                            }

                            carrier.AddDriver(newDriver);

                            newDriver.Coach = driver.Coach;
                            newDriver.Coach.AddDriver(newDriver);

                            driver.Coach.RemoveDriver(driver);
                            driver.Coach = null;
                        }
                    }
                }

                foreach (Instructor instructor in instructorsToRetire)
                {
                    if (carrier.IsHuman)
                    {
                        GameObject.GetInstance()
                                  .NewsBox.AddNews(
                                      new News(
                                          News.NewsType.TripNews,
                                          GameObject.GetInstance().GameTime,
                                          Translator.GetInstance().GetString("News", "1008"),
                                          string.Format(
                                              Translator.GetInstance().GetString("News", "1008", "message"),
                                              instructor.Profile.Name,
                                              instructor.Profile.Age + 1)));
                    }
                }

                foreach (Driver driver in driversToRetirement)
                {
                    if (driver.Coach != null)
                    {
                        //driver.Coach.Status = FleetCoach.CoachStatus.Stopped;

                        if (carrier.IsHuman)
                        {
                            GameObject.GetInstance()
                                      .NewsBox.AddNews(
                                          new News(
                                              News.NewsType.TripNews,
                                              GameObject.GetInstance().GameTime,
                                              Translator.GetInstance().GetString("News", "1010"),
                                              string.Format(
                                                  Translator.GetInstance().GetString("News", "1010", "message"),
                                                  driver.Profile.Name,
                                                  driver.Coach.Name)));
                            if (driver.Coach != null)
                            {
                                if (Drivers.GetNumberOfUnassignedDrivers() == 0)
                                {
                                    GeneralHelpers.CreateDrivers(10);
                                }

                                if (GameObject.GetInstance().HumanCarrier.Drivers.Exists(p => p.Coach == null))
                                {
                                    Driver newDriver =
                                        GameObject.GetInstance().HumanCarrier.Drivers.Find(p => p.Coach == null);

                                    newDriver.Coach = driver.Coach;
                                    newDriver.Coach.AddDriver(newDriver);
                                }
                                else
                                {
                                    Driver driver1 = driver;
                                    List<Driver> drivers =
                                        Drivers.GetUnassignedDrivers(
                                            p =>
                                            p.Profile.Town.Country
                                            == driver1.Coach.Coach.Carrier.Profile.Country
                                            && p.Coaches.Contains(driver1.Coach.Coach.Type.CoachFamily));

                                    if (drivers.Count == 0)
                                    {
                                        Driver driver2 = driver;
                                        drivers =
                                            Drivers.GetUnassignedDrivers(
                                                p =>
                                                p.Profile.Town.Country.Region
                                                == driver2.Coach.Coach.Carrier.Profile.Country.Region
                                                && p.Coaches.Contains(driver2.Coach.Coach.Type.CoachFamily));
                                    }

                                    if (drivers.Count == 0)
                                    {
                                        GeneralHelpers.CreateDrivers(4, driver.Coach.Coach.Type.CoachFamily);
                                        Driver driver2 = driver;
                                        drivers =
                                            Drivers.GetUnassignedDrivers(
                                                p => p.Coaches.Contains(driver2.Coach.Coach.Type.CoachFamily));
                                    }

                                    Driver newDriver = drivers.First();

                                    driver.Coach.Coach.Carrier.AddDriver(newDriver);
                                    newDriver.Coach = driver.Coach;
                                    newDriver.Coach.AddDriver(newDriver);
                                }
                            }
                        }
                        driver.Coach.RemoveDriver(driver);
                    }
                    else
                    {
                        if (carrier.IsHuman)
                        {
                            GameObject.GetInstance()
                                      .NewsBox.AddNews(
                                          new News(
                                              News.NewsType.TripNews,
                                              GameObject.GetInstance().GameTime,
                                              Translator.GetInstance().GetString("News", "1009"),
                                              string.Format(
                                                  Translator.GetInstance().GetString("News", "1009", "message"),
                                                  driver.Profile.Name)));
                        }
                    }
                    carrier.RemoveDriver(driver);
                    driver.Carrier = null;
                    Drivers.RemoveDriver(driver);
                }

                foreach (Instructor instructor in instructorsToRetirement)
                {
                    int studentsCapacity = (instructor.TripSchool.Instructors.Count - 1)
                                           *TripSchool.MaxNumberOfStudentsPerInstructor;

                    if (instructor.Students.Count > 0)
                    {
                        if (instructor.TripSchool.NumberOfStudents > studentsCapacity)
                        {
                            Instructor newInstructor;

                            if (carrier.IsHuman)
                            {
                                newInstructor =
                                    Instructors.GetUnassignedInstructors().OrderBy(i => i.Rating).ToList()[0];
                            }
                            else
                            {
                                newInstructor =
                                    Instructors.GetUnassignedInstructors()[
                                        Rnd.Next(Instructors.GetUnassignedInstructors().Count)];
                            }

                            foreach (DriverStudent student in instructor.Students)
                            {
                                student.Instructor = newInstructor;
                            }

                            newInstructor.TripSchool = instructor.TripSchool;
                            newInstructor.TripSchool.AddInstructor(newInstructor);
                            instructor.Students.Clear();

                            if (carrier.IsHuman)
                            {
                                GameObject.GetInstance()
                                          .NewsBox.AddNews(
                                              new News(
                                                  News.NewsType.TripNews,
                                                  GameObject.GetInstance().GameTime,
                                                  Translator.GetInstance().GetString("News", "1012"),
                                                  string.Format(
                                                      Translator.GetInstance().GetString("News", "1012", "message"),
                                                      instructor.Profile.Name,
                                                      newInstructor.Profile.Name)));
                            }
                        }
                        else
                        {
                            while (instructor.Students.Count > 0)
                            {
                                DriverStudent student = instructor.Students[0];

                                Instructor newInstructor =
                                    instructor.TripSchool.Instructors.Find(
                                        i =>
                                        i.Students.Count < TripSchool.MaxNumberOfStudentsPerInstructor
                                        && i != instructor);
                                newInstructor.AddStudent(student);
                                student.Instructor = newInstructor;

                                instructor.RemoveStudent(student);
                            }

                            if (carrier.IsHuman)
                            {
                                GameObject.GetInstance()
                                          .NewsBox.AddNews(
                                              new News(
                                                  News.NewsType.TripNews,
                                                  GameObject.GetInstance().GameTime,
                                                  Translator.GetInstance().GetString("News", "1011"),
                                                  string.Format(
                                                      Translator.GetInstance().GetString("News", "1011", "message"),
                                                      instructor.Profile.Name)));
                            }
                        }
                    }
                    else
                    {
                        if (carrier.IsHuman)
                        {
                            GameObject.GetInstance()
                                      .NewsBox.AddNews(
                                          new News(
                                              News.NewsType.TripNews,
                                              GameObject.GetInstance().GameTime,
                                              Translator.GetInstance().GetString("News", "1011"),
                                              string.Format(
                                                  Translator.GetInstance().GetString("News", "1011", "message"),
                                                  instructor.Profile.Name)));
                        }
                    }

                    instructor.TripSchool.RemoveInstructor(instructor);
                    instructor.TripSchool = null;

                    Instructors.RemoveInstructor(instructor);
                }

                //wages
                foreach (Driver driver in carrier.Drivers)
                {
                    double salary = CarrierHelpers.GetDriverSalary(carrier, driver);
                    CarrierHelpers.AddCarrierInvoice(
                        carrier,
                        GameObject.GetInstance().GameTime,
                        Invoice.InvoiceType.Wages,
                        -salary);

                    driver.Coach?.Data.AddOperatingValue(new OperatingValue("Salary", GameObject.GetInstance().GameTime.Year, GameObject.GetInstance().GameTime.Month, -salary));
                }

                foreach (Instructor instructor in carrier.TripSchools.SelectMany(f => f.Instructors))
                {
                    double salary = CarrierHelpers.PayGrade(instructor.Rating.CostIndex)
                                    *carrier.Fees.GetValue(FeeTypes.GetType("Instructor Base Salary"));
                    CarrierHelpers.AddCarrierInvoice(
                        carrier,
                        GameObject.GetInstance().GameTime,
                        Invoice.InvoiceType.Wages,
                        -salary);
                }
                //check for employee happiness and wages
                //Console.WriteLine("Carrier: {0} Avg. Wages: {1} Happiness: {2}", carrier.Profile.Name, StatisticsHelpers.GetEmployeeWages()[carrier], Ratings.GetEmployeeHappiness(carrier));

                /*
                double employeeHapiness = Ratings.GetEmployeeHappiness(carrier);
                double avgCompetitorsWages = StatisticsHelpers.GetAverageEmployeeWages(carrier);
                double carrierWage = StatisticsHelpers.GetEmployeeWages()[carrier];

                double wagesDiff = (carrierWage / avgCompetitorsWages) * 100;

                //striking if below 80% of the average
                if (wagesDiff < 80 && employeeHapiness < 80)
                {
                   // if (carrier.IsHuman)
                    //    GameObject.GetInstance().NewsBox.addNews(new News(News.NewsType.Trip_News, GameObject.GetInstance().GameTime, Translator.GetInstance().GetString("News", "1012"), string.Format(Translator.GetInstance().GetString("News", "1012", "message"), instructor.Profile.Name, newInstructor.Profile.Name)));

                  

                    //tjek for union ved skift af løn - forhandling på års niveau

                }*/

                foreach (CarrierFacility facility in carrier.Facilities)
                {
                    CarrierHelpers.AddCarrierInvoice(
                        carrier,
                        GameObject.GetInstance().GameTime,
                        Invoice.InvoiceType.CarrierExpenses,
                        -facility.MonthlyCost);
                }

                foreach (FleetCoach coach in
                    carrier.Fleet.FindAll(
                        (a => a.Purchased == FleetCoach.PurchasedType.Leased)))
                {
                    CarrierHelpers.AddCarrierInvoice(
                        carrier,
                        GameObject.GetInstance().GameTime,
                        Invoice.InvoiceType.Rents,
                        -coach.Coach.LeasingPrice);

                    if (coach.Coach.Owner != null)
                    {
                        CarrierHelpers.AddCarrierInvoice(
                            coach.Coach.Owner,
                            GameObject.GetInstance().GameTime,
                            Invoice.InvoiceType.Rents,
                            coach.Coach.LeasingPrice);
                    }

                    coach.Data.AddOperatingValue(new OperatingValue("Leasing Expenses", GameObject.GetInstance().GameTime.Year, GameObject.GetInstance().GameTime.Month,
                                                                       -coach.Coach.LeasingPrice));
                }

                foreach (Station station in carrier.Stations)
                {
                    var contracts = new List<StationContract>(station.GetCarrierContracts(carrier));

                    double contractPrice = contracts.Sum(c => c.YearlyPayment)/12;

                    CarrierHelpers.AddCarrierInvoice(
                        carrier,
                        GameObject.GetInstance().GameTime,
                        Invoice.InvoiceType.Rents,
                        -contractPrice);

                    long stationIncome = Convert.ToInt64(contractPrice);
                    station.Income += stationIncome;

                    //expired contracts
                    var carrierContracts =
                        new List<StationContract>(
                            station.CarrierContracts.FindAll(
                                c => c.ExpireDate < GameObject.GetInstance().GameTime.AddMonths(2)));

                    foreach (StationContract contract in carrierContracts)
                    {
                        if (contract.Carrier.IsHuman)
                        {
                            GameObject.GetInstance()
                                      .NewsBox.AddNews(
                                          new News(
                                              News.NewsType.StationNews,
                                              GameObject.GetInstance().GameTime,
                                              "Station contract expires",
                                              $"Your contract for {contract.NumberOfGates} gates at [LI station={contract.Station.Profile.IATACode}], {contract.Station.Profile.Country.Name} expires soon. Please extend the contracts."));
                        }
                        else
                        {
                            int numberOfRoutes = StationHelpers.GetStationRoutes(station, contract.Carrier).Count;

                            if (numberOfRoutes > 0)
                            {
                                contract.ContractDate = GameObject.GetInstance().GameTime;
                                contract.ExpireDate = GameObject.GetInstance().GameTime.AddYears(contract.Length);
                            }
                            else
                            {
                                station.RemoveCarrierContract(contract);
                            }
                        }
                    }

                    //wages
                    foreach (StationFacility facility in station.GetCurrentStationFacilities(carrier))
                    {
                        double wage = 0;
                        int employees = facility.NumberOfEmployees;

                        if (facility.Type == StationFacility.FacilityType.Service)
                        {
                            //the more coaches the more employees
                            int coaches = carrier.Fleet.Count(a => a.Homebase == station);
                            employees += coaches;
                        }

                        if (facility.EmployeeType == StationFacility.EmployeeTypes.Maintenance)
                        {
                            wage = carrier.Fees.GetValue(FeeTypes.GetType("Maintenance Wage"));
                        }

                        if (facility.EmployeeType == StationFacility.EmployeeTypes.Support)
                        {
                            wage = carrier.Fees.GetValue(FeeTypes.GetType("Support Wage"));
                        }

                        double facilityWage = employees*wage*(40*4.33);
                        //40 hours per week and 4.33 weeks per month

                        CarrierHelpers.AddCarrierInvoice(
                            carrier,
                            GameObject.GetInstance().GameTime,
                            Invoice.InvoiceType.Wages,
                            -facilityWage);
                    }
                    //passenger growth if ticket office
                    //checking if someone in the alliance has a ticket office for the route (if in an alliance).
                    if (carrier.Alliances.Count > 0)
                    {
                        int highest =
                            carrier.Alliances.SelectMany(a => a.Members)
                                   .Select(m => m.Carrier)
                                   .Max(
                                       m =>
                                       station.GetCarrierStationFacility(m, StationFacility.FacilityType.TicketOffice)
                                              .Facility.ServiceLevel);
                        Station station1 = station;
                        bool hasTicketOffice =
                            carrier.Alliances.SelectMany(a => a.Members)
                                   .Select(m => m.Carrier)
                                   .Where(
                                       m =>
                                       station1.GetCarrierStationFacility(m, StationFacility.FacilityType.TicketOffice)
                                              .Facility.TypeLevel > 0) != null;

                        //If there is an service level update the routes
                        if (hasTicketOffice)
                        {
                            Station station2 = station;
                            foreach (Route route in
                                carrier.Routes.Where(r => r.Destination1 == station2 || r.Destination2 == station2))
                            {
                                Station destination = station == route.Destination1
                                                          ? route.Destination2
                                                          : route.Destination1;
                                station.AddDestinationPassengersRate(destination, (ushort) highest);
                            }
                        }
                    }


                        //not in an alliance so we will look for the carrier only
                    else
                    {
                        if (
                            station.GetCarrierStationFacility(carrier, StationFacility.FacilityType.TicketOffice)
                                   .Facility.TypeLevel > 0
                            || station.HasContractType(carrier, StationContract.ContractType.FullService)
                            || station.HasContractType(carrier, StationContract.ContractType.MediumService))
                        {
                            Station station1 = station;
                            foreach (Route route in
                                carrier.Routes.Where(r => r.Destination1 == station1 || r.Destination2 == station1))
                            {
                                Station destination = station == route.Destination1
                                                          ? route.Destination2
                                                          : route.Destination1;

                                station.AddDestinationPassengersRate(
                                    destination,
                                    (ushort)
                                    station.GetCarrierStationFacility(
                                        carrier,
                                        StationFacility.FacilityType.TicketOffice).Facility.ServiceLevel);
                            }
                        }
                    }
                }
                //passenger demand
                int advertisementFactor = carrier.GetCarrierAdvertisements().Sum(a => a.ReputationLevel);

                foreach (Route route in carrier.Routes)
                {
                    route.Destination1.AddDestinationPassengersRate(
                        route.Destination2,
                        (ushort) (5*advertisementFactor));
                    route.Destination2.AddDestinationPassengersRate(
                        route.Destination1,
                        (ushort) (5*advertisementFactor));
                }
                foreach (Loan loan in carrier.Loans)
                {
                    if (loan.IsActive)
                    {
                        double amount = Math.Min(loan.GetMonthlyPayment(), loan.PaymentLeft);

                        CarrierHelpers.AddCarrierInvoice(
                            carrier,
                            GameObject.GetInstance().GameTime,
                            Invoice.InvoiceType.Loans,
                            -amount);

                        loan.PaymentLeft -= amount;
                    }
                }
                // chs, 2011-17-10 change so it only looks at the advertisement types which are invented at the time
                foreach (AdvertisementType.CarrierAdvertisementType type in
                    Enum.GetValues(typeof (AdvertisementType.CarrierAdvertisementType)))
                {
                    if (GameObject.GetInstance().GameTime.Year >= (int) type)
                    {
                        CarrierHelpers.AddCarrierInvoice(
                            carrier,
                            GameObject.GetInstance().GameTime,
                            Invoice.InvoiceType.CarrierExpenses,
                            -carrier.GetCarrierAdvertisement(type).Price);

                    }
                }
                // reputation moves toward what the advertising supports; campaigns are paid here
                AdvertisingService.ProcessMonth(carrier);
                // owned coach cost money too (realistic costs)
                OwnershipService.ProcessMonth(carrier);
                //maintenance, insurance, and ratings

                if (carrier.InsurancePolicies != null)
                {
                    InsuranceService.ProcessMonth(carrier);
                }
                RatingsService.ProcessMonth(carrier);
                if (carrier == GameObject.GetInstance().HumanCarrier)
                {
                    MaintenanceService.ProcessMonth(carrier);
                    DriverSupply.TopUp(carrier);
                    DriverMoraleService.ProcessMonth(carrier);
                }

                RandomEvent.CheckExpired();
            } //);

            if (Drivers.GetNumberOfUnassignedDrivers() < 25)
            {
                GeneralHelpers.CreateDrivers(100);
            }

            if (Instructors.GetNumberOfUnassignedInstructors() < 20)
            {
                GeneralHelpers.CreateInstructors(75);
            }

            //checks if the station will increase the number of gates either by new terminal or by extending existing

            //Parallel.ForEach(
            //    Stations.GetAllStations(a => a.Terminals.getInusePercent() > 90),
            //    station => { StationHelpers.CheckForExtendGates(station); });
            foreach (Station station in Stations.GetAllStations(a => a.Terminals.GetInusePercent(Terminal.TerminalType.Passenger) > 90))
            {
                StationHelpers.CheckForExtendGates(station);
            }

            long longestRequiredBayLenght =
                CoachTypes.GetTypes(
                    a =>
                    a.Produced.From <= GameObject.GetInstance().GameTime
                    && a.Produced.To >= GameObject.GetInstance().GameTime).Max(a => a.MinBayLength);


            foreach (
                Station station in
                    Stations.GetAllStations(
                        a =>
                        a.Bays.Count > 0
                        && a.Bays.Select(r => r.Length).Max() < longestRequiredBayLenght/2))

                //Parallel.ForEach(
                //    Stations.GetAllStations(
                //        a => a.Bays.Count > 0 && a.Bays.Select(r => r.Length).Max() < longestRequiredBayLenght / 2),
                //    station =>
            {
                StationHelpers.CheckForExtendBay(station);

                foreach (Cooperation cooperation in station.Cooperations)
                {
                    CarrierHelpers.AddCarrierInvoice(
                        cooperation.Carrier,
                        GameObject.GetInstance().GameTime,
                        Invoice.InvoiceType.Purchases,
                        -cooperation.Type.MonthlyPrice);
                }
            } //);

            foreach (Station station in Stations.GetAllStations(a => StationHelpers.GetStationRoutes(a).Count > 0))
            {
                double stationRoutes = StationHelpers.GetStationRoutes(station).Count;
                double growth = Math.Min(0.5*stationRoutes, 2);

                PassengerHelpers.ChangePaxDemand(station, growth);
            }

            if (GameObject.GetInstance().Scenario != null)
            {
                ScenarioHelpers.UpdateScenario(GameObject.GetInstance().Scenario);
            }

            CreateMontlySummary();
        }

        private static void DoYearlyUpdate()
        {
            //Clear stats when it on yearly
            if (Infrastructure.Settings.GetInstance().ClearStats == Intervals.Yearly)
            {
                ClearAllUsedStats();
            }

            //Auto save when it on yearly
            if (Infrastructure.Settings.GetInstance().AutoSave == Intervals.Yearly)
            {
                SerializedLoadSaveHelpers.SaveGame("autosave");
            }

            CarrierHelpers.ClearRoutesStatistics();
            //updates holidays 
            GeneralHelpers.CreateHolidays(GameObject.GetInstance().GameTime.Year);

            const double yearlyRaise = 1.03; //3 % 

            foreach (Carrier carrier in Carriers.GetAllCarriers())
            {
                // with realistic costs maintenance is paid per trip hour (TripCosts) and by the checks: no yearly bill on top
                foreach (FleetCoach coach in TripCosts.Enabled ? new List<FleetCoach>() : carrier.Fleet)
                {
                    CarrierHelpers.AddCarrierInvoice(
                        carrier,
                        GameObject.GetInstance().GameTime,
                        Invoice.InvoiceType.Maintenances,
                        -coach.Coach.Type.GetMaintenance());
                }

                foreach (FeeType feeType in FeeTypes.GetTypes())
                {
                    carrier.Fees.SetValue(feeType, carrier.Fees.GetValue(feeType)*yearlyRaise);
                }
            }

            // the year's demand: made again from the year's figures (the random growth below only ever reached the extra
            // demand from events, never the stations' own demand, which a load made again from the new year anyway)
            try
            {
                PassengerHelpers.RebuildDemand();
            }
            catch (Exception ex)
            {
                Infrastructure.StartupLog.Write("New year: making the demand again failed: " + ex);
            }

            //increases the extra passenger demand (from events) between stations with up to 5%
            //Parallel.ForEach(
            //    Stations.GetAllActiveStations(),
            //    station =>
            foreach (Station station in Stations.GetAllActiveStations())
            {
                foreach (DestinationDemand destPax in station.GetDestinationsPassengers())
                {
                    destPax.Rate = (ushort) (destPax.Rate*MathHelpers.GetRandomDoubleNumber(0.97, 1.05));
                }
            } //);

            //removes the oldest drivers/instructors and creates some new ones
            List<Driver> oldDrivers = Drivers.GetUnassignedDrivers().OrderByDescending(p => p.Profile.Age).ToList();
            List<Instructor> oldInstructors =
                Instructors.GetUnassignedInstructors().OrderByDescending(i => i.Profile.Age).ToList();

            for (int i = 0; i < Math.Min(15, oldDrivers.Count); i++)
            {
                Drivers.RemoveDriver(oldDrivers[i]);
            }

            for (int i = 0; i < Math.Min(10, oldInstructors.Count); i++)
            {
                Instructors.RemoveInstructor(oldInstructors[i]);
            }

            GeneralHelpers.CreateDrivers(15);
            GeneralHelpers.CreateInstructors(10);

            foreach (Carrier carrier in Carriers.GetAllCarriers())
            {
                EventsHelpers.GenerateEvents(carrier);
            }
        }

        //simualtes an coach for service

        //gets the weather for an coach
        private static Weather GetCoachWeather(FleetCoach coach)
        {
            double distance = MathHelpers.GetDistance(
                coach.CurrentPosition,
                coach.CurrentTrip.Entry.Destination.Station);
            Station dest = coach.CurrentTrip.Entry.Destination.Station;
            Station dept = coach.CurrentTrip.GetDepartureStation();

            double totalDistance = MathHelpers.GetDistance(
                dept.Profile.Coordinates.ConvertToGeoCoordinate(),
                dest.Profile.Coordinates.ConvertToGeoCoordinate());

            return distance > totalDistance/2 ? dept.Weather[0] : dest.Weather[0];
        }

        //sets the next trip for a route coach

        //finds the next trip time for an coach - checks also for delay
        private static DateTime GetNextTripTime(FleetCoach coach)
        {
            if (coach.CurrentTrip == null)
            {
                SetNextTrip(coach);
                if (coach.CurrentTrip != null) return coach.CurrentTrip.TripTime;
            }
            if (coach.CurrentTrip != null && coach.CurrentTrip.Entry.TimeTable.Route.Banned)
            {
                SetNextTrip(coach);
                if (coach.CurrentTrip.Entry.TimeTable.Route.Banned)
                {
                    coach.Status = FleetCoach.CoachStatus.Stopped;
                    return new DateTime(2500, 1, 1);
                }
                return coach.CurrentTrip.TripTime;
            }
            return coach.CurrentTrip?.TripTime ?? new DateTime();
        }

        //returns the next route for an coach 
        private static Route GetNextRoute(FleetCoach coach)
        {
            IOrderedEnumerable<RouteTimeTableEntry> entries =
                from e in
                    coach.Routes.Where(r => r.StartDate <= GameObject.GetInstance().GameTime)
                            .Select(r => r.TimeTable.GetNextEntry(GameObject.GetInstance().GameTime, coach))
                where e != null
                orderby MathHelpers.ConvertEntryToDate(e)
                select e;

            if (entries.Any())
            {
                return entries.First().TimeTable.Route;
            }
            return null;
        }

        private static int GetWindInfluence(FleetCoach coach)
        {
            double direction =
                MathHelpers.GetDirection(
                    coach.CurrentPosition.Profile.Coordinates.ConvertToGeoCoordinate(),
                    coach.CurrentTrip.GetNextDestination().Profile.Coordinates.ConvertToGeoCoordinate());

            Weather.WindDirection windDirection = MathHelpers.GetWindDirectionFromDirection(direction);

            Weather currentWeather = GetCoachWeather(coach);
            //W+E = 0+4= 5, N+S=2+6 - = Abs(Count/2) -> Head, Abs(0) -> Tail -> if ends/starts with same => tail, indexof +-1 -> tail, (4+(indexof))+-1 -> head 

            int windDirectionLenght = Enum.GetValues(typeof (Weather.WindDirection)).Length;
            int indexCurrentPosition = Array.IndexOf(Enum.GetValues(typeof (Weather.WindDirection)), windDirection);
            //int indexWeather = Array.IndexOf(Enum.GetValues(typeof(Weather.WindDirection)),currentWeather.WindSpeed);

            //check for tail wind
            Weather.WindDirection windTailLeft = indexCurrentPosition > 0
                                                     ? (Weather.WindDirection) indexCurrentPosition - 1
                                                     : (Weather.WindDirection) windDirectionLenght - 1;
            Weather.WindDirection windTailRight = indexCurrentPosition < windDirectionLenght - 1
                                                      ? (Weather.WindDirection) indexCurrentPosition + 1
                                                      : 0;

            if (windTailLeft == currentWeather.Direction || windTailRight == currentWeather.Direction
                || windDirection == currentWeather.Direction)
            {
                return 1;
            }

            Weather.WindDirection windOpposite = indexCurrentPosition - (windDirectionLenght/2) > 0
                                                     ? (Weather.WindDirection) indexCurrentPosition - (windDirectionLenght/2)
                                                     : (Weather.WindDirection) windDirectionLenght - 1 - indexCurrentPosition - (windDirectionLenght/2);
            int indexOpposite = Array.IndexOf(Enum.GetValues(typeof (Weather.WindDirection)), windOpposite);

            Weather.WindDirection windHeadLeft = indexOpposite > 0
                                                     ? (Weather.WindDirection) indexOpposite - 1
                                                     : (Weather.WindDirection) windDirectionLenght - 1;
            Weather.WindDirection windHeadRight = indexOpposite < windDirectionLenght - 1
                                                      ? (Weather.WindDirection) indexOpposite + 1
                                                      : 0;

            if (windHeadLeft == currentWeather.Direction || windHeadRight == currentWeather.Direction
                || windOpposite == currentWeather.Direction)
            {
                return -1;
            }

            return 0;
        }

        private static void SetNextTrip(FleetCoach coach)
        {
            Route route = GetNextRoute(coach);

            if ((coach.CurrentTrip == null && route != null && route.HasStopovers)
                || (coach.CurrentTrip is StopoverTrip
                    && ((StopoverTrip) coach.CurrentTrip).IsLastTrip)
                || (coach.CurrentTrip != null && coach.CurrentTrip.Entry.MainEntry == null && route != null
                    && route.HasStopovers))
            {
                if (route != null)
                    coach.CurrentTrip = coach.GroundedToDate > GameObject.GetInstance().GameTime ? new StopoverTrip(route.TimeTable.GetNextEntry(coach.GroundedToDate, coach)) : new StopoverTrip(route.TimeTable.GetNextEntry(GameObject.GetInstance().GameTime, coach));
            }

            coach.Status = FleetCoach.CoachStatus.ToRouteStart;

            if (coach.CurrentTrip is StopoverTrip && !((StopoverTrip) coach.CurrentTrip).IsLastTrip)
            {
                ((StopoverTrip) coach.CurrentTrip).SetNextEntry();
                //coach.CurrentTrip.TripTime = new DateTime(Math.Max(GameObject.GetInstance().GameTime.Add(RouteTimeTable.MinTimeBetweenTrips).Ticks, coach.CurrentTrip.TripTime.Ticks));
            }
            else
            {
                if (route != null)
                {
                    coach.CurrentTrip = coach.GroundedToDate > GameObject.GetInstance().GameTime ? new Trip(route.TimeTable.GetNextEntry(coach.GroundedToDate, coach)) : new Trip(route.TimeTable.GetNextEntry(GameObject.GetInstance().GameTime, coach));
                }
                else
                {
                    coach.Status = FleetCoach.CoachStatus.ToHomebase;
                }
            }
        }

        private static void SimulateService(FleetCoach coach)
        {
            double servicePrice = 10000;

            coach.CurrentPosition = coach.CurrentTrip.Entry.Destination.Station;
            // new GeoCoordinate(coach.CurrentTrip.Entry.Destination.Station.Profile.Coordinates.Latitude, coach.CurrentTrip.Entry.Destination.Station.Profile.Coordinates.Longitude);
            coach.Status = FleetCoach.CoachStatus.ToRouteStart;

            double fdistance = MathHelpers.GetDistance(
                coach.CurrentTrip.Entry.Destination.Station,
                coach.CurrentPosition);
            double expenses = StationHelpers.GetFuelPrice(coach.CurrentPosition)*fdistance
                              *coach.CurrentTrip.GetTotalPassengers()*coach.Coach.FuelConsumption
                              + StationHelpers.GetLandingFee(coach.CurrentPosition, coach.Coach);

            servicePrice += expenses;

            coach.Coach.Flown += fdistance;

            coach.Coach.LastServiceCheck = coach.Coach.Flown;

            CarrierHelpers.AddCarrierInvoice(
                coach.Coach.Carrier,
                GameObject.GetInstance().GameTime,
                Invoice.InvoiceType.Maintenances,
                -servicePrice);

            coach.CurrentTrip.Entry.TimeTable.Route.AddRouteInvoice(
                new Invoice(GameObject.GetInstance().GameTime, Invoice.InvoiceType.Maintenances, -servicePrice));
            coach.Statistics.AddStatisticsValue(
                GameObject.GetInstance().GameTime.Year,
                StatisticsTypes.GetStatisticsType("Coach_Income"),
                -servicePrice);

            SetNextTrip(coach);
        }

        private static void SimulateTakeOff(FleetCoach coach)
        {
            KeyValuePair<FleetCoachHelpers.DelayType, int> delayedMinutes =
                FleetCoachHelpers.GetDelayedMinutes(coach);

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
                SetNextTrip(coach);
            }
            else
            {
                coach.CurrentTrip.AddDelayMinutes(delayedMinutes.Value);

                if (delayedMinutes.Value > 0)
                {
                    coach.CurrentTrip.IsOnTime = false;
                }
            }

            if (coach.CurrentTrip.TripTime <= GameObject.GetInstance().GameTime)
            {
                if (StationHelpers.HasBadWeather(coach.CurrentTrip.Entry.DepartureStation)
                    || StationHelpers.HasBadWeather(coach.CurrentTrip.Entry.Destination.Station))
                {
                    if (coach.Coach.Carrier.IsHuman)
                    {
                        Station station = StationHelpers.HasBadWeather(coach.CurrentTrip.Entry.Destination.Station)
                                              ? coach.CurrentTrip.Entry.Destination.Station
                                              : coach.CurrentTrip.Entry.DepartureStation;
                        GameObject.GetInstance()
                                  .NewsBox.AddNews(
                                      new News(
                                          News.NewsType.TripNews,
                                          GameObject.GetInstance().GameTime,
                                          Translator.GetInstance().GetString("News", "1003"),
                                          string.Format(
                                              Translator.GetInstance().GetString("News", "1003", "message"),
                                              coach.Coach.TailNumber,
                                              station.Profile.IATACode)));
                    }
                    SetNextTrip(coach);
                }
                else
                {
                    coach.Status = FleetCoach.CoachStatus.OnRoute;

                    if (coach.CurrentTrip.Entry.MainEntry == null)
                    {
                        if (coach.CurrentTrip.IsPassengerTrip())
                        {
                            var classes = new List<CoachClass>(coach.Coach.Classes);

                            foreach (CoachClass aClass in classes)
                            {
                                coach.CurrentTrip.Classes.Add(
                                    new TripCoachClass(
                                        ((PassengerRoute) coach.CurrentTrip.Entry.TimeTable.Route)
                                            .GetRouteCoachClass(aClass.Type),
                                        GetPassengers(coach, aClass.Type)));
                            }
                        }
                    }
                    else
                    {
                        coach.Status = FleetCoach.CoachStatus.OnRoute;
                    }
                }
            }
        }

        private static void SimulateToHomebase(FleetCoach coach)
        {
            coach.CurrentPosition = coach.CurrentTrip.Entry.Destination.Station;
            //new GeoCoordinate(coach.CurrentTrip.Entry.Destination.Station.Profile.Coordinates.Latitude, coach.CurrentTrip.Entry.Destination.Station.Profile.Coordinates.Longitude);
            Station station = coach.CurrentPosition;
            coach.Status = FleetCoach.CoachStatus.ToHomebase;

            if (
                !coach.CurrentTrip.Entry.Destination.Station.Profile.Coordinates.Equals(
                    coach.Homebase.Profile.Coordinates))
            {
                coach.Status = FleetCoach.CoachStatus.Stopped;
            }
            else
            {
                coach.CurrentTrip =
                    new Trip(
                        new RouteTimeTableEntry(
                            coach.CurrentTrip.Entry.TimeTable,
                            GameObject.GetInstance().GameTime.DayOfWeek,
                            GameObject.GetInstance().GameTime.TimeOfDay,
                            new RouteEntryDestination(coach.Homebase, "Service", null)));
            }
        }

        private static void UpdateCoach(FleetCoach coach)
        {
            if (coach.HasRoute)
            {
                switch (coach.Status)
                {
                    case FleetCoach.CoachStatus.OnRoute:
                        UpdateOnRouteCoach(coach);
                        break;
                    case FleetCoach.CoachStatus.OnService:
                        UpdateOnRouteCoach(coach);
                        break;
                    case FleetCoach.CoachStatus.ToHomebase:
                        UpdateOnRouteCoach(coach);
                        break;
                    case FleetCoach.CoachStatus.ToRouteStart:
                        UpdateToRouteStartCoach(coach);
                        break;

                    case FleetCoach.CoachStatus.Resting:
                        DateTime nextTripTime = GetNextTripTime(coach);
                        if (nextTripTime <= GameObject.GetInstance().GameTime)
                        {
                            SimulateTakeOff(coach);
                        }
                        break;
                }
            }
        }

        //the method for updating a route coach
        private static void UpdateOnRouteCoach(FleetCoach coach)
        {
            if (coach.CurrentTrip == null)
            {
                Route route = GetNextRoute(coach);

                if (route?.TimeTable.GetNextEntry(GameObject.GetInstance().GameTime, coach.CurrentPosition) == null)
                {
                    coach.CurrentTrip = null;
                }
                else
                {
                    coach.CurrentTrip =
                        new Trip(
                            route.TimeTable.GetNextEntry(GameObject.GetInstance().GameTime, coach.CurrentPosition));
                }
            }
            if (coach.CurrentTrip != null)
            {
                double adistance = coach.CurrentTrip.DistanceToDestination;
                //coach.CurrentPosition.GetDistanceTo(coach.CurrentTrip.Entry.Destination.Station.Profile.Coordinates);

                double speed = coach.Coach.CruisingSpeed;

                /* Leaving it out until we get a position implemented agian
                Weather currentWeather = GetCoachWeather(coach);
                int wind = GetWindInfluence(coach) * (int)currentWeather.WindSpeed;
                speed = coach.Coach.Type.CruisingSpeed  + wind;
                */

                if (adistance > 4)
                {
                    MathHelpers.MoveObject(coach, speed);
                }

                double distance =
                    MathHelpers.GetDistance(
                        coach.CurrentPosition.Profile.Coordinates.ConvertToGeoCoordinate(),
                        coach.CurrentTrip.Entry.Destination.Station.Profile.Coordinates.ConvertToGeoCoordinate());

                if (coach.CurrentTrip.DistanceToDestination < 5)
                {
                    if (coach.Status == FleetCoach.CoachStatus.OnRoute)
                    {
                        SimulateLanding(coach);
                    }
                    else if (coach.Status == FleetCoach.CoachStatus.OnService)
                    {
                        SimulateService(coach);
                    }
                    else if (coach.Status == FleetCoach.CoachStatus.ToHomebase)
                    {
                        SimulateToHomebase(coach);
                    }
                    //else if (coach.Status == FleetCoach.CoachStatus.To_route_start)
                    //  SimulateRouteStart(coach);
                }
            }
            else
            {
                coach.Status = FleetCoach.CoachStatus.ToRouteStart;
            }
        }

        //the method for updating a route coach with status toroutestart
        private static void UpdateToRouteStartCoach(FleetCoach coach)
        {
            if (coach.CurrentTrip == null)
            {
                Route route = GetNextRoute(coach);

                if (route == null)
                {
                    coach.Status = FleetCoach.CoachStatus.ToHomebase;
                }
                else
                {
                    coach.CurrentTrip =
                        new Trip(route.TimeTable.GetNextEntry(GameObject.GetInstance().GameTime, coach));
                }
            }
            if (coach.CurrentTrip != null)
            {
                double adistance = coach.CurrentTrip.DistanceToDestination;

                double speed = coach.Coach.CruisingSpeed;

                /* leaving it out until we get position working agian
                if (coach.CurrentTrip != null)
                {
                    Weather currentWeather = GetCoachWeather(coach);

                    int wind = GetWindInfluence(coach) * (int)currentWeather.WindSpeed;
                    speed = coach.Coach.Type.CruisingSpeed  + wind;

                } */

                if (adistance > 4)
                {
                    MathHelpers.MoveObject(coach, speed);
                }

                if (coach.CurrentTrip.DistanceToDestination < 5)
                {
                    coach.Status = FleetCoach.CoachStatus.Resting;
                    coach.CurrentPosition = coach.CurrentTrip.Entry.DepartureStation;
                    // new GeoCoordinate(destination.Latitude, destination.Longitude);
                }
            }
            else
            {
                coach.Status = FleetCoach.CoachStatus.ToHomebase;
            }
        }

        #endregion

        //calibrates the time if needed

        //handles an influence for a historic event
    }
}