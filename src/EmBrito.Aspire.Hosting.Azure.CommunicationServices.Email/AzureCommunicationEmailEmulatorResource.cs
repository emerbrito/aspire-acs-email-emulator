using Aspire.Hosting.ApplicationModel;

namespace EmBrito.Aspire.Hosting.Azure.CommunicationServices;

/// <summary>
/// Wraps an <see cref="AzureCommunicationEmailResource"/> so container extension methods can configure its emulator.
/// </summary>
/// <param name="innerResource">The Azure resource whose annotations are shared with the emulator.</param>
public sealed class AzureCommunicationEmailEmulatorResource(AzureCommunicationEmailResource innerResource)
    : ContainerResource(innerResource?.Name ?? throw new ArgumentNullException(nameof(innerResource))), IResource
{
    private readonly AzureCommunicationEmailResource _innerResource = innerResource;

    /// <inheritdoc />
    public override string Name => _innerResource.Name;

    /// <inheritdoc />
    public override ResourceAnnotationCollection Annotations => _innerResource.Annotations;
}
