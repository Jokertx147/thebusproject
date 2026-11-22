using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;

namespace TheBusProject.Models.Pilots
{
    //the class for a pilot
    [Serializable]
    public class Pilot : BaseModel
    {
        #region Constants

        public const int RetirementAge = 55;

        #endregion

        #region Constructors and Destructors

        public Pilot(PilotProfile profile, DateTime educationTime, PilotRating rating)
        {
            Profile = profile;
            EducationTime = educationTime;
            Rating = rating;
            Coaches = new List<string>();
        }

        private Pilot(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
            if (Version == 1)
            {
                Rating = GeneralHelpers.GetPilotRating();
            }
            if (Version < 3)
            {
                Coaches = GeneralHelpers.GetPilotCoaches(this);
                Training = null;
            }
        }

        #endregion

        #region Public Properties

        [Versioning("coaches", Version = 3)]
        public List<string> Coaches { get; set; }

        /// <summary>0 to 100: how happy the pilot is at his carrier.</summary>
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
        public PilotProfile Profile { get; set; }

        [Versioning("pilotrating", Version = 2)]
        public PilotRating Rating { get; set; }

        [Versioning("training", Version = 3)]
        public PilotTraining Training { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 3);

            base.GetObjectData(info, context);
        }

        //adds an coach type family which the pilot expirence
        public void AddCoachFamily(string family)
        {
            Coaches.Add(family);
        }

        //sets the carrier for a pilot
        public void SetCarrier(Carrier carrier, DateTime signDate)
        {
            Carrier = carrier;
            CarrierSignedDate = signDate;
        }

        #endregion
    }

    //the list of pilots
    public class Pilots
    {
        #region Static Fields

        private static readonly List<Pilot> pilots = new List<Pilot>();

        #endregion

        #region Public Methods and Operators

        public static void AddPilot(Pilot pilot)
        {
            lock (pilots)
            {
                if (pilot != null)
                {
                    pilots.Add(pilot);
                }
            }
        }

        //clears the list of pilots
        public static void Clear()
        {
            pilots.Clear();
        }

        public static int GetNumberOfPilots()
        {
            return pilots.Count;
        }

        public static int GetNumberOfUnassignedPilots()
        {
            return GetUnassignedPilots().Count;
        }

        //returns all pilots
        public static List<Pilot> GetPilots()
        {
            return pilots;
        }

        //returns all unassigned pilots
        public static List<Pilot> GetUnassignedPilots()
        {
            List<Pilot> unassigned = pilots.FindAll(p => p.Carrier == null);

            if (unassigned.Count < 5)
            {
                GeneralHelpers.CreatePilots(10);

                return GetUnassignedPilots();
            }

            return unassigned;
        }

        public static List<Pilot> GetUnassignedPilots(Predicate<Pilot> match)
        {
            return GetUnassignedPilots().FindAll(match);
        }

        //removes a pilot from the list
        public static void RemovePilot(Pilot pilot)
        {
            pilots.Remove(pilot);
        }

        #endregion

        //adds a pilot to the list

        //counts the number of unassigned pilots
    }
}