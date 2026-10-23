namespace EmsPortal.Domain.Enums;

/// <summary>Why a file is attached to a request. One table holds every file; this says which slot it fills.</summary>
public enum RemsFileKind
{
    Attachment,
    ClientAcceptanceForm,
    PurchaseOrder,
}
