namespace TransportPlanner.Infrastructure.Identity;

public static class AppRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Planner = "Planner";
    public const string Driver = "Driver";

    public const string AdminsAndAbove = $"{SuperAdmin},{Admin}";
    public const string NonDriverStaff = $"{SuperAdmin},{Admin},{Planner}";
    public const string StaffAndDrivers = $"{SuperAdmin},{Admin},{Planner},{Driver}";
}

