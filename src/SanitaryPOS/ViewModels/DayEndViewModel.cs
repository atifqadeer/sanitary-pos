using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SanitaryPOS.Helpers;
using SanitaryPOS.Localization;
using SanitaryPOS.Models;
using SanitaryPOS.Printing;
using SanitaryPOS.Services;

namespace SanitaryPOS.ViewModels;

/// <summary>
/// Phase 3 — Day-End Closing screen.
/// ANY logged-in user may VIEW the day summary (one shared shop till —
/// there is no per-counter till in this app); only ADMIN may close the
/// day: the counted-cash panel is hidden for Cashier and CloseDay guards
/// again in code. Figures come from DayEndService.GetSummary — read the
/// EXPECTED CASH formula in that file (Mixed bills and any drawer float
/// are deliberately outside Expected Cash and are said so on screen).
/// An already-closed date shows its saved record instead of the form.
/// </summary>
public partial class DayEndViewModel : ViewModelBase
{
    private readonly DayEndService _dayEnd;
    private readonly SettingsService _settings;
    private readonly SessionService _session;

    public ObservableCollection<ReportLine> SummaryRows { get; } = new();
    public ObservableCollection<ReportLine> MethodRows { get; } = new();
    public ObservableCollection<DayClosing> History { get; } = new();

    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private string _countedText = "";
    [ObservableProperty] private string _noteText = "";
    [ObservableProperty] private DayClosing? _existingClosing;

    private DaySummary? _summary;

    public bool IsAdmin => _session.IsAdmin;
    public bool IsClosed => ExistingClosing != null;
    /// <summary>Count + close panel: Admin only, and only while the day is open.</summary>
    public bool CanClose => IsAdmin && !IsClosed;

    public string ExpectedText => _summary == null ? "Rs 0" : MoneyFormatter.Format(_summary.ExpectedCashPaisa);

    public string DifferenceText
    {
        get
        {
            if (ExistingClosing != null)
                return MoneyFormatter.Format(ExistingClosing.DifferencePaisa);
            long counted = MoneyFormatter.ParseRupeesOrZero(CountedText);
            long expected = _summary?.ExpectedCashPaisa ?? 0;
            return MoneyFormatter.Format(counted - expected);
        }
    }

    public string ClosingStatusText
    {
        get
        {
            if (ExistingClosing == null) return "";
            return $"{S("AlreadyClosedMsg")}  •  {S("ClosedBy")}: {ExistingClosing.ClosedByName}  •  " +
                   $"{S("CountedCash")}: {MoneyFormatter.Format(ExistingClosing.CountedPaisa)}  •  " +
                   $"{S("Difference")}: {MoneyFormatter.Format(ExistingClosing.DifferencePaisa)}";
        }
    }

    public DayEndViewModel(LocalizationService loc, DayEndService dayEnd,
                           SettingsService settings, SessionService session)
        : base(loc)
    {
        _dayEnd = dayEnd; _settings = settings; _session = session;
        Load();
    }

    public override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        Load(); // row labels are built from localized strings
    }

    partial void OnSelectedDateChanged(DateTime value) => Load();

    partial void OnCountedTextChanged(string value) => OnPropertyChanged(nameof(DifferenceText));

    partial void OnExistingClosingChanged(DayClosing? value)
    {
        OnPropertyChanged(nameof(IsClosed));
        OnPropertyChanged(nameof(CanClose));
        OnPropertyChanged(nameof(ClosingStatusText));
        OnPropertyChanged(nameof(DifferenceText));
    }

    private void Load()
    {
        string dateDb = SelectedDate.ToString("yyyy-MM-dd");
        _summary = _dayEnd.GetSummary(dateDb);
        ExistingClosing = _dayEnd.GetClosing(dateDb);

        if (ExistingClosing != null)
        {
            CountedText = MoneyFormatter.ToRupeesText(ExistingClosing.CountedPaisa);
            NoteText = ExistingClosing.Note ?? "";
        }
        else
        {
            CountedText = "";
            NoteText = "";
        }

        SummaryRows.Clear();
        var s = _summary;
        SummaryRows.Add(Row(S("DayTotalSales"), MoneyFormatter.Format(s.TotalSalesPaisa)));
        SummaryRows.Add(Row(S("Bills"), s.BillCount.ToString()));
        SummaryRows.Add(Row(S("CustomerReceiptsDay"), MoneyFormatter.Format(s.CustomerReceiptsPaisa)));
        SummaryRows.Add(Row(S("SupplierPaymentsDay"), MoneyFormatter.Format(s.SupplierPaymentsPaisa)));
        SummaryRows.Add(Row(S("ReturnsTotalDay"), MoneyFormatter.Format(s.ReturnsTotalPaisa)));
        SummaryRows.Add(Row(S("CashSalesPaid"), MoneyFormatter.Format(s.CashSalesPaidPaisa)));
        SummaryRows.Add(Row(S("CashReceiptsDay"), MoneyFormatter.Format(s.CustomerCashReceiptsPaisa)));
        SummaryRows.Add(Row(S("CashPaidSuppliersDay"), MoneyFormatter.Format(s.SupplierCashPaymentsPaisa)));
        SummaryRows.Add(Row(S("CashRefundsDay"), MoneyFormatter.Format(s.CashRefundsPaisa)));
        if (s.MixedPaidPaisa > 0)
            SummaryRows.Add(Row(S("MixedPaidNotCounted"), MoneyFormatter.Format(s.MixedPaidPaisa)));
        SummaryRows.Add(Row(S("ExpectedCash"), MoneyFormatter.Format(s.ExpectedCashPaisa)));

        MethodRows.Clear();
        foreach (var m in s.MethodRows)
        {
            MethodRows.Add(new ReportLine
            {
                C1 = m.PaymentMethod,
                C2 = m.BillCount.ToString(),
                C3 = MoneyFormatter.Format(m.TotalPaisa),
                C4 = MoneyFormatter.Format(m.PaidPaisa)
            });
        }

        History.Clear();
        foreach (var h in _dayEnd.GetHistory()) History.Add(h);

        OnPropertyChanged(nameof(ExpectedText));
        OnPropertyChanged(nameof(DifferenceText));
        OnPropertyChanged(nameof(ClosingStatusText));
    }

    private static ReportLine Row(string label, string amount)
        => new() { C1 = label, C2 = amount };

    [RelayCommand]
    private void RefreshDay() => Load();

    [RelayCommand]
    private void CloseDay()
    {
        if (!IsAdmin) { Warn(S("AdminOnlyMsg")); return; }
        if (ExistingClosing != null) { Warn(S("AlreadyClosedMsg")); return; }
        if (string.IsNullOrWhiteSpace(CountedText)) { Warn(S("CountedCashRequiredMsg")); return; }
        if (_summary == null) return;

        try
        {
            var saved = _dayEnd.SaveClosing(new DayClosing
            {
                ClosingDate = SelectedDate.ToString("yyyy-MM-dd"),
                ExpectedPaisa = _summary.ExpectedCashPaisa,
                CountedPaisa = MoneyFormatter.ParseRupeesOrZero(CountedText),
                Note = string.IsNullOrWhiteSpace(NoteText) ? null : NoteText.Trim(),
                UserId = _session.CurrentUser?.Id
            });
            Info(S("DayClosedMsg"));
            ExistingClosing = saved;
            Load();
        }
        catch (Exception ex) { Warn(S(ex.Message)); }
    }

    /// <summary>Z-report: the same rows as the screen, on A4 through the
    /// Phase 2 report printer (pick a PDF printer in the dialog for a PDF).</summary>
    [RelayCommand]
    private void PrintZReport()
    {
        if (_summary == null) return;
        var rows = new List<IReadOnlyList<string>>();
        foreach (var r in SummaryRows) rows.Add(new[] { r.C1, r.C2 });
        rows.Add(new[] { "— " + S("SalesByMethod") + " —", "" });
        foreach (var m in MethodRows) rows.Add(new[] { m.C1, m.C4 });
        rows.Add(new[] { S("CountedCash"), MoneyFormatter.Format(MoneyFormatter.ParseRupeesOrZero(CountedText)) });
        rows.Add(new[] { S("Difference"), DifferenceText });

        new ReportPrintService().PrintReport(
            S("ZReportTitle"),
            DateHelper.FormatDate(SelectedDate),
            new[] { S("Description"), S("Amount") },
            rows,
            $"{S("ExpectedCash")}: {ExpectedText}",
            _settings.GetAll(),
            _settings.Get("a4_printer", ""));
    }
}
