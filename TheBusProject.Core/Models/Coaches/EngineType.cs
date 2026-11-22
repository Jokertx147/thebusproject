using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.General;

namespace TheBusProject.Models.Coaches
{
    //the class for an engine type 
    [Serializable]
    public class EngineType : BaseModel
    {
        #region Constructors and Destructors

        public EngineType(
            string model,
            Manufacturer manufacturer,
            TypeOfEngine engine,
            NoiseLevel noise,
            double consumptation,
            long price,
            int maxspeed,
            int ceiling,
            double runway,
            double range,
            Period<int> produced)
        {
            Model = model;
            Manufacturer = manufacturer;
            Engine = engine;
            ConsumptationModifier = consumptation;
            Price = price;
            MaxSpeed = maxspeed;
            Ceiling = ceiling;
            RunwayModifier = runway;
            RangeModifier = range;
            Produced = produced;
            Types = new List<CoachType>();
            Noise = noise;
        }

        private EngineType(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Enums

        public enum NoiseLevel
        {
            VeryHigh,

            High,

            Medium,

            Low,

            VeryLow
        }

        public enum TypeOfEngine
        {
            Jet,

            Turboprop,

            /// <summary>The propeller coaches before the turboprops (the DC-3, the DC-6...).</summary>
            Piston
        }

        #endregion

        #region Public Properties

        [Versioning("ceiling")]
        public int Ceiling { get; set; }

        [Versioning("consumptation")]
        public double ConsumptationModifier { get; set; }

        [Versioning("engine")]
        public TypeOfEngine Engine { get; set; }

        [Versioning("manufacturer")]
        public Manufacturer Manufacturer { get; set; }

        [Versioning("speed")]
        public int MaxSpeed { get; set; }

        [Versioning("model")]
        public string Model { get; set; }

        [Versioning("noise")]
        public NoiseLevel Noise { get; set; }

        [Versioning("price")]
        public long Price { get; set; }

        [Versioning("produced")]
        public Period<int> Produced { get; set; }

        [Versioning("range")]
        public double RangeModifier { get; set; }

        [Versioning("runway")]
        public double RunwayModifier { get; set; }

        [Versioning("types")]
        public List<CoachType> Types { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        public void AddCoachType(CoachType type)
        {
            Types.Add(type);
        }

        #endregion
    }

    //the list of engine types
    public class EngineTypes
    {
        #region Static Fields

        private static List<EngineType> _types = new List<EngineType>();

        #endregion

        #region Public Methods and Operators

        public static void Clear()
        {
            _types = new List<EngineType>();
            _standard = new System.Collections.Concurrent.ConcurrentDictionary<CoachType, EngineType>();
        }

        public static void AddEngineType(EngineType type)
        {
            _types.Add(type);
            _standard = new System.Collections.Concurrent.ConcurrentDictionary<CoachType, EngineType>();
        }

        // the standard (cheapest) engine of each coach type; an coach's price asks for it every time
        private static System.Collections.Concurrent.ConcurrentDictionary<CoachType, EngineType> _standard =
            new System.Collections.Concurrent.ConcurrentDictionary<CoachType, EngineType>();

        //returns the list of engine types
        public static List<EngineType> GetEngineTypes()
        {
            return _types;
        }

        //returns the list of engine types for an coach type
        public static List<EngineType> GetEngineTypes(CoachType type, int year)
        {
            return GetEngineTypes(type).FindAll(t => t.Produced.From <= year && t.Produced.To >= year);
        }

        public static List<EngineType> GetEngineTypes(CoachType type)
        {
            return (from t in _types from at in t.Types where at.Name == type.Name select t).ToList();
        }

        /// <summary>The engine's maker and model as players know it, e.g. "Pratt & Whitney PW4090", "GE90-94B".</summary>
        public static string FullName(EngineType e)
        {
            if (e == null) return "";
            string maker = e.Manufacturer?.ShortName ?? "";
            switch (maker)
            {
                case "Pratt": maker = "Pratt & Whitney"; break;
                case "PWC": maker = "Pratt & Whitney Canada"; break;
                case "IAE-Engines": maker = "IAE"; break;
                case "EA": maker = "Engine Alliance"; break;
            }
            return maker == "" || e.Model.StartsWith(maker) ? e.Model : maker + " " + e.Model;
        }

        /// <summary>What an engine changes on its coach, e.g. "3% less fuel, 2% more range" ("" when nothing).</summary>
        public static string Describe(EngineType e)
        {
            if (e == null) return "";
            var parts = new List<string>();
            Func<double, string, string, string> part = (m, less, more) =>
            {
                int pct = (int) Math.Round(Math.Abs(1 - m)*100);
                return pct == 0 ? null : pct + "% " + (m < 1 ? less : more);
            };
            parts.Add(part(e.ConsumptationModifier, "less fuel", "more fuel"));
            parts.Add(part(e.RangeModifier, "less range", "more range"));
            parts.Add(part(e.RunwayModifier, "shorter runway", "longer runway"));
            return string.Join(", ", parts.Where(p => p != null));
        }

        //returns the standard engine for an coach type
        public static EngineType GetStandardEngineType(CoachType type, int year)
        {
            List<EngineType> allTypes = GetEngineTypes(type, year);

            if (allTypes.Count > 0)
            {
                return allTypes.OrderBy(t => t.Price).First();
            }

            return null;
        }

        public static EngineType GetStandardEngineType(CoachType type)
        {
            var cache = _standard;
            EngineType found;
            if (type != null && cache.TryGetValue(type, out found)) return found;
            found = FindStandardEngineType(type);
            if (type != null) cache[type] = found;
            return found;
        }

        private static EngineType FindStandardEngineType(CoachType type)
        {
            List<EngineType> allTypes = GetEngineTypes(type);

            if (allTypes.Count > 0)
            {
                return allTypes.OrderBy(t => t.Price).First();
            }

            return null;
        }

        #endregion

        //adds an engine type to the list
    }
}