using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Countries;

namespace TheBusProject.Models.Coaches
{
    //the class for an coach
    [Serializable]
    public class Coach : BaseModel
    {
        #region Enums

        public enum StatusTypes
        {
            Normal,
            Leasing
        }

        #endregion

        #region Fields

        private readonly Random _rnd = new Random();

        #endregion

        #region Constructors and Destructors

        public Coach(string id, CoachType type, string tailNumber, DateTime builtDate)
        {
            ID = id;
            BuiltDate = new DateTime(builtDate.Year, builtDate.Month, builtDate.Day);
            Type = type;
            LastServiceCheck = 0;
            TailNumber = tailNumber;
            Flown = 0;
            Condition = _rnd.Next(90, 100);
            Status = StatusTypes.Normal;

            Classes = new List<CoachClass>();

            // Every coach gets a cabin. One with no cabin cannot be asked which cabin a passenger sits in, so
            // running it threw as soon as its passengers were counted.
            var aClass = new CoachClass(
                CoachClass.ClassType.EconomyClass,
                ((CoachPassengerType) Type).MaxSeatingCapacity);
            aClass.CreateBasicFacilities(Carrier);
            Classes.Add(aClass);

        }

        private Coach(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
            Classes.RemoveAll(c => c == null);

            var doubleClasses =
                new List<CoachClass.ClassType>(
                    Classes.Where(c => Classes.Count(cc => cc.Type == c.Type) > 1).Select(c => c.Type));

            foreach (var doubleClassType in doubleClasses)
            {
                var dClass = Classes.Last(c => c.Type == doubleClassType);
                Classes.Remove(dClass);
            }

            // A helicopter or a combi saved before they were given a cabin has none, and flying it would throw, so it
            // is given the one it should have had.
            if (Classes.Count == 0 && Type is CoachPassengerType seats)
            {
                var aClass = new CoachClass(CoachClass.ClassType.EconomyClass, seats.MaxSeatingCapacity);
                aClass.CreateBasicFacilities(Carrier);
                Classes.Add(aClass);
            }

            if (Version == 1)
                EngineType = null;
            if (Version < 3)
                FlownHours = new TimeSpan();
            if (Version < 4)
                Status = StatusTypes.Normal;
            if (Version < 5)
            {
                Owner = Carrier;
            }
        }

        #endregion

        #region Public Properties

        public int Age => GetAge();

        [Versioning("carrier")]
        public Carrier Carrier { get; set; }

        [Versioning("built")]
        public DateTime BuiltDate { get; set; }

        [Versioning("classes")]
        public List<CoachClass> Classes { get; set; }

        [Versioning("enginetype", Version = 2)]
        public EngineType EngineType { get; set; }

        public string CabinConfiguration
        {
            get
            {
                return
                    $"{GetSeatingCapacity(CoachClass.ClassType.FirstClass)}F | {GetSeatingCapacity(CoachClass.ClassType.BusinessClass)}C | {GetSeatingCapacity(CoachClass.ClassType.EconomyClass)}Y";
            }
        }

        [Versioning("condition")]
        public double Condition { get; set; }

        [Versioning("flownhours")]
        public TimeSpan FlownHours { get; set; }

        [Versioning("run")]
        public double Flown { get; set; }

        [Versioning("fuelcapacity")]
        public long FuelCapacity { get; set; }

        [Versioning("id")]
        public string ID { get; set; }

        //distance flown by the coach

        [Versioning("lastservice")]
        public double LastServiceCheck { get; set; }

        //the km were the coach was last at service

        public long LeasingPrice => GetLeasingPrice();

        public long Price => GetPrice();

        public Country Registered => Countries.GetCountryFromTailNumber(TailNumber);

        public double FuelConsumption
        {
            get
            {
                if (EngineType == null)
                    return Type.FuelConsumption;
                return Type.FuelConsumption*EngineType.ConsumptationModifier;
            }
        }

        public double CruisingSpeed => EngineType == null ? Type.CruisingSpeed : Math.Min(Type.CruisingSpeed, EngineType.MaxSpeed);

        public long MinBayLength => EngineType == null
            ? Type.MinBayLength
            : Convert.ToInt64(Type.MinBayLength*EngineType.BayModifier);

        public long Range => EngineType == null ? Type.Range : Convert.ToInt64(Type.Range*EngineType.RangeModifier);

        [Versioning("status", Version = 4)]
        public StatusTypes Status { get; set; }

        [Versioning("tailnumber")]
        public string TailNumber { get; set; }

        [Versioning("type")]
        public CoachType Type { get; set; }

        [Versioning("owner", Version = 4)]
        public Carrier Owner { get; set; }

        private int GetSeatingCapacity(CoachClass.ClassType classType)
        {
            return Classes.Exists(x => x.Type == classType) ? Classes.Find(x => x.Type == classType).SeatingCapacity : 0;
        }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 5);

            base.GetObjectData(info, context);
        }

        //adds a new coach class to the coach
        public void AddCoachClass(CoachClass coachClass)
        {
            if (coachClass != null)
            {
                if (Classes.Exists(c => c.Type == coachClass.Type))
                {
                    Classes[Classes.FindIndex(c => c.Type == coachClass.Type)] = coachClass;
                }
                else
                {
                    Classes.Add(coachClass);
                    if (coachClass.GetFacilities().Count == 0)
                    {
                        coachClass.CreateBasicFacilities(Carrier);
                    }
                }
            }
        }

        //removes an coach class from the coach

        //clear the list of coach classes
        public void ClearCoachClasses()
        {
            Classes.Clear();
        }

        public int GetAge()
        {
            return MathHelpers.CalculateAge(BuiltDate, GameObject.GetInstance().GameTime);
        }

        //returns the total amount of seat capacity

        //returns the coach class for the coach
        /// <summary>
        ///     The cabin of this class, or the first cabin the coach has when it has none of that class, or null
        ///     when it has no cabin at all. Callers that want the real seat count of a class should use
        ///     GetCoachClasses or look in Classes themselves: the first cabin is a different cabin with a different
        ///     number of seats, and counting it as the one asked for overstates the coach.
        /// </summary>
        public CoachClass GetCoachClass(CoachClass.ClassType type)
        {
            if (Classes.Exists(c => c.Type == type))
            {
                return Classes.Find(c => c.Type == type);
            }
            return Classes.Count > 0 ? Classes[0] : null;
        }


        public long GetLeasingPrice()
        {
            const double months = 12*15;
            const double rate = 1.30;

            var leasingPrice = (GetPrice()*rate/months);
            return Convert.ToInt64(leasingPrice);
        }

        /// <summary>
        ///     What the coach would cost new today: the type's list price plus what has been fitted to it, before any
        ///     allowance for its age or the state it is in. <see cref="GetPrice" /> is this with those allowances made.
        /// </summary>
        public long GetListPrice()
        {
            return Convert.ToInt64(ListPrice());
        }

        /// <summary>
        ///     What the coach loses in value over a year at its current age, which is what owning it costs. It is
        ///     worked out from the game's own value curve rather than from a separate rate, so the cost of holding a
        ///     coach and what it is worth when sold cannot drift apart. The curve is list*(1-0.02a)*(1-0.03a), so the
        ///     loss in a year is list*(0.05 - 0.0012a): about 5% of list when the coach is new, falling as it ages and
        ///     there is less left to lose. On a $700,000 coach that is $35,000 a year new and $23,000 at fourteen,
        ///     against the $21,936 a bus a year that Greyhound's accounts show for a fleet of mid-life coaches.
        /// </summary>
        public long GetAnnualDepreciation()
        {
            double rate = 0.05 - 0.0012*GetAge();
            if (rate <= 0) return 0;
            return Convert.ToInt64(ListPrice()*(Condition/100.0)*rate);
        }

        public long GetPrice()
        {
            var age = GetAge();

            // the value curve runs out at age 50 and would go negative past it, so it is held at a twentieth of
            // list: a coach that old is worth its parts, not nothing and not less than nothing
            double devaluationPercent = Math.Max(0.05, 1 - 0.02*age);

            return Convert.ToInt64(ListPrice()*devaluationPercent*(Condition/100));
        }

        /// <summary>List price plus fitted equipment, with no allowance for age or condition.</summary>
        private double ListPrice()
        {
            double basePrice = Type.Price;

            // a copy of the cabin: a computer carrier may be refitting this coach on another thread, so a class can be
            // there without its facilities yet (that used to fail the whole game day); a missing one adds nothing
            CoachClass[] classes;
            try
            {
                classes = Classes.ToArray();
            }
            catch (ArgumentException)
            {
                classes = new CoachClass[0];
            }

            double facilityPrice = 0;
            foreach (CoachClass aClass in classes)
            {
                if (aClass == null) continue;
                foreach (var kind in new[] {CoachFacility.FacilityType.Audio, CoachFacility.FacilityType.Video, CoachFacility.FacilityType.Seat})
                {
                    CoachFacility facility;
                    try
                    {
                        facility = aClass.GetFacility(kind);
                    }
                    catch (Exception)
                    {
                        facility = null;
                    }
                    if (facility != null) facilityPrice += facility.PricePerSeat*(facility.PercentOfSeats/100.0)*aClass.SeatingCapacity;
                }
            }

            basePrice += facilityPrice;

            double diffEnginePrice;

            if (EngineType == null)
                diffEnginePrice = 0;
            else
            {
                double enginePrice = EngineType.Price;
                double standardEnginePrice = EngineTypes.GetStandardEngineType(Type).Price;

                diffEnginePrice = enginePrice - standardEnginePrice;
            }

            basePrice += diffEnginePrice;

            return basePrice;
        }

        public int GetTotalSeatCapacity()
        {
            // the class list can change on another thread while the game is being set up, so work on a copy
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    return Classes.ToArray().Sum(aClass => aClass.SeatingCapacity);
                }
                catch (Exception e) when (e is InvalidOperationException || e is ArgumentException || e is NullReferenceException)
                {
                }
            }

            return 0;
        }

        // 3% less each year of age, and a fifth of the price from 25 years on (written as 3/100 and 20/100 before,
        // whole-number divisions: an coach kept its full price until 25 and was then worth nothing)
        public long GetValue()
        {
            if (GetAge() < 25)
            {
                return (long) (GetPrice()*(1 - GetAge()*0.03));
            }
            return (long) (GetPrice()*0.20);
        }

        public void RemoveCoachClass(CoachClass coachClass)
        {
            Classes.Remove(coachClass);
        }

        #endregion
    }

    //the list of coaches
    public class Coaches
    {
        /// <summary>
        ///     The oldest a coach on the second-hand market can be. The FTA's useful life benchmark for an
        ///     over-the-road bus is 14 years; a study of transit fleets found the average bus retires at 15.1 and
        ///     that retirements run as late as 20. Past about twenty years a coach is not carrying passengers for a
        ///     living, so there is no market in one.
        ///     <para>
        ///         It is enforced here, where coaches are listed, and not only by the monthly sweep that clears them
        ///         out. A carrier that sells a coach only sets its Carrier to null, which puts it straight back on
        ///         the market at whatever age it has reached, and the sweep would not catch it until the following
        ///         month. That window was real: a playthrough bought a coach built in 1989 in 2014, at 25 years old.
        ///     </para>
        /// </summary>
        public const int OldestForSale = 22;

        #region Static Fields

        private static List<Coach> _coaches = new List<Coach>();

        #endregion

        #region Public Methods and Operators

        public static void AddCoach(Coach coach)
        {
            lock (_coaches)
            {
                //if (coaches.Exists(a => a.ID == coach.ID))
                //  throw new Exception("Coach element already exists exception");

                _coaches.Add(coach);
            }
        }

        public static void Clear()
        {
            _coaches = new List<Coach>();
        }

        //returns an coach
        public static Coach GetCoach(string tailnumber)
        {
            return _coaches.Find(coach => coach.TailNumber == tailnumber);
        }

        //returns the list of coaches

        //returns the list of coaches for sale
        public static List<Coach> GetCoachesForSale()
        {
            return _coaches.FindAll(a => a.Carrier == null && a.Status == Coach.StatusTypes.Normal && !TooOldToSell(a));
        }

        /// <summary>Whether a coach is past the age anyone would buy it to carry passengers in.</summary>
        public static bool TooOldToSell(Coach coach)
        {
            return coach.GetAge() >= OldestForSale;
        }

        //returns the list of coaches for leasing
        public static List<Coach> GetCoachesForLeasing()
        {
            return _coaches.FindAll(a => a.Status == Coach.StatusTypes.Leasing);
        }

        //returns the list of coaches for sale
        public static List<Coach> GetCoachesForSale(Predicate<Coach> match)
        {
            return _coaches.FindAll(a => a.Carrier == null && !TooOldToSell(a)).FindAll(match);
        }

        /// <summary>
        ///     Coaches nobody will buy any more, for the monthly sweep that takes them off the books. They are
        ///     already hidden from the market by <see cref="GetCoachesForSale()" />; this is what clears the objects.
        /// </summary>
        public static List<Coach> GetCoachesTooOldToSell()
        {
            return _coaches.FindAll(a => a.Carrier == null && TooOldToSell(a));
        }

        public static List<Coach> GetAllCoaches()
        {
            return _coaches;
        }

        //removes an coach from the list
        public static void RemoveCoach(Coach coach)
        {
            _coaches.Remove(coach);
        }

        #endregion
    }
}