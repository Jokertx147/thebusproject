using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;
using TheBusProject.Models.General;
using TheBusProject.Models.Pilots;

namespace TheBusProject.Models.Carriers
{
    //the class for an carriers facilities
    [Serializable]
    public class CarrierFacility : BaseModel
    {
        #region Constructors and Destructors

        public CarrierFacility(
            string section,
            string uid,
            double price,
            double monthlyCost,
            int fromYear,
            int serviceLevel,
            int luxuryLevel)
        {
            Section = section;
            Uid = uid;
            FromYear = fromYear;
            MonthlyCost = monthlyCost;
            Price = price;
            LuxuryLevel = luxuryLevel;
            ServiceLevel = serviceLevel;
        }

        public CarrierFacility(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        public static string Section { get; set; }

        [Versioning("monthlycost")]
        public double AMonthlyCost { get; set; }

        [Versioning("price")]
        public double APrice { get; set; }

        //for repairing coaches 

        [Versioning("fromyear")]
        public int FromYear { get; set; }

        [Versioning("luxury")]
        public int LuxuryLevel { get; set; }

        public double MonthlyCost
        {
            get { return GeneralHelpers.GetInflationPrice(AMonthlyCost); }
            set { AMonthlyCost = value; }
        }

        public virtual string Name => Translator.GetInstance().GetString(Section, Uid);

        public double Price
        {
            get { return GeneralHelpers.GetInflationPrice(APrice); }
            set { APrice = value; }
        }

        [Versioning("service")]
        public int ServiceLevel { get; set; }

        public virtual string Shortname => Translator.GetInstance().GetString(Section, Uid, "shortname");

        [Versioning("uid")]
        public string Uid { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (!(this is PilotTrainingFacility))
            {
                AddVersion(info, 1);
            }

            base.GetObjectData(info, context);
        }

        #endregion
    }

    //the collection of facilities
    public class CarrierFacilities
    {
        #region Static Fields

        private static readonly List<CarrierFacility> Facilities = new List<CarrierFacility>();

        #endregion

        #region Public Methods and Operators

        public static void AddFacility(CarrierFacility facility)
        {
            Facilities.Add(facility);
        }

        public static void Clear()
        {
            Facilities.Clear();
        }

        //printed media (the intrip magazine and the newspaper service) is gone after this year
        public const int PrintedMediaLastYear = 2020;

        public static bool IsDiscontinued(CarrierFacility facility, int year)
        {
            return facility != null && year > PrintedMediaLastYear && (facility.Uid == "101" || facility.Uid == "102");
        }

        //returns a facility

        //returns the list of facilities
        public static List<CarrierFacility> GetFacilities()
        {
            return Facilities;
        }

        public static List<CarrierFacility> GetFacilities(Predicate<CarrierFacility> match)
        {
            return Facilities.FindAll(match);
        }

        public static CarrierFacility GetFacility(string uid)
        {
            return Facilities.Find(f => f.Uid == uid);
        }

        #endregion

        //clears the list

        //adds a new facility to the collection
    }
}