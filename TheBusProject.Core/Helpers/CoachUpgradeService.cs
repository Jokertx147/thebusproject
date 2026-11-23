using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Finances;

namespace TheBusProject.Helpers
{
    /// <summary>
    ///     What can be done to a coach you already own, short of selling it. A coach is a long-lived thing: operators
    ///     re-power them, refit the interior and keep them running for twenty years, which is why the second-hand
    ///     market is full of coaches older than their drivers.
    /// </summary>
    public class CoachUpgrade
    {
        /// <summary>The engine that would go in.</summary>
        public EngineType Engine { get; set; }

        /// <summary>What it costs today, the engine and the labour of changing it.</summary>
        public double Price { get; set; }

        /// <summary>Days off the road while the work is done.</summary>
        public int Days { get; set; }

        /// <summary>What the coach gains, in the player's words.</summary>
        public string Effect { get; set; }

        public string Name => EngineTypes.FullName(Engine);
    }

    public static class CoachUpgradeService
    {
        /// <summary>The engine and the labour: changing one out costs about half as much again as the engine.</summary>
        public const double LabourFactor = 1.5;

        /// <summary>A re-power keeps the coach in the shop for a fortnight, less with a garage of your own.</summary>
        public const int BaseDays = 14;

        /// <summary>The engines that could go in this coach today, the thriftiest first, excluding the one in it.</summary>
        public static List<CoachUpgrade> Repowers(FleetCoach coach)
        {
            var result = new List<CoachUpgrade>();
            if (coach?.Coach?.Type == null) return result;

            int year = GameObject.GetInstance().GameTime.Year;
            EngineType fitted = coach.Coach.EngineType;
            double discount = FleetCoachHelpers.GarageDiscount(coach);

            foreach (EngineType e in EngineTypes.GetEngineTypes(coach.Coach.Type, year))
            {
                if (e == fitted) continue;
                result.Add(
                    new CoachUpgrade
                    {
                        Engine = e,
                        Price = Price(coach, e),
                        Days = Math.Max(3, (int) Math.Round(BaseDays*(1 - discount))),
                        Effect = Effect(coach, e)
                    });
            }

            return result.OrderBy(u => u.Engine.ConsumptationModifier).ThenBy(u => u.Price).ToList();
        }

        /// <summary>What a re-power costs today: the engine at today's money, plus the labour of changing it.</summary>
        public static double Price(FleetCoach coach, EngineType engine)
        {
            double discount = FleetCoachHelpers.GarageDiscount(coach);
            return GeneralHelpers.GetInflationPrice(engine.Price)*LabourFactor*(1 - discount*0.5);
        }

        /// <summary>What changes, against the engine the coach has now.</summary>
        public static string Effect(FleetCoach coach, EngineType engine)
        {
            EngineType fitted = coach.Coach.EngineType;
            double oldFuel = fitted?.ConsumptationModifier ?? 1;
            double oldRange = fitted?.RangeModifier ?? 1;
            int oldSpeed = fitted?.MaxSpeed ?? (int) coach.Coach.Type.CruisingSpeed;

            var parts = new List<string>();
            double fuel = (1 - engine.ConsumptationModifier/oldFuel)*100;
            if (Math.Abs(fuel) >= 0.5) parts.Add(Math.Abs(fuel).ToString("0") + "% " + (fuel > 0 ? "less" : "more") + " fuel");
            double range = (engine.RangeModifier/oldRange - 1)*100;
            if (Math.Abs(range) >= 0.5) parts.Add(Math.Abs(range).ToString("0") + "% " + (range > 0 ? "more" : "less") + " range");
            int speed = engine.MaxSpeed - oldSpeed;
            if (speed != 0 && coach.Coach.Type.CruisingSpeed > engine.MaxSpeed)
                parts.Add("held to " + engine.MaxSpeed + " km/h");

            return parts.Count == 0 ? "the same figures, a newer engine" : string.Join(", ", parts);
        }

        /// <summary>
        ///     Why this coach cannot be re-powered right now, or null when it can. A coach has to be stopped: you
        ///     cannot take the engine out of something that is carrying passengers this afternoon.
        /// </summary>
        public static string WhyNot(FleetCoach coach, CoachUpgrade upgrade)
        {
            if (coach == null || upgrade == null) return "Nothing chosen.";
            if (coach.Status != FleetCoach.CoachStatus.Stopped) return "Stop the coach first: the engine can't come out while it is on a route.";
            if (coach.Coach?.Carrier == null) return "This coach isn't yours.";
            if (upgrade.Price > coach.Coach.Carrier.Money) return "You can't afford it.";
            return null;
        }

        /// <summary>
        ///     Fits the engine, pays for it and puts the coach in the shop for the work. Returns what to tell the
        ///     player, or the reason it didn't happen.
        /// </summary>
        public static string Repower(FleetCoach coach, CoachUpgrade upgrade)
        {
            string no = WhyNot(coach, upgrade);
            if (no != null) return no;

            Carrier carrier = coach.Coach.Carrier;
            DateTime now = GameObject.GetInstance().GameTime;
            CarrierHelpers.AddCarrierInvoice(carrier, now, Invoice.InvoiceType.Maintenances, -upgrade.Price);
            coach.Coach.EngineType = upgrade.Engine;
            coach.OOSDate = now.AddDays(upgrade.Days);

            return string.Format(
                "{0} is being re-powered with a {1} for {2}. It is back on the road {3:d} ({4}).",
                coach.Name,
                upgrade.Name,
                Convert.ToString(new ValueCurrencyFormatter().Convert(upgrade.Price)),
                coach.OOSDate,
                upgrade.Effect);
        }
    }
}
