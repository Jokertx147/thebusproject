using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;
using TheBusProject.Models.General;

namespace TheBusProject.Models.Stations
{
    [Serializable]
    //the class for a facility at an station
    public class StationFacility : BaseModel
    {
        #region Fields

        [Versioning("price")] private double _aPrice;

        #endregion

        #region Constructors and Destructors

        public StationFacility(
            string section,
            string uid,
            string shortname,
            FacilityType type,
            int buildingDays,
            int typeLevel,
            double price,
            int serviceLevel,
            int luxuryLevel)
        {
            Section = section;
            Uid = uid;
            Shortname = shortname;
            Price = price;
            LuxuryLevel = luxuryLevel;
            ServiceLevel = serviceLevel;
            BuildingDays = buildingDays;
            TypeLevel = typeLevel;
            Type = type;
        }

        private StationFacility(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Enums

        public enum EmployeeTypes
        {
            Maintenance,

            Support
        }

        public enum FacilityType
        {
            Lounge,

            Service,

            CheckIn,

            SelfCheck,

            TicketOffice,

            Cargo
        }

        #endregion

        #region Public Properties

        public static string Section { get; set; }

        [Versioning("buildingdays")]
        public int BuildingDays { get; set; }

        [Versioning("employeetype")]
        public EmployeeTypes EmployeeType { get; set; }

        [Versioning("luxurylevel")]
        public int LuxuryLevel { get; set; }

        public virtual string Name => Translator.GetInstance().GetString(Section, Uid);

        [Versioning("employees")]
        public int NumberOfEmployees { get; set; }

        public double Price
        {
            get { return GeneralHelpers.GetInflationPrice(_aPrice); }
            set { _aPrice = value; }
        }

        [Versioning("servicelevel")]
        public int ServiceLevel { get; set; }

        [Versioning("shortname")]
        public string Shortname { get; set; }

        [Versioning("type")]
        public FacilityType Type { get; set; }

        [Versioning("typelevel")]
        public int TypeLevel { get; set; }

        [Versioning("uid")]
        public string Uid { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        #endregion
    }

    //the collection of facilities
    public class StationFacilities
    {
        #region Static Fields

        private static Dictionary<string, StationFacility> _facilities = new Dictionary<string, StationFacility>();

        #endregion

        #region Public Methods and Operators

        public static void AddFacility(StationFacility facility)
        {
            _facilities.Add(facility.Shortname, facility);
        }

        public static void Clear()
        {
            _facilities = new Dictionary<string, StationFacility>();
        }

        //returns a facility

        //returns the list of facilities
        public static List<StationFacility> GetFacilities()
        {
            return _facilities.Values.ToList();
        }

        //returns all facilities of a specific type
        public static List<StationFacility> GetFacilities(StationFacility.FacilityType type)
        {
            return GetFacilities().FindAll((facility => facility.Type == type));
        }

        public static StationFacility GetFacility(string shortname)
        {
            return _facilities[shortname];
        }

        #endregion

        //clears the list

        //adds a new facility to the collection
    }
}