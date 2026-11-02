using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using SanitaryPOS.Helpers;
using SanitaryPOS.Models;

namespace SanitaryPOS.Printing;

/// <summary>Shared printing helpers. Printouts are ALWAYS bilingual (EN + UR)
/// no matter which language the screen is in — the customer reads the paper.</summary>
public static class PrintHelper
{
    public static Paragraph Para(string text, double size = 12, bool bold = false,
                                 TextAlignment align = TextAlignment.Left)
    {
        return new Paragraph(new Run(text))
        {
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            TextAlignment = align,
            Margin = new Thickness(0, 1, 0, 1)
        };
    }

    public static TableCell Cell(string text, bool bold = false, TextAlignment align = TextAlignment.Left)
    {
        var p = Para(text, 11, bold, align);
        return new TableCell(p) { Padding = new Thickness(2) };
    }

    public static void AddShopHeader(FlowDocument doc, IDictionary<string, string> shop, double titleSize)
    {
        doc.Blocks.Add(Para(shop.TryGetValue("shop_name_en", out string? en) ? en : "My Sanitary Store",
                            titleSize, true, TextAlignment.Center));
        doc.Blocks.Add(Para(shop.TryGetValue("shop_name_ur", out string? ur) ? ur : "",
                            titleSize - 2, false, TextAlignment.Center));
        if (shop.TryGetValue("shop_address", out string? addr) && !string.IsNullOrWhiteSpace(addr))
            doc.Blocks.Add(Para(addr, 10, false, TextAlignment.Center));

        string phone = shop.TryGetValue("shop_phone", out string? ph) ? ph : "";
        string wa = shop.TryGetValue("shop_whatsapp", out string? w) ? w : "";
        string contact = string.Join("  ", new[] { phone, string.IsNullOrWhiteSpace(wa) ? "" : "WhatsApp: " + wa }
                                     .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(contact))
            doc.Blocks.Add(Para(contact, 10, false, TextAlignment.Center));
    }

    /// <summary>
    /// Print a FlowDocument. If a printer name is saved in Settings it is used
    /// directly; otherwise the Windows print dialog opens (where the operator
    /// can also pick "Microsoft Print to PDF" to save a PDF for WhatsApp).
    /// </summary>
    public static bool Print(FlowDocument doc, string? printerName, string description)
    {
        var dialog = new PrintDialog();
        try
        {
            if (!string.IsNullOrWhiteSpace(printerName))
            {
                // Not disposed on purpose: the PrintQueue must stay alive until PrintDocument runs.
                var server = new LocalPrintServer();
                dialog.PrintQueue = server.GetPrintQueue(printerName);
            }
            else if (dialog.ShowDialog() != true)
            {
                return false; // operator cancelled
            }
        }
        catch
        {
            // Saved printer not found on this PC — fall back to the dialog.
            if (dialog.ShowDialog() != true) return false;
        }

        var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        dialog.PrintDocument(paginator, description);
        return true;
    }
}

/// <summary>80mm thermal receipt for a completed bill (bilingual).</summary>
public class ReceiptService
{
    public FlowDocument BuildReceipt(Sale sale, IReadOnlyList<SaleItem> items,
                                     IDictionary<string, string> shop, string? customerName)
    {
        // 80mm at 96dpi ≈ 302px; use 280 so margins fit cheap thermal printers.
        var doc = new FlowDocument
        {
            PageWidth = 280,
            PagePadding = new Thickness(4),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 11
        };

        PrintHelper.AddShopHeader(doc, shop, 15);
        doc.Blocks.Add(PrintHelper.Para("SALES RECEIPT / سیلز رسید", 12, true, TextAlignment.Center));
        doc.Blocks.Add(PrintHelper.Para($"Bill No / بل نمبر: {sale.BillNo}    Date / تاریخ: {DateHelper.FormatDbDate(sale.CreatedAt)}", 10));
        if (!string.IsNullOrWhiteSpace(customerName))
            doc.Blocks.Add(PrintHelper.Para($"Customer / گاہک: {customerName}", 10));

        var table = new Table { FontSize = 10 };
        table.Columns.Add(new TableColumn { Width = new GridLength(120) });
        table.Columns.Add(new TableColumn { Width = new GridLength(30) });
        table.Columns.Add(new TableColumn { Width = new GridLength(55) });
        table.Columns.Add(new TableColumn { Width = new GridLength(60) });
        var group = new TableRowGroup();
        group.Rows.Add(new TableRow
        {
            Cells =
            {
                PrintHelper.Cell("Item", true),
                PrintHelper.Cell("Qty", true, TextAlignment.Center),
                PrintHelper.Cell("Rate", true, TextAlignment.Right),
                PrintHelper.Cell("Amount", true, TextAlignment.Right)
            }
        });
        foreach (var i in items)
        {
            group.Rows.Add(new TableRow
            {
                Cells =
                {
                    PrintHelper.Cell(i.ProductNameSnapshot), // product names stay English
                    PrintHelper.Cell(i.Qty.ToString("0.##"), false, TextAlignment.Center),
                    PrintHelper.Cell(MoneyFormatter.FormatNumber(i.RatePaisa), false, TextAlignment.Right),
                    PrintHelper.Cell(MoneyFormatter.FormatNumber(i.LineTotalPaisa), false, TextAlignment.Right)
                }
            });
        }
        table.RowGroups.Add(group);
        doc.Blocks.Add(table);

        doc.Blocks.Add(PrintHelper.Para($"Subtotal / ذیلی کل: {MoneyFormatter.Format(sale.SubtotalPaisa)}", 10, false, TextAlignment.Right));
        long discount = sale.ItemDiscountPaisa + sale.BillDiscountPaisa;
        if (discount > 0)
            doc.Blocks.Add(PrintHelper.Para($"Discount / رعایت: -{MoneyFormatter.Format(discount)}", 10, false, TextAlignment.Right));
        if (sale.TaxPaisa > 0)
            doc.Blocks.Add(PrintHelper.Para($"Tax / ٹیکس: {MoneyFormatter.Format(sale.TaxPaisa)}", 10, false, TextAlignment.Right));
        doc.Blocks.Add(PrintHelper.Para($"TOTAL / کل رقم: {MoneyFormatter.Format(sale.TotalPaisa)}", 13, true, TextAlignment.Right));
        doc.Blocks.Add(PrintHelper.Para($"Paid / وصول: {MoneyFormatter.Format(sale.PaidPaisa)}  ({sale.PaymentMethod})", 10, false, TextAlignment.Right));
        if (sale.BalancePaisa > 0)
            doc.Blocks.Add(PrintHelper.Para($"Balance (Udhaar) / بقایا ادھار: {MoneyFormatter.Format(sale.BalancePaisa)}", 11, true, TextAlignment.Right));
        else if (sale.PaidPaisa > sale.TotalPaisa)
            doc.Blocks.Add(PrintHelper.Para($"Change / واپس رقم: {MoneyFormatter.Format(sale.PaidPaisa - sale.TotalPaisa)}", 10, false, TextAlignment.Right));

        doc.Blocks.Add(PrintHelper.Para("Thank you for your business!", 10, false, TextAlignment.Center));
        doc.Blocks.Add(PrintHelper.Para("آپ کے کاروبار کا شکریہ!", 11, false, TextAlignment.Center));
        return doc;
    }

    public bool PrintReceipt(Sale sale, IReadOnlyList<SaleItem> items,
                             IDictionary<string, string> shop, string? customerName, string? printerName)
        => PrintHelper.Print(BuildReceipt(sale, items, shop, customerName), printerName, $"Receipt Bill #{sale.BillNo}");
}

/// <summary>A4 bilingual quotation (for builders / plumbers / house customers).</summary>
public class QuotationPrintService
{
    public FlowDocument BuildQuotation(Quotation q, IReadOnlyList<QuotationItem> items,
                                       IDictionary<string, string> shop, string? savedCustomerName)
    {
        var doc = new FlowDocument
        {
            PageWidth = 816,   // A4-ish at 96dpi with margins
            PageHeight = 1056,
            PagePadding = new Thickness(40),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 12
        };

        PrintHelper.AddShopHeader(doc, shop, 20);
        doc.Blocks.Add(PrintHelper.Para("QUOTATION / ESTIMATE — کوٹیشن / تخمینہ", 15, true, TextAlignment.Center));
        doc.Blocks.Add(PrintHelper.Para(
            $"Quotation No / کوٹیشن نمبر: {q.QuotationNo}      Date / تاریخ: {DateHelper.FormatDbDate(q.CreatedAt)}", 11));

        string custName = savedCustomerName ?? q.CustomerNameSnapshot ?? "";
        string custMobile = q.CustomerMobileSnapshot ?? "";
        if (!string.IsNullOrWhiteSpace(custName))
            doc.Blocks.Add(PrintHelper.Para($"Customer / گاہک: {custName}   {custMobile}", 11));

        // Validity line — exact wording required by the owner.
        doc.Blocks.Add(PrintHelper.Para(
            $"Rates valid for {q.ValidDays} days / یہ نرخ {q.ValidDays} دن کے لیے ہیں", 11, true));

        var table = new Table { FontSize = 11 };
        table.Columns.Add(new TableColumn { Width = new GridLength(330) });
        table.Columns.Add(new TableColumn { Width = new GridLength(60) });
        table.Columns.Add(new TableColumn { Width = new GridLength(100) });
        table.Columns.Add(new TableColumn { Width = new GridLength(100) });
        table.Columns.Add(new TableColumn { Width = new GridLength(110) });
        var group = new TableRowGroup();
        group.Rows.Add(new TableRow
        {
            Cells =
            {
                PrintHelper.Cell("Item", true),
                PrintHelper.Cell("Qty", true, TextAlignment.Center),
                PrintHelper.Cell("Rate", true, TextAlignment.Right),
                PrintHelper.Cell("Discount", true, TextAlignment.Right),
                PrintHelper.Cell("Amount", true, TextAlignment.Right)
            }
        });
        foreach (var i in items)
        {
            group.Rows.Add(new TableRow
            {
                Cells =
                {
                    PrintHelper.Cell(i.ProductNameSnapshot),
                    PrintHelper.Cell(i.Qty.ToString("0.##"), false, TextAlignment.Center),
                    PrintHelper.Cell(MoneyFormatter.FormatNumber(i.RatePaisa), false, TextAlignment.Right),
                    PrintHelper.Cell(MoneyFormatter.FormatNumber(i.DiscountPaisa), false, TextAlignment.Right),
                    PrintHelper.Cell(MoneyFormatter.FormatNumber(i.LineTotalPaisa), false, TextAlignment.Right)
                }
            });
        }
        table.RowGroups.Add(group);
        doc.Blocks.Add(table);

        doc.Blocks.Add(PrintHelper.Para($"Subtotal / ذیلی کل: {MoneyFormatter.Format(q.SubtotalPaisa)}", 11, false, TextAlignment.Right));
        if (q.DiscountPaisa > 0)
            doc.Blocks.Add(PrintHelper.Para($"Discount / رعایت: -{MoneyFormatter.Format(q.DiscountPaisa)}", 11, false, TextAlignment.Right));
        doc.Blocks.Add(PrintHelper.Para($"TOTAL / کل رقم: {MoneyFormatter.Format(q.TotalPaisa)}", 14, true, TextAlignment.Right));

        doc.Blocks.Add(PrintHelper.Para(" ", 11));
        doc.Blocks.Add(PrintHelper.Para("To order, visit our shop or message us on WhatsApp. / آرڈر کے لیے دکان تشریف لائیں یا واٹس ایپ پر پیغام بھیجیں۔", 11));
        doc.Blocks.Add(PrintHelper.Para("Signature / دستخط: ____________________", 11));
        return doc;
    }

    public bool PrintQuotation(Quotation q, IReadOnlyList<QuotationItem> items,
                               IDictionary<string, string> shop, string? savedCustomerName, string? printerName)
        => PrintHelper.Print(BuildQuotation(q, items, shop, savedCustomerName), printerName, $"Quotation #{q.QuotationNo}");
}
