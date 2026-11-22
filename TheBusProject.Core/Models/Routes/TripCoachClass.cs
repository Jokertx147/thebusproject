using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;

namespace TheBusProject.Models.Routes
{
    /*! Trip coach class.
   * This class is used for an coach class onboard of a trip
   * The class needs parameters for type of class and the number of passengers
   */

    [Serializable]
    public class TripCoachClass : BaseModel
    {
        #region Constructors and Destructors

        public TripCoachClass(RouteCoachClass aClass, int passengers)
        {
            CoachClass = aClass;
            Passengers = passengers;
        }

        private TripCoachClass(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("class")]
        public RouteCoachClass CoachClass { get; set; }

        [Versioning("passengers")]
        public int Passengers { get; set; }

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