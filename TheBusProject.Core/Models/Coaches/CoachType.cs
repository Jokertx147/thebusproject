using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;
using TheBusProject.Models.General;

// TODO - Split classes into their own files

namespace TheBusProject.Models.Coaches
{
    //the class for a type of coach
    [Serializable]
    public abstract class CoachType : BaseModel
    {
        #region Constructors and Destructors

        protected CoachType(
            Manufacturer manufacturer,
            TypeOfCoach typeOfCoach,
            string name,
            string family,
            int cockpitCrew,
            double speed,
            long range,
            double wingspan,
            double length,
            double weight,
            double consumption,
            long price,
            long minRunwaylength,
            long fuelcapacity,
            BodyType body,
            TypeRange rangeType,
            TypeOfEngine engine,
            Period<DateTime> produced,
            int prodRate,
            bool isConvertable,
            bool standardType)
        {
            TypeCoach = typeOfCoach;
            CoachFamily = family;
            Manufacturer = manufacturer;
            Name = name;
            CruisingSpeed = speed;
            Range = range;
            Wingspan = wingspan;
            Length = length;
            Weight = weight;
            CockpitCrew = cockpitCrew;
            Price = price;
            FuelConsumption = consumption;
            Produced = produced;
            Engine = engine;
            Body = body;
            RangeType = rangeType;
            MinRunwaylength = minRunwaylength;
            FuelCapacity = fuelcapacity;
            IsStandardType = standardType;
            ProductionRate = prodRate;
            IsConvertable = isConvertable;
        }

        protected CoachType(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
            if (Version == 1)
            {
                IsConvertable = TypeCoach != TypeOfCoach.Cargo;
            }
            if (Version < 3)
                Weight = CoachHelpers.GetCalculatedWeight(this);
        }

        #endregion

        #region Enums

        public enum BodyType
        {
            NarrowBody,

            SingleAisle,

            WideBody,

            Helicopter
        }

        public enum TypeOfCoach
        {
            Passenger,

            Cargo,

            Mixed,

            Helicopter
        }

        public enum TypeOfEngine
        {
            Jet,

            Turboprop
        }

        public enum TypeRange
        {
            Regional,

            ShortRange,

            MediumRange,

            LongRange
        }

        #endregion

        #region Public Properties

        [Versioning("family")]
        public string CoachFamily { get; set; }

        [Versioning("basetype")]
        public CoachType BaseType { get; set; }

        [Versioning("body")]
        public BodyType Body { get; set; }


        public int Capacity => GetCapacity();

        [Versioning("cockpit")]
        public int CockpitCrew { get; set; }

        [Versioning("engine")]
        public TypeOfEngine Engine { get; set; }

        [Versioning("fuelcapacity")]
        public long FuelCapacity { get; set; }

        [Versioning("image")]
        public string Image { get; set; }

        [Versioning("weight", Version = 3)]
        public double Weight { get; set; }

        [Versioning("isconvertable", Version = 2)]
        public bool IsConvertable { get; set; }

        [Versioning("isstandard")]
        public bool IsStandardType { get; set; }

        [Versioning("length")]
        public double Length { get; set; }

        public double Maintenance => GetMaintenance();

        [Versioning("manufacturer")]
        public Manufacturer Manufacturer { get; set; }

        [Versioning("speed")]
        public double CruisingSpeed { get; set; }

        [Versioning("range")]
        public long Range { get; set; }

        [Versioning("fuel")]
        public double FuelConsumption { get; set; }

        [Versioning("runway")]
        public long MinRunwaylength { get; set; }

        [Versioning("name")]
        public string Name { get; set; }

        public long Price
        {
            get { return Convert.ToInt64(GeneralHelpers.GetInflationPrice(APrice)); }
            set { APrice = value; }
        }

        [Versioning("produced")]
        public Period<DateTime> Produced { get; set; }

        [Versioning("production")]
        public int ProductionRate { get; set; }

        [Versioning("rangetype")]
        public TypeRange RangeType { get; set; }

        [Versioning("typecoach")]
        public TypeOfCoach TypeCoach { get; set; }

        [Versioning("wingspan")]
        public double Wingspan { get; set; }

        [Versioning("price")]
        public long APrice { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 3);

            base.GetObjectData(info, context);
        }

        public abstract int GetCapacity();

        public long GetLeasingPrice()
        {
            const double months = 5*12;
            const double rate = 1.06;
            double leasingPrice = Price/months*rate;
            return Convert.ToInt64(leasingPrice);
        }

        public long GetMaintenance()
        {
            double maintenance = 0.0013*Price*12;
            return Convert.ToInt64(maintenance);
        }

        #endregion
    }

    //the class for a passenger coach type
    [Serializable]
    public class CoachPassengerType : CoachType
    {
        #region Constructors and Destructors

        public CoachPassengerType(
            Manufacturer manufacturer,
            string name,
            string family,
            int seating,
            int cockpitcrew,
            int cabincrew,
            double speed,
            long range,
            double wingspan,
            double length,
            double weight,
            double consumption,
            long price,
            int maxCoachClasses,
            long minRunwaylength,
            long fuelcapacity,
            BodyType body,
            TypeRange rangeType,
            TypeOfEngine engine,
            Period<DateTime> produced,
            int prodRate,
            bool isConvertable,
            bool standardType = true)
            : base(
                manufacturer,
                TypeOfCoach.Passenger,
                name,
                family,
                cockpitcrew,
                speed,
                range,
                wingspan,
                length,
                weight,
                consumption,
                price,
                minRunwaylength,
                fuelcapacity,
                body,
                rangeType,
                engine,
                produced,
                prodRate,
                isConvertable,
                standardType)
        {
            MaxSeatingCapacity = seating;
            CabinCrew = cabincrew;
            MaxCoachClasses = maxCoachClasses;
        }

        protected CoachPassengerType(SerializationInfo info, StreamingContext ctxt)
            : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("cabincrew")]
        public int CabinCrew { get; set; }

        [Versioning("maxclasses")]
        public int MaxCoachClasses { get; set; }

        [Versioning("maxcapacity")]
        public int MaxSeatingCapacity { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        public override int GetCapacity()
        {
            return MaxSeatingCapacity;
        }

        #endregion
    }

    // TODO - Derive from CoachType directly or possibly create a type field in parent class.
    //the class for a combi coach type
    [Serializable]
    public class CoachCombiType : CoachPassengerType
    {
        #region Constructors and Destructors

        public CoachCombiType(
            Manufacturer manufacturer,
            string name,
            string family,
            int seating,
            int cockpitcrew,
            int cabincrew,
            double speed,
            long range,
            double wingspan,
            double length,
            double weight,
            double consumption,
            long price,
            int maxCoachClasses,
            long minRunwaylength,
            long fuelcapacity,
            BodyType body,
            TypeRange rangeType,
            TypeOfEngine engine,
            Period<DateTime> produced,
            int prodRate,
            double cargo,
            bool isConvertable,
            bool standardType = true)
            : base(
                manufacturer,
                name,
                family,
                seating,
                cockpitcrew,
                cabincrew,
                speed,
                range,
                wingspan,
                length,
                weight,
                consumption,
                price,
                maxCoachClasses,
                minRunwaylength,
                fuelcapacity,
                body,
                rangeType,
                engine,
                produced,
                prodRate,
                isConvertable,
                standardType)
        {
            CargoSize = cargo;
            TypeCoach = TypeOfCoach.Mixed;
        }

        private CoachCombiType(SerializationInfo info, StreamingContext ctxt)
            : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("cargo")]
        public double CargoSize { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        #endregion
    }

    //the class for a cargo coach type
    [Serializable]
    public class CoachCargoType : CoachType
    {
        #region Constructors and Destructors

        public CoachCargoType(
            Manufacturer manufacturer,
            string name,
            string family,
            int cockpitcrew,
            double cargoSize,
            double speed,
            long range,
            double wingspan,
            double length,
            double weight,
            double consumption,
            long price,
            long minRunwaylength,
            long fuelcapacity,
            BodyType body,
            TypeRange rangeType,
            TypeOfEngine engine,
            Period<DateTime> produced,
            int prodRate,
            bool isConvertable,
            bool standardType = true)
            : base(
                manufacturer,
                TypeOfCoach.Cargo,
                name,
                family,
                cockpitcrew,
                speed,
                range,
                wingspan,
                length,
                weight,
                consumption,
                price,
                minRunwaylength,
                fuelcapacity,
                body,
                rangeType,
                engine,
                produced,
                prodRate,
                isConvertable,
                standardType)
        {
            CargoSize = cargoSize;
        }

        private CoachCargoType(SerializationInfo info, StreamingContext ctxt)
            : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("cargo")]
        public double CargoSize { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 2);

            base.GetObjectData(info, context);
        }

        public override int GetCapacity()
        {
            return (int) CargoSize;
        }

        #endregion
    }

    //the collection of coach types
    public class CoachTypes
    {
        #region Static Fields

        private static List<CoachType> _types = new List<CoachType>();

        #endregion

        #region Public Methods and Operators

        public static void AddType(CoachType type)
        {
            _types.Add(type);
        }

        public static void Clear()
        {
            _types = new List<CoachType>();
        }

        //returns an coach with a name

        //returns all coach types
        public static List<CoachType> GetAllTypes()
        {
            return _types.FindAll(t => t.IsStandardType);
        }

        //returns a list of coach types

        //returns all non standard coach types
        public static List<CoachType> GetNonStandardTypes()
        {
            return _types.FindAll(t => !t.IsStandardType);
        }

        public static CoachType GetType(string name)
        {
            return _types.Find(t => t.Name == name || t.Name.ToUpper() == name);
        }

        public static List<CoachType> GetTypes(Predicate<CoachType> match)
        {
            return _types.FindAll(t => t.IsStandardType).FindAll(match);
        }

        //removes an coach type
        public static void RemoveType(CoachType type)
        {
            _types.Remove(type);
        }

        #endregion

        //clears the list of coach types

        //adds an coach type to the list
    }
}