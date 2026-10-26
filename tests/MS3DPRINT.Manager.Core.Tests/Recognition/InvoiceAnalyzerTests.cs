using MS3DPRINT.Manager.Core.Recognition;
using MS3DPRINT.Manager.Core.Revenues;

namespace MS3DPRINT.Manager.Core.Tests.Recognition;

/// <summary>Documents are fictional but laid out like the real ones: tickets, shop invoices, supplier invoices, noisy OCR, columns, English.</summary>
public sealed class InvoiceAnalyzerTests
{
    private static readonly AnalysisContext Me = new()
    {
        OwnBusinessName = "MS3DPRINT", OwnOwnerName = "Jean Martin", OwnSiret = "123 456 789 00012", Today = new DateOnly(2026, 10, 26),
        KnownSuppliers = ["Polymaker"], KnownClients = ["Caserne de pompiers de Tuffé"]
    };

    [Fact]
    public void Own_invoice_is_a_revenue_with_client_number_date_and_total()
    {
        var text = """
            MS3DPRINT
            Jean Martin
            SIRET 123 456 789 00012
            12 rue des Ateliers 72000 Le Mans
            Facture FACT202610-33
            Facture émise le 08/10/2026
            Destinataire
            Caserne de pompiers de Tuffé
            Place de la Mairie 72160 Tuffé
            Support de caméra réglable    4    12,50    50,00
            Montage et finition 1 16,17 16,17
            Total HT 66,17
            TVA non applicable, art. 293 B du CGI
            Total TTC 66,17
            """;
        var r = InvoiceAnalyzer.Analyze(text, Me);
        Assert.Equal(DocumentDirection.Revenue, r.Direction);
        Assert.Equal(DocumentKind.Invoice, r.Kind);
        Assert.Equal(66.17m, r.TotalTtc!.Value);
        Assert.Equal(new DateOnly(2026, 10, 8), r.Date!.Value);
        Assert.Equal("FACT202610-33", r.Number!.Value);
        Assert.Equal("Caserne de pompiers de Tuffé", r.Party!.Value);
        Assert.True(r.TotalTtc.IsReliable);
    }

    [Fact]
    public void Shop_receipt_takes_the_total_not_the_cash_given_or_the_change()
    {
        var text = """
            LEROY MERLIN LE MANS
            ZI Sud 72100 LE MANS
            Tel 02 43 00 00 00
            VIS TC 4X40 X200      6,90
            COLLE CYANO 20G       4,50
            EQUERRE ACIER 2X      3,00
            TOTAL TTC            14,40
            ESPECES              20,00
            RENDU                 5,60
            TVA 20,00%            2,40
            24/10/2026 14:32 Ticket n° 004512
            Merci de votre visite
            """;
        var r = InvoiceAnalyzer.Analyze(text, Me);
        Assert.Equal(DocumentDirection.Purchase, r.Direction);
        Assert.Equal(DocumentKind.Receipt, r.Kind);
        Assert.Equal(14.40m, r.TotalTtc!.Value);
        Assert.Equal(new DateOnly(2026, 10, 24), r.Date!.Value);
        Assert.Equal("Leroy Merlin", r.Party!.Value);
        Assert.Equal("Espèces", r.Payment!.Value);
        Assert.Equal("004512", r.Number!.Value);
    }

    [Fact]
    public void Supplier_invoice_addressed_to_the_user_is_a_purchase_with_checked_vat()
    {
        var text = """
            POLYMAKER SAS
            Zone Industrielle 69000 Lyon
            SIRET 552 081 317 00011
            Facture n° FA-2026-004521
            Date : 17/10/2026
            Échéance : 17/11/2026
            Facturé à
            MS3DPRINT
            Jean Martin
            SIRET 123 456 789 00012
            Panchroma PLA 1kg noir       3     19,90    59,70
            Frais de port                 1      6,00     6,00
            Total HT        65,70
            TVA 20 %        13,14
            Total TTC       78,84
            """;
        var r = InvoiceAnalyzer.Analyze(text, Me);
        Assert.Equal(DocumentDirection.Purchase, r.Direction);
        Assert.Equal(78.84m, r.TotalTtc!.Value);
        Assert.Equal(65.70m, r.TotalHt!.Value);
        Assert.Equal(13.14m, r.Vat!.Value);
        Assert.Equal("Polymaker", r.Party!.Value);
        Assert.Equal("FA-2026-004521", r.Number!.Value);
        Assert.Equal(new DateOnly(2026, 10, 17), r.Date!.Value);
        Assert.Equal("55208131700011", r.PartySiret!.Value);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void Due_date_is_never_taken_for_the_invoice_date()
    {
        var r = InvoiceAnalyzer.Analyze("SARL Dupont Outillage\nFacture n° 2026-118\nDate limite de paiement : 30/11/2026\nDate de facture : 02/11/2026\nTotal TTC 120,00 €", new AnalysisContext { Today = new DateOnly(2026, 11, 10) });
        Assert.Equal(new DateOnly(2026, 11, 2), r.Date!.Value);
    }

    [Fact]
    public void Ocr_damage_inside_numbers_is_repaired()
    {
        var text = """
            BRICOMAN
            Ticket n° 77l9
            23/lO/2026
            PERCEUSE 18V   l29,9O
            TOTAL A PAYER  l29,9O
            CB SANS CONTACT l29,9O
            """;
        var r = InvoiceAnalyzer.Analyze(text, Me);
        Assert.Equal(129.90m, r.TotalTtc!.Value);
        Assert.Equal("Carte bancaire", r.Payment!.Value);
    }

    [Fact]
    public void Column_layout_with_labels_then_amounts_is_put_back_together()
    {
        var text = """
            Atelier Bois & Cie
            Facture 8841
            Total HT
            TVA 20%
            Total TTC
            100,00
            20,00
            120,00
            """;
        var r = InvoiceAnalyzer.Analyze(text, new AnalysisContext { Today = new DateOnly(2026, 10, 26) });
        Assert.Equal(120.00m, r.TotalTtc!.Value);
        Assert.Equal(100.00m, r.TotalHt!.Value);
        Assert.Equal(20.00m, r.Vat!.Value);
    }

    [Fact]
    public void English_invoice_with_thousand_separators_and_a_written_month()
    {
        var text = """
            Bambu Lab Store
            Invoice No: INV-202610-8832
            Invoice date: October 12, 2026
            Subtotal  1,249.00
            Shipping  0.00
            Tax  0.00
            Total  1,249.00
            Paid with PayPal
            """;
        var r = InvoiceAnalyzer.Analyze(text, Me);
        Assert.Equal(1249.00m, r.TotalTtc!.Value);
        Assert.Equal(new DateOnly(2026, 10, 12), r.Date!.Value);
        Assert.Equal("INV-202610-8832", r.Number!.Value);
        Assert.Equal("Bambu Lab", r.Party!.Value);
    }

    [Fact]
    public void Online_order_page_uses_the_order_number_and_warns_it_is_not_an_invoice()
    {
        var text = """
            Amazon.fr
            Confirmation de commande
            Commande n° 171-2345678-9012345
            Commandé le 20 octobre 2026
            Total de la commande : 35,98 €
            """;
        var r = InvoiceAnalyzer.Analyze(text, Me);
        Assert.Equal(DocumentKind.OrderConfirmation, r.Kind);
        Assert.Equal("171-2345678-9012345", r.Number!.Value);
        Assert.Equal(35.98m, r.TotalTtc!.Value);
        Assert.Contains(r.Warnings, w => w.Contains("pas une facture"));
    }

    [Fact]
    public void Quote_is_flagged_and_deposit_invoice_prefers_the_amount_to_pay()
    {
        var quote = InvoiceAnalyzer.Analyze("MS3DPRINT\nDevis D-2026-044\nTotal TTC 500,00", Me);
        Assert.Equal(DocumentKind.Quote, quote.Kind);
        Assert.Contains(quote.Warnings, w => w.Contains("devis"));

        var deposit = InvoiceAnalyzer.Analyze("Atelier Martin\nFacture n° F-2026-77\n02/10/2026\nTotal TTC 1 000,00\nAcompte déjà versé 300,00\nNet à payer 700,00", new AnalysisContext { Today = new DateOnly(2026, 10, 26) });
        Assert.Equal(700.00m, deposit.TotalTtc!.Value);
    }

    [Fact]
    public void Unit_prices_and_percentages_are_not_taken_for_the_total()
    {
        var r = InvoiceAnalyzer.Analyze("Quincaillerie du Centre\n05/10/2026\n2 x 3,50   7,00\nTVA 20,00 %\nTOTAL 7,00 €\nCB 7,00", new AnalysisContext { Today = new DateOnly(2026, 10, 26) });
        Assert.Equal(7.00m, r.TotalTtc!.Value);
    }

    [Fact]
    public void Two_digit_year_and_dotted_dates_are_understood_day_first()
    {
        var r = InvoiceAnalyzer.Analyze("Boulangerie Dupain\n03.10.26 08:15\nTOTAL 4,20 EUR", new AnalysisContext { Today = new DateOnly(2026, 10, 26) });
        Assert.Equal(new DateOnly(2026, 10, 3), r.Date!.Value);
        Assert.Equal(4.20m, r.TotalTtc!.Value);
    }

    [Fact]
    public void Garbage_text_reads_nothing_instead_of_inventing()
    {
        var r = InvoiceAnalyzer.Analyze("Bonjour, voici la photo que tu voulais.\nA bientôt", Me);
        Assert.True(r.IsEmpty);
        Assert.Null(r.TotalTtc);
    }

    [Fact]
    public void Whole_euro_amounts_and_the_euro_sign_as_decimal_separator()
    {
        var r = InvoiceAnalyzer.Analyze("Café du Port\nTicket 12\n10/10/2026\nTOTAL 12€50", new AnalysisContext { Today = new DateOnly(2026, 10, 26) });
        Assert.Equal(12.50m, r.TotalTtc!.Value);
    }

    [Fact]
    public void Bank_app_screenshot_with_a_negative_amount_and_a_written_date()
    {
        var text = """
            Paiement par carte
            LEROY MERLIN LE MANS
            -14,40 €
            Samedi 24 octobre 2026 à 14:32
            Catégorie : Bricolage
            """;
        var r = InvoiceAnalyzer.Analyze(text, Me);
        Assert.Equal(14.40m, r.TotalTtc!.Value);
        Assert.Equal(new DateOnly(2026, 10, 24), r.Date!.Value);
        Assert.Equal("Leroy Merlin", r.Party!.Value);
    }

    [Fact]
    public void Paypal_payment_screenshot()
    {
        var text = """
            Vous avez envoyé un paiement de 24,90 € EUR à Sunlu Official
            Date de la transaction : 15 oct. 2026
            Numéro de transaction : 5RT12345AB6789012
            Mode de paiement : Solde PayPal
            """;
        var r = InvoiceAnalyzer.Analyze(text, Me);
        Assert.Equal(24.90m, r.TotalTtc!.Value);
        Assert.Equal(new DateOnly(2026, 10, 15), r.Date!.Value);
        Assert.Equal("Sunlu", r.Party!.Value);
    }

    [Fact]
    public void Fuel_ticket_ignores_the_volume_and_the_price_per_litre()
    {
        var text = """
            TOTAL ENERGIES RELAIS DU MANS
            Ticket 00231
            18/10/2026 07:45
            GAZOLE  35,12 L
            Prix/L 1,547
            TOTAL EUR 54,32
            CB 54,32
            """;
        var r = InvoiceAnalyzer.Analyze(text, Me);
        Assert.Equal(54.32m, r.TotalTtc!.Value);
        Assert.Equal(DocumentDirection.Purchase, r.Direction);
    }

    [Fact]
    public void Dollar_amounts_are_flagged_so_that_they_are_not_recorded_as_euros()
    {
        var text = """
            AliExpress
            Order placed: Oct 11, 2026
            Order total: US $12.99
            Shipping: US $0.00
            """;
        var r = InvoiceAnalyzer.Analyze(text, Me);
        Assert.Equal(12.99m, r.TotalTtc!.Value);
        Assert.Contains(r.Warnings, w => w.Contains("dollars"));
    }

    [Fact]
    public void Amount_to_settle_wording_and_euro_sign_after_thousands()
    {
        var text = """
            Atelier Mécanique Rousseau
            Facture n° 2026-0412
            Date de facturation : 09/10/2026
            Date d'échéance : 08/11/2026
            Sous-total  2 100,00 €
            TVA (20 %)  420,00 €
            Montant à régler  2 520,00 €
            """;
        var r = InvoiceAnalyzer.Analyze(text, new AnalysisContext { Today = new DateOnly(2026, 10, 26) });
        Assert.Equal(2520.00m, r.TotalTtc!.Value);
        Assert.Equal(new DateOnly(2026, 10, 9), r.Date!.Value);
        Assert.Equal("2026-0412", r.Number!.Value);
    }

    [Fact]
    public void Own_invoice_is_recognised_by_siret_even_when_the_name_is_unknown()
    {
        var text = """
            Atelier X
            SIRET : 12345678900012
            Facture F-1
            01/10/2026
            Client
            Bati Drone Services
            Total TTC 90,00
            """;
        var r = InvoiceAnalyzer.Analyze(text, new AnalysisContext { OwnSiret = "123 456 789 00012", Today = new DateOnly(2026, 10, 26) });
        Assert.Equal(DocumentDirection.Revenue, r.Direction);
        Assert.Equal("Bati Drone Services", r.Party!.Value);
    }
}
