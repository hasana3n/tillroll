using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Tillroll.App.Transports;

/// <summary>What a transport connects to, so the connections view can show a matching icon.</summary>
public enum TransportKind
{
    Bluetooth,
    Network,
    Cable,
    Browser,
}

/// <summary>
/// A platform-agnostic, self-describing transport shown in the shared connections view: a name, a
/// status line, some configurable <see cref="TransportField"/>s, and a toggle action. The desktop
/// (TCP, serial) and browser (Web Serial, WebUSB, WebSocket/SignalR) transports all subclass this, so
/// one shared view renders them uniformly.
/// </summary>
public abstract partial class TransportEntry : ObservableObject
{
    protected TransportEntry(string name) => Name = name;

    public string Name { get; }

    /// <summary>Who or what connects through this transport, in a few plain words ("Android phones").</summary>
    public virtual string Description => string.Empty;

    public bool HasDescription => Description.Length > 0;

    public virtual TransportKind Kind => TransportKind.Network;

    [ObservableProperty] private string _status = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText), nameof(ShowsPrimaryAction))]
    private bool _isActive;

    /// <summary>Set on the transport the window leads with (the first one); its start button is the primary action.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPrimaryAction))]
    private bool _isFeatured;

    /// <summary>True while this transport's button is the screen's one primary action: featured and not running.</summary>
    public bool ShowsPrimaryAction => IsFeatured && !IsActive;

    /// <summary>Connect/Disconnect by default; Start/Stop or Open/Close for the desktop server transports.</summary>
    public virtual string ButtonText => IsActive ? "Disconnect" : "Connect";

    public IReadOnlyList<TransportField> Fields { get; protected set; } = Array.Empty<TransportField>();

    /// <summary>True to show a refresh button (e.g. re-enumerate serial ports).</summary>
    public virtual bool CanRefresh => false;

    [RelayCommand] private Task Toggle() => ToggleAsync();
    [RelayCommand] private Task Refresh() => RefreshAsync();

    protected abstract Task ToggleAsync();
    protected virtual Task RefreshAsync() => Task.CompletedTask;

    protected void Set(bool active, string status)
    {
        IsActive = active;
        Status = status;
    }

    /// <summary>Stop/disconnect on shutdown.</summary>
    public virtual void Shutdown() { }
}
