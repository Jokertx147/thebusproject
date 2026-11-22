using System.Collections.Generic;
using System.Linq;
using TheBusProject.Helpers;
using TheBusProject.Models.Stations;

namespace TheBusProject.Models.Passengers
{
    //the route finder for the passengers
    public class PassengerRouteFinder
    {
        #region Constructors and Destructors

        /// <summary>
        ///     Constructor
        /// </summary>
        /// <param name="nodes">All stations</param>
        public PassengerRouteFinder(List<Station> nodes)
        {
            Nodes = nodes;
            Basis = new List<Station>();
            Dist = new Dictionary<string, double>();
            Previous = new Dictionary<string, Station>();

            foreach (Station n in Nodes)
            {
                Previous.Add(n.Profile.IATACode, null);
                Basis.Add(n);
                Dist.Add(n.Profile.IATACode, double.MaxValue);
            }
        }

        #endregion

        #region Public Properties

        public List<Station> Basis { get; set; }

        public Dictionary<string, double> Dist { get; set; }

        public List<Station> Nodes { get; set; }

        public Dictionary<string, Station> Previous { get; set; }

        #endregion

        #region Public Methods and Operators

        /// <summary>
        ///     Calculates the distance from
        ///     the start station to all other stations
        /// </summary>
        /// <param name="start">Startknoten</param>
        public void CalculateDistance(Station start)
        {
            Dist[start.Profile.IATACode] = 0;

            while (Basis.Count > 0)
            {
                Station u = GetNodeWithSmallestDistance();
                if (u == null)
                {
                    Basis.Clear();
                }
                else
                {
                    foreach (Station v in GetNeighbors(u))
                    {
                        double alt = Dist[u.Profile.IATACode] + getDistanceBetween(u, v);
                        if (alt < Dist[v.Profile.IATACode])
                        {
                            Dist[v.Profile.IATACode] = alt;
                            Previous[v.Profile.IATACode] = u;
                        }
                    }
                    Basis.Remove(u);
                }
            }
        }

        /// <summary>
        ///     Returns the station with the smallest distance
        /// </summary>
        /// <returns></returns>
        public Station GetNodeWithSmallestDistance()
        {
            double distance = double.MaxValue;
            Station smallest = null;

            foreach (Station n in Basis)
            {
                if (Dist[n.Profile.IATACode] < distance)
                {
                    distance = Dist[n.Profile.IATACode];
                    smallest = n;
                }
            }

            return smallest;
        }

        /// <summary>
        ///     Gets the path to an station
        /// </summary>
        /// <param name="d">
        ///     Destination
        /// </param>
        /// <returns></returns>
        public List<Station> GetPathTo(Station d)
        {
            var path = new List<Station>();

            path.Insert(0, d);

            while ((Previous.Count != 0) && (Previous[d.Profile.IATACode] != null))
            {
                d = Previous[d.Profile.IATACode];
                path.Insert(0, d);
            }

            return path;
        }

        #endregion

        #region Methods

        /// <summary>
        ///     Returns the distance between two stations
        /// </summary>
        /// <param name="o">Start station</param>
        /// <param name="d">Destination station</param>
        /// <returns></returns>
        private double getDistanceBetween(Station o, Station d)
        {
            if (StationHelpers.GetStationRoutes(o).Find(r => r.Destination1 == d || r.Destination2 == d) != null)
            {
                return 1;
            }
            return 0;
        }

        /// <summary>
        ///     Returns all stations with a route from the an station
        /// </summary>
        /// <param name="n">Station</param>
        /// <returns></returns>
        private IEnumerable<Station> GetNeighbors(Station n)
        {
            return StationHelpers.GetStationRoutes(n).Select(route => route.Destination1 == n ? route.Destination2 : route.Destination1).Where(destination => Basis.Contains(n)).ToList();
        }

        #endregion
    }
}