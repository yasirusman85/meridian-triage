using Meridian.Application.Contractors;
using Meridian.Domain.Contractors;
using Meridian.Domain.Jobs;
using Meridian.Infrastructure.Persistence;

namespace Meridian.Tests.Application;

/// <summary>Each job shows its building and usual contractor (story 5): the lookup rows of the I/O matrix.</summary>
public class ContractorLookupTests
{
    private static readonly ContractorLookup Seeded = new(SeedData.CreateContractorAssignments());

    private static Guid Building(string name) => SeedData.IdFor("building", name);

    private static Guid Contractor(string name) => SeedData.IdFor("contractor", name);

    [Fact]
    public void Assigned_a_heating_job_at_Alder_Court_gets_Alder_Courts_heating_contractor_by_building()
    {
        var suggestion = Seeded.Suggest(Building("Alder Court"), Category.Heating);

        Assert.Equal(new ContractorSuggestion(Contractor("Warmhold Heating"), ContractorRule.Building), suggestion);
    }

    [Fact]
    public void Default_a_pest_job_at_a_building_with_no_pest_assignment_gets_the_default_pest_contractor()
    {
        Assert.DoesNotContain(SeedData.Assignments, a => a.Building == "Alder Court" && a.Category == Category.Pest);

        var suggestion = Seeded.Suggest(Building("Alder Court"), Category.Pest);

        Assert.Equal(new ContractorSuggestion(Contractor("Clearway Pest Control"), ContractorRule.Default), suggestion);
    }

    [Theory]
    [InlineData("Alder Court")]
    [InlineData(null)]
    public void Unsure_gets_no_suggestion(string? building)
    {
        Assert.Null(Seeded.Suggest(building is null ? null : Building(building), Category.Unsure));
    }

    [Fact]
    public void No_property_a_plumbing_job_with_an_unresolved_property_gets_the_default_plumbing_contractor()
    {
        var suggestion = Seeded.Suggest(null, Category.Plumbing);

        Assert.Equal(new ContractorSuggestion(Contractor("Northway Plumbing"), ContractorRule.Default), suggestion);
    }

    [Fact]
    public void An_unknown_building_falls_back_to_the_default()
    {
        Assert.Equal(ContractorRule.Default, Seeded.Suggest(Guid.NewGuid(), Category.Heating)?.Rule);
    }

    [Fact]
    public void Seed_changed_a_changed_assignment_gives_the_new_suggestion()
    {
        var changed = SeedData.Assignments
            .Select(a => a.Building == "Alder Court" && a.Category == Category.Heating
                ? (a.Building, a.Category, "Cottonfield Boiler Services")
                : a)
            .ToList();
        var lookup = new ContractorLookup(SeedData.CreateContractorAssignments(changed));

        var suggestion = lookup.Suggest(Building("Alder Court"), Category.Heating);

        Assert.Equal(new ContractorSuggestion(Contractor("Cottonfield Boiler Services"), ContractorRule.Building), suggestion);
        Assert.Equal(Contractor("Warmhold Heating"), lookup.Suggest(Building("Beechwood House"), Category.Heating)?.ContractorId);
    }

    [Fact]
    public void A_category_with_no_assignment_at_all_gets_no_suggestion()
    {
        var lookup = new ContractorLookup(Array.Empty<ContractorAssignment>());

        Assert.Null(lookup.Suggest(Building("Alder Court"), Category.Plumbing));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Two_usual_contractors_for_one_building_and_category_are_rejected(bool companyWide)
    {
        Guid? building = companyWide ? null : Building("Alder Court");
        var assignments = new[]
        {
            ContractorAssignment.Create(Guid.NewGuid(), building, Category.Heating, Contractor("Warmhold Heating")),
            ContractorAssignment.Create(Guid.NewGuid(), building, Category.Heating, Contractor("Cottonfield Boiler Services")),
        };

        Assert.Throws<ArgumentException>(() => new ContractorLookup(assignments));
    }

    [Fact]
    public void The_seed_has_a_default_for_every_trade_and_every_building_has_its_own_assignments()
    {
        var trades = Enum.GetValues<Category>().Where(c => c != Category.Unsure);
        Assert.All(trades, c => Assert.Equal(ContractorRule.Default, Seeded.Suggest(null, c)?.Rule));
        Assert.All(
            SeedData.Buildings.SelectMany(b => SeedData.BuildingCategories.Select(c => (b.Building, c))),
            x => Assert.Equal(ContractorRule.Building, Seeded.Suggest(Building(x.Building), x.c)?.Rule));
    }

    [Fact]
    public void The_seed_has_one_usual_contractor_per_trade_plus_heating_and_plumbing_fallbacks()
    {
        var contractors = SeedData.CreateContractors();
        Assert.All(contractors, c => Assert.False(string.IsNullOrWhiteSpace(c.Phone)));
        Assert.Equal(2, contractors.Count(c => c.Trade == Category.Heating));
        Assert.Equal(2, contractors.Count(c => c.Trade == Category.Plumbing));
        Assert.All(
            contractors.Where(c => c.Trade is not (Category.Heating or Category.Plumbing)),
            c => Assert.Single(contractors, x => x.Trade == c.Trade));

        // Every assignment names a seeded contractor of the right trade; the fallbacks are nobody's usual contractor.
        var byId = contractors.ToDictionary(c => c.Id);
        var assignments = SeedData.CreateContractorAssignments();
        Assert.All(assignments, a => Assert.Equal(a.Category, byId[a.ContractorId].Trade));
        Assert.DoesNotContain(assignments, a => a.ContractorId == Contractor("Irwell Pipe and Drain"));
        Assert.DoesNotContain(assignments, a => a.ContractorId == Contractor("Cottonfield Boiler Services"));
    }
}
