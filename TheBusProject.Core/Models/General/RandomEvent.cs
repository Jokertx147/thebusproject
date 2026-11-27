using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TheBusProject.Helpers;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General.Countries;
using TheBusProject.Models.Routes;

namespace TheBusProject.Models.General
{
    [Serializable]
    public class RandomEvent : BaseModel
    {
        #region Constructors and Destructors

        public RandomEvent(
            EventType type,
            Focus focus,
            int carrierSafety,
            int moneyEffect,
            string id,
            int frequency,
            DateTime stat,
            DateTime end,
            string name = "",
            string message = "",
            bool critical = false,
            int custHappiness = 0,
            int coachDamage = 0,
            int carrierSecurity = 0,
            int empHappiness = 0,
            double paxDemand = 1,
            int length = 1)
        {
            DateOccurred = GameObject.GetInstance().GameTime;
            CustomerHappinessEffect = custHappiness;
            CoachDamageEffect = coachDamage;
            CarrierSecurityEffect = carrierSecurity;
            EmployeeHappinessEffect = empHappiness;
            FinancialPenalty = 0;
            PaxDemandEffect = paxDemand;
            EffectLength = length;
            CriticalEvent = critical;
            EventName = name;
            EventMessage = message;
            Type = type;

            EventID = id;
        }

        private RandomEvent(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Enums

        public enum EventType
        {
            Safety,

            Security,

            Maintenance,

            Customer,

            Employee,

            Political
        }

        public enum Focus
        {
            Coach,

            Station,

            Carrier
        }

        #endregion

        #region Public Properties

        public int CoachDamageEffect { get; set; }

        [Versioning("carrier")]
        public Carrier Carrier { get; set; }

        public int CarrierSafetyEffect { get; set; }

        public int CarrierSecurityEffect { get; set; }

        [Versioning("coach")]
        public FleetCoach Coach { get; set; }

        [Versioning("station")]
        public Station Station { get; set; }


        [Versioning("country")]
        public Country Country { get; set; }

        public bool CriticalEvent { get; set; }

        public int CustomerHappinessEffect { get; set; }

        public DateTime DateOccurred { get; set; }

        //0-100

        //0-2

        public int EffectLength { get; set; }

        public int EmployeeHappinessEffect { get; set; }

        public DateTime End { get; set; }

        //should be defined in months

        public string EventID { get; set; }

        [Versioning("message")]
        public string EventMessage { get; set; }

        [Versioning("name")]
        public string EventName { get; set; }

        public int FinancialPenalty { get; set; }

        public int Frequency { get; set; }

        public double PaxDemandEffect { get; set; }

        [Versioning("route")]
        public Route Route { get; set; }

        //frequency per 3 years

        public DateTime Start { get; set; }

        [Versioning("type")]
        public EventType Type { get; set; }

        [Versioning("focus")]
        public Focus focus { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        public static void CheckExpired()
        {
            foreach (Carrier carrier in Carriers.Carriers.GetAllCarriers())
            {
                foreach (RandomEvent rEvent in carrier.EventLog)
                {
                    DateTime expDate = GameObject.GetInstance().GameTime.AddMonths(rEvent.EffectLength);
                    if (expDate < GameObject.GetInstance().GameTime)
                    {
                        PassengerHelpers.ChangePaxDemand(carrier, (1/rEvent.PaxDemandEffect));
                        RemoveEvent(carrier, rEvent);
                    }
                }
            }
        }

        public static void RemoveEvent(Carrier carrier, RandomEvent rEvent)
        {
            carrier.EventLog.Remove(rEvent);
        }

        public void AddEvent(Carrier carrier, RandomEvent rEvent)
        {
            carrier.EventLog.Add(rEvent);
        }

        public void ExecuteEvents(Carrier carrier, DateTime time)
        {
            var rnd = new Random();
            foreach (RandomEvent rEvent in carrier.EventLog)
            {
                if (rEvent.DateOccurred.DayOfYear == time.DayOfYear)
                {
                    rEvent.Coach.Coach.Condition += CoachDamageEffect;
                    carrier.Money += rEvent.FinancialPenalty;
                    carrier.Scores.CHR.Add(rEvent.CustomerHappinessEffect);
                    carrier.Scores.EHR.Add(rEvent.EmployeeHappinessEffect);
                    carrier.Scores.Safety.Add(rEvent.CarrierSafetyEffect);
                    carrier.Scores.Security.Add(rEvent.CarrierSecurityEffect);
                    PassengerHelpers.ChangePaxDemand(carrier, (rEvent.PaxDemandEffect*rnd.Next(9, 11)/10));
                }
            }
        }

        #endregion

        //checks if an event's effects are expired
    }

    public class RandomEvents
    {
        #region Static Fields

        private static Dictionary<string, RandomEvent> _events = new Dictionary<string, RandomEvent>();

        #endregion

        #region Public Methods and Operators

        public static void AddEvent(RandomEvent rEvent)
        {
            _events.Add(rEvent.EventName, rEvent);
        }

        public static void Clear()
        {
            _events = new Dictionary<string, RandomEvent>();
        }

        //gets a single event by name
        public static RandomEvent GetEvent(string name)
        {
            return _events[name];
        }

        //gets a list of all events
        public static List<RandomEvent> GetEvents()
        {
            return _events.Values.ToList();
        }

        //gets all events of a given type
        public static List<RandomEvent> GetEvents(RandomEvent.EventType type)
        {
            return GetEvents().FindAll((rEvent => rEvent.Type == type));
        }

        //gets x number of random events of a given type
        public static List<RandomEvent> GetEvents(RandomEvent.EventType type, int number, Carrier carrier)
        {
            var rnd = new Random();
            var rEvents = new Dictionary<int, RandomEvent>();
            List<RandomEvent> tEvents = GetEvents(type);
            int i = 1;
            int j = 0;
            foreach (RandomEvent r in tEvents)
            {
                if (r.Start <= GameObject.GetInstance().GameTime && r.End >= GameObject.GetInstance().GameTime)
                {
                    {
                        r.DateOccurred = MathHelpers.GetRandomDate(
                            GameObject.GetInstance().GameTime,
                            GameObject.GetInstance().GameTime.AddMonths(12));
                        r.Carrier = carrier;
                        r.Coach = CoachHelpers.GetRandomCoach(carrier);
                        r.Route = r.Coach.Routes[rnd.Next(r.Coach.Routes.Count())];
                        r.Country = r.Route.Destination1.Profile.Country;
                        r.Station = r.Route.Destination1;

                        if (r.focus == RandomEvent.Focus.Carrier)
                        {
                            r.Coach = null;
                            r.Station = null;
                            r.Country = null;
                            r.Route = null;
                        }

                        rEvents.Add(i, r);
                        i++;
                    }
                }
            }

            tEvents.Clear();

            while (j < number)
            {
                int item = rnd.Next(rEvents.Count());
                tEvents.Add(rEvents[item]);
                j++;
            }

            return tEvents;
        }

        #endregion
    }
}