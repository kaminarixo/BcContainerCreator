using System.Security;
using System.Windows;
using System.Windows.Controls;

namespace BcContainerCreator.App.Services;

/// <summary>
/// Attached-Properties, die einen <see cref="PasswordBox"/> bindbar machen.
/// WPF hat das aus Sicherheitsgründen nicht von Haus aus.
///
/// <list type="bullet">
///   <item><see cref="BoundPasswordProperty"/> — als <see cref="SecureString"/> binden</item>
///   <item><see cref="PlainPasswordProperty"/> — als Plain-String binden (für Show/Hide-Toggle)</item>
/// </list>
/// Zum Aktivieren <c>BindPassword="True"</c> setzen.
/// </summary>
public static class PasswordBoxAssistant
{
    private static readonly DependencyProperty SuppressUpdateProperty =
        DependencyProperty.RegisterAttached(
            "SuppressUpdate",
            typeof(bool),
            typeof(PasswordBoxAssistant),
            new PropertyMetadata(false));

    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(SecureString),
            typeof(PasswordBoxAssistant),
            new FrameworkPropertyMetadata(default(SecureString), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty PlainPasswordProperty =
        DependencyProperty.RegisterAttached(
            "PlainPassword",
            typeof(string),
            typeof(PasswordBoxAssistant),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPlainPasswordChanged));

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

    public static string GetPlainPassword(DependencyObject d) =>
        (string)d.GetValue(PlainPasswordProperty);
    public static void SetPlainPassword(DependencyObject d, string value) =>
        d.SetValue(PlainPasswordProperty, value);

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

        // Immer erst abhängen — idempotent, unabhängig davon, was e.OldValue
        // meldet. Verhindert Doppel-Subscriptions bei ungewöhnlichen
        // Property-Übergängen (z. B. Style-/Template-Wechsel).
        pb.PasswordChanged -= OnPasswordChanged;
        if ((bool)e.NewValue)
        {
            pb.PasswordChanged += OnPasswordChanged;

            // Initial-Sync: wenn das ViewModel schon einen Default vorhält,
            // diesen ins PasswordBox.Password schieben.
            var initial = GetPlainPassword(pb);
            if (!string.IsNullOrEmpty(initial) && pb.Password != initial)
            {
                pb.SetValue(SuppressUpdateProperty, true);
                try { pb.Password = initial; }
                finally { pb.SetValue(SuppressUpdateProperty, false); }
            }
        }
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox pb || (bool)pb.GetValue(SuppressUpdateProperty))
        {
            return;
        }

        SetBoundPassword(pb, pb.SecurePassword.Copy());
        SetPlainPassword(pb, pb.Password);
    }

    private static void OnPlainPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox pb)
        {
            return;
        }

        var newValue = (string?)e.NewValue ?? string.Empty;
        if (pb.Password == newValue)
        {
            return;
        }

        // Schleifen-Guard, damit das anschließende PasswordChanged-Event
        // nicht wieder zurück ans ViewModel propagiert wird.
        pb.SetValue(SuppressUpdateProperty, true);
        try { pb.Password = newValue; }
        finally { pb.SetValue(SuppressUpdateProperty, false); }
    }
}
