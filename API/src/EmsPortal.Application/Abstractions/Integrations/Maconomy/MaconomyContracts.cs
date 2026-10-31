namespace EmsPortal.Application.Abstractions.Integrations.Maconomy;

/// <summary>Where a Maconomy instance answers: the API root and the instance short name in every path.</summary>
public sealed record MaconomyEndpoint(string BaseUrl, string InstanceCode);

/// <summary>A container filter: the restriction (Maconomy's query language), the fields wanted back, and the row cap.</summary>
public sealed record MaconomyFilterRequest(string Restriction, IReadOnlyList<string> Fields, int Limit);

/// <summary>The filter pane's records, each as field name → value text, and the row count Maconomy reported.</summary>
public sealed record MaconomyFilterResult(IReadOnlyList<IReadOnlyDictionary<string, string?>> Records, int RowCount);

/// <summary>
/// One customer as a dropdown option: "number - name (specification 6 name)" to read, the number to store,
/// the specification 6 name on its own, null when blank, the REMS entity type it stands for, null when
/// it stands for none, the customer's email address and telephone, each null when blank, and the name
/// on its own, and the REMS industry code its specification 1 stands for, null when it stands for none.
/// </summary>
public sealed record MaconomyCustomerOption(
    string Text, string Value, string? Specification6Name, string? EntityType, string? EmailAddress, string? PhoneNumber,
    string Name, string? Industry = null);

/// <summary>A search's rows, and whether they are all there are — fewer than the cap asked for.</summary>
public sealed record MaconomyCustomerSearch(IReadOnlyList<MaconomyCustomerOption> Customers, bool Complete);

/// <summary>What a fresh login produced: when the token was issued, and when it stops being used.</summary>
public sealed record MaconomyLoginResult(DateTime IssuedOnUtc, DateTime ExpiresOnUtc);

/// <summary>The connection as an admin saves it. A null or empty password keeps the stored one.</summary>
public sealed record SaveMaconomyConnectionInput(
    string BaseUrl,
    string InstanceCode,
    string UserName,
    string? Password,
    string? ContainerId,
    int DefaultLimit,
    bool IsEnabled);

/// <summary>
/// One record to create on a card, as a mapping class produces it and the card writer sends it: the
/// container, the fields the instance is opened with, the values posted on create, the values posted on
/// the init step before it (a template reference Maconomy derives defaults from; null for a bare init),
/// and the key field the reply must carry back. Knows nothing about customers or jobs.
/// </summary>
public sealed record MaconomyCardWrite(
    string Container,
    IReadOnlyList<string> Fields,
    IReadOnlyDictionary<string, string?> Values,
    IReadOnlyDictionary<string, string?>? InitValues,
    string KeyField);

/// <summary>The record Maconomy created, as field name → value text.</summary>
public sealed record MaconomyCreatedRecord(IReadOnlyDictionary<string, string?> Data);

/// <summary>The job Maconomy created: the number its series assigned, and the name as stored.</summary>
public sealed record MaconomyCreatedJob(string Number, string Name);

/// <summary>Why a call to Maconomy did not produce an answer.</summary>
public enum MaconomyFailure
{
    /// <summary>The tenant has no connection, or it is switched off.</summary>
    NotConfigured,

    /// <summary>Maconomy refused the reconnect token. Retried once, after a fresh login.</summary>
    TokenRejected,

    /// <summary>Maconomy refused the credentials, or a token it had only just issued.</summary>
    AuthFailed,

    /// <summary>Maconomy could not be reached, took too long, or answered something other than success.</summary>
    Unavailable,

    /// <summary>Maconomy understood a write and refused the record: a validation message, a field it does not know.</summary>
    Rejected,
}

/// <summary>A call to Maconomy that did not produce an answer, and why.</summary>
public sealed class MaconomyException : Exception
{
    public MaconomyException(MaconomyFailure failure, string message, Exception? inner = null)
        : base(message, inner)
        => Failure = failure;

    public MaconomyFailure Failure { get; }
}

/// <summary>A rule of connection management broken by the caller — a new connection saved without a password.</summary>
public sealed class MaconomyConnectionException : Exception
{
    public MaconomyConnectionException(string message) : base(message)
    {
    }
}
