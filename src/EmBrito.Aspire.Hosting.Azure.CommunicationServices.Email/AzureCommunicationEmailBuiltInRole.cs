using System.ComponentModel;

namespace EmBrito.Aspire.Hosting.Azure.CommunicationServices;

/// <summary>
/// Azure built-in roles applicable to Communication Services email resources.
/// </summary>
/// <param name="value">The Azure role-definition ID.</param>
public readonly struct AzureCommunicationEmailBuiltInRole(string value)
    : IEquatable<AzureCommunicationEmailBuiltInRole>
{
    private const string CommunicationAndEmailServiceOwnerValue = "09976791-48a7-449e-bb21-39d1a415f350";
    private readonly string _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>
    /// Grants the permissions required to send email by using Microsoft Entra authentication.
    /// </summary>
    public static AzureCommunicationEmailBuiltInRole CommunicationAndEmailServiceOwner { get; } =
        new(CommunicationAndEmailServiceOwnerValue);

    /// <summary>
    /// Gets the display name for a known built-in role, or its ID for a custom role.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static string GetBuiltInRoleName(AzureCommunicationEmailBuiltInRole value) =>
        value._value == CommunicationAndEmailServiceOwnerValue
            ? nameof(CommunicationAndEmailServiceOwner)
            : value._value;

    /// <inheritdoc />
    public bool Equals(AzureCommunicationEmailBuiltInRole other) =>
        string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override bool Equals(object? obj) =>
        obj is AzureCommunicationEmailBuiltInRole other && Equals(other);

    /// <inheritdoc />
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value);

    /// <inheritdoc />
    public override string ToString() => _value;

    /// <summary>
    /// Converts a role-definition ID to a role value.
    /// </summary>
    public static implicit operator AzureCommunicationEmailBuiltInRole(string value) => new(value);

    /// <summary>Tests two role values for equality.</summary>
    public static bool operator ==(AzureCommunicationEmailBuiltInRole left, AzureCommunicationEmailBuiltInRole right) =>
        left.Equals(right);

    /// <summary>Tests two role values for inequality.</summary>
    public static bool operator !=(AzureCommunicationEmailBuiltInRole left, AzureCommunicationEmailBuiltInRole right) =>
        !left.Equals(right);
}
