using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheBusProject.Helpers;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.Routes;

namespace TheBusProject.Presentation
{
    public class CoachOption
    {
        public FleetCoach Coach { get; set; }
        public string Text { get; set; }
    }

    public class RouteOption
    {
        public Route Route { get; set; }
        public string Text { get; set; }
    }

    public class StationOption
    {
        public Station Station { get; set; }
        public string Text { get; set; }
    }

    /// <summary>One coach's week, kept so a change to it can be taken back.</summary>
    internal class TimetableMemo
    {
        public string Label { get; set; }
        public FleetCoach Coach { get; set; }
        public List<MemoLine> Lines { get; set; }
    }

    internal class MemoLine
    {
        public Route Route { get; set; }
        public DayOfWeek Day { get; set; }
        public TimeSpan Time { get; set; }
        public string Code { get; set; }
        public Station To { get; set; }
    }

    public class DayChoice
    {
        public DayOfWeek Day { get; set; }
        public string Text { get; set; }
    }

    public class TimeChoice
    {
        public TimeSpan Time { get; set; }
        public string Text { get; set; }
    }

    public class DayFlag : ObservableObject
    {
        private bool _checked;
        public DayOfWeek Day { get; set; }
        public string Name { get; set; }

        public bool IsChecked
        {
            get { return _checked; }
            set { SetProperty(ref _checked, value); }
        }
    }

    public class TimetableRow
    {
        public RouteTimeTableEntry Entry { get; set; }
        public string Day { get; set; }
        public string Time { get; set; }
        public string Arrives { get; set; }
        public string Trip { get; set; }
        public string Route { get; set; }
        public ICommand RemoveCommand { get; set; }
    }

    /// <summary>A trip drawn on the weekly timeline: a day, a start and an end in minutes of that day.</summary>
    public class TimelineBlock
    {
        public int Day { get; set; }
        public double Start { get; set; }
        public double End { get; set; }
        public string Label { get; set; }
        public string Detail { get; set; }
        public int ColorIndex { get; set; }
        public bool IsPreview { get; set; }
        public RouteTimeTableEntry Entry { get; set; }

        /// <summary>The whole trip's length, which a block cut in two at midnight does not show.</summary>
        public double DurationMinutes { get; set; }

        /// <summary>
        ///     A stretch of ground time rather than a trip: the coach is at the gate being turned round, and
        ///     cannot leave again until it has passed. Drawn faint behind the trips.
        /// </summary>
        public bool IsGround { get; set; }
    }

    /// <summary>Edit the weekly schedule of one coach: add or remove trips on its routes.</summary>
    public class TimetableViewModel : ObservableObject
    {
        private readonly Action _changed;
        private CoachOption _coach;
        private RouteOption _route;
        private StationOption _from;
        private TimeChoice _time;
        private int _turnaround = 45;
        private string _tripCode = "";
        private string _status;
        private string _summary;
        private List<StationOption> _directions = new List<StationOption>();
        private List<TimelineBlock> _blocks = new List<TimelineBlock>();
        private List<TimelineBlock> _preview = new List<TimelineBlock>();
        private List<TimelineBlock> _ground = new List<TimelineBlock>();
        private List<TimelineBlock> _previewGround = new List<TimelineBlock>();
        private TimelineBlock _selectedBlock;
        private List<RouteTimeTableEntry> _justAdded = new List<RouteTimeTableEntry>();
        private readonly HashSet<RouteTimeTableEntry> _selected = new HashSet<RouteTimeTableEntry>();

        // What the player last chose to add, which is not the same as what is chosen right now. Rebuilding the
        // coach list empties the box bound to it, and the empty box writes its own null back before the list is
        // filled again, which wiped the choice and sent the route box back to the first route after every trip
        // added. These hold the choice across that, so adding four trips on one route needs the route picked once.
        private Route _wantRoute;
        private Station _wantFrom;

        // One step back is this coach's whole week, which is a short list and makes every change undoable the same
        // way, whether it added, moved, renumbered, copied or removed trips.
        private readonly List<TimetableMemo> _memos = new List<TimetableMemo>();
        private const int MemoDepth = 25;
        private TimetableRow _selectedRow;
        private DayChoice _editDay;
        private TimeChoice _editTime;
        private List<TimeChoice> _editTimes = new List<TimeChoice>();
        private string _editTripCode = "";
        private bool _syncing;

        public TimetableViewModel(Action changed = null)
        {
            _changed = changed;
            Coach = new ObservableCollection<CoachOption>();
            Rows = new ObservableCollection<TimetableRow>();
            Routes = new ObservableCollection<RouteOption>();
            Days = new List<DayFlag>();
            foreach (var d in new[]
                     {
                         DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                         DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
                     })
            {
                var flag = new DayFlag { Day = d, Name = d.ToString().Substring(0, 3), IsChecked = true };
                flag.PropertyChanged += (s, e) => BuildPreview();
                Days.Add(flag);
            }

            Times = new List<TimeChoice>();
            for (int m = 0; m < 24 * 60; m += 15)
            {
                var t = new TimeSpan(0, m, 0);
                Times.Add(new TimeChoice { Time = t, Text = t.ToString(@"hh\:mm") });
            }

            _time = Times.First(t => t.Time == new TimeSpan(8, 0, 0));
            _editTimes = Times;
            TurnaroundOptions = RouteCreator.TurnaroundOptions;
            DayChoices = Days.Select(d => new DayChoice { Day = d.Day, Text = d.Day.ToString() }).ToList();

            AddRoundTripCommand = new RelayCommand(() => Add(true), () => _coach != null && _route != null);
            AddOneWayCommand = new RelayCommand(() => Add(false), () => _coach != null && _route != null);
            RemoveRouteTripsCommand = new RelayCommand(RemoveRouteTrips, () => _coach != null && _route != null);
            RemoveAllTripsCommand = new RelayCommand(AskRemoveAll);
            ConfirmRemoveAllCommand = new RelayCommand(RemoveAllTrips);
            CancelRemoveAllCommand = new RelayCommand(() => RemoveAllText = null);
            RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => _selected.Count > 0);
            ApplyEditCommand = new RelayCommand(ApplyEdit, () => _selected.Count == 1 && _selectedBlock?.Entry != null);
            UndoCommand = new RelayCommand(Undo, () => _memos.Count > 0);
            DailyCommand = new RelayCommand(() => SetDays(Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>().ToArray()));
            WeekdaysCommand = new RelayCommand(
                () => SetDays(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday));
            WeekendCommand = new RelayCommand(() => SetDays(DayOfWeek.Saturday, DayOfWeek.Sunday));
            MwfCommand = new RelayCommand(() => SetDays(DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday));
            TtsCommand = new RelayCommand(() => SetDays(DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday));

            ReloadCoach();
        }

        public ObservableCollection<CoachOption> Coach { get; }
        public ObservableCollection<TimetableRow> Rows { get; }
        public ObservableCollection<RouteOption> Routes { get; }
        public List<DayFlag> Days { get; }
        public List<TimeChoice> Times { get; }
        public List<int> TurnaroundOptions { get; }
        public List<DayChoice> DayChoices { get; }

        public ICommand AddRoundTripCommand { get; }
        public ICommand AddOneWayCommand { get; }
        public ICommand RemoveRouteTripsCommand { get; }
        public ICommand RemoveAllTripsCommand { get; }
        public ICommand ConfirmRemoveAllCommand { get; }
        public ICommand CancelRemoveAllCommand { get; }
        private string _removeAllText;
        public bool HasRemoveAll => _removeAllText != null;

        public string RemoveAllText
        {
            get { return _removeAllText; }
            private set { if (SetProperty(ref _removeAllText, value)) OnPropertyChanged(nameof(HasRemoveAll)); }
        }

        private void AskRemoveAll()
        {
            if (_coach == null) return;
            var f = _coach.Coach;
            int n = f.Routes.Sum(r => r.TimeTable.Entries.Count(e => e.Coach == f));
            if (n == 0)
            {
                Status = f.Name + " has no trips in a timetable.";
                return;
            }
            RemoveAllText = "Remove all " + n + " trip(s) of " + f.Name + " on its " + f.Routes.Count + " route(s)? The coach is stopped and keeps no schedule. The routes stay and can be run by another coach.";
        }

        private void RemoveAllTrips()
        {
            RemoveAllText = null;
            if (_coach == null) return;
            var f = _coach.Coach;
            int n = f.Routes.Sum(r => r.TimeTable.Entries.Count(e => e.Coach == f));
            var memo = Snapshot("clearing " + f.Name + "'s week");
            foreach (var route in f.Routes.ToList())
            {
                f.RemoveRoute(route);
            }
            Remember(memo);
            Finish(n + " trip(s) removed from " + f.Name + ".");
        }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand ApplyEditCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand DailyCommand { get; }
        public ICommand WeekdaysCommand { get; }
        public ICommand WeekendCommand { get; }
        public ICommand MwfCommand { get; }
        public ICommand TtsCommand { get; }

        public CoachOption SelectedCoach
        {
            get { return _coach; }
            set
            {
                if (SetProperty(ref _coach, value))
                {
                    LoadRoutes();
                    LoadRows();
                    RaiseCanExecute();
                }
            }
        }

        /// <summary>Raised when the timeline has to be drawn again.</summary>
        public event Action TimelineChanged;

        public List<TimelineBlock> Blocks => _blocks;
        public List<TimelineBlock> Preview => _preview;

        /// <summary>The time each trip spends at the gate afterwards, drawn behind the trips.</summary>
        public List<TimelineBlock> Ground => _ground;

        public List<TimelineBlock> PreviewGround => _previewGround;

        /// <summary>How long the chosen coach needs on the ground between trips.</summary>
        public int GroundMinutes => _coach == null ? 0 : (int) FleetCoachHelpers.GetMinTimeBetweenTrips(_coach.Coach).TotalMinutes;

        /// <summary>
        ///     The trip the edit boxes act on: the last one clicked. Setting it picks that trip alone, as a plain
        ///     click does; SelectAlso and SelectRange build a selection of several.
        /// </summary>
        public TimelineBlock SelectedBlock
        {
            get { return _selectedBlock; }
            set
            {
                _selected.Clear();
                if (value?.Entry != null) _selected.Add(value.Entry);
                _selectedBlock = value;
                OnPropertyChanged(nameof(SelectedBlock));
                AfterSelectionChanged();
                TimelineChanged?.Invoke();
            }
        }

        /// <summary>Every trip picked, which a drag moves together and the remove button removes together.</summary>
        public IReadOnlyCollection<RouteTimeTableEntry> SelectedEntries => _selected;

        public int SelectedCount => _selected.Count;

        public bool HasSelection => _selected.Count > 0;

        /// <summary>The remove button says how many trips it would take away.</summary>
        public string RemoveSelectedText => _selected.Count > 1 ? "Remove these " + _selected.Count + " trips" : "Remove this trip";

        public bool IsSelected(RouteTimeTableEntry entry)
        {
            return entry != null && _selected.Contains(entry);
        }

        /// <summary>Adds a trip to the picked ones, or takes it out again: what ctrl-clicking does.</summary>
        public void SelectAlso(TimelineBlock block)
        {
            var entry = block?.Entry;
            if (entry == null) return;
            if (!_selected.Remove(entry))
            {
                _selected.Add(entry);
                _selectedBlock = block;
            }
            else if (_selectedBlock?.Entry == entry)
            {
                _selectedBlock = _blocks.FirstOrDefault(b => _selected.Contains(b.Entry));
            }

            OnPropertyChanged(nameof(SelectedBlock));
            AfterSelectionChanged();
            TimelineChanged?.Invoke();
        }

        /// <summary>Picks everything between the trip already picked and this one, as shift-clicking does.</summary>
        public void SelectRange(TimelineBlock block)
        {
            if (block?.Entry == null) return;
            var from = _selectedBlock?.Entry;
            if (from == null)
            {
                SelectedBlock = block;
                return;
            }

            double a = WeekMinutes(from), b = WeekMinutes(block.Entry);
            double lo = Math.Min(a, b), hi = Math.Max(a, b);
            Replace(CoachEntries().Where(e => WeekMinutes(e) >= lo && WeekMinutes(e) <= hi), block);
        }

        /// <summary>Picks exactly these trips: what a rubber band drawn over the timeline leaves picked.</summary>
        public void SelectBlocks(IEnumerable<RouteTimeTableEntry> entries, bool addToSelection)
        {
            var list = (entries ?? Enumerable.Empty<RouteTimeTableEntry>()).Where(e => e != null).ToList();
            if (addToSelection) list.AddRange(_selected);
            var primary = list.OrderBy(WeekMinutes).FirstOrDefault();
            Replace(list, primary == null ? null : _blocks.FirstOrDefault(b => b.Entry == primary));
        }

        private void Replace(IEnumerable<RouteTimeTableEntry> entries, TimelineBlock primary)
        {
            _selected.Clear();
            foreach (var e in entries) _selected.Add(e);
            _selectedBlock = primary ?? _blocks.FirstOrDefault(b => _selected.Contains(b.Entry));
            OnPropertyChanged(nameof(SelectedBlock));
            AfterSelectionChanged();
            TimelineChanged?.Invoke();
        }

        /// <summary>Where a trip sits in the week, counting from Monday morning, so trips can be put in order.</summary>
        public static double WeekMinutes(RouteTimeTableEntry entry)
        {
            return DayIndex(entry.Day)*1440.0 + entry.Time.TotalMinutes;
        }

        private void RemoveSelected()
        {
            var going = _selected.ToList();
            if (going.Count == 0) return;
            if (going.Count == 1)
            {
                RemoveEntry(going[0]);
                return;
            }

            var memo = Snapshot("removing " + going.Count + " trips");
            foreach (var e in going) RemoveEntryQuietly(e);
            Remember(memo);
            _selected.Clear();
            _selectedBlock = null;
            Finish(going.Count + " trips removed.");
        }

        /// <summary>The trip picked in the list below the timeline; the two pick the same trip.</summary>
        public TimetableRow SelectedRow
        {
            get { return _selectedRow; }
            set
            {
                if (!SetProperty(ref _selectedRow, value) || _syncing) return;
                _syncing = true;
                SelectedBlock = value?.Entry == null ? null : _blocks.FirstOrDefault(b => b.Entry == value.Entry);
                _syncing = false;
            }
        }

        public bool CanEditSelected => _selected.Count == 1 && _selectedBlock?.Entry != null;

        /// <summary>The day the selected trip would move to.</summary>
        public DayChoice EditDay
        {
            get { return _editDay; }
            set { SetProperty(ref _editDay, value); }
        }

        /// <summary>Every quarter hour, plus the selected trip's own time when it falls between two.</summary>
        public List<TimeChoice> EditTimes
        {
            get { return _editTimes; }
            private set { SetProperty(ref _editTimes, value); }
        }

        public TimeChoice EditTime
        {
            get { return _editTime; }
            set { SetProperty(ref _editTime, value); }
        }

        public string EditTripCode
        {
            get { return _editTripCode; }
            set { SetProperty(ref _editTripCode, value ?? ""); }
        }

        // the edit boxes follow whatever is picked, on the timeline or in the list
        private void AfterSelectionChanged()
        {
            var entry = _selectedBlock?.Entry;
            if (entry != null)
            {
                EditTimes = Times.Any(t => t.Time == entry.Time)
                    ? Times
                    : Times.Concat(new[] { new TimeChoice { Time = entry.Time, Text = entry.Time.ToString(@"hh\:mm") } })
                           .OrderBy(t => t.Time).ToList();
                EditDay = DayChoices.FirstOrDefault(d => d.Day == entry.Day);
                EditTime = EditTimes.FirstOrDefault(t => t.Time == entry.Time);
                EditTripCode = entry.Destination.TripCode ?? "";
            }

            if (!_syncing)
            {
                _syncing = true;
                SelectedRow = entry == null ? null : Rows.FirstOrDefault(r => r.Entry == entry);
                _syncing = false;
            }

            OnPropertyChanged(nameof(SelectedBlockText));
            OnPropertyChanged(nameof(CanEditSelected));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(RemoveSelectedText));
            (RemoveSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ApplyEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        public string SelectedBlockText
        {
            get
            {
                if (_selected.Count > 1) return _selected.Count + " trips picked. Drag any of them to move them all, or hold shift while dragging to copy them all, keeping their days and times apart.";
                if (_selectedBlock == null)
                    return "Click a trip to pick it, or drag across empty space to pick several; ctrl-click adds one, shift-click picks everything between. Drag a trip to move it. The dashed blocks show the trips you are about to add.";
                return _selectedBlock.Detail;
            }
        }

        /// <summary>Trips are placed on the quarter hour, as the Add panel offers, so a drag snaps to one.</summary>
        public const int SnapMinutes = 15;

        /// <summary>
        ///     How far a drag of so many minutes really shifts a trip: to the nearest quarter hour. A group is
        ///     shifted by this one amount rather than each trip snapping on its own, so the trips in it stay
        ///     exactly as far apart as they were.
        /// </summary>
        public static double SnapShift(double minutesMoved)
        {
            return Math.Round(minutesMoved/SnapMinutes)*SnapMinutes;
        }

        /// <summary>
        ///     Where a trip lands when it is dragged across so many day rows and so many minutes. The whole week
        ///     moves with the pointer, so a trip dragged off Sunday evening comes back on Monday, and times snap to
        ///     the quarter hour, which is what the Add panel offers.
        /// </summary>
        public static void DragTarget(DayOfWeek day, TimeSpan time, int daysMoved, double minutesMoved, out DayOfWeek newDay, out TimeSpan newTime, bool snapToQuarter = true)
        {
            const int week = 7*1440;
            double at = DayIndex(day)*1440.0 + time.TotalMinutes + daysMoved*1440.0 + minutesMoved;
            if (snapToQuarter) at = Math.Round(at/SnapMinutes)*SnapMinutes;
            at = ((at%week) + week)%week;
            int index = (int) (at/1440);
            newDay = (DayOfWeek) ((index + 1)%7); // Monday is row 0
            newTime = TimeSpan.FromMinutes(at - index*1440);
        }

        public bool CanUndo => _memos.Count > 0;

        /// <summary>What the undo button offers to take back, named so it is clear what will happen.</summary>
        public string UndoText => _memos.Count == 0 ? "Nothing to undo" : "Undo " + _memos[_memos.Count - 1].Label;

        // this coach's week as it stands, to go back to
        private TimetableMemo Snapshot(string label)
        {
            if (_coach == null) return null;
            return new TimetableMemo
            {
                Label = label,
                Coach = _coach.Coach,
                Lines = CoachEntries().Select(e => new MemoLine
                {
                    Route = e.TimeTable.Route, Day = e.Day, Time = e.Time,
                    Code = e.Destination.TripCode, To = e.Destination.Station
                }).ToList()
            };
        }

        // kept only once the change it precedes has actually happened, so a refused move leaves nothing to undo
        private void Remember(TimetableMemo memo)
        {
            if (memo == null) return;
            _memos.Add(memo);
            while (_memos.Count > MemoDepth) _memos.RemoveAt(0);
            RaiseUndo();
        }

        private void RaiseUndo()
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(UndoText));
            (UndoCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void Undo()
        {
            if (_memos.Count == 0) return;
            var memo = _memos[_memos.Count - 1];
            _memos.RemoveAt(_memos.Count - 1);

            var option = Coach.FirstOrDefault(a => a.Coach == memo.Coach);
            if (option == null)
            {
                Status = "That coach has left the fleet, so this cannot be undone.";
                RaiseUndo();
                return;
            }

            SelectedCoach = option; // the change may have been made to another coach
            var f = memo.Coach;
            foreach (var e in CoachEntries().ToList()) RemoveEntryQuietly(e);

            int gone = 0;
            foreach (var line in memo.Lines)
            {
                if (!Human.Routes.Contains(line.Route))
                {
                    gone++; // the route itself has been closed since
                    continue;
                }

                var entry = new RouteTimeTableEntry(line.Route.TimeTable, line.Day, line.Time,
                                                    new RouteEntryDestination(line.To, line.Code)) { Coach = f };
                line.Route.TimeTable.AddEntry(entry);
                if (!f.Routes.Contains(line.Route)) f.AddRoute(line.Route);
            }

            _justAdded = new List<RouteTimeTableEntry>();
            Finish("Undone: " + memo.Label + "." + (gone > 0 ? " " + gone + " trip(s) could not come back: their route has closed." : ""));
            RaiseUndo();
        }

        /// <summary>
        ///     Leaves a trip where it is and puts a copy of it at another day and time, with the next free trip
        ///     number: what dragging a trip with shift held does. Returns null when the copy fits, or why it does not.
        /// </summary>
        public string CopyEntry(RouteTimeTableEntry entry, DayOfWeek day, TimeSpan time)
        {
            if (_coach == null || entry == null) return "Pick a coach first.";
            var f = _coach.Coach;
            var route = entry.TimeTable.Route;
            var memo = Snapshot("copying " + entry.Destination.TripCode);
            string code = Human.GetNextTripCode(0);
            var copy = new RouteTimeTableEntry(route.TimeTable, day, time,
                                               new RouteEntryDestination(entry.Destination.Station, code)) { Coach = f };
            route.TimeTable.AddEntry(copy);

            if (!TimeTableHelpers.IsTimeTableValid(f, CoachEntries().ToList()))
            {
                route.TimeTable.RemoveEntry(copy);
                string why = "No copy of " + entry.Destination.TripCode + " on " + day + " at " + time.ToString(@"hh\:mm")
                             + ": it would overlap another trip of " + f.Name + " (which needs the trip time plus its "
                             + "time on the ground), or the station has no free gate time then.";
                Status = why;
                return why;
            }

            if (!f.Routes.Contains(route)) f.AddRoute(route);
            Remember(memo);
            _justAdded = new List<RouteTimeTableEntry> { copy };
            Finish("Trip " + entry.Destination.TripCode + " copied to " + day + " at " + time.ToString(@"hh\:mm") + " as " + code + ".");
            return null;
        }

        /// <summary>
        ///     Copies every trip given, each landing the same number of day rows and minutes from where it is, so
        ///     the copies keep the shape of the group: a whole day's flying can be copied onto another day at once.
        ///     Each copy gets its own free trip number. Either all of them are made or none is.
        /// </summary>
        public string CopyEntries(IEnumerable<RouteTimeTableEntry> entries, int daysMoved, double minutesMoved)
        {
            if (_coach == null) return "Pick a coach first.";
            var going = (entries ?? Enumerable.Empty<RouteTimeTableEntry>()).Where(e => e != null).Distinct().OrderBy(WeekMinutes).ToList();
            if (going.Count == 0) return null;
            if (going.Count == 1)
            {
                DayOfWeek one; TimeSpan at;
                DragTarget(going[0].Day, going[0].Time, daysMoved, minutesMoved, out one, out at);
                return CopyEntry(going[0], one, at);
            }

            var f = _coach.Coach;
            var free = Human.GetTripCodes();
            if (free.Count < going.Count)
                return "There are not enough free trip numbers left to copy " + going.Count + " trips.";

            var memo = Snapshot("copying " + going.Count + " trips");
            double shift = SnapShift(minutesMoved);
            var copies = new List<Tuple<Route, RouteTimeTableEntry>>();
            for (int i = 0; i < going.Count; i++)
            {
                var e = going[i];
                DayOfWeek day; TimeSpan time;
                DragTarget(e.Day, e.Time, daysMoved, shift, out day, out time, false);
                var route = e.TimeTable.Route;
                var copy = new RouteTimeTableEntry(route.TimeTable, day, time,
                                                   new RouteEntryDestination(e.Destination.Station, free[i])) { Coach = f };
                route.TimeTable.AddEntry(copy);
                copies.Add(Tuple.Create(route, copy));
            }

            if (!TimeTableHelpers.IsTimeTableValid(f, CoachEntries().ToList()))
            {
                foreach (var c in copies) c.Item1.TimeTable.RemoveEntry(c.Item2);
                string why = "No copies made: those " + going.Count + " trips would overlap each other or another "
                             + "trip of " + f.Name + " where they landed (each needs the trip time plus its time "
                             + "on the ground), or a station has no free gate time then.";
                Status = why;
                return why;
            }

            foreach (var c in copies)
            {
                if (!f.Routes.Contains(c.Item1)) f.AddRoute(c.Item1);
            }

            Remember(memo);
            _justAdded = copies.Select(c => c.Item2).OrderBy(WeekMinutes).ToList();
            Finish(going.Count + " trips copied, numbered " + free[0] + " to " + free[going.Count - 1] + ".");
            return null;
        }

        /// <summary>
        ///     Moves every trip given by the same number of day rows and minutes, which is what dragging a picked
        ///     group does: they keep their days and times apart from one another. Either all of them move or none
        ///     does, so a group that would put two trips on top of each other is refused whole.
        /// </summary>
        public string MoveEntries(IEnumerable<RouteTimeTableEntry> entries, int daysMoved, double minutesMoved)
        {
            if (_coach == null) return "Pick a coach first.";
            var going = (entries ?? Enumerable.Empty<RouteTimeTableEntry>()).Where(e => e != null).Distinct().ToList();
            if (going.Count == 0) return null;
            if (going.Count == 1)
            {
                DayOfWeek one; TimeSpan at;
                DragTarget(going[0].Day, going[0].Time, daysMoved, minutesMoved, out one, out at);
                return MoveEntry(going[0], one, at);
            }

            // where each one lands, worked out before any of them moves. The drag snaps once and every trip is
            // shifted by that same amount, so the group keeps its own shape.
            double shift = SnapShift(minutesMoved);
            var memo = Snapshot("moving " + going.Count + " trips");
            var was = going.Select(e => Tuple.Create(e, e.Day, e.Time)).ToList();
            var to = new List<Tuple<RouteTimeTableEntry, DayOfWeek, TimeSpan>>();
            foreach (var e in going)
            {
                DayOfWeek day; TimeSpan time;
                DragTarget(e.Day, e.Time, daysMoved, shift, out day, out time, false);
                to.Add(Tuple.Create(e, day, time));
            }

            if (to.All(x => x.Item1.Day == x.Item2 && x.Item1.Time == x.Item3)) return null;

            foreach (var x in to)
            {
                x.Item1.Day = x.Item2;
                x.Item1.Time = x.Item3;
            }

            if (!TimeTableHelpers.IsTimeTableValid(_coach.Coach, CoachEntries().ToList()))
            {
                foreach (var x in was)
                {
                    x.Item1.Day = x.Item2;
                    x.Item1.Time = x.Item3;
                }

                string why = "Those " + going.Count + " trips stay where they are: moved together they would overlap "
                             + "another trip of " + _coach.Coach.Name + " (which needs the trip time plus its "
                             + "time on the ground), or a station has no free gate time then.";
                Status = why;
                return why;
            }

            Remember(memo);
            _justAdded = going.OrderBy(WeekMinutes).ToList();
            Finish(going.Count + " trips moved.");
            return null;
        }

        /// <summary>
        ///     Moves one trip to another day and time, which is what dragging a block does. The trip is put back
        ///     where it was when it would not fit, and the reason is returned; null means it moved.
        /// </summary>
        public string MoveEntry(RouteTimeTableEntry entry, DayOfWeek day, TimeSpan time)
        {
            if (_coach == null || entry == null) return "Pick a coach first.";
            if (entry.Day == day && entry.Time == time) return null;

            var f = _coach.Coach;
            var memo = Snapshot("moving " + entry.Destination.TripCode);
            DayOfWeek wasDay = entry.Day;
            TimeSpan wasTime = entry.Time;
            entry.Day = day;
            entry.Time = time;
            if (!TimeTableHelpers.IsTimeTableValid(f, CoachEntries().ToList()))
            {
                entry.Day = wasDay;
                entry.Time = wasTime;
                string why = "That leaves " + entry.Destination.TripCode + " where it was: on " + day + " at "
                             + time.ToString(@"hh\:mm") + " it would overlap another trip of " + f.Name
                             + " (which needs the trip time plus its time on the ground), or the station has no free gate time then.";
                Status = why;
                return why;
            }

            Remember(memo);
            _justAdded = new List<RouteTimeTableEntry> { entry };
            Finish("Trip " + entry.Destination.TripCode + " moved to " + day + " at " + time.ToString(@"hh\:mm") + ".");
            return null;
        }

        // the edit panel's button: the day, the time and the trip number of the picked trip at once
        private void ApplyEdit()
        {
            var entry = _selectedBlock?.Entry;
            if (entry == null || _editDay == null || _editTime == null) return;

            string code = _editTripCode.Trim();
            string wasCode = entry.Destination.TripCode;
            bool codeChanged = code.Length > 0 && code != wasCode;
            if (codeChanged)
            {
                var clash = CoachEntries().FirstOrDefault(e => e != entry && e.Destination.TripCode == code);
                if (clash == null && Human.Routes.SelectMany(r => r.TimeTable.Entries).Any(e => e != entry && e.Destination.TripCode == code))
                {
                    Status = "Trip number " + code + " is already used by another of your trips.";
                    return;
                }
                if (clash != null)
                {
                    Status = "Trip number " + code + " is already used by this coach.";
                    return;
                }
                entry.Destination.TripCode = code;
            }

            if (entry.Day == _editDay.Day && entry.Time == _editTime.Time)
            {
                if (codeChanged)
                {
                    entry.Destination.TripCode = wasCode;
                    var memoC = Snapshot("renumbering " + wasCode);
                    entry.Destination.TripCode = code;
                    Remember(memoC);
                    Finish("Trip " + wasCode + " is now " + code + ".");
                }

                return;
            }

            if (MoveEntry(entry, _editDay.Day, _editTime.Time) != null && codeChanged)
            {
                entry.Destination.TripCode = wasCode; // the move was refused, so the number goes back too
            }
        }

        public RouteOption SelectedRoute
        {
            get { return _route; }
            set
            {
                if (value?.Route != null) _wantRoute = value.Route;
                if (SetProperty(ref _route, value))
                {
                    LoadDirections();
                    BuildPreview();
                    RaiseCanExecute();
                }
            }
        }

        public List<StationOption> Directions
        {
            get { return _directions; }
            private set { SetProperty(ref _directions, value); }
        }

        public StationOption SelectedFrom
        {
            get { return _from; }
            set
            {
                if (value?.Station != null) _wantFrom = value.Station;
                if (SetProperty(ref _from, value)) BuildPreview();
            }
        }

        public TimeChoice SelectedTime
        {
            get { return _time; }
            set { if (SetProperty(ref _time, value)) BuildPreview(); }
        }

        public int Turnaround
        {
            get { return _turnaround; }
            set { if (SetProperty(ref _turnaround, value)) BuildPreview(); }
        }

        /// <summary>Trip number of the outbound trip; empty picks the next free numbers.</summary>
        public string TripCode
        {
            get { return _tripCode; }
            set { SetProperty(ref _tripCode, value ?? ""); }
        }

        public string Status
        {
            get { return _status; }
            private set { SetProperty(ref _status, value); }
        }

        public string Summary
        {
            get { return _summary; }
            private set { SetProperty(ref _summary, value); }
        }

        private static Carrier Human
        {
            get { return GameObject.GetInstance().HumanCarrier; }
        }

        private void RaiseCanExecute()
        {
            (AddRoundTripCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AddOneWayCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveRouteTripsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void SetDays(params DayOfWeek[] days)
        {
            foreach (var d in Days)
            {
                d.IsChecked = days.Contains(d.Day);
            }
        }

        /// <summary>Opens the schedule of an coach.</summary>
        public void Select(FleetCoach f)
        {
            ReloadCoach();
            var option = Coach.FirstOrDefault(a => a.Coach == f);
            if (option != null)
            {
                SelectedCoach = option;
            }
        }

        /// <summary>Refreshes the lists after something changed elsewhere (new coach, new route).</summary>
        public void ReloadCoach()
        {
            var keep = _coach?.Coach;
            var list = Human.DeliveredFleet
                            .Where(
                                f => f.Coach.Type.TypeCoach == CoachType.TypeOfCoach.Passenger
                                     || f.Coach.Type.TypeCoach == CoachType.TypeOfCoach.Mixed
                                     || f.Coach.Type.TypeCoach == CoachType.TypeOfCoach.Cargo)
                            .OrderBy(f => f.Name)
                            .ToList();

            Coach.Clear();
            foreach (var f in list)
            {
                Coach.Add(
                    new CoachOption
                    {
                        Coach = f,
                        Text = f.Name + "  |  " + f.Coach.Type.Name + "  |  "
                               + f.Routes.SelectMany(r => r.TimeTable.Entries).Count(e => e.Coach == f) + " trips/week"
                    });
            }

            _coach = Coach.FirstOrDefault(a => a.Coach == keep) ?? Coach.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedCoach));
            LoadRoutes();
            LoadRows();
            RaiseCanExecute();
        }

        private void LoadRoutes()
        {
            var keep = _route?.Route;
            Routes.Clear();

            if (_coach != null)
            {
                var type = _coach.Coach.Coach.Type;
                bool cargoCoach = type.TypeCoach == CoachType.TypeOfCoach.Cargo;
                foreach (var r in Human.Routes.Where(r => r.IsCargoRoute == cargoCoach).OrderBy(r => r.Destination1.Profile.IATACode))
                {
                    double distance = r.GetDistance();
                    long bay = Math.Min(r.Destination1.GetMaxBayLength(), r.Destination2.GetMaxBayLength());
                    if (_coach.Coach.Coach.Range < distance || _coach.Coach.Coach.MinBayLength > bay)
                    {
                        continue;
                    }

                    Routes.Add(
                        new RouteOption
                        {
                            Route = r,
                            Text = string.Format(
                                "{0} - {1}  ({2}, {3})",
                                r.Destination1.Profile.IATACode,
                                r.Destination2.Profile.IATACode,
                                DistanceFormatter.Km(distance),
                                FormatSpan(r.GetTripTime(type)))
                        });
                }
            }

            _route = Routes.FirstOrDefault(r => r.Route == keep)
                     ?? Routes.FirstOrDefault(r => r.Route == _wantRoute)
                     ?? Routes.FirstOrDefault();
            if (_route?.Route != null) _wantRoute = _route.Route;
            OnPropertyChanged(nameof(SelectedRoute));
            LoadDirections();
        }

        private void LoadDirections()
        {
            var keep = _from?.Station ?? _wantFrom;
            if (_route == null)
            {
                Directions = new List<StationOption>();
                SelectedFrom = null;
                return;
            }

            Directions = new[] { _route.Route.Destination1, _route.Route.Destination2 }
                .Select(a => new StationOption { Station = a, Text = a.Profile.IATACode + "  " + a.Profile.Name })
                .ToList();
            SelectedFrom = Directions.FirstOrDefault(d => d.Station == keep) ?? Directions[0];
            if (_from?.Station != null) _wantFrom = _from.Station;
        }

        private static string FormatSpan(TimeSpan t)
        {
            return string.Format("{0}h{1:00}", (int) t.TotalHours, t.Minutes);
        }

        private static int DayIndex(DayOfWeek d)
        {
            return ((int) d + 6) % 7; // Monday first
        }

        private IEnumerable<RouteTimeTableEntry> CoachEntries()
        {
            var f = _coach.Coach;
            return f.Routes.SelectMany(r => r.TimeTable.Entries.Where(e => e.Coach == f)).ToList();
        }

        private void LoadRows()
        {
            Rows.Clear();
            if (_coach == null)
            {
                Summary = "You have no delivered coach.";
                _blocks = new List<TimelineBlock>();
                SelectedBlock = null;
                BuildPreview();
                return;
            }

            var f = _coach.Coach;
            var entries = CoachEntries()
                .OrderBy(e => DayIndex(e.Day)).ThenBy(e => e.Time).ToList();
            double airMinutes = 0;

            foreach (var e in entries)
            {
                TimeSpan trip = e.TimeTable.Route.GetTripTime(f.Coach.Type);
                airMinutes += trip.TotalMinutes;
                TimeSpan arrive = e.Time.Add(trip);
                var entry = e;
                Rows.Add(
                    new TimetableRow
                    {
                        Entry = e,
                        Day = e.Day.ToString().Substring(0, 3),
                        Time = e.Time.ToString(@"hh\:mm"),
                        Arrives = new TimeSpan(arrive.Hours, arrive.Minutes, 0).ToString(@"hh\:mm")
                                  + (arrive.Days > 0 ? " +" + arrive.Days : ""),
                        Trip = e.Destination.TripCode,
                        Route = e.DepartureStation.Profile.IATACode + " > " + e.Destination.Station.Profile.IATACode,
                        RemoveCommand = new RelayCommand(() => RemoveEntry(entry))
                    });
            }

            BuildBlocks(entries);
            BuildPreview();

            Summary = entries.Count == 0
                ? string.Format("{0} has no trips yet. It needs {1} minutes at the gate between one trip and the next.", f.Name, GroundMinutes)
                : string.Format(
                    "{0}: {1} trips a week, about {2:0} hours in the air. The faint band after each trip is the {3} minutes it needs at the gate before it can leave again.",
                    f.Name, entries.Count, airMinutes/60, GroundMinutes);
        }

        private static void AddBlocks(
            List<TimelineBlock> list, List<TimelineBlock> groundList, DayOfWeek day, double startMinutes, double durationMinutes,
            double groundMinutes, string label, string detail, int color, bool preview, RouteTimeTableEntry entry)
        {
            int d = DayIndex(day);
            double length = Math.Max(durationMinutes, 10);
            Span(list, d, startMinutes, length, b =>
            {
                b.Label = label;
                b.Detail = detail;
                b.ColorIndex = color;
                b.IsPreview = preview;
                b.Entry = entry;
                b.DurationMinutes = length;
            });

            // the time at the gate after it lands, which the coach needs before it can leave again
            if (groundList == null || groundMinutes <= 0) return;
            Span(groundList, d, startMinutes + length, groundMinutes, b =>
            {
                b.Detail = detail;
                b.ColorIndex = color;
                b.IsPreview = preview;
                b.Entry = entry;
                b.DurationMinutes = groundMinutes;
                b.IsGround = true;
            });
        }

        // a stretch of the week as one block, or two where it runs past midnight into the next day
        private static void Span(List<TimelineBlock> list, int dayIndex, double start, double length, Action<TimelineBlock> fill)
        {
            int day = (dayIndex + (int) (start/1440))%7;
            double from = start%1440;
            double end = from + length;

            var first = new TimelineBlock { Day = day, Start = from, End = Math.Min(end, 1440) };
            fill(first);
            list.Add(first);
            if (end <= 1440) return;

            var second = new TimelineBlock { Day = (day + 1)%7, Start = 0, End = end - 1440 };
            fill(second);
            list.Add(second);
        }

        private void BuildBlocks(List<RouteTimeTableEntry> entries)
        {
            var f = _coach.Coach;
            var routes = f.Routes.ToList();
            var keep = _selectedBlock?.Entry;
            var list = new List<TimelineBlock>();
            var ground = new List<TimelineBlock>();
            double groundMinutes = FleetCoachHelpers.GetMinTimeBetweenTrips(f).TotalMinutes;
            foreach (var e in entries)
            {
                TimeSpan trip = e.TimeTable.Route.GetTripTime(f.Coach.Type);
                string route = e.DepartureStation.Profile.IATACode + " > " + e.Destination.Station.Profile.IATACode;
                TimeSpan arrive = e.Time.Add(trip);
                string detail = string.Format(
                    "{0} {1}  |  {2}  |  leaves {3}, arrives {4}{5}  |  {6}",
                    e.Day, e.Destination.TripCode, route, e.Time.ToString(@"hh\:mm"),
                    new TimeSpan(arrive.Hours, arrive.Minutes, 0).ToString(@"hh\:mm"), arrive.Days > 0 ? " (+" + arrive.Days + ")" : "", FormatSpan(trip));
                AddBlocks(list, ground, e.Day, e.Time.TotalMinutes, trip.TotalMinutes, groundMinutes,
                          e.Destination.TripCode + " " + route, detail,
                          Math.Max(0, routes.IndexOf(e.TimeTable.Route)), false, e);
            }

            _blocks = list;
            _ground = ground;
            // what was just placed or moved is picked, so it can be seen and moved again straight away; otherwise
            // whatever was picked before stays picked, less any trip that has gone
            TimelineBlock pick = null;
            foreach (var e in _justAdded)
            {
                pick = list.FirstOrDefault(b => b.Entry == e);
                if (pick != null) break;
            }

            if (_justAdded.Count > 1)
            {
                // a group that was just moved stays picked as a group, so it can be moved again
                _selected.Clear();
                foreach (var e in _justAdded)
                {
                    if (list.Any(b => b.Entry == e)) _selected.Add(e);
                }
            }
            else
            {
                _selected.RemoveWhere(e => list.All(b => b.Entry != e));
                if (pick?.Entry != null)
                {
                    _selected.Clear();
                    _selected.Add(pick.Entry);
                }
            }

            _justAdded = new List<RouteTimeTableEntry>();
            _selectedBlock = pick ?? list.FirstOrDefault(b => b.Entry == keep) ?? list.FirstOrDefault(b => _selected.Contains(b.Entry));
            OnPropertyChanged(nameof(SelectedBlock));
            AfterSelectionChanged();
        }

        /// <summary>The dashed trips that "Add round trips" would put on the timeline.</summary>
        private void BuildPreview()
        {
            var list = new List<TimelineBlock>();
            var ground = new List<TimelineBlock>();
            if (_coach != null && _route != null && _from != null && Days != null)
            {
                var f = _coach.Coach;
                var route = _route.Route;
                Station from = _from.Station;
                Station to = from == route.Destination1 ? route.Destination2 : route.Destination1;
                TimeSpan trip = route.GetTripTime(f.Coach.Type);
                TimeSpan back = _time.Time.Add(trip).Add(new TimeSpan(0, _turnaround, 0));
                int color = Math.Max(0, f.Routes.ToList().IndexOf(route));
                double groundMinutes = FleetCoachHelpers.GetMinTimeBetweenTrips(f).TotalMinutes;
                foreach (var day in Days.Where(d => d.IsChecked).Select(d => d.Day))
                {
                    AddBlocks(list, ground, day, _time.Time.TotalMinutes, trip.TotalMinutes, groundMinutes,
                              from.Profile.IATACode + " > " + to.Profile.IATACode, "New trip", color, true, null);
                    DayOfWeek backDay = (DayOfWeek) (((int) day + back.Days) % 7);
                    AddBlocks(list, ground, backDay, back.Hours * 60 + back.Minutes, trip.TotalMinutes, groundMinutes,
                              to.Profile.IATACode + " > " + from.Profile.IATACode, "New trip", color, true, null);
                }
            }

            _preview = list;
            _previewGround = ground;
            TimelineChanged?.Invoke();
        }

        private void StopIfFlying(FleetCoach f)
        {
            if (f.Status != FleetCoach.CoachStatus.Stopped)
            {
                f.Status = FleetCoach.CoachStatus.Stopped;
                Status += " " + f.Name + " was stopped; start it again under Trips > Fleet.";
            }
        }

        private void Finish(string message)
        {
            Status = message;
            StopIfFlying(_coach.Coach);
            ReloadCoach();
            _changed?.Invoke();
        }

        private void RemoveEntry(RouteTimeTableEntry entry)
        {
            if (_coach == null) return;
            string code = entry.Destination.TripCode;
            var memo = Snapshot("removing " + code);
            RemoveEntryQuietly(entry);
            Remember(memo);
            Finish("Trip " + code + " removed.");
        }

        // takes the trip out without reloading or saying so, for removing several at once
        private void RemoveEntryQuietly(RouteTimeTableEntry entry)
        {
            if (_coach == null) return;
            var f = _coach.Coach;
            var route = entry.TimeTable.Route;
            route.TimeTable.RemoveEntry(entry); // entries made by the route creator can still point at a temporary timetable
            entry.TimeTable.RemoveEntry(entry);

            if (!route.TimeTable.Entries.Any(e => e.Coach == f))
            {
                f.RemoveRoute(route);
            }
        }

        private void RemoveRouteTrips()
        {
            if (_coach == null || _route == null)
            {
                return;
            }

            var f = _coach.Coach;
            int n = _route.Route.TimeTable.Entries.Count(e => e.Coach == f);
            var memo = Snapshot("clearing " + _route.Route.Destination1.Profile.IATACode + "-" + _route.Route.Destination2.Profile.IATACode);
            f.RemoveRoute(_route.Route);
            Remember(memo);
            Finish(n + " trip(s) removed from " + _route.Text + ".");
        }

        private void Add(bool roundTrip)
        {
            if (_coach == null || _route == null || _from == null)
            {
                return;
            }

            var days = Days.Where(d => d.IsChecked).Select(d => d.Day).ToList();
            if (days.Count == 0)
            {
                Status = "Tick at least one day.";
                return;
            }

            var f = _coach.Coach;
            var route = _route.Route;
            Station from = _from.Station;
            Station to = from == route.Destination1 ? route.Destination2 : route.Destination1;
            var memo = Snapshot("adding trips");

            // trip numbers: typed by the player, or the next free ones
            string code1 = _tripCode.Trim();
            var free = Human.GetTripCodes();
            if (code1.Length == 0)
            {
                code1 = free[0];
            }

            string code2 = free.FirstOrDefault(c => c != code1) ?? free[0];
            if (!string.IsNullOrEmpty(_tripCode.Trim()))
            {
                int idx = free.IndexOf(code1);
                code2 = idx >= 0 && idx + 1 < free.Count ? free[idx + 1] : free.First(c => c != code1);
            }

            TimeSpan tripTime = route.GetTripTime(f.Coach.Type);
            TimeSpan back = _time.Time.Add(tripTime).Add(new TimeSpan(0, _turnaround, 0));

            var rt = new RouteTimeTable(route);
            foreach (var day in days)
            {
                rt.AddEntry(
                    new RouteTimeTableEntry(route.TimeTable, day, _time.Time, new RouteEntryDestination(to, code1))
                    {
                        Coach = f
                    });

                if (roundTrip)
                {
                    DayOfWeek backDay = (DayOfWeek) (((int) day + back.Days) % 7);
                    rt.AddEntry(
                        new RouteTimeTableEntry(
                            route.TimeTable,
                            backDay,
                            new TimeSpan(back.Hours, back.Minutes, 0),
                            new RouteEntryDestination(from, code2))
                        {
                            Coach = f
                        });
                }
            }

            var existing = CoachEntries().ToList();
            if (!TimeTableHelpers.IsRoutePlannerTimeTableValid(rt, f, existing))
            {
                Status = "These trips don't fit: they overlap another trip of " + f.Name
                         + " (it needs the trip time plus time on the ground), or one of the stations has no free gate time then. Try another time or fewer days.";
                return;
            }

            foreach (var e in rt.Entries)
            {
                route.TimeTable.AddEntry(e);
            }

            if (!f.Routes.Contains(route))
            {
                f.AddRoute(route);
            }

            memo.Label = "adding " + rt.Entries.Count + " trip(s)";
            Remember(memo);
            _justAdded = rt.Entries.OrderBy(e => DayIndex(e.Day)).ThenBy(e => e.Time).ToList();
            Finish(string.Format("{0} trip(s) added ({1}).", rt.Entries.Count, roundTrip ? "round trips" : "one way"));
        }
    }
}
