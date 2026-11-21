using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using ProjectZ.Services;

namespace ProjectZ.Helpers;

/// <summary>Collapses a protected button/command entry when the active user lacks its action code.</summary>
public static class PermissionVisibility
{
    public static readonly DependencyProperty CodeProperty = DependencyProperty.RegisterAttached(
        "Code", typeof(string), typeof(PermissionVisibility), new PropertyMetadata(null, OnCodeChanged));

    private sealed class VisibilityState(object localValue, BindingBase? binding)
    {
        public object LocalValue { get; } = localValue;
        public BindingBase? Binding { get; } = binding;
    }

    private static readonly ConditionalWeakTable<FrameworkElement, VisibilityState> States = new();

    public static void SetCode(DependencyObject element, string? value) => element.SetValue(CodeProperty, value);
    public static string? GetCode(DependencyObject element) => (string?)element.GetValue(CodeProperty);

    private static void OnCodeChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        if (args.NewValue is string code && !string.IsNullOrWhiteSpace(code))
        {
            if (!States.TryGetValue(element, out _))
            {
                var localValue = element.ReadLocalValue(UIElement.VisibilityProperty);
                var binding = BindingOperations.GetBindingBase(element, UIElement.VisibilityProperty);
                States.Add(element, new VisibilityState(localValue, binding));
            }
            element.Loaded -= Refresh;
            element.Loaded += Refresh;
            Refresh(element, new RoutedEventArgs());
        }
        else
        {
            element.Loaded -= Refresh;
            Restore(element);
            States.Remove(element);
        }
    }

    private static void Refresh(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element || GetCode(element) is not { } code
            || !States.TryGetValue(element, out var state)) return;

        if (PermissionContext.Current?.HasPermission(code) == true)
            Restore(element, state);
        else
        {
            BindingOperations.ClearBinding(element, UIElement.VisibilityProperty);
            element.Visibility = Visibility.Collapsed;
        }
    }

    private static void Restore(FrameworkElement element)
    {
        if (States.TryGetValue(element, out var state)) Restore(element, state);
    }

    private static void Restore(FrameworkElement element, VisibilityState state)
    {
        if (state.Binding is not null)
            BindingOperations.SetBinding(element, UIElement.VisibilityProperty, state.Binding);
        else if (state.LocalValue != DependencyProperty.UnsetValue)
            element.SetValue(UIElement.VisibilityProperty, state.LocalValue);
        else
            element.ClearValue(UIElement.VisibilityProperty);
    }
}
