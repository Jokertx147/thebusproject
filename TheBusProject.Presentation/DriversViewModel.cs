using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.General;
using TheBusProject.Models.Drivers;

namespace TheBusProject.Presentation
{
    public class DriverItem
    {
        public DriverItem(Driver driver)
        {
            Driver = driver;
            IsIdle = driver.Carrier != null && driver.Coach == null;
            IsTraining = driver.Training != null;
            Name = driver.Profile.Name;
            Details = string.Format(
                "age {0}  |  {1}  |  {2}",
                driver.Profile.Age, driver.Rating?.Name, string.Join(", ", driver.Coaches ?? new List<string>()));
            Mood = driver.Carrier == null ? null : string.Format("morale {0:0} ({1})", driver.Morale, DriverMoraleService.MoodText(driver.Morale));
            IsUnhappy = driver.Carrier != null && driver.Morale < 35;
            Assignment = (driver.Coach == null ? "not assigned" : "runs " + driver.Coach.Name)
                         + (driver.Training != null ? "  |  training for the " + driver.Training.CoachFamily + " until " + DateFormatter.Date(driver.Training.EndDate) : "");
        }

        public Driver Driver { get; }

        /// <summary>One of your drivers who has no coach.</summary>
        public bool IsIdle { get; }

        public bool IsTraining { get; }
        public string Name { get; }
        public string Details { get; }
        public string Assignment { get; }
        public string Mood { get; }
        public bool IsUnhappy { get; }
    }

    public class CrewChoice
    {
        public string Name { get; set; }
        public FleetCoach Coach { get; set; }
        public override string ToString() { return Name; }
    }

    /// <summary>Hire drivers and put them on coach. An coach needs a full cab crew before it can fly.</summary>
    public class DriversViewModel : ObservableObject
    {
        private readonly Action _changed;
        private CrewChoice _target;
        private DriverItem _selected;
        private string _status;
        private string _pendingText;
        private Action _pending;
        private bool _includeAssigned;
        private NamedChoice<Driver> _trainDriver;
        private NamedChoice<string> _trainFamily;
        private NamedChoice<Driver> _assignDriver;
        private NamedChoice<FleetCoach> _assignTarget;

        public DriversViewModel(Action changed)
        {
            _changed = changed;
            Available = new ObservableCollection<DriverItem>();
            Hired = new ObservableCollection<DriverItem>();
            Targets = new ObservableCollection<CrewChoice>();
            HireCommand = new RelayCommand(HireSelected, () => _selected != null);
            HireCrewCommand = new RelayCommand(HireCrew, () => _target?.Coach != null);
            AssignIdleCommand = new RelayCommand(AssignIdle);
            TrainDrivers = new ObservableCollection<NamedChoice<Driver>>();
            TrainFamilies = new ObservableCollection<NamedChoice<string>>();
            TrainCommand = new RelayCommand(AskTrain);
            AssignDrivers = new ObservableCollection<NamedChoice<Driver>>();
            AssignTargets = new ObservableCollection<NamedChoice<FleetCoach>>();
            AssignCommand = new RelayCommand(AssignSelected);
            UnassignCommand = new RelayCommand(UnassignSelected);
            ConfirmCommand = new RelayCommand(() => { var a = _pending; SetPending(null, null); a?.Invoke(); });
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            PayLevels = new ObservableCollection<NamedChoice<int>>();
            for (int pct = DriverMoraleService.MinPay; pct <= DriverMoraleService.MaxPay; pct += 10)
            {
                PayLevels.Add(new NamedChoice<int> { Name = pct + "%" + (pct == 100 ? " (market)" : ""), Value = pct });
            }
            BonusCommand = new RelayCommand(AskBonus);
            Reload();
        }

        public ObservableCollection<DriverItem> Available { get; }
        public ObservableCollection<DriverItem> Hired { get; }

        /// <summary>The coach that still need drivers; "any" shows every driver for hire.</summary>
        public ObservableCollection<CrewChoice> Targets { get; }

        public ICommand HireCommand { get; }
        public ICommand HireCrewCommand { get; }
        public ICommand AssignIdleCommand { get; }
        public ICommand TrainCommand { get; }
        public ICommand AssignCommand { get; }
        public ICommand UnassignCommand { get; }

        /// <summary>Your drivers, those without an coach first.</summary>
        public ObservableCollection<NamedChoice<Driver>> AssignDrivers { get; }

        /// <summary>The coach that need a driver and match the chosen driver's types.</summary>
        public ObservableCollection<NamedChoice<FleetCoach>> AssignTargets { get; }

        public string AssignHint { get; private set; }

        /// <summary>Drivers who finish training or trip school, or have no coach, are put on coach that need them every day.</summary>
        public bool AutoAssign
        {
            get { return GameObject.GetInstance().HumanCarrier?.AutoAssignDrivers ?? false; }
            set
            {
                var carrier = GameObject.GetInstance().HumanCarrier;
                if (carrier == null || carrier.AutoAssignDrivers == value)
                {
                    return;
                }

                carrier.AutoAssignDrivers = value;
                OnPropertyChanged(nameof(AutoAssign));
                if (value)
                {
                    int n = TripSchoolService.AssignIdleDrivers(carrier);
                    Status = "Automatic assignment is on" + (n > 0 ? ": " + n + " driver(s) without a coach were put on coaches that needed them." : ".");
                }
                else
                {
                    Status = "Automatic assignment is off. Assign drivers yourself below.";
                }

                Reload();
                _changed?.Invoke();
            }
        }

        public NamedChoice<Driver> AssignDriver
        {
            get { return _assignDriver; }
            set
            {
                if (SetProperty(ref _assignDriver, value))
                {
                    LoadAssignTargets();
                }
            }
        }

        public NamedChoice<FleetCoach> AssignTarget
        {
            get { return _assignTarget; }
            set { SetProperty(ref _assignTarget, value); }
        }

        private void LoadAssignDrivers()
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            var keep = _assignDriver?.Value;
            AssignDrivers.Clear();
            if (carrier != null)
            {
                foreach (var p in carrier.Drivers.Where(p => p.Training == null).OrderBy(p => p.Coach != null).ThenBy(p => p.Profile.Name))
                {
                    AssignDrivers.Add(
                        new NamedChoice<Driver>
                        {
                            Name = (p.Coach == null ? "[no coach] " : "") + p.Profile.Name + "  (" + (p.Coach == null ? "" : "runs " + p.Coach.Name + ", ") + string.Join(", ", p.Coaches ?? new List<string>()) + ")",
                            Value = p
                        });
                }
            }

            _assignDriver = AssignDrivers.FirstOrDefault(c => c.Value == keep) ?? AssignDrivers.FirstOrDefault();
            OnPropertyChanged(nameof(AssignDriver));
            LoadAssignTargets();
        }

        private void LoadAssignTargets()
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            var driver = _assignDriver?.Value;
            var keep = _assignTarget?.Value;
            AssignTargets.Clear();
            if (carrier != null && driver != null)
            {
                foreach (var f in TripSchoolService.AssignTargets(carrier, driver))
                {
                    AssignTargets.Add(
                        new NamedChoice<FleetCoach>
                        {
                            Name = string.Format("{0} - {1} ({2}/{3} drivers)", f.Name, f.Coach.Type.Name, f.Drivers.Count, f.Coach.Type.DriverCrew),
                            Value = f
                        });
                }
            }

            _assignTarget = AssignTargets.FirstOrDefault(c => c.Value == keep) ?? AssignTargets.FirstOrDefault();
            OnPropertyChanged(nameof(AssignTarget));
            AssignHint = driver == null
                ? "You have no driver to assign."
                : AssignTargets.Count == 0
                    ? "No coach with a free cab seat matches what " + driver.Profile.Name + " can run. Train him for another family, or buy a matching coach."
                    : "Choose the coach for " + driver.Profile.Name + ". A driver who runs another coach moves over.";
            OnPropertyChanged(nameof(AssignHint));
        }

        private void AssignSelected()
        {
            var driver = _assignDriver?.Value;
            var target = _assignTarget?.Value;
            string m;
            TripSchoolService.AssignDriver(GameObject.GetInstance().HumanCarrier, driver, target, out m);
            Status = m;
            Reload();
            _changed?.Invoke();
        }

        private void UnassignSelected()
        {
            string m;
            TripSchoolService.UnassignDriver(GameObject.GetInstance().HumanCarrier, _assignDriver?.Value, out m);
            Status = m;
            Reload();
            _changed?.Invoke();
        }
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand BonusCommand { get; }

        public ObservableCollection<NamedChoice<int>> PayLevels { get; }
        public string PayText { get; private set; }
        public string MoraleText { get; private set; }
        private NamedChoice<int> _payLevel;

        /// <summary>What the carrier pays its drivers compared to the market. Higher pay lifts morale and costs more every month.</summary>
        public NamedChoice<int> PayLevel
        {
            get { return _payLevel; }
            set
            {
                if (value == null || value == _payLevel) return;
                var carrier = GameObject.GetInstance().HumanCarrier;
                _payLevel = value;
                OnPropertyChanged(nameof(PayLevel));
                if (carrier == null || (int) Math.Round(carrier.DriverPayPercent <= 0 ? 100 : carrier.DriverPayPercent) == value.Value) return;
                string m;
                DriverMoraleService.SetPay(carrier, value.Value, out m);
                Status = m;
                LoadPay();
                _changed?.Invoke();
            }
        }

        private void LoadPay()
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            if (carrier == null) return;
            int pct = (int) Math.Round(carrier.DriverPayPercent <= 0 ? 100 : carrier.DriverPayPercent);
            pct = Math.Max(DriverMoraleService.MinPay, Math.Min(DriverMoraleService.MaxPay, pct / 10 * 10));
            _payLevel = PayLevels.FirstOrDefault(c => c.Value == pct) ?? PayLevels[0];
            OnPropertyChanged(nameof(PayLevel));
            double now = DriverMoraleService.Payroll(carrier, pct);
            double market = DriverMoraleService.Payroll(carrier, 100);
            PayText = string.Format("Monthly driver pay: {0} (market level: {1}). Each month morale drifts toward a target: higher pay raises it, drivers without a coach lose heart, long service helps.", Money(now), Money(market));
            var drivers = carrier.Drivers.ToList();
            int low = drivers.Count(p => p.Morale < 35);
            MoraleText = drivers.Count == 0
                ? "You have no drivers."
                : string.Format("Average morale {0:0} ({1}).{2}", DriverMoraleService.Average(carrier), DriverMoraleService.MoodText(DriverMoraleService.Average(carrier)), low > 0 ? " " + low + " driver(s) are close to quitting." : "");
            OnPropertyChanged(nameof(PayText));
            OnPropertyChanged(nameof(MoraleText));
        }

        private void AskBonus()
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            if (carrier == null) return;
            int wait = DriverMoraleService.DaysToNextBonus(carrier);
            if (wait > 0)
            {
                Status = "A bonus was paid recently. You can pay the next one in " + wait + " days.";
                return;
            }
            SetPending(
                string.Format("Pay every driver a one-month bonus? It costs {0} and lifts morale by 15. You can do it once every {1} days.", Money(DriverMoraleService.BonusCost(carrier)), DriverMoraleService.BonusWaitDays),
                () =>
                {
                    string m;
                    DriverMoraleService.PayBonus(carrier, out m);
                    Status = m;
                    Reload();
                    _changed?.Invoke();
                });
        }

        /// <summary>Drivers who can be sent to training for another coach family: by default those without an coach.</summary>
        public ObservableCollection<NamedChoice<Driver>> TrainDrivers { get; }

        /// <summary>The coach families the chosen driver cannot fly yet, with the days and the price.</summary>
        public ObservableCollection<NamedChoice<string>> TrainFamilies { get; }

        public bool HasPending => _pending != null;
        public string TrainHint { get; private set; }

        public string PendingText
        {
            get { return _pendingText; }
            private set { SetProperty(ref _pendingText, value); }
        }

        /// <summary>Also show drivers who fly an coach. A substitute driver is paid while they are away.</summary>
        public bool IncludeAssigned
        {
            get { return _includeAssigned; }
            set
            {
                if (SetProperty(ref _includeAssigned, value))
                {
                    LoadTrainDrivers();
                }
            }
        }

        public NamedChoice<Driver> TrainDriver
        {
            get { return _trainDriver; }
            set
            {
                if (SetProperty(ref _trainDriver, value))
                {
                    LoadTrainFamilies();
                }
            }
        }

        public NamedChoice<string> TrainFamily
        {
            get { return _trainFamily; }
            set
            {
                if (SetProperty(ref _trainFamily, value))
                {
                    UpdateTrainHint();
                }
            }
        }

        private void SetPending(string text, Action action)
        {
            _pending = action;
            PendingText = text;
            OnPropertyChanged(nameof(HasPending));
        }

        private static string Money(double v)
        {
            return v.ToString("C0", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        }

        private void LoadTrainDrivers()
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            var keep = _trainDriver?.Value;
            TrainDrivers.Clear();
            if (carrier != null)
            {
                foreach (var p in carrier.Drivers.Where(p => p.Training == null && (_includeAssigned || p.Coach == null)).OrderBy(p => p.Profile.Name))
                {
                    TrainDrivers.Add(
                        new NamedChoice<Driver>
                        {
                            Name = p.Profile.Name + "  (" + (p.Coach == null ? "no coach" : "runs " + p.Coach.Name) + ", " + string.Join(", ", p.Coaches ?? new List<string>()) + ")",
                            Value = p
                        });
                }
            }

            _trainDriver = TrainDrivers.FirstOrDefault(c => c.Value == keep) ?? TrainDrivers.FirstOrDefault();
            OnPropertyChanged(nameof(TrainDriver));
            LoadTrainFamilies();
        }

        private void LoadTrainFamilies()
        {
            var driver = _trainDriver?.Value;
            var keep = _trainFamily?.Value;
            TrainFamilies.Clear();
            if (driver != null)
            {
                var fleetFamilies = GameObject.GetInstance().HumanCarrier.Fleet.Select(f => f.Coach.Type.CoachFamily).ToList();
                // the families of your own fleet come first
                foreach (var f in TripSchoolService.TrainingFamilies(driver).OrderByDescending(f => fleetFamilies.Contains(f)).ThenBy(f => f))
                {
                    int days = CarrierHelpers.GetTrainingDays(driver, f);
                    TrainFamilies.Add(
                        new NamedChoice<string>
                        {
                            Name = f + (fleetFamilies.Contains(f) ? " (in your fleet)" : "") + "  -  " + days + " days, " + Money(TripSchoolService.GetTrainingCost(driver, f)),
                            Value = f
                        });
                }
            }

            _trainFamily = TrainFamilies.FirstOrDefault(c => c.Value == keep) ?? TrainFamilies.FirstOrDefault();
            OnPropertyChanged(nameof(TrainFamily));
            UpdateTrainHint();
        }

        private void UpdateTrainHint()
        {
            var driver = _trainDriver?.Value;
            TrainHint = driver == null
                ? (_includeAssigned ? "You have no driver who can be trained." : "None of your drivers is without a coach. Tick the box to also train drivers who run one.")
                : "Training takes 2 days for a family of a manufacturer the driver knows and 14 days otherwise. A training facility for the family (under Facilities) brings the price down to one day."
                  + (driver.Coach != null ? " " + driver.Coach.Name + " gets a substitute driver meanwhile, which is paid extra." : "");
            OnPropertyChanged(nameof(TrainHint));
        }

        private void AskTrain()
        {
            var driver = _trainDriver?.Value;
            var family = _trainFamily?.Value;
            if (driver == null || family == null)
            {
                Status = "Choose a driver and a coach family.";
                return;
            }

            SetPending(
                "Send " + driver.Profile.Name + " to train for the " + family + " for " + Money(TripSchoolService.GetTrainingCost(driver, family)) + "? It takes "
                + CarrierHelpers.GetTrainingDays(driver, family) + " days.",
                () =>
                {
                    string m;
                    TripSchoolService.TrainDriver(GameObject.GetInstance().HumanCarrier, driver, family, out m);
                    Status = m;
                    Reload();
                    _changed?.Invoke();
                });
        }


        public CrewChoice Target
        {
            get { return _target; }
            set
            {
                if (value != null && SetProperty(ref _target, value))
                {
                    LoadAvailable();
                    (HireCrewCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public DriverItem Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    (HireCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        /// <summary>Refreshes the lists after the fleet or crews changed.</summary>
        public void Reload()
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            if (carrier == null)
            {
                return;
            }

            var keep = _target?.Coach;
            Targets.Clear();
            Targets.Add(new CrewChoice { Name = "(any coach)" });
            foreach (var f in carrier.Fleet.Where(f => f.Drivers.Count < f.Coach.Type.DriverCrew).OrderBy(f => f.Name))
            {
                Targets.Add(
                    new CrewChoice
                    {
                        Name = string.Format("{0} - {1} ({2}/{3} drivers)", f.Name, f.Coach.Type.Name, f.Drivers.Count, f.Coach.Type.DriverCrew),
                        Coach = f
                    });
            }

            _target = Targets.FirstOrDefault(t => t.Coach == keep && keep != null) ?? Targets[0];
            OnPropertyChanged(nameof(Target));

            Hired.Clear();
            foreach (var p in carrier.Drivers.OrderBy(p => p.Profile.Name))
            {
                Hired.Add(new DriverItem(p));
            }

            LoadAvailable();
            LoadTrainDrivers();
            LoadAssignDrivers();
            LoadPay();
            OnPropertyChanged(nameof(AutoAssign));
            (HireCrewCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private List<Driver> Candidates()
        {
            var all = Drivers.GetUnassignedDrivers();
            var family = _target?.Coach?.Coach.Type.CoachFamily;
            //there are always drivers to hire: the market fills up when it runs short
            if (family != null)
            {
                if (DriverSupply.EnsureFamily(family) > 0)
                {
                    all = Drivers.GetUnassignedDrivers();
                }
            }
            else
            {
                DriverSupply.EnsureAnyDrivers();
                all = Drivers.GetUnassignedDrivers();
            }
            return all.Where(p => family == null || (p.Coaches != null && p.Coaches.Contains(family)))
                      .OrderBy(p => p.Profile.Name)
                      .ToList();
        }

        private void LoadAvailable()
        {
            Available.Clear();
            foreach (var p in Candidates().Take(300))
            {
                Available.Add(new DriverItem(p));
            }

            Selected = null;
        }

        private void Hire(Driver driver, FleetCoach coach)
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            carrier.AddDriver(driver);
            driver.CarrierSignedDate = GameObject.GetInstance().GameTime;

            // with no coach chosen the driver goes to the first coach of his type that needs a driver
            coach = coach ?? carrier.Fleet.FirstOrDefault(
                f => f.Drivers.Count < f.Coach.Type.DriverCrew
                     && driver.Coaches != null && driver.Coaches.Contains(f.Coach.Type.CoachFamily));

            if (coach != null && coach.Drivers.Count < coach.Coach.Type.DriverCrew)
            {
                coach.AddDriver(driver);
            }
        }

        private void AssignIdle()
        {
            int n = TripSchoolService.AssignIdleDrivers(GameObject.GetInstance().HumanCarrier);
            Status = n > 0 ? n + " driver(s) without a coach were put on coaches that needed them." : "No idle driver fits a coach that needs a driver.";
            Reload();
            _changed?.Invoke();
        }

        private void HireSelected()
        {
            if (_selected == null)
            {
                return;
            }

            var driver = _selected.Driver;
            Hire(driver, _target?.Coach);
            Status = "Hired " + driver.Profile.Name + (driver.Coach != null ? " for " + driver.Coach.Name : "") + ".";
            Reload();
            _changed?.Invoke();
        }

        private void HireCrew()
        {
            var coach = _target?.Coach;
            if (coach == null)
            {
                return;
            }

            int needed = coach.Coach.Type.DriverCrew - coach.Drivers.Count;
            var candidates = Candidates().Take(needed).ToList();
            foreach (var p in candidates)
            {
                Hire(p, coach);
            }

            Status = candidates.Count < needed
                ? "Only " + candidates.Count + " matching driver(s) were available for " + coach.Name + "."
                : "Hired a full crew for " + coach.Name + ".";
            Reload();
            _changed?.Invoke();
        }
    }
}
