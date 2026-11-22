using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General.Statistics;

namespace TheBusProject.Models.Stations
{
    /*! Station statistics.
 * This is used for statistics for an station.
 * The class needs no parameters
 */

    [Serializable]
    public class StationStatistics : BaseModel
    {
        #region Constructors and Destructors

        public StationStatistics()
        {
            Stats = new List<StationStatisticsValue>();
        }

        private StationStatistics(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("stats")]
        public List<StationStatisticsValue> Stats { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        public void AddStatisticsValue(int year, Carrier carrier, StatisticsType type, int value)
        {
            lock (Stats)
            {
                StationStatisticsValue item =
                    Stats.Find(s => s != null && s.Year == year && s.Carrier == carrier && s.Stat.Shortname == type.Shortname);

                if (item == null)
                {
                    Stats.Add(new StationStatisticsValue(carrier, year, type, value));
                }
                else
                {
                    item.Value += value;
                }
            }
        }

        //returns the value for a statistics type for an carrier for a year
        public double GetStatisticsValue(int year, Carrier carrier, StatisticsType type)
        {
            lock (Stats)
            {
                StationStatisticsValue item =
                    Stats.Find(s => s != null && s.Year == year && s.Carrier == carrier && s.Stat.Shortname == type.Shortname);

                return item == null ? 0 : item.Value;
            }
        }

        public double GetTotalValue(int year, StatisticsType type)
        {
            lock (Stats)
            {
                return Stats.Where(s => s != null && s.Year == year && s.Stat.Shortname == type.Shortname).Sum(s => s.Value);
            }
        }

        //returns every year with statistics
        public List<int> GetYears()
        {
            lock (Stats)
            {
                return Stats.Where(s => s != null).Select(s => s.Year).Distinct().ToList();
            }
        }

        //adds the value for a statistics type to an carrier for a year

        //sets the value for a statistics type for an carrier for a year
        public void SetStatisticsValue(int year, Carrier carrier, StatisticsType type, int value)
        {
            lock (Stats)
            {
                StationStatisticsValue item =
                    Stats.Find(s => s != null && s.Year == year && s.Carrier == carrier && s.Stat.Shortname == type.Shortname);

                if (item == null)
                {
                    Stats.Add(new StationStatisticsValue(carrier, year, type, value));
                }
                else
                {
                    item.Value = value;
                }
            }
        }

        #endregion

        //returns the total value for a statistics type for a year
    }
}