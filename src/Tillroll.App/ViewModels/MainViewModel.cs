using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tillroll.Controls;
using Tillroll.Controls.Services;
using Tillroll.Emulator;
using Tillroll.Emulator.Rendering;
using Tillroll.Graphics;
using Tillroll.App.Monitor;

namespace Tillroll.App.ViewModels;

/// <summary>The view shown in the side panel beside the rail (or none, to give the receipts the width).</summary>
public enum SidePanel
{
    None,
    Connections,
    Printer,
    TestPrint,
}

/// <summary>
/// The shared main view model, reused by both heads. It owns the platform-agnostic features — the
/// paste/upload ESC/POS input, the rendered receipts, printer-state simulation, export, and the
/// buzzer/cash-drawer toast — and hosts the platform's transport UI via <see cref="ConnectionsView"/>.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private const string DefaultInput =
        "*** Tillroll ***\n" +
        "Type or paste ESC/POS here\n" +
        "and press Render, or start\n" +
        "a connection.\n" +
        "\n" +
        "Item            $ 4.50\n" +
        "Tax             $ 0.36\n" +
        "TOTAL           $ 4.86\n" +
        "\n" +
        "Thank you!\n";

    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(3.5);

    private readonly ReceiptPrinter _printer;
    private readonly IPlatformServices _platform;
    private readonly IImageEncoder _encoder;
    private readonly IFileDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly Dictionary<string, ReceiptViewModel> _receiptsById = new();

    public ObservableCollection<ReceiptViewModel> Receipts { get; } = new();
    public bool HasReceipts => Receipts.Count > 0;
    public PrinterState State => _printer.State;

    /// <summary>Receipts not looked at yet.</summary>
    public int NewCount => Receipts.Count(r => r.IsNew);

    public bool HasNew => NewCount > 0;

    /// <summary>"3 receipts · 1 new" for the canvas title row.</summary>
    public string ReceiptCountText
    {
        get
        {
            int count = Receipts.Count, fresh = NewCount;
            var text = count == 1 ? "1 receipt" : $"{count} receipts";
            return fresh > 0 ? $"{text} · {fresh} new" : text;
        }
    }

    /// <summary>The selected arrival on the board; selecting one marks it seen and brings it into view.</summary>
    [ObservableProperty] private ReceiptViewModel? _selectedReceipt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnectionsOpen), nameof(IsPrinterOpen), nameof(IsTestPrintOpen), nameof(IsSidePanelOpen))]
    private SidePanel _panel = SidePanel.Connections;

    public bool IsConnectionsOpen => Panel == SidePanel.Connections;
    public bool IsPrinterOpen => Panel == SidePanel.Printer;
    public bool IsTestPrintOpen => Panel == SidePanel.TestPrint;
    public bool IsSidePanelOpen => Panel != SidePanel.None;

    [ObservableProperty] private PrinterHealth _health;
    [ObservableProperty] private string _healthText = string.Empty;

    /// <summary>How many connections are running (listening, open or connected).</summary>
    public int LiveCount => Transports.Count(t => t.IsActive);

    public string LiveText => LiveCount switch
    {
        0 => "No connections running",
        1 => "1 connection running",
        var n => $"{n} connections running",
    };

    /// <summary>"80 mm · 576 dots": the paper this printer emulates.</summary>
    public string PaperText { get; }

    /// <summary>Paper width in printer dots: the width every receipt is rendered at.</summary>
    public int PaperDots { get; }

    /// <summary>How the receipts are drawn right now: "2 px per dot", or a warning when they had to be shrunk.</summary>
    [ObservableProperty] private string _scaleText = string.Empty;

    /// <summary>False when the receipts are shrunk below one screen pixel per dot (no longer pixel-exact).</summary>
    [ObservableProperty] private bool _isScaleExact = true;

    /// <summary>The time now, for reading arrivals against (updated every second).</summary>
    [ObservableProperty] private string _clock = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Called by the view whenever the receipts' fit changes.</summary>
    public void ReportScale(PixelPerfectImage.DotFit fit, double renderScaling)
    {
        IsScaleExact = fit.Sharp;
        int pixels = (int)Math.Round(fit.DipsPerDot * renderScaling);
        ScaleText = fit.Sharp ? $"{pixels} px per dot" : "Shrunk to fit, not pixel-exact";
    }

    /// <summary>The platform's transports, rendered uniformly by the shared connections view.</summary>
    public IReadOnlyList<Transports.TransportEntry> Transports { get; }

    /// <summary>The shared Monitor test-client, or null if the platform has no Monitor.</summary>
    public MonitorViewModel? Monitor { get; }
    public bool SupportsMonitor => Monitor is not null;
    public string BackendName => _platform.BackendName;

    [ObservableProperty] private string _input = DefaultInput;
    [ObservableProperty] private string _toastMessage = string.Empty;
    [ObservableProperty] private bool _toastVisible;
    /// <summary>Browser only: shows the Monitor as an in-page overlay (desktop uses a window instead).</summary>
    [ObservableProperty] private bool _isMonitorOpen;

    /// <summary>Raised on the UI thread after receipts change (the view scrolls to the newest).</summary>
    public event EventHandler? ReceiptsUpdated;

    private int _refreshQueued;

    public MainViewModel(ReceiptPrinter printer, IPlatformServices platform)
    {
        _printer = printer;
        _platform = platform;
        _encoder = platform.Encoder;
        _dialogs = platform.FileDialogs;
        _notifications = platform.Notifications;

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => Clock = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        _clockTimer.Start();

        _toastTimer = new DispatcherTimer { Interval = ToastDuration };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastVisible = false; };

        // Transport receive threads raise these — marshal to the UI thread.
        _printer.OnActivityEvent += (_, _) =>
        {
            // One refresh per batch of writes: a BLE job arrives as ~150 writes, and queueing a refresh for each
            // kept the window busy long after the job finished.
            if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
                return;
            Dispatcher.UIThread.Post(() =>
            {
                Volatile.Write(ref _refreshQueued, 0);
                RefreshReceipts();
                _notifications.NotifyActivity();
            });
        };
        _printer.OnBuzzer += () => Dispatcher.UIThread.Post(() => { _notifications.Beep(); ShowToast("Buzzer sounded"); });
        _printer.OnCashDrawer += () => Dispatcher.UIThread.Post(() => { _notifications.OpenCashDrawer(); ShowToast("Cash drawer opened"); });
        _printer.OnPrintBlocked += reason => Dispatcher.UIThread.Post(() => ShowToast($"Print dropped: {reason}"));

        Transports = platform.CreateTransports(printer);
        if (Transports.Count > 0)
            Transports[0].IsFeatured = true;
        foreach (var transport in Transports)
            transport.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(Tillroll.App.Transports.TransportEntry.IsActive))
                    return;
                OnPropertyChanged(nameof(LiveCount));
                OnPropertyChanged(nameof(LiveText));
            };

        var paper = printer.Paper;
        PaperDots = paper.GetPaperWidthInPixels();
        PaperText = string.Create(CultureInfo.InvariantCulture, $"{paper.PaperWidthMm:0} mm · {PaperDots} dots");

        State.PropertyChanged += (_, _) =>
        {
            if (Dispatcher.UIThread.CheckAccess())
                UpdateHealth();
            else
                Dispatcher.UIThread.Post(UpdateHealth);
        };
        UpdateHealth();

        if (platform.CreateMonitorClient() is { } monitorClient)
            Monitor = new MonitorViewModel(monitorClient);

        // Nothing is rendered on launch: the canvas shows how to get the first receipt instead.
    }

    /// <summary>Rail behaviour: opens a panel, or closes it when it is already open.</summary>
    [RelayCommand]
    private void ShowPanel(SidePanel panel) => Panel = Panel == panel ? SidePanel.None : panel;

    /// <summary>Opens a panel without toggling (for links such as the empty state's "Show connections").</summary>
    [RelayCommand]
    private void OpenPanel(SidePanel panel) => Panel = panel;

    [RelayCommand]
    private void MarkAllSeen()
    {
        foreach (var receipt in Receipts)
            receipt.MarkSeen();
    }

    partial void OnSelectedReceiptChanged(ReceiptViewModel? value) => value?.MarkSeen();

    private void UpdateHealth() => (Health, HealthText) = PrinterReadiness.Describe(State);

    private void OnReceiptPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ReceiptViewModel.IsNew))
            return;
        OnPropertyChanged(nameof(NewCount));
        OnPropertyChanged(nameof(HasNew));
        OnPropertyChanged(nameof(ReceiptCountText));
    }

    [RelayCommand]
    private void Render()
    {
        ResetPrinter();
        _printer.FeedEscPos(Encoding.Latin1.GetBytes(Input));
        RefreshReceipts();
    }

    [RelayCommand]
    private void LoadSample()
    {
        var sample = _platform.SampleTicket;
        if (sample.Length == 0)
            return;
        ResetPrinter();
        _printer.FeedEscPos(sample);
        RefreshReceipts();
    }

    [RelayCommand]
    private void Clear()
    {
        ResetPrinter();
        RefreshReceipts();
    }

    [RelayCommand]
    private void OpenMonitor()
    {
        if (Monitor is null)
            return;
        if (_platform.MonitorInWindow)
            _platform.ShowMonitorWindow(Monitor);
        else
            IsMonitorOpen = true; // in-page overlay (browser)
    }

    [RelayCommand]
    private void CloseMonitor() => IsMonitorOpen = false;

    private void ResetPrinter()
    {
        // Transports may be feeding on other threads: change the printer only while no feed is in progress.
        _printer.RunExclusive(() =>
        {
            _printer.ReceiptStack.Clear();
            _printer.Initialize();
            _printer.StartNewReceipt();
        });
        _receiptsById.Clear();
        SelectedReceipt = null;
        foreach (var receipt in Receipts)
            receipt.PropertyChanged -= OnReceiptPropertyChanged;
        Receipts.Clear();
    }

    #region Export

    private List<Receipt> NonEmptyReceipts()
        => _printer.RunExclusive(() => _printer.ReceiptStack.Where(r => !r.IsEmpty).ToList());

    [RelayCommand(CanExecute = nameof(HasReceipts))]
    private async Task ExportAll()
    {
        var receipts = NonEmptyReceipts();
        if (receipts.Count == 0)
            return;

        var bitmaps = _printer.RunExclusive(() => receipts.Select(r => r.Render()).ToList());
        try
        {
            using var combined = ReceiptExporter.StackVertical(bitmaps, _printer.ImageFactory);
            var stream = await _dialogs.SavePngAsync("receipts");
            if (stream is null)
                return;
            await using (stream)
                _encoder.EncodePng(combined, stream);
        }
        finally
        {
            foreach (var b in bitmaps)
                b.Dispose();
        }
    }

    [RelayCommand(CanExecute = nameof(HasReceipts))]
    private async Task ExportEach()
    {
        var receipts = NonEmptyReceipts();
        if (receipts.Count == 0)
            return;

        var folder = await _dialogs.PickFolderAsync();
        if (folder is null)
            return;

        for (int i = 0; i < receipts.Count; i++)
        {
            var receipt = receipts[i];
            using var bmp = _printer.RunExclusive(() => receipt.Render());
            var path = Path.Combine(folder, $"receipt_{i + 1:D3}.png");
            await using var fs = File.Create(path);
            _encoder.EncodePng(bmp, fs);
        }
    }

    #endregion

    /// <summary>Called on shutdown — stop every transport.</summary>
    public void Shutdown()
    {
        _clockTimer.Stop();
        Monitor?.Shutdown();
        foreach (var transport in Transports)
            transport.Shutdown();
    }

    private void ShowToast(string message)
    {
        ToastMessage = message;
        ToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void RefreshReceipts()
    {
        // A transport thread may be feeding right now (a BLE phone sends a job as many small writes): list and
        // render the receipts only while no feed is in progress, or the collections change under us.
        _printer.RunExclusive(RefreshReceiptsCore);

        OnPropertyChanged(nameof(HasReceipts));
        OnPropertyChanged(nameof(NewCount));
        OnPropertyChanged(nameof(HasNew));
        OnPropertyChanged(nameof(ReceiptCountText));
        ExportAllCommand.NotifyCanExecuteChanged();
        ExportEachCommand.NotifyCanExecuteChanged();
        ReceiptsUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshReceiptsCore()
    {
        foreach (var receipt in _printer.ReceiptStack)
        {
            if (receipt.IsEmpty)
                continue;

            if (_receiptsById.TryGetValue(receipt.Guid, out var vm))
            {
                vm.Refresh();
            }
            else
            {
                vm = new ReceiptViewModel(receipt, _encoder, _dialogs, Receipts.Count + 1, _printer.RunExclusive);
                _receiptsById[receipt.Guid] = vm;
                vm.PropertyChanged += OnReceiptPropertyChanged;
                Receipts.Add(vm);
            }
        }
    }
}
