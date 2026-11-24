using System.Windows;
using System.Windows.Threading;
using AduSkin.Controls;

namespace AIComponent.Controls;

/// <summary>Coalesces token updates before asking AduSkin to render Markdown.</summary>
public sealed class StreamingMarkdownViewer : AduMarkDownViewer
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StreamingMarkdownViewer),
        new PropertyMetadata(string.Empty, OnTextChanged));

    private readonly DispatcherTimer renderTimer;

    public StreamingMarkdownViewer()
    {
        renderTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(80)
        };
        renderTimer.Tick += (_, _) => RenderLatest();
        Loaded += (_, _) => { if (IsVisible) RenderLatest(); };
        Unloaded += (_, _) => renderTimer.Stop();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && IsLoaded) RenderLatest();
            else renderTimer.Stop();
        };
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var viewer = (StreamingMarkdownViewer)sender;
        // Do not restart the timer: a continuous token stream must still render.
        if (viewer.IsLoaded && viewer.IsVisible && !viewer.renderTimer.IsEnabled) viewer.renderTimer.Start();
    }

    private void RenderLatest()
    {
        renderTimer.Stop();
        MarkdownText = Text ?? string.Empty;
    }
}
