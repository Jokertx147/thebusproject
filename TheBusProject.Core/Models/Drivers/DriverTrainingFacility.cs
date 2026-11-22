using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Carriers;

namespace TheBusProject.Models.Drivers
{
    //the facility for driver training
    [Serializable]
    public class DriverTrainingFacility : CarrierFacility
    {
        #region Constructors and Destructors

        public DriverTrainingFacility(
            string section,
            string uid,
            double price,
            double monthlyCost,
            int fromYear,
            int serviceLevel,
            int luxuryLevel,
            string coachfamily)
            : base(section, uid, price, monthlyCost, fromYear, serviceLevel, luxuryLevel)
        {
            CoachFamily = coachfamily;
        }

        private DriverTrainingFacility(SerializationInfo info, StreamingContext ctxt)
            : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("family")]
        public string CoachFamily { get; set; }

        public override string Name => CoachFamily;

        public override string Shortname => CoachFamily;

        #endregion

        #region Public Methods and Operators

        public new void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        #endregion
    }
}