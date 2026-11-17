namespace Meridian.Application.Templates;

/// <summary>
/// Who an outbound message is for, from its template (AD-7): the tenant's acknowledgement, the staff SMS for an
/// emergency, or the contractor's job link. The API sends it as <c>audience</c>, so the SPA filters on that flag and
/// keeps no template-id list of its own.
/// </summary>
public static class OutboundAudience
{
    public const string Tenant = "tenant";
    public const string Staff = "staff";
    public const string Contractor = "contractor";

    /// <summary>The audience of a template id.</summary>
    public static string For(string templateId) => templateId switch
    {
        TenantTemplates.AcknowledgementId or TenantTemplates.GasSafetyId => Tenant,
        StaffTemplates.EscalationPrimaryId or StaffTemplates.EscalationBackupId => Staff,
        ContractorTemplates.JobId => Contractor,
        _ => throw new ArgumentOutOfRangeException(nameof(templateId), templateId, "Unknown template."),
    };
}
