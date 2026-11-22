using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;

namespace TheBusProject.Models.General.Scenarios
{
    //the class for an carrier (opponent) in a scenario
    [Serializable]
    public class ScenarioCarrier : BaseModel
    {
        #region Constructors and Destructors

        public ScenarioCarrier(Carrier carrier, Station homebase)
        {
            Carrier = carrier;
            Homebase = homebase;
            Routes = new List<ScenarioCarrierRoute>();
        }

        private ScenarioCarrier(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("carrier")]
        public Carrier Carrier { get; set; }

        [Versioning("homebase")]
        public Station Homebase { get; set; }

        [Versioning("routes")]
        public List<ScenarioCarrierRoute> Routes { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        public void AddRoute(ScenarioCarrierRoute route)
        {
            Routes.Add(route);
        }

        #endregion
    }

    [Serializable]
    //a route for an scenario carrier
    public class ScenarioCarrierRoute : BaseModel
    {
        #region Constructors and Destructors

        public ScenarioCarrierRoute(Station destination1, Station destination2, CoachType coachtype, int quantity)
        {
            Destination1 = destination1;
            Destination2 = destination2;
            CoachType = coachtype;
            Quantity = quantity;
        }

        private ScenarioCarrierRoute(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("coachtype")]
        public CoachType CoachType { get; set; }

        [Versioning("destination1")]
        public Station Destination1 { get; set; }

        [Versioning("destination2")]
        public Station Destination2 { get; set; }

        [Versioning("quantity")]
        public int Quantity { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        #endregion
    }
}