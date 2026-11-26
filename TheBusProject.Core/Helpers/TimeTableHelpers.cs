using System;
using System.Collections.Generic;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Stations;
using TheBusProject.Models.General;
using TheBusProject.Models.General.Environment;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    //the helpers class for time tables
    public class TimeTableHelpers
    {
        #region Public Methods and Operators

        public static bool IsRouteEntryInOccupied(RouteTimeTableEntry entry, FleetCoach coach)
        {
            List<TimeSpan> occupiedSlots1 = StationHelpers.GetOccupiedSlotTimes(
                entry.DepartureStation,
                coach.Coach.Carrier,
                GeneralHelpers.GetSeason(GameObject.GetInstance().GameTime), Terminal.TerminalType.Passenger);
            List<TimeSpan> occupiedSlots2 = StationHelpers.GetOccupiedSlotTimes(
                entry.Destination.Station,
                coach.Coach.Carrier,
                GeneralHelpers.GetSeason(GameObject.GetInstance().GameTime), Terminal.TerminalType.Passenger);

            var gateTimeBefore = new TimeSpan(0, 15, 0);
            var gateTimeAfter = new TimeSpan(0, 15, 0);

            var entryTakeoffTime = new TimeSpan(
                (int) entry.Day,
                entry.Time.Hours,
                entry.Time.Minutes,
                entry.Time.Seconds);
            TimeSpan entryLandingTime =
                entryTakeoffTime.Add(entry.TimeTable.Route.GetTripTime(entry.Coach.Coach.Type));

            if (entryLandingTime.Days > 6)
            {
                entryLandingTime = new TimeSpan(
                    0,
                    entryLandingTime.Hours,
                    entryLandingTime.Minutes,
                    entryLandingTime.Seconds);
            }

            TimeSpan entryStartTakeoffTime = entryTakeoffTime.Subtract(gateTimeBefore);
            TimeSpan entryEndTakeoffTime = entryTakeoffTime.Add(gateTimeAfter);

            var tTakeoffTime = new TimeSpan(
                entryStartTakeoffTime.Days,
                entryStartTakeoffTime.Hours,
                (entryStartTakeoffTime.Minutes/15)*15,
                0);

            while (tTakeoffTime < entryEndTakeoffTime)
            {
                if (occupiedSlots1.Contains(tTakeoffTime))
                {
                    return true;
                }

                tTakeoffTime = tTakeoffTime.Add(new TimeSpan(0, 15, 0));
            }

            TimeSpan entryStartLandingTime = entryLandingTime.Subtract(gateTimeBefore);
            TimeSpan entryEndLandingTime = entryLandingTime.Add(gateTimeAfter);

            var tLandingTime = new TimeSpan(
                entryStartLandingTime.Days,
                entryStartLandingTime.Hours,
                (entryStartLandingTime.Minutes/15)*15,
                0);

            while (tLandingTime < entryEndLandingTime)
            {
                if (occupiedSlots2.Contains(tLandingTime))
                {
                    return true;
                }

                tLandingTime = tLandingTime.Add(new TimeSpan(0, 15, 0));
            }

            return false;
        }

        public static bool IsRoutePlannerTimeTableValid(
            RouteTimeTable timeTable,
            FleetCoach coach,
            List<RouteTimeTableEntry> entries,
            bool withSlots = true)
        {
            var tEntries = new List<RouteTimeTableEntry>();
            tEntries.AddRange(entries);
            tEntries.AddRange(timeTable.Entries);

            foreach (RouteTimeTableEntry e in timeTable.Entries)
            {
                var cEntries = new List<RouteTimeTableEntry>(tEntries);
                cEntries.Remove(e);

                if (!IsRouteEntryValid(e, coach, cEntries, withSlots))
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsTimeTableValid(
            FleetCoach coach,
            List<RouteTimeTableEntry> entries,
            bool withSlots = true)
        {
            foreach (RouteTimeTableEntry e in entries)
            {
                var tEntries = new List<RouteTimeTableEntry>(entries);
                tEntries.Remove(e);

                if (!IsRouteEntryValid(e, coach, tEntries, withSlots))
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsTimeTableValid(
            RouteTimeTable timeTable,
            FleetCoach coach,
            List<RouteTimeTableEntry> entries,
            bool withSlots = true)
        {
            return timeTable.Entries.All(e => IsRouteEntryValid(e, coach, entries, withSlots));
        }

        #endregion

        #region Methods

        private static RouteTimeTableEntry GetNextEntry(
            RouteTimeTableEntry entry,
            FleetCoach coach,
            List<RouteTimeTableEntry> entries)
        {
            DayOfWeek day = entry.Day;

            int counter = 0;

            while (counter < 7)
            {
                List<RouteTimeTableEntry> tEntries = entries;

                tEntries = (from e in tEntries orderby e.Time select e).ToList();

                foreach (RouteTimeTableEntry dEntry in tEntries)
                {
                    if (!((dEntry.Day == entry.Day && dEntry.Time <= entry.Time)))
                    {
                        return dEntry;
                    }
                }
                day++;

                if (day == (DayOfWeek) 7)
                {
                    day = 0;
                }

                counter++;
            }

            return null;
        }

        private static RouteTimeTableEntry GetPreviousEntry(
            RouteTimeTableEntry entry,
            FleetCoach coach,
            List<RouteTimeTableEntry> entries)
        {
            var tsEntry = new TimeSpan((int) entry.Day, entry.Time.Hours, entry.Time.Minutes, entry.Time.Seconds);
            DayOfWeek eDay = entry.Day;

            int counter = 0;

            while (counter < 7)
            {
                List<RouteTimeTableEntry> tEntries = entries;

                tEntries = (from e in tEntries orderby e.Time descending select e).ToList();

                foreach (RouteTimeTableEntry dEntry in tEntries)
                {
                    var ts = new TimeSpan((int) eDay, dEntry.Time.Hours, dEntry.Time.Minutes, dEntry.Time.Seconds);
                    if (ts < tsEntry)
                    {
                        return dEntry;
                    }
                }
                counter++;

                eDay--;

                if (((int) eDay) == -1)
                {
                    eDay = (DayOfWeek) 6;
                    tsEntry = new TimeSpan(7, tsEntry.Hours, tsEntry.Minutes, tsEntry.Seconds);
                }
            }

            return null;
        }

        private static bool IsRouteEntryValid(
            RouteTimeTableEntry entry,
            FleetCoach coach,
            IEnumerable<RouteTimeTableEntry> entries,
            bool withSlots)
        {
            TimeSpan tripTime =
                entry.TimeTable.Route.GetTripTime(coach.Coach.Type)
                     .Add(FleetCoachHelpers.GetMinTimeBetweenTrips(coach));

            var startTime = new TimeSpan((int) entry.Day, entry.Time.Hours, entry.Time.Minutes, entry.Time.Seconds);

            TimeSpan endTime = startTime.Add(tripTime);
            if (endTime.Days == 7)
            {
                endTime = new TimeSpan(0, endTime.Hours, endTime.Minutes, endTime.Seconds);
            }

            var coachEntries = new List<RouteTimeTableEntry>(entries);

            if (entry.TimeTable.Route.Season == Weather.Season.Winter)
            {
                coachEntries.RemoveAll(e => e.TimeTable.Route.Season == Weather.Season.Summer);
            }

            if (entry.TimeTable.Route.Season == Weather.Season.Summer)
            {
                coachEntries.RemoveAll(e => e.TimeTable.Route.Season == Weather.Season.Winter);
            }

            if (withSlots)
            {
                if (IsRouteEntryInOccupied(entry, coach))
                {
                    return false;
                }
            }

            // coachEntries.AddRange(entry.TimeTable.Entries.FindAll(e => e.Destination.Station == entry.Destination.Station));

            foreach (RouteTimeTableEntry e in coachEntries)
            {
                var eStartTime = new TimeSpan((int) e.Day, e.Time.Hours, e.Time.Minutes, e.Time.Seconds);

                TimeSpan eTripTime =
                    e.TimeTable.Route.GetTripTime(coach.Coach.Type)
                     .Add(FleetCoachHelpers.GetMinTimeBetweenTrips(coach));

                TimeSpan eEndTime = eStartTime.Add(eTripTime);

                double diffStartTime = Math.Abs(eStartTime.Subtract(startTime).TotalMinutes);
                double diffEndTime = Math.Abs(eEndTime.Subtract(endTime).TotalMinutes);

                if (eEndTime.Days == 7)
                {
                    eEndTime = new TimeSpan(0, eEndTime.Hours, eEndTime.Minutes, eEndTime.Seconds);
                }

                // Two trips clash when one is still in the yard as the other leaves. A trip that leaves at the
                // moment the one before it has finished its ground time does not clash: the comparisons at the two
                // touching ends are strict, where they used to count that as a clash and so quietly demanded a minute
                // more than the ground time, which with departures on the quarter hour cost a whole extra quarter.
                if ((eStartTime >= startTime && endTime > eStartTime) || (eEndTime > startTime && endTime >= eEndTime)
                    || (endTime > eStartTime && eEndTime >= endTime)
                    || (startTime >= eStartTime && eEndTime > startTime))
                {
                    if (e.Coach == coach || diffEndTime < 15 || diffStartTime < 15)
                    {
                        return false;
                    }
                }
            }
            const double minutesPerWeek = 7*24*60;

            RouteTimeTableEntry nextEntry = GetNextEntry(entry, coach, coachEntries);

            RouteTimeTableEntry previousEntry = GetPreviousEntry(entry, coach, coachEntries);

            if (nextEntry != null && previousEntry != null)
            {
                TimeSpan tripTimeNext =
                    MathHelpers.GetTripTime(
                        entry.Destination.Station.Profile.Coordinates.ConvertToGeoCoordinate(),
                        nextEntry.DepartureStation.Profile.Coordinates.ConvertToGeoCoordinate(),
                        coach.Coach.Type.CruisingSpeed)
                               .Add(FleetCoachHelpers.GetMinTimeBetweenTrips(coach));
                TimeSpan tripTimePrevious =
                    MathHelpers.GetTripTime(
                        entry.DepartureStation.Profile.Coordinates.ConvertToGeoCoordinate(),
                        previousEntry.Destination.Station.Profile.Coordinates.ConvertToGeoCoordinate(),
                        coach.Coach.Type.CruisingSpeed)
                               .Add(FleetCoachHelpers.GetMinTimeBetweenTrips(coach));

                var prevDate = new TimeSpan(
                    (int) previousEntry.Day,
                    previousEntry.Time.Hours,
                    previousEntry.Time.Minutes,
                    previousEntry.Time.Seconds);
                var nextDate = new TimeSpan(
                    (int) nextEntry.Day,
                    nextEntry.Time.Hours,
                    nextEntry.Time.Minutes,
                    nextEntry.Time.Seconds);
                var currentDate = new TimeSpan((int) entry.Day, entry.Time.Hours, entry.Time.Minutes, entry.Time.Seconds);

                double timeToNext = currentDate.Subtract(nextDate).TotalMinutes > 0
                                        ? minutesPerWeek - currentDate.Subtract(nextDate).TotalMinutes
                                        : Math.Abs(currentDate.Subtract(nextDate).TotalMinutes);
                double timeFromPrev = prevDate.Subtract(currentDate).TotalMinutes > 0
                                          ? minutesPerWeek - prevDate.Subtract(currentDate).TotalMinutes
                                          : Math.Abs(prevDate.Subtract(currentDate).TotalMinutes);

                // Enough time to fly it, reach the next departure station and do the ground time: exactly enough is
                // enough, where asking for strictly more again cost a departure slot it did not need to.
                if (timeFromPrev
                    >= previousEntry.TimeTable.Route.GetTripTime(coach.Coach.Type).TotalMinutes
                      + tripTimePrevious.TotalMinutes
                    && timeToNext
                    >= entry.TimeTable.Route.GetTripTime(coach.Coach.Type).TotalMinutes
                    + tripTimeNext.TotalMinutes)
                {
                    return true;
                }
                return false;
            }
            return true;
        }

        #endregion

        //checks if a time table is valid

        //checks if an entry is in occupied slot

        //checks if an entry is valid
    }
}