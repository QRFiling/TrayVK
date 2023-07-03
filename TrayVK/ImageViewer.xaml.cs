using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using static TrayVK.MainWindow;

namespace TrayVK
{
    public partial class ImageViewer : Window
    {
        public ImageViewer(VisualMessage message)
        {
            InitializeComponent();

            imageSize.Text = $"{message.OriginalImage.Width}x{message.OriginalImage.Height} pt  -  ";
            image.Source = message.Image;

            startAnimation.Completed += (s, e) =>
            {
                if (message.LoadedOriginalImage != null)
                    image.Source = message.LoadedOriginalImage;
                else
                {
                    message.LoadedOriginalImage = new BitmapImage(message.OriginalImage.Url);
                    message.LoadedOriginalImage.DownloadCompleted += (r, a) => image.Source = message.LoadedOriginalImage;
                }
            };
        }

        void Rectangle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;

            DragMove();
        }

        void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double modifer = e.Delta / 1000.0;
            if (scale.ScaleX < 0.1 && e.Delta < 0) return;

            scale.ScaleX += modifer;
            scale.ScaleY += modifer;

            imageZoom.Text = ((int)(scale.ScaleX * 100)) + "%";
        }

        void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
            Close();

        void Border_MouseLeftButtonUp_1(object sender, MouseButtonEventArgs e)
        {
            if (WindowState == WindowState.Normal) WindowState = WindowState.Maximized;
            else WindowState = WindowState.Normal;
        }

        void pe_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
            WindowState = WindowState.Minimized;

        Point lastPoint;

        void image_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
            lastPoint = e.GetPosition(image);

        void image_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
            image.Cursor = Cursors.Arrow;

        void image_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            scale.ScaleX = scale.ScaleY = 1;
            transform.X = transform.Y = 0;
            imageZoom.Text = "100%";
        }

        void image_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && scale.ScaleX > 0.5)
            {
                image.Cursor = Cursors.SizeAll;
                Point current = e.GetPosition(image);

                transform.X += current.X - lastPoint.X;
                transform.Y += current.Y - lastPoint.Y;
            }
        }

        bool realyClose = false;

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!realyClose)
            {
                e.Cancel = true;

                PowerEase function = new PowerEase { Power = 5, EasingMode = EasingMode.EaseIn };
                TimeSpan duration = TimeSpan.FromSeconds(0.35);

                DoubleAnimation animation1 = new DoubleAnimation
                {
                    From = 1,
                    To = 0.65,
                    Duration = duration,
                    EasingFunction = function
                };

                DoubleAnimation animation2 = new DoubleAnimation
                {
                    From = 1,
                    To = 0.65,
                    Duration = duration,
                    EasingFunction = function
                };

                animation1.Completed += (s, a) =>
                {
                    realyClose = true;
                    Owner.Activate();
                    Close();
                };

                windowScale.BeginAnimation(ScaleTransform.ScaleXProperty, animation1);
                windowScale.BeginAnimation(ScaleTransform.ScaleYProperty, animation2);
            }
        }
    }
}
