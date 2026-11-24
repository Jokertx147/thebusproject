using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TheBusProject.Infrastructure.Enums;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Carriers.Subsidiary;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.Passengers;
using TheBusProject.Models.Drivers;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    //the class for some general carrier helpers
    public class CarrierHelpers
    {
        //checks for a special contract returns true if not successed or overdue
        public static bool CheckSpecialContract(SpecialContract sc)
        {
            bool isOk = true;

            foreach (ContractRequirement cr in sc.Type.Requirements)
            {
                foreach (SpecialContractRoute scr in sc.Type.Routes)
                {
                    SpecialContractRoute scr1 = scr;
                    IEnumerable<Route> routes =
                        sc.Routes.Where(
                            r =>
                            r.HasCoach &&
                            ((r.Destination1 == scr1.Departure && r.Destination2 == scr1.Destination) || (scr1.BothWays && r.Destination2 == scr1.Departure && r.Destination1 == scr1.Destination)));
                    if (cr.Type == ContractRequirement.RequirementType.ClassType)
                    {
                        if (routes.FirstOrDefault(r => ((PassengerRoute) r).GetRouteCoachClass(cr.ClassType) != null) == null)
                            isOk = false;
                    }
                    else if (cr.Type == ContractRequirement.RequirementType.Destination)
                    {
                        if (!routes.Any())
                            isOk = false;
                    }
                }
            }

            if (!isOk)
            {
                if (sc.Carrier.IsHuman)
                {
                    GameObject.GetInstance()
                              .NewsBox.AddNews(
                                  new News(
                                      News.NewsType.TripNews,
                                      GameObject.GetInstance().GameTime,
                                      Translator.GetInstance().GetString("News", "1016"),
                                      string.Format(
                                          Translator.GetInstance().GetString("News", "1016", "message"),
                                          sc.Type.Name,
                                          new ValueCurrencyFormatter().Convert(sc.Type.Penalty))));
                }

                AddCarrierInvoice(sc.Carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.CarrierExpenses, -sc.Type.Penalty);
            }

            bool overdue = sc.Type.IsFixedDate ? GameObject.GetInstance().GameTime > sc.Type.Period.To : GameObject.GetInstance().GameTime >= sc.Date;

            if (isOk && overdue)
            {
                if (sc.Carrier.IsHuman)
                {
                    GameObject.GetInstance()
                              .NewsBox.AddNews(
                                  new News(
                                      News.NewsType.TripNews,
                                      GameObject.GetInstance().GameTime,
                                      Translator.GetInstance().GetString("News", "1017"),
                                      string.Format(
                                          Translator.GetInstance().GetString("News", "1017", "message"),
                                          sc.Type.Name,
                                          new ValueCurrencyFormatter().Convert(sc.Type.Payment))));
                }

                AddCarrierInvoice(sc.Carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.CarrierExpenses, sc.Type.Payment);

                return true;
            }

            return !isOk;
        }

        //clears the statistics for all routes for all carriers
        public static void ClearRoutesStatistics()
        {
            IEnumerable<Route> routes = Carriers.GetAllCarriers().SelectMany(a => a.Routes);

            foreach (Route route in routes)
                route.Statistics.Clear();
        }

        //clears the statistics for all carriers
        public static void ClearCarriersStatistics()
        {
            foreach (Carrier carrier in Carriers.GetAllCarriers())
                carrier.Statistics.Clear();
        }

        //creates an coach for an carrier
        public static FleetCoach CreateCoach(Carrier carrier, CoachType type)
        {
            Guid id = Guid.NewGuid();

            var coach = new Coach(id.ToString(), type, carrier.Profile.Country.TailNumbers.GetNextTailNumber(carrier), GameObject.GetInstance().GameTime);
            Coaches.AddCoach(coach);

            var fCoach = new FleetCoach(FleetCoach.PurchasedType.Bought, GameObject.GetInstance().GameTime, carrier, coach, carrier.Stations[0]);

            coach.ClearCoachClasses();

            CoachHelpers.CreateCoachClasses(coach);

            return fCoach;
        }

        //adds an invoice to an carrier
        public static void AddCarrierInvoice(Carrier carrier, DateTime date, Invoice.InvoiceType type, double amount)
        {
            // the player's money is the human carrier's money, so every carrier is paid the same way
            carrier.AddInvoice(new Invoice(date, type, amount));
        }

        //buys an coach to an carrier
        public static FleetCoach BuyCoach(Carrier carrier, Coach coach, Station station)
        {
            return BuyCoach(carrier, coach, station, 0);
        }

        public static FleetCoach BuyCoach(Carrier carrier, Coach coach, Station station, double discount)
        {
            FleetCoach fCoach = AddCoach(carrier, coach, station, false);

            double price = coach.GetPrice()*((100 - discount)/100);

            AddCarrierInvoice(carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -price);

            return fCoach;
        }

        //returns if an coach has to be registered again for an carrier: when it is registered in another country,
        //or (US carriers) when it does not carry this carrier's own N###XX registration
        public static bool NeedsNewRegistration(Carrier carrier, string tailNumber)
        {
            Country home = carrier?.Profile?.Country;

            if (home?.TailNumbers == null)
            {
                return false;
            }

            if (home.TailNumberFormat != null && home.TailNumberFormat.StartsWith("N-")
                && !string.IsNullOrWhiteSpace(carrier.Profile.IATACode))
            {
                return tailNumber == null
                       || !System.Text.RegularExpressions.Regex.IsMatch(
                           tailNumber,
                           "^N\\d{3,}" + System.Text.RegularExpressions.Regex.Escape(carrier.Profile.IATACode) + "$");
            }

            return Countries.GetCountryFromTailNumber(tailNumber)?.Name != home.Name;
        }

        public static FleetCoach AddCoach(Carrier carrier, Coach coach, Station station, Boolean leased)
        {
            // a missing home country falls back to the home station's country
            carrier.Profile.Country ??= station?.Profile.Country;

            if (NeedsNewRegistration(carrier, coach.TailNumber))
            {
                lock (carrier.Profile.Country.TailNumbers)
                {
                    coach.TailNumber = carrier.Profile.Country.TailNumbers.GetNextTailNumber(carrier);
                }
            }

            var fCoach = new FleetCoach(leased ? FleetCoach.PurchasedType.Leased : FleetCoach.PurchasedType.Bought, GameObject.GetInstance().GameTime, carrier, coach, station);

            carrier.AddCoach(fCoach);

            return fCoach;
        }

        //orders a number of coaches for an carrier
        public static void OrderCoaches(Carrier carrier, List<CoachOrder> orders, Station station, DateTime deliveryDate)
        {
            OrderCoaches(carrier, orders, station, deliveryDate, 0);
        }

        //orders a number of coaches for an carrier
        public static void OrderCoaches(Carrier carrier, List<CoachOrder> orders, Station station, DateTime deliveryDate, double discount)
        {
            Guid id = Guid.NewGuid();

            foreach (CoachOrder order in orders)
            {
                for (int i = 0; i < order.Amount; i++)
                {
                    var coach = new Coach(id.ToString(), order.Type, carrier.Profile.Country.TailNumbers.GetNextTailNumber(carrier), deliveryDate);
                    Coaches.AddCoach(coach);

                    const FleetCoach.PurchasedType pType = FleetCoach.PurchasedType.Bought;
                    carrier.AddCoach(pType, coach, station);

                    coach.ClearCoachClasses();

                    foreach (CoachClass aClass in order.Classes)
                    {
                        var tClass = new CoachClass(aClass.Type, aClass.SeatingCapacity) {RegularSeatingCapacity = aClass.RegularSeatingCapacity};

                        foreach (CoachFacility facility in aClass.GetFacilities())
                            tClass.SetFacility(carrier, facility);

                        coach.AddCoachClass(tClass);
                    }
                }
            }

            int totalAmount = orders.Sum(o => o.Amount);
            double price = orders.Sum(o => o.Type.Price*o.Amount);

            double totalPrice = price*((1 - GeneralHelpers.GetCoachOrderDiscount(totalAmount)))*((100 - discount)/100);

            AddCarrierInvoice(carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Purchases, -totalPrice);
        }

        //reallocate all gates and facilities from one station to another - gates, facilities and routes
        public static void ReallocateStation(Station oldStation, Station newStation, Carrier carrier)
        {
            //contract
            List<StationContract> oldContracts = oldStation.GetCarrierContracts(carrier);

            foreach (StationContract oldContract in oldContracts)
            {
                oldStation.RemoveCarrierContract(oldContract);

                oldContract.Station = newStation;

                StationHelpers.AddCarrierContract(oldContract);


                for (int i = 0; i < oldContract.NumberOfGates; i++)
                {
                    Gate newGate = newStation.Terminals.GetGates().First(g => g.Carrier == null);
                    newGate.Carrier = carrier;

                    Gate oldGate = oldStation.Terminals.GetGates().First(g => g.Carrier == carrier);
                    oldGate.Carrier = null;
                }
            }
            //routes
            IEnumerable<Route> obsoleteRoutes = (from r in carrier.Routes where r.Destination1 == oldStation || r.Destination2 == oldStation select r);

            foreach (Route route in obsoleteRoutes)
            {
                if (route.Destination1 == oldStation) route.Destination1 = newStation;
                if (route.Destination2 == oldStation) route.Destination2 = newStation;


                List<RouteTimeTableEntry> entries = route.TimeTable.Entries.FindAll(e => e.Destination.Station == oldStation);

                foreach (RouteTimeTableEntry entry in entries)
                    entry.Destination.Station = newStation;

                foreach (FleetCoach coach in route.GetCoaches())
                {
                    if (coach.Homebase == oldStation)
                        coach.Homebase = newStation;
                }
            }

            //facilities
            foreach (StationFacility facility in oldStation.GetCurrentStationFacilities(carrier))
            {
                newStation.AddStationFacility(carrier, facility, GameObject.GetInstance().GameTime);
            }

            oldStation.ClearFacilities(carrier);

            foreach (StationFacility.FacilityType type in Enum.GetValues(typeof (StationFacility.FacilityType)))
            {
                StationFacility noneFacility = StationFacilities.GetFacilities(type).Find((facility => facility.TypeLevel == 0));

                oldStation.AddStationFacility(carrier, noneFacility, GameObject.GetInstance().GameTime);
            }
        }

        //returns all route facilities for a given carrier and type
        public static List<RouteFacility> GetRouteFacilities(Carrier carrier, RouteFacility.FacilityType type)
        {
            return RouteFacilities.GetFacilities(type).FindAll(f => f.Requires == null || carrier.Facilities.Exists(a => a.Uid == f.Requires.Uid));
        }

        //returns the pay for a codesharing agreement - one wayed
        public static double GetCodesharingPrice(CodeshareAgreement agreement)
        {
            return GetCodesharingPrice(agreement.Carrier1, agreement.Carrier2);
        }

        //returns the pay for codesharing agreement
        public static double GetCodesharingPrice(Carrier carrier1, Carrier carrier2)
        {
            //from carrier1 to carrier2
            return GeneralHelpers.GetInflationPrice(750);
        }

        //returns if an carrier wants to have code sharing with another carrier
        public static bool AcceptCodesharing(Carrier carrier, Carrier asker, CodeshareAgreement.CodeshareType type)
        {
            double coeff = type == CodeshareAgreement.CodeshareType.OneWay ? 0.25 : 0.40;

            IEnumerable<Country> sameCountries = asker.Stations.Select(a => a.Profile.Country).Distinct().Intersect(carrier.Stations.Select(a => a.Profile.Country).Distinct());
            IEnumerable<Station> sameDestinations = asker.Stations.Distinct().Intersect(carrier.Stations);
            IEnumerable<Country> sameCodesharingCountries =
                carrier.GetCodesharingCarriers().SelectMany(a => a.Stations).Select(a => a.Profile.Country).Distinct().Intersect(carrier.Stations.Select(a => a.Profile.Country).Distinct());

            double carrierDestinations = carrier.Stations.Count;
            double carrierRoutes = carrier.Routes.Count;
            double carrierCountries = carrier.Stations.Select(a => a.Profile.Country).Distinct().Count();
            double carrierCodesharings = carrier.Codeshares.Count;
            double carrierAlliances = carrier.Alliances.Count;

            //declines if asker is much smaller than the invited carrier
            if (carrierRoutes > 3*asker.Routes.Count)
                return false;

            //declines if there is a match for x% of the carriers
            if (sameDestinations.Count() >= carrierDestinations*coeff)
                return false;

            //declines if there is a match for 75% of the carriers
            if (sameCountries.Count() >= carrierCountries*0.75)
                return false;

            //declines if the carrier already has a code sharing or alliance in that area
            if (sameCodesharingCountries.Count() >= carrierCountries*coeff)
                return false;

            return true;
        }

        //returns if the carrier has training facilities for a specific coach family
        public static bool HasTrainingFacility(Carrier carrier, string coachfamily)
        {
            IEnumerable<CarrierFacility> facilities = carrier.Facilities.Where(f => f is DriverTrainingFacility);

            return facilities.SingleOrDefault(f => ((DriverTrainingFacility) f).CoachFamily == coachfamily) != null;
        }

        //launches a subsidiary to operate on its own
        public static void MakeSubsidiaryCarrierIndependent(SubsidiaryCarrier carrier)
        {
            carrier.Carrier.RemoveSubsidiaryCarrier(carrier);

            carrier.Carrier = null;

            carrier.Profile.CEO =
                $"{Names.GetInstance().GetRandomFirstName(carrier.Profile.Country)} {Names.GetInstance().GetRandomLastName(carrier.Profile.Country)}";

            if (!Carriers.ContainsCarrier(carrier))
                Carriers.AddCarrier(carrier);
        }

        //closes a subsidiary carrier for an carrier
        public static void CloseSubsidiaryCarrier(SubsidiaryCarrier carrier)
        {
            AddCarrierInvoice(carrier.Carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.CarrierExpenses, carrier.Money);

            carrier.Carrier.RemoveSubsidiaryCarrier(carrier);
            Carriers.RemoveCarrier(carrier);

            List<FleetCoach> fleet = carrier.Fleet;

            foreach (FleetCoach t in fleet)
            {
                t.Coach.Carrier = carrier.Carrier;
                carrier.Carrier.AddCoach(t);
            }

            List<Station> stations = carrier.Stations;

            foreach (Station t in stations)
            {
                List<StationContract> contracts = t.GetCarrierContracts(carrier);

                foreach (StationContract t1 in contracts)
                {
                    t1.Carrier = carrier.Carrier;
                }

                if (!carrier.Carrier.Stations.Contains(t))
                {
                    carrier.Carrier.AddStation(t);
                }

                foreach (StationFacility facility in t.GetCurrentStationFacilities(carrier))
                {
                    if (t.GetCarrierStationFacility(carrier.Carrier, facility.Type).Facility.TypeLevel < facility.TypeLevel)
                        t.AddStationFacility(carrier.Carrier, facility, GameObject.GetInstance().GameTime);
                }

                t.ClearFacilities(carrier);
            }
            //moves the terminals from the subsidiary to the parent carrier
            foreach (Station station in carrier.Stations)
            {
                IEnumerable<Terminal> terminals = station.Terminals.GetTerminals().Where(t => t.Carrier == carrier);

                foreach (Terminal terminal in terminals)
                    terminal.Carrier = carrier.Carrier;
            }
        }

        //adds a subsidiary carrier to an carrier
        public static void AddSubsidiaryCarrier(Carrier carrier, SubsidiaryCarrier sCarrier, double money, Station stationHomeBase)
        {
            Terminal.TerminalType terminaltype = sCarrier.CarrierRouteFocus == Route.RouteType.Cargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger;
            AddCarrierInvoice(carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.CarrierExpenses, -money);
            sCarrier.Money = money;
            sCarrier.StartMoney = money;

            sCarrier.Fees = new CarrierFees();

            carrier.AddSubsidiaryCarrier(sCarrier);

            if (!StationHelpers.HasFreeGates(stationHomeBase, sCarrier, terminaltype) && stationHomeBase.Terminals.GetFreeGates(terminaltype) > 1)
            {
                StationHelpers.RentGates(stationHomeBase, sCarrier, StationContract.ContractType.Full, terminaltype, 2);
                //sets all the facilities at an station to none for all carriers
                foreach (Station station in Stations.GetAllStations())
                {
                    foreach (StationFacility.FacilityType type in Enum.GetValues(typeof (StationFacility.FacilityType)))
                    {
                        StationFacility noneFacility = StationFacilities.GetFacilities(type).Find((facility => facility.TypeLevel == 0));

                        station.AddStationFacility(sCarrier, noneFacility, GameObject.GetInstance().GameTime);
                    }
                }


                StationFacility serviceFacility = StationFacilities.GetFacilities(StationFacility.FacilityType.Service).Find(f => f.TypeLevel == 1);
                StationFacility checkinFacility = StationFacilities.GetFacilities(StationFacility.FacilityType.CheckIn).Find(f => f.TypeLevel == 1);

                stationHomeBase.AddStationFacility(sCarrier, serviceFacility, GameObject.GetInstance().GameTime);
                stationHomeBase.AddStationFacility(sCarrier, checkinFacility, GameObject.GetInstance().GameTime);
            }

            foreach (CarrierPolicy policy in carrier.Policies)
                sCarrier.AddCarrierPolicy(policy);

            Carriers.AddCarrier(sCarrier);
        }


        //creates a subsidiary carrier for an carrier
        public static SubsidiaryCarrier CreateSubsidiaryCarrier(Carrier carrier, double money, string name, string iata, Carrier.CarrierMentality mentality, CarrierFocus market,
                                                                Route.RouteType routefocus, Station homebase)
        {
            var profile = new CarrierProfile(name, iata, carrier.Profile.Color, carrier.Profile.CEO, true, GameObject.GetInstance().GameTime.Year, 2199) {Country = homebase.Profile.Country};

            var sCarrier = new SubsidiaryCarrier(carrier, profile, mentality, market, carrier.License, routefocus);

            AddSubsidiaryCarrier(carrier, sCarrier, money, homebase);

            return sCarrier;
        }

        //hires the drivers for a specific coach
        public static void HireCoachDrivers(FleetCoach coach)
        {
            if (Drivers.GetNumberOfUnassignedDrivers() < 10)
                GeneralHelpers.CreateDrivers(50);

            while (coach.Coach.Type.DriverCrew > coach.NumberOfDrivers)
            {
                List<Driver> drivers = Drivers.GetUnassignedDrivers(p => p.Profile.Town.Country == coach.Coach.Carrier.Profile.Country && p.Coaches.Contains(coach.Coach.Type.CoachFamily));

                if (drivers.Count == 0)
                    drivers =
                        Drivers.GetUnassignedDrivers(p => p.Profile.Town.Country.Region == coach.Coach.Carrier.Profile.Country.Region && p.Coaches.Contains(coach.Coach.Type.CoachFamily));

                if (drivers.Count == 0)
                    drivers = Drivers.GetUnassignedDrivers(p => p.Coaches.Contains(coach.Coach.Type.CoachFamily));

                if (drivers.Count == 0)
                {
                    GeneralHelpers.CreateDrivers(4, coach.Coach.Type.CoachFamily);
                    HireCoachDrivers(coach);
                }

                Driver driver = drivers.OrderByDescending(p => p.Rating.CostIndex).FirstOrDefault();

                if (driver != null)
                {
                    coach.Coach.Carrier.AddDriver(driver);

                    driver.Coach = coach;
                    coach.AddDriver(driver);
                }
                else
                    GeneralHelpers.CreateDrivers(50);
            }
        }

        //returns the price for training a driver
        public static double GetTrainingPrice(Driver driver, string coachfamily)
        {
            double dayTrainingPrice = GeneralHelpers.GetInflationPrice(750);

            int days = GetTrainingDays(driver, coachfamily);

            if (HasTrainingFacility(driver.Carrier, coachfamily))
                return dayTrainingPrice;
            return days*dayTrainingPrice;
        }

        //returns the number of training days a driver 
        public static int GetTrainingDays(Driver driver, string coachfamily)
        {
            Manufacturer manufacturer = CoachTypes.GetTypes(t => t.CoachFamily == coachfamily).Select(t => t.Manufacturer).FirstOrDefault();

            bool hasManufacturer = false;
            if (manufacturer != null)
            {
                foreach (string family in driver.Coaches)
                {
                    string family1 = family;
                    Manufacturer tManufacturer = CoachTypes.GetTypes(t => t.CoachFamily == family1).Select(t => t.Manufacturer).FirstOrDefault();

                    if (tManufacturer != null && tManufacturer.ShortName == manufacturer.ShortName)
                        hasManufacturer = true;
                }
            }

            if (hasManufacturer)
                return 2;
            return 14;
        }

        //send a driver for an carrier on training
        public static void SendForTraining(Carrier carrier, Driver driver, string coachfamily, int trainingdays, double price)
        {
            AddCarrierInvoice(carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.CarrierExpenses, -price);

            driver.Training = new DriverTraining(coachfamily, GameObject.GetInstance().GameTime.AddDays(trainingdays));
        }

        //returns the discount factor for a manufactorer for an carrier and for a period
        public static double GetCarrierManufactorerDiscountFactor(Carrier carrier, int length, bool forReputation)
        {
            double score;

            if (forReputation)
                score = 0.3*(1 + (int) carrier.GetReputation());
            else
                score = 0.005*(1 + (int) carrier.GetValue());

            double discountFactor = (Convert.ToDouble(length)/20) + (score);
            double discount = Math.Pow(discountFactor, 5);

            if (discount > 30)
                discount = length*3;

            return discount;
        }

        //returns if an carrier can create a hub at an station
        public static bool CanCreateHub(Carrier carrier, Station station, HubType type)
        {
            Terminal.TerminalType terminaltype = carrier.CarrierRouteFocus == Route.RouteType.Cargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger;

            bool carrierHub = station.GetHubs().Exists(h => h.Carrier == carrier);

            int carrierValue = (int) carrier.GetCarrierValue() + 1;

            int totalCarrierHubs = carrier.GetHubs().Count; // 'Stations.GetAllActiveStations().Sum(a => a.Hubs.Count(h => h.Carrier == carrier));
            double carrierGatesPercent = Convert.ToDouble(station.Terminals.GetNumberOfGates(carrier))/Convert.ToDouble(station.Terminals.GetNumberOfGates(terminaltype))*100;

            switch (type.Type)
            {
                case HubType.TypeOfHub.FocusCity:
                    return !carrierHub && carrier.Money > StationHelpers.GetHubPrice(station, type);
                case HubType.TypeOfHub.RegionalHub:
                    return !carrierHub && carrier.Money > StationHelpers.GetHubPrice(station, type) &&
                           (station.Profile.Size == GeneralHelpers.Size.Large || station.Profile.Size == GeneralHelpers.Size.Medium) && station.GetHubs().Count < 7;
                case HubType.TypeOfHub.FortressHub:
                    return !carrierHub && carrier.Money > StationHelpers.GetHubPrice(station, type) && (station.Profile.Size > GeneralHelpers.Size.Medium) && carrierGatesPercent > 70 &&
                           (totalCarrierHubs < carrierValue);
                case HubType.TypeOfHub.Hub:
                    return !carrierHub && carrier.Money > StationHelpers.GetHubPrice(station, type) && (carrierGatesPercent > 20) && (totalCarrierHubs < carrierValue) &&
                           (station.GetHubs(HubType.TypeOfHub.Hub).Count < (int) station.Profile.Size);
            }

            return false;
        }

        //returns the possible home bases for an carrier
        public static List<Station> GetHomebases(Carrier carrier)
        {
            //var curentFacility = 
            return
                carrier.Stations.FindAll(
                    a =>
                    (a.HasContractType(carrier, StationContract.ContractType.FullService) ||
                     carrier.Fleet.Count(ar => ar.Homebase == a) < a.GetCurrentStationFacility(carrier, StationFacility.FacilityType.Service).ServiceLevel));
        }

        public static List<Station> GetHomebases(Carrier carrier, CoachType type)
        {
            return GetHomebases(carrier, type.MinBayLength);
        }

        public static List<Station> GetHomebases(Carrier carrier, long minbay)
        {
            return GetHomebases(carrier).Where(h => h.GetMaxBayLength() >= minbay).ToList();
        }

        //update the damage scores for an carrier
        public static void UpdateMaintList(Carrier carrier)
        {
            foreach (FleetCoach a in carrier.Fleet)
            {
                carrier.Scores.Maintenance.Add((int) a.Coach.Condition);
            }

            if (carrier.Scores.Maintenance.Count > (carrier.Fleet.Count*2))
            {
                carrier.Scores.Maintenance.RemoveRange(0, carrier.Fleet.Count);
            }
        }

        //updates the carriers ratings for an carrier
        public static void UpdateRatings(Carrier carrier)
        {
            carrier.Ratings.SafetyRating = (int) carrier.Scores.Safety.Average();
            carrier.Ratings.SecurityRating = (int) carrier.Scores.Security.Average();
            carrier.Ratings.CustomerHappinessRating = (int) carrier.Scores.CHR.Average();
            carrier.Ratings.EmployeeHappinessRating = (int) carrier.Scores.EHR.Average();
            carrier.Ratings.MaintenanceRating = (int) carrier.Scores.Maintenance.Average();
        }

        //returns the max loan sum for an carrier
        public static double GetMaxLoanAmount(Carrier carrier)
        {
            return carrier.GetValue()*500000;
        }

        //returns if an carrier can apply for a loan
        public static bool CanApplyForLoan(Carrier carrier, Loan loan)
        {
            double loans = carrier.Loans.Sum(l => l.Amount);

            return loan.Amount + loans < GetMaxLoanAmount(carrier);
        }

        //returns if an carrier has licens for flying between two stations
        public static bool HasCarrierLicens(Carrier carrier, Station station1, Station station2)
        {
            Continent continent1 = Continents.GetContinent(station1.Profile.Country.Region);
            Continent continent2 = Continents.GetContinent(station2.Profile.Country.Region);

            bool isInUnion =
                Unions.GetUnions(station1.Profile.Country, GameObject.GetInstance().GameTime).Intersect(Unions.GetUnions(station2.Profile.Country, GameObject.GetInstance().GameTime)).Any();

            // A motor carrier's authority: Regional -> Domestic -> Long haul, each tier covering the one below it.
            // Regional is the state it is licensed in and the short hops over its line, Domestic is the whole
            // country, and only Long haul takes a coach across the border.
            if (carrier.License == Carrier.CarrierLicense.LongHaul)
                return true;

            if (station1.Profile.Country != station2.Profile.Country)
                return isInUnion;

            if (carrier.License == Carrier.CarrierLicense.Domestic || carrier.License == Carrier.CarrierLicense.ShortHaul)
                return true;

            var state1 = station1.Profile.Town?.State;
            var state2 = station2.Profile.Town?.State;
            return (state1 != null && state1 == state2) || MathHelpers.GetDistance(station1, station2) < 600;
        }

        //the order of the license tiers: Regional, Domestic, Long haul (the old Short haul counts as Domestic)
        public static int GetLicenseRank(Carrier.CarrierLicense license)
        {
            switch (license)
            {
                case Carrier.CarrierLicense.Regional:
                    return 0;
                case Carrier.CarrierLicense.LongHaul:
                    return 2;
                default:
                    return 1;
            }
        }

        public static Carrier.CarrierLicense GetNextLicense(Carrier.CarrierLicense license)
        {
            switch (GetLicenseRank(license))
            {
                case 0:
                    return Carrier.CarrierLicense.Domestic;
                default:
                    return Carrier.CarrierLicense.LongHaul;
            }
        }

        // GetMonthlyPayroll used to live here. Nothing called it, and it was worth deleting rather than leaving,
        // because it read like the payroll calculation while the real one is spread over four places: a driver's
        // standing packet in GetDriverSalary, the hours actually driven in TripCosts, the attendants at a landing
        // in DayTurnHelpers, and the station staff by the hour in GameObjectHelpers. It also charged the station
        // staff a flat monthly rate where the live code treats their fee as hourly over a 173-hour month, so it
        // disagreed with the game by a factor of 173 and would have misled anyone who trusted it.

        //returns the number of routes out of an station for an carrier
        public static int GetStationOutboundRoutes(Carrier carrier, Station station)
        {
            var routes = new List<Route>(carrier.Routes);
            return routes.Count(r => r.Destination1 == station || r.Destination2 == station);
        }

        //checks for any insurance claims up for settlement
        public static void CheckInsuranceSettlements(Carrier carrier)
        {
            // each claim is settled once
            foreach (InsuranceClaim claim in carrier.InsuranceClaims.ToList())
            {
                if (claim.SettlementDate <= GameObject.GetInstance().GameTime)
                {
                    CarrierInsuranceHelpers.ReceiveInsurancePayout(carrier, claim.Policy, claim);
                    carrier.InsuranceClaims.Remove(claim);
                }
            }
        }

        //returns the current price per share for an carrier
        public static double GetPricePerCarrierShare(Carrier carrier)
        {
            var rnd = new Random();

            double price = 0;
            Carrier.CarrierValue value = carrier.GetCarrierValue();

            switch (value)
            {
                case Carrier.CarrierValue.Low:
                    price = 15 + (rnd.NextDouble()*10);
                    break;
                case Carrier.CarrierValue.VeryLow:
                    price = 5 + (rnd.NextDouble()*10);
                    break;
                case Carrier.CarrierValue.Normal:
                    price = 25 + (rnd.NextDouble()*10);
                    break;
                case Carrier.CarrierValue.High:
                    price = 40 + (rnd.NextDouble()*10);
                    break;
                case Carrier.CarrierValue.VeryHigh:
                    price = 55 + (rnd.NextDouble()*10);
                    break;
            }

            return GeneralHelpers.GetInflationPrice(price);
        }

        //adds a number of shares to an carrier
        public static void AddCarrierShares(Carrier carrier, int shares, double sharePrice)
        {
            for (int i = 0; i < shares; i++)
            {
                var share = new CarrierShare(null, sharePrice);
                carrier.Shares.Add(share);
            }
        }

        //sets a number of shares to an carrier
        public static void SetCarrierShares(Carrier carrier, Carrier shareCarrier, int shares)
        {
            for (int i = 0; i < shares; i++)
            {
                CarrierShare share = carrier.Shares.First(s => s.Carrier == null);
                share.Carrier = shareCarrier;
            }
        }

        //creates the standard number of shares for an carrier
        public static void CreateStandardCarrierShares(Carrier carrier, double sharePrice)
        {
            var rnd = new Random();

            const int numberOfShares = 10000;

            int carrierPercentShares = rnd.Next(55, 65);

            carrier.Shares = new List<CarrierShare>();

            int carrierShares = (numberOfShares/100)*carrierPercentShares;

            //carrier shares
            lock (carrier.Shares)
            {
                for (int i = 0; i < carrierShares; i++)
                {
                    var share = new CarrierShare(carrier, sharePrice);

                    carrier.Shares.Add(share);
                }

                //'free' shares
                for (int i = carrierShares; i < numberOfShares; i++)
                {
                    var share = new CarrierShare(null, sharePrice);

                    carrier.Shares.Add(share);
                }
            }
        }

        public static void CreateStandardCarrierShares(Carrier carrier)
        {
            double sharePrice = GetPricePerCarrierShare(carrier);

            CreateStandardCarrierShares(carrier, sharePrice);
        }

        //switches from one carrier to another carrier
        public static void SwitchCarrier(Carrier carrierFrom, Carrier carrierTo)
        {
            while (carrierFrom.Alliances.Count > 0)
            {
                Alliance alliance = carrierFrom.Alliances[0];
                alliance.RemoveMember(carrierFrom);
                alliance.AddMember(new AllianceMember(carrierTo, GameObject.GetInstance().GameTime));
            }
            while (carrierFrom.Facilities.Count > 0)
            {
                CarrierFacility carrierFacility = carrierFrom.Facilities[0];
                carrierFrom.RemoveFacility(carrierFacility);
                carrierTo.AddFacility(carrierFacility);
            }


            while (carrierFrom.GetFleetSize() > 0)
            {
                FleetCoach coach = carrierFrom.Fleet[0];
                carrierFrom.RemoveCoach(coach);
                carrierTo.AddCoach(coach);
                coach.Coach.Carrier = carrierTo;
            }

            while (carrierFrom.Routes.Count > 0)
            {
                Route route = carrierFrom.Routes[0];
                route.Carrier = carrierTo;

                carrierFrom.RemoveRoute(route);
                carrierTo.AddRoute(route);
            }
            while (carrierFrom.Drivers.Count > 0)
            {
                Driver driver = carrierFrom.Drivers[0];
                carrierFrom.RemoveDriver(driver);

                driver.Carrier = carrierTo;
                carrierTo.AddDriver(driver);
            }
            while (carrierFrom.Stations.Count > 0)
            {
                Station station = carrierFrom.Stations[0];
                station.Terminals.SwitchCarrier(carrierFrom, carrierTo);

                foreach (StationFacility facility in station.GetCurrentStationFacilities(carrierFrom))
                {
                    if (facility.TypeLevel > station.GetCurrentStationFacility(carrierTo, facility.Type).TypeLevel)
                        station.AddStationFacility(carrierTo, facility, GameObject.GetInstance().GameTime);

                    StationFacility noneFacility = StationFacilities.GetFacilities(facility.Type).Find(f => f.TypeLevel == 0);

                    station.AddStationFacility(carrierFrom, noneFacility, GameObject.GetInstance().GameTime);
                }
            }
        }

        //returns if a route can be created
        public static bool IsRouteDestinationsOk(Carrier carrier, Station destination1, Station destination2, Route.RouteType routeType, Station stopover1 = null, Station stopover2 = null)
        {
            var calls = new List<Station> { destination1 };
            if (stopover1 != null) calls.Add(stopover1);
            if (stopover2 != null) calls.Add(stopover2);
            calls.Add(destination2);
            return IsRouteDestinationsOk(carrier, calls, routeType);
        }

        /// <summary>
        ///     Whether a service calling at these places in order can be run: a bay free at every one of them, each
        ///     leg a leg some coach of the day could manage, and nothing in the way of the licence. A service with
        ///     stops is judged leg by leg, which is the point of the stops.
        /// </summary>
        public static bool IsRouteDestinationsOk(Carrier carrier, IList<Station> calls, Route.RouteType routeType)
        {
            Terminal.TerminalType type = routeType == Route.RouteType.Cargo ? Terminal.TerminalType.Cargo : Terminal.TerminalType.Passenger;

            if (calls == null || calls.Count < 2) throw new Exception("3000");

            if (routeType != Route.RouteType.Cargo)
            {
                foreach (Station call in calls)
                {
                    if (!StationHelpers.HasFreeGates(call, carrier, type)) throw new Exception("3000");
                }
            }

            var routeOkStatus = RouteOkStatus.Ok;
            var distances = new List<double>();
            for (int i = 0; i < calls.Count - 1; i++)
            {
                distances.Add(MathHelpers.GetDistance(calls[i], calls[i + 1]));
                if (routeOkStatus == RouteOkStatus.Ok) routeOkStatus = GetRouteStatus(calls[i], calls[i + 1], routeType);
            }

            double maxDistance = distances.Max();
            double minDistance = distances.Min();

            IEnumerable<long> query = from a in CoachTypes.GetTypes(t => t.Produced.From < GameObject.GetInstance().GameTime)
                                      select a.Range;

            double maxTripDistance = query.Max();

            if (minDistance <= Route.MinRouteDistance || maxDistance > maxTripDistance)
                routeOkStatus = RouteOkStatus.WrongDistance;

            if (routeOkStatus == RouteOkStatus.Ok)
                return true;
            if (routeOkStatus == RouteOkStatus.AppropriateType)
                throw new Exception("3002");
            if (routeOkStatus == RouteOkStatus.WrongDistance)
                throw new Exception("3001");
            if (routeOkStatus == RouteOkStatus.MissingCargo)
                throw new Exception("3003");
            if (routeOkStatus == RouteOkStatus.Restrictions)
                throw new Exception("3005");
            if (routeOkStatus == RouteOkStatus.MissingLicense)
                throw new Exception("3004");
            throw new Exception("3000");
        }

        //returns if two stations can have route between them and if the carrier has license for the route
        private static bool CheckRouteOk(Station station1, Station station2, Route.RouteType routeType)
        {
            bool isCargoRouteOk = true;
            if (routeType == Route.RouteType.Cargo)
                isCargoRouteOk = AIHelpers.IsCargoRouteDestinationsCorrect(station1, station2, GameObject.GetInstance().HumanCarrier);

            return isCargoRouteOk && HasCarrierLicens(GameObject.GetInstance().HumanCarrier, station1, station2) && AIHelpers.IsRouteInCorrectArea(station1, station2) &&
                   !TripRestrictions.HasRestriction(station1.Profile.Country, station2.Profile.Country, GameObject.GetInstance().GameTime, TripRestriction.RestrictionType.Trips) &&
                   !TripRestrictions.HasRestriction(station2.Profile.Country, station1.Profile.Country, GameObject.GetInstance().GameTime, TripRestriction.RestrictionType.Trips) &&
                   !TripRestrictions.HasRestriction(GameObject.GetInstance().HumanCarrier, station1.Profile.Country, station2.Profile.Country, GameObject.GetInstance().GameTime);
        }

        private static RouteOkStatus GetRouteStatus(Station station1, Station station2, Route.RouteType routeType)
        {
            var status = RouteOkStatus.Ok;

            if (routeType == Route.RouteType.Cargo || routeType == Route.RouteType.Mixed)
                if (!AIHelpers.IsCargoRouteDestinationsCorrect(station1, station2, GameObject.GetInstance().HumanCarrier))
                    status = RouteOkStatus.MissingCargo;

            if (status == RouteOkStatus.Ok)
            {
                if (HasCarrierLicens(GameObject.GetInstance().HumanCarrier, station1, station2))
                {
                    if (AIHelpers.IsRouteInCorrectArea(station1, station2))
                    {
                        if (!TripRestrictions.HasRestriction(station1.Profile.Country, station2.Profile.Country, GameObject.GetInstance().GameTime, TripRestriction.RestrictionType.Trips) &&
                            !TripRestrictions.HasRestriction(station2.Profile.Country, station1.Profile.Country, GameObject.GetInstance().GameTime, TripRestriction.RestrictionType.Trips) &&
                            !TripRestrictions.HasRestriction(GameObject.GetInstance().HumanCarrier, station1.Profile.Country, station2.Profile.Country, GameObject.GetInstance().GameTime))
                            status = RouteOkStatus.Ok;
                        else
                            status = RouteOkStatus.Restrictions;
                    }
                    else
                        status = RouteOkStatus.AppropriateType;
                }
                else
                    status = RouteOkStatus.MissingLicense;
            }

            return status;
        }

        //returns the salary for a driver at an carrier
        /// <summary>
        ///     What a rating's cost index is worth as a multiple of an average employee's pay. The indexes run 3, 4, 5,
        ///     7, 10 across the five ratings, so using one as a multiplier directly made a top-rated driver cost three
        ///     and a third times a bottom-rated one and ten times the base fee. A real intercity driver at the top of
        ///     the scale earns something like 1.4 times a new one, so the index is mapped onto a scale centred on 1 at
        ///     the middle rating: 0.90, 0.95, 1.00, 1.10, 1.25.
        /// </summary>
        public static double PayGrade(int costIndex)
        {
            return 0.75 + 0.05*costIndex;
        }

        /// <summary>
        ///     A driver's monthly packet. This is the standing part of the pay only: the hours actually driven are
        ///     charged to the trip that drove them, at a real hourly wage, by <see cref="TripCosts" />. So what is paid
        ///     here is the retainer behind that - benefits, payroll tax, standby and the guaranteed minimum - which for
        ///     an intercity driver runs about 30% of a working year's wages.
        /// </summary>
        public static double GetDriverSalary(Carrier carrier, Driver driver)
        {
            double driverBasePrice = carrier.Fees.GetValue(FeeTypes.GetType("Driver Base Salary"));

            double driverExperienceFee = driver.Coaches.Count*GeneralHelpers.GetInflationPrice(6.0);

            return (PayGrade(driver.Rating.CostIndex)*driverBasePrice + driverExperienceFee)*DriverMoraleService.PayFactor(carrier);
        }

        private enum RouteOkStatus
        {
            Ok,
            MissingCargo,
            WrongDistance,
            AppropriateType,
            Restrictions,
            MissingLicense
        };
    }

    //carrier insurance helpers
    public class CarrierInsuranceHelpers
    {
        //add insurance policy
        public static CarrierInsurance CreatePolicy(Carrier carrier, CarrierInsurance.InsuranceType type, CarrierInsurance.InsuranceScope scope, CarrierInsurance.PaymentTerms terms, bool allCoaches,
                                                    double length, int amount)
        {
            #region Method Setup

            var rnd = new Random();
            double modifier = GetRatingModifier(carrier);
            double hub = carrier.GetHubs().Count()*0.1;
            var policy = new CarrierInsurance(type, scope, terms, amount)
                {
                    InsuranceEffective = GameObject.GetInstance().GameTime,
                    InsuranceExpires = GameObject.GetInstance().GameTime.AddYears((int) length),
                    PolicyIndex = GameObject.GetInstance().GameTime.ToString(CultureInfo.InvariantCulture) + carrier,
                    TermLength = length
                };
            switch (policy.InsTerms)
            {
                case CarrierInsurance.PaymentTerms.Monthly:
                    policy.RemainingPayments = length*12;
                    break;
                case CarrierInsurance.PaymentTerms.Quarterly:
                    policy.RemainingPayments = length*4;
                    break;
                case CarrierInsurance.PaymentTerms.Biannual:
                    policy.RemainingPayments = length*2;
                    break;
                case CarrierInsurance.PaymentTerms.Annual:
                    policy.RemainingPayments = length;
                    break;
            }
            //sets up multipliers based on the type and scope of insurance policy
            var typeMultipliers = new Dictionary<CarrierInsurance.InsuranceType, double>();
            var scopeMultipliers = new Dictionary<CarrierInsurance.InsuranceScope, double>();
            double typeMPublic = modifier;
            double typeMPassenger = modifier + 0.2;
            double typeMCSL = modifier + 0.5;
            double typeMFull = modifier + 1;

            double scMStation = modifier;
            double scMDomestic = modifier + 0.2;
            double scMHub = modifier + hub + 0.5;
            double scMGlobal = modifier + hub + 1;

            #endregion

            #region Domestic/Int'l Station Counter

            int i = 0;
            int j = 0;
            foreach (Station station in GameObject.GetInstance().HumanCarrier.Stations)
            {
                if (station.Profile.Country != GameObject.GetInstance().HumanCarrier.Profile.Country)
                {
                    i++;
                }
                else j++;
            }

            #endregion

            // all the decision making for monthly payment amounts and deductibles

            #region Public Liability

            switch (type)
            {
                case CarrierInsurance.InsuranceType.PublicLiability:
                    switch (scope)
                    {
                        case CarrierInsurance.InsuranceScope.Station:
                            policy.Deductible = amount*0.005;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMPublic*scMStation;
                            if (terms == CarrierInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CarrierInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CarrierInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CarrierInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;

                            break;

                        case CarrierInsurance.InsuranceScope.Domestic:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMPublic*scMDomestic;
                            if (terms == CarrierInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CarrierInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CarrierInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CarrierInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CarrierInsurance.InsuranceScope.Hub:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMPublic*scMHub;
                            if (terms == CarrierInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CarrierInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CarrierInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CarrierInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CarrierInsurance.InsuranceScope.Global:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMPublic*scMGlobal;
                            if (terms == CarrierInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CarrierInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CarrierInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CarrierInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;
                    }
                    break;

                    #endregion

                    #region Passenger Liability

                case CarrierInsurance.InsuranceType.PassengerLiability:
                    switch (scope)
                    {
                        case CarrierInsurance.InsuranceScope.Station:
                            policy.Deductible = amount*0.005;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMPassenger*scMStation;
                            if (terms == CarrierInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CarrierInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CarrierInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CarrierInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CarrierInsurance.InsuranceScope.Domestic:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMPassenger*scMDomestic;
                            if (terms == CarrierInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CarrierInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CarrierInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CarrierInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CarrierInsurance.InsuranceScope.Hub:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMPassenger*scMHub;
                            if (terms == CarrierInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CarrierInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CarrierInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CarrierInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CarrierInsurance.InsuranceScope.Global:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMPassenger*scMGlobal;
                            if (terms == CarrierInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CarrierInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CarrierInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CarrierInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;
                    }
                    break;

                    #endregion

                    #region Combined Single Limit

                case CarrierInsurance.InsuranceType.CombinedSingleLimit:
                    switch (scope)
                    {
                        case CarrierInsurance.InsuranceScope.Station:
                            policy.Deductible = amount*0.005;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMCSL*scMStation;
                            if (terms == CarrierInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CarrierInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CarrierInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CarrierInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CarrierInsurance.InsuranceScope.Domestic:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMCSL*scMDomestic;
                            if (terms == CarrierInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CarrierInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CarrierInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CarrierInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CarrierInsurance.InsuranceScope.Hub:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMCSL*scMHub;
                            if (terms == CarrierInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CarrierInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CarrierInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CarrierInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CarrierInsurance.InsuranceScope.Global:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMCSL*scMGlobal;
                            if (terms == CarrierInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CarrierInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CarrierInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CarrierInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;
                    }
                    break;

                    #endregion

                    #region Full Coverage

                case CarrierInsurance.InsuranceType.FullCoverage:
                    switch (scope)
                    {
                        case CarrierInsurance.InsuranceScope.Station:
                            policy.Deductible = amount*0.005;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMFull*scMStation;
                            if (terms == CarrierInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CarrierInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CarrierInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CarrierInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CarrierInsurance.InsuranceScope.Domestic:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMFull*scMDomestic;
                            if (terms == CarrierInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CarrierInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CarrierInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CarrierInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CarrierInsurance.InsuranceScope.Hub:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMFull*scMHub;
                            if (terms == CarrierInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CarrierInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CarrierInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CarrierInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;

                        case CarrierInsurance.InsuranceScope.Global:
                            policy.Deductible = amount*0.001;
                            policy.PaymentAmount = policy.InsuredAmount*(4/10)*typeMFull*scMGlobal;
                            if (terms == CarrierInsurance.PaymentTerms.Annual) policy.PaymentAmount = policy.InsuredAmount/length;
                            if (terms == CarrierInsurance.PaymentTerms.Biannual) policy.PaymentAmount = policy.InsuredAmount/length/2;
                            if (terms == CarrierInsurance.PaymentTerms.Quarterly) policy.PaymentAmount = policy.InsuredAmount/length/4;
                            if (terms == CarrierInsurance.PaymentTerms.Monthly) policy.PaymentAmount = policy.InsuredAmount/length/12;
                            break;
                    }

                    #endregion

                    break;
            }

            if (allCoaches)
            {
                amount *= carrier.Fleet.Count();
                policy.PaymentAmount *= (carrier.Fleet.Count()*0.95);
            }
            return policy;
        }


        //gets insurance rate modifiers based on security, safety, and coach state of maintenance
        public static double GetRatingModifier(Carrier carrier)
        {
            double mod = 1;
            mod += (100 - carrier.Ratings.MaintenanceRating)/100.0;
            mod += (100 - carrier.Ratings.SafetyRating)/150.0;
            mod += (100 - carrier.Ratings.SecurityRating)/100.0;
            return mod;
        }


        //extend or modify policy
        public static void ModifyPolicy(Carrier carrier, string index, CarrierInsurance newPolicy)
        {
            //CarrierInsurance oldPolicy = carrier.InsurancePolicies[index];
            //use the index to compare the new policy passed in to the existing one and make changes
        }

        public static void CheckExpiredInsurance(Carrier carrier)
        {
            DateTime date = GameObject.GetInstance().GameTime;
            foreach (CarrierInsurance policy in carrier.InsurancePolicies)
            {
                if (policy.InsuranceExpires < date)
                {
                    carrier.RemoveInsurance(policy);
                }
            }
        }

        public static void MakeInsurancePayment(Carrier carrier)
        {
            foreach (CarrierInsurance policy in carrier.InsurancePolicies)
            {
                if (policy.RemainingPayments > 0)
                {
                    if (policy.NextPaymentDue.Month == GameObject.GetInstance().GameTime.Month)
                    {
                        CarrierHelpers.AddCarrierInvoice(carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Maintenances, -policy.PaymentAmount);
                        policy.RemainingPayments--;
                        switch (policy.InsTerms)
                        {
                            case CarrierInsurance.PaymentTerms.Monthly:
                                policy.NextPaymentDue = GameObject.GetInstance().GameTime.AddMonths(1);
                                break;
                            case CarrierInsurance.PaymentTerms.Quarterly:
                                policy.NextPaymentDue = GameObject.GetInstance().GameTime.AddMonths(3);
                                break;
                            case CarrierInsurance.PaymentTerms.Biannual:
                                policy.NextPaymentDue = GameObject.GetInstance().GameTime.AddMonths(6);
                                break;
                            case CarrierInsurance.PaymentTerms.Annual:
                                policy.NextPaymentDue = GameObject.GetInstance().GameTime.AddMonths(12);
                                break;
                        }
                    }
                }
            }
        }


        //for general damage or claims
        public static void FileInsuranceClaim(Carrier carrier, CarrierInsurance policy, int damage)
        {
            var claim = new InsuranceClaim(carrier, null, null, GameObject.GetInstance().GameTime, damage);
            carrier.InsuranceClaims.Add(claim);
            var news = new News(News.NewsType.CarrierNews, GameObject.GetInstance().GameTime, "Insurance Claim Filed", "You have filed an insurance claim. Reference: " + claim.Index);
        }

        //for damage and claims involving an station or station facility
        public static void FileInsuranceClaim(Carrier carrier, Station station, int damage)
        {
            var claim = new InsuranceClaim(carrier, null, station, GameObject.GetInstance().GameTime, damage);
            carrier.InsuranceClaims.Add(claim);
            var news = new News(News.NewsType.CarrierNews, GameObject.GetInstance().GameTime, "Insurance Claim Filed", "You have filed an insurance claim. Reference: " + claim.Index);
        }


        //for damage and claims involving an coach
        public static void FileInsuranceClaim(Carrier carrier, FleetCoach coach, int damage)
        {
            var claim = new InsuranceClaim(carrier, coach, null, GameObject.GetInstance().GameTime, damage);
            carrier.InsuranceClaims.Add(claim);
            var news = new News(News.NewsType.CarrierNews, GameObject.GetInstance().GameTime, "Insurance Claim Filed", "You have filed an insurance claim. Reference: " + claim.Index);
        }

        //for damage and claims involving an coach and station or station facility
        public static void FileInsuranceClaim(Carrier carrier, FleetCoach coach, Station station, int damage)
        {
            var claim = new InsuranceClaim(carrier, coach, station, GameObject.GetInstance().GameTime, damage);
            carrier.InsuranceClaims.Add(claim);
            var news = new News(News.NewsType.CarrierNews, GameObject.GetInstance().GameTime, "Insurance Claim Filed", "You have filed an insurance claim. Reference: " + claim.Index);
        }


        public static void ReceiveInsurancePayout(Carrier carrier, CarrierInsurance policy, InsuranceClaim claim)
        {
            if (claim.Damage > policy.Deductible)
            {
                claim.Damage -= (int) policy.Deductible;
                policy.Deductible = 0;
                policy.InsuredAmount -= claim.Damage;
                // the deductible is what the carrier bears itself: the insurer pays the rest
                CarrierHelpers.AddCarrierInvoice(carrier, GameObject.GetInstance().GameTime, Invoice.InvoiceType.Maintenances, claim.Damage);
                var news = new News(News.NewsType.CarrierNews, GameObject.GetInstance().GameTime, "Insurance Claim Payout",
                                    "You have received an insurance payout in the amount of $" + claim.Damage.ToString(CultureInfo.InvariantCulture) + ". This was for claim number " + claim.Index);
            }

            else if (claim.Damage < policy.Deductible)
            {
                policy.Deductible -= claim.Damage;
                //Warnings.AddWarning("Low Damage", "The damage incurred was less than your deductible, so you will not receive an insurance payout for this claim! \n Reference: " + claim.Index);
            }
        }
    }
}