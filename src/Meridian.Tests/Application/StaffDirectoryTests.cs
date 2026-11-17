using Meridian.Application.Staff;

namespace Meridian.Tests.Application;

public sealed class StaffDirectoryTests
{
    private static readonly StaffMember Rachel = new("rachel", "Rachel", "07700 900461", StaffRole.Primary);
    private static readonly StaffMember Dawn = new("dawn", "Dawn", "07700 900872", StaffRole.Backup);

    [Fact]
    public void The_default_is_Rachel_as_primary_and_Dawn_as_backup()
    {
        var staff = new StaffDirectory();

        Assert.Equal(Rachel, staff.Primary);
        Assert.Equal(Dawn, staff.Backup);
        Assert.Equal(new[] { "rachel", "dawn" }, staff.All.Select(s => s.Id));
        Assert.Equal("Rachel (07700 900461)", staff.Primary.Recipient);
    }

    [Fact]
    public void Find_ignores_case_and_surrounding_spaces_and_returns_null_for_unknown_people()
    {
        var staff = new StaffDirectory();

        Assert.Equal("dawn", staff.Find(" DAWN ")!.Id);
        Assert.Null(staff.Find("mallory"));
        Assert.Null(staff.Find(" "));
    }

    [Fact]
    public void Exactly_one_primary_and_one_backup_are_required_and_they_must_differ()
    {
        Assert.Throws<ArgumentException>(() => new StaffDirectory(new[] { Rachel }));
        Assert.Throws<ArgumentException>(() => new StaffDirectory(new[] { Rachel, Rachel with { Id = "sam" } }));
        Assert.Throws<ArgumentException>(() => new StaffDirectory(new[] { Rachel, Dawn with { Role = StaffRole.Primary } }));
        Assert.Throws<ArgumentException>(() => new StaffDirectory(new[] { Rachel, Rachel with { Role = StaffRole.Backup } }));
    }

    [Fact]
    public void An_id_is_listed_once_and_every_member_needs_a_name_and_a_phone()
    {
        Assert.Throws<ArgumentException>(() => new StaffDirectory(new[] { Rachel, Dawn with { Id = "RACHEL" } }));
        Assert.Throws<ArgumentException>(() => new StaffDirectory(new[] { Rachel, Dawn with { Phone = " " } }));
        Assert.Throws<ArgumentException>(() => new StaffDirectory(new[] { Rachel, Dawn with { Name = "" } }));
    }

    [Fact]
    public void A_configured_list_replaces_the_default_and_drives_the_primary_and_backup()
    {
        var staff = new StaffDirectory(new[]
        {
            new StaffMember("priya", "Priya", "07700 900111", StaffRole.Primary),
            new StaffMember("tom", "Tom", "07700 900222", StaffRole.Backup),
        });

        Assert.Equal("priya", staff.Primary.Id);
        Assert.Equal("tom", staff.Backup.Id);
        Assert.Null(staff.Find("rachel"));
    }
}
