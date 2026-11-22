using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.General.Statistics;

namespace TheBusProject.Models.Routes
{
    /*! Route statistics.
   * This is used for statistics for a route.
   * The class needs no parameters
   */

    [Serializable]
    public class RouteStatistics : BaseModel
    {
        #region Constructors and Destructors

        public RouteStatistics()
        {
            Stats = new List<RouteStatisticsItem>();
        }

        private RouteStatistics(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("stats")]
        public List<RouteStatisticsItem> Stats { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        // locked for the same reason as an carrier's statistics: a find, a change and a write that two threads
        // could interleave, losing what one of them counted
        public void AddStatisticsValue(RouteCoachClass aClass, StatisticsType type, int value)
        {
            lock (Stats)
            {
                RouteStatisticsItem item = Stats.Find(
                    i => i.Type.Shortname == type.Shortname && i.RouteClass == aClass);

                if (item == null)
                {
                    Stats.Add(new RouteStatisticsItem(aClass, type, value));
                }
                else
                {
                    item.Value += value;
                }
            }
        }

        public void AddStatisticsValue(StatisticsType type, int value)
        {
            var aClass = new RouteCoachClass(
                CoachClass.ClassType.EconomyClass,
                RouteCoachClass.SeatingType.FreeSeating,
                0);

            AddStatisticsValue(aClass, type, value);
        }

        //clears the list
        public void Clear()
        {
            Stats.Clear();
        }

        //returns the value for a statistics type for a route class
        public long GetStatisticsValue(RouteCoachClass aClass, StatisticsType type)
        {
            lock (Stats)
            {
                RouteStatisticsItem item =
                    Stats.Find(i => i.Type.Shortname == type.Shortname && i.RouteClass.Type == aClass.Type);

                return item == null ? 0 : item.Value;
            }
        }

        public long GetStatisticsValue(StatisticsType type)
        {
            var aClass = new RouteCoachClass(
                CoachClass.ClassType.EconomyClass,
                RouteCoachClass.SeatingType.FreeSeating,
                0);

            return GetStatisticsValue(aClass, type);
        }

        public long GetTotalValue(StatisticsType type)
        {
            long value;

            lock (Stats)
            {
                value = Stats.Where(s => s.Type.Shortname == type.Shortname).Sum(s => s.Value);
            }

            return value;
        }

        //adds the value for a statistics type to a route class

        //sets the value for a statistics type to a route class
        public void SetStatisticsValue(RouteCoachClass aClass, StatisticsType type, int value)
        {
            lock (Stats)
            {
                RouteStatisticsItem item = Stats.Find(
                    i => i.Type.Shortname == type.Shortname && i.RouteClass == aClass);

                if (item == null)
                {
                    Stats.Add(new RouteStatisticsItem(aClass, type, value));
                }
                else
                {
                    item.Value = value;
                }
            }
        }

        public void SetStatisticsValue(StatisticsType type, int value)
        {
            var aClass = new RouteCoachClass(
                CoachClass.ClassType.EconomyClass,
                RouteCoachClass.SeatingType.FreeSeating,
                0);

            SetStatisticsValue(aClass, type, value);
        }

        #endregion

        //returns the total value of a statistics type
    }

    /*
    public class RouteStatistics
    {
        
        private Dictionary<RouteCoachClass, Dictionary<StatisticsType, int>> Stats;

        public RouteStatistics()
        {
            this.Stats = new Dictionary<RouteCoachClass, Dictionary<StatisticsType, int>>();

        }
        //clears the statistics
        public void clear()
        {
            this.Stats = new Dictionary<RouteCoachClass, Dictionary<StatisticsType, int>>();
        }
        //returns the value for a statistics type for a route class
        public int getStatisticsValue(RouteCoachClass aClass, StatisticsType type)
        {
            if (this.Stats.ContainsKey(aClass) && this.Stats[aClass].ContainsKey(type))
                return this.Stats[aClass][type];
            else
                return 0;
        }
        public int getStatisticsValue(StatisticsType type)
        {
            RouteCoachClass aClass = new RouteCoachClass(CoachClass.ClassType.Economy_Class, RouteCoachClass.SeatingType.Free_Seating, 0);

            return getStatisticsValue(aClass, type);
        }
        //adds the value for a statistics type to a route class
        public void addStatisticsValue(RouteCoachClass aClass, StatisticsType type, int value)
        {
            lock (this.Stats)
            {
                if (!this.Stats.ContainsKey(aClass))
                    this.Stats.Add(aClass, new Dictionary<StatisticsType, int>());
                if (!this.Stats[aClass].ContainsKey(type))
                    this.Stats[aClass].Add(type, 0);
                this.Stats[aClass][type] += value;
            }
        }
        public void addStatisticsValue(StatisticsType type, int value)
        {
            RouteCoachClass aClass = new RouteCoachClass(CoachClass.ClassType.Economy_Class, RouteCoachClass.SeatingType.Free_Seating, 0);

            addStatisticsValue(aClass,type,value);
        }
        //sets the value for a statistics type to a route class
        public void setStatisticsValue(RouteCoachClass aClass, StatisticsType type, int value)
        {
            if (!this.Stats.ContainsKey(aClass))
                this.Stats.Add(aClass, new Dictionary<StatisticsType, int>());
            if (!this.Stats[aClass].ContainsKey(type))
                this.Stats[aClass].Add(type, value);
            else
                this.Stats[aClass][type] = value;
        }
        public void setStatisticsValue(StatisticsType type, int value)
        {
            RouteCoachClass aClass = new RouteCoachClass(CoachClass.ClassType.Economy_Class,RouteCoachClass.SeatingType.Free_Seating, 0);

            setStatisticsValue(aClass, type, value);
        }
        //returns the total value of a statistics type
        public int getTotalValue(StatisticsType type)
        {
            int value = 0;

            lock (this.Stats)
            {
                foreach (RouteCoachClass aClass in this.Stats.Keys)
                {
                    if (this.Stats[aClass].ContainsKey(type))
                        value += this.Stats[aClass][type];
                }
            }

            return value;
        }
    }
     * */
    //the statistics item for route
    [Serializable]
    public class RouteStatisticsItem
    {
        #region Constructors and Destructors

        public RouteStatisticsItem(RouteCoachClass routeClass, StatisticsType type, int value)
        {
            RouteClass = routeClass;
            Type = type;
            Value = value;
        }

        #endregion

        #region Public Properties

        public RouteCoachClass RouteClass { get; set; }

        public StatisticsType Type { get; set; }

        public long Value { get; set; }

        #endregion
    }
}