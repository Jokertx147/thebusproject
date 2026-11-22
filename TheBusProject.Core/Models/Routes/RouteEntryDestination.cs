using System;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Stations;

namespace TheBusProject.Models.Routes
{
    /*! RouteEntryDestination.
  * This is used for destination for the route.
  * The class needs parameter for the destination station and the trip code
  */

    [Serializable]
    public class RouteEntryDestination : BaseModel, IComparable<RouteEntryDestination>
    {
        #region Constructors and Destructors

        public RouteEntryDestination(Station station, string tripCode)
            : this(station, tripCode, null)
        {
        }

        public RouteEntryDestination(Station station, string tripCode, Gate inboundgate)
        {
            Station = station;
            TripCode = tripCode;
            Gate = inboundgate;
        }

        private RouteEntryDestination(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("station")]
        public Station Station { get; set; }

        [Versioning("tripcode")]
        public string TripCode { get; set; }

        [Versioning("gate")]
        public Gate Gate { get; set; }

        #endregion

        #region Public Methods and Operators

        public int CompareTo(RouteEntryDestination entry)
        {
            int compare = string.Compare(entry.TripCode, TripCode, StringComparison.Ordinal);
            if (compare == 0)
            {
                return string.Compare(entry.Station.Profile.IATACode, Station.Profile.IATACode, StringComparison.Ordinal);
            }
            return compare;
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        #endregion
    }
}