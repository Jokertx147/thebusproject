using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.Pilots;

namespace TheBusProject.Presentation
{
    public class SchoolItem
    {
        public TripSchool School { get; set; }
        public string Title { get; set; }
        public string Info { get; set; }
        public override string ToString() { return Title; }
    }

    public class InstructorItem
    {
        public Instructor Instructor { get; set; }
        public string Text { get; set; }
        public string Detail { get; set; }
        public string ActionText { get; set; }
        public ICommand Command { get; set; }
    }

    public class TrainingCoachItem
    {
        public TrainingCoach Coach { get; set; }
        public string Text { get; set; }
        public string Detail { get; set; }
        public ICommand SellCommand { get; set; }
    }

    public class StudentItem
    {
        public PilotStudent Student { get; set; }
        public string Text { get; set; }
        public string Detail { get; set; }
        public ICommand CancelCommand { get; set; }
    }

    /// <summary>Trip schools: pilots of your own, trained by instructors in training coach.</summary>
    public class TripSchoolsViewModel : ObservableObject
    {
        private readonly Action _changed;
        private SchoolItem _selected;
        private NamedChoice<Station> _buildStation;
        private NamedChoice<TrainingCoachType> _buyType;
        private NamedChoice<Instructor> _studentInstructor;
        private string _family;
        private string _status, _pendingText;
        private Action _pending;

        public TripSchoolsViewModel(Action changed)
        {
            _changed = changed;
            Schools = new ObservableCollection<SchoolItem>();
            BuildStations = new ObservableCollection<NamedChoice<Station>>();
            Instructors = new ObservableCollection<InstructorItem>();
            Market = new ObservableCollection<InstructorItem>();
            Coach = new ObservableCollection<TrainingCoachItem>();
            CoachTypes = new ObservableCollection<NamedChoice<TrainingCoachType>>();
            Students = new ObservableCollection<StudentItem>();
            FreeInstructors = new ObservableCollection<NamedChoice<Instructor>>();
            Families = new ObservableCollection<string>();
            ConfirmCommand = new RelayCommand(Confirm);
            CancelCommand = new RelayCommand(() => SetPending(null, null));
            BuildCommand = new RelayCommand(AskBuild);
            CloseCommand = new RelayCommand(AskClose);
            BuyCoachCommand = new RelayCommand(AskBuyCoach);
            StartStudentCommand = new RelayCommand(AskStartStudent);
            Reload();
        }

        public ObservableCollection<SchoolItem> Schools { get; }
        public ObservableCollection<NamedChoice<Station>> BuildStations { get; }
        public ObservableCollection<InstructorItem> Instructors { get; }
        public ObservableCollection<InstructorItem> Market { get; }
        public ObservableCollection<TrainingCoachItem> Coach { get; }
        public ObservableCollection<NamedChoice<TrainingCoachType>> CoachTypes { get; }
        public ObservableCollection<StudentItem> Students { get; }
        public ObservableCollection<NamedChoice<Instructor>> FreeInstructors { get; }
        public ObservableCollection<string> Families { get; }

        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand BuildCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand BuyCoachCommand { get; }
        public ICommand StartStudentCommand { get; }

        public string Summary { get; private set; }
        public string SchoolInfo { get; private set; }
        public string StudentHint { get; private set; }
        public bool HasSchool => _selected != null;
        public bool HasSchools => Schools.Count > 0;
        public bool CanBuild => BuildStations.Count > 0;
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

        public SchoolItem Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    ReloadSchool();
                    OnPropertyChanged(nameof(HasSchool));
                }
            }
        }

        public NamedChoice<Station> BuildStation
        {
            get { return _buildStation; }
            set { SetProperty(ref _buildStation, value); }
        }

        public NamedChoice<TrainingCoachType> BuyType
        {
            get { return _buyType; }
            set { SetProperty(ref _buyType, value); }
        }

        public NamedChoice<Instructor> StudentInstructor
        {
            get { return _studentInstructor; }
            set { SetProperty(ref _studentInstructor, value); }
        }

        public string Family
        {
            get { return _family; }
            set { SetProperty(ref _family, value); }
        }

        private static string Money(double v)
        {
            return v.ToString("C0", CultureInfo.GetCultureInfo("en-US"));
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

        private void Done(string message)
        {
            Status = message;
            Reload();
            _changed?.Invoke();
        }

        public void Reload()
        {
            var game = GameObject.GetInstance();
            var carrier = game.HumanCarrier;
            if (carrier == null)
            {
                return;
            }

            PilotSupply.EnsureInstructors();
            var keep = _selected?.School;
            Schools.Clear();
            foreach (var s in carrier.TripSchools.OrderBy(s => s.Name))
            {
                Schools.Add(
                    new SchoolItem
                    {
                        School = s,
                        Title = s.Name + " (" + s.Station.Profile.IATACode + ")",
                        Info = string.Format(
                            "{0}/{1} instructors  |  {2} of {3} student places in use  |  {4} training coach",
                            s.NumberOfInstructors, TripSchool.MaxNumberOfInstructors, s.NumberOfStudents, TripSchoolService.Capacity(s), s.TrainingCoaches.Count)
                    });
            }

            _selected = Schools.FirstOrDefault(s => s.School == keep) ?? Schools.FirstOrDefault();
            OnPropertyChanged(nameof(Selected));
            OnPropertyChanged(nameof(HasSchool));
            OnPropertyChanged(nameof(HasSchools));

            var prev = _buildStation?.Value;
            BuildStations.Clear();
            foreach (var a in TripSchoolService.PossibleStations(carrier))
            {
                BuildStations.Add(new NamedChoice<Station> {Name = a.Profile.IATACode + "  " + a.Profile.Name + ", " + a.Profile.Town.Name, Value = a});
            }
            _buildStation = BuildStations.FirstOrDefault(b => b.Value == prev) ?? BuildStations.FirstOrDefault();
            OnPropertyChanged(nameof(BuildStation));
            OnPropertyChanged(nameof(CanBuild));

            int students = carrier.TripSchools.Sum(s => s.NumberOfStudents);
            int instructors = carrier.TripSchools.Sum(s => s.NumberOfInstructors);
            Summary = carrier.TripSchools.Count == 0
                ? "You have no trip school. Build one at a hub or at a station with a service facility, hire instructors, buy a training coach, and train pilots of your own for "
                  + Money(TripSchoolService.StudentPrice) + " each."
                : string.Format(
                    "{0} trip school(s)  |  {1} instructors  |  {2} students in training. A school costs {3} to build, a student {4}.",
                    carrier.TripSchools.Count, instructors, students, Money(TripSchoolService.BuildPrice), Money(TripSchoolService.StudentPrice));
            OnPropertyChanged(nameof(Summary));

            ReloadSchool();
        }

        private void ReloadSchool()
        {
            Instructors.Clear();
            Market.Clear();
            Coach.Clear();
            Students.Clear();
            FreeInstructors.Clear();
            CoachTypes.Clear();
            Families.Clear();

            var game = GameObject.GetInstance();
            var carrier = game.HumanCarrier;
            var school = _selected?.School;
            if (school == null || carrier == null)
            {
                SchoolInfo = "";
                StudentHint = "";
                OnPropertyChanged(nameof(SchoolInfo));
                OnPropertyChanged(nameof(StudentHint));
                return;
            }

            double salaryBase = carrier.Fees.GetValue(FeeTypes.GetType("Instructor Base Salary"));
            foreach (var i in school.Instructors.OrderBy(i => i.Profile.Name))
            {
                var instructor = i;
                Instructors.Add(
                    new InstructorItem
                    {
                        Instructor = instructor,
                        Text = instructor.Profile.Name + "  (rating " + instructor.Rating.Name + ")",
                        Detail = string.Format("{0}/{1} students  |  {2} a month", instructor.Students.Count, TripSchool.MaxNumberOfStudentsPerInstructor, Money(instructor.Rating.CostIndex*salaryBase)),
                        ActionText = "Dismiss",
                        Command = new RelayCommand(() => AskDismiss(school, instructor))
                    });
            }

            if (school.NumberOfInstructors < TripSchool.MaxNumberOfInstructors)
            {
                foreach (var i in TheBusProject.Models.Pilots.Instructors.GetUnassignedInstructors().OrderBy(i => i.Rating.CostIndex).ThenBy(i => i.Profile.Name).Take(60))
                {
                    var instructor = i;
                    Market.Add(
                        new InstructorItem
                        {
                            Instructor = instructor,
                            Text = instructor.Profile.Name + "  (rating " + instructor.Rating.Name + ")",
                            Detail = string.Format("age {0}  |  {1} a month", instructor.Profile.Age, Money(instructor.Rating.CostIndex*salaryBase)),
                            ActionText = "Hire",
                            Command = new RelayCommand(() => AskHire(school, instructor))
                        });
                }
            }

            foreach (var a in school.TrainingCoaches.OrderBy(a => a.Type.TypeLevel))
            {
                var coach = a;
                Coach.Add(
                    new TrainingCoachItem
                    {
                        Coach = coach,
                        Text = coach.Type.Name,
                        Detail = string.Format(
                            "{0} seats, {1} in use  |  bought {2}  |  sells for {3}",
                            coach.Type.MaxNumberOfStudents, coach.Type.MaxNumberOfStudents - TripSchoolService.FreeSeats(school, coach), DateFormatter.Date(coach.BoughtDate), Money(coach.Type.Price*0.75)),
                        SellCommand = new RelayCommand(() => AskSell(school, coach))
                    });
            }

            foreach (var t in TrainingCoachTypes.GetCoachTypes().OrderBy(t => t.TypeLevel))
            {
                CoachTypes.Add(new NamedChoice<TrainingCoachType> {Name = t.Name + "  -  " + Money(t.Price) + ", " + t.MaxNumberOfStudents + " seats", Value = t});
            }
            _buyType = CoachTypes.FirstOrDefault(c => c.Value == _buyType?.Value) ?? CoachTypes.FirstOrDefault();
            OnPropertyChanged(nameof(BuyType));

            foreach (var s in school.Students.OrderBy(s => s.EndDate))
            {
                var student = s;
                int left = Math.Max(0, (int) (student.EndDate.Date - game.GameTime.Date).TotalDays);
                Students.Add(
                    new StudentItem
                    {
                        Student = student,
                        Text = student.Profile.Name + "  -  " + student.CoachFamily,
                        Detail = string.Format(
                            "rating {0}  |  instructor {1}  |  {2}  |  graduates {3} ({4} days)",
                            student.Rating?.Name, student.Instructor?.Profile.Name, student.Coach?.Type.Name, DateFormatter.Date(student.EndDate), left),
                        CancelCommand = new RelayCommand(() => AskCancel(school, student))
                    });
            }

            foreach (var i in TripSchoolService.InstructorsWithRoom(school).OrderBy(i => i.Profile.Name))
            {
                FreeInstructors.Add(new NamedChoice<Instructor> {Name = i.Profile.Name + " (" + i.Students.Count + "/" + TripSchool.MaxNumberOfStudentsPerInstructor + ")", Value = i});
            }
            _studentInstructor = FreeInstructors.FirstOrDefault(c => c.Value == _studentInstructor?.Value) ?? FreeInstructors.FirstOrDefault();
            OnPropertyChanged(nameof(StudentInstructor));

            foreach (var f in TripSchoolService.Families())
            {
                Families.Add(f);
            }
            if (_family == null || !Families.Contains(_family))
            {
                _family = carrier.Fleet.Select(f => f.Coach.Type.CoachFamily).FirstOrDefault(f => Families.Contains(f)) ?? Families.FirstOrDefault();
            }
            OnPropertyChanged(nameof(Family));

            SchoolInfo = string.Format(
                "{0}  |  {1} instructors  |  {2} training seats  |  room for {3} students ({4} in training)",
                school.Name, school.NumberOfInstructors, TripSchoolService.Seats(school), TripSchoolService.Capacity(school), school.NumberOfStudents);
            StudentHint = TripSchoolService.GetStudentBlocker(carrier, school)
                          ?? "A student costs " + Money(TripSchoolService.StudentPrice) + " and graduates in about 70 to 150 days, depending on the rating. The pilot then joins your carrier and goes to a coach that needs a pilot of that family.";
            OnPropertyChanged(nameof(SchoolInfo));
            OnPropertyChanged(nameof(StudentHint));
        }

        private void AskBuild()
        {
            var station = _buildStation?.Value;
            if (station == null)
            {
                Status = "You have no station that can take a trip school: build it at a hub or at a station with a service facility.";
                return;
            }
            SetPending(
                "Build a trip school at " + station.Profile.Name + " for " + Money(TripSchoolService.BuildPrice) + "?",
                () =>
                {
                    string m;
                    var carrier = GameObject.GetInstance().HumanCarrier;
                    bool ok = TripSchoolService.Build(carrier, station, out m);
                    if (ok)
                    {
                        _selected = null;
                        Reload();
                        _selected = Schools.FirstOrDefault(s => s.School.Station == station) ?? _selected;
                        OnPropertyChanged(nameof(Selected));
                        ReloadSchool();
                        Status = m;
                        _changed?.Invoke();
                    }
                    else
                    {
                        Status = m;
                    }
                });
        }

        private void AskClose()
        {
            var school = _selected?.School;
            if (school == null) return;
            SetPending(
                "Close " + school.Name + "? Its " + school.NumberOfStudents + " student(s) are sent home and their fee is lost; the training coach are sold for 75% and the instructors go back to the market.",
                () =>
                {
                    string m;
                    TripSchoolService.Close(GameObject.GetInstance().HumanCarrier, school, out m);
                    _selected = null;
                    Done(m);
                });
        }

        private void AskHire(TripSchool school, Instructor instructor)
        {
            SetPending(
                "Hire " + instructor.Profile.Name + " (rating " + instructor.Rating.Name + ") as an instructor at " + school.Name + "?",
                () =>
                {
                    string m;
                    TripSchoolService.HireInstructor(GameObject.GetInstance().HumanCarrier, school, instructor, out m);
                    Done(m);
                });
        }

        private void AskDismiss(TripSchool school, Instructor instructor)
        {
            if (instructor.Students.Count > 0)
            {
                string m;
                TripSchoolService.DismissInstructor(school, instructor, out m);
                Status = m;
                return;
            }
            SetPending(
                "Dismiss " + instructor.Profile.Name + "?",
                () =>
                {
                    string m;
                    TripSchoolService.DismissInstructor(school, instructor, out m);
                    Done(m);
                });
        }

        private void AskBuyCoach()
        {
            var school = _selected?.School;
            var type = _buyType?.Value;
            if (school == null || type == null) return;
            SetPending(
                "Buy a " + type.Name + " for " + school.Name + " at " + Money(type.Price) + "? It gives " + type.MaxNumberOfStudents + " student seats.",
                () =>
                {
                    string m;
                    TripSchoolService.BuyCoach(GameObject.GetInstance().HumanCarrier, school, type, out m);
                    Done(m);
                });
        }

        private void AskSell(TripSchool school, TrainingCoach coach)
        {
            if (school.Students.Any(s => s.Coach == coach))
            {
                string m;
                TripSchoolService.SellCoach(GameObject.GetInstance().HumanCarrier, school, coach, out m);
                Status = m;
                return;
            }
            SetPending(
                "Sell the " + coach.Type.Name + " for " + Money(coach.Type.Price*0.75) + "?",
                () =>
                {
                    string m;
                    TripSchoolService.SellCoach(GameObject.GetInstance().HumanCarrier, school, coach, out m);
                    Done(m);
                });
        }

        private void AskStartStudent()
        {
            var school = _selected?.School;
            if (school == null) return;
            var carrier = GameObject.GetInstance().HumanCarrier;
            string blocker = TripSchoolService.GetStudentBlocker(carrier, school);
            if (blocker != null)
            {
                Status = blocker;
                return;
            }
            var instructor = _studentInstructor?.Value;
            if (instructor == null || string.IsNullOrEmpty(_family))
            {
                Status = "Choose an instructor and a coach family.";
                return;
            }
            SetPending(
                "Take in a new student with " + instructor.Profile.Name + ", trained for the " + _family + ", for " + Money(TripSchoolService.StudentPrice) + "?",
                () =>
                {
                    string m;
                    TripSchoolService.StartStudent(GameObject.GetInstance().HumanCarrier, school, instructor, _family, out m);
                    Done(m);
                });
        }

        private void AskCancel(TripSchool school, PilotStudent student)
        {
            SetPending(
                "Send " + student.Profile.Name + " home? The fee is not paid back.",
                () =>
                {
                    TripSchoolService.CancelStudent(school, student);
                    Done(student.Profile.Name + " left the school.");
                });
        }
    }
}
