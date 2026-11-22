using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.General;
using TheBusProject.Models.Pilots;

namespace TheBusProject.Presentation
{
    public class PilotItem
    {
        public PilotItem(Pilot pilot)
        {
            Pilot = pilot;
            IsIdle = pilot.Carrier != null && pilot.Coach == null;
            IsTraining = pilot.Training != null;
            Name = pilot.Profile.Name;
            Details = string.Format(
                "age {0}  |  {1}  |  {2}",
                pilot.Profile.Age, pilot.Rating?.Name, string.Join(", ", pilot.Coaches ?? new List<string>()));
            Mood = pilot.Carrier == null ? null : string.Format("morale {0:0} ({1})", pilot.Morale, PilotMoraleService.MoodText(pilot.Morale));
            IsUnhappy = pilot.Carrier != null && pilot.Morale < 35;
            Assignment = (pilot.Coach == null ? "not assigned" : "runs " + pilot.Coach.Name)
                         + (pilot.Training != null ? "  |  training for the " + pilot.Training.CoachFamily + " until " + DateFormatter.Date(pilot.Training.EndDate) : "");
        }

        public Pilot Pilot { get; }

        /// <summary>One of your pilots who has no coach.</summary>
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

    /// <summary>Hire pilots and put them on coach. An coach needs a full cockpit crew before it can fly.</summary>
    public class PilotsViewModel : ObservableObject
    {
        private readonly Action _changed;
        private CrewChoice _target;
        private PilotItem _selected;
        private string _status;
        private string _pendingText;
        private Action _pending;
        private bool _includeAssigned;
        private NamedChoice<Pilot> _trainPilot;
        private NamedChoice<string> _trainFamily;
        private NamedChoice<Pilot> _assignPilot;
        private NamedChoice<FleetCoach> _assignTarget;

        public PilotsViewModel(Action changed)
        {
            _changed = changed;
            Available = new ObservableCollection<PilotItem>();
            Hired = new ObservableCollection<PilotItem>();
            Targets = new ObservableCollection<CrewChoice>();
            HireCommand = new RelayCommand(HireSelected, () => _selected != null);
            HireCrewCommand = new RelayCommand(HireCrew, () => _target?.Coach != null);
            AssignIdleCommand = new RelayCommand(AssignIdle);
            TrainPilots = new ObservableCollection<NamedChoice<Pilot>>();
            TrainFamilies = new ObservableCollection<NamedChoice<string>>();
            TrainCommand = new RelayCommand(AskTrain);
            AssignPilots = new ObservableCollection<NamedChoice<Pilot>>();
            AssignTargets = new ObservableCollection<NamedChoice<FleetCoach>>();
            AssignCommand = new RelayCommand(AssignSelected);
            UnassignCommand = new RelayCommand(UnassignSelected);
            ConfirmCommand = new RelayCommand(() => { var a = _pending; SetPending(null, null); a?.Invoke(); });
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            PayLevels = new ObservableCollection<NamedChoice<int>>();
            for (int pct = PilotMoraleService.MinPay; pct <= PilotMoraleService.MaxPay; pct += 10)
            {
                PayLevels.Add(new NamedChoice<int> { Name = pct + "%" + (pct == 100 ? " (market)" : ""), Value = pct });
            }
            BonusCommand = new RelayCommand(AskBonus);
            Reload();
        }

        public ObservableCollection<PilotItem> Available { get; }
        public ObservableCollection<PilotItem> Hired { get; }

        /// <summary>The coach that still need pilots; "any" shows every pilot for hire.</summary>
        public ObservableCollection<CrewChoice> Targets { get; }

        public ICommand HireCommand { get; }
        public ICommand HireCrewCommand { get; }
        public ICommand AssignIdleCommand { get; }
        public ICommand TrainCommand { get; }
        public ICommand AssignCommand { get; }
        public ICommand UnassignCommand { get; }

        /// <summary>Your pilots, those without an coach first.</summary>
        public ObservableCollection<NamedChoice<Pilot>> AssignPilots { get; }

        /// <summary>The coach that need a pilot and match the chosen pilot's types.</summary>
        public ObservableCollection<NamedChoice<FleetCoach>> AssignTargets { get; }

        public string AssignHint { get; private set; }

        /// <summary>Pilots who finish training or trip school, or have no coach, are put on coach that need them every day.</summary>
        public bool AutoAssign
        {
            get { return GameObject.GetInstance().HumanCarrier?.AutoAssignPilots ?? false; }
            set
            {
                var carrier = GameObject.GetInstance().HumanCarrier;
                if (carrier == null || carrier.AutoAssignPilots == value)
                {
                    return;
                }

                carrier.AutoAssignPilots = value;
                OnPropertyChanged(nameof(AutoAssign));
                if (value)
                {
                    int n = TripSchoolService.AssignIdlePilots(carrier);
                    Status = "Automatic assignment is on" + (n > 0 ? ": " + n + " pilot(s) without a coach were put on coach that needed them." : ".");
                }
                else
                {
                    Status = "Automatic assignment is off. Assign pilots yourself below.";
                }

                Reload();
                _changed?.Invoke();
            }
        }

        public NamedChoice<Pilot> AssignPilot
        {
            get { return _assignPilot; }
            set
            {
                if (SetProperty(ref _assignPilot, value))
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

        private void LoadAssignPilots()
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            var keep = _assignPilot?.Value;
            AssignPilots.Clear();
            if (carrier != null)
            {
                foreach (var p in carrier.Pilots.Where(p => p.Training == null).OrderBy(p => p.Coach != null).ThenBy(p => p.Profile.Name))
                {
                    AssignPilots.Add(
                        new NamedChoice<Pilot>
                        {
                            Name = (p.Coach == null ? "[no coach] " : "") + p.Profile.Name + "  (" + (p.Coach == null ? "" : "runs " + p.Coach.Name + ", ") + string.Join(", ", p.Coaches ?? new List<string>()) + ")",
                            Value = p
                        });
                }
            }

            _assignPilot = AssignPilots.FirstOrDefault(c => c.Value == keep) ?? AssignPilots.FirstOrDefault();
            OnPropertyChanged(nameof(AssignPilot));
            LoadAssignTargets();
        }

        private void LoadAssignTargets()
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            var pilot = _assignPilot?.Value;
            var keep = _assignTarget?.Value;
            AssignTargets.Clear();
            if (carrier != null && pilot != null)
            {
                foreach (var f in TripSchoolService.AssignTargets(carrier, pilot))
                {
                    AssignTargets.Add(
                        new NamedChoice<FleetCoach>
                        {
                            Name = string.Format("{0} - {1} ({2}/{3} pilots)", f.Name, f.Coach.Type.Name, f.Pilots.Count, f.Coach.Type.CockpitCrew),
                            Value = f
                        });
                }
            }

            _assignTarget = AssignTargets.FirstOrDefault(c => c.Value == keep) ?? AssignTargets.FirstOrDefault();
            OnPropertyChanged(nameof(AssignTarget));
            AssignHint = pilot == null
                ? "You have no pilot to assign."
                : AssignTargets.Count == 0
                    ? "No coach with a free cockpit seat matches what " + pilot.Profile.Name + " can run. Train him for another family, or buy a matching coach."
                    : "Choose the coach for " + pilot.Profile.Name + ". A pilot who runs another coach moves over.";
            OnPropertyChanged(nameof(AssignHint));
        }

        private void AssignSelected()
        {
            var pilot = _assignPilot?.Value;
            var target = _assignTarget?.Value;
            string m;
            TripSchoolService.AssignPilot(GameObject.GetInstance().HumanCarrier, pilot, target, out m);
            Status = m;
            Reload();
            _changed?.Invoke();
        }

        private void UnassignSelected()
        {
            string m;
            TripSchoolService.UnassignPilot(GameObject.GetInstance().HumanCarrier, _assignPilot?.Value, out m);
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

        /// <summary>What the carrier pays its pilots compared to the market. Higher pay lifts morale and costs more every month.</summary>
        public NamedChoice<int> PayLevel
        {
            get { return _payLevel; }
            set
            {
                if (value == null || value == _payLevel) return;
                var carrier = GameObject.GetInstance().HumanCarrier;
                _payLevel = value;
                OnPropertyChanged(nameof(PayLevel));
                if (carrier == null || (int) Math.Round(carrier.PilotPayPercent <= 0 ? 100 : carrier.PilotPayPercent) == value.Value) return;
                string m;
                PilotMoraleService.SetPay(carrier, value.Value, out m);
                Status = m;
                LoadPay();
                _changed?.Invoke();
            }
        }

        private void LoadPay()
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            if (carrier == null) return;
            int pct = (int) Math.Round(carrier.PilotPayPercent <= 0 ? 100 : carrier.PilotPayPercent);
            pct = Math.Max(PilotMoraleService.MinPay, Math.Min(PilotMoraleService.MaxPay, pct / 10 * 10));
            _payLevel = PayLevels.FirstOrDefault(c => c.Value == pct) ?? PayLevels[0];
            OnPropertyChanged(nameof(PayLevel));
            double now = PilotMoraleService.Payroll(carrier, pct);
            double market = PilotMoraleService.Payroll(carrier, 100);
            PayText = string.Format("Monthly pilot pay: {0} (market level: {1}). Each month morale drifts toward a target: higher pay raises it, pilots without a coach lose heart, long service helps.", Money(now), Money(market));
            var pilots = carrier.Pilots.ToList();
            int low = pilots.Count(p => p.Morale < 35);
            MoraleText = pilots.Count == 0
                ? "You have no pilots."
                : string.Format("Average morale {0:0} ({1}).{2}", PilotMoraleService.Average(carrier), PilotMoraleService.MoodText(PilotMoraleService.Average(carrier)), low > 0 ? " " + low + " pilot(s) are close to quitting." : "");
            OnPropertyChanged(nameof(PayText));
            OnPropertyChanged(nameof(MoraleText));
        }

        private void AskBonus()
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            if (carrier == null) return;
            int wait = PilotMoraleService.DaysToNextBonus(carrier);
            if (wait > 0)
            {
                Status = "A bonus was paid recently. You can pay the next one in " + wait + " days.";
                return;
            }
            SetPending(
                string.Format("Pay every pilot a one-month bonus? It costs {0} and lifts morale by 15. You can do it once every {1} days.", Money(PilotMoraleService.BonusCost(carrier)), PilotMoraleService.BonusWaitDays),
                () =>
                {
                    string m;
                    PilotMoraleService.PayBonus(carrier, out m);
                    Status = m;
                    Reload();
                    _changed?.Invoke();
                });
        }

        /// <summary>Pilots who can be sent to training for another coach family: by default those without an coach.</summary>
        public ObservableCollection<NamedChoice<Pilot>> TrainPilots { get; }

        /// <summary>The coach families the chosen pilot cannot fly yet, with the days and the price.</summary>
        public ObservableCollection<NamedChoice<string>> TrainFamilies { get; }

        public bool HasPending => _pending != null;
        public string TrainHint { get; private set; }

        public string PendingText
        {
            get { return _pendingText; }
            private set { SetProperty(ref _pendingText, value); }
        }

        /// <summary>Also show pilots who fly an coach. A substitute pilot is paid while they are away.</summary>
        public bool IncludeAssigned
        {
            get { return _includeAssigned; }
            set
            {
                if (SetProperty(ref _includeAssigned, value))
                {
                    LoadTrainPilots();
                }
            }
        }

        public NamedChoice<Pilot> TrainPilot
        {
            get { return _trainPilot; }
            set
            {
                if (SetProperty(ref _trainPilot, value))
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

        private void LoadTrainPilots()
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            var keep = _trainPilot?.Value;
            TrainPilots.Clear();
            if (carrier != null)
            {
                foreach (var p in carrier.Pilots.Where(p => p.Training == null && (_includeAssigned || p.Coach == null)).OrderBy(p => p.Profile.Name))
                {
                    TrainPilots.Add(
                        new NamedChoice<Pilot>
                        {
                            Name = p.Profile.Name + "  (" + (p.Coach == null ? "no coach" : "runs " + p.Coach.Name) + ", " + string.Join(", ", p.Coaches ?? new List<string>()) + ")",
                            Value = p
                        });
                }
            }

            _trainPilot = TrainPilots.FirstOrDefault(c => c.Value == keep) ?? TrainPilots.FirstOrDefault();
            OnPropertyChanged(nameof(TrainPilot));
            LoadTrainFamilies();
        }

        private void LoadTrainFamilies()
        {
            var pilot = _trainPilot?.Value;
            var keep = _trainFamily?.Value;
            TrainFamilies.Clear();
            if (pilot != null)
            {
                var fleetFamilies = GameObject.GetInstance().HumanCarrier.Fleet.Select(f => f.Coach.Type.CoachFamily).ToList();
                // the families of your own fleet come first
                foreach (var f in TripSchoolService.TrainingFamilies(pilot).OrderByDescending(f => fleetFamilies.Contains(f)).ThenBy(f => f))
                {
                    int days = CarrierHelpers.GetTrainingDays(pilot, f);
                    TrainFamilies.Add(
                        new NamedChoice<string>
                        {
                            Name = f + (fleetFamilies.Contains(f) ? " (in your fleet)" : "") + "  -  " + days + " days, " + Money(TripSchoolService.GetTrainingCost(pilot, f)),
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
            var pilot = _trainPilot?.Value;
            TrainHint = pilot == null
                ? (_includeAssigned ? "You have no pilot who can be trained." : "None of your pilots is without a coach. Tick the box to also train pilots who run one.")
                : "Training takes 2 days for a family of a manufacturer the pilot knows and 14 days otherwise. A training facility for the family (under Facilities) brings the price down to one day."
                  + (pilot.Coach != null ? " " + pilot.Coach.Name + " gets a substitute pilot meanwhile, which is paid extra." : "");
            OnPropertyChanged(nameof(TrainHint));
        }

        private void AskTrain()
        {
            var pilot = _trainPilot?.Value;
            var family = _trainFamily?.Value;
            if (pilot == null || family == null)
            {
                Status = "Choose a pilot and a coach family.";
                return;
            }

            SetPending(
                "Send " + pilot.Profile.Name + " to train for the " + family + " for " + Money(TripSchoolService.GetTrainingCost(pilot, family)) + "? It takes "
                + CarrierHelpers.GetTrainingDays(pilot, family) + " days.",
                () =>
                {
                    string m;
                    TripSchoolService.TrainPilot(GameObject.GetInstance().HumanCarrier, pilot, family, out m);
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

        public PilotItem Selected
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
            foreach (var f in carrier.Fleet.Where(f => f.Pilots.Count < f.Coach.Type.CockpitCrew).OrderBy(f => f.Name))
            {
                Targets.Add(
                    new CrewChoice
                    {
                        Name = string.Format("{0} - {1} ({2}/{3} pilots)", f.Name, f.Coach.Type.Name, f.Pilots.Count, f.Coach.Type.CockpitCrew),
                        Coach = f
                    });
            }

            _target = Targets.FirstOrDefault(t => t.Coach == keep && keep != null) ?? Targets[0];
            OnPropertyChanged(nameof(Target));

            Hired.Clear();
            foreach (var p in carrier.Pilots.OrderBy(p => p.Profile.Name))
            {
                Hired.Add(new PilotItem(p));
            }

            LoadAvailable();
            LoadTrainPilots();
            LoadAssignPilots();
            LoadPay();
            OnPropertyChanged(nameof(AutoAssign));
            (HireCrewCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private List<Pilot> Candidates()
        {
            var all = Pilots.GetUnassignedPilots();
            var family = _target?.Coach?.Coach.Type.CoachFamily;
            //there are always pilots to hire: the market fills up when it runs short
            if (family != null)
            {
                if (PilotSupply.EnsureFamily(family) > 0)
                {
                    all = Pilots.GetUnassignedPilots();
                }
            }
            else
            {
                PilotSupply.EnsureAnyPilots();
                all = Pilots.GetUnassignedPilots();
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
                Available.Add(new PilotItem(p));
            }

            Selected = null;
        }

        private void Hire(Pilot pilot, FleetCoach coach)
        {
            var carrier = GameObject.GetInstance().HumanCarrier;
            carrier.AddPilot(pilot);
            pilot.CarrierSignedDate = GameObject.GetInstance().GameTime;

            // with no coach chosen the pilot goes to the first coach of his type that needs a pilot
            coach = coach ?? carrier.Fleet.FirstOrDefault(
                f => f.Pilots.Count < f.Coach.Type.CockpitCrew
                     && pilot.Coaches != null && pilot.Coaches.Contains(f.Coach.Type.CoachFamily));

            if (coach != null && coach.Pilots.Count < coach.Coach.Type.CockpitCrew)
            {
                coach.AddPilot(pilot);
            }
        }

        private void AssignIdle()
        {
            int n = TripSchoolService.AssignIdlePilots(GameObject.GetInstance().HumanCarrier);
            Status = n > 0 ? n + " pilot(s) without a coach were put on coach that needed them." : "No idle pilot fits a coach that needs a pilot.";
            Reload();
            _changed?.Invoke();
        }

        private void HireSelected()
        {
            if (_selected == null)
            {
                return;
            }

            var pilot = _selected.Pilot;
            Hire(pilot, _target?.Coach);
            Status = "Hired " + pilot.Profile.Name + (pilot.Coach != null ? " for " + pilot.Coach.Name : "") + ".";
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

            int needed = coach.Coach.Type.CockpitCrew - coach.Pilots.Count;
            var candidates = Candidates().Take(needed).ToList();
            foreach (var p in candidates)
            {
                Hire(p, coach);
            }

            Status = candidates.Count < needed
                ? "Only " + candidates.Count + " matching pilot(s) were available for " + coach.Name + "."
                : "Hired a full crew for " + coach.Name + ".";
            Reload();
            _changed?.Invoke();
        }
    }
}
