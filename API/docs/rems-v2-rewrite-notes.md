# REMS data model v2 — rewrite notes for the controller work

Branch `feat/rems-data-model-v2`, catalog `THF_EMSPortal_New`. The database already holds the new model with
the dev data migrated (`API/scripts/rems-v2-migrate.sql`). The legacy tables and the old entity classes are gone (migration `RemsLegacyModelRemoved`); this note
is kept as the map from the old model to the new one. **The API contract is frozen**: every route, every request/response record in
`Models/Rems/*.cs` and every status code stays exactly as it is. Only what sits beneath them changes.

## The model, old → new

| Old | New | Notes |
|---|---|---|
| `REMS` | `RemsRequest` (table `REMS`) | Same ids. `RemsNumber`, `TypeId`, `StatusId` (the only workflow status), `ClientId` (never null), `CSEId`, `AdminAssignedToId`, `OnBehalfOfUserId`. Carries the invite, setup and government columns below. `Description` and `ExistingClientReferenceId` are gone. |
| `REMSForm` + `REMSFormDraft` + `REMSFormSubmission` | columns on `RemsRequest` | `InviteCode`, `InviteRecipientEmail`, `InviteSentByUserId`, `InviteSentOnUtc`, `FormLastSavedOnUtc`, `FormSubmittedOnUtc`. The form's status is derived by `RemsDerived.FormStatusOf(request)`: null → Saved (has a code, not sent) → Sent → Submitted. No JSON anywhere: the form writes `Client_*` rows through `RemsClientIntake`. |
| `REMSEngagement` | columns on `RemsRequest` | `DepartmentId`, `ServiceLineId`, `JobTemplateId`, `BillingPeriodId`, `DepartmentDirectorId`, `EngagementExecutiveId`, `BillingManagerId`, `FirstYearFeeEstimate`, `EngagementFee`, `RealizationPercentage`, `BillingProcessDescription`. Engagement status is derived: `RemsDerived.EngagementStatusOf(request)`. **Every route that took an engagement id now takes the REMS id**, and every DTO that carries an engagement id carries the REMS id. `IndustryId` moved to `Client`. |
| `REMSEngagementAuditDetail` | `RemsFile` kind `ClientAcceptanceForm` + `Client.FiscalYearEnd` | |
| `REMSEngagementGovernmentDetail` | `RemsRequest.ContractId → ClientContract` (dates/terms/PO period, what the client declared) + `ContractNumber`, `PurchaseOrderNumber`, `PurchaseOrderAmount`, `IsFloridaOnePercentFeeApplicable` on the request + `RemsFile` kind `PurchaseOrder` | |
| `REMSEngagementTaxDetail` | `Client.FiscalYearEnd` + `RemsTaxDueDate` rows (Kind Original/FirstExtension, DueOn, IsOverridden, OverriddenByUserId) + `RemsTaxForm` | `RemsWorkspaceMapper.TaxDetail` rebuilds the old view incl. the schedule JSON string. |
| `REMSEngagementApprover` / `CommissionSplit` / `MarketingMethod` / `PersonnelRate` / `TaxForm` | `RemsApprover`, `RemsCommissionSplit`, `RemsMarketingMethod`, `RemsPersonnelRate`, `RemsTaxForm` | All keyed by `REMSId`. |
| `REMSApprovalRound` | gone | `RemsApprovalTask.RoundNumber`. `RemsApprovalRounds.Summarize(tasks, declineThreshold)` gives round summaries (status, sent on/by, completed, rejection reason, tasks). A "round id" in a DTO is `summary.Id` (the round's first task id). |
| `REMSApprovalTask` / `REMSApprovalChecklistItem` | `RemsApprovalTask` (`REMSId`, `RoundNumber`) / `RemsApprovalChecklistItem` (`TaskId`) | Same ids. |
| `REMSFiles` | `RemsFile` with `Kind` (Attachment, ClientAcceptanceForm, PurchaseOrder) | Same ids. |
| `REMSSendBack` | `RemsStatusHistory` row to `returned_to_initiator` with `ReturnedToUserId` and `Reason` | Open while it is the latest history row: `IRemsRequestRepository.GetOpenSendBackAsync`. |
| `REMSFormEmailEvent` | `RemsEmailEvent` (`REMSId`, `EmailTemplateId`, Subject, Body kept; no provider payload) | |
| `REMSClient` + `REMSEntity` + client half of `Person` | `Client` (`ClientNumber`, `Status` Prospect/Active/Inactive, `PersonId` for individuals, `LegalName` for organisations, `EntityTypeId`, `IndustryId`, `EIN`, `FiscalYearEnd`, `PrimaryEmail`, `PrimaryPhone`, `ReferralSourceId`, `ReferralSourceDetail`, Maconomy number + `MaconomySyncStatus`/`MaconomySyncedOnUtc`/`MaconomySyncError`) | The old "main entity" is the client itself: `RemsWorkspaceMapper.Entities(client)` returns one `RemsEntityView` whose id is the client id. **Routes that took an entity id now take the client id.** `Client.ClientName/ClientEmail/ClientPhone/IsMaconomyClient/Maconomy` mirror what `Person` offered; hydrate with `IMaconomyClientDirectory.HydrateAsync(client)`. |
| `REMSEntityAddress` | `ClientAddress` (`AddressId → Address`, `AddressTypeId → REMS.AddressType` option) | Codes `physical/mailing/billing`; the contract still says `Physical/Mailing/Billing`: `RemsAddressTypes.NameOf/CodeOf`. |
| `REMSEntityContact` | `ClientContact` (`PersonId → Person`, `ContactRoleId → REMS.ContactRole` option, `IsPrimary`) | Codes are snake_case; the contract still says `PrimaryClientContact` etc.: `RemsContactRoles.NameOf/CodeOf`. `IsRequired` is no longer stored: `RemsContactRoles.IsRequired(entityTypeCode, role)`. |
| `REMSAdditionalEntity` / `REMSAdditionalIndividual` | `ClientRelationship` (`ClientId`, `RelatedClientId → Client`, `RelationTypeId`, `FilingTypeId`, `BillingPreferenceId`, `IsMinor`, `RelatedStatusId`) | A related party is a `Client` row (Prospect). Kind: individual when `RelatedClient.PersonId != null`. `CreatedREMSId` is answered by the related client's own requests. `RelatedStatusId` is still set by hand. `IRemsRequestRepository.ListRelatedClientsAsync` returns the same `RemsRelatedClientItem` rows, `Id` = relationship id. |
| `REMSMaconomyCustomerSync` | three columns on `Client` | `IMaconomyCustomerSyncService` now works in `Client`; `RemsMaconomySyncViews.For(request, …)` / `From(client, …)`. |
| `RemsSettings` | same class, table `REMS_Settings` + `DeclineThreshold`, `TaxFeeCeilingWithoutShareholder`, `IsStaticRouting`, `ChecklistTemplates` | `RemsDepartmentDirector` unchanged (table renamed). |
| `REMSDelegation` | same class, table `REMS_Delegation`, columns `IsPrepareAllowed`/`IsSendAllowed` (C# props still `CanPrepare`/`CanSend`) | |

New classes live in `EmsPortal.Domain/Entities/Rems/`. DbSets on `EmsPortalDbContext`: `Clients`, `ClientAddresses`, `ClientContacts`,
`ClientRelationships`, `ClientContracts`, `RemsRequests`, `RemsFiles`, `RemsEmailEvents`, `RemsStatusHistory`, `RemsApprovers`,
`RemsCommissionSplits`, `RemsApprovalTasks`, `RemsApprovalChecklistItems`, `RemsPersonnelRates`, `RemsMarketingMethods`,
`RemsTaxForms`, `RemsTaxDueDates`, `RemsChecklistTemplates`. Controllers do not use the DbContext; they use the repositories.

## Repositories (Application/Abstractions/Persistence)

- `IClientRepository` — the client master and its sub-rows; lookup for the picker; Maconomy sync queries. `IClientNumberGenerator` for `ClientNumber`.
- `IRemsRequestRepository` — replaces `IRemsRepository`, `IRemsFormRepository`, `IRemsEngagementRepository`. `GetByIdAsync` (type, status, client, files), `GetWorkspaceAsync` (everything), `GetByInviteCodeUnscopedAsync`, the list/inbox/review/related queries with the **same option records as before**, and the child-row mutations. `ClientPersonIds` in the option records now hold client ids (the choice lists return client ids).
- `IRemsApprovalTaskRepository` — replaces `IRemsApprovalRepository`. No rounds: `GetNextRoundNumberAsync(remsId)`, `ListByRequestAsync(remsId)`, `GetTaskWithContextAsync(taskId)` loads the whole request graph plus every task of the request.
- `IRemsSettingsRepository`, `IRemsDelegationRepository` — as before, plus checklist templates.
- `IRemsNumberGenerator` — now a sequence; still `GenerateAsync(tenantId)`.

## Rules every controller follows

1. **Status changes append history.** Whenever `request.StatusId` changes, add a `RemsStatusHistory` row (`FromStatusId`, `ToStatusId`, `ChangedByUserId`, `ChangedOnUtc`, `Reason`, and `ReturnedToUserId` for a send-back) through `IRemsRequestRepository.AddStatusHistoryAsync`. Keep writing the activity events the controller wrote before.
2. **Creating a request creates or picks its client.** A brand-new client: mint a `Person` (individual) or nothing (organisation), then a `Client` with `Id = Guid.NewGuid()`, `ClientNumber` from `IClientNumberGenerator`, `Status = Prospect`, `PersonId` or `LegalName`, `PrimaryEmail`, `PrimaryPhone`, `EntityTypeId` if known. An existing client (`ExistingClientReferenceId` in the request DTO is now the **client id**; `clients/lookup` and `clients/on-file` return client ids): set `ClientId` and lock its entity type. A Maconomy pick: a `Client` with `MaconomyCustomerNumber` (find-or-create via `IClientRepository.FindByMaconomyNumberAsync`). The old `Person.MaconomyCustomerNumber` is never written again.
3. **The form writes the master.** `RemsClientIntake.ApplyAsync(request, payload, entityType, ct)` for draft saves, submit and the staff correction (`PUT requests/{id}/submission`); `RemsClientIntake.ToPayload(request, entityType, lockedEmail)` to build what the form or the submission view shows. Draft = `request.FormLastSavedOnUtc` set and `FormSubmittedOnUtc` null. Submit sets `FormSubmittedOnUtc` and the request status.
4. **Approval:** build the route with `RemsStaticApprovalRoute.Build(request, pickedIds, policy)` (takes the request now); create tasks with `RoundNumber = GetNextRoundNumberAsync`; the round's outcome is `RemsApprovalRounds`. The decline threshold comes from `RemsSettings.DeclineThreshold` (default 1). Approving/rejecting writes history and, on full approval, calls `IMaconomyCustomerSyncService.RecordOnApprovalAsync(request)` then `Enqueue(client)`.
5. **Derived fields keep their shape.** `engagementStatus` → `RemsDerived.EngagementStatusOf(request).ToString()`; form status → `RemsDerived.FormStatusOf`; `rounds[]`/`roundNumber` → `RemsApprovalRounds.Summarize`; `isRequired` → `RemsContactRoles.IsRequired`; entity id / engagement id → client id / request id.
6. **Access checks** keep using `RemsSetupAccess` (now typed on `RemsRequest`).
7. **No new DTOs, no renamed JSON properties, no route changes.** If something cannot be mapped, leave a `// TODO(rems-v2):` comment with the reason and keep the endpoint compiling.
