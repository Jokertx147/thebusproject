using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.General;
using TheBusProject.Models.Pilots;
using TheBusProject.Models.Carriers;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     Pilot pay and morale. The carrier sets a pay level (percent of the market salary). Every month each pilot's morale moves
    ///     toward a target that depends on pay, whether he has an coach, and his time with the carrier. Unhappy pilots quit.
    ///     A one-off bonus lifts everybody's morale.
    /// </summary>
    public static class PilotMoraleService
    {
        public const int MinPay = 70;
        public const int MaxPay = 150;
        public const int BonusWaitDays = 120;
        // Random.Shared is safe to use from the parallel carrier threads (a shared new Random() is not)
        private static Random Rnd => Random.Shared;

        public static double PayFactor(Carrier carrier)
        {
            return carrier.PilotPayPercent <= 0 ? 1.0 : carrier.PilotPayPercent/100.0;
        }

        public static string MoodText(double morale)
        {
            if (morale >= 85) return "Excellent";
            if (morale >= 65) return "Good";
            if (morale >= 45) return "Okay";
            if (morale >= 30) return "Low";
            return "About to quit";
        }

        /// <summary>The monthly pay of all the carrier's pilots at a pay level.</summary>
        public static double Payroll(Carrier carrier, int percent)
        {
            double sum = 0;
            foreach (Pilot p in carrier.Pilots.ToList())
            {
                sum += CarrierHelpers.GetPilotSalary(carrier, p)/PayFactor(carrier)*percent/100.0;
            }
            return sum;
        }

        public static bool SetPay(Carrier carrier, int percent, out string message)
        {
            percent = Math.Max(MinPay, Math.Min(MaxPay, percent));
            carrier.PilotPayPercent = percent;
            message = "Pilots are now paid " + percent + "% of the market salary.";
            return true;
        }

        public static double BonusCost(Carrier carrier)
        {
            return Payroll(carrier, carrier.PilotPayPercent <= 0 ? 100 : (int) Math.Round(carrier.PilotPayPercent));
        }

        public static int DaysToNextBonus(Carrier carrier)
        {
            double d = BonusWaitDays - (GameObject.GetInstance().GameTime.Date - carrier.LastPilotBonus.Date).TotalDays;
            return carrier.LastPilotBonus == DateTime.MinValue ? 0 : (int) Math.Max(0, Math.Ceiling(d));
        }

        public static bool PayBonus(Carrier carrier, out string message)
        {
            GameObject game = GameObject.GetInstance();
            if (carrier.Pilots.Count == 0)
            {
                message = "You have no pilots.";
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
            foreach (Pilot p in carrier.Pilots.ToList()) p.Morale = Math.Min(100, p.Morale + 15);
            carrier.LastPilotBonus = game.GameTime.Date;
            message = "Every pilot got a month's pay as a bonus (" + cost.ToString("N0") + "). Morale is up.";
            return true;
        }

        public static double Target(Carrier carrier, Pilot p)
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
            return carrier.Pilots.Count == 0 ? 0 : carrier.Pilots.Average(p => p.Morale);
        }

        public static List<string> ProcessMonth(Carrier carrier)
        {
            GameObject game = GameObject.GetInstance();
            var quit = new List<string>();
            foreach (Pilot p in carrier.Pilots.ToList())
            {
                double target = Target(carrier, p);
                p.Morale = Math.Max(0, Math.Min(100, p.Morale + (target - p.Morale)*0.3 + (Rnd.NextDouble()*4 - 2)));
                if (p.Morale < 30 && p.Training == null && Rnd.NextDouble() < Math.Min(0.5, (30 - p.Morale)/40.0))
                {
                    string where = p.Coach != null ? " (he ran " + p.Coach.Name + ")" : "";
                    carrier.RemovePilot(p);
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
                            News.NewsType.CarrierNews, game.GameTime, "Pilots have quit",
                            string.Join(", ", quit) + " left the carrier because of low morale. Pay better, give a bonus, or keep pilots on coach."));
                }
                else if (carrier.Pilots.Count > 0 && Average(carrier) < 40)
                {
                    game.NewsBox.AddNews(
                        new News(
                            News.NewsType.CarrierNews, game.GameTime, "Pilots are unhappy",
                            "The average pilot morale is " + Average(carrier).ToString("0") + ". Some are close to quitting. Raise the pay level, pay a bonus, or put idle pilots on coach."));
                }
            }
            return quit;
        }
    }
}
