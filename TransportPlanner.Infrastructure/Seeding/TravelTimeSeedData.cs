namespace TransportPlanner.Infrastructure.Seeding;

internal static class TravelTimeSeedData
{
    public const string RegionsCsv = @"RegionId,Name,Country,MinLat,MinLon,MaxLat,MaxLon,Priority
1,BE_Antwerp_Metro,BE,51.05,4.15,51.40,4.70,90
2,BE_Brussels_Metro,BE,50.75,4.20,50.95,4.55,95
3,BE_Ghent_Metro,BE,51.00,3.55,51.20,3.85,80
4,BE_Limburg_Rural,BE,50.80,5.10,51.35,5.85,60
5,BE_Flanders_Other,BE,50.95,2.55,51.45,5.10,50
6,BE_Wallonia,BE,49.50,2.80,50.85,6.50,40
7,NL_Randstad,NL,51.80,4.20,52.60,5.25,95
8,NL_Amsterdam,NL,52.25,4.70,52.45,5.05,99
9,NL_Rotterdam,NL,51.80,4.25,52.05,4.70,92
10,NL_Utrecht,NL,52.00,4.95,52.20,5.25,88
11,NL_Eindhoven_Brabant,NL,51.25,5.25,51.60,5.75,75
12,NL_Limburg,NL,50.75,5.55,51.25,6.20,70
13,NL_Other,NL,50.75,3.35,53.55,7.25,30
99,DEFAULT,XX,-90.00,-180.00,90.00,180.00,0";

    public const string SpeedProfilesCsv = @"RegionId,DayType,BucketStartHour,BucketEndHour,AvgMinutesPerKm
1,Weekday,0,6,0.95
1,Weekday,6,9,1.90
1,Weekday,9,16,1.25
1,Weekday,16,19,1.85
1,Weekday,19,24,1.10

2,Weekday,0,6,1.05
2,Weekday,6,9,2.10
2,Weekday,9,16,1.45
2,Weekday,16,19,2.05
2,Weekday,19,24,1.25

3,Weekday,0,6,0.90
3,Weekday,6,9,1.60
3,Weekday,9,16,1.15
3,Weekday,16,19,1.55
3,Weekday,19,24,1.00

4,Weekday,0,6,0.75
4,Weekday,6,9,0.95
4,Weekday,9,16,0.80
4,Weekday,16,19,0.95
4,Weekday,19,24,0.78

5,Weekday,0,6,0.80
5,Weekday,6,9,1.05
5,Weekday,9,16,0.88
5,Weekday,16,19,1.05
5,Weekday,19,24,0.82

6,Weekday,0,6,0.78
6,Weekday,6,9,0.98
6,Weekday,9,16,0.85
6,Weekday,16,19,0.98
6,Weekday,19,24,0.80

7,Weekday,0,6,1.00
7,Weekday,6,9,2.05
7,Weekday,9,16,1.35
7,Weekday,16,19,2.00
7,Weekday,19,24,1.20

8,Weekday,0,6,1.10
8,Weekday,6,9,2.30
8,Weekday,9,16,1.55
8,Weekday,16,19,2.20
8,Weekday,19,24,1.35

9,Weekday,0,6,1.05
9,Weekday,6,9,2.10
9,Weekday,9,16,1.45
9,Weekday,16,19,2.05
9,Weekday,19,24,1.25

10,Weekday,0,6,0.95
10,Weekday,6,9,1.95
10,Weekday,9,16,1.30
10,Weekday,16,19,1.90
10,Weekday,19,24,1.15

11,Weekday,0,6,0.85
11,Weekday,6,9,1.35
11,Weekday,9,16,1.00
11,Weekday,16,19,1.35
11,Weekday,19,24,0.95

12,Weekday,0,6,0.82
12,Weekday,6,9,1.15
12,Weekday,9,16,0.92
12,Weekday,16,19,1.15
12,Weekday,19,24,0.88

13,Weekday,0,6,0.85
13,Weekday,6,9,1.10
13,Weekday,9,16,0.95
13,Weekday,16,19,1.10
13,Weekday,19,24,0.90

99,Weekday,0,24,1.10";
}
