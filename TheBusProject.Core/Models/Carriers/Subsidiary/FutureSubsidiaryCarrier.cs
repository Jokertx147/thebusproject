using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Infrastructure.Enums;
using TheBusProject.Models.Stations;
using TheBusProject.Models.Routes;

namespace TheBusProject.Models.Carriers.Subsidiary
{
    [Serializable]
    //the class for a future subsidiary carrier for an carrier
    public class FutureSubsidiaryCarrier : BaseModel
    {
        #region Constructors and Destructors

        public FutureSubsidiaryCarrier(
            string name,
            string iata,
            Station station,
            Carrier.CarrierMentality mentality,
            CarrierFocus market,
            Route.RouteType carrierRouteFocus,
            string logo)
        {
            Name = name;
            IATA = iata;
            PreferedStation = station;
            Mentality = mentality;
            Market = market;
            Logo = logo;
            CarrierRouteFocus = carrierRouteFocus;
        }

        private FutureSubsidiaryCarrier(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("focus")]
        public Route.RouteType CarrierRouteFocus { get; set; }

        [Versioning("IATA")]
        public string IATA { get; set; }

        [Versioning("logo")]
        public string Logo { get; set; }

        [Versioning("market")]
        public CarrierFocus Market { get; set; }

        [Versioning("mentality")]
        public Carrier.CarrierMentality Mentality { get; set; }

        [Versioning("name")]
        public string Name { get; set; }

        [Versioning("station")]
        public Station PreferedStation { get; set; }

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