using System;
using System.Linq;
using TheBusProject.Models.Coaches;
using TheBusProject.Models.Carriers;
using TheBusProject.Models.General;
using TheBusProject.Models.Routes;

namespace TheBusProject.Helpers
{
    /*the class for some general route helpers*/

    public class RouteHelpers
    {
        /*returns the price score for the route*/

        public static double GetRoutePriceScore(Route route)
        {
            double basePrice = PassengerHelpers.GetPassengerPrice(route.Destination1, route.Destination2);

            double price = ((PassengerRoute) route).GetFarePrice(CoachClass.ClassType.EconomyClass);

            double priceFactor = price/basePrice;

            double priceLevel = 10*priceFactor;

            return Math.Max(1, 16 - priceLevel);
        }

        /*returns the seats score for the route*/

        public static double GetRouteSeatsScore(Route route)
        {
            TimeSpan tripTime = MathHelpers.GetTripTime(route.Destination1, route.Destination2, route.GetCoaches()[0].Coach.Type);

            CoachFacility seats = route.GetCoaches()[0].Coach.GetCoachClass(CoachClass.ClassType.EconomyClass).GetFacility(CoachFacility.FacilityType.Seat);

            IOrderedEnumerable<CoachFacility> seatfacilities =
                CoachFacilities.GetFacilities(CoachFacility.FacilityType.Seat).Where(f => f.FromYear >= GameObject.GetInstance().GameTime.Year).OrderBy(f => f.ServiceLevel);

            int facilitynumber = seatfacilities.Count() - seatfacilities.ToList().IndexOf(seats) - 1; //max == 6

            double seatlevel;

            if (tripTime.Hours < 1)
            {
                seatlevel = 13 - facilitynumber;
            }
            else if (tripTime.Hours >= 1 && tripTime.Hours < 3)
            {
                seatlevel = 12 - facilitynumber;
            }
            else if (tripTime.Hours >= 3 && tripTime.Hours < 7)
            {
                seatlevel = 11 - facilitynumber;
            }
            else
            {
                seatlevel = 10 - facilitynumber;
            }

            return Math.Min(10, seatlevel);
        }

        /*returns the meal score for the route*/

        public static double GetRouteMealScore(Route route)
        {
            TimeSpan tripTime = MathHelpers.GetTripTime(route.Destination1, route.Destination2, route.GetCoaches()[0].Coach.Type);

            RouteFacility food = ((PassengerRoute) route).GetRouteCoachClass(CoachClass.ClassType.EconomyClass).GetFacility(RouteFacility.FacilityType.Food);

            //routes made before a facility existed (or without one) have none
            if (food == null)
                return 3;

            double foodlevel;

            if (tripTime.Hours < 1)
            {
                if (food.ServiceLevel < 0)
                    foodlevel = 5;
                else
                    foodlevel = 5 + (food.ServiceLevel/10);
            }
            else if (tripTime.Hours >= 1 && tripTime.Hours < 3)
            {
                if (food.ServiceLevel < 0)
                    foodlevel = 4;
                else
                    foodlevel = 4 + (food.ServiceLevel/10);
            }
            else if (tripTime.Hours >= 3 && tripTime.Hours < 7)
            {
                if (food.ServiceLevel < 0)
                    foodlevel = 2;
                else
                    foodlevel = 3 + (food.ServiceLevel/10);
            }
            else
            {
                if (food.ServiceLevel < 0)
                    foodlevel = 1;
                else
                    foodlevel = 2 + (food.ServiceLevel/10);
            }

            return Math.Min(10, foodlevel);
        }

        /*returns the plane type score for the route*/

        public static double GetRoutePlaneTypeScore(Route route)
        {
            TimeSpan tripTime = MathHelpers.GetTripTime(route.Destination1, route.Destination2, route.GetCoaches()[0].Coach.Type);

            CoachType coachtype = route.GetCoaches()[0].Coach.Type;

            int oldTypeFactor = coachtype.Produced.To < GameObject.GetInstance().GameTime ? 1 : 0;

            int coachLevel;

            int paxLevel = ((CoachPassengerType) coachtype).MaxSeatingCapacity/40; //maks 10??

            if (tripTime.Hours < 1)
            {
                coachLevel = 4 + paxLevel - oldTypeFactor;
            }
            else if (tripTime.Hours >= 1 && tripTime.Hours < 3)
            {
                coachLevel = 3 + paxLevel - oldTypeFactor;
            }
            else if (tripTime.Hours >= 3 && tripTime.Hours < 7)
            {
                coachLevel = 2 + paxLevel - oldTypeFactor;
            }
            else
            {
                coachLevel = 1 + paxLevel - oldTypeFactor;
            }

            return Math.Min(10, coachLevel);
        }

        /*returns the plane age score for the route*/

        public static double GetPlaneAgeScore(Route route)
        {
            FleetCoach coach = route.GetCoaches()[0];

            int age = coach.Coach.Age;

            double score = 10 - (age/2);

            return Math.Max(1, score);
        }

        /*returns the luggage score for the route*/

        public static double GetRouteLuggageScore(Route route)
        {
            double bagFee = GameObject.GetInstance().HumanCarrier.Fees.GetValue(FeeTypes.GetType("1 Bag"));

            if (bagFee.Equals(0))
                return 8;
            return 3;
        }

        /*returns the total score of the route*/

        public static double GetRouteTotalScore(Route route)
        {
            double score = GetPlaneAgeScore(route) + GetRouteIntripScore(route) + GetRouteMealScore(route) + GetRoutePlaneTypeScore(route) + GetRoutePriceScore(route) + GetRouteSeatsScore(route) +
                           GetRouteLuggageScore(route);

            if ((int) RouteFacility.FacilityType.WiFi <= GameObject.GetInstance().GameTime.Year)
            {
                double wifiScore = GetRouteWifiScore(route);
                score += wifiScore;

                return score/8;
            }

            return score/7;
        }

        /*returns the intrip demand score for the route*/

        public static double GetRouteIntripScore(Route route)
        {
            TimeSpan tripTime = MathHelpers.GetTripTime(route.Destination1, route.Destination2, route.GetCoaches()[0].Coach.Type);

            CoachFacility intrip = route.GetCoaches()[0].Coach.GetCoachClass(CoachClass.ClassType.EconomyClass).GetFacility(CoachFacility.FacilityType.Video);

            IOrderedEnumerable<CoachFacility> videofacilities =
                CoachFacilities.GetFacilities(CoachFacility.FacilityType.Video).Where(f => f.FromYear >= GameObject.GetInstance().GameTime.Year).OrderBy(f => f.ServiceLevel);

            int facilitynumber = videofacilities.Count() - videofacilities.ToList().IndexOf(intrip) - 1;

            double intriplevel;

            if (tripTime.Hours < 1)
            {
                intriplevel = 9 - facilitynumber;
            }
            else if (tripTime.Hours >= 1 && tripTime.Hours < 3)
            {
                intriplevel = 8 - facilitynumber;
            }
            else if (tripTime.Hours >= 3 && tripTime.Hours < 7)
            {
                intriplevel = 7 - facilitynumber;
            }
            else
            {
                intriplevel = 10 - (2*facilitynumber);
            }

            return Math.Min(10, intriplevel);
        }

        /*returns the wifi score for the route*/

        public static double GetRouteWifiScore(Route route)
        {
            RouteFacility wifi = ((PassengerRoute) route).GetRouteCoachClass(CoachClass.ClassType.EconomyClass).GetFacility(RouteFacility.FacilityType.WiFi);

            if (wifi == null)
                return 3;

            if (wifi.Name == "None")
                return 3;
            return wifi.Name == "Buyable" ? 6 : 9;
        }
    }
}