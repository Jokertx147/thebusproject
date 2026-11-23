using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;

namespace TheBusProject.Presentation
{
    /// <summary>An idle coach in the list, with its best suggestion.</summary>
    public class IdleCoachRow
    {
        public FleetCoach Coach { get; set; }
        public string Title { get; set; }
        public string Line { get; set; }
        public string Best { get; set; }
        public List<RouteSuggestion> Suggestions { get; set; }
    }

    /// <summary>A route suggested for the selected coach.</summary>
    public class SuggestionRow
    {
        public RouteSuggestion Suggestion { get; set; }
        public string Title { get; set; }
        public string Line1 { get; set; }
        public string Line2 { get; set; }
        public string Gates { get; set; }
        public bool HasGates => !string.IsNullOrEmpty(Gates);
        public bool Loses { get; set; }
        public ICommand OpenCommand { get; set; }
        public ICommand AdjustCommand { get; set; }
    }

    /// <summary>
    ///     Routes > Suggestions: the player's idle passenger coach, and for each the routes forecast to earn the most,
    ///     opened in one click (gates leased, the best number of round trips scheduled) or taken to the planner first.
    /// </summary>
    public class RouteSuggestionsViewModel : ObservableObject
    {
        private readonly Action _changed;
        private readonly Action<Station, Station, FleetCoach> _plan;
        private IdleCoachRow _selected;
        private bool _busy;
        private string _status, _summary;
        private int _loadId;

        /// <param name="changed">Called after a route was opened (the route list is read again).</param>
        /// <param name="plan">Opens the route planner for two stations with an coach picked.</param>
        public RouteSuggestionsViewModel(Action changed, Action<Station, Station, FleetCoach> plan)
        {
            _changed = changed;
            _plan = plan;
            RefreshCommand = new RelayCommand(() => { var _ = Reload(); });
        }

        public ObservableCollection<IdleCoachRow> Coach { get; } = new ObservableCollection<IdleCoachRow>();
        public ObservableCollection<SuggestionRow> Suggestions { get; } = new ObservableCollection<SuggestionRow>();
        public ICommand RefreshCommand { get; }

        public bool IsBusy { get => _busy; private set => SetProperty(ref _busy, value); }
        public string Status { get => _status; private set => SetProperty(ref _status, value); }
        public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
        public bool HasCoach => Coach.Count > 0;
        public bool NoSuggestions => _selected != null && Suggestions.Count == 0;

        public IdleCoachRow Selected
        {
            get => _selected;
            set
            {
                if (SetProperty(ref _selected, value)) ShowSuggestions();
            }
        }

        private static string M(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        /// <summary>Works the suggestions out again (in the background: each coach forecasts a few dozen routes).</summary>
        public async Task Reload()
        {
            var carrier = GameObject.GetInstance()?.HumanCarrier;
            if (carrier == null) return;
            int id = ++_loadId;
            IsBusy = true;
            Summary = "Looking for routes...";
            FleetCoach keep = _selected?.Coach;
            Dictionary<FleetCoach, List<RouteSuggestion>> all;
            try
            {
                all = await Task.Run(() => RouteSuggestionService.ForIdle(carrier));
            }
            catch (Exception ex)
            {
                if (id != _loadId) return;
                IsBusy = false;
                Summary = "The suggestions couldn't be worked out: " + ex.GetBaseException().Message;
                return;
            }
            if (id != _loadId) return;
            IsBusy = false;
            Show(all, keep);
        }

        private void Show(Dictionary<FleetCoach, List<RouteSuggestion>> all, FleetCoach keep)
        {
            Coach.Clear();
            foreach (var kv in all.OrderByDescending(x => x.Value.Count == 0 ? double.MinValue : x.Value[0].GainPerDay))
            {
                FleetCoach f = kv.Key;
                RouteSuggestion best = kv.Value.FirstOrDefault();
                Coach.Add(new IdleCoachRow
                {
                    Coach = f, Suggestions = kv.Value,
                    Title = f.Name + "  " + f.Coach.Type.Name,
                    Line = string.Format("{0} seats, {1:#,0} km range, based at {2}", f.Coach.GetTotalSeatCapacity(), f.Coach.Range, f.Homebase?.Profile.IATACode ?? "?"),
                    Best = best == null ? "no route found" : string.Format("Best: {0}, {1} a day", best.Name, Signed(best.GainPerDay))
                });
            }
            int n = Coach.Count;
            Summary = n == 0
                ? "Every coach of yours has a route."
                : n == 1 ? "1 coach has no route. It still costs " + M(OwnershipService.MonthlyCost(all.Keys.First())) + " a month to own or lease."
                : n + " coaches have no route. Together they cost " + M(all.Keys.Sum(OwnershipService.MonthlyCost)) + " a month to own or lease.";
            OnPropertyChanged(nameof(HasCoach));
            _selected = Coach.FirstOrDefault(a => a.Coach == keep) ?? Coach.FirstOrDefault();
            OnPropertyChanged(nameof(Selected));
            ShowSuggestions();
        }

        private static string Signed(double v)
        {
            return (v >= 0 ? "+" : "-") + M(Math.Abs(v));
        }

        private void ShowSuggestions()
        {
            Suggestions.Clear();
            if (_selected != null)
            {
                foreach (RouteSuggestion s in _selected.Suggestions)
                {
                    RouteSuggestion x = s;
                    RouteForecast fc = s.Forecast;
                    Suggestions.Add(new SuggestionRow
                    {
                        Suggestion = s, Loses = s.GainPerDay < 0,
                        Title = s.Name + "  " + s.To.Profile.Name + (s.From != _selected.Coach.Homebase ? " (from " + s.From.Profile.Name + ")" : ""),
                        Line1 = string.Format("{0:#,0} km, {1} round trip{2} a day, {3:0}% full, {4}, about {5:#,0} passengers a day want to run it",
                                              s.Km, fc.RoundTrips, fc.RoundTrips == 1 ? "" : "s", fc.Load*100,
                                              fc.Rivals == 0 ? "no rivals" : fc.Rivals == 1 ? "1 rival" : fc.Rivals + " rivals", s.DailyDemand),
                        Line2 = string.Format("{0} a day from its trips{1}; {2} after the coach's own cost",
                                              Signed(fc.ProfitPerDay), s.GatesPerDay > 0 ? ", " + Signed(s.GainPerDay) + " after the new gates" : "", Signed(s.NetPerDay)),
                        Gates = s.GatesToLease.Count == 0 ? null : "Leases gates at " + string.Join(" and ", s.GatesToLease.Select(a => a.Profile.IATACode)) + " (about " + M(s.GatesPerDay) + " a day for 2 years)",
                        OpenCommand = new RelayCommand(() => Open(x)),
                        AdjustCommand = new RelayCommand(() => _plan?.Invoke(x.From, x.To, x.Coach))
                    });
                }
            }
            OnPropertyChanged(nameof(NoSuggestions));
        }

        /// <summary>Opens the route in one go: leases the missing gates, then schedules the coach for the best number of round trips.</summary>
        public bool Open(RouteSuggestion s)
        {
            string done = null;
            bool finished = false;
            var create = new CreateRouteViewModel(s.From, s.To, m => { done = m; finished = true; });
            CoachChoice choice = create.Coaches.FirstOrDefault(a => a.Coach == s.Coach);
            if (choice == null)
            {
                Status = s.Coach.Name + " can't run " + s.Name + " any more.";
                return false;
            }
            create.SelectedCoach = choice;
            foreach (GateOffer o in create.Offers.ToList())
            {
                if (o.CanLease) o.LeaseCommand.Execute(null);
            }
            if (create.RoundTrips != s.Forecast.RoundTrips && create.Trips.Contains(s.Forecast.RoundTrips)) create.RoundTrips = s.Forecast.RoundTrips;
            create.CreateCommand.Execute(null);
            if (!finished || done == null)
            {
                Status = "Couldn't open " + s.Name + ": " + (create.Info ?? "the planner refused it.");
                return false;
            }
            Status = done;
            _changed?.Invoke();
            var reload = Reload();
            return true;
        }
    }
}
