using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;

namespace TheBusProject.Helpers
{
    //the class for some general carrier helpers
    public class CoachHelpers
    {
        #region Static Fields

        // Random.Shared is safe to use from the parallel carrier threads (a shared new Random() is not)
        private static Random Rnd => Random.Shared;

        #endregion

        #region Public Methods and Operators

        //returns a calculated weight for an coach
        public static double GetCalculatedWeight(CoachType type)
        {
            return GetCalculatedWeight(type.Wingspan, type.Length, type.FuelCapacity);
        }

        public static double GetCalculatedWeight(double wingspan, double lenght, long fuel)
        {
            return (wingspan*lenght*4) + fuel;
        }

        public static void CreateCoachClasses(Coach coach)
        {
            coach.ClearCoachClasses();

            List<CoachClass> classes = GetCoachClasses(coach.Type);

            foreach (CoachClass aClass in classes)
            {
                coach.AddCoachClass(aClass);
            }
        }

        public static Coach CreateCoachFromYear(int year)
        {
            Guid id = Guid.NewGuid();

            List<CoachType> types =
                CoachTypes.GetTypes(t => t.Produced.From.Year < year && t.Produced.To.Year > year);

            int typeNumber = Rnd.Next(types.Count);
            CoachType type = types[typeNumber];

            int countryNumber = Rnd.Next(Countries.GetCountries().Count() - 1);
            Country country = Countries.GetCountries()[countryNumber];

            int builtYear = year;

            var coach = new Coach(
                id.ToString(),
                type,
                country.TailNumbers.GetNextTailNumber(),
                new DateTime(builtYear, 1, 1));

            int age = MathHelpers.CalculateAge(coach.BuiltDate, GameObject.GetInstance().GameTime);

            long kmPerYear = Rnd.Next(1000, 100000);
            long km = kmPerYear*age;

            coach.Flown = km;

            // the engines offered when the coach was built (any of the type's, for a year without one)
            List<EngineType> engines = EngineTypes.GetEngineTypes(coach.Type, coach.BuiltDate.Year);
            if (engines.Count == 0) engines = EngineTypes.GetEngineTypes(coach.Type);

            if (engines.Count > 0)
            {
                int engineNumber = Rnd.Next(engines.Count);

                coach.EngineType = engines[engineNumber];
            }


            CreateCoachClasses(coach);

            return coach;
        }

        //how many times the original number of used coaches are for sale when a game starts
        public const int UsedCoachSupply = 3;

        public static void CreateStartUpCoaches()
        {
            int number =
                CoachTypes.GetTypes(
                    t => t.Produced.From <= GameObject.GetInstance().GameTime
                         && t.Produced.To.AddYears(-10) >= GameObject.GetInstance().GameTime.AddYears(-30))
                             .Count*Rnd.Next(1, 3);

            int carriers = Carriers.GetNumberOfCarriers();

            // the original game made carriers * types / 10 used coaches; this makes three times as many
            number = (carriers*number*UsedCoachSupply)/10;
            for (int i = 0; i < number; i++)
            {
                Coaches.AddCoach(CreateCoach(0));
            }

            int coachesForLease = number/20;

            for (int i = 0; i < coachesForLease; i++)
            {
                Coaches.AddCoach(CreateLeasingCoach());
            }
        }

        //returns the delivery date for a order of coaches

        //returns the code for an coach class
        public static string GetCoachClassCode(CoachClass aClass)
        {
            string symbol = "Y";

            if (aClass.Type == CoachClass.ClassType.BusinessClass)
            {
                symbol = "C";
            }

            if (aClass.Type == CoachClass.ClassType.FirstClass)
            {
                symbol = "F";
            }

            if (aClass.Type == CoachClass.ClassType.EconomyClass)
            {
                symbol = "Y";
            }

            return $"{aClass.SeatingCapacity}{symbol}";
        }

        public static List<CoachClass> GetCoachClasses(CoachType type)
        {
            var classes = new List<CoachClass>();

            var passengerType = type as CoachPassengerType;
            if (passengerType != null)
            {
                Configuration coachTypeConfiguration =
                    Configurations.GetConfigurations(Configuration.ConfigurationType.CoachType)
                                  .Find(
                                      c =>
                                      ((CoachTypeConfiguration) c).Coach == type
                                      && ((CoachTypeConfiguration) c).Period.From <= GameObject.GetInstance().GameTime
                                      && ((CoachTypeConfiguration) c).Period.To > GameObject.GetInstance().GameTime);

                if (coachTypeConfiguration == null)
                {
                    int seatingDiff = 0;
                    CoachConfiguration configuration = null;

                    int numOfClasses = Rnd.Next(0, passengerType.MaxCoachClasses) + 1;

                    // the standard configurations only cover 1 to 3 classes
                    numOfClasses = Math.Min(numOfClasses, 3);

                    if (GameObject.GetInstance().GameTime.Year >= (int) CoachClass.ClassType.BusinessClass)
                    {
                        if (numOfClasses == 1)
                        {
                            configuration = (CoachConfiguration) Configurations.GetStandardConfiguration("200");
                        }
                        if (numOfClasses == 2)
                        {
                            configuration = (CoachConfiguration) Configurations.GetStandardConfiguration("202");
                        }
                        if (numOfClasses == 3)
                        {
                            configuration = (CoachConfiguration) Configurations.GetStandardConfiguration("203");
                        }
                    }
                    else
                    {
                        if (numOfClasses == 1)
                        {
                            configuration = (CoachConfiguration) Configurations.GetStandardConfiguration("200");
                        }
                        if (numOfClasses == 2)
                        {
                            configuration = (CoachConfiguration) Configurations.GetStandardConfiguration("201");
                        }
                        if (numOfClasses == 3)
                        {
                            configuration = (CoachConfiguration) Configurations.GetStandardConfiguration("201");
                        }
                    }

                    if (configuration == null)
                    {
                        Infrastructure.StartupLog.Write("No standard configuration found for " + numOfClasses + " classes in " + GameObject.GetInstance().GameTime.Year + ", using the 1 class layout");
                        configuration = (CoachConfiguration) Configurations.GetStandardConfiguration("200");
                    }

                    if (configuration != null)
                    {
                        foreach (CoachClassConfiguration aClass in configuration.Classes)
                        {
                            var coachClass = new CoachClass(aClass.Type, aClass.SeatingCapacity) {RegularSeatingCapacity = aClass.RegularSeatingCapacity};

                            foreach (CoachFacility facility in aClass.GetFacilities())
                            {
                                coachClass.SetFacility(null, facility);
                            }

                            foreach (
                                CoachFacility.FacilityType fType in Enum.GetValues(typeof (CoachFacility.FacilityType))
                                )
                            {
                                if (!aClass.Facilities.Exists(f => f.Type == fType))
                                {
                                    coachClass.SetFacility(null, CoachFacilities.GetBasicFacility(fType));
                                }
                            }

                            coachClass.SeatingCapacity =
                                Convert.ToInt16(
                                    Convert.ToDouble(coachClass.RegularSeatingCapacity)
                                    /coachClass.GetFacility(CoachFacility.FacilityType.Seat).SeatUses);

                            classes.Add(coachClass);
                        }

                        seatingDiff = passengerType.MaxSeatingCapacity - configuration.MinimumSeats;
                    }

                    CoachClass economyClass = classes.Find(c => c.Type == CoachClass.ClassType.EconomyClass);
                    economyClass.RegularSeatingCapacity += seatingDiff;

                    CoachFacility seatingFacility = economyClass.GetFacility(CoachFacility.FacilityType.Seat);

                    var extraSeats = (int) (seatingDiff/seatingFacility.SeatUses);

                    economyClass.SeatingCapacity += extraSeats;
                }
                else
                {
                    foreach (
                        CoachClassConfiguration aClass in
                            ((CoachTypeConfiguration) coachTypeConfiguration).Classes)
                    {
                        var coachClass = new CoachClass(aClass.Type, aClass.SeatingCapacity) {RegularSeatingCapacity = aClass.RegularSeatingCapacity};

                        foreach (CoachFacility facility in aClass.GetFacilities())
                        {
                            coachClass.SetFacility(null, facility);
                        }

                        coachClass.SeatingCapacity =
                            Convert.ToInt16(
                                Convert.ToDouble(coachClass.RegularSeatingCapacity)
                                /coachClass.GetFacility(CoachFacility.FacilityType.Seat).SeatUses);

                        classes.Add(coachClass);
                    }
                }
            }
            else if (type is CoachCargoType)
            {
                var cargoClass = new CoachClass(CoachClass.ClassType.EconomyClass, 0);
                classes.Add(cargoClass);
            }
            else if (type.TypeCoach == CoachType.TypeOfCoach.Helicopter)
            {
                var helicopterClass = new CoachClass(CoachClass.ClassType.EconomyClass, 0);
                classes.Add(helicopterClass);
            }
            return classes;
        }

        //returns the cargo size for a passenger coach if needed to converted

        //return the days for converting a passenger coach to a cargo coach
        public static int GetCargoConvertingDays(CoachPassengerType type)
        {
            return (int) (Convert.ToDouble(type.MaxSeatingCapacity)/1.15);
        }

        //returns the price for converting a passenger coach to a cargo coach
        public static double GetCargoConvertingPrice(CoachPassengerType type)
        {
            double basePrice = 650000;

            if (type.Body == CoachType.BodyType.SingleAisle)
            {
                basePrice = basePrice*1.2;
            }

            if (type.Body == CoachType.BodyType.NarrowBody)
            {
                basePrice = basePrice*2.4;
            }

            if (type.Body == CoachType.BodyType.WideBody)
            {
                basePrice = basePrice*3.6;
            }

            double paxRate = type.MaxSeatingCapacity*800;

            return GeneralHelpers.GetInflationPrice(basePrice + paxRate);
        }

        public static DateTime GetOrderDeliveryDate(List<CoachOrder> orders)
        {
            double monthsToComplete = orders.Select(order => Math.Ceiling(Convert.ToDouble(order.Amount)/order.Type.ProductionRate)).Concat(new double[] {0}).Max();

            var latestDate = new DateTime(1900, 1, 1);

            foreach (CoachOrder order in orders)
            {
                var date = new DateTime(
                    GameObject.GetInstance().GameTime.Year,
                    GameObject.GetInstance().GameTime.Month,
                    GameObject.GetInstance().GameTime.Day);
                int rate = order.Type.ProductionRate;
                if (order.Amount <= (rate/4))
                {
                    date = date.AddMonths(3);
                }
                else
                {
                    for (int i = (rate/4) + 1; i <= order.Amount; i++)
                    {
                        date = date.AddDays(365.0/rate);
                    }
                }

                if (date > latestDate)
                {
                    latestDate = date;
                }
            }

            return latestDate;
        }

        public static double GetPassengerCargoSize(CoachPassengerType type)
        {
            return Convert.ToDouble(type.MaxSeatingCapacity)*1.25;
        }

        public static FleetCoach GetRandomCoach(Carrier carrier)
        {
            return carrier.Fleet[Rnd.Next(carrier.Fleet.Count)];
        }

        #endregion

        #region Methods

        private static Coach CreateLeasingCoach()
        {
            int years = Rnd.Next(0, 4);

            Coach coach = CreateCoachFromYear(GameObject.GetInstance().GameTime.AddYears(-years).Year);

            coach.Status = Coach.StatusTypes.Leasing;

            return coach;
        }

        private static Coach CreateCoach(double minRange)
        {
            Guid id = Guid.NewGuid();

            List<CoachType> types =
                CoachTypes.GetTypes(
                    t => t.Range >= minRange && t.Produced.From.Year < GameObject.GetInstance().GameTime.AddYears(-5).Year
                         && t.Produced.To > GameObject.GetInstance().GameTime.AddYears(-35));

            int typeNumber = Rnd.Next(types.Count);
            CoachType type = types[typeNumber];

            int countryNumber = Rnd.Next(Countries.GetCountries().Count() - 1);
            Country country = Countries.GetCountries()[countryNumber];

            int builtYear = Rnd.Next(
                Math.Max(type.Produced.From.Year, GameObject.GetInstance().GameTime.Year - 35),
                Math.Min(GameObject.GetInstance().GameTime.Year - 5, type.Produced.To.Year));

            var coach = new Coach(
                id.ToString(),
                type,
                country.TailNumbers.GetNextTailNumber(),
                new DateTime(builtYear, 1, 1));

            if (coach.TailNumber.Length < 2)
            {
                typeNumber = 0;
            }

            int age = MathHelpers.CalculateAge(coach.BuiltDate, GameObject.GetInstance().GameTime);

            long kmPerYear = Rnd.Next(100000, 1000000);
            long km = kmPerYear*age;

            coach.Flown = km;

            CreateCoachClasses(coach);

            // the engines offered when the coach was built (any of the type's, for a year without one)
            List<EngineType> engines = EngineTypes.GetEngineTypes(coach.Type, coach.BuiltDate.Year);
            if (engines.Count == 0) engines = EngineTypes.GetEngineTypes(coach.Type);

            if (engines.Count > 0)
            {
                int engineNumber = Rnd.Next(engines.Count);

                coach.EngineType = engines[engineNumber];
            }

            return coach;
        }

        #endregion

        /*! create a random coach with a minimum range.
        */

        //creates the coach classes for an coach
    }
}