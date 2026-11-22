using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General.Countries;

namespace TheBusProject.Models.Passengers
{
    /*
     * The class for trip restrictions with no trips between two countries or unions
     */

    [Serializable]
    public class TripRestriction : BaseModel
    {
        #region Constructors and Destructors

        public TripRestriction(RestrictionType type, DateTime startDate, DateTime endDate, BaseUnit from, BaseUnit to)
        {
            Type = type;
            StartDate = startDate;
            EndDate = endDate;
            From = from;
            To = to;
        }

        private TripRestriction(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Enums

        public enum RestrictionType
        {
            Trips,

            Carriers
        }

        #endregion

        #region Public Properties

        [Versioning("enddate")]
        public DateTime EndDate { get; set; }

        [Versioning("from")]
        public BaseUnit From { get; set; }

        [Versioning("startdate")]
        public DateTime StartDate { get; set; }

        [Versioning("to")]
        public BaseUnit To { get; set; }

        [Versioning("type")]
        public RestrictionType Type { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        #endregion
    }

    //the list of trip restrictions
    public class TripRestrictions
    {
        #region Static Fields

        private static readonly List<TripRestriction> Restrictions = new List<TripRestriction>();

        #endregion

        #region Public Methods and Operators

        public static void AddRestriction(TripRestriction restriction)
        {
            Restrictions.Add(restriction);
        }

        //returns all restrictions

        //celars the list
        public static void Clear()
        {
            Restrictions.Clear();
        }

        public static List<TripRestriction> GetRestrictions()
        {
            return Restrictions;
        }

        //returns if there is trip restrictions from one country to another
        public static bool HasRestriction(
            Country from,
            Country to,
            DateTime date,
            TripRestriction.RestrictionType type)
        {
            TripRestriction restriction =
                GetRestrictions()
                    .Find(
                        r =>
                        (r.From == from || (r.From is Union && ((Union) r.From).IsMember(from, date)))
                        && (r.To == to || (r.To is Union && ((Union) r.To).IsMember(to, date)))
                        && (date >= r.StartDate && date <= r.EndDate) && r.Type == type);

            //TripRestriction res = GetRestrictions().Find(r => r.From == from && r.To == to && r.Type == type);

            return restriction != null;
        }

        //returns if there is trip restrictions between two countries
        public static bool HasRestriction(Country country1, Country country2, DateTime date)
        {
            return HasRestriction(country1, country2, date, TripRestriction.RestrictionType.Trips)
                   || HasRestriction(country2, country1, date, TripRestriction.RestrictionType.Trips);
        }

        //returns if there is trip restrictions for carriers to one of the destinations
        public static bool HasRestriction(Carrier carrier, Country dest1, Country dest2, DateTime date)
        {
            return HasRestriction(carrier.Profile.Country, dest2, date, TripRestriction.RestrictionType.Carriers)
                   || HasRestriction(carrier.Profile.Country, dest1, date, TripRestriction.RestrictionType.Carriers);
        }

        #endregion

        //adds a trip restriction to the list of restrictions
    }
}