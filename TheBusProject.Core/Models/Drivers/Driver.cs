using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;

namespace TheBusProject.Models.Drivers
{
    //the class for a driver
    [Serializable]
    public class Driver : BaseModel
    {
        #region Constants

        public const int RetirementAge = 55;

        #endregion

        #region Constructors and Destructors

        public Driver(DriverProfile profile, DateTime educationTime, DriverRating rating)
        {
            Profile = profile;
            EducationTime = educationTime;
            Rating = rating;
            Coaches = new List<string>();
        }

        private Driver(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
            if (Version == 1)
            {
                Rating = GeneralHelpers.GetDriverRating();
            }
            if (Version < 3)
            {
                Coaches = GeneralHelpers.GetDriverCoaches(this);
                Training = null;
            }
        }

        #endregion

        #region Public Properties

        [Versioning("coaches", Version = 3)]
        public List<string> Coaches { get; set; }

        /// <summary>0 to 100: how happy the driver is at his carrier.</summary>
        [Versioning("morale")]
        public double Morale { get; set; } = 70;

        [Versioning("carrier")]
        public Carrier Carrier { get; set; }

        [Versioning("signeddate")]
        public DateTime CarrierSignedDate { get; set; }

        [Versioning("coach")]
        public FleetCoach Coach { get; set; }

        [Versioning("education")]
        public DateTime EducationTime { get; set; }

        public bool OnTraining => Training != null;

        [Versioning("profile")]
        public DriverProfile Profile { get; set; }

        [Versioning("driverrating", Version = 2)]
        public DriverRating Rating { get; set; }

        [Versioning("training", Version = 3)]
        public DriverTraining Training { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 3);

            base.GetObjectData(info, context);
        }

        //adds an coach type family which the driver expirence
        public void AddCoachFamily(string family)
        {
            Coaches.Add(family);
        }

        //sets the carrier for a driver
        public void SetCarrier(Carrier carrier, DateTime signDate)
        {
            Carrier = carrier;
            CarrierSignedDate = signDate;
        }

        #endregion
    }

    //the list of drivers
    public class Drivers
    {
        #region Static Fields

        private static readonly List<Driver> drivers = new List<Driver>();

        #endregion

        #region Public Methods and Operators

        public static void AddDriver(Driver driver)
        {
            lock (drivers)
            {
                if (driver != null)
                {
                    drivers.Add(driver);
                }
            }
        }

        //clears the list of drivers
        public static void Clear()
        {
            drivers.Clear();
        }

        public static int GetNumberOfDrivers()
        {
            return drivers.Count;
        }

        public static int GetNumberOfUnassignedDrivers()
        {
            return GetUnassignedDrivers().Count;
        }

        //returns all drivers
        public static List<Driver> GetDrivers()
        {
            return drivers;
        }

        //returns all unassigned drivers
        public static List<Driver> GetUnassignedDrivers()
        {
            List<Driver> unassigned = drivers.FindAll(p => p.Carrier == null);

            if (unassigned.Count < 5)
            {
                GeneralHelpers.CreateDrivers(10);

                return GetUnassignedDrivers();
            }

            return unassigned;
        }

        public static List<Driver> GetUnassignedDrivers(Predicate<Driver> match)
        {
            return GetUnassignedDrivers().FindAll(match);
        }

        //removes a driver from the list
        public static void RemoveDriver(Driver driver)
        {
            drivers.Remove(driver);
        }

        #endregion

        //adds a driver to the list

        //counts the number of unassigned drivers
    }
}