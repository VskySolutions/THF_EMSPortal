using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmsPortal.Api.Models.Rems;

// ---------------------------------------------------------------------------------------------------
// WO-113 — REMS public client EMS form.

/// <summary>Versioned REMS onboarding form payload (V1).</summary>
public sealed class RemsFormPayloadV1
{
    /// <summary>Payload schema version; pinned so future shapes can be migrated.</summary>
    public int Version { get; set; } = 1;

    // ---- Common ----

    /// <summary>The client's name as one string.</summary>
    public string? ClientName { get; set; }

    /// <summary>
    /// The generational particle on an individual client's name — Jr., Sr., II, III, IV. Held beside
    /// the family name rather than typed into.
    /// </summary>
    public string? ClientSuffix { get; set; }

    /// <summary>
    /// The courtesy title an individual client was once asked for — Mr., Mrs., Ms., Dr. RETIRED from
    /// the form.
    /// </summary>
    public string? ClientPrefix { get; set; }

    /// <summary>An individual client's given name.</summary>
    public string? ClientFirstName { get; set; }

    /// <summary>An individual client's family name.</summary>
    public string? ClientLastName { get; set; }

    /// <summary>Echo of the client email.</summary>
    public string? Email { get; set; }

    public string? MobileNumber { get; set; }
    public string? ReferralSource { get; set; }

    /// <summary>Free-text follow-up for the chosen referral source, e.g. who referred them.</summary>
    public string? ReferralSourceDetail { get; set; }

    // ---- Address (main entity) ----
    public RemsAddressPayload? PhysicalAddress { get; set; }
    public RemsAddressPayload? MailingAddress { get; set; }

    /// <summary>The client said their post reaches them at the physical address.</summary>
    public bool MailingSameAsPhysical { get; set; }

    // ---- Billing ----

    /// <summary>
    /// Where invoices should be sent, and who each one is addressed to — a LIST, because a client
    /// with two places to invoice has two.
    /// </summary>
    public List<RemsAddressPayload> BillingAddresses { get; set; } = new();

    /// <summary>The single billing address the form used to ask for.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RemsAddressPayload? BillingAddress { get; set; }

    /// <summary>The name and email of the one billing contact the form used to ask for in two plain boxes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BillingContactName { get; set; }

    /// <inheritdoc cref="BillingContactName"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BillingEmail { get; set; }

    /// <summary>
    /// Everyone else the invoice should reach, as CONTACTS. RETIRED with the Billing Contact block
    /// that asked for them: the addressee travels on the billing address now.
    /// </summary>
    public List<RemsRolePayload> AdditionalBillingContacts { get; set; } = new();

    // ---- Individual ----

    /// <summary>
    /// The other people on this client's return — a spouse, a child, anyone else the firm is
    /// preparing for.
    /// </summary>
    public List<RemsAdditionalIndividualPayload> AdditionalIndividuals { get; set; } = new();

    /// <summary>The one spouse the form used to ask for in three plain boxes.</summary>
    public string? SpouseName { get; set; }

    /// <inheritdoc cref="SpouseName"/>
    public string? SpousePhone { get; set; }

    /// <inheritdoc cref="SpouseName"/>
    public string? SpouseEmail { get; set; }

    // ---- Business ----
    public string? Ein { get; set; }

    // ---- Government (contract details) ----
    public DateOnly? ContractStartDate { get; set; }
    public DateOnly? ContractEndDate { get; set; }
    public string? OriginalTerm { get; set; }
    public string? RenewalTerms { get; set; }
    public DateOnly? PoStartDate { get; set; }
    public DateOnly? PoEndDate { get; set; }

    /// <summary>
    /// The role contacts collected for the main entity; which are required depends on the industry
    /// group.
    /// </summary>
    public RemsRolesPayload? Roles { get; set; }

    /// <summary>Related / subsidiary entities captured alongside the main entity.</summary>
    public List<RemsRelatedEntityPayload> RelatedEntities { get; set; } = new();

    /// <summary>
    /// The client's name as it should read: the two boxes joined SURNAME FIRST where an individual
    /// gave them — "Smith John" — and the single box otherwise.
    /// </summary>
    [JsonIgnore]
    public string EffectiveClientName
    {
        get
        {
            var joined = string.Join(
                " ",
                new[] { ClientLastName, ClientFirstName }
                    .Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));
            return joined.Length > 0 ? joined : ClientName?.Trim() ?? string.Empty;
        }
    }

    /// <summary>The roles with the legacy keys folded in — always read the contacts through this.</summary>
    [JsonIgnore]
    public RemsRolesPayload EffectiveRoles => (Roles ?? new RemsRolesPayload()).Normalized();

    /// <summary>
    /// Where the client's post actually goes — the physical address when they said it is the same,
    /// and the mailing node otherwise.
    /// </summary>
    [JsonIgnore]
    public RemsAddressPayload? EffectiveMailingAddress => MailingSameAsPhysical ? PhysicalAddress : MailingAddress;

    /// <summary>
    /// The billing addresses this payload actually carries, with the retired single <see
    /// cref="BillingAddress"/> folded in — always read them through this.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<RemsAddressPayload> EffectiveBillingAddresses
    {
        get
        {
            var live = BillingAddresses.Where(a => a is { HasAnyContent: true }).ToList();
            if (live.Count > 0)
            {
                return live;
            }

            return BillingAddress is { HasAnyContent: true }
                ? new List<RemsAddressPayload> { BillingAddress }
                : Array.Empty<RemsAddressPayload>();
        }
    }
}

/// <summary>A postal address node in <see cref="RemsFormPayloadV1"/>.</summary>
public sealed class RemsAddressPayload
{
    /// <summary>Address line 1.</summary>
    public string? Street { get; set; }

    /// <summary>Address line 2 (optional — the only line of the standard block that never is required).</summary>
    public string? AddressLine2 { get; set; }

    public string? City { get; set; }

    /// <summary>State / province display NAME (e.g. "California"); <see cref="StateCode"/> holds the ISO code.</summary>
    public string? State { get; set; }
    public string? Zip { get; set; }

    /// <summary>ISO-3166-1 alpha-2 country code (e.g. "US").</summary>
    public string? CountryCode { get; set; }

    /// <summary>Country display name resolved from <see cref="CountryCode"/> (e.g. "United States").</summary>
    public string? CountryName { get; set; }

    /// <summary>ISO-3166-2 subdivision code for <see cref="State"/>; null when the country has no state list.</summary>
    public string? StateCode { get; set; }

    // ---- Who the post is addressed to ----
    // The person AT the address, carried on the address itself.

    /// <summary>The generational particle on the addressee's name — Jr., Sr., III.</summary>
    public string? Suffix { get; set; }

    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }

    /// <summary>True when at least one postal line carries content (an all-blank node is treated as absent).</summary>
    [JsonIgnore]
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(Street) || !string.IsNullOrWhiteSpace(AddressLine2)
        || !string.IsNullOrWhiteSpace(City) || !string.IsNullOrWhiteSpace(State)
        || !string.IsNullOrWhiteSpace(Zip);

    /// <summary>True when anything was said about the addressee.</summary>
    [JsonIgnore]
    public bool HasContact =>
        !string.IsNullOrWhiteSpace(FirstName) || !string.IsNullOrWhiteSpace(LastName)
        || !string.IsNullOrWhiteSpace(Email) || !string.IsNullOrWhiteSpace(Phone);

    /// <summary>True when the node carries an address, an addressee, or both.</summary>
    [JsonIgnore]
    public bool HasAnyContent => HasAny || HasContact;
}

/// <summary>A single role contact.</summary>
public sealed class RemsRolePayload
{
    /// <summary>
    /// The title this contact is addressed by — Mr., Mrs., Ms., Dr. Kept out of <see
    /// cref="DisplayName"/> for the same reason it is kept out of the client's.
    /// </summary>
    public string? Prefix { get; set; }

    /// <summary>
    /// The generational particle on this contact's name — Jr., Sr., II, III, IV. Out of <see
    /// cref="DisplayName"/> for exactly the reason the prefix.
    /// </summary>
    public string? Suffix { get; set; }

    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>First and last joined.</summary>
    public string? Name { get; set; }

    public string? Email { get; set; }
    public string? Phone { get; set; }

    /// <summary>
    /// The joined name, from the two boxes when they carry anything and from <see cref="Name"/>
    /// otherwise.
    /// </summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var joined = string.Join(
                " ",
                new[] { FirstName, LastName }.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));
            return joined.Length > 0 ? joined : Name?.Trim() ?? string.Empty;
        }
    }

    /// <summary>
    /// The joined name with its generational particle AFTER it — "Jane Smith Jr.". What a
    /// materialised contact's <c>Person.DisplayName</c> is set.
    /// </summary>
    [JsonIgnore]
    public string NameWithSuffix
    {
        get
        {
            var name = DisplayName;
            if (name.Length == 0)
            {
                return string.Empty;
            }

            var suffix = Suffix?.Trim();
            return string.IsNullOrWhiteSpace(suffix) ? name : $"{name} {suffix}";
        }
    }

    /// <summary>The given name, falling back to the first word of a pre-split <see cref="Name"/>.</summary>
    [JsonIgnore]
    public string EffectiveFirstName =>
        !string.IsNullOrWhiteSpace(FirstName) ? FirstName.Trim() : RemsNameSplit.Split(Name).First;

    /// <summary>The family name, falling back to the rest of a pre-split <see cref="Name"/>.</summary>
    [JsonIgnore]
    public string EffectiveLastName =>
        !string.IsNullOrWhiteSpace(LastName) ? LastName.Trim() : RemsNameSplit.Split(Name).Last;

    /// <summary>True when any field carries content (an all-blank role is treated as absent).</summary>
    [JsonIgnore]
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(FirstName) || !string.IsNullOrWhiteSpace(LastName)
        || !string.IsNullOrWhiteSpace(Name) || !string.IsNullOrWhiteSpace(Email)
        || !string.IsNullOrWhiteSpace(Phone);
}

/// <summary>The role contacts, keyed by the canonical <c>RemsContactRole</c> names.</summary>
public sealed class RemsRolesPayload
{
    // Individual
    public RemsRolePayload? Self { get; set; }
    public RemsRolePayload? Spouse { get; set; }

    // Business
    public RemsRolePayload? PrimaryContact { get; set; }
    public RemsRolePayload? FinancialContact { get; set; }
    public RemsRolePayload? BillingContact { get; set; }

    /// <summary>Anyone else the client wants the firm to have.</summary>
    public RemsRolePayload? OtherContact { get; set; }

    // Government
    public RemsRolePayload? FinanceDirector { get; set; }

    /// <summary>The trustee or personal representative who acts for a trust or an estate.</summary>
    public RemsRolePayload? TrustEstateContact { get; set; }

    // ---- Legacy keys: read, never written ----

    /// <summary>Legacy key for <see cref="PrimaryContact"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RemsRolePayload? Ceo { get; set; }

    /// <summary>Legacy key for <see cref="FinancialContact"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RemsRolePayload? Cfo { get; set; }

    /// <summary>Legacy key for <see cref="BillingContact"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RemsRolePayload? AccountsPayable { get; set; }

    /// <summary>Retired role, no longer asked for.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RemsRolePayload? Banker { get; set; }

    /// <summary>Retired role, no longer asked for.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RemsRolePayload? Lawyer { get; set; }

    /// <summary>
    /// This node with the legacy keys folded into their successors, so everything downstream reads one
    /// shape.
    /// </summary>
    public RemsRolesPayload Normalized() => new()
    {
        Self = Self,
        Spouse = Spouse,
        PrimaryContact = Pick(PrimaryContact, Ceo),
        FinancialContact = Pick(FinancialContact, Cfo),
        BillingContact = Pick(BillingContact, AccountsPayable),
        OtherContact = OtherContact,
        FinanceDirector = FinanceDirector,
        TrustEstateContact = TrustEstateContact,
        Banker = Banker,
        Lawyer = Lawyer,
    };

    private static RemsRolePayload? Pick(RemsRolePayload? current, RemsRolePayload? legacy)
        => current is { HasAny: true } ? current : legacy ?? current;
}

/// <summary>First word is the given name, the rest the family name.</summary>
public static class RemsNameSplit
{
    public static (string First, string Last) Split(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        var space = trimmed.IndexOf(' ');
        return space < 0 ? (trimmed, string.Empty) : (trimmed[..space], trimmed[(space + 1)..].Trim());
    }
}

/// <summary>
/// One of the other people on an individual client's return — a spouse, a child, anyone else the
/// firm is preparing for.
/// </summary>
public sealed class RemsAdditionalIndividualPayload
{
    /// <summary>What they are to the client: <c>spouse</c>, <c>child</c> or <c>other</c>.</summary>
    public const string TypeSpouse = "spouse";

    /// <inheritdoc cref="TypeSpouse"/>
    public const string TypeChild = "child";

    /// <inheritdoc cref="TypeSpouse"/>
    public const string TypeOther = "other";

    /// <summary>How the return is filed: <c>joint</c> or <c>individual</c>.</summary>
    public const string FilingJoint = "joint";

    /// <inheritdoc cref="FilingJoint"/>
    public const string FilingIndividual = "individual";

    /// <summary>Who is invoiced: <c>primary</c> (the client) or <c>separate</c> (somebody named below).</summary>
    public const string BillingPrimary = "primary";

    /// <inheritdoc cref="BillingPrimary"/>
    public const string BillingSeparate = "separate";

    /// <summary>Stable client-supplied key tying this row to its payload node (never trusted as an id).</summary>
    public string? SourceKey { get; set; }

    public string? Type { get; set; }
    public string? FilingType { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>
    /// The generational particle on their name — Jr., Sr., II, III, IV. Asked for in the box after
    /// Last Name, as the client themselves is asked, and for the same reason.
    /// </summary>
    public string? Suffix { get; set; }

    public string? Email { get; set; }
    public string? Phone { get; set; }

    /// <summary>Whether a child is still a minor.</summary>
    public bool? IsMinor { get; set; }

    public string? BillingPreference { get; set; }

    /// <summary>Who to invoice instead of the client.</summary>
    public string? BillingFirstName { get; set; }

    /// <inheritdoc cref="BillingFirstName"/>
    public string? BillingLastName { get; set; }

    /// <summary>True when anything was said about this person (a blank block is treated as absent).</summary>
    [JsonIgnore]
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(Type) || !string.IsNullOrWhiteSpace(FirstName)
        || !string.IsNullOrWhiteSpace(LastName) || !string.IsNullOrWhiteSpace(Email)
        || !string.IsNullOrWhiteSpace(Phone);

    /// <summary>
    /// Their name as it is ADDRESSED — the two parts in the order a name is written, with the
    /// particle after them.
    /// </summary>
    [JsonIgnore]
    public string DisplayName => string.Join(
        " ",
        new[] { FirstName, LastName, Suffix }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    private string TypeCode => Type?.Trim().ToLowerInvariant() ?? string.Empty;

    /// <summary>A child files individually, whatever the payload says.</summary>
    [JsonIgnore]
    public bool FilingLocked => TypeCode == TypeChild;

    /// <summary>
    /// A spouse on a JOINT return, and a minor child, are billed to the primary client whatever the
    /// payload says.
    /// </summary>
    [JsonIgnore]
    public bool BillingLocked =>
        (TypeCode == TypeSpouse && EffectiveFilingType != FilingIndividual) || EffectiveIsMinor == true;

    /// <summary>The filing type after the rules — the only one to store or show.</summary>
    [JsonIgnore]
    public string EffectiveFilingType => FilingLocked
        ? FilingIndividual
        : (string.IsNullOrWhiteSpace(FilingType) ? FilingJoint : FilingType.Trim().ToLowerInvariant());

    /// <summary>The billing preference after the rules — the only one to store or show.</summary>
    [JsonIgnore]
    public string EffectiveBillingPreference => BillingLocked
        ? BillingPrimary
        : (string.IsNullOrWhiteSpace(BillingPreference) ? BillingPrimary : BillingPreference.Trim().ToLowerInvariant());

    /// <summary>True where the form asks who to invoice instead of the client.</summary>
    [JsonIgnore]
    public bool AsksBillingName => !BillingLocked && EffectiveBillingPreference == BillingSeparate;

    /// <summary>The minor answer after the rules: null for anybody who is not a child.</summary>
    [JsonIgnore]
    public bool? EffectiveIsMinor => TypeCode == TypeChild ? IsMinor ?? true : null;
}

/// <summary>A related / subsidiary entity node.</summary>
public sealed class RemsRelatedEntityPayload
{
    /// <summary>Stable client-supplied key that ties this entity to its payload node (never trusted as an id).</summary>
    public string? SourceKey { get; set; }

    /// <summary>Who to speak to about this other business.</summary>
    public string? FullName { get; set; }
    public string? EmailAddress { get; set; }
    public string? PhoneNumber { get; set; }

    // An additional entity is a CONTACT, not a second legal entity: it produces no REMSEntity, no engagement
    // and no approval round of its own.
}

// ---------------------------------------------------------------------------------------------------
// Load state (GET) — the public link resolves to exactly one of these states.

/// <summary>The public form state names returned by the GET load endpoint.</summary>
public static class RemsPublicFormStates
{
    /// <summary>Unknown / bad invite code (generic; no disclosure).</summary>
    public const string Invalid = "Invalid";

    /// <summary>The link is not (or no longer) active: request deleted, form cancelled, or not yet sent.</summary>
    public const string Unavailable = "Unavailable";

    /// <summary>Already submitted — a personalized thank-you with no editable data and no reset path.</summary>
    public const string Submitted = "Submitted";

    /// <summary>Live and editable — carries the industry group, locked prefill and any saved draft.</summary>
    public const string Editable = "Editable";
}

/// <summary>The public form load response.</summary>
public sealed record RemsPublicFormResponse(
    string State,
    string? ClientName = null,
    string? EntityType = null,
    RemsPublicPrefill? Prefill = null,
    RemsFormPayloadV1? DraftPayload = null,
    // The tenant's REMS.ReferralSource list, delivered WITH the form.
    IReadOnlyList<RemsPublicOption>? ReferralSources = null);

/// <summary>One selectable value for a public-form picker.</summary>
public sealed record RemsPublicOption(string Value, string Label, string? Description);

/// <summary>Prefill for the editable form.</summary>
public sealed record RemsPublicPrefill(
    string? ClientName,
    string? ClientFirstName,
    string? ClientLastName,
    string? ClientSuffix,
    string Email,
    string? MobileNumber);

/// <summary>Draft auto-save acknowledgement (WO-113 PUT draft).</summary>
public sealed record RemsDraftSavedResponse(DateTime LastSavedOnUtc);

/// <summary>Client-cancellation acknowledgement (WO-113 POST cancel) — non-destructive; the draft is kept.</summary>
public sealed record RemsPublicCancelResponse(bool Acknowledged);

// ---------------------------------------------------------------------------------------------------
// Review presentation model (POST review) — read-only, grouped EXACTLY as AC-REMS-024.7.

/// <summary>The read-only review presentation model (AC-REMS-024.7).</summary>
public sealed record RemsReviewModel(
    RemsReviewContact Contact,
    RemsReviewContractDetails? ContractDetails,
    IReadOnlyList<RemsReviewOtherEntity> OtherEntities,
    RemsReviewAddressGroup Address,
    IReadOnlyList<RemsReviewContactRow> AdditionalContacts,
    RemsReviewBilling Billing,
    /// <summary>The other people on an individual's return, with the firm's rules already applied.</summary>
    IReadOnlyList<RemsReviewIndividual>? AdditionalIndividuals = null);

/// <summary>One of the other people on an individual's return, as shown on review.</summary>
public sealed record RemsReviewIndividual(
    string? SourceKey,
    string? Type,
    string FilingType,
    string? FirstName,
    string? LastName,
    /// <summary>The generational particle, reported as its own row — it is a box the client filled in.</summary>
    string? Suffix,
    string? Name,
    string? Email,
    string? Phone,
    bool? IsMinor,
    string BillingPreference,
    string? BillingFirstName,
    string? BillingLastName);

/// <summary>The client themselves.</summary>
public sealed record RemsReviewContact(
    string? ClientName,
    string? ClientSuffix,
    string? ClientFirstName,
    string? ClientLastName,
    string Email,
    string? MobileNumber,
    string? ReferralSource);

/// <summary>Government contract details block.</summary>
public sealed record RemsReviewContractDetails(
    DateOnly? ContractStartDate,
    DateOnly? ContractEndDate,
    string? OriginalTerm,
    string? RenewalTerms,
    DateOnly? PurchaseOrderStartDate,
    DateOnly? PurchaseOrderEndDate);

/// <summary>Another business the client named, as shown on review — a contact, not a second entity.</summary>
public sealed record RemsReviewOtherEntity(
    string? SourceKey,
    string? FullName,
    string? EmailAddress,
    string? PhoneNumber);

/// <summary>The main entity's addresses.</summary>
public sealed record RemsReviewAddressGroup(
    RemsAddressPayload? Physical,
    RemsAddressPayload? Mailing,
    IReadOnlyList<RemsAddressPayload> Billing);

/// <summary>A role contact row on review.</summary>
public sealed record RemsReviewContactRow(
    string Role, bool IsRequired, string? Prefix, string? Suffix, string? FirstName, string? LastName,
    string? Name, string? Email, string? Phone);

/// <summary>Billing block.</summary>
public sealed record RemsReviewBilling(
    string? BillingContactName,
    string? BillingEmail,
    IReadOnlyList<RemsReviewContactRow> AdditionalContacts);

/// <summary>
/// Shared JSON options for (de)serializing the stored draft / submission payloads (web defaults:
/// camelCase, case-insensitive).
/// </summary>
public static class RemsFormPayloadJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(RemsFormPayloadV1 payload) => JsonSerializer.Serialize(payload, Options);

    public static RemsFormPayloadV1? TryDeserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RemsFormPayloadV1>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
