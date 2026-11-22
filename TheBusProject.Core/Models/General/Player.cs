using TheBusProject.Infrastructure.Enums;
using TheBusProject.Models.General.Countries;

namespace TheBusProject.Models.General
{
    public class Player
    {
        public int Id { get; set; }
        public Continent Continent { get; set; }
        public Region Region { get; set; }
        public bool MajorStations { get; set; }
        public int StartYear { get; set; }
        public CarrierFocus Focus { get; set; }
        public Difficulty Difficulty { get; set; }
        public int NumOfOpponents { get; set; }
        public bool RandomOpponents { get; set; }
        public bool SameRegion { get; set; }
        public bool RealData { get; set; }
        public bool UseDays { get; set; }
        public bool Paused { get; set; }
    }
}