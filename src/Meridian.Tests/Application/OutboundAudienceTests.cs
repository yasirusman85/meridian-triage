using Meridian.Application.Templates;

namespace Meridian.Tests.Application;

public sealed class OutboundAudienceTests
{
    [Theory]
    [InlineData(TenantTemplates.AcknowledgementId, "tenant")]
    [InlineData(TenantTemplates.GasSafetyId, "tenant")]
    [InlineData(StaffTemplates.EscalationPrimaryId, "staff")]
    [InlineData(StaffTemplates.EscalationBackupId, "staff")]
    [InlineData(ContractorTemplates.JobId, "contractor")]
    public void Every_template_has_one_audience(string templateId, string audience)
    {
        Assert.Equal(audience, OutboundAudience.For(templateId));
    }

    [Fact]
    public void An_unknown_template_has_no_audience()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OutboundAudience.For("made-up"));
    }
}
