using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;

namespace TheBusProject.Models.General.Scenarios
{
    //the class for a scenario
    [Serializable]
    public class Scenario : BaseModel
    {
        #region Constructors and Destructors

        public Scenario(
            string name,
            string description,
            Carrier carrier,
            Station homebase,
            int startyear,
            int endyear,
            long startcash,
            DifficultyLevel difficulty)
        {
            Name = name;
            Carrier = carrier;
            Homebase = homebase;
            StartYear = startyear;
            StartCash = startcash;
            Description = description;
            Difficulty = difficulty;
            EndYear = endyear;
            OpponentCarriers = new List<ScenarioCarrier>();
            Destinations = new List<Station>();
            Fleet = new Dictionary<CoachType, int>();
            Routes = new List<ScenarioCarrierRoute>();
            Failures = new List<ScenarioFailure>();
            PassengerDemands = new List<ScenarioPassengerDemand>();
        }

        private Scenario(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("carrier")]
        public Carrier Carrier { get; set; }

        [Versioning("description")]
        public string Description { get; set; }

        [Versioning("destinations")]
        public List<Station> Destinations { get; set; }

        [Versioning("difficulty")]
        public DifficultyLevel Difficulty { get; set; }

        [Versioning("endyear")]
        public int EndYear { get; set; }

        [Versioning("failures")]
        public List<ScenarioFailure> Failures { get; set; }

        [Versioning("fleet")]
        public Dictionary<CoachType, int> Fleet { get; set; }

        [Versioning("homebase")]
        public Station Homebase { get; set; }

        [Versioning("name")]
        public string Name { get; set; }

        [Versioning("opponents")]
        public List<ScenarioCarrier> OpponentCarriers { get; set; }

        [Versioning("demands")]
        public List<ScenarioPassengerDemand> PassengerDemands { get; set; }

        [Versioning("routes")]
        public List<ScenarioCarrierRoute> Routes { get; set; }

        [Versioning("startcash")]
        public long StartCash { get; set; }

        [Versioning("startyear")]
        public int StartYear { get; set; }

        /// <summary>The message when the scenario is won (from the scenario file).</summary>
        [Versioning("successtext")]
        public string SuccessText { get; set; }

        /// <summary>The debt the carrier starts with (some scenarios start in debt).</summary>
        [Versioning("startdebt")]
        public long StartDebt { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        //adds a destinaton to the scenario
        public void AddDestination(Station destination)
        {
            Destinations.Add(destination);
        }

        public void AddFleet(CoachType type, int quantity)
        {
            Fleet.Add(type, quantity);
        }

        //adds an opponent carrier to the scenario
        public void AddOpponentCarrier(ScenarioCarrier carrier)
        {
            OpponentCarriers.Add(carrier);
        }

        public void AddPassengerDemand(ScenarioPassengerDemand demand)
        {
            PassengerDemands.Add(demand);
        }

        //adds a route to the scenario
        public void AddRoute(ScenarioCarrierRoute route)
        {
            Routes.Add(route);
        }

        //adds a failure to the scenario
        public void AddScenarioFailure(ScenarioFailure failure)
        {
            Failures.Add(failure);
        }

        #endregion
    }

    //the list of scenario
    public class Scenarios
    {
        #region Static Fields

        private static readonly List<Scenario> scenarios = new List<Scenario>();

        #endregion

        #region Public Methods and Operators

        public static void AddScenario(Scenario scenario)
        {
            scenarios.Add(scenario);
        }

        //returns all scenarios

        //clears the list of scenarios
        public static void Clear()
        {
            scenarios.Clear();
        }

        //returns the number of scenarios
        public static int GetNumberOfScenarios()
        {
            return scenarios.Count;
        }

        public static Scenario GetScenario(string name)
        {
            return scenarios.Find(s => s.Name == name);
        }

        public static List<Scenario> GetScenarios()
        {
            return scenarios;
        }

        #endregion

        //adds a scenario to the list
    }
}