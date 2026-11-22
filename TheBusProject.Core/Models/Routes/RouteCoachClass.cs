using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;

namespace TheBusProject.Models.Routes
{
    /*! Route coach class for passengers.
    * This class is used for an coach class onboard of a route coach for passengers
    * The class needs parameters for type of class and the fare price
    */

    [Serializable]
    public class RouteCoachClass : BaseModel
    {
        #region Constructors and Destructors

        public RouteCoachClass(CoachClass.ClassType type, SeatingType seating, double fareprice)
        {
            Facilities = new List<RouteFacility>();
            FarePrice = fareprice;
            Seating = seating;
            Type = type;
        }

        private RouteCoachClass(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Enums

        public enum SeatingType
        {
            ReservedSeating,

            FreeSeating
        }

        #endregion

        #region Public Properties

        [Versioning("facilities")]
        public List<RouteFacility> Facilities { get; set; }

        [Versioning("fareprice")]
        public double FarePrice { get; set; }

        [Versioning("seating")]
        public SeatingType Seating { get; set; }

        [Versioning("type")]
        public CoachClass.ClassType Type { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        //public int Attendants { get; set; }

        //adds a facility to the route class
        public void AddFacility(RouteFacility facility)
        {
            if (facility != null)
            {
                if (Facilities.Exists(f => f.Type == facility.Type))
                {
                    Facilities.RemoveAll(f => f.Type == facility.Type);
                }

                Facilities.Add(facility);
            }
        }

        //returns the facility for a type for the route class

        //returns all facilities
        public List<RouteFacility> GetFacilities()
        {
            return Facilities;
        }

        public RouteFacility GetFacility(RouteFacility.FacilityType type)
        {
            return Facilities.Find(f => f.Type == type);
        }

        #endregion

        // chs, 2011-18-10 added seating type to a route coach class
    }
}