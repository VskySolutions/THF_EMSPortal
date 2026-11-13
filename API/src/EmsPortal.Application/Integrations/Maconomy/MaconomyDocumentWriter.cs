using EmsPortal.Application.Abstractions.Integrations.Maconomy;
using EmsPortal.Application.Integrations.Maconomy.Mapping;
using EmsPortal.Domain.Entities;
using EmsPortal.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace EmsPortal.Application.Integrations.Maconomy;

/// <summary>Default <see cref="IMaconomyDocumentWriter"/>, on the names <see cref="MaconomyDocumentMapping"/> holds.</summary>
public sealed class MaconomyDocumentWriter : IMaconomyDocumentWriter
{
    private const int LinesMax = 2000;

    private readonly IMaconomySessionManager _session;
    private readonly IMaconomyGateway _gateway;
    private readonly MaconomyOptions _options;

    public MaconomyDocumentWriter(IMaconomySessionManager session, IMaconomyGateway gateway, IOptions<MaconomyOptions> options)
    {
        _session = session;
        _gateway = gateway;
        _options = options.Value;
    }

    public async Task<MaconomyStoredDocument> AttachToCustomerAsync(
        MaconomyConnection connection, string customerNumber, MaconomyFile file, CancellationToken cancellationToken = default)
    {
        var endpoint = new MaconomyEndpoint(connection.BaseUrl, connection.InstanceCode);
        var number = customerNumber.Trim();
        var fields = new[] { MaconomyDocumentMapping.CustomerNumberField, MaconomyDocumentMapping.ArchiveNumberField };

        // Read from the container every other read uses; the action goes to the one the create writes to.
        var card = await ReadOneAsync(
            connection, endpoint, MaconomyCustomerService.CustomerContainer, MaconomyDocumentMapping.CustomerNumberField, number, fields, cancellationToken)
            ?? throw new MaconomyException(MaconomyFailure.Rejected, $"Maconomy holds no customer {number}.");
        var archive = Clean(card, MaconomyDocumentMapping.ArchiveNumberField);
        var before = archive is null ? Empty : await LinesAsync(connection, endpoint, archive, cancellationToken);
        var named = file with { FileName = MaconomyDocumentMapping.DocumentNameOf(file.FileName, before.Select(NameOf)) };

        var drop = await _session.InvokeAsync(connection, (token, ct) => _gateway.DropFileAsync(endpoint, token, named, ct), cancellationToken);
        var attached = await _session.InvokeAsync(
            connection,
            (token, ct) => _gateway.RunCardActionAsync(
                endpoint, token,
                new MaconomyCardAction(
                    _options.CustomerCard.Container, fields, MaconomyDocumentMapping.CustomerNumberField, number,
                    MaconomyDocumentMapping.AttachAction, drop),
                ct),
            cancellationToken);

        archive = Clean(attached, MaconomyDocumentMapping.ArchiveNumberField) ?? archive ?? MaconomyDocumentMapping.CustomerArchiveNumber(number);
        var after = await LinesQuietlyAsync(connection, endpoint, archive, cancellationToken);
        return Stored(archive, named.FileName, before, after);
    }

    public async Task<MaconomyStoredDocument> AttachToJobAsync(
        MaconomyConnection connection, string jobNumber, MaconomyFile file, CancellationToken cancellationToken = default)
    {
        var endpoint = new MaconomyEndpoint(connection.BaseUrl, connection.InstanceCode);
        var number = jobNumber.Trim();
        var jobFields = new[] { MaconomyDocumentMapping.JobNumberField, MaconomyDocumentMapping.ArchiveNumberField };

        var job = await ReadOneAsync(
            connection, endpoint, MaconomyDocumentMapping.JobContainer, MaconomyDocumentMapping.JobNumberField, number, jobFields, cancellationToken)
            ?? throw new MaconomyException(MaconomyFailure.Rejected, $"Maconomy holds no job {number}.");
        var linked = Clean(job, MaconomyDocumentMapping.ArchiveNumberField);
        var archive = linked ?? MaconomyDocumentMapping.JobArchiveNumber(number);

        if (linked is null)
        {
            // The archive first, then the job pointed at it, and only then the file: a step that fails
            // leaves at most an empty archive behind, never a document nobody can find from the job.
            await AddAsync(connection, endpoint, archive, fileDropUri: null, cancellationToken);
            await _session.InvokeAsync(
                connection,
                (token, ct) => _gateway.UpdateCardAsync(
                    endpoint, token,
                    new MaconomyCardUpdate(
                        MaconomyDocumentMapping.JobContainer, jobFields, MaconomyDocumentMapping.JobNumberField, number,
                        new Dictionary<string, string?> { [MaconomyDocumentMapping.ArchiveNumberField] = archive }),
                    ct),
                cancellationToken);
        }

        var before = await LinesAsync(connection, endpoint, archive, cancellationToken);
        var named = file with { FileName = MaconomyDocumentMapping.DocumentNameOf(file.FileName, before.Select(NameOf)) };
        var drop = await _session.InvokeAsync(connection, (token, ct) => _gateway.DropFileAsync(endpoint, token, named, ct), cancellationToken);
        var added = await AddAsync(connection, endpoint, archive, drop, cancellationToken);
        // The add's own reply where it lists the documents, a fresh read where it does not.
        var after = added.Count > before.Count ? added : await LinesQuietlyAsync(connection, endpoint, archive, cancellationToken);
        return Stored(archive, named.FileName, before, after);
    }

    private static readonly IReadOnlyList<IReadOnlyDictionary<string, string?>> Empty = Array.Empty<IReadOnlyDictionary<string, string?>>();

    // With no file the call only makes sure the archive is there.
    private Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> AddAsync(
        MaconomyConnection connection, MaconomyEndpoint endpoint, string archive, string? fileDropUri, CancellationToken cancellationToken)
        => _session.InvokeAsync(
            connection,
            (token, ct) => _gateway.AddArchiveDocumentAsync(
                endpoint, token,
                new MaconomyArchiveDocumentAdd(
                    MaconomyDocumentMapping.ArchiveContainer,
                    MaconomyDocumentMapping.ArchiveNumberField,
                    archive,
                    new[] { MaconomyDocumentMapping.ArchiveNumberField, MaconomyDocumentMapping.ArchiveDescriptionField },
                    new Dictionary<string, string?>
                    {
                        [MaconomyDocumentMapping.ArchiveNumberField] = archive,
                        [MaconomyDocumentMapping.ArchiveDescriptionField] = archive,
                    },
                    new[] { MaconomyDocumentMapping.DocumentNameField, MaconomyDocumentMapping.DocumentKeyField },
                    fileDropUri),
                ct),
            cancellationToken);

    /// <summary>
    /// Where the document landed: the line carrying the name sent, else the one line that was not there
    /// before — Maconomy may name a document its own way — else just the name sent.
    /// </summary>
    private static MaconomyStoredDocument Stored(
        string archive, string sentName,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> before, IReadOnlyList<IReadOnlyDictionary<string, string?>> after)
    {
        var known = new HashSet<string>(before.Select(NameOf), StringComparer.OrdinalIgnoreCase);
        var fresh = after.Where(l => !known.Contains(NameOf(l))).ToList();
        var line = fresh.FirstOrDefault(l => string.Equals(NameOf(l), sentName, StringComparison.OrdinalIgnoreCase))
            ?? (fresh.Count == 1 ? fresh[0] : null);
        return line is null
            ? new MaconomyStoredDocument(archive, sentName, null)
            : new MaconomyStoredDocument(archive, NameOf(line), Clean(line, MaconomyDocumentMapping.DocumentKeyField));
    }

    private async Task<IReadOnlyDictionary<string, string?>?> ReadOneAsync(
        MaconomyConnection connection, MaconomyEndpoint endpoint, string container, string keyField, string keyValue,
        IReadOnlyList<string> fields, CancellationToken cancellationToken)
    {
        var result = await _session.InvokeAsync(
            connection,
            (token, ct) => _gateway.FilterAsync(
                endpoint, token, container,
                new MaconomyFilterRequest($"{keyField} = '{MaconomyCustomerService.Sanitize(keyValue)}'", fields, 2), ct),
            cancellationToken);
        return result.Records.FirstOrDefault(r => string.Equals(Clean(r, keyField), keyValue, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The documents an archive holds: their names and keys. None for an archive Maconomy does not have.</summary>
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> LinesAsync(
        MaconomyConnection connection, MaconomyEndpoint endpoint, string archive, CancellationToken cancellationToken)
    {
        var result = await _session.InvokeAsync(
            connection,
            (token, ct) => _gateway.FilterAsync(
                endpoint, token, MaconomyDocumentMapping.ArchiveLinesContainer,
                new MaconomyFilterRequest(
                    $"{MaconomyDocumentMapping.ArchiveNumberField} = '{MaconomyCustomerService.Sanitize(archive)}'",
                    new[] { MaconomyDocumentMapping.ArchiveNumberField, MaconomyDocumentMapping.DocumentNameField, MaconomyDocumentMapping.DocumentKeyField },
                    LinesMax),
                ct),
            cancellationToken);
        return result.Records;
    }

    // After the upload the read is a nicety — it finds the document's key — so its failure is not the upload's.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, string?>>> LinesQuietlyAsync(
        MaconomyConnection connection, MaconomyEndpoint endpoint, string archive, CancellationToken cancellationToken)
    {
        try
        {
            return await LinesAsync(connection, endpoint, archive, cancellationToken);
        }
        catch (MaconomyException)
        {
            return Empty;
        }
    }

    private static string NameOf(IReadOnlyDictionary<string, string?> line)
        => Clean(line, MaconomyDocumentMapping.DocumentNameField) ?? string.Empty;

    private static string? Clean(IReadOnlyDictionary<string, string?> record, string field)
        => record.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
