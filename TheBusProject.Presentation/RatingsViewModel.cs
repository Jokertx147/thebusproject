using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TheBusProject.Helpers;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Statistics;

namespace TheBusProject.Presentation
{
    /// <summary>One of the five ratings with its reasons.</summary>
    public class RatingRow
    {
        public string Name { get; set; }
        public int Value { get; set; }
        public string ValueText { get; set; }
        public string Change { get; set; }
        public string Heading { get; set; }
        public string Reasons { get; set; }
        public string Tip { get; set; }
    }

    /// <summary>One line of the carrier comparison.</summary>
    public class RatingCompareRow
    {
        public string Rank { get; set; }
        public string Carrier { get; set; }
        public string Overall { get; set; }
        public string Detail { get; set; }
        public bool IsMine { get; set; }
    }

    /// <summary>One statistic this year against last year.</summary>
    public class RatingStatRow
    {
        public string Name { get; set; }
        public string ThisYear { get; set; }
        public string LastYear { get; set; }
        public string Change { get; set; }
    }

    /// <summary>My Carrier > Ratings: the five ratings, why they are what they are, and how the carrier compares.</summary>
    public class RatingsViewModel : ObservableObject
    {
        private string _summary;
        private string _rank;

        public RatingsViewModel()
        {
            Ratings = new ObservableCollection<RatingRow>();
            Compare = new ObservableCollection<RatingCompareRow>();
            Stats = new ObservableCollection<RatingStatRow>();
            Reload();
        }

        public ObservableCollection<RatingRow> Ratings { get; }
        public ObservableCollection<RatingCompareRow> Compare { get; }
        public ObservableCollection<RatingStatRow> Stats { get; }

        public string Summary
        {
            get { return _summary; }
            private set { SetProperty(ref _summary, value); }
        }

        public string Rank
        {
            get { return _rank; }
            private set { SetProperty(ref _rank, value); }
        }

        public string Effects =>
            "What the ratings do: customer happiness changes how often passengers pick you over other carriers " +
            "(from 75% at 0 to 125% at 100). Maintenance, safety and security set the price of insurance. " +
            "All five count toward your game score. Ratings are updated on the 1st of each month and move a third of the way toward where they are heading.";

        private static string Signed(double v, string format = "0")
        {
            return (v > 0 ? "+" : "") + v.ToString(format);
        }

        public void Reload()
        {
            var game = GameObject.GetInstance();
            var carrier = game?.HumanCarrier;
            if (carrier == null) return;

            var all = Carriers.GetAllCarriers().ToList();

            // a new game, or a save from before ratings were worked out: work them out once now
            if (all.All(a => !a.Ratings.IsCalculated))
            {
                foreach (var a in all) RatingsService.ProcessMonth(a);
            }
            else if (!carrier.Ratings.IsCalculated)
            {
                RatingsService.ProcessMonth(carrier);
            }

            Ratings.Clear();
            foreach (var t in RatingsService.Targets(carrier))
            {
                int value = RatingsService.Get(carrier, t.Name);
                int change = value - RatingsService.GetLastMonth(carrier, t.Name);
                int target = t.Value;
                string heading = target == value ? "Steady" : target > value ? "Rising toward " + target : "Falling toward " + target;
                var reasons = t.Factors.Select(f => Signed(f.Points) + "  " + f.Text).ToList();
                if (Math.Abs(t.Base - 50) > 0.1 && t.Base > 0) reasons.Insert(0, "Starts at " + t.Base.ToString("0"));
                Ratings.Add(new RatingRow
                {
                    Name = t.Name,
                    Value = value,
                    ValueText = value + " / 100  " + RatingsService.Grade(value),
                    Change = change == 0 ? "no change this month" : Signed(change) + " this month",
                    Heading = heading,
                    Reasons = reasons.Count == 0 ? "Nothing to rate yet: buy coaches, open routes and hire staff." : string.Join(Environment.NewLine, reasons),
                    Tip = t.Tip
                });
            }

            int overall = RatingsService.Overall(carrier);
            Summary = "Overall rating " + overall + " / 100  (" + RatingsService.Grade(overall) + ")";

            var ranked = all.OrderByDescending(RatingsService.Overall).ThenBy(a => a.Profile.Name).ToList();
            int position = ranked.IndexOf(carrier) + 1;
            Rank = "You are number " + position + " of " + ranked.Count + " carriers by overall rating.";

            Compare.Clear();
            var shown = ranked.Take(10).ToList();
            if (!shown.Contains(carrier)) shown.Add(carrier);
            foreach (var a in shown)
            {
                var r = a.Ratings;
                Compare.Add(new RatingCompareRow
                {
                    Rank = "#" + (ranked.IndexOf(a) + 1),
                    Carrier = a.Profile.Name + (a.Profile.IATACode != null ? " (" + a.Profile.IATACode + ")" : "") + (a == carrier ? "  \u2190 you" : ""),
                    Overall = RatingsService.Overall(a).ToString(),
                    Detail = string.Format(
                        "customers {0}  |  staff {1}  |  maintenance {2}  |  safety {3}  |  security {4}",
                        r.CustomerHappinessRating, r.EmployeeHappinessRating, r.MaintenanceRating, r.SafetyRating, r.SecurityRating),
                    IsMine = a == carrier
                });
            }

            Stats.Clear();
            int year = game.GameTime.Year;
            foreach (var pair in new[]
            {
                new[] { "Passengers", "Passengers" }, new[] { "Passengers per trip", "Passengers%" },
                new[] { "Trips", "Arrivals" }, new[] { "On-time trips %", "On-Time%" }
            })
            {
                var type = StatisticsTypes.GetStatisticsType(pair[1]);
                if (type == null) continue;
                double now = carrier.Statistics.GetStatisticsValue(year, type);
                double last = carrier.Statistics.GetStatisticsValue(year - 1, type);
                string change = last == 0 ? "" : Signed((now - last)/last*100, "0.#") + "%";
                Stats.Add(new RatingStatRow
                {
                    Name = pair[0],
                    ThisYear = now.ToString("N0"),
                    LastYear = last.ToString("N0"),
                    Change = change
                });
            }
        }
    }
}
