using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General;

namespace TheBusProject.Helpers
{
    //the helpers class for events
    public class EventsHelpers
    {
        #region Public Methods and Operators

        public static void GenerateEvents(Carrier carrier)
        {
            var rnd = new Random();
            var eventOccurences = new Dictionary<RandomEvent.EventType, double>();
            int eFreq;

            //sets an overall event frequency based on an carriers total overall rating
            int totalRating = carrier.Ratings.CustomerHappinessRating + carrier.Ratings.EmployeeHappinessRating
                              + carrier.Ratings.SafetyRating + carrier.Ratings.SecurityRating;
            if (totalRating < 300)
            {
                eFreq = rnd.Next(1, 6);
            }
            else if (totalRating < 200)
            {
                eFreq = rnd.Next(4, 10);
            }
            else if (totalRating < 100)
            {
                eFreq = rnd.Next(8, 16);
            }
            else
            {
                eFreq = rnd.Next(0, 4);
            }

            //gets the event proportions and multiplies them by total # events to get events per type
            List<double> probs = GetEventProportions(carrier);
            double custEvents = eFreq*probs[0];
            double empEvents = eFreq*probs[1];
            double secEvents = eFreq*probs[2];
            double safEvents = eFreq*probs[3];
            double maintEvents = eFreq*probs[4];
            double polEvents = eFreq - custEvents - empEvents - secEvents - maintEvents;
            eventOccurences.Add(RandomEvent.EventType.Customer, custEvents);
            eventOccurences.Add(RandomEvent.EventType.Employee, empEvents);
            eventOccurences.Add(RandomEvent.EventType.Maintenance, maintEvents);
            eventOccurences.Add(RandomEvent.EventType.Safety, safEvents);
            eventOccurences.Add(RandomEvent.EventType.Security, secEvents);
            eventOccurences.Add(RandomEvent.EventType.Political, polEvents);

            /*
            //generates the given number of events for each type
            foreach (KeyValuePair<RandomEvent.EventType, double> v in eventOccurences)
            {
                int k = (int)v.Value;
                List<RandomEvent> list = RandomEvents.GetEvents(v.Key, k, carrier);
                foreach (RandomEvent e in list)
                {
                    carrier.EventLog.Add(e);
                }
            }
            */
        }

        public static List<double> GetEventProportions(Carrier carrier)
        {
            //chr 0 ehr 1 scr 2 sfr 3 total 4
            var ratings = new List<int>
                {
                    100 - carrier.Ratings.CustomerHappinessRating,
                    100 - carrier.Ratings.EmployeeHappinessRating,
                    100 - carrier.Ratings.SecurityRating,
                    100 - carrier.Ratings.SafetyRating,
                    100 - carrier.Ratings.MaintenanceRating
                };
            ratings.Add(500 - ratings.Sum());
            double pCHR = ratings[0]/ratings[5];
            double pEHR = ratings[1]/ratings[5];
            double pSCR = ratings[2]/ratings[5];
            double pSFR = ratings[3]/ratings[5];
            double pMTR = ratings[4]/ratings[5];
            ratings.Clear();
            var pRatings = new List<double> {pCHR, pEHR, pSCR, pSFR, pMTR};
            return pRatings;
        }

        #endregion

        //returns a list of proportions of events based on current ratings

        //generates x number of events for each event type for the current year. Should be called only from OnNewYear
    }
}