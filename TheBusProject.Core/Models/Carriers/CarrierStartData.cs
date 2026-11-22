using System.Collections.Generic;
using TheBusProject.Helpers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.General.Countries;
using TheBusProject.Models.Routes;

namespace TheBusProject.Models.Carriers
{
    //the start data for an carrier
    public class CarrierStartData
    {
        #region Constructors and Destructors

        public CarrierStartData(Carrier carrier)
        {
            Carrier = carrier;
            Routes = new List<StartDataRoute>();
            Coaches = new List<StartDataCoaches>();
            OriginRoutes = new List<StartDataRoutes>();
        }

        #endregion

        #region Public Properties

        public Carrier Carrier { get; set; }

        public List<StartDataCoaches> Coaches { get; }

        public List<StartDataRoutes> OriginRoutes { get; }

        public List<StartDataRoute> Routes { get; }

        #endregion

        #region Public Methods and Operators

        public void AddCoaches(StartDataCoaches coaches)
        {
            Coaches.Add(coaches);
        }

        //adds origin routes to the list
        public void AddOriginRoutes(StartDataRoutes route)
        {
            OriginRoutes.Add(route);
        }

        //adds a route to the list
        public void AddRoute(StartDataRoute route)
        {
            Routes.Add(route);
        }

        #endregion

        //adds an coaches to the list
    }

    //the coaches for the start data
    public class StartDataCoaches
    {
        #region Constructors and Destructors

        public StartDataCoaches(string type, int coachesEarly, int coachesLate)
        {
            Type = type;
            CoachesEarly = coachesEarly;
            CoachesLate = coachesLate;
        }

        #endregion

        #region Public Properties

        public int CoachesEarly { get; set; }

        public int CoachesLate { get; set; }

        public string Type { get; set; }

        #endregion
    }

    //the routes for the start data
    public class StartDataRoutes
    {
        #region Constructors and Destructors

        public StartDataRoutes(
            string origin,
            int destinations,
            GeneralHelpers.Size minimumsize,
            Route.RouteType routetype)
        {
            Countries = new List<Country>();
            Origin = origin;
            Destinations = destinations;
            MinimumSize = minimumsize;
            RouteType = routetype;
        }

        #endregion

        #region Public Properties

        public List<Country> Countries { get; set; }

        public int Destinations { get; set; }

        public GeneralHelpers.Size MinimumSize { get; set; }

        public string Origin { get; set; }

        public Route.RouteType RouteType { get; set; }

        #endregion

        #region Public Methods and Operators

        public void AddCountry(Country country)
        {
            Countries.Add(country);
        }

        #endregion

        //adds a country to the routes
    }

    //the route for the start data
    public class StartDataRoute
    {
        #region Constructors and Destructors

        public StartDataRoute(
            string destination1,
            string destination2,
            int opened,
            int closed,
            Route.RouteType routetype)
        {
            Opened = opened;
            Closed = closed;
            Destination1 = destination1;
            Destination2 = destination2;
            RouteType = routetype;
        }

        #endregion

        #region Public Properties

        public int Closed { get; set; }

        public string Destination1 { get; set; }

        public string Destination2 { get; set; }

        public int Opened { get; set; }

        public Route.RouteType RouteType { get; set; }

        public CoachType Type { get; set; }

        #endregion
    }

    //the list of start data
    public class CarrierStartDatas
    {
        #region Static Fields

        private static readonly List<CarrierStartData> StartData = new List<CarrierStartData>();

        #endregion

        #region Public Methods and Operators

        public static void Clear()
        {
            StartData.Clear();
        }

        public static void AddStartData(CarrierStartData data)
        {
            StartData.Add(data);
        }

        //returns the start data for an carrier
        public static CarrierStartData GetCarrierStartData(Carrier carrier)
        {
            return StartData.Find(s => s.Carrier == carrier);
        }

        #endregion

        //adds start data to the list
    }
}