using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TheBusProject.Helpers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.General;

namespace TheBusProject.Presentation
{
    public class PerformanceRow
    {
        public FleetCoach Coach { get; set; }
        public string Name { get; set; }
        public string Numbers { get; set; }
        public double Profit { get; set; }
        public double LoadFactor { get; set; }
        public double Trips { get; set; }
        public bool IsLoss => Profit < 0;
    }

    public class MonthRow
    {
        public string Month { get; set; }
        public string Income { get; set; }
        public string Costs { get; set; }
        public string Profit { get; set; }
        public bool IsLoss { get; set; }
        public string Detail { get; set; }
    }

    /// <summary>My Carrier > Fleet performance: which coach earn money, and each coach's history.</summary>
    public class FleetPerformanceViewModel : ObservableObject
    {
        private string _sortBy = "Profit (12 months)";
        private PerformanceRow _selected;
        private string _summary, _detail;

        public FleetPerformanceViewModel()
        {
            Rows = new ObservableCollection<PerformanceRow>();
            Months = new ObservableCollection<MonthRow>();
            YearLines = new ObservableCollection<string>();
            Reload();
        }

        public ObservableCollection<PerformanceRow> Rows { get; }
        public ObservableCollection<MonthRow> Months { get; }
        public ObservableCollection<string> YearLines { get; }
        public List<string> SortOptions { get; } = new List<string> { "Profit (12 months)", "Loss first", "Load factor", "Trips", "Name" };

        public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
        public string Detail { get => _detail; private set => SetProperty(ref _detail, value); }
        public bool HasSelection => _selected != null;

        public string SortBy
        {
            get => _sortBy;
            set { if (SetProperty(ref _sortBy, value ?? SortOptions[0])) Reload(); }
        }

        public PerformanceRow Selected
        {
            get => _selected;
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    OnPropertyChanged(nameof(HasSelection));
                    LoadDetail();
                }
            }
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        /// <summary>Shows one coach (used by the fleet page).</summary>
        public void ShowCoach(FleetCoach f)
        {
            Reload();
            Selected = Rows.FirstOrDefault(r => r.Coach == f);
        }

        public void Reload()
        {
            var carrier = GameObject.GetInstance()?.HumanCarrier;
            if (carrier == null) return;
            int year = GameObject.GetInstance().GameTime.Year;

            var rows = new List<PerformanceRow>();
            foreach (var f in carrier.Fleet.ToList().Where(f => f?.Coach != null))
            {
                double profit = CoachHistoryService.ProfitLast12(f);
                var y = CoachHistoryService.Years(f).FirstOrDefault(x => x.Year == year);
                double lf = y?.LoadFactor ?? 0, trips = y?.Trips ?? 0;
                var routes = f.Routes.Select(r => r.Destination1.Profile.IATACode + "-" + r.Destination2.Profile.IATACode).Take(3).ToList();
                rows.Add(new PerformanceRow
                {
                    Coach = f,
                    Name = f.Name + "  " + f.Coach.Type.Name,
                    Profit = profit,
                    LoadFactor = lf,
                    Trips = trips,
                    Numbers = string.Format(
                        "{0} in the last 12 months  |  {1:N0} trips this year{2}  |  {3}",
                        Money(profit), trips, lf > 0 ? string.Format(", {0:0}% full", lf*100) : "",
                        routes.Count == 0 ? "no routes" : string.Join(", ", routes) + (f.Routes.Count > 3 ? " ..." : ""))
                });
            }

            IEnumerable<PerformanceRow> sorted;
            switch (_sortBy)
            {
                case "Loss first": sorted = rows.OrderBy(r => r.Profit); break;
                case "Load factor": sorted = rows.OrderByDescending(r => r.LoadFactor); break;
                case "Trips": sorted = rows.OrderByDescending(r => r.Trips); break;
                case "Name": sorted = rows.OrderBy(r => r.Name); break;
                default: sorted = rows.OrderByDescending(r => r.Profit); break;
            }

            var keep = _selected?.Coach;
            Rows.Clear();
            foreach (var r in sorted) Rows.Add(r);
            _selected = null;
            Selected = Rows.FirstOrDefault(r => r.Coach == keep) ?? Rows.FirstOrDefault();
            OnPropertyChanged(nameof(Selected));

            int losing = rows.Count(r => r.Profit < 0);
            Summary = rows.Count == 0
                ? "You have no coach yet."
                : string.Format(
                    "{0} coach  |  fleet profit in the last 12 months {1}  |  {2} making a loss  |  best: {3}",
                    rows.Count, Money(rows.Sum(r => r.Profit)), losing, rows.OrderByDescending(r => r.Profit).First().Name);
        }

        private void LoadDetail()
        {
            Months.Clear();
            YearLines.Clear();
            if (_selected == null)
            {
                Detail = "";
                return;
            }
            var f = _selected.Coach;
            var a = f.Coach;
            var all = CoachHistoryService.Months(f, 1000);
            Detail = string.Format(
                "{0}  {1}  |  built {2}, {3} years old  |  condition {4:0}%  |  {5}  |  worth about {6}" + Environment.NewLine +
                "Profit since you got it: {7}  |  {8} route(s)",
                f.Name, a.Type.Name, a.BuiltDate.Year, a.Age, a.Condition,
                f.Purchased == FleetCoach.PurchasedType.Leased ? "leased" : "owned", Money(a.GetPrice()),
                Money(all.Sum(m => m.Profit)), f.Routes.Count);

            foreach (var m in all.Take(24))
            {
                Months.Add(new MonthRow
                {
                    Month = CoachHistoryService.MonthName(m.Year, m.Month),
                    Income = Money(m.Income),
                    Costs = Money(m.Costs),
                    Profit = Money(m.Profit),
                    IsLoss = m.Profit < 0,
                    Detail = string.Join("  |  ", m.Parts.Where(p => Math.Abs(p.Value) >= 0.5).OrderByDescending(p => Math.Abs(p.Value)).Select(p => p.Key + " " + Money(p.Value)))
                });
            }

            foreach (var y in CoachHistoryService.Years(f))
            {
                YearLines.Add(string.Format(
                    "{0}:  {1:N0} trips  |  {2:N0} passengers{3}{4}",
                    y.Year, y.Trips, y.Passengers,
                    y.LoadFactor > 0 ? string.Format("  |  {0:0}% full on average", y.LoadFactor*100) : "",
                    y.Cargo > 0 ? string.Format("  |  {0:N0} cargo", y.Cargo) : ""));
            }
        }
    }
}
