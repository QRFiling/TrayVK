using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TrayVK
{
    public partial class NotificationMessage : Window
    {
        public event Action NotifyClick;

        public NotificationMessage(ImageSource image, string title, string text, int timeOut, Color iconColor = default)
        {
            InitializeComponent();
            CloseAwaiter(timeOut);

            if (iconColor != default)
            {
                icon.ImageSource = image;
                this.iconColor.Color = iconColor;
            }
            else this.image.ImageSource = image;

            this.title.Text = title;
            this.text.Text = text;
        }

        async void CloseAwaiter(int timeOut)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            while (stopwatch.ElapsedMilliseconds < timeOut)
                await Task.Delay(1);

            stopwatch.Stop();

            if ((Content as FrameworkElement).IsMouseOver || GetInactiveTime() >= timeOut)
            {
                CloseAwaiter(timeOut);
                return;
            }

            Close();
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        long GetInactiveTime()
        {
            LASTINPUTINFO info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(info);

            GetLastInputInfo(ref info);
            return Environment.TickCount - info.dwTime;
        }

        const double PADDING = 20;

        void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateLayout();
            Left = SystemParameters.PrimaryScreenWidth;
            UpdateTop();

            DoubleAnimation animation = new DoubleAnimation
            {
                From = Left,
                To = Left - ActualWidth - PADDING,
                Duration = TimeSpan.FromSeconds(1),
                EasingFunction = new PowerEase { Power = 15 }
            };

            BeginAnimation(LeftProperty, animation);
        }

        void UpdateTop()
        {
            NotificationMessage last = Application.Current.Windows.OfType<NotificationMessage>().LastOrDefault(fd => fd != this);
            double top = last != null ? last.Top - ActualHeight - PADDING / 2 : 0;

            if (top == 0 && Application.Current.MainWindow.WindowState == WindowState.Normal)
                top = Application.Current.MainWindow.Top - ActualHeight - PADDING / 1.5;

            Top = top > 0 ? top : SystemParameters.WorkArea.Height - ActualHeight - PADDING;
        }

        public bool closeClick = false;

        void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            closeClick = true;
            Close();
        }

        bool realyClose = false;

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!realyClose)
            {
                e.Cancel = true;

                DoubleAnimation animation = new DoubleAnimation
                {
                    From = Left,
                    To = SystemParameters.PrimaryScreenWidth,
                    Duration = TimeSpan.FromSeconds(0.7),
                    EasingFunction = new PowerEase { Power = 5, EasingMode = EasingMode.EaseIn }
                };

                animation.Completed += (s, a) =>
                {
                    realyClose = true;
                    Close();
                };

                BeginAnimation(LeftProperty, animation);
            }
        }

        void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!closeClick)
            {
                Close();
                NotifyClick?.Invoke();
            }
        }
    }
}
