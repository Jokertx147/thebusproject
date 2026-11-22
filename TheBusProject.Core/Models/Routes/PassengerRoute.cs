using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General.Statistics;

namespace TheBusProject.Models.Routes
{
    [Serializable]
    //the class for a passenger route
    public class PassengerRoute : Route
    {
        #region Constructors and Destructors

        public PassengerRoute(
            string id,
            Station destination1,
            Station destination2,
            DateTime startDate,
            double farePrice)
            : base(RouteType.Passenger, id, destination1, destination2, startDate)
        {
            Classes = new List<RouteCoachClass>();

            foreach (CoachClass.ClassType ctype in Enum.GetValues(typeof (CoachClass.ClassType)))
            {
                var cl = new RouteCoachClass(ctype, RouteCoachClass.SeatingType.ReservedSeating, farePrice);

                Classes.Add(cl);
            }
        }

        protected PassengerRoute(SerializationInfo info, StreamingContext ctxt)
            : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("classes")]
        public List<RouteCoachClass> Classes { get; set; }

        public double IncomePerPassenger => GetIncomePerPassenger();

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        //adds a route coach class to the route
        public void AddRouteCoachClass(RouteCoachClass aClass)
        {
            Classes.Add(aClass);
        }

        public double GetFarePrice(CoachClass.ClassType type)
        {
            return Classes.Find(c => c.Type == type).FarePrice;
        }

        public override double GetFillingDegree()
        {
            if (HasStopovers)
            {
                IEnumerable<Route> legs = Stopovers.SelectMany(s => s.Legs);
                Route[] enumerable = legs as Route[] ?? legs.ToArray();
                double fillingDegree = enumerable.Cast<PassengerRoute>().Sum(leg => leg.GetFillingDegree());
                return fillingDegree/enumerable.Count();
            }
            double passengers =
                Convert.ToDouble(Statistics.GetTotalValue(StatisticsTypes.GetStatisticsType("Passengers")));

            double passengerCapacity =
                Convert.ToDouble(Statistics.GetTotalValue(StatisticsTypes.GetStatisticsType("Capacity")));

            return passengers/passengerCapacity;
        }

        //returns the route coach class for a specific class type
        public RouteCoachClass GetRouteCoachClass(CoachClass.ClassType type)
        {
            return Classes.Find(cl => cl.Type == type);
        }

        public double GetServiceLevel(CoachClass.ClassType type)
        {
            return Classes.Find(c => c.Type == type).GetFacilities().Sum(f => f.ServiceLevel);
        }

        //returns the total number of cabin crew for the route based on coach
        public int GetTotalAttendants()
        {
            int cabinCrew = 0;

            if (GetCoaches().Count > 0)
            {
                cabinCrew = GetCoaches().Sum(a => ((CoachPassengerType) a.Coach.Type).Attendants);
            }

            return cabinCrew;
        }

        #endregion

        #region Methods

        private double GetIncomePerPassenger()
        {
            double totalPassengers =
                Convert.ToDouble(Statistics.GetTotalValue(StatisticsTypes.GetStatisticsType("Passengers")));

            return Balance/totalPassengers;
        }

        #endregion

        //public FleetCoach Coach { get; set; }

        //get the degree of filling

        //gets the income per passenger

        //returns the service level for a specific class
    }

    [Serializable]
    //the class for a helicopter route
    // TODO - Move into its own file - Shouldn't be declared in the same file as another class
    // TODO - Subtype directly from Route. Possibly create enum for route type.
    public class HelicopterRoute : PassengerRoute
    {
        public HelicopterRoute(
            string id,
            Station destination1,
            Station destination2,
            DateTime startDate,
            double farePrice)
            : base(id, destination1, destination2, startDate, farePrice)
        {
            Type = RouteType.Helicopter;
        }

        protected HelicopterRoute(SerializationInfo info, StreamingContext ctxt)
            : base(info, ctxt)
        {
        }

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        #endregion
    }
}