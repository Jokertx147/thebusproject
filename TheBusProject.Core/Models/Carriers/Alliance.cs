using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using TheBusProject.Infrastructure;
using TheBusProject.Models.Stations;

namespace TheBusProject.Models.Carriers
{
    [Serializable]
    //the class for an alliance of carriers
    public class Alliance : BaseModel
    {
        #region Constructors and Destructors

        public Alliance(DateTime formationDate, string name, Station headquarter)
        {
            FormationDate = formationDate;
            Name = name;
            Members = new List<AllianceMember>();
            PendingMembers = new List<PendingAllianceMember>();
            Headquarter = headquarter;
        }

        private Alliance(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Public Properties

        [Versioning("date")]
        public DateTime FormationDate { get; set; }

        [Versioning("headquarter")]
        public Station Headquarter { get; set; }

        public bool IsHumanAlliance
        {
            get { return Members.ToList().Exists(m => m.Carrier.IsHuman); }
        }

        [Versioning("logo")]
        public string Logo { get; set; }

        [Versioning("members")]
        public List<AllianceMember> Members { get; set; }

        [Versioning("name")]
        public string Name { get; set; }

        [Versioning("pending")]
        public List<PendingAllianceMember> PendingMembers { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        public static string GenerateAllianceName()
        {
            var rnd = new Random();

            string[] tNames =
                {
                    "Wings Alliance", "Qualiflyer", "Air Team", "Sky Alliance", "WOW Alliance",
                    "Air Alliance", "Blue Sky", "Golden Circle Alliance", "Skywalkers", "One Air Alliance"
                };
            List<string> aNames = (from a in Alliances.GetAlliances() select a.Name).ToList();

            List<string> names = tNames.ToList().Except(aNames).ToList();

            return names[rnd.Next(names.Count)];
        }

        //adds an carrier to the alliance
        public void AddMember(AllianceMember carrier)
        {
            Members.Add(carrier);
            carrier.Carrier.AddAlliance(this);
        }

        public void AddPendingMember(PendingAllianceMember pending)
        {
            PendingAllianceMember member = PendingMembers.FirstOrDefault(p => p.Carrier == pending.Carrier);

            if (member != null)
            {
                RemovePendingMember(member);
            }

            PendingMembers.Add(pending);
        }

        //removes an carrier from the alliance
        public void RemoveMember(AllianceMember carrier)
        {
            Members.Remove(carrier);
            carrier.Carrier.RemoveAlliance(this);
        }

        public void RemoveMember(Carrier carrier)
        {
            AllianceMember member = Members.FirstOrDefault(m => m.Carrier == carrier);
            Members.Remove(member);
            carrier.RemoveAlliance(this);
        }

        //adds a pending member to the alliance

        //removes a pending member from the alliance
        public void RemovePendingMember(PendingAllianceMember pending)
        {
            PendingMembers.Remove(pending);
        }

        #endregion

        //the method for generating an alliance name
    }

    //the list of alliances
    public class Alliances
    {
        #region Static Fields

        private static readonly List<Alliance> alliances = new List<Alliance>();

        #endregion

        #region Public Methods and Operators

        public static void AddAlliance(Alliance alliance)
        {
            alliances.Add(alliance);
        }

        //removes an alliance from the list

        //clears the list of alliances
        public static void Clear()
        {
            alliances.Clear();
        }

        public static List<Alliance> GetAlliances()
        {
            return alliances;
        }

        public static void RemoveAlliance(Alliance alliance)
        {
            alliances.Remove(alliance);
        }

        #endregion

        //adds an alliance to the list
    }

    [Serializable]
    //the class for pending acceptions to an alliance
    public class PendingAllianceMember : BaseModel
    {
        #region Constructors and Destructors

        public PendingAllianceMember(DateTime date, Alliance alliance, Carrier carrier, AcceptType type)
        {
            Alliance = alliance;
            Carrier = carrier;
            Date = date;
            Type = type;
        }

        private PendingAllianceMember(SerializationInfo info, StreamingContext ctxt) : base(info, ctxt)
        {
        }

        #endregion

        #region Enums

        public enum AcceptType
        {
            Invitation,

            Request
        }

        #endregion

        #region Public Properties

        public Carrier Carrier { get; set; }

        public Alliance Alliance { get; set; }

        public DateTime Date { get; set; }

        public AcceptType Type { get; set; }

        #endregion

        #region Public Methods and Operators

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            AddVersion(info, 1);

            base.GetObjectData(info, context);
        }

        #endregion
    }
}