namespace EmBrito.Aspire.Hosting.Azure.CommunicationServices;

/// <summary>
/// Describes a customer-managed domain owned by an Azure Communication Services Email resource.
/// </summary>
/// <remarks>
/// Creating this descriptor declares the Azure domain resource. It does not assert that the
/// required ownership, SPF, and DKIM DNS records have been verified.
/// </remarks>
public sealed class AzureCommunicationEmailDomain
{
    internal AzureCommunicationEmailDomain(
        AzureCommunicationEmailResource owner,
        string name,
        int index)
    {
        Owner = owner;
        Name = name;
        BicepIdentifier = $"customDomain{index}";
    }

    /// <summary>
    /// Gets the fully qualified domain name registered with Email Communication Services.
    /// </summary>
    public string Name { get; }

    internal AzureCommunicationEmailResource Owner { get; }

    internal string BicepIdentifier { get; }
}
