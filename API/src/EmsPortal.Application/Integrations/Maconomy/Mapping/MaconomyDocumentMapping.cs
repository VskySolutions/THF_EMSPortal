using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.Integrations.Maconomy.Mapping;

/// <summary>Which Maconomy record a request's file is filed under.</summary>
public enum MaconomyDocumentTarget
{
    /// <summary>The client's customer: the request's attachments.</summary>
    Customer,

    /// <summary>The engagement's job: the signed client acceptance form and the purchase order.</summary>
    Job,
}

/// <summary>
/// THE mapping of a request's files to Maconomy's documents: which file goes under which record, and
/// every Maconomy name the upload uses. In Maconomy a document is a line of a document archive, and a
/// customer or a job points at its archive ("Customer 900004", "Job 10234") by number.
/// <para>
/// The names are the instance's own, read from its specifications on 12 Nov 2026. The customer route is
/// the card's Attach Document action. No job container offers one, so a job's file is added to the job's
/// archive directly and the job pointed at it. Neither route has been run against the instance yet.
/// </para>
/// </summary>
public static class MaconomyDocumentMapping
{
    // ---- Customer: the card's own action files the document, making the archive the first time ----
    public const string CustomerNumberField = "customernumber";
    public const string AttachAction = "attachdocument";

    // ---- Job: the document goes into the job's archive, and the job is pointed at the archive ----
    public const string JobContainer = "jobs";
    public const string JobNumberField = "jobnumber";

    // ---- Document archives ----
    public const string ArchiveContainer = "documentarchives";
    public const string ArchiveLinesContainer = "documentarchivelines";
    public const string ArchiveNumberField = "documentarchivenumber";
    public const string ArchiveDescriptionField = "description";
    public const string DocumentNameField = "documentname";
    public const string DocumentKeyField = "instancekey";

    // Maconomy's text length is 255; a name is kept well inside it so a numbering suffix always fits.
    private const int NameMax = 200;

    /// <summary>Attachments are the client's; the acceptance form and the purchase order are the engagement's.</summary>
    public static MaconomyDocumentTarget TargetOf(RemsFileKind kind)
        => kind == RemsFileKind.Attachment ? MaconomyDocumentTarget.Customer : MaconomyDocumentTarget.Job;

    /// <summary>The number Maconomy itself gives a job's archive, used when the portal has to make it.</summary>
    public static string JobArchiveNumber(string jobNumber) => $"Job {jobNumber.Trim()}";

    /// <summary>The number Maconomy gives a customer's archive: where a document went when the reply does not say.</summary>
    public static string CustomerArchiveNumber(string customerNumber) => $"Customer {customerNumber.Trim()}";

    /// <summary>
    /// The name a file is filed under: its own, in plain characters (the name travels in a header), and
    /// numbered when the archive already holds one of that name — the name is half of the document's key.
    /// </summary>
    public static string DocumentNameOf(string? fileName, IEnumerable<string> taken)
    {
        var name = new string((fileName ?? string.Empty).Trim().Select(c => c is < ' ' or > '~' or '"' or '\\' or '/' ? '_' : c).ToArray());
        if (name.Length == 0)
        {
            name = "document";
        }
        var dot = name.LastIndexOf('.');
        var stem = dot > 0 ? name[..dot] : name;
        var extension = dot > 0 ? name[dot..] : string.Empty;
        if (stem.Length + extension.Length > NameMax)
        {
            stem = stem[..Math.Max(1, NameMax - extension.Length)];
        }
        var used = new HashSet<string>(taken.Select(t => t.Trim()), StringComparer.OrdinalIgnoreCase);
        var candidate = stem + extension;
        for (var n = 2; used.Contains(candidate); n++)
        {
            candidate = $"{stem} ({n}){extension}";
        }
        return candidate;
    }
}
