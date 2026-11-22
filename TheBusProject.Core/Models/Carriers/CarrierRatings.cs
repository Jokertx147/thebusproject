using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Carriers
{
    //the class for the ratings for an carrier
    [Serializable]
    public class CarrierRatings : BaseModel
    {
        #region Constructors and Destructors

        public CarrierRatings()
        {
            CustomerHappinessRating = 50;
            SafetyRating = 50;
            SecurityRating = 50;
            EmployeeHappinessRating = 50;
            MaintenanceRating = 50;
        }

        private CarrierRatings(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("customer")]
        public int CustomerHappinessRating { get; set; }

        [Versioning("employee")]
        public int EmployeeHappinessRating { get; set; }

        [Versioning("maintenance")]
        public int MaintenanceRating { get; set; }

        [Versioning("safety")]
        public int SafetyRating { get; set; }

        [Versioning("security")]
        public int SecurityRating { get; set; }

        /// <summary>True once the ratings have been worked out from the carrier's real record (see RatingsService).</summary>
        [Versioning("calculated")]
        public bool IsCalculated { get; set; }

        // the ratings a month ago, to show the change
        [Versioning("lastcustomer")]
        public int LastCustomerHappiness { get; set; } = 50;

        [Versioning("lastemployee")]
        public int LastEmployeeHappiness { get; set; } = 50;

        [Versioning("lastmaintenance")]
        public int LastMaintenance { get; set; } = 50;

        [Versioning("lastsafety")]
        public int LastSafety { get; set; } = 50;

        [Versioning("lastsecurity")]
        public int LastSecurity { get; set; } = 50;

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