using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.General;

namespace TheBusProject.Models.Routes
{
    //the class for a stopover trip
    [Serializable]
    public class StopoverTrip : Trip
    {
        #region Constructors and Destructors

        public StopoverTrip(RouteTimeTableEntry entry)
            : base(entry)
        {
            CurrentTrip = 0;
            AllClasses = new Dictionary<RouteTimeTableEntry, List<TripCoachClass>>();
            AllCargo = new Dictionary<RouteTimeTableEntry, double>();

            List<Route> legs = entry.TimeTable.Route.Stopovers.SelectMany(s => s.Legs).ToList();

            bool isInbound = entry.DepartureStation == entry.TimeTable.Route.Destination2;

            if (isInbound)
            {
                legs.Reverse();
            }

            CreateEntries(entry);
            /*
            foreach (Route route in legs)
            {
                List<TripCoachClass> classes = new List<TripCoachClass>();
                foreach (CoachClass aClass in this.Coach.Coach.Classes)
                {
                    TripCoachClass faClass;
                     if (isInbound)
                         faClass = new TripCoachClass(route.getRouteCoachClass(aClass.Type), PassengerHelpers.GetStopoverTripPassengers(this.Coach,aClass.Type,route.Destination2,route.Destination1));
                     else
                         faClass = new TripCoachClass(route.getRouteCoachClass(aClass.Type), PassengerHelpers.GetStopoverTripPassengers(this.Coach,aClass.Type,route.Destination1,route.Destination2));
            
                     classes.Add(faClass);
                }
                this.AllClasses.Add(route, classes);
            }
             * */
        }

        private StopoverTrip(SerializationInfo info, StreamingContext ctxt)
            : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("allcargo")]
        public Dictionary<RouteTimeTableEntry, double> AllCargo { get; set; }

        [Versioning("allclasses")]
        public Dictionary<RouteTimeTableEntry, List<TripCoachClass>> AllClasses { get; set; }

        [Versioning("currenttrip")]
        public int CurrentTrip { get; set; }

        public bool IsLastTrip => CurrentTrip == Math.Max(AllClasses.Keys.Count, AllCargo.Keys.Count);

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        public override void AddDelayMinutes(int minutes)
        {
            base.AddDelayMinutes(minutes);

            if (IsPassengerTrip())
            {
                foreach (RouteTimeTableEntry e in AllClasses.Keys)
                {
                    e.Time = e.Time.Add(new TimeSpan(0, minutes, 0));
                }
            }

            if (IsCargoTrip())
            {
                foreach (RouteTimeTableEntry e in AllCargo.Keys)
                {
                    e.Time = e.Time.Add(new TimeSpan(0, minutes, 0));
                }
            }
        }

        //sets the next entry
        public void SetNextEntry()
        {
            RouteTimeTableEntry entry = null;

            if (IsPassengerTrip())
            {
                entry = AllClasses.Keys.ElementAt(CurrentTrip);
            }

            if (IsCargoTrip())
            {
                entry = AllCargo.Keys.ElementAt(CurrentTrip);
            }

            Entry = entry;

            if (entry != null && (entry.TimeTable.Route.Type == Route.RouteType.Mixed
                                  || entry.TimeTable.Route.Type == Route.RouteType.Passenger))
            {
                Classes = AllClasses[entry];
            }

            if (entry != null && (entry.TimeTable.Route.Type == Route.RouteType.Mixed
                                  || entry.TimeTable.Route.Type == Route.RouteType.Cargo))
            {
                Cargo = AllCargo[entry];
            }

            if (Entry != null)
            {
                Coach = Entry.Coach;

                TripTime = CurrentTrip == 0 ? MathHelpers.ConvertEntryToDate(Entry, 0) : GameObject.GetInstance().GameTime.Add(FleetCoachHelpers.GetMinTimeBetweenTrips(Coach));
            }

            IsOnTime = true;

            if (entry != null)
                DistanceToDestination =
                    MathHelpers.GetDistance(
                        entry.Destination.Station.Profile.Coordinates.ConvertToGeoCoordinate(),
                        entry.DepartureStation.Profile.Coordinates.ConvertToGeoCoordinate());

            CurrentTrip++;
        }

        #endregion

        #region Methods

        private void CreateEntries(RouteTimeTableEntry mainEntry)
        {
            List<Route> routes = mainEntry.TimeTable.Route.Stopovers.SelectMany(s => s.Legs).ToList();

            TimeSpan time = mainEntry.Time;

            bool isInbound = mainEntry.DepartureStation == mainEntry.TimeTable.Route.Destination2;

            if (isInbound)
            {
                routes.Reverse();
            }

            foreach (Route route in routes)
            {
                var timetable = new RouteTimeTable(route);

                RouteTimeTableEntry entry;
                //inbound
                if (isInbound)
                {
                    entry = new RouteTimeTableEntry(
                        timetable,
                        mainEntry.Day,
                        time,
                        new RouteEntryDestination(route.Destination1, mainEntry.Destination.TripCode));

                    time =
                        time.Add(entry.TimeTable.Route.GetTripTime(mainEntry.Coach.Coach.Type))
                            .Add(FleetCoachHelpers.GetMinTimeBetweenTrips(mainEntry.Coach));
                    entry.Coach = mainEntry.Coach;
                    entry.MainEntry = mainEntry;
                }
                    //outbound
                else
                {
                    entry = new RouteTimeTableEntry(
                        timetable,
                        mainEntry.Day,
                        time,
                        new RouteEntryDestination(route.Destination2, mainEntry.Destination.TripCode)) {Coach = mainEntry.Coach, MainEntry = mainEntry};

                    time =
                        time.Add(entry.TimeTable.Route.GetTripTime(mainEntry.Coach.Coach.Type))
                            .Add(FleetCoachHelpers.GetMinTimeBetweenTrips(mainEntry.Coach));
                }

                if (route.Type == Route.RouteType.Passenger || route.Type == Route.RouteType.Mixed)
                {
                    var classes = new List<TripCoachClass>();
                    foreach (CoachClass aClass in Coach.Coach.Classes)
                    {
                        TripCoachClass faClass;
                        if (isInbound)
                        {
                            faClass = new TripCoachClass(
                                ((PassengerRoute) route).GetRouteCoachClass(aClass.Type),
                                PassengerHelpers.GetStopoverTripPassengers(
                                    Coach,
                                    aClass.Type,
                                    route.Destination2,
                                    route.Destination1,
                                    routes,
                                    true));
                        }
                        else
                        {
                            faClass = new TripCoachClass(
                                ((PassengerRoute) route).GetRouteCoachClass(aClass.Type),
                                PassengerHelpers.GetStopoverTripPassengers(
                                    Coach,
                                    aClass.Type,
                                    route.Destination1,
                                    route.Destination2,
                                    routes,
                                    false));
                        }

                        classes.Add(faClass);
                    }

                    AllClasses.Add(entry, classes);
                }
                if (route.Type == Route.RouteType.Cargo || route.Type == Route.RouteType.Mixed)
                {
                    if (isInbound)
                    {
                        AllCargo.Add(
                            entry,
                            PassengerHelpers.GetStopoverTripCargo(
                                Coach,
                                route.Destination2,
                                route.Destination1,
                                routes,
                                true));
                    }
                    else
                    {
                        AllCargo.Add(
                            entry,
                            PassengerHelpers.GetStopoverTripCargo(
                                Coach,
                                route.Destination1,
                                route.Destination2,
                                routes,
                                false));
                    }
                }
            }
        }

        #endregion
    }
}