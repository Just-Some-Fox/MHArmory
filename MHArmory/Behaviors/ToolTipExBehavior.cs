using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MHArmory.Behaviors
{
    public static class ToolTipEx
    {
        public static object GetContent(DependencyObject target)
        {
            return target.GetValue(ContentProperty);
        }

        public static void SetContent(DependencyObject target, object value)
        {
            target.SetValue(ContentProperty, value);
        }

        public static readonly DependencyProperty ContentProperty = DependencyProperty.RegisterAttached(
            "Content",
            typeof(object),
            typeof(ToolTipEx),
            new PropertyMetadata(OnContentPropertyChanged)
        );

        private static void OnContentPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            sender.SetValue(ToolTipService.ToolTipProperty, e.NewValue);

            if (e.NewValue != null)
                sender.SetValue(ToolTipService.ShowDurationProperty, 9999);
            else
                sender.ClearValue(ToolTipService.ShowDurationProperty);
        }
    }
}