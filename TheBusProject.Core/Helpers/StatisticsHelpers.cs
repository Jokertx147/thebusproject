using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;
using TheBusProject.Models.General.Statistics;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    public static class StatisticsHelpers
    {
        #region Public Methods and Operators

        public static List<int> GaussianList(int vals, int avg, double stdv = 1)
        {
            var dv = new List<int>();
            for (int i = 0; i <= vals; i++)
            {
                dv.Add((int) NextGaussian(new Random(), avg, stdv));
            }

            return dv;
        }

        public static List<double> GaussianList(int vals, double avg, double stdv = 1)
        {
            var dv = new List<double>();
            for (int i = 0; i <= vals; i++)
            {
                dv.Add(NextGaussian(new Random(), avg, stdv));
            }

            return dv;
        }

        //calculates maximum difference

        //calculates average AI ticket price
        public static double GetAIAvgTicketPPD()
        {
            var priceDiff = new List<double>();
            var aiPrices = (from carrier in Carriers.GetCarriers(a => !a.IsHuman)
                            let aiEconPrices = (from r in carrier.Routes
                                                where r.Type == Route.RouteType.Passenger || r.Type == Route.RouteType.Mixed
                                                select ((PassengerRoute) r).GetFarePrice(CoachClass.ClassType.EconomyClass)).ToList()
                            let aiBusPrices = (from r in carrier.Routes
                                               where r.Type == Route.RouteType.Passenger || r.Type == Route.RouteType.Mixed
                                               select ((PassengerRoute) r).GetFarePrice(CoachClass.ClassType.BusinessClass)).ToList()
                            let aiFirstPrices = (from r in carrier.Routes
                                                 where r.Type == Route.RouteType.Passenger || r.Type == Route.RouteType.Mixed
                                                 select ((PassengerRoute) r).GetFarePrice(CoachClass.ClassType.FirstClass)).ToList()
                            let distance = (from r in carrier.Routes select MathHelpers.GetDistance(r.Destination1, r.Destination2)).DefaultIfEmpty(0).Average()
                            let avgEP = aiEconPrices.DefaultIfEmpty(0).Average()
                            let avgBP = aiBusPrices.DefaultIfEmpty(0).Average()
                            let avgFP = aiFirstPrices.DefaultIfEmpty(0).Average()
                            let avgPrice = ((avgEP*0.7) + (avgBP*0.2) + (avgFP*0.1))/3
                            select avgPrice/distance).ToList();
            return aiPrices.DefaultIfEmpty(0).Average();
        }

        //calculate average human ticket price per distance

        //returns dictionary of AI average fill degrees
        public static Dictionary<Carrier, double> GetAIFillDegree()
        {
            var aIafd = new Dictionary<Carrier, double>();
            foreach (Carrier carrier in Carriers.GetCarriers(a => !a.IsHuman))
            {
                List<double> aiFillDegree = (from r in carrier.Routes select r.GetFillingDegree()).ToList();
                aIafd.Add(carrier, aiFillDegree.DefaultIfEmpty(0).Average());
            }
            return aIafd;
        }

        //calculates human carrier average on-time %

        //calculates AI carrier average on-time %
        public static Dictionary<Carrier, double> GetAIonTime()
        {
            var aiOnTime = new Dictionary<Carrier, double>();
            foreach (Carrier carrier in Carriers.GetCarriers(a => !a.IsHuman))
            {
                double otp = TripStatistics.OnTimePercent(carrier);
                aiOnTime.Add(carrier, otp);
            }
            return aiOnTime;
        }

        public static double GetCarrierFillAverage(Carrier carrier)
        {
            List<double> fillDegree = (from r in carrier.Routes select r.GetFillingDegree()).ToList();

            double avg = fillDegree.DefaultIfEmpty(0).Average();
            return fillDegree.DefaultIfEmpty(0).Average();
        }

        //returns dictionary of all carrier average on-time %

        //returns the average employee wages
        public static double GetAverageEmployeeWages()
        {
            int numberOfCarriers = Carriers.GetNumberOfCarriers();
            var carrierWages = new Dictionary<Carrier, double>(GetEmployeeWages());

            double totalWages = Carriers.GetAllCarriers().Sum(carrier => GetEmployeeWages()[carrier]);

            return totalWages/numberOfCarriers;
        }

        //returns the average employee wages for the competitors for an carriers
        public static double GetAverageEmployeeWages(Carrier carrier)
        {
            int numberOfCarriers = Carriers.GetNumberOfCarriers() - 1;
            var carrierWages = new Dictionary<Carrier, double>(GetEmployeeWages());

            double totalWages = Carriers.GetAllCarriers().Where(competitor => competitor != carrier).Sum(competitor => GetEmployeeWages()[competitor]);

            return totalWages/numberOfCarriers;
        }

        public static int GetBestYear(Carrier carrier)
        {
            int high = 0;
            int year = 0;
            {
                int years = GameObject.GetInstance().GameTime.Year - GameObject.StartYear;
                for (int z = GameObject.StartYear; z < (z + years); z++)
                {
                    int sc = GetScore(carrier, z);
                    if (sc > high)
                    {
                        high = sc;
                        year = z;
                    }
                }
            }

            return year;
        }

        //creates a dictionary of average employee discounts
        public static Dictionary<Carrier, double> GetEmployeeDiscounts()
        {
            var employeeDiscounts = new Dictionary<Carrier, double>();
            foreach (Carrier carrier in Carriers.GetAllCarriers())
            {
                double eDiscount = carrier.Fees.GetValue(FeeTypes.GetType("Employee Discount"));
                employeeDiscounts.Add(carrier, eDiscount);
            }
            return employeeDiscounts;
        }

        public static Dictionary<Carrier, double> GetEmployeeWages()
        {
            var employeeWages = new Dictionary<Carrier, double>();
            foreach (Carrier carrier in Carriers.GetAllCarriers())
            {
                double sWage = carrier.Fees.GetValue(FeeTypes.GetType("Support Wage"));
                double mWage = carrier.Fees.GetValue(FeeTypes.GetType("Maintenance Wage"));
                double pWage = carrier.Fees.GetValue(FeeTypes.GetType("Driver Base Salary")); // *carrier.Drivers.Count();
                double cWage = carrier.Fees.GetValue(FeeTypes.GetType("Cabin Wage"));
                double iWage = carrier.Fees.GetValue(FeeTypes.GetType("Instructor Base Salary"));
                // *DriverModel.TripSchool.MaxNumberOfInstructors;
                int cabinCrew =
                    carrier.Routes.Where(r => r.Type == Route.RouteType.Passenger || r.Type == Route.RouteType.Mixed)
                           .Sum(r => ((PassengerRoute) r).GetTotalAttendants());
                int serviceCrew =
                    carrier.Stations.SelectMany(a => a.GetCurrentStationFacilities(carrier))
                           .Where(a => a.EmployeeType == StationFacility.EmployeeTypes.Support)
                           .Sum(a => a.NumberOfEmployees);
                int maintenanceCrew =
                    carrier.Stations.SelectMany(a => a.GetCurrentStationFacilities(carrier))
                           .Where(a => a.EmployeeType == StationFacility.EmployeeTypes.Maintenance)
                           .Sum(a => a.NumberOfEmployees);

                double averageWage = (sWage + mWage + pWage + cWage + iWage)/5;

                employeeWages.Add(carrier, averageWage);
            }
            return employeeWages;
        }

        public static double GetFillAverage()
        {
            var fillDegrees = Carriers.GetAllCarriers().Select(carrier => (from r in carrier.Routes select r.GetFillingDegree()).ToList()).Select(fillDegree => fillDegree.DefaultIfEmpty(0).Average()).ToList();
            return fillDegrees.DefaultIfEmpty(0).Average();
        }

        public static Dictionary<Carrier, double> GetFillAverages()
        {
            var fillAverage = new Dictionary<Carrier, double>();
            foreach (Carrier carrier in Carriers.GetAllCarriers())
            {
                List<double> fillDegree = (from r in carrier.Routes
                                           where r.Type == Route.RouteType.Mixed || r.Type == Route.RouteType.Passenger
                                           select r.GetFillingDegree()).ToList();
                double uFillDegree = fillDegree.DefaultIfEmpty(0).Average();
                fillAverage.Add(carrier, uFillDegree);
            }
            return fillAverage;
        }

        public static double GetHumanAvgTicketPPD()
        {
            List<double> ePrices = (from r in GameObject.GetInstance().HumanCarrier.Routes
                                    where r.Type == Route.RouteType.Passenger || r.Type == Route.RouteType.Mixed
                                    select ((PassengerRoute) r).GetFarePrice(CoachClass.ClassType.EconomyClass)).ToList();
            List<double> bPrices = (from r in GameObject.GetInstance().HumanCarrier.Routes
                                    where r.Type == Route.RouteType.Passenger || r.Type == Route.RouteType.Mixed
                                    select ((PassengerRoute) r).GetFarePrice(CoachClass.ClassType.BusinessClass)).ToList();
            List<double> fPrices = (from r in GameObject.GetInstance().HumanCarrier.Routes
                                    where r.Type == Route.RouteType.Passenger || r.Type == Route.RouteType.Mixed
                                    select ((PassengerRoute) r).GetFarePrice(CoachClass.ClassType.FirstClass)).ToList();
            double distance =
                (from r in GameObject.GetInstance().HumanCarrier.Routes
                 select MathHelpers.GetDistance(r.Destination1, r.Destination2)).DefaultIfEmpty(0).Average();
            double avgHEP = ePrices.DefaultIfEmpty(0).Average();
            double avgHBP = bPrices.DefaultIfEmpty(0).Average();
            double avgFBP = fPrices.DefaultIfEmpty(0).Average();
            double avgHPrice = ((avgHEP*0.7) + (avgHBP*0.2) + (avgFBP*0.1))/3;
            return ((avgHEP*0.7) + (avgHBP*0.2) + (avgFBP*0.1))/distance;
        }

        public static double GetHumanFillAverage()
        {
            List<double> fillDegree =
                (from r in GameObject.GetInstance().HumanCarrier.Routes select r.GetFillingDegree()).ToList();

            return fillDegree.DefaultIfEmpty(0).Average();
        }

        public static double GetHumanOnTime()
        {
            return TripStatistics.OnTimePercent(GameObject.GetInstance().HumanCarrier);
        }

        public static double GetHumanOnTimeYear(int year)
        {
            return GameObject.GetInstance()
                             .HumanCarrier.Statistics.GetStatisticsValue(year, StatisticsTypes.GetStatisticsType("On-Time%"));
        }

        public static double GetOnTimePercent(Carrier carrier)
        {
            return TripStatistics.OnTimePercent(carrier);
        }

        public static Dictionary<Carrier, double> GetPPDdifference()
        {
            var ppdDifference = new Dictionary<Carrier, double>();
            foreach (Carrier carrier in Carriers.GetAllCarriers())
            {
                const double avgPPD = 0; // GetTotalTicketPPD();
                List<double> aiEconPrices = (from r in carrier.Routes
                                             where r.Type == Route.RouteType.Passenger || r.Type == Route.RouteType.Mixed
                                             select ((PassengerRoute) r).GetFarePrice(CoachClass.ClassType.EconomyClass)).ToList();
                List<double> aiBusPrices = (from r in carrier.Routes
                                            where r.Type == Route.RouteType.Passenger || r.Type == Route.RouteType.Mixed
                                            select ((PassengerRoute) r).GetFarePrice(CoachClass.ClassType.BusinessClass)).ToList();
                List<double> aiFirstPrices = (from r in carrier.Routes
                                              where r.Type == Route.RouteType.Passenger || r.Type == Route.RouteType.Mixed
                                              select ((PassengerRoute) r).GetFarePrice(CoachClass.ClassType.FirstClass)).ToList();
                double distance =
                    (from r in carrier.Routes select MathHelpers.GetDistance(r.Destination1, r.Destination2))
                        .DefaultIfEmpty(0).Average();
                double avgEP = aiEconPrices.DefaultIfEmpty(0).Average();
                double avgBP = aiBusPrices.DefaultIfEmpty(0).Average();
                double avgFP = aiFirstPrices.DefaultIfEmpty(0).Average();
                double avgDistPrice = ((avgEP*0.7) + (avgBP*0.2) + (avgFP*0.1))/distance;
                double avgDiff = avgDistPrice - avgPPD;
                ppdDifference.Add(carrier, avgDiff);
            }
            return ppdDifference;
        }

        public static IDictionary<Carrier, double> GetRatingScale(
            IDictionary<Carrier, double> valueDictionary,
            double lower = 1,
            double upper = 100)
        {
            foreach (Carrier carrier in valueDictionary.Keys.ToArray())
            {
                if (double.IsNaN(valueDictionary[carrier]))
                {
                    valueDictionary[carrier] = 0;
                }
            }

            var scaleValues = new Dictionary<Carrier, double>();
            double max = valueDictionary.Values.DefaultIfEmpty(0).Max();
            double min = valueDictionary.Values.DefaultIfEmpty(0).Min();
            double avg = valueDictionary.Values.DefaultIfEmpty(0).Sum()/valueDictionary.Values.Count;

            double gap = max - min;

            if (gap.Equals(0))
            {
                gap = 1;
            }

            return valueDictionary.ToDictionary(v => v.Key, x => (upper - lower)*((x.Value - min)/gap) + lower);
        }

        //returns a dictionary of unassigned drivers

        //just a helper method to get a score for a given year
        public static int GetScore(Carrier carrier, int year)
        {
            return carrier.GameScores.Where(score => score.Key.Year == year).Sum(score => score.Value);
        }

        //helper method to get a score for a given month of a given year
        public static int GetScore(Carrier carrier, int year, int month)
        {
            return carrier.GameScores.Where(score => score.Key.Month == month && score.Key.Year == year).Sum(score => score.Value);
        }

        //helper method to get the score inclusively for a provided date range

        public static int GetScore(Carrier carrier)
        {
            return carrier.GameScores.Sum(score => score.Value);
        }

        public static Dictionary<Carrier, double> GetTotalOnTime()
        {
            var totalOnTime = new Dictionary<Carrier, double>();
            foreach (Carrier carrier in Carriers.GetAllCarriers())
            {
                double otp = TripStatistics.OnTimePercent(carrier);
                totalOnTime.Add(carrier, otp);
            }
            return totalOnTime;
        }

        public static Dictionary<Carrier, double> GetTotalPPD()
        {
            var ticketPPD = new List<double>();
            var uDistPrice = new Dictionary<Carrier, double>();
            foreach (Carrier carrier in Carriers.GetAllCarriers())
            {
                List<double> econPrices = (from r in carrier.Routes
                                           where r.Type == Route.RouteType.Passenger || r.Type == Route.RouteType.Mixed
                                           select ((PassengerRoute) r).GetFarePrice(CoachClass.ClassType.EconomyClass)).ToList();
                List<double> busPrices = (from r in carrier.Routes
                                          where r.Type == Route.RouteType.Passenger || r.Type == Route.RouteType.Mixed
                                          select ((PassengerRoute) r).GetFarePrice(CoachClass.ClassType.BusinessClass)).ToList();
                List<double> firstPrices = (from r in carrier.Routes
                                            where r.Type == Route.RouteType.Passenger || r.Type == Route.RouteType.Mixed
                                            select ((PassengerRoute) r).GetFarePrice(CoachClass.ClassType.FirstClass)).ToList();
                List<double> distance =
                    (from r in carrier.Routes select MathHelpers.GetDistance(r.Destination1, r.Destination2)).ToList();
                double avgDistPrice = ((econPrices.DefaultIfEmpty(0).Average()*0.7)
                                       + (busPrices.DefaultIfEmpty(0).Average()*0.2)
                                       + (firstPrices.DefaultIfEmpty(0).Average()*0.1))
                                      /distance.DefaultIfEmpty(0).Average();
                uDistPrice.Add(carrier, avgDistPrice);
            }
            return uDistPrice;
        }

        public static Dictionary<Carrier, double> GetUnassignedDrivers()
        {
            var unassignedDrivers = new Dictionary<Carrier, double>();
            foreach (Carrier carrier in Carriers.GetAllCarriers())
            {
                double unDrivers = carrier.Drivers.Count(p => p.Coach == null);
                unassignedDrivers.Add(carrier, unDrivers);
            }
            return unassignedDrivers;
        }

        public static int GetWeeklyScore(Carrier carrier)
        {
            int score = 0;

            //some base score weights:
            //fleet: 5 per coach
            // destinations: 2 per destination
            // cash: 1 per $1,000,000
            // happiness: raw value
            // employee happiness: raw value
            // maint, security, safety: raw values

            int fleet = carrier.Fleet.Count();
            int destinations = carrier.Stations.Count();
            int cash = (int) carrier.Money/1000000;
            double happiness = carrier.Ratings.CustomerHappinessRating;
            happiness = Math.Pow(happiness, 2)/2;
            double empHappiness = carrier.Ratings.EmployeeHappinessRating;
            empHappiness = Math.Pow(empHappiness, 2)/4;
            double maint = carrier.Ratings.MaintenanceRating;
            double security = carrier.Ratings.SecurityRating;
            double safety = carrier.Ratings.SafetyRating;

            score +=
                (int) ((fleet*5) + (destinations*2) + cash + happiness + empHappiness + maint + safety + security);
            carrier.CountedScores++;
            return score;
        }

        public static int GetYearScore(Carrier carrier, int startYear, int endYear)
        {
            return carrier.GameScores.Where(score => score.Key.Year >= startYear && score.Key.Year <= endYear).Sum(score => score.Value);
        }

        public static double NextGaussian(this Random r, double mean = 0, double stdv = 1)
        {
            //generate a couple randoms
            double u1 = r.NextDouble();
            double u2 = r.NextDouble();

            //stand dev
            double randStdNormal = Math.Sqrt(-2.0*Math.Log(u1))*Math.Sin(2.0*Math.PI*u2);

            //normalize
            double randNormal = mean + stdv*randStdNormal;

            return randNormal;
        }

        public static Dictionary<Country, double> GetCountryStationsServed()

        {
            var countryStations = new Dictionary<Country, double>();
            foreach (
                Country country in
                    GameObject.GetInstance().HumanCarrier.Stations.Select(a => a.Profile.Country).Distinct())
            {
                double c = Stations.GetStations(country).Count();
                double cHuman = GameObject.GetInstance().HumanCarrier.Stations.Count(a => a.Profile.Country == country);
                countryStations.Add(country, (c/cHuman));
            }
            return countryStations;
        }

        public static double GetWorldStationsServed()
        {
            double hStations = GameObject.GetInstance().HumanCarrier.Stations.Count();
            return hStations/Stations.GetAllStations().Count();
        }

        #endregion

        //generate a 1-100 scale for a list of values

        //returns the carriers best year in terms of score
    }
}