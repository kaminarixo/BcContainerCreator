using System.Security;
using System.Windows;
using System.Windows.Controls;

namespace BcContainerLauncher.App.Services;

/// <summary>
/// Attached-Property, das den <see cref="PasswordBox.SecurePassword"/> als
/// <see cref="SecureString"/> in eine bindbare Eigenschaft pusht. WPF hat
/// von Haus aus keine Bindung am PasswordBox aus Security-Gründen — dieses
/// Helper-Pattern ist die etablierte Lösung.
/// </summary>
public static class PasswordBoxAssistant
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(SecureString),
            typeof(PasswordBoxAssistant),
            new FrameworkPropertyMetadata(default(SecureString), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty BindPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BindPassword",
            typeof(bool),
            typeof(PasswordBoxAssistant),
            new PropertyMetadata(false, OnBindPasswordChanged));

    public static SecureString? GetBoundPassword(DependencyObject d) =>
        (SecureString?)d.GetValue(BoundPasswordProperty);
    public static void SetBoundPassword(DependencyObject d, SecureString? value) =>
        d.SetValue(BoundPasswordProperty, value);

    public static bool GetBindPassword(DependencyObject d) =>
        (bool)d.GetValue(BindPasswordProperty);
    public static void SetBindPassword(DependencyObject d, bool value) =>
        d.SetValue(BindPasswordProperty, value);

    private static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox pb)
        {
            return;
        }

        if ((bool)e.OldValue)
        {
            pb.PasswordChanged -= OnPasswordChanged;
        }
        if ((bool)e.NewValue)
        {
            pb.PasswordChanged += OnPasswordChanged;
        }
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
        {
            SetBoundPassword(pb, pb.SecurePassword.Copy());
        }
    }
}
