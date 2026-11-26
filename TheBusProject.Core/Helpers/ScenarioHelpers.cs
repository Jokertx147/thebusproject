using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TheBusProject.Helpers.Workers;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Scenarios;
using TheBusProject.Models.General.Statistics;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    //the helpers class for scenarios
    public class ScenarioHelpers
    {
        #region Public Methods and Operators

        public static void SetupScenario(Scenario scenario)
        {
            Carrier carrier = scenario.Carrier;

            GameObject.GetInstance().DayRoundEnabled = true;
            GameObject.GetInstance().TimeZone = scenario.Homebase.Profile.TimeZone;
            GameObject.GetInstance().Difficulty = scenario.Difficulty;
            GameObject.GetInstance().GameTime = new DateTime(scenario.StartYear, 1, 1);
            GameObject.GetInstance().StartDate = GameObject.GetInstance().GameTime;
            //sets the fuel price
            GameObject.GetInstance().FuelPrice =
                Inflations.GetInflation(GameObject.GetInstance().GameTime.Year).FuelPrice;

            GameObject.GetInstance().SetHumanCarrier(carrier);
            GameObject.GetInstance().MainCarrier = GameObject.GetInstance().HumanCarrier;
            //GameObject.GetInstance().HumanCarrier.Money = scenario.StartCash;

            GameObject.GetInstance().Scenario = new ScenarioObject(scenario);

            Station station = scenario.Homebase;

            SetupScenarioStation(carrier, station);

            // PassengerHelpers.CreateDestinationPassengers();

            CoachHelpers.CreateStartUpCoaches();

            int driversPool = 100*Carriers.GetAllCarriers().Count;

            GeneralHelpers.CreateDrivers(driversPool);

            int instructorsPool = 75*Carriers.GetAllCarriers().Count;

            GeneralHelpers.CreateInstructors(instructorsPool);

            SetupScenarioCarriers(scenario);
            SetupScenario();
            Setup.SetupAlliances();

            // the scenario's own start: its cash, and its debt as a loan to pay off
            carrier.Money = scenario.StartCash;
            carrier.StartMoney = scenario.StartCash;
            if (scenario.StartDebt > 0)
            {
                carrier.AddLoan(new Loan(GameObject.GetInstance().GameTime, scenario.StartDebt, 120, GeneralHelpers.GetCarrierLoanRate(carrier)));
            }

            PassengerHelpers.CreateCarrierDestinationDemand();

            GeneralHelpers.CreateHolidays(GameObject.GetInstance().GameTime.Year);

            UserInterface.ScenarioStarted?.Invoke(GameObject.GetInstance().HumanCarrier);

            // GameObject.GetInstance().HumanCarrier.Money = 1000000000;

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

            // the demand must exist before the clock runs, otherwise months pass with empty trips
            PassengerHelpers.CreateDestinationDemand();
            SetupScenarioPassengerDemand(scenario);
            GameObjectWorker.GetInstance().StartPaused();
        }

        public static void UpdateScenario(ScenarioObject scenario)
        {
            // the goals (see ScenarioService: clearer checks, and a scenario ends only once)
            ScenarioService.Check(scenario);
            UpdatePassengerDemands(scenario);
        }

        #endregion

        #region Methods

        private static void EndScenario(ScenarioFailure failure)
        {
            GameObject.GetInstance().Scenario.ScenarioFailed = failure;
        }

        //sets up the passenger demand for a scenario

        //sets up the human carrier 
        private static void SetupHumanCarrier(Scenario scenario)
        {
            foreach (Station destination in scenario.Destinations)
            {
                SetupScenarioStation(GameObject.GetInstance().HumanCarrier, destination);
            }

            foreach (var fleetCoach in scenario.Fleet)
            {
                for (int i = 0; i < fleetCoach.Value; i++)
                {
                    GameObject.GetInstance()
                              .HumanCarrier.AddCoach(
                                  CarrierHelpers.CreateCoach(GameObject.GetInstance().HumanCarrier, fleetCoach.Key));
                }
            }
            foreach (ScenarioCarrierRoute route in scenario.Routes)
            {
                SetupScenarioRoute(route, GameObject.GetInstance().HumanCarrier);
            }
        }

        private static void SetupOpponentCarrier(ScenarioCarrier carrier)
        {
            StationHelpers.RentGates(carrier.Homebase, carrier.Carrier, StationContract.ContractType.Full,
                                     Terminal.TerminalType.Passenger);

            StationFacility checkinFacility =
                StationFacilities.GetFacilities(StationFacility.FacilityType.CheckIn).Find(f => f.TypeLevel == 1);
            StationFacility facility =
                StationFacilities.GetFacilities(StationFacility.FacilityType.Service)
                                 .Find((f => f.TypeLevel == 1));

            carrier.Homebase.AddStationFacility(carrier.Carrier, facility, GameObject.GetInstance().GameTime);
            carrier.Homebase.AddStationFacility(carrier.Carrier, checkinFacility, GameObject.GetInstance().GameTime);

            foreach (ScenarioCarrierRoute saroute in carrier.Routes)
            {
                SetupScenarioRoute(saroute, carrier.Carrier);
            }
        }

        //sets up the different scenario setting
        private static void SetupScenario()
        {
            Parallel.ForEach(
                Stations.GetAllStations(),
                station =>
                    {
                        foreach (Carrier carrier in Carriers.GetAllCarriers())
                        {
                            foreach (
                                StationFacility.FacilityType type in Enum.GetValues(typeof (StationFacility.FacilityType)))
                            {
                                StationFacility noneFacility =
                                    StationFacilities.GetFacilities(type)
                                                     .Find((facility => facility.TypeLevel == 0));

                                station.AddStationFacility(carrier, noneFacility, GameObject.GetInstance().GameTime);
                            }
                        }
                        StationHelpers.CreateStationWeather(station);
                    });

            foreach (Carrier carrier in Carriers.GetAllCarriers())
            {
                carrier.Money = GameObject.GetInstance().StartMoney;

                if (carrier.IsHuman)
                {
                    GameObject.GetInstance().HumanMoney = carrier.Money;
                }

                carrier.StartMoney = carrier.Money;

                carrier.Fees = new CarrierFees();
                carrier.AddCarrierPolicy(new CarrierPolicy("Cancellation Minutes", 150));
            }
        }

        private static void SetupScenarioCarriers(Scenario scenario)
        {
            var carriers = new List<Carrier> {scenario.Carrier};

            foreach (ScenarioCarrier carrier in scenario.OpponentCarriers)
            {
                carriers.Add(carrier.Carrier);
                SetupOpponentCarrier(carrier);
            }

            Carriers.Clear();

            carriers.ForEach(Carriers.AddCarrier);

            SetupHumanCarrier(scenario);
        }

        private static void SetupScenarioStation(Carrier carrier, Station station, int quantity = 2)
        {
            for (int i = 0; i < quantity; i++)
            {
                if (!StationHelpers.HasFreeGates(station, carrier))
                {
                    StationHelpers.RentGates(station, carrier, StationContract.ContractType.Full);
                }
            }

            StationFacility checkinFacility =
                StationFacilities.GetFacilities(StationFacility.FacilityType.CheckIn).Find(f => f.TypeLevel == 1);
            StationFacility facility =
                StationFacilities.GetFacilities(StationFacility.FacilityType.Service)
                                 .Find((f => f.TypeLevel == 1));

            station.AddStationFacility(carrier, facility, GameObject.GetInstance().GameTime);
            station.AddStationFacility(carrier, checkinFacility, GameObject.GetInstance().GameTime);
        }

        private static void SetupScenarioPassengerDemand(Scenario scenario)
        {
            foreach (ScenarioPassengerDemand demand in scenario.PassengerDemands)
            {
                if (demand.Station != null)
                {
                    PassengerHelpers.ChangePaxDemand(demand.Factor);
                }
                if (demand.Country != null)
                {
                    ScenarioPassengerDemand demand1 = demand;
                    PassengerHelpers.ChangePaxDemand(
                        Stations.GetAllStations(a => a.Profile.Country == demand1.Country),
                        demand.Factor);
                }
            }
        }

        private static void SetupScenarioRoute(ScenarioCarrierRoute saroute, Carrier carrier)
        {
            SetupScenarioStation(carrier, saroute.Destination1, saroute.Quantity);
            SetupScenarioStation(carrier, saroute.Destination2, saroute.Quantity);

            double price = PassengerHelpers.GetPassengerPrice(saroute.Destination1, saroute.Destination2);

            for (int i = 0; i < saroute.Quantity; i++)
            {
                Guid id = Guid.NewGuid();

                var route = new PassengerRoute(
                    id.ToString(),
                    saroute.Destination1,
                    saroute.Destination2,
                    GameObject.GetInstance().GameTime,
                    price);

                RouteClassesConfiguration configuration = AIHelpers.GetRouteConfiguration(route);

                foreach (RouteClassConfiguration classConfiguration in configuration.GetClasses())
                {
                    route.GetRouteCoachClass(classConfiguration.Type).FarePrice = price
                                                                                     *GeneralHelpers.ClassToPriceFactor
                                                                                          (classConfiguration.Type);

                    foreach (RouteFacility rfacility in classConfiguration.GetFacilities())
                    {
                        route.GetRouteCoachClass(classConfiguration.Type).AddFacility(rfacility);
                    }
                }

                carrier.AddRoute(route);

                FleetCoach fCoach = CarrierHelpers.CreateCoach(carrier, saroute.CoachType);
                carrier.AddCoach(fCoach);

                fCoach.AddRoute(route);

                AIHelpers.CreateRouteTimeTable(route, fCoach);

                fCoach.Status = FleetCoach.CoachStatus.ToRouteStart;
                CarrierHelpers.HireCoachDrivers(fCoach);

                route.LastUpdated = GameObject.GetInstance().GameTime;
            }
        }

        //updates the pax demands for the scenario

        //adds another month for where the scenario parameter has not been fulfilled and returns if failing scenario
        private static bool UpdateFailureValue(ScenarioObject scenario, ScenarioFailure failure)
        {
            ScenarioFailureObject failureObject = scenario.GetScenarioFailure(failure);
            int monthsSinceLastFailure = MathHelpers.GetMonthsBetween(
                failureObject.LastFailureTime,
                GameObject.GetInstance().GameTime);

            if (monthsSinceLastFailure == 1)
            {
                failureObject.Failures++;
            }
            else
            {
                failureObject.Failures = 1;
            }

            failureObject.LastFailureTime = GameObject.GetInstance().GameTime;

            return failureObject.Failures == failure.MonthsOfFailure;
        }

        private static void UpdatePassengerDemands(ScenarioObject scenario)
        {
            foreach (ScenarioPassengerDemand demand in scenario.Scenario.PassengerDemands)
            {
                if (GameObject.GetInstance().GameTime.ToShortDateString() == demand.EndDate.ToShortDateString())
                {
                    if (demand.Station != null)
                    {
                        PassengerHelpers.ChangePaxDemand(-demand.Factor);
                    }
                    if (demand.Country != null)
                    {
                        ScenarioPassengerDemand demand1 = demand;
                        PassengerHelpers.ChangePaxDemand(
                            Stations.GetAllStations(a => a.Profile.Country == demand1.Country),
                            -demand.Factor);
                    }
                }
            }
        }

        #endregion

        //sets up a scenario

        //ends a scenario
    }
}