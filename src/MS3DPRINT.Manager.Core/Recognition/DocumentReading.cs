using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Recognition;

public enum DocumentDirection { Unknown, Purchase, Revenue }

public enum DocumentKind { Unknown, Invoice, Receipt, Quote, CreditNote, OrderConfirmation, DeliveryNote }

/// <summary>A value read from a document, how sure the reader is (0 to 1) and the passage it was read from, so the user can check it at a glance.</summary>
public sealed record ReadField<T>(T Value, double Confidence, string Evidence)
{
    /// <summary>Below this the user is told to check the value; above it the value is still shown for confirmation.</summary>
    public bool IsReliable => Confidence >= 0.7;
}

/// <summary>What the reader understood of one invoice, receipt or screenshot. Every value is a proposal: nothing is recorded without the user's confirmation.</summary>
public sealed record DocumentReading
{
    public DocumentKind Kind { get; init; }
    public DocumentDirection Direction { get; init; }
    public string DirectionReason { get; init; } = "";
    public ReadField<decimal>? TotalTtc { get; init; }
    public ReadField<decimal>? TotalHt { get; init; }
    public ReadField<decimal>? Vat { get; init; }
    public ReadField<DateOnly>? Date { get; init; }
    /// <summary>Invoice number; for a receipt or an online order the ticket or order number.</summary>
    public ReadField<string>? Number { get; init; }
    /// <summary>The other party: the supplier of a purchase, the client of a revenue.</summary>
    public ReadField<string>? Party { get; init; }
    public ReadField<string>? PartySiret { get; init; }
    public ReadField<string>? Payment { get; init; }
    public ReadField<string>? Label { get; init; }
    public RevenueNature? Nature { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    /// <summary>0 to 1: how much of what matters was found with confidence. Used to pick the best of several readings of the same image.</summary>
    public double Quality { get; init; }
    public string Text { get; init; } = "";
    /// <summary>How the text was obtained: "PDF", "OCR", "OCR (inversé)"…</summary>
    public string Source { get; init; } = "";
    /// <summary>Every amount found on the document, for a "which figure is it?" choice when the total is uncertain.</summary>
    public IReadOnlyList<decimal> Amounts { get; init; } = [];

    public bool IsEmpty => TotalTtc is null && Date is null && Number is null;
}

/// <summary>What the reader may know about the user: it decides whether a document is a purchase or a revenue and avoids taking the user's own name for the other party.</summary>
public sealed record AnalysisContext
{
    public string? OwnBusinessName { get; init; }
    public string? OwnOwnerName { get; init; }
    public string? OwnSiret { get; init; }
    public IReadOnlyList<string> KnownSuppliers { get; init; } = [];
    public IReadOnlyList<string> KnownClients { get; init; } = [];
    public DateOnly Today { get; init; } = DateOnly.FromDateTime(DateTime.Today);
}
