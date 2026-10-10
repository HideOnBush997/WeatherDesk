using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using System.Collections.Specialized;
using WeatherDesk.App.ViewModels;
using DomainLocation = WeatherDesk.Domain.Models.Location;

namespace WeatherDesk.App;

public partial class MainWindow : Window
{
    private bool _isDraggingHourly;
    private Point _lastHourlyPoint;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += MainWindow_DataContextChanged;
    }

    private void MainWindow_DataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Chat.Messages.CollectionChanged += ChatMessages_CollectionChanged;
            Dispatcher.UIThread.Post(ScrollChatToEnd, DispatcherPriority.Loaded);
        }
    }

    private void ChatMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(ScrollChatToEnd, DispatcherPriority.Background);

    private void ScrollChatToEnd() => ChatScrollViewer.Offset = new Vector(ChatScrollViewer.Offset.X, double.MaxValue);

    private void CityResult_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && sender is Button { DataContext: DomainLocation location })
            vm.Search.Select(location);
    }

    private void CitySearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            _ = vm.Search.SuggestAsync();
    }

    private void QuickQuestion_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && sender is Button { DataContext: string question })
            vm.Chat.UseQuickQuestion(question);
    }

    private void SavedCity_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && sender is Button { DataContext: DomainLocation location })
            vm.CityList.Select(location);
    }

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.OpenSettings();
    }

    private void CloseSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.CloseSettings();
    }

    private void HourlyScroll_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ScrollViewer viewer || !e.GetCurrentPoint(viewer).Properties.IsLeftButtonPressed)
            return;

        _isDraggingHourly = true;
        _lastHourlyPoint = e.GetPosition(viewer);
        viewer.Cursor = new Cursor(StandardCursorType.Hand);
        e.Pointer.Capture(viewer);
        e.Handled = true;
    }

    private void HourlyScroll_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingHourly || sender is not ScrollViewer viewer)
            return;

        var point = e.GetPosition(viewer);
        var delta = point.X - _lastHourlyPoint.X;
        if (Math.Abs(delta) > 0.1)
        {
            viewer.Offset = new Vector(Math.Max(0, viewer.Offset.X - delta), viewer.Offset.Y);
            _lastHourlyPoint = point;
        }
        e.Handled = true;
    }

    private void HourlyScroll_PointerReleased(object? sender, PointerReleasedEventArgs e) => EndHourlyDrag(sender, e.Pointer);

    private void HourlyScroll_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndHourlyDrag(sender, null);

    private void EndHourlyDrag(object? sender, IPointer? pointer)
    {
        _isDraggingHourly = false;
        if (sender is ScrollViewer viewer)
        {
            viewer.Cursor = Cursor.Default;
            pointer?.Capture(null);
        }
    }
}
