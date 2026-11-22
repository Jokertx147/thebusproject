using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.General;
using TheBusProject.Models.Drivers;
using TheBusProject.Models.Carriers;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     Driver pay and morale. The carrier sets a pay level (percent of the market salary). Every month each driver's morale moves
    ///     toward a target that depends on pay, whether he has an coach, and his time with the carrier. Unhappy drivers quit.
    ///     A one-off bonus lifts everybody's morale.
    /// </summary>
    public static class DriverMoraleService
    {
        public const int MinPay = 70;
        public const int MaxPay = 150;
        public const int BonusWaitDays = 120;
        // Random.Shared is safe to use from the parallel carrier threads (a shared new Random() is not)
        private static Random Rnd => Random.Shared;

        public static double PayFactor(Carrier carrier)
        {
            return carrier.DriverPayPercent <= 0 ? 1.0 : carrier.DriverPayPercent/100.0;
        }

        public static string MoodText(double morale)
        {
            if (morale >= 85) return "Excellent";
            if (morale >= 65) return "Good";
            if (morale >= 45) return "Okay";
            if (morale >= 30) return "Low";
            return "About to quit";
        }

        /// <summary>The monthly pay of all the carrier's drivers at a pay level.</summary>
        public static double Payroll(Carrier carrier, int percent)
        {
            double sum = 0;
            foreach (Driver p in carrier.Drivers.ToList())
            {
                sum += CarrierHelpers.GetDriverSalary(carrier, p)/PayFactor(carrier)*percent/100.0;
            }
            return sum;
        }

        public static bool SetPay(Carrier carrier, int percent, out string message)
        {
            percent = Math.Max(MinPay, Math.Min(MaxPay, percent));
            carrier.DriverPayPercent = percent;
            message = "Drivers are now paid " + percent + "% of the market salary.";
            return true;
        }

        public static double BonusCost(Carrier carrier)
        {
            return Payroll(carrier, carrier.DriverPayPercent <= 0 ? 100 : (int) Math.Round(carrier.DriverPayPercent));
        }

        public static int DaysToNextBonus(Carrier carrier)
        {
            double d = BonusWaitDays - (GameObject.GetInstance().GameTime.Date - carrier.LastDriverBonus.Date).TotalDays;
            return carrier.LastDriverBonus == DateTime.MinValue ? 0 : (int) Math.Max(0, Math.Ceiling(d));
        }

        public static bool PayBonus(Carrier carrier, out string message)
        {
            GameObject game = GameObject.GetInstance();
            if (carrier.Drivers.Count == 0)
            {
                message = "You have no drivers.";
                return false;
            }
            int wait = DaysToNextBonus(carrier);
            if (wait > 0)
            {
                message = "A bonus was paid recently. Wait " + wait + " more days or it stops meaning anything.";
                return false;
            }
            double cost = BonusCost(carrier);
            if (carrier.Money < cost)
            {
                message = "Not enough money: the bonus costs " + cost.ToString("N0") + ".";
                return false;
            }
            CarrierHelpers.AddCarrierInvoice(carrier, game.GameTime, TheBusProject.Models.General.Finances.Invoice.InvoiceType.Wages, -cost);
            foreach (Driver p in carrier.Drivers.ToList()) p.Morale = Math.Min(100, p.Morale + 15);
            carrier.LastDriverBonus = game.GameTime.Date;
            message = "Every driver got a month's pay as a bonus (" + cost.ToString("N0") + "). Morale is up.";
            return true;
        }

        public static double Target(Carrier carrier, Driver p)
        {
            GameObject game = GameObject.GetInstance();
            double years = Math.Max(0, (game.GameTime - p.CarrierSignedDate).TotalDays/365.0);
            double target = 55 + (PayFactor(carrier)*100 - 100)*0.9;
            target += p.Coach != null ? 10 : -12;
            target += Math.Min(6, years*1.5);
            if (p.Training != null) target += 4;
            return Math.Max(0, Math.Min(100, target));
        }

        public static double Average(Carrier carrier)
        {
            return carrier.Drivers.Count == 0 ? 0 : carrier.Drivers.Average(p => p.Morale);
        }

        public static List<string> ProcessMonth(Carrier carrier)
        {
            GameObject game = GameObject.GetInstance();
            var quit = new List<string>();
            foreach (Driver p in carrier.Drivers.ToList())
            {
                double target = Target(carrier, p);
                p.Morale = Math.Max(0, Math.Min(100, p.Morale + (target - p.Morale)*0.3 + (Rnd.NextDouble()*4 - 2)));
                if (p.Morale < 30 && p.Training == null && Rnd.NextDouble() < Math.Min(0.5, (30 - p.Morale)/40.0))
                {
                    string where = p.Coach != null ? " (he ran " + p.Coach.Name + ")" : "";
                    carrier.RemoveDriver(p);
                    p.Morale = 60;
                    quit.Add(p.Profile.Name + where);
                }
            }

            if (carrier == game.HumanCarrier)
            {
                if (quit.Count > 0)
                {
                    game.NewsBox.AddNews(
                        new News(
                            News.NewsType.CarrierNews, game.GameTime, "Drivers have quit",
                            string.Join(", ", quit) + " left the carrier because of low morale. Pay better, give a bonus, or keep drivers on coach."));
                }
                else if (carrier.Drivers.Count > 0 && Average(carrier) < 40)
                {
                    game.NewsBox.AddNews(
                        new News(
                            News.NewsType.CarrierNews, game.GameTime, "Drivers are unhappy",
                            "The average driver morale is " + Average(carrier).ToString("0") + ". Some are close to quitting. Raise the pay level, pay a bonus, or put idle drivers on coach."));
                }
            }
            return quit;
        }
    }
}
