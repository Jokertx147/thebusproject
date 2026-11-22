using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Infrastructure.Enums;
using TheBusProject.Models.Routes;

namespace TheBusProject.Models.Carriers.Subsidiary
{
    //the class for a subsidiary carrier for an carrier

    // TODO - Replace with a proper reference to an carrier object with a subsidiary flag instead of a class. Improper OO design.
    [Serializable]
    public class SubsidiaryCarrier : Carrier
    {
        #region Constructors and Destructors

        public SubsidiaryCarrier(
            Carrier carrier,
            CarrierProfile profile,
            CarrierMentality mentality,
            CarrierFocus market,
            CarrierLicense license,
            Route.RouteType routefocus)
            : base(profile, mentality, market, license, routefocus)
        {
            Carrier = carrier;

            foreach (CarrierLogo logo in Carrier.Profile.Logos)
            {
                Profile.AddLogo(logo);
            }
        }

        private SubsidiaryCarrier(SerializationInfo info, StreamingContext ctxt)
            : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("carrier")]
        public Carrier Carrier { get; set; }

        /// <summary>
        ///     True when the subsidiary is run by its own management (the computer) even though it belongs to the
        ///     player. The player's screens show the main carrier only, so a player-run subsidiary would stand still.
        /// </summary>
        [Versioning("automanaged")]
        public bool AutoManaged { get; set; } = true;

        #endregion

        #region Public Methods and Operators

        public new void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        public override bool isHuman()
        {
            return Carrier != null && Carrier.isHuman() && !AutoManaged;
        }

        public override bool IsSubsidiaryCarrier()
        {
            return Carrier != null;
        }

        #endregion
    }
}