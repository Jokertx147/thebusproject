using System.Collections.Generic;
using System.Linq;
using TheBusProject.Helpers;
using TheBusProject.Models.Carriers;

namespace TheBusProject.Models.General.Statistics
{
    public class Ratings
    {
        #region Public Methods and Operators

        public static double GetCustomerHappiness(Carrier carrier)
        {
            const int negInt = -1;
            Dictionary<Carrier, double> fillAverages = StatisticsHelpers.GetFillAverages();
            Dictionary<Carrier, double> onTimePercent = StatisticsHelpers.GetTotalOnTime();
            Dictionary<Carrier, double> ticketPPD = StatisticsHelpers.GetTotalPPD();
            IDictionary<Carrier, double> scaleAvgFill = StatisticsHelpers.GetRatingScale(fillAverages);
            IDictionary<Carrier, double> scaleOnTimeP = StatisticsHelpers.GetRatingScale(onTimePercent);
            IDictionary<Carrier, double> scalePPD = StatisticsHelpers.GetRatingScale(ticketPPD);
            IDictionary<Carrier, double> scaleLuxury = StatisticsHelpers.GetRatingScale(GetCarriersLuxuryLevels());

            double carrierAvgFill = scaleAvgFill[carrier];
            double carrierOTP = scaleOnTimeP[carrier];
            double carrierPPD = scalePPD[carrier];
            double carrierLuxury = scaleLuxury[carrier];

            return ((carrierPPD*negInt + 100)*0.4) + (carrierAvgFill*0.2) + (carrierOTP*0.2)
                   + (carrierLuxury*0.2);
        }

        //returns all carrier luxury levels

        //calculates employee happiness as a function of wages, discounts, and free drivers (relative to workload)
        public static double GetEmployeeHappiness(Carrier carrier)
        {
            Dictionary<Carrier, double> wages = StatisticsHelpers.GetEmployeeWages();
            Dictionary<Carrier, double> discounts = StatisticsHelpers.GetEmployeeDiscounts();
            Dictionary<Carrier, double> unassignedDrivers = StatisticsHelpers.GetUnassignedDrivers();
            IDictionary<Carrier, double> scaleWages = StatisticsHelpers.GetRatingScale(wages);
            IDictionary<Carrier, double> scaleDiscounts = StatisticsHelpers.GetRatingScale(discounts);
            IDictionary<Carrier, double> scaleUDrivers = StatisticsHelpers.GetRatingScale(unassignedDrivers);

            double carrierWages = scaleWages[carrier];
            double carrierDiscounts = scaleDiscounts[carrier];
            double carrierUnassignedDrivers = scaleUDrivers[carrier];

            return (carrierWages*0.7) + (carrierUnassignedDrivers*0.2) + (carrierDiscounts*0.1);
        }

        #endregion

        #region Methods

        private static double GetCarrierLuxuryLevel(Carrier carrier)
        {
            int luxuryLevel;

            if (carrier.Alliances.Count > 0)
            {
                luxuryLevel =
                    carrier.Alliances.SelectMany(a => a.Members)
                           .Select(m => m.Carrier)
                           .Max(m => carrier.Facilities.Sum(f => f.LuxuryLevel));
            }
            else
            {
                luxuryLevel = carrier.Facilities.Sum(f => f.LuxuryLevel);
            }
            return luxuryLevel;
        }

        private static Dictionary<Carrier, double> GetCarriersLuxuryLevels()
        {
            return Carriers.Carriers.GetAllCarriers().ToDictionary(carrier => carrier, GetCarrierLuxuryLevel);
        }

        #endregion

        //calculates customer happiness as a function of average ticket price, crowding on trips, and on-time % - for an carrier
    }
}