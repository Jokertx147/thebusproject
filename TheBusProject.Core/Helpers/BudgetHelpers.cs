using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General;

namespace TheBusProject.Helpers
{
    public class BudgetHelpers
    {
        #region Fields

/*
        private long _money = (long) GameObject.GetInstance().HumanCarrier.Money;
*/

        #endregion

        #region Public Methods and Operators

        public static double GetAvgFleetValue()
        {
            if (!GameObject.GetInstance().HumanCarrier.Fleet.Any())
            {
                return 0;
            }
            return GetFleetValue()/GameObject.GetInstance().HumanCarrier.Fleet.Count();
        }

        public static long GetAvgSubValue(Carrier carrier)
        {
            if (!carrier.Subsidiaries.Any())
            {
                return 0;
            }

            return GetTotalSubValues(GameObject.GetInstance().HumanCarrier)
                   /GameObject.GetInstance().HumanCarrier.Subsidiaries.Count();
        }

        //returns the remaining budget amount based on the current month

        //returns the estimated end of year cash
        public static long GetEndYearCash(long budget, long money)
        {
            Carrier humanCarrier = GameObject.GetInstance().HumanCarrier;

            long endYearCash = money - GetRemainingBudget() - (humanCarrier.Budget.TotalBudget*15/100);
            return endYearCash;
        }

        public static long GetFleetValue()
        {
            var values = GameObject.GetInstance().HumanCarrier.Fleet.Select(coach => coach.Coach.Price).ToList();
            return values.Sum();
        }

        public static CarrierBudget GetOneYearBudget(DateTime date, int n)
        {
            Carrier humanCarrier = GameObject.GetInstance().HumanCarrier;
            if (GameObject.GetInstance().StartDate > GameObject.GetInstance().GameTime.AddMonths(-12))
            {
                CarrierBudget oldBudget =
                    (from d in humanCarrier.TestBudget
                     where (d.Key.Month == date.Month) && (d.Key.Year == (date.Year - n))
                     select d.Value).FirstOrDefault();
                return oldBudget;
            }
            return humanCarrier.Budget;
        }

        public static long GetRemainingBudget()
        {
            return (1 - GameObject.GetInstance().GameTime.Month/12)
                   *GameObject.GetInstance().HumanCarrier.Budget.TotalBudget;
        }

        public static IDictionary<DateTime, CarrierBudget> GetTestBudget(Carrier carrier)
        {
            CarrierBudget budget = GameObject.GetInstance().HumanCarrier.Budget;
            var random = new Random();
            for (int i = 1; i < 61; i++)
            {
                budget.BudgetActive = GameObject.GetInstance().GameTime.AddMonths(-1);
                budget.BudgetExpires = GameObject.GetInstance().GameTime.AddMonths(-1 + 12);
                budget.Cash = random.Next(1, 100000000);
                budget.StationBudget = random.Next(1, 500000);
                budget.CompBudget = random.Next(1, 500000);
                budget.CSBudget = random.Next(1, 2000000);
                budget.EndYearCash = random.Next(1, 100000000);
                budget.EnginesBudget = random.Next(1, 1000000);
                budget.EquipmentBudget = random.Next(1, 500000);
                budget.FleetSize = random.Next(1, 250);
                budget.FleetValue = random.Next(10, 10000);
                budget.InTripBudget = random.Next(1, 500000);
                budget.InternetBudget = random.Next(1, 500000);
                budget.ITBudget = random.Next(1, 500000);
                budget.MaintenanceBudget = random.Next(1, 4000000);
                budget.MarketingBudget = random.Next(1, 2000000);
                budget.OverhaulBudget = random.Next(1, 1000000);
                budget.PartsBudget = random.Next(1, 1000000);
                budget.PRBudget = random.Next(1, 500000);
                budget.PrintBudget = random.Next(1, 500000);
                budget.PromoBudget = random.Next(1, 500000);
                budget.RadioBudget = random.Next(1, 500000);
                budget.RemainingBudget = random.Next(1, 10000000);
                budget.RemoteBudget = random.Next(1, 1000000);
                budget.SecurityBudget = random.Next(1, 2000000);
                budget.ServCenterBudget = random.Next(1, 500000);
                budget.Subsidiaries = random.Next(0, 4);
                budget.TelevisionBudget = random.Next(1, 500000);
                budget.TotalBudget = random.Next(4, 10000000);
                budget.TotalEmployees = random.Next(1, 10000);
                budget.TotalPayroll = random.Next(500000, 10000000);
                budget.TotalSubValue = random.Next(200000000, 1000000000);
                if (carrier.TestBudget.ContainsKey(budget.BudgetActive.AddMonths(-1)))
                {
                }
                else
                {
                    carrier.TestBudget.Add(budget.BudgetActive.AddMonths(-1), budget);
                }
            }
            return carrier.TestBudget;
        }

        public static long GetTotalSubValues(Carrier carrier)
        {
            return (from Carrier subsidiary in carrier.Subsidiaries select 10000*(int) StatisticsHelpers.GetWorldStationsServed() into stationValue select (GetFleetValue() + stationValue)).Sum();
        }

        //sets default values and max values
        public static long SetDefaults(Carrier humanCarrier)
        {
            humanCarrier.Budget.MarketingBudget =
                humanCarrier.Budget.CSBudget = humanCarrier.Budget.SecurityBudget = (long) (humanCarrier.Money*0.05);
            humanCarrier.Budget.MaintenanceBudget = (long) (humanCarrier.Money*0.1);
            humanCarrier.Budget.PrintBudget =
                humanCarrier.Budget.TelevisionBudget =
                humanCarrier.Budget.RadioBudget =
                humanCarrier.Budget.InternetBudget = (humanCarrier.Budget.MarketingBudget/4);
            humanCarrier.Budget.OverhaulBudget =
                humanCarrier.Budget.PartsBudget =
                humanCarrier.Budget.RemoteBudget =
                humanCarrier.Budget.EnginesBudget = humanCarrier.Budget.MaintenanceBudget/4;
            humanCarrier.Budget.ITBudget =
                humanCarrier.Budget.EquipmentBudget =
                humanCarrier.Budget.StationBudget =
                humanCarrier.Budget.InTripBudget = humanCarrier.Budget.SecurityBudget/4;
            humanCarrier.Budget.PRBudget =
                humanCarrier.Budget.PromoBudget =
                humanCarrier.Budget.CompBudget =
                humanCarrier.Budget.ServCenterBudget = humanCarrier.Budget.CSBudget/4;
            return humanCarrier.Budget.MarketingBudget + humanCarrier.Budget.MaintenanceBudget
                   + humanCarrier.Budget.SecurityBudget + humanCarrier.Budget.CSBudget;
        }

        //verifies to make sure sub-budgets don't exceed master budget
        public static void VerifyValues(CarrierBudget budget)
        {
            long userMarketingBudget = budget.RadioBudget + budget.TelevisionBudget + budget.PrintBudget
                                       + budget.InternetBudget;
            if (userMarketingBudget > budget.MarketingBudget)
            {
                budget.RadioBudget =
                    budget.TelevisionBudget = budget.PrintBudget = budget.InternetBudget = budget.MarketingBudget/4;
            }

            long userMaintBudget = budget.PartsBudget + budget.EnginesBudget + budget.OverhaulBudget
                                   + budget.RemoteBudget;
            if (userMaintBudget > budget.MaintenanceBudget)
            {
                budget.PartsBudget =
                    budget.EnginesBudget = budget.OverhaulBudget = budget.RemoteBudget = budget.MaintenanceBudget/4;
            }

            long userCSBudget = budget.ServCenterBudget + budget.CompBudget + budget.PromoBudget + budget.PRBudget;
            if (userCSBudget > budget.CSBudget)
            {
                budget.ServCenterBudget = budget.CompBudget = budget.PromoBudget = budget.PRBudget = budget.CSBudget/4;
            }

            long userSecurityBudget = budget.StationBudget + budget.EquipmentBudget + budget.InTripBudget
                                      + budget.ITBudget;
            if (userSecurityBudget > budget.SecurityBudget)
            {
                budget.StationBudget =
                    budget.EquipmentBudget = budget.InTripBudget = budget.ITBudget = budget.SecurityBudget/4;
            }
        }

        #endregion

        //gets the total value of all coach in fleet combined

        //gets the average value of an coach in the fleet

        //returns the budget from 1 year ago
    }
}