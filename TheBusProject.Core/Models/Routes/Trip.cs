using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Stations;

namespace TheBusProject.Models.Routes
{
    /*! Trip.
  * This is used for a actually trip.
  * The class needs parameter for the time table entry which the trip trips after
  */

    [Serializable]
    public class Trip : BaseModel
    {
        #region Constructors and Destructors

        public Trip(RouteTimeTableEntry entry)
        {
            Entry = entry;

            if (Entry.TimeTable.Route.Type == Route.RouteType.Passenger)
            {
                Classes = new List<TripCoachClass>();

                if (Entry != null)
                {
                    Coach = Entry.Coach;
                    TripTime = MathHelpers.ConvertEntryToDate(Entry);
                    ScheduledTripTime = TripTime;
                }

                IsOnTime = true;
            }

            DistanceToDestination =
                MathHelpers.GetDistance(
                    Entry.Destination.Station.Profile.Coordinates.ConvertToGeoCoordinate(),
                    Entry.DepartureStation.Profile.Coordinates.ConvertToGeoCoordinate());
        }

        protected Trip(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("coach")]
        public FleetCoach Coach { get; set; }

        [Versioning("cargo")]
        public double Cargo { get; set; }

        [Versioning("classes")]
        public List<TripCoachClass> Classes { get; set; }

        [Versioning("distance")]
        public double DistanceToDestination { get; set; }

        [Versioning("entry")]
        public RouteTimeTableEntry Entry { get; set; }

        public DateTime ExpectedLanding => GetExpectedLandingTime();

        [Versioning("triptime")]
        public DateTime TripTime { get; set; }

        [Versioning("isontime")]
        public bool IsOnTime { get; set; }

        [Versioning("scheduled")]
        public DateTime ScheduledTripTime { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        //adds some delay minutes to the trip
        public virtual void AddDelayMinutes(int minutes)
        {
            TripTime = TripTime.AddMinutes(minutes);
        }

        //returns the scheduled expected landing time

        public Station GetDepartureStation()
        {
            return GetNextDestination();
        }

        public DateTime GetExpectedLandingTime()
        {
            return
                TripTime.Add(
                    MathHelpers.GetTripTime(
                        Entry.DepartureStation.Profile.Coordinates.ConvertToGeoCoordinate(),
                        Entry.Destination.Station.Profile.Coordinates.ConvertToGeoCoordinate(),
                        Coach.Coach.Type));
        }

        //returns the total number of passengers

        //returns the trip coach class for a specific class type
        public TripCoachClass GetTripCoachClass(CoachClass.ClassType type)
        {
            return Classes.Find(c => c.CoachClass.Type == type);
        }

        //returns the next destination
        public Station GetNextDestination()
        {
            return Entry.Destination.Station == Entry.TimeTable.Route.Destination1
                       ? Entry.TimeTable.Route.Destination2
                       : Entry.TimeTable.Route.Destination1;
        }

        public DateTime GetScheduledLandingTime()
        {
            return
                ScheduledTripTime.Add(
                    MathHelpers.GetTripTime(
                        Entry.DepartureStation.Profile.Coordinates.ConvertToGeoCoordinate(),
                        Entry.Destination.Station.Profile.Coordinates.ConvertToGeoCoordinate(),
                        Coach.Coach.Type));
        }

        public int GetTotalPassengers()
        {
            int passengers = Classes.Sum(aClass => aClass.Passengers);

            if (passengers < 0)
            {
                passengers = 0;
            }

            return passengers;
        }

        //returns the departure location

        public bool IsPassengerTrip()
        {
            return Entry.TimeTable.Route.Type == Route.RouteType.Passenger;
        }

        #endregion
    }
}