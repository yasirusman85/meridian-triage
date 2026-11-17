using Microsoft.Extensions.Configuration;
using Meridian.Application.Staff;
using Meridian.Web.Configuration;

namespace Meridian.Tests.Web;

public sealed class StaffConfigurationTests
{
    [Fact]
    public void The_Staff_list_is_read_with_its_roles_and_phones()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Staff:0:Id"] = "rachel",
            ["Staff:0:Name"] = "Rachel",
            ["Staff:0:Phone"] = "07700 900461",
            ["Staff:0:Role"] = "primary",
            ["Staff:1:Id"] = "dawn",
            ["Staff:1:Name"] = "Dawn",
            ["Staff:1:Phone"] = "07700 900872",
            ["Staff:1:Role"] = "Backup",
        });

        var staff = StaffConfiguration.Read(configuration);

        Assert.Equal("Rachel (07700 900461)", staff.Primary.Recipient);
        Assert.Equal("Dawn (07700 900872)", staff.Backup.Recipient);
        Assert.Equal(new[] { "rachel", "dawn" }, staff.All.Select(s => s.Id));
    }

    [Fact]
    public void An_empty_Staff_list_gives_the_default_people()
    {
        Assert.Equal("rachel", StaffConfiguration.Read(Configuration(new Dictionary<string, string?>())).Primary.Id);
    }

    [Fact]
    public void A_role_other_than_primary_or_backup_stops_startup()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Staff:0:Id"] = "rachel",
            ["Staff:0:Name"] = "Rachel",
            ["Staff:0:Phone"] = "07700 900461",
            ["Staff:0:Role"] = "manager",
        });

        var error = Assert.Throws<InvalidOperationException>(() => StaffConfiguration.Read(configuration));
        Assert.Contains("Staff:0:Role", error.Message);
    }

    [Fact]
    public void The_escalation_window_defaults_to_30_minutes_and_must_be_a_whole_number()
    {
        Assert.Equal(30, EscalationConfiguration.Read(Configuration(new Dictionary<string, string?>())).WindowMinutes);
        Assert.Equal(10, EscalationConfiguration.Read(Configuration(new Dictionary<string, string?> { ["Escalation:WindowMinutes"] = "10" })).WindowMinutes);
        Assert.Throws<InvalidOperationException>(() =>
            EscalationConfiguration.Read(Configuration(new Dictionary<string, string?> { ["Escalation:WindowMinutes"] = "soon" })));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
