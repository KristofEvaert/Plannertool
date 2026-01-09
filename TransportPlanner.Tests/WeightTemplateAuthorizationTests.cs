using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using TransportPlanner.Api.Controllers;
using Xunit;

namespace TransportPlanner.Tests;

public class WeightTemplateAuthorizationTests
{
    [Theory]
    [InlineData("Create")]
    [InlineData("Update")]
    public void WeightTemplatesController_RequiresStaffPolicy(string methodName)
    {
        var method = typeof(WeightTemplatesController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .First(m => m.Name == methodName);

        var authorize = method.GetCustomAttributes<AuthorizeAttribute>(inherit: true).FirstOrDefault();

        Assert.NotNull(authorize);
        Assert.Equal("RequireStaff", authorize!.Policy);
    }

    [Fact]
    public void WeightTemplatesController_DeleteRequiresAdminPolicy()
    {
        var method = typeof(WeightTemplatesController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .First(m => m.Name == "Delete");

        var authorize = method.GetCustomAttributes<AuthorizeAttribute>(inherit: true).FirstOrDefault();

        Assert.NotNull(authorize);
        Assert.Equal("RequireAdmin", authorize!.Policy);
    }
}
