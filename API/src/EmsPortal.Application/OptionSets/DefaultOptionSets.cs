using EmsPortal.Domain.Enums;

namespace EmsPortal.Application.OptionSets;

/// <summary>
/// Platform-standard option lists seeded on startup (TenantId = null, IsSystem = true), visible to every
/// tenant. They are the STARTING values, not fixed ones — their items can be managed in the app, and a
/// tenant created via <see cref="TenantOptionSetSeeder"/> gets its own copy. Because the seeded rows are
/// shared, editing one there changes it for every tenant that has no copy of its own.
/// Mirrors the <c>DefaultEmailTemplates</c> definition pattern.
/// </summary>
public static class DefaultOptionSets
{
    // ---- The badge palette, as hex ----
    // These are the values the REMS badges used to carry as hardcoded Quasar colour names, written out
    // once here so a status list arrives already looking the way it always has. They are a STARTING
    // point like every label beside them: a tenant recolours a status in Administration → Option Sets and
    // every badge for it follows, because nothing downstream holds a colour of its own any more.
    private const string OnDark = "#ffffff";
    private const string Grey = "#9e9e9e";
    private const string Teal = "#00897b";
    private const string Purple = "#673ab7";
    private const string PurpleLight = "#9575cd";
    private const string Amber = "#ffa000";
    private const string AmberSoft = "#ffb300";
    private const string Orange = "#f57c00";
    private const string OrangeDeep = "#ef6c00";
    private const string Red = "#C10015";
    private const string Green = "#21BA45";
    private const string Brand = "#1f6478";

    // ---- and the categorical half of it ----
    // A status list is a PROGRESSION and reads as one — grey, then teal, then amber, then green. An entity
    // type is not: Commercial is not further along than Insurance. So the three below exist to give that
    // list six hues a reader can tell apart at a glance rather than a ramp implying an order that is not
    // there. All are dark enough to carry white text, like every colour above them.
    private const string Blue = "#0277bd";
    private const string Slate = "#546e7a";
    private const string Brown = "#6d4c41";

    /// <summary>
    /// <paramref name="Description"/> is what the value MEANS, surfaced as its tooltip wherever the value
    /// is offered or displayed. Worth filling in wherever the labels alone could be mistaken for one
    /// another; left null where the label speaks for itself.
    /// </summary>
    public sealed record ItemDefinition(
        string Value,
        string Label,
        int SortOrder,
        string? MetadataJson = null,
        string? Description = null,
        /// <summary>Badge background, as hex. Seeded so a status list arrives already coloured.</summary>
        string? BackgroundColor = null,
        /// <summary>Badge text colour, as hex.</summary>
        string? TextColor = null,
        /// <summary>Material icon name shown beside the value, e.g. <c>o_support_agent</c>.</summary>
        string? Icon = null,
        /// <summary>
        /// Whether the value is OFFERED. False seeds it hidden: the code stays on the list, records
        /// already recorded against it keep reading correctly, and nothing new can be filed under it —
        /// which is the difference between retiring a value and deleting one. A tenant can put it back
        /// in Administration → Option Sets, so this is a starting position like every other field here.
        /// </summary>
        bool IsActive = true);

    /// <summary>
    /// Two different kinds of protection, because two different things can be true of a list.
    /// <para>
    /// <paramref name="IsClosed"/> — the application branches on the values AND the set of them is fixed.
    /// Nothing may be added, and no seeded value may be deleted, re-coded or hidden. The seven lists that
    /// mirror a C# enum are these: a status the server never writes is a status nothing can reach.
    /// </para>
    /// <para>
    /// <paramref name="LockSeededValues"/> — the application branches on the values it SEEDED, but the
    /// list itself is open. A firm may add a department of its own and hide one it does not use; what it
    /// may not do is delete or re-code <c>audit</c>, because the conditional CAF card, the government
    /// contract block and the approval prerequisites are all written against that exact string.
    /// </para>
    /// Everything a tenant would actually want to change — the label, the description, the colours, the
    /// icon, the order — stays theirs on both.
    /// </summary>
    public sealed record Definition(
        EntityType EntityType,
        string Key,
        string Name,
        OptionItemSortMode ItemSortMode,
        IReadOnlyList<ItemDefinition> Items,
        bool IsClosed = false,
        bool LockSeededValues = false)
    {
        /// <summary>Whether the values seeded into this list are the application's own.</summary>
        public bool SeedsSystemValues => IsClosed || LockSeededValues;
    }

    /// <summary>
    /// The platform-standard option lists to seed. Most of the REMS lists keyed to
    /// <see cref="EntityType.Rems"/> are CODE-valued — the chosen item's <c>Value</c> is what gets stored
    /// on the REMS / REMSForm / REMSEngagement row. Two are not: the grouped marketing-methods list and the
    /// tax-form checklist are referenced by item ID, as foreign keys from
    /// <c>REMSEngagementMarketingMethod</c> and <c>REMSEngagementTaxForm</c>. Each marketing item carries
    /// its group and behaviour flags in <see cref="ItemDefinition.MetadataJson"/>.
    /// </summary>
    public static IReadOnlyList<Definition> All { get; } = new[]
    {
        new Definition(EntityType.Rems, "REMS.Type", "REMS Type", OptionItemSortMode.Custom, new[]
        {
            // Two ways a referral can relate to THF's records, and only two: every new engagement for a
            // client on file is both "new engagement" and "existing client", and a subsidiary of one is
            // an engagement for a client we already have. Splitting those hairs asked the partner a
            // question nobody could answer consistently.
            new ItemDefinition("brand_new_client", "Brand-New Client", 1, Description:
                "The client/company is working with THF for the first time. No prior record exists in the system."),
            new ItemDefinition("existing_client", "New Engagement, Existing Client", 2, Description:
                "The person or company already has an active client record with THF, and this request " +
                "creates an additional engagement under that same client."),
        // The client lookup marks the request by these two codes BY NAME (REMS_TYPE_BRAND_NEW_CLIENT /
        // REMS_TYPE_EXISTING_CLIENT), so neither may be deleted or re-coded. The list itself stays open —
        // a firm that classifies a referral a third way is welcome to say so.
        }, LockSeededValues: true),
        // How the client heard about THF, asked on the public EMS form. The descriptions double as the
        // examples the client needs to place themselves, so they carry the "Friend, Family, or Colleague"
        // detail rather than crowding it into the label.
        new Definition(EntityType.Rems, "REMS.ReferralSource", "REMS Referral Source", OptionItemSortMode.Custom, new[]
        {
            new ItemDefinition("referral", "Referral", 1, Description: "Friend, Family, or Colleague."),
            new ItemDefinition("search_engine", "Search Engine", 2, Description: "Google, Bing, Yahoo."),
            new ItemDefinition("digital_ad_social", "Digital Ad / Social Media", 3, Description:
                "Facebook, Instagram, LinkedIn."),
            new ItemDefinition("event_conference", "Event or Conference", 4, Description:
                "Trade shows, webinars, or local community events."),
            new ItemDefinition("print_broadcast", "Print or Broadcast", 5, Description:
                "Direct mailers, billboards, TV, or radio ads."),
            new ItemDefinition("website_blog", "Website or Blog", 6, Description:
                "Mentioned in an article, forum (e.g., Reddit), or guest post."),
            new ItemDefinition("other", "Other", 7, Description: "Anything not covered above."),
        }),
        new Definition(EntityType.Rems, "REMS.Status", "REMS Status", OptionItemSortMode.Custom, new[]
        {
            // The request lifecycle, in stage order — each value names who the request is waiting on.
            // See RemsRequestStatuses for the transitions.
            //
            // `customer_submitted` reads as "Admin Review": the stage is the Admin reviewing what came
            // back, not staff starting the engagement setup, which happens before any of this. The code
            // keeps its older name because rows already hold it.
            new ItemDefinition("draft", "Draft", 1, Description:
                "With its initiator. Saved but not yet sent to the client.",
                BackgroundColor: Grey, TextColor: OnDark),
            new ItemDefinition("awaiting_customer", "Awaiting Customer", 2, Description:
                "The intake form has been emailed. The ball is with the client.",
                BackgroundColor: Teal, TextColor: OnDark),
            new ItemDefinition("customer_submitted", "Admin Review", 3, Description:
                "The client's answers are in and the named Admin is reviewing them.",
                BackgroundColor: Purple, TextColor: OnDark),
            // NOT a stored status. `customer_submitted` covers both "an admin has this" and "nobody has
            // picked it up yet", and those read very differently to somebody waiting on the request — so
            // the badge says which. The application decides WHEN to show it (RemsRequestStatuses); what
            // it is CALLED, what it explains and what colour it is are the tenant's, like every other
            // value here. Without this row those three would be a hardcoded string in the front end.
            new ItemDefinition("waiting_for_pickup", "Waiting For Pickup", 4, Description:
                "The client's answers are in and the request is with the admins, but no admin has picked "
                + "it up yet. Until one does, its engagement setup is nobody's to work.",
                BackgroundColor: Amber, TextColor: OnDark),
            new ItemDefinition("returned_to_initiator", "Returned to Initiator", 5, Description:
                "The Admin sent the engagement setup back for rework, with a reason. Client intake is read-only.",
                BackgroundColor: OrangeDeep, TextColor: OnDark),
            new ItemDefinition("awaiting_admin_confirmation", "Awaiting Admin Confirmation", 6, Description:
                "The initiator revised the setup and handed it back for the Admin to confirm.",
                BackgroundColor: PurpleLight, TextColor: OnDark),
            new ItemDefinition("pending_approval", "Pending Approval", 7, Description:
                "Routed to the approvers. Every field is read-only while the approval is open.",
                BackgroundColor: Orange, TextColor: OnDark),
            new ItemDefinition("changes_requested", "Changes Requested", 8, Description:
                "Enough approvers declined. Back with the initiator to rework the setup.",
                BackgroundColor: Red, TextColor: OnDark),
            new ItemDefinition("approved", "Approved", 9, Description:
                "Fully approved. Permanently read-only.",
                BackgroundColor: Green, TextColor: OnDark),
        }, IsClosed: true),
        // How far a client's RELATED client has got — the other people on an individual's return, and the
        // other businesses a company named at intake. The Related Entities list draws one badge per row
        // from this and is where it is set.
        //
        // SET BY HAND, always. Nothing in the workflow advances it: raising the follow-up request does
        // not, approving it does not, and neither does the parent request. It is the firm's own note about
        // work that largely happens outside this portal, so the value of it is that whoever is doing that
        // work says where it stands.
        //
        // Which is why this is the one status list here that is NOT closed. Nothing branches on the set of
        // values, so a firm that tracks a fifth position — declined, on hold, not applicable — can add it
        // and every row can be set to it. The four seeded codes are locked against deletion and re-coding
        // all the same: `not_initiated` is the value the server writes for a row nobody has answered for.
        new Definition(EntityType.Rems, "REMS.RelatedEntityStatus", "REMS Related Entity Status", OptionItemSortMode.Custom, new[]
        {
            new ItemDefinition("not_initiated", "Not Initiated", 1, Description:
                "Nothing has been raised for this related client yet. Every row starts here.",
                BackgroundColor: Grey, TextColor: OnDark),
            new ItemDefinition("rems_initiated", "REMS Initiated", 2, Description:
                "A REMS request has been raised for this related client and is being worked.",
                BackgroundColor: Teal, TextColor: OnDark),
            new ItemDefinition("pending_approval", "Pending Approval", 3, Description:
                "Their request has reached the approvers and is waiting on their decisions.",
                BackgroundColor: Amber, TextColor: OnDark),
            new ItemDefinition("approved", "Approved", 4, Description:
                "Their engagement is approved — the end of the road for this row.",
                BackgroundColor: Brand, TextColor: OnDark),
        }, LockSeededValues: true),
        new Definition(EntityType.Rems, "REMS.BillingPeriod", "REMS Billing Period", OptionItemSortMode.Custom, new[]
        {
            // How often the client is billed. Pairs with the engagement's Description of Billing Process,
            // which is where a schedule that does not reduce to a frequency gets written out.
            new ItemDefinition("monthly", "Monthly", 1),
            new ItemDefinition("quarterly", "Quarterly", 2),
            new ItemDefinition("annual", "Annual", 3),
            // Not a frequency at all: the engagement is billed when a piece of work lands, not when the
            // calendar turns. It is offered here because it is the answer to the same question — "when do
            // we invoice?" — and the description beside it is where the milestones themselves are named.
            new ItemDefinition("milestone", "Milestone", 4, Description:
                "Billed as each agreed milestone is reached, rather than on a calendar cycle. " +
                "Set out the milestones in the Description of Billing Process."),
        }),
        // What KIND of entity the client is — an individual, a not-for-profit, an insurer, a commercial
        // business, a government body. An "audit" department for a "government"
        // entity is a Government Audit and additionally requires a contract number + Florida 1%
        // state-fee flag (RemsEngagementCodes.IsGovernmentAudit).
        new Definition(EntityType.Rems, "REMS.EntityType", "REMS Entity Type", OptionItemSortMode.Custom, new[]
        {
            // "Business" was split into the three kinds THF actually onboards. All three ask the client
            // exactly the same questions the old single group did (EIN, CEO/CFO/AP, banker, lawyer) — see
            // RemsFormPayloadValidator.IsBusinessGroup, which is what keeps them one family on the form.
            // COLOURED, because this is a badge now: the Related Entities list shows the entity type on
            // every row, and it is what decides which question the client was asked — the individual's
            // "Spouse & More Individuals", or everybody else's "Other Entities". Six hues rather than a
            // ramp: these are categories, not stages. Like every colour here they are a STARTING point,
            // recolourable in Administration → Option Sets.
            new ItemDefinition("individual", "Individual", 1,
                BackgroundColor: Teal, TextColor: OnDark),
            new ItemDefinition("not_for_profit", "Not-for-Profit", 2,
                BackgroundColor: Purple, TextColor: OnDark),
            new ItemDefinition("insurance", "Insurance", 3,
                BackgroundColor: Brand, TextColor: OnDark),
            new ItemDefinition("commercial", "Commercial", 4,
                BackgroundColor: Blue, TextColor: OnDark),
            new ItemDefinition("government", "Government", 5,
                BackgroundColor: Slate, TextColor: OnDark),
            // A trust or an estate is a legal entity with a name, a tax number and people who act for it,
            // so it is asked exactly what the three business groups are asked (see IsBusinessGroup) —
            // an EIN and the primary / financial / billing contacts. What it is NOT is an individual:
            // filing one as its trustee is what put the trust's affairs under a person's own name.
            new ItemDefinition("trust_estate", "Trust and Estate", 6, Description:
                "A trust or a decedent's estate. Asked the same questions as a business — it has an EIN " +
                "and is acted for by trustees or personal representatives rather than by an individual.",
                BackgroundColor: Brown, TextColor: OnDark),
        // The client's intake form is SHAPED by these codes: individual asks for a spouse, the business
        // family for an EIN and three contacts, government for a finance director and a contract block
        // (RemsFormPayloadValidator). Deleting or re-coding one would leave submitted forms nobody can
        // validate, so the seeded six are locked — while the list stays open to an entity type a firm adds.
        }, LockSeededValues: true),
        // The client's trade. Unlike the entity type — which decides which questions the client's
        // intake form asks and is therefore frozen once that form goes out — this is an internal
        // classification only, so it stays editable for as long as the setup does. One flat list rather
        // than one filtered by the entity type: the two do not partition cleanly (a hospital is Health
        // Care whether it is Commercial or Not-for-Profit), and a tenant adding a trade should not have to
        // say which entity types may see it.
        new Definition(EntityType.Rems, "REMS.Industry", "REMS Industry", OptionItemSortMode.Custom, new[]
        {
            new ItemDefinition("affordable_housing", "Affordable Housing", 1),
            new ItemDefinition("agribusiness", "Agribusiness", 2),
            new ItemDefinition("auto_dealers", "Auto Dealers", 3),
            new ItemDefinition("construction", "Construction", 4),
            new ItemDefinition("entertainment", "Entertainment", 5),
            new ItemDefinition("financial_institutions_banking", "Financial Institutions/Banking", 6),
            new ItemDefinition("hospitality", "Hospitality", 7),
            new ItemDefinition("manufacturing", "Manufacturing", 8),
            new ItemDefinition("professional_service_firms", "Professional Service Firms", 9),
            new ItemDefinition("real_estate", "Real Estate", 10),
            new ItemDefinition("retail", "Retail", 11),
            new ItemDefinition("health_care", "Health Care", 12),
            new ItemDefinition("oil_gas_distribution", "Oil & Gas Distribution", 13),
            new ItemDefinition("wholesale", "Wholesale", 14),
            new ItemDefinition("technology", "Technology", 15),
            new ItemDefinition("state_government", "State Government", 16),
            new ItemDefinition("local_government", "Local Government", 17),
            new ItemDefinition("federal_government", "Federal Government", 18),
            new ItemDefinition("educational_institutions", "Educational Institutions", 19),
            // The four insurance trades, with no "Insurance -" on the front: the Industry list is narrowed
            // by the entity type beside it, which already says Insurance. The VALUES keep the prefix — they
            // are the codes engagements are recorded against.
            new ItemDefinition("insurance_property_casualty", "Property and Casualty", 20),
            new ItemDefinition("insurance_life", "Life", 21),
            new ItemDefinition("insurance_other", "Other", 22),
            new ItemDefinition("trade_associations", "Trade Associations", 23),
            new ItemDefinition("charitable_organizations_foundations", "Charitable Organizations or Foundations", 24),
            new ItemDefinition("other_not_for_profit", "Other Not-for-Profit", 25),
            // Kept alongside the three tiers above it: not every government client is filed as state,
            // local or federal, and the unqualified value is what those are recorded under.
            new ItemDefinition("government", "Government", 26),
            new ItemDefinition("individual", "Individual", 27),
            new ItemDefinition("distribution", "Distribution", 28),
            // Appended rather than slotted in beside the other three Insurance trades at 20-22. The
            // backfill that adds this to each existing tenant's copy takes MAX(DisplayOrder) + 1, so
            // renumbering here would put the item in one place for a new tenant and another for everybody
            // already running. The list is not alphabetical in any case — Health Care sits at 12.
            // "Healthcare", one word — the entity type beside it says Insurance, and this is deliberately
            // not the same string as "Health Care" above, which is the trade a hospital is in whether it
            // is Commercial or Not-for-Profit. The two never meet in a picker (the entity type narrows the
            // list to one or the other), only in the option-set admin.
            new ItemDefinition("insurance_health", "Healthcare", 29),
        }),
        new Definition(EntityType.Rems, "REMSMarketing_MarketingMethods.MarketingMethodId", "REMS Marketing Methods", OptionItemSortMode.Custom, new[]
        {
            // Global — not auto-suggested, not editable.
            new ItemDefinition("all", "All", 1, MarketingMetadata("Global", autoSuggested: false, editable: false)),
            new ItemDefinition("active_clients", "Active Clients", 2, MarketingMetadata("Global", autoSuggested: false, editable: false)),
            // Geography — auto-suggested and editable.
            new ItemDefinition("tallahassee", "Tallahassee", 3, MarketingMetadata("Geography", autoSuggested: true, editable: true)),
            new ItemDefinition("panama_city_bay_county", "Panama City-Bay County", 4, MarketingMetadata("Geography", autoSuggested: true, editable: true)),
            new ItemDefinition("lakeland_dade_city", "Lakeland-Dade City", 5, MarketingMetadata("Geography", autoSuggested: true, editable: true)),
            new ItemDefinition("tampa", "TAMPA", 6, MarketingMetadata("Geography", autoSuggested: true, editable: true)),
            // Service / Education — auto-suggested and editable.
            new ItemDefinition("tax", "Tax", 7, MarketingMetadata("Service/Education", autoSuggested: true, editable: true)),
            new ItemDefinition("audit_nfp", "Audit-NFP", 8, MarketingMetadata("Service/Education", autoSuggested: true, editable: true)),
            new ItemDefinition("audit_insurance", "Audit-Insurance", 9, MarketingMetadata("Service/Education", autoSuggested: true, editable: true)),
            new ItemDefinition("gcs", "GCS", 10, MarketingMetadata("Service/Education", autoSuggested: true, editable: true)),
            new ItemDefinition("cas", "CAS", 11, MarketingMetadata("Service/Education", autoSuggested: true, editable: true)),
            // Event — not auto-suggested, not editable.
            new ItemDefinition("tax_event", "Tax Event", 12, MarketingMetadata("Event", autoSuggested: false, editable: false)),
            new ItemDefinition("finrep_conference", "FINREP Conference", 13, MarketingMetadata("Event", autoSuggested: false, editable: false)),
        }),
        // Engagement department (WO-114). The code drives the conditional engagement detail: an "audit"
        // engagement carries an audit detail (signed CAF), a "tax" engagement a tax detail (fiscal year
        // end + calculated due dates + form checklist). Also the key for the department-to-director map.
        new Definition(EntityType.Rems, "REMS.Department", "REMS Department", OptionItemSortMode.Custom, new[]
        {
            new ItemDefinition("cas", "CAS", 1),
            new ItemDefinition("tax", "Tax", 2),
            // RETIRED, not removed. The firm does not place new engagements in Audit — Assurance below is
            // where attest work goes — but the code is branched on all over the setup (the signed CAF, the
            // government contract block, the approval prerequisites) and engagements are already filed
            // under it. Hidden from the picker leaves every one of those reading correctly while nothing
            // new can be booked into it; deleting it would strand them. A firm that wants it back turns it
            // on in Administration → Option Sets.
            new ItemDefinition("audit", "Audit", 3, IsActive: false),
            new ItemDefinition("gcs", "GCS", 4),
            // Attest work priced for the engagement rather than for its first year. It is asked the signed
            // client-acceptance form the Audit department is, plus the client's fiscal year end and whether
            // administrative fees are charged — see RemsEngagementCodes.IsAssurance.
            new ItemDefinition("assurance", "Assurance", 5),
            // The firm's own internal work, booked as an engagement so it routes through the same setup and
            // approval as client work. Carries no conditional detail: the audit and tax cards key off the
            // "audit" and "tax" codes specifically, so an Admin engagement asks for neither a signed CAF
            // nor a fiscal year end.
            //
            // RETIRED for the same reason and in the same way as Audit above — hidden, not deleted. An
            // engagement is a piece of CLIENT work, and the firm's own internal jobs stopped being booked
            // through this setup; the ones already booked keep their department.
            new ItemDefinition("admin", "Admin", 6, IsActive: false),
        // Everything conditional on the engagement setup keys off these codes by name — the signed CAF on
        // audit and assurance, the fiscal year end on tax, the purchase order on gcs, the billing pair on
        // cas, and the approval prerequisites behind all of them (RemsEngagementCodes). So the seeded six
        // cannot be deleted or re-coded. A department a firm adds is its own, and asks nothing conditional.
        }, LockSeededValues: true),
        // How a GCS engagement is staffed, which with the bill rate beside it is how the work is priced.
        // One level per engagement — a single rate for the whole piece of work, not a rate card.
        new Definition(EntityType.Rems, "REMS.PersonnelLevel", "REMS Personnel Level", OptionItemSortMode.Custom, new[]
        {
            new ItemDefinition("principal", "Principal", 1),
            new ItemDefinition("senior_consultant", "Senior Consultant", 2),
            new ItemDefinition("consultant", "Consultant", 3),
            new ItemDefinition("junior_consultant", "Junior Consultant", 4),
            new ItemDefinition("project_analyst", "Project Analyst", 5),
            new ItemDefinition("program_admin_support", "Program and Administrative Support", 6),
        }),
        // The service actually being sold. A classification field: what the firm is engaged to do, for
        // reporting and for the billing/marketing view. The Internal-* values are the firm's own work,
        // booked as engagements so the same setup and approval route covers them.
        new Definition(EntityType.Rems, "REMS.ServiceLine", "REMS Service Line", OptionItemSortMode.Custom, new[]
        {
            new ItemDefinition("attest_services", "Attest Services", 1),
            new ItemDefinition("tax_compliance", "Tax Compliance", 2),
            new ItemDefinition("client_accounting_services", "Client Accounting Services", 3),
            new ItemDefinition("outsourced_cfo", "Outsourced CFO", 4),
            new ItemDefinition("consulting", "Consulting", 5),
            new ItemDefinition("business_valuation", "Business Valuation", 6),
            new ItemDefinition("it_services", "IT Services", 7),
            new ItemDefinition("plan_administration", "Plan Administration", 8),
            new ItemDefinition("mergers_acquisitions", "Mergers & Acquisitions", 9),
            new ItemDefinition("payroll_services", "Payroll Services", 10),
            new ItemDefinition("peer_review", "Peer Review", 11),
            new ItemDefinition("soc", "SOC", 12, Description:
                "System and Organization Controls reporting (SOC 1 / SOC 2)."),
            new ItemDefinition("employee_benefits", "Employee Benefits", 13),
            new ItemDefinition("estate_planning", "Estate Planning", 14),
            new ItemDefinition("litigation_support", "Litigation Support", 15),
            new ItemDefinition("forensic_accounting", "Forensic Accounting", 16),
            new ItemDefinition("internal_accounting", "Internal-Accounting", 17),
            new ItemDefinition("internal_billing", "Internal-Billing", 18),
            new ItemDefinition("internal_operations", "Internal-Operations", 19),
            new ItemDefinition("internal_marketing", "Internal-Marketing", 20),
            new ItemDefinition("internal_it", "Internal-IT", 21),
            new ItemDefinition("internal_miscellaneous", "Internal-Miscellaneous", 22),
        }),
        // Engagement tax forms (WO-114): the checklist values referenced by foreign key from
        // REMSEngagementTaxForm.TaxFormId on a tax engagement's tax detail.
        //
        // Ordered as a preparer reads a return shelf rather than alphabetically or by popularity: the
        // 1040 family, then the 1041s, then the entity returns in form-number order, then the exempt and
        // plan returns, and finally the four that are not a federal form at all. A checklist is scanned,
        // not searched, and scanning it is far quicker when the numbers run in order.
        //
        // Every label is "form number — who files it", because the number alone is the part staff know
        // and the gloss is the part that settles which of two similar numbers is meant — 1041 (Trust) and
        // 1041 (Estate) are the same IRS form filed by two different kinds of client, and the whole
        // reason they are two rows here is that THF tracks them apart.
        new Definition(EntityType.Rems, "REMS.TaxForm", "REMS Tax Form", OptionItemSortMode.Custom, new[]
        {
            new ItemDefinition("1040", "1040 — Individual", 1),
            new ItemDefinition("1040_es", "1040-ES — Estimated Tax", 2),
            // One IRS form, two codes. A trust and a decedent's estate file the same 1041 but are
            // different engagements with different deadlines and different people to chase, so the
            // checklist says which — and the codes have to differ because each is its own row.
            new ItemDefinition("1041_trust", "1041 — Trust", 3),
            new ItemDefinition("1041_estate", "1041 — Estate", 4),
            new ItemDefinition("1065", "1065 — Partnership", 5),
            new ItemDefinition("1120", "1120 — C Corporation", 6),
            new ItemDefinition("1120_pc", "1120-PC — Property & Casualty Insurance", 7),
            new ItemDefinition("1120_pol", "1120-POL — Political Organization", 8),
            new ItemDefinition("1120_s", "1120-S — S Corporation", 9),
            new ItemDefinition("990", "990 — Tax-Exempt", 10),
            new ItemDefinition("990_t", "990-T — Exempt Organization Business Income", 11),
            new ItemDefinition("5500", "5500 — Employee Benefit Plan", 12),
            // The last four are not federal income-tax forms. They are on the same checklist because
            // they are the same question — what does this engagement actually file? — and a tax
            // engagement that files a tangible-property return and a payroll return had nowhere to say
            // so.
            new ItemDefinition("tpp", "TPP — Tangible Personal Property", 13),
            new ItemDefinition("payroll", "Payroll", 14),
            // Deliberately last, and deliberately vague: "Other" is the row that keeps a return nobody
            // listed from being left off the checklist altogether, and "States" covers whichever state
            // returns this engagement files.
            new ItemDefinition("other", "Other", 15),
            new ItemDefinition("states", "States", 16),
        }),

        // ---------------------------------------------------------------------------------------------
        // The seven lists below mirror C# enums the workflow BRANCHES on — RemsFormStatus,
        // RemsApproverRole, RemsApprovalTaskStatus, RemsApprovalRoundStatus, RemsEngagementStatus,
        // RemsFormEmailEventType, and the submission state derived from the first.
        //
        // They are seeded as CLOSED lists: the codes are the enum's and cannot be added to, deleted or
        // renamed, because the server writes them and reads them back. What a firm actually wants to
        // change is open — the wording on the badge, the sentence behind its tooltip, the colour, the
        // icon, the order. Until now all of that lived in a hardcoded map in the front end, which meant
        // a firm that calls a Shareholder a Principal had no way to say so.
        // ---------------------------------------------------------------------------------------------

        // RemsFormStatus. Worded from the FIRM's side: a form that has come back reads "Received", not
        // "Submitted" — submitting is the client's act and it is over; what staff want to know is whether
        // the answers are in hand.
        //
        // Draft and Saved are STORED values that the dashboard never shows: both mean the client has not
        // been written to, so RemsWorkspaceMapper.FormState folds them into Not started. They stay on the
        // list because REMSForm.Status still holds them and they are what the send guard reads.
        new Definition(EntityType.Rems, "REMS.FormStatus", "REMS Form Status", OptionItemSortMode.Custom, new[]
        {
            new ItemDefinition("NotStarted", "Not started", 1, Description:
                "The intake form has not gone out to the client yet — whether or not staff have prepared it.",
                BackgroundColor: Grey, TextColor: OnDark),
            new ItemDefinition("Draft", "Draft", 2, Description:
                "The intake form exists but has not been sent to the client.",
                BackgroundColor: Grey, TextColor: OnDark),
            new ItemDefinition("Saved", "Saved", 3, Description:
                "The intake form has been prepared and saved, ready to send.",
                BackgroundColor: Brand, TextColor: OnDark),
            new ItemDefinition("Sent", "Sent", 4, Description:
                "The intake form has been emailed to the client and is waiting on them.",
                BackgroundColor: Teal, TextColor: OnDark),
            new ItemDefinition("Submitted", "Received", 5, Description:
                "The client filled the intake form in and sent it back.",
                BackgroundColor: Green, TextColor: OnDark),
            new ItemDefinition("Cancelled", "Cancelled", 6, Description:
                "The intake form was cancelled — the client cannot fill it in.",
                BackgroundColor: Red, TextColor: OnDark),
        }, IsClosed: true),

        // Whether the client's answers are in hand. Derived from the form status rather than stored, but
        // rendered as a value of its own on the request lists — so it is a list of its own.
        new Definition(EntityType.Rems, "REMS.ClientSubmissionState", "REMS Client Submission", OptionItemSortMode.Custom, new[]
        {
            new ItemDefinition("AwaitingCustomer", "Awaiting customer", 1, Description:
                "The intake form is out with the client and they have not answered yet.",
                BackgroundColor: Teal, TextColor: OnDark),
            new ItemDefinition("Submitted", "Received", 2, Description:
                "The client's answers are in hand.",
                BackgroundColor: Green, TextColor: OnDark),
        }, IsClosed: true),

        // RemsApproverRole — what puts somebody on an engagement's approver list. The icon is part of the
        // value here: the approver list and the inbox are read down their left edge.
        new Definition(EntityType.Rems, "REMS.ApproverRole", "REMS Approver Role", OptionItemSortMode.Custom, new[]
        {
            new ItemDefinition("Shareholder", "Shareholder", 1, Description:
                "A holder of the Shareholder role — on every engagement's list by standing, and not removable.",
                BackgroundColor: Brand, TextColor: OnDark, Icon: "o_workspace_premium"),
            new ItemDefinition("DepartmentDirector", "Department Director", 2, Description:
                "The head of the department the engagement was placed in.",
                BackgroundColor: Brand, TextColor: OnDark, Icon: "o_account_tree"),
            new ItemDefinition("CSE", "CSE", 3, Description:
                "The Client Service Executive who owns the client relationship.",
                BackgroundColor: Brand, TextColor: OnDark, Icon: "o_support_agent"),
            new ItemDefinition("CommissionRecipient", "Commission Recipient", 4, Description:
                "Named for a share of the commission on this engagement, which is why they are asked to accept it.",
                BackgroundColor: Brand, TextColor: OnDark, Icon: "o_payments"),
            new ItemDefinition("Approver", "Approver", 5, Description:
                "Added to the round by hand, with no other standing on the engagement.",
                BackgroundColor: Brand, TextColor: OnDark, Icon: "o_how_to_reg"),
        }, IsClosed: true),

        // RemsApprovalTaskStatus — ONE approver's decision, which is not the request's outcome. The
        // description on Approved says so, because that is the confusion this list exists to end.
        new Definition(EntityType.Rems, "REMS.ApprovalStatus", "REMS Approval Decision", OptionItemSortMode.Custom, new[]
        {
            new ItemDefinition("Pending", "Pending", 1, Description:
                "Waiting on this approver's decision.",
                BackgroundColor: Orange, TextColor: OnDark),
            new ItemDefinition("Approved", "Approved", 2, Description:
                "This approver signed off. It does not mean the request is approved — the round is "
                + "approved only once every approver has.",
                BackgroundColor: Green, TextColor: OnDark),
            new ItemDefinition("Rejected", "Rejected", 3, Description:
                "This approver declined, with a reason. A decline ends the round and sends the setup back for rework.",
                BackgroundColor: Red, TextColor: OnDark),
            new ItemDefinition("Superseded", "No longer required", 4, Description:
                "The round closed on somebody else's decline before this approver decided, so no decision "
                + "is needed from them.",
                BackgroundColor: Grey, TextColor: OnDark),
            // STATIC-APPROVAL-POLICY: a later stage of a staged round.
            new ItemDefinition("Waiting", "Waiting", 5, Description:
                "Not asked yet. Their turn comes once the stage before them has approved.",
                BackgroundColor: Grey, TextColor: OnDark),
        }, IsClosed: true),

        // RemsApprovalRoundStatus — where the whole ROUND stands. `partially_approved` is the one value
        // the enum does not have: a round is Pending from the moment it is sent until the last signature,
        // which cannot tell "nobody has looked at this" from "everybody but you has signed". The
        // application decides when to show it; its wording, colour and explanation live here like the rest.
        new Definition(EntityType.Rems, "REMS.ApprovalRoundStatus", "REMS Approval Status", OptionItemSortMode.Custom, new[]
        {
            new ItemDefinition("Pending", "Pending", 1, Description:
                "Routed to the approvers. Nobody has signed yet.",
                BackgroundColor: Orange, TextColor: OnDark),
            new ItemDefinition("partially_approved", "Partially Approved", 2, Description:
                "Some of the approvers have signed. The request is approved only once all of them have.",
                BackgroundColor: AmberSoft, TextColor: OnDark),
            new ItemDefinition("Approved", "Approved", 3, Description:
                "Every approver signed. The engagement is approved.",
                BackgroundColor: Green, TextColor: OnDark),
            new ItemDefinition("Rejected", "Declined", 4, Description:
                "An approver declined. The round is closed and the setup went back for rework.",
                BackgroundColor: Red, TextColor: OnDark),
        }, IsClosed: true),

        // RemsEngagementStatus — the engagement's own lifecycle, shown beside the request's.
        new Definition(EntityType.Rems, "REMS.EngagementStatus", "REMS Engagement Status", OptionItemSortMode.Custom, new[]
        {
            new ItemDefinition("Draft", "Draft", 1, Description:
                "Being set up. It has not been routed to the approvers.",
                BackgroundColor: Grey, TextColor: OnDark),
            new ItemDefinition("PendingApproval", "Pending Approval", 2, Description:
                "With its approvers. It becomes Approved only once every one of them has signed.",
                BackgroundColor: Orange, TextColor: OnDark),
            new ItemDefinition("Rejected", "Rejected", 3, Description:
                "An approver declined. The setup is back with staff to rework and resubmit.",
                BackgroundColor: Red, TextColor: OnDark),
            new ItemDefinition("Approved", "Approved", 4, Description:
                "Every approver signed. The engagement is approved and permanently read-only.",
                BackgroundColor: Green, TextColor: OnDark),
        }, IsClosed: true),

        // RemsFormEmailEventType — what the provider reported about the client's intake email. These are
        // never synthesised: the log shows exactly what came back.
        new Definition(EntityType.Rems, "REMS.EmailEvent", "REMS Email Event", OptionItemSortMode.Custom, new[]
        {
            new ItemDefinition("Sent", "Sent", 1, Description:
                "The intake form was emailed to the client.",
                BackgroundColor: Teal, TextColor: OnDark, Icon: "o_send"),
            new ItemDefinition("Reminder", "Reminder sent", 2, Description:
                "The client was chased about a form already sent to them.",
                BackgroundColor: Amber, TextColor: OnDark, Icon: "o_notifications_active"),
            new ItemDefinition("Delivered", "Delivered", 3, Description:
                "The provider confirmed the message reached the client's mail server.",
                BackgroundColor: Green, TextColor: OnDark, Icon: "o_mark_email_read"),
            new ItemDefinition("Opened", "Opened", 4, Description:
                "The provider reported the client opening the message.",
                BackgroundColor: Brand, TextColor: OnDark, Icon: "o_drafts"),
            new ItemDefinition("Failed", "Failed", 5, Description:
                "The message could not be delivered. The reason is on the row.",
                BackgroundColor: Red, TextColor: OnDark, Icon: "o_error"),
        }, IsClosed: true),
    };

    /// <summary>Builds the <c>MetadataJson</c> for a REMS marketing-method item: its group and behaviour flags.</summary>
    private static string MarketingMetadata(string group, bool autoSuggested, bool editable)
        => $"{{\"group\":\"{group}\",\"autoSuggested\":{(autoSuggested ? "true" : "false")},\"editable\":{(editable ? "true" : "false")}}}";
}
