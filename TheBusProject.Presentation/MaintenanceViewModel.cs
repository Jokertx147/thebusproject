using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.General;

namespace TheBusProject.Presentation
{
    public class CheckItem
    {
        public string Label { get; set; }
        public string Last { get; set; }
        public string Due { get; set; }
        public bool Overdue { get; set; }
        public string Effect { get; set; }
        public ICommand Command { get; set; }
        public bool CanDo { get; set; }
    }

    public class MaintenanceItem : ObservableObject
    {
        private string _aText, _bText, _cText, _dText;

        public FleetCoach Coach { get; set; }
        public string Title { get; set; }
        public string Condition { get; set; }
        public double ConditionNumber { get; set; }
        public string Grounded { get; set; }
        public bool IsGrounded { get; set; }
        public string History { get; set; }
        public List<CheckItem> Checks { get; set; }
        public ICommand ApplyIntervalsCommand { get; set; }
        public Action<MaintenanceItem, bool> AutoChanged { get; set; }

        /// <summary>Do the checks that fall due automatically.</summary>
        public bool Auto
        {
            get { return Coach != null && Coach.AutoMaintenance; }
            set
            {
                if (Coach == null || Coach.AutoMaintenance == value)
                {
                    return;
                }

                Coach.AutoMaintenance = value;
                OnPropertyChanged(nameof(Auto));
                AutoChanged?.Invoke(this, value);
            }
        }

        public string AText { get { return _aText; } set { SetProperty(ref _aText, value); } }
        public string BText { get { return _bText; } set { SetProperty(ref _bText, value); } }
        public string CText { get { return _cText; } set { SetProperty(ref _cText, value); } }
        public string DText { get { return _dText; } set { SetProperty(ref _dText, value); } }
    }

    /// <summary>Condition of the fleet and the A to D checks that keep it flying.</summary>
    public class MaintenanceViewModel : ObservableObject
    {
        private readonly Action _changed;
        private string _status;
        private string _pendingText;
        private Action _pending;

        public MaintenanceViewModel(Action changed)
        {
            _changed = changed;
            MaintenanceService.DistanceText = DistanceFormatter.Km;
            Items = new ObservableCollection<MaintenanceItem>();
            ConfirmCommand = new RelayCommand(Confirm);
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            AutoAllCommand = new RelayCommand(() => SetAutoForAll(true));
            AutoNoneCommand = new RelayCommand(() => SetAutoForAll(false));
            Reload();
        }

        public ObservableCollection<MaintenanceItem> Items { get; }
        public string Summary { get; private set; }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand AutoAllCommand { get; }
        public ICommand AutoNoneCommand { get; }
        public bool HasPending => _pending != null;

        public string PendingText
        {
            get { return _pendingText; }
            private set { SetProperty(ref _pendingText, value); }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        private static string Money(double v)
        {
            return Convert.ToString(new ValueCurrencyFormatter().Convert(v));
        }

        private void SetPending(string text, Action action)
        {
            _pending = action;
            PendingText = text;
            OnPropertyChanged(nameof(HasPending));
        }

        private void Confirm()
        {
            var action = _pending;
            SetPending(null, null);
            action?.Invoke();
        }

        public void Reload()
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            if (carrier == null)
            {
                return;
            }

            var game = GameObject.GetInstance();
            var keep = Items.ToDictionary(i => i.Coach, i => i);
            Items.Clear();
            var delivered = carrier.Fleet.Where(f => f.Coach.BuiltDate <= game.GameTime).OrderBy(f => f.Coach.Condition).ToList();
            int overdue = 0;
            foreach (var f in delivered)
            {
                var coach = f;
                var checks = new List<CheckItem>();
                foreach (MaintenanceCheck c in Enum.GetValues(typeof(MaintenanceCheck)))
                {
                    var check = c;
                    bool late = MaintenanceService.IsOverdue(coach, check);
                    if (late)
                    {
                        overdue++;
                    }
                    int days = MaintenanceService.GetDowntimeDays(coach, check);
                    string blocker = MaintenanceService.GetBlocker(coach, check);
                    string spareText = "";
                    if (MaintenanceService.NeedsSpare(coach, check))
                    {
                        var sp = MaintenanceService.IsCovered(coach) ? null : MaintenanceService.FindSpare(coach);
                        spareText = blocker != null ? "  |  NO SPARE AIRCRAFT" : sp != null ? "  |  spare: " + sp.Name : "";
                    }
                    checks.Add(new CheckItem
                    {
                        Label = check + " check",
                        Last = DateFormatter.Date(MaintenanceService.GetLastDate(coach, check)),
                        Due = (late ? "overdue since " : "due ") + DateFormatter.Date(MaintenanceService.GetDueDate(coach, check)),
                        Overdue = late,
                        Effect = string.Format(
                            "{0}  |  +{1} to {2}% condition  |  {3}",
                            Money(MaintenanceService.GetCost(coach, check)), MaintenanceService.MinGain(check), MaintenanceService.MaxGain(check),
                            days == 0 ? "no downtime" : "grounded about " + days + (days == 1 ? " day" : " days")) + spareText,
                        CanDo = !MaintenanceService.IsGrounded(coach) && blocker == null,
                        Command = new RelayCommand(() => AskCheck(coach, check))
                    });
                }

                var history = coach.MaintenanceHistory.Reverse().Take(4)
                    .Select(h => DateFormatter.Date(h.Key.Date) + " " + h.Value + " check " + Money(-h.Key.Amount)).ToList();

                var item = new MaintenanceItem
                {
                    Coach = coach,
                    Title = coach.Name + "  -  " + coach.Coach.Type.Name,
                    ConditionNumber = coach.Coach.Condition,
                    Condition = string.Format("Condition {0:0}% ({1})", coach.Coach.Condition, MaintenanceService.GetConditionText(coach.Coach.Condition)),
                    IsGrounded = MaintenanceService.IsGrounded(coach) || MaintenanceService.IsCovered(coach) || MaintenanceService.IsCovering(coach),
                    Grounded = (MaintenanceService.IsGrounded(coach) ? "Grounded until " + DateFormatter.Date(coach.GroundedToDate) : "")
                               + (MaintenanceService.IsCovered(coach) ? (MaintenanceService.IsGrounded(coach) ? ", but " : "") + "covered by " + coach.CoverName + ", its routes keep running" : "")
                               + (MaintenanceService.IsCovering(coach) ? "Standing in for " + coach.CoverName + " until " + DateFormatter.Date(coach.CoverUntil) : ""),
                    History = history.Count == 0 ? "No checks done yet." : "Recent: " + string.Join("   |   ", history),
                    Checks = checks,
                    AText = (coach.AMaintenanceInterval > 0 ? coach.AMaintenanceInterval : MaintenanceService.GetIntervalDays(coach, MaintenanceCheck.A)).ToString(),
                    BText = MaintenanceService.GetIntervalDays(coach, MaintenanceCheck.B).ToString(),
                    CText = (MaintenanceService.GetIntervalDays(coach, MaintenanceCheck.C)/30).ToString(),
                    DText = (MaintenanceService.GetIntervalDays(coach, MaintenanceCheck.D)/30).ToString()
                };
                MaintenanceItem old;
                if (keep.TryGetValue(coach, out old))
                {
                    item.AText = old.AText;
                    item.BText = old.BText;
                    item.CText = old.CText;
                    item.DText = old.DText;
                }
                item.ApplyIntervalsCommand = new RelayCommand(() => ApplyIntervals(item));
                item.AutoChanged = OnAutoChanged;
                Items.Add(item);
            }

            int auto = delivered.Count(f => f.AutoMaintenance);
            double avg = delivered.Count == 0 ? 0 : delivered.Average(f => f.Coach.Condition);
            Summary = delivered.Count == 0
                ? "You have no coach yet. Buy one under Coaches."
                : string.Format(
                    "{0} coach  |  average condition {1:0}%  |  {2} overdue checks  |  {3} on automatic maintenance  |  worn coach break down more often, and lose value. A and B checks are cheap; C and D checks ground the coach.",
                    delivered.Count, avg, overdue, auto);
            OnPropertyChanged(nameof(Summary));
        }

        private void OnAutoChanged(MaintenanceItem item, bool on)
        {
            Status = on
                ? item.Coach.Name + " is on automatic maintenance: the checks are done as they fall due, as long as there is cash for them."
                : item.Coach.Name + " is back on manual maintenance.";
            Reload();
        }

        private void SetAutoForAll(bool on)
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            if (carrier == null)
            {
                return;
            }

            int n = 0;
            foreach (var f in carrier.Fleet)
            {
                if (f.AutoMaintenance != on)
                {
                    f.AutoMaintenance = on;
                    n++;
                }
            }

            Status = on ? "Automatic maintenance is on for " + n + " more coach." : "Automatic maintenance is off for " + n + " coach.";
            Reload();
        }

        private void AskCheck(FleetCoach f, MaintenanceCheck c)
        {
            int days = MaintenanceService.GetDowntimeDays(f, c);
            var spare = MaintenanceService.NeedsSpare(f, c) ? MaintenanceService.FindSpare(f) : null;
            SetPending(
                string.Format(
                    "Do the {0} check on {1} for {2}?{3}{4}", c, f.Name, Money(MaintenanceService.GetCost(f, c)),
                    days > 0 ? " It will be grounded for about " + days + " days." : "",
                    spare != null ? " " + spare.Name + " takes over its routes meanwhile." : ""),
                () => DoCheck(f, c));
        }

        private void DoCheck(FleetCoach f, MaintenanceCheck c)
        {
            string message;
            MaintenanceService.PerformCheck(f, c, out message);
            Status = message;
            Reload();
            _changed();
        }

        private void ApplyIntervals(MaintenanceItem item)
        {
            int a, b, c, d;
            if (!int.TryParse(item.AText, out a) || !int.TryParse(item.BText, out b) || !int.TryParse(item.CText, out c) || !int.TryParse(item.DText, out d))
            {
                Status = "Enter the intervals as whole numbers.";
                return;
            }

            MaintenanceService.SetIntervals(item.Coach, a, b, c, d);
            Status = "Intervals for " + item.Coach.Name + " set (A and B in days: 30-365 and 90-730, C and D in months: 6-48 and 24-120).";
            Reload();
        }
    }
}
