using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;



namespace AIComponent.Behaviors;

/// <summary>View-only keyboard behavior; keeps multiline editing independent of conversation logic.</summary>
public static class ChatViewBehavior
{
    public static readonly DependencyProperty EnterCommandProperty = DependencyProperty.RegisterAttached(
        "EnterCommand", typeof(ICommand), typeof(ChatViewBehavior), new PropertyMetadata(null, OnEnterCommandChanged));
    public static ICommand? GetEnterCommand(DependencyObject element) => (ICommand?)element.GetValue(EnterCommandProperty);
    public static void SetEnterCommand(DependencyObject element, ICommand? value) => element.SetValue(EnterCommandProperty, value);
    private static void OnEnterCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.PreviewKeyDown -= OnKeyDown;
        if (e.NewValue is not null) element.PreviewKeyDown += OnKeyDown;
    }
    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        var command = GetEnterCommand((DependencyObject)sender);
        if (command?.CanExecute(null) == true) command.Execute(null);
    }
}

