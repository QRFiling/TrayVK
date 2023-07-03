using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace TrayVK
{
    public partial class InfinityProgressBar : UserControl
    {
        public InfinityProgressBar()
        {
            InitializeComponent();
        }

        Storyboard storyboard;

        void Border_Loaded(object sender, RoutedEventArgs e)
        {
            storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            Border border = sender as Border;

            DoubleAnimation animation = new DoubleAnimation
            {
                From = -border.ActualWidth,
                To = canvas.ActualWidth,
                Duration = TimeSpan.FromSeconds(0.8)
            };

            Storyboard.SetTarget(animation, border);
            Storyboard.SetTargetProperty(animation, new PropertyPath("(Canvas.Left)"));

            storyboard.Children.Add(animation);
            storyboard.Begin();
        }

        void userControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible) storyboard.Begin();
            else storyboard.Stop();          
        }
    }
}
