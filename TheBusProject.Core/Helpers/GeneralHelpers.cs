using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries.Towns;
using TheBusProject.Models.General.Environment;
using TheBusProject.Models.General.Holidays;
using TheBusProject.Models.Pilots;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    //class for some general helpers
    public class GeneralHelpers
    {
        #region Static Fields

        public static string BigMapXaml;

        #endregion

        #region Enums

        public enum GameSpeedValue
        {
            Slowest = 2500,

            Slow = 2000,

            Normal = 1500,

            Fast = 1000,

            Fastest = 500,

            //runs as fast as the computer can simulate the turns
            Turbo = 250
        }

        public enum Size
        {
            Smallest,

            VerySmall,

            Small,

            Medium,

            Large,

            VeryLarge,

            Largest
        }

        #endregion

        #region Public Methods and Operators

        public static int ClassToPriceFactor(CoachClass.ClassType type)
        {
            if (type == CoachClass.ClassType.BusinessClass)
            {
                return 3;
            }
            if (type == CoachClass.ClassType.EconomyClass)
            {
                return 1;
            }
            if (type == CoachClass.ClassType.FirstClass)
            {
                return 6;
            }
            return 1;
        }

        public static CoachType.TypeRange ConvertDistanceToRangeType(double distance)
        {
            if (distance < 2000)
            {
                return CoachType.TypeRange.Regional;
            }
            if (distance >= 2000 && distance < 3500)
            {
                return CoachType.TypeRange.ShortRange;
            }
            if (distance >= 3500 && distance < 9000)
            {
                return CoachType.TypeRange.MediumRange;
            }
            if (distance >= 9000)
            {
                return CoachType.TypeRange.LongRange;
            }

            return CoachType.TypeRange.MediumRange;
        }

        public static void CreateHolidays(int startYear)
        {
            HolidayYear.Clear();

            int endYear = startYear + 5;

            for (int i = startYear; i < endYear; i++)
            {
                foreach (Holiday holiday in Holidays.GetHolidays())
                {
                    if (holiday.Type == Holiday.HolidayType.FixedDate)
                    {
                        HolidayYear.AddHoliday(
                            new HolidayYearEvent(
                                new DateTime(startYear, holiday.Date.Month, holiday.Date.Day),
                                holiday,
                                1));
                    }
                    if (holiday.Type == Holiday.HolidayType.FixedMonth)
                    {
                        var eHoliday = new HolidayYearEvent(
                            new DateTime(startYear, holiday.Month, 1),
                            holiday,
                            DateTime.DaysInMonth(startYear, holiday.Month));
                        HolidayYear.AddHoliday(eHoliday);
                    }
                    if (holiday.Type == Holiday.HolidayType.FixedWeek)
                    {
                        HolidayYear.AddHoliday(
                            new HolidayYearEvent(MathHelpers.GetFirstDateOfWeek(startYear, holiday.Week), holiday, 7));
                    }
                    if (holiday.Type == Holiday.HolidayType.NonFixedDate)
                    {
                        HolidayYear.AddHoliday(
                            new HolidayYearEvent(
                                MathHelpers.GetNthWeekdayOfMonth(startYear, holiday.Month, holiday.Day, holiday.Week),
                                holiday,
                                1));
                    }
                }
            }
        }

        public static void CreateInstructors(int count)
        {
            List<Town> towns = Towns.GetTowns();

            var rnd = new Random();
            for (int i = 0; i < count; i++)
            {
                Town town = towns[rnd.Next(towns.Count)];
                DateTime birthdate =
                    MathHelpers.GetRandomDate(
                        GameObject.GetInstance().GameTime.AddYears(-Pilot.RetirementAge),
                        GameObject.GetInstance().GameTime.AddYears(-23));
                var profile = new PilotProfile(
                    Names.GetInstance().GetRandomFirstName(town.Country),
                    Names.GetInstance().GetRandomLastName(town.Country),
                    birthdate,
                    town);

                var rankings = new Dictionary<PilotRating, int>
                    {
                        {PilotRatings.GetRating("A"), 10},
                        {PilotRatings.GetRating("B"), 20},
                        {PilotRatings.GetRating("C"), 40},
                        {PilotRatings.GetRating("D"), 20},
                        {PilotRatings.GetRating("E"), 10}
                    };

                PilotRating ranking = AIHelpers.GetRandomItem(rankings);

                var instructor = new Instructor(profile, ranking);

                Instructors.AddInstructor(instructor);
            }
        }

        public static void CreatePilots(int count, string coachFamily)
        {
            List<Town> towns = Towns.GetTowns();

            var rnd = new Random();
            for (int i = 0; i < count; i++)
            {
                Town town = towns[rnd.Next(towns.Count)];
                DateTime birthdate =
                    MathHelpers.GetRandomDate(
                        GameObject.GetInstance().GameTime.AddYears(-Pilot.RetirementAge),
                        GameObject.GetInstance().GameTime.AddYears(-23));

                var profile = new PilotProfile(
                    Names.GetInstance().GetRandomFirstName(town.Country),
                    Names.GetInstance().GetRandomLastName(town.Country),
                    birthdate,
                    town);

                PilotRating rating = GetPilotRating();

                int fromYear = Math.Min(GameObject.GetInstance().GameTime.Year - 1, birthdate.AddYears(23).Year);
                int toYear = Math.Min(
                    GameObject.GetInstance().GameTime.Year,
                    birthdate.AddYears(Pilot.RetirementAge).Year);

                DateTime educationTime = MathHelpers.GetRandomDate(birthdate.AddYears(23), new DateTime(toYear, 1, 1));
                var pilot = new Pilot(profile, educationTime, rating);

                pilot.Coaches = GetPilotCoaches(pilot);
                pilot.Coaches.RemoveAt(0);
                pilot.Coaches.Add(coachFamily);

                Pilots.AddPilot(pilot);
            }
        }

        //creates a number of pilots
        public static void CreatePilots(int count)
        {
            List<Town> towns = Towns.GetTowns();

            var rnd = new Random();
            for (int i = 0; i < count; i++)
            {
                Town town = towns[rnd.Next(towns.Count)];
                DateTime birthdate =
                    MathHelpers.GetRandomDate(
                        GameObject.GetInstance().GameTime.AddYears(-Pilot.RetirementAge),
                        GameObject.GetInstance().GameTime.AddYears(-23));

                var profile = new PilotProfile(
                    Names.GetInstance().GetRandomFirstName(town.Country),
                    Names.GetInstance().GetRandomLastName(town.Country),
                    birthdate,
                    town);

                PilotRating rating = GetPilotRating();

                int fromYear = Math.Min(GameObject.GetInstance().GameTime.Year - 1, birthdate.AddYears(23).Year);
                int toYear = Math.Min(
                    GameObject.GetInstance().GameTime.Year,
                    birthdate.AddYears(Pilot.RetirementAge).Year);

                DateTime educationTime = MathHelpers.GetRandomDate(birthdate.AddYears(23), new DateTime(toYear, 1, 1));
                var pilot = new Pilot(profile, educationTime, rating);

                pilot.Coaches = GetPilotCoaches(pilot);

                Pilots.AddPilot(pilot);
            }
        }

        public static double GetCarrierLoanRate(Carrier carrier)
        {
            var value = (double) carrier.GetCarrierValue();

            // yearly interest in percent: a base rate plus a risk premium that falls as the carrier gets stronger
            return 2 + (GameObject.GetInstance().Difficulty.LoanLevel + 0.5)
                   *((double) Carrier.CarrierValue.VeryHigh + 1 - value);
        }

        public static double GetCoachOrderDiscount(int orders)
        {
            if (orders > 2)
            {
                return ((orders - 2)*0.5)/100;
            }
            return 0;
        }

        // public enum Rate { None }

        //returns the list of arrivals for an station
        public static List<RouteTimeTableEntry> GetStationArrivals(Station station, DayOfWeek day)
        {
            var entries = new List<RouteTimeTableEntry>();
            foreach (Route route in StationHelpers.GetStationRoutes(station))
            {
                if (route.HasCoach)
                {
                    IEnumerable<RouteTimeTableEntry> rEntries =
                        route.TimeTable.Entries.Where(e => e.Day == day && e.Destination.Station == station);

                    entries.AddRange(rEntries);
                }
            }
            entries.Sort(
                (e1, e2) => MathHelpers.ConvertEntryToDate(e1).CompareTo(MathHelpers.ConvertEntryToDate(e2)));
            return entries;
        }

        public static List<RouteTimeTableEntry> GetStationArrivals(Station station, int count)
        {
            var entries = new List<RouteTimeTableEntry>();
            foreach (Route route in StationHelpers.GetStationRoutes(station))
            {
                if (route.HasCoach && route.GetCurrentCoach() != null)
                {
                    RouteTimeTableEntry entry = route.GetCurrentCoach().CurrentTrip == null
                                                    ? route.TimeTable.GetNextEntry(
                                                        GameObject.GetInstance().GameTime,
                                                        (station == route.Destination1 ? route.Destination2 : route.Destination1))
                                                    : route.GetCurrentCoach().CurrentTrip.Entry;

                    for (int i = 0; i < route.TimeTable.Entries.Count; i++)
                    {
                        if (entry.Destination.Station == station)
                        {
                            entries.Add(entry);
                        }
                        entry = route.TimeTable.GetNextEntry(entry);
                    }
                }
            }
            entries.Sort(
                (e1, e2) => MathHelpers.ConvertEntryToDate(e1).CompareTo(MathHelpers.ConvertEntryToDate(e2)));
            return entries.GetRange(0, Math.Min(entries.Count, count));
        }

        //returns the list of departures for an station
        public static List<RouteTimeTableEntry> GetStationDepartures(Station station, DayOfWeek day)
        {
            var entries = new List<RouteTimeTableEntry>();
            foreach (Route route in StationHelpers.GetStationRoutes(station))
            {
                if (route.HasCoach)
                {
                    IEnumerable<RouteTimeTableEntry> rEntries =
                        route.TimeTable.Entries.Where(e => e.Day == day && e.DepartureStation == station);

                    entries.AddRange(rEntries);
                }
            }
            entries.Sort(
                (e1, e2) => MathHelpers.ConvertEntryToDate(e1).CompareTo(MathHelpers.ConvertEntryToDate(e2)));
            return entries;
        }

        public static List<RouteTimeTableEntry> GetStationDepartures(Station station, int count)
        {
            var entries = new List<RouteTimeTableEntry>();
            foreach (Route route in StationHelpers.GetStationRoutes(station))
            {
                if (route.HasCoach && route.GetCurrentCoach() != null)
                {
                    RouteTimeTableEntry entry = route.GetCurrentCoach().CurrentTrip == null
                                                    ? route.TimeTable.GetNextEntry(GameObject.GetInstance().GameTime, station)
                                                    : route.GetCurrentCoach().CurrentTrip.Entry;

                    if (!entry.Destination.Station.Profile.Coordinates.Equals(station.Profile.Coordinates))
                    {
                        entries.Add(entry);
                    }

                    while (entries.Count < 4)
                    {
                        entry = route.TimeTable.GetNextEntry(entry);
                        if (entry.Destination.Station != station)
                        {
                            entries.Add(entry);
                        }
                    }
                }
            }
            entries.Sort(
                (e1, e2) => MathHelpers.ConvertEntryToDate(e1).CompareTo(MathHelpers.ConvertEntryToDate(e2)));
            return entries.GetRange(0, Math.Min(entries.Count, count));
        }

        public static List<RouteTimeTableEntry> GetStationTrips(Station fStation, Station tStation, Boolean arrivals)
        {
            var entries = new List<RouteTimeTableEntry>();
            foreach (Route route in StationHelpers.GetStationRoutes(fStation))
            {
                if (route.HasCoach && (route.Destination1 == tStation || route.Destination2 == tStation))
                {
                    RouteTimeTableEntry entry = route.GetCurrentCoach() == null
                                                || route.GetCurrentCoach().CurrentTrip == null
                                                || route.GetCurrentCoach().CurrentTrip.Entry == null
                                                    ? route.TimeTable.GetNextEntry(GameObject.GetInstance().GameTime)
                                                    : route.GetCurrentCoach().CurrentTrip.Entry;

                    for (int i = 0; i < route.TimeTable.Entries.Count; i++)
                    {
                        if (!arrivals && entry.Destination.Station == tStation)
                        {
                            entries.Add(entry);
                        }

                        if (arrivals && entry.Destination.Station == fStation)
                        {
                            entries.Add(entry);
                        }

                        entry = route.TimeTable.GetNextEntry(entry);
                    }
                }
            }
            entries.Sort(
                (e1, e2) => MathHelpers.ConvertEntryToDate(e1).CompareTo(MathHelpers.ConvertEntryToDate(e2)));
            return entries;
        }

        //returns the rate (for loan) for an carrier

        //the converter for a price based on inflation
        public static double GetInflationPrice(double price)
        {
            Inflation baseInflation = Inflations.GetInflation(Inflations.BaseYear);
            Inflation currentInflation = Inflations.GetInflation(GameObject.GetInstance().GameTime.Year);

            double modifier = currentInflation.Modifier/baseInflation.Modifier;

            double newPrice = price*modifier;

            return newPrice;
        }

        public static List<string> GetPilotCoaches(Pilot pilot)
        {
            int year = GameObject.GetInstance().GameTime.Year;

            IEnumerable<string> coachFamilies =
                CoachTypes.GetTypes(
                    t =>
                    t.Produced.From.Year <= GameObject.GetInstance().GameTime.Year
                    && t.Produced.To > GameObject.GetInstance().GameTime.AddYears(-30))
                             .Select(a => a.CoachFamily)
                             .Distinct();

            var rnd = new Random();
            var families = new List<string>();

            int numberOfCoaches = PilotRatings.GetRatings().IndexOf(pilot.Rating) + 1;

            for (int i = 0; i < numberOfCoaches; i++)
            {
                var enumerable = coachFamilies as string[] ?? coachFamilies.ToArray();
                List<string> freeFamilies = enumerable.Where(a => !families.Contains(a)).ToList();
                string family = freeFamilies[rnd.Next(freeFamilies.Count)];

                families.Add(family);
            }

            return families;
        }

        //returns a rating for a pilot
        public static PilotRating GetPilotRating()
        {
            var ratings = new Dictionary<PilotRating, int>
                {
                    {PilotRatings.GetRating("A"), 10},
                    {PilotRatings.GetRating("B"), 20},
                    {PilotRatings.GetRating("C"), 40},
                    {PilotRatings.GetRating("D"), 20},
                    {PilotRatings.GetRating("E"), 10}
                };

            return AIHelpers.GetRandomItem(ratings);
        }

        //creates the rating for a pilot student
        public static PilotRating GetPilotStudentRating(Instructor instructor, List<PilotRating> ratings)
        {
            PilotRating instructorRanking = instructor.Rating;
            int coachCoeff = instructor.TripSchool.TrainingCoaches.Exists(a => a.Type.MaxNumberOfStudents > 5)
                                    ? 10
                                    : 0;

            int instructorRankingIndex = PilotRatings.GetRatings().IndexOf(instructorRanking);
            var rankings = new Dictionary<PilotRating, int>();

            if (ratings.Contains(instructorRanking))
            {
                rankings.Add(instructorRanking, 50);
            }

            if (instructorRankingIndex > 0)
            {
                PilotRating prevRating = PilotRatings.GetRatings()[instructorRankingIndex - 1];

                if (ratings.Contains(prevRating))
                {
                    rankings.Add(prevRating, 35 - coachCoeff);
                }
            }
            if (instructorRankingIndex < PilotRatings.GetRatings().Count - 1)
            {
                PilotRating nextRating = PilotRatings.GetRatings()[instructorRankingIndex + 1];

                if (ratings.Contains(nextRating))
                {
                    rankings.Add(nextRating, 15 + coachCoeff);
                }
            }

            if (rankings.Count == 0)
            {
                ratings.ForEach(r => rankings.Add(r, 20 - r.CostIndex));
            }

            if (rankings.Count == 0)
            {
                return GetPilotRating();
            }

            PilotRating rating = AIHelpers.GetRandomItem(rankings);

            return rating;
        }

        //creates the pilot rating for a pilot student
        public static PilotRating GetPilotStudentRating(Instructor instructor)
        {
            PilotRating instructorRanking = instructor.Rating;
            int coachCoeff = instructor.TripSchool.TrainingCoaches.Exists(a => a.Type.MaxNumberOfStudents > 5)
                                    ? 10
                                    : 0;

            int instructorRankingIndex = PilotRatings.GetRatings().IndexOf(instructorRanking);
            var rankings = new Dictionary<PilotRating, int> {{instructorRanking, 50}};

            if (instructorRankingIndex > 0)
            {
                PilotRating prevRating = PilotRatings.GetRatings()[instructorRankingIndex - 1];
                rankings.Add(prevRating, 35 - coachCoeff);
            }
            if (instructorRankingIndex < PilotRatings.GetRatings().Count)
            {
                PilotRating nextRating = PilotRatings.GetRatings()[instructorRankingIndex + 1];

                rankings.Add(nextRating, 15 + coachCoeff);
            }

            PilotRating rating = AIHelpers.GetRandomItem(rankings);

            return rating;
        }

        //creates the pilot rating for a pilot student
        public static PilotRating GetPilotStudentRating(PilotStudent student)
        {
            return GetPilotStudentRating(student.Instructor);
        }

        public static Weather.Season GetSeason(DateTime date)
        {
            var summertimeStart = new DateTime(date.Year, 4, 1);
            var summertimeEnd = new DateTime(date.Year, 10, 1);

            if (date >= summertimeStart && date < summertimeEnd)
            {
                return Weather.Season.Summer;
            }
            return Weather.Season.Winter;
        }

        public static Boolean IsStationActive(Station station)
        {
            return station.Profile.Period.From <= GameObject.GetInstance().GameTime
                   && station.Profile.Period.To > GameObject.GetInstance().GameTime && Stations.Contains(station);
        }

        #endregion

        //creates a number of pilots with a specific coach
    }

    public static class EnumHelper
    {
        #region Public Methods and Operators

        /// <summary>
        ///     Gets an attribute on an enum field value
        /// </summary>
        /// <typeparam name="T">The type of the attribute you want to retrieve</typeparam>
        /// <param name="enumVal">The enum value</param>
        /// <returns>The attribute of type T that exists on the enum value</returns>
        public static T GetAttributeOfType<T>(this Enum enumVal) where T : Attribute
        {
            Type type = enumVal.GetType();
            MemberInfo[] memInfo = type.GetMember(enumVal.ToString());
            object[] attributes = memInfo[0].GetCustomAttributes(typeof (T), false);
            return (T) attributes[0];
        }

        #endregion
    }

    //the class for a key value pair for use on convertres etc.
    public class GameKeyValuePair<T, TS>
    {
        #region Constructors and Destructors

        public GameKeyValuePair(T key, TS value)
        {
            Key = key;
            Value = value;
        }

        #endregion

        #region Public Properties

        public T Key { get; set; }

        public TS Value { get; set; }

        #endregion
    }
}