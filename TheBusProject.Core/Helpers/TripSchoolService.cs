using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;
using TheBusProject.Models.General.Countries.Towns;
using TheBusProject.Models.General.Finances;
using TheBusProject.Models.Pilots;

namespace TheBusProject.Helpers
{
    /// <summary>Keeps the world supplied with pilots and instructors, so there is always somebody to hire.</summary>
    public static class PilotSupply
    {
        /// <summary>Pilots for hire that can fly one coach family, at least.</summary>
        public const int MinPerFamily = 40;

        public const int MinPilots = 120;
        public const int MinInstructors = 40;

        public static int CountFor(string family)
        {
            return Pilots.GetPilots().Count(p => p.Carrier == null && p.Coaches != null && p.Coaches.Contains(family));
        }

        /// <summary>Makes sure there are enough pilots for hire who can fly the family. Returns how many were created.</summary>
        public static int EnsureFamily(string family, int minimum = MinPerFamily)
        {
            if (string.IsNullOrEmpty(family)) return 0;
            int have = CountFor(family);
            if (have >= minimum) return 0;
            GeneralHelpers.CreatePilots(minimum - have, family);
            return minimum - have;
        }

        public static void EnsureAnyPilots(int minimum = MinPilots)
        {
            int have = Pilots.GetPilots().Count(p => p.Carrier == null);
            if (have < minimum)
            {
                GeneralHelpers.CreatePilots(minimum - have);
            }
        }

        public static void EnsureInstructors(int minimum = MinInstructors)
        {
            int have = Instructors.GetUnassignedInstructors().Count;
            if (have < minimum)
            {
                GeneralHelpers.CreateInstructors(minimum - have);
            }
        }

        /// <summary>Monthly top up for the player: pilots for every family the fleet flies, and instructors.</summary>
        public static void TopUp(Carrier carrier)
        {
            EnsureAnyPilots();
            EnsureInstructors();
            if (carrier == null) return;
            foreach (string family in carrier.Fleet.Select(f => f.Coach.Type.CoachFamily).Distinct().ToList())
            {
                EnsureFamily(family);
            }
        }
    }

    /// <summary>Trip schools: building them, instructors, training coach and students.</summary>
    public static class TripSchoolService
    {
        // Random.Shared is safe to use from the parallel carrier threads (a shared new Random() is not)
        private static Random Rnd => Random.Shared;

        public static double BuildPrice => GeneralHelpers.GetInflationPrice(267050);
        public static double StudentPrice => GeneralHelpers.GetInflationPrice(PilotStudent.StudentCost);

        private static string Money(double v)
        {
            return v.ToString("C0", CultureInfo.GetCultureInfo("en-US"));
        }

        private static GameObject Game => GameObject.GetInstance();

        /// <summary>Stations where a school can be built: hubs and stations with a service facility, one school each.</summary>
        public static List<Station> PossibleStations(Carrier carrier)
        {
            var list = carrier.Stations.Where(a => a.GetCurrentStationFacility(carrier, StationFacility.FacilityType.Service).TypeLevel > 0).ToList();
            list.AddRange(carrier.Stations.Where(a => a.IsHub));
            return list.Distinct().Where(a => carrier.TripSchools.All(f => f.Station != a)).OrderBy(a => a.Profile.Town.Name).ToList();
        }

        public static bool Build(Carrier carrier, Station station, out string message)
        {
            if (station == null || !PossibleStations(carrier).Contains(station))
            {
                message = "A trip school can only be built at one of your hubs or at a station with a service facility, and one school per station.";
                return false;
            }
            double price = BuildPrice;
            if (carrier.Money < price)
            {
                message = "Not enough money: a trip school costs " + Money(price) + ".";
                return false;
            }
            var school = new TripSchool(station);
            carrier.AddTripSchool(school);
            CarrierHelpers.AddCarrierInvoice(carrier, Game.GameTime, Invoice.InvoiceType.CarrierExpenses, -price);
            message = school.Name + " is built for " + Money(price) + ". Hire instructors and buy a training coach before taking in students.";
            return true;
        }

        /// <summary>Closes a school: the instructors go back to the market, the training coach are sold for 75%, students are sent home.</summary>
        public static void Close(Carrier carrier, TripSchool school, out string message)
        {
            double refund = 0;
            foreach (var a in school.TrainingCoaches.ToList())
            {
                refund += a.Type.Price*0.75;
            }
            if (refund > 0)
            {
                CarrierHelpers.AddCarrierInvoice(carrier, Game.GameTime, Invoice.InvoiceType.CarrierExpenses, refund);
            }
            int students = school.Students.Count;
            foreach (var i in school.Instructors)
            {
                i.Students.Clear();
            }
            school.Students.Clear();
            school.TrainingCoaches.Clear();
            carrier.RemoveTripSchool(school);
            message = school.Name + " is closed. " + (refund > 0 ? "The training coach were sold for " + Money(refund) + ". " : "")
                      + (students > 0 ? students + " student(s) were sent home." : "");
        }

        public static int Seats(TripSchool school)
        {
            return school.TrainingCoaches.Sum(a => a.Type.MaxNumberOfStudents);
        }

        /// <summary>How many students the school can take in all: two per instructor, limited by the seats in the training coach.</summary>
        public static int Capacity(TripSchool school)
        {
            return Math.Min(school.Instructors.Count*TripSchool.MaxNumberOfStudentsPerInstructor, Seats(school));
        }

        public static int FreeSeats(TripSchool school, TrainingCoach coach)
        {
            return coach.Type.MaxNumberOfStudents - school.Students.Count(s => s.Coach == coach);
        }

        public static List<Instructor> InstructorsWithRoom(TripSchool school)
        {
            return school.Instructors.Where(i => i.Students.Count < TripSchool.MaxNumberOfStudentsPerInstructor).ToList();
        }

        /// <summary>The ratings a new student can get: those who can train in an coach with a free seat.</summary>
        public static List<PilotRating> PossibleRatings(TripSchool school)
        {
            return PilotRatings.GetRatings()
                               .Where(r => r.Coaches.Any(t => school.TrainingCoaches.Any(a => a.Type == t && FreeSeats(school, a) > 0)))
                               .ToList();
        }

        /// <summary>Why no student can be taken in now, or null.</summary>
        public static string GetStudentBlocker(Carrier carrier, TripSchool school)
        {
            if (school.Instructors.Count == 0) return "The school has no instructors yet.";
            if (school.TrainingCoaches.Count == 0) return "The school has no training coach yet.";
            if (InstructorsWithRoom(school).Count == 0) return "All instructors have their " + TripSchool.MaxNumberOfStudentsPerInstructor + " students. Hire another instructor.";
            if (PossibleRatings(school).Count == 0) return "All the seats in the training coach are taken. Buy another training coach.";
            if (carrier.Money < StudentPrice) return "Not enough money: a student costs " + Money(StudentPrice) + ".";
            return null;
        }

        /// <summary>The coach families a student can be trained for: what is flying now, and what flew in the last 30 years.</summary>
        public static List<string> Families()
        {
            return CoachTypes.GetTypes(t => t.Produced.From.Year <= Game.GameTime.Year && t.Produced.To > Game.GameTime.AddYears(-30))
                                .Select(t => t.CoachFamily).Distinct().OrderBy(f => f).ToList();
        }

        public static bool StartStudent(Carrier carrier, TripSchool school, Instructor instructor, string family, out string message)
        {
            string blocker = GetStudentBlocker(carrier, school);
            if (blocker != null)
            {
                message = blocker;
                return false;
            }
            if (instructor == null || !InstructorsWithRoom(school).Contains(instructor))
            {
                message = "Choose an instructor who has room for another student.";
                return false;
            }
            if (string.IsNullOrEmpty(family))
            {
                message = "Choose the coach family the student is trained for.";
                return false;
            }

            var possible = PossibleRatings(school);
            var rating = GeneralHelpers.GetPilotStudentRating(instructor, possible);
            if (!possible.Contains(rating)) rating = possible[Rnd.Next(possible.Count)];

            var free = school.TrainingCoaches.Where(a => FreeSeats(school, a) > 0).ToList();
            var coach = free.Where(a => rating.Coaches.Contains(a.Type)).OrderBy(a => a.Type.TypeLevel).FirstOrDefault() ?? free.OrderBy(a => a.Type.TypeLevel).First();

            List<Town> towns = Towns.GetTowns(school.Station.Profile.Country);
            if (towns == null || towns.Count == 0) towns = Towns.GetTowns();
            Town town = towns[Rnd.Next(towns.Count)];
            DateTime birthdate = MathHelpers.GetRandomDate(Game.GameTime.AddYears(-35), Game.GameTime.AddYears(-23));
            var profile = new PilotProfile(Names.GetInstance().GetRandomFirstName(town.Country), Names.GetInstance().GetRandomLastName(town.Country), birthdate, town);

            var student = new PilotStudent(profile, Game.GameTime, instructor, rating, family) {Coach = coach};
            school.AddStudent(student);
            instructor.AddStudent(student);
            double price = StudentPrice;
            CarrierHelpers.AddCarrierInvoice(carrier, Game.GameTime, Invoice.InvoiceType.CarrierExpenses, -price);

            message = profile.Name + " starts training for the " + family + " with " + instructor.Profile.Name + " (rating " + rating.Name + ", about "
                      + rating.TrainingDays + " days, ready " + DateDisplay.Format(student.EndDate) + "). It cost " + Money(price) + ".";
            return true;
        }

        public static void CancelStudent(TripSchool school, PilotStudent student)
        {
            school.RemoveStudent(student);
            student.Instructor?.RemoveStudent(student);
            student.Instructor = null;
        }

        public static bool HireInstructor(Carrier carrier, TripSchool school, Instructor instructor, out string message)
        {
            if (instructor == null || instructor.TripSchool != null)
            {
                message = "That instructor is not available.";
                return false;
            }
            if (school.NumberOfInstructors >= TripSchool.MaxNumberOfInstructors)
            {
                message = school.Name + " has " + TripSchool.MaxNumberOfInstructors + " instructors already, which is the most it can have.";
                return false;
            }
            school.AddInstructor(instructor);
            instructor.TripSchool = school;
            message = instructor.Profile.Name + " is hired as an instructor at " + school.Name + ".";
            return true;
        }

        public static bool DismissInstructor(TripSchool school, Instructor instructor, out string message)
        {
            if (instructor.Students.Count > 0)
            {
                message = instructor.Profile.Name + " still has students. Wait until they graduate, or move or cancel them first.";
                return false;
            }
            school.RemoveInstructor(instructor);
            instructor.TripSchool = null;
            message = instructor.Profile.Name + " leaves " + school.Name + ".";
            return true;
        }

        public static bool BuyCoach(Carrier carrier, TripSchool school, TrainingCoachType type, out string message)
        {
            if (type == null)
            {
                message = "Choose a training coach.";
                return false;
            }
            if (carrier.Money < type.Price)
            {
                message = "Not enough money: a " + type.Name + " costs " + Money(type.Price) + ".";
                return false;
            }
            school.AddTrainingCoach(new TrainingCoach(type, Game.GameTime, school));
            CarrierHelpers.AddCarrierInvoice(carrier, Game.GameTime, Invoice.InvoiceType.CarrierExpenses, -type.Price);
            message = "Bought a " + type.Name + " for " + Money(type.Price) + ".";
            return true;
        }

        public static bool SellCoach(Carrier carrier, TripSchool school, TrainingCoach coach, out string message)
        {
            if (school.Students.Any(s => s.Coach == coach))
            {
                message = "Students are training in this " + coach.Type.Name + ". Sell it after they graduate.";
                return false;
            }
            school.RemoveTrainingCoach(coach);
            double price = coach.Type.Price*0.75;
            CarrierHelpers.AddCarrierInvoice(carrier, Game.GameTime, Invoice.InvoiceType.CarrierExpenses, price);
            message = "Sold the " + coach.Type.Name + " for " + Money(price) + " (75% of the price).";
            return true;
        }

        // ---- type training of pilots you have hired ----

        /// <summary>A day of a substitute pilot, while a pilot who flies an coach is away on training.</summary>
        public const double SubstituteDayPrice = 500;

        public static List<string> TrainingFamilies(Pilot pilot)
        {
            return Families().Where(f => pilot.Coaches == null || !pilot.Coaches.Contains(f)).ToList();
        }

        public static double GetSubstitutePrice(Pilot pilot, string family)
        {
            return pilot.Coach == null ? 0 : GeneralHelpers.GetInflationPrice(CarrierHelpers.GetTrainingDays(pilot, family)*SubstituteDayPrice);
        }

        public static double GetTrainingCost(Pilot pilot, string family)
        {
            return CarrierHelpers.GetTrainingPrice(pilot, family) + GetSubstitutePrice(pilot, family);
        }

        /// <summary>Sends a pilot of the carrier to training for another coach family. The pilot can fly it when the training ends.</summary>
        public static bool TrainPilot(Carrier carrier, Pilot pilot, string family, out string message)
        {
            if (pilot == null || pilot.Carrier != carrier)
            {
                message = "Choose one of your pilots.";
                return false;
            }
            if (pilot.Training != null)
            {
                message = pilot.Profile.Name + " is already training for the " + pilot.Training.CoachFamily + ".";
                return false;
            }
            if (string.IsNullOrEmpty(family) || (pilot.Coaches != null && pilot.Coaches.Contains(family)))
            {
                message = "Choose a coach family " + pilot.Profile.Name + " cannot run yet.";
                return false;
            }

            double training = CarrierHelpers.GetTrainingPrice(pilot, family);
            double substitute = GetSubstitutePrice(pilot, family);
            int days = CarrierHelpers.GetTrainingDays(pilot, family);
            if (carrier.Money < training + substitute)
            {
                message = "Not enough money: the training costs " + Money(training + substitute) + ".";
                return false;
            }

            if (substitute > 0)
            {
                CarrierHelpers.AddCarrierInvoice(carrier, Game.GameTime, Invoice.InvoiceType.CarrierExpenses, -substitute);
            }
            CarrierHelpers.SendForTraining(carrier, pilot, family, days, training);
            message = pilot.Profile.Name + " is training for the " + family + " for " + days + " days, until " + DateDisplay.Format(pilot.Training.EndDate) + ". It costs "
                      + Money(training) + (substitute > 0 ? " plus " + Money(substitute) + " for a substitute pilot on " + pilot.Coach.Name : "") + ".";
            return true;
        }

        // ---- assigning pilots to coach ----

        /// <summary>Coach that can take this pilot: the cockpit is not full and the pilot can fly the family. The coach he flies now is left out.</summary>
        public static List<FleetCoach> AssignTargets(Carrier carrier, Pilot pilot)
        {
            if (pilot == null || pilot.Coaches == null) return new List<FleetCoach>();
            return carrier.Fleet
                          .Where(f => f != pilot.Coach && f.Pilots.Count < f.Coach.Type.CockpitCrew && pilot.Coaches.Contains(f.Coach.Type.CoachFamily))
                          .OrderBy(f => f.Name).ToList();
        }

        public static bool AssignPilot(Carrier carrier, Pilot pilot, FleetCoach target, out string message)
        {
            if (pilot == null || pilot.Carrier != carrier)
            {
                message = "Choose one of your pilots.";
                return false;
            }
            if (pilot.Training != null)
            {
                message = pilot.Profile.Name + " is away on training until " + DateDisplay.Format(pilot.Training.EndDate) + ".";
                return false;
            }
            if (target == null || !carrier.Fleet.Contains(target))
            {
                message = "Choose one of your coach.";
                return false;
            }
            if (pilot.Coaches == null || !pilot.Coaches.Contains(target.Coach.Type.CoachFamily))
            {
                message = pilot.Profile.Name + " cannot run the " + target.Coach.Type.CoachFamily + ". Train him for it first.";
                return false;
            }
            if (target.Pilots.Count >= target.Coach.Type.CockpitCrew)
            {
                message = target.Name + " has a full cockpit crew already.";
                return false;
            }

            string from = pilot.Coach?.Name;
            pilot.Coach?.RemovePilot(pilot);
            target.AddPilot(pilot);
            message = pilot.Profile.Name + " now runs " + target.Name + (from != null ? " (was on " + from + ")" : "") + ".";
            return true;
        }

        public static bool UnassignPilot(Carrier carrier, Pilot pilot, out string message)
        {
            if (pilot == null || pilot.Carrier != carrier || pilot.Coach == null)
            {
                message = "That pilot has no coach.";
                return false;
            }
            string name = pilot.Coach.Name;
            pilot.Coach.RemovePilot(pilot);
            message = pilot.Profile.Name + " leaves " + name + " and is without a coach.";
            return true;
        }

        /// <summary>Puts pilots who have no coach (new graduates, for example) on coach that need pilots and match their type.</summary>
        public static int AssignIdlePilots(Carrier carrier)
        {
            int n = 0;
            foreach (var pilot in carrier.Pilots.Where(p => p.Coach == null && p.Training == null).ToList())
            {
                var target = carrier.Fleet.FirstOrDefault(
                    f => f.Pilots.Count < f.Coach.Type.CockpitCrew && pilot.Coaches != null && pilot.Coaches.Contains(f.Coach.Type.CoachFamily));
                if (target == null) continue;
                pilot.Coach = target;
                target.AddPilot(pilot);
                n++;
            }
            return n;
        }
    }
}
