using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using VkNet;
using VkNet.AudioBypassService.Extensions;
using VkNet.Enums.Filters;
using VkNet.Enums.SafetyEnums;
using VkNet.Model;
using VkNet.Model.Attachments;
using VkNet.Model.RequestParams;

namespace TrayVK
{
    public class RectConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try { return new System.Windows.Rect(0d, 0d, (double)values[0], (double)values[1]); }
            catch { return null; }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public partial class MainWindow : Window
    {
        static string trayNoLogText = "TrayVK - отсутствует вход в аккаунт";

        System.Windows.Forms.NotifyIcon notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = GetIcon(),
            Visible = true,
            Text = trayNoLogText
        };

        static System.Drawing.Icon GetIcon()
        {
            BitmapImage trayIcon = new BitmapImage(new Uri("pack://application:,,,/TrayVK;component/Resources/TrayIcon.png"));
            return System.Drawing.Icon.FromHandle(BitmapImageToBitmap(trayIcon).GetHicon());
        }

        static System.Drawing.Bitmap BitmapImageToBitmap(BitmapImage bitmapImage)
        {
            using (MemoryStream outStream = new MemoryStream())
            {
                PngBitmapEncoder enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bitmapImage));
                enc.Save(outStream);
                System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(outStream);

                return bitmap;
            }
        }

        BitmapImage exceptionIcon = new BitmapImage(new Uri("pack://application:,,,/TrayVK;component/Resources/WarningIcon.png"));

        public MainWindow()
        {
            InitializeComponent();
            ChangeSelectDialogTipVisisble(true);

            dialogsList.SelectionChanged += DialogsList_SelectionChanged;
            OnNewMessage += MainWindow_OnNewMessage;
            OnMessageDelete += MainWindow_OnMessageDelete;
            OnMessageEdit += MainWindow_OnMessageEdit;
            messageAttantments.CollectionChanged += MessageAttantments_CollectionChanged;
            OnImMessageReaded += MainWindow_OnImMessageReaded;
            scrollButton.MouseLeftButtonUp += (s, e) => ScrollMessagesToEnd();
            System.Windows.Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;
            notifyIcon.MouseClick += NotifyIcon_MouseClick;
            closeApp.MouseLeftButtonUp += (s, e) => Close();

            if (File.Exists("UserData"))
            {
                using (FileStream stream = new FileStream("UserData", FileMode.Open))
                {
                    UserData data = new BinaryFormatter().Deserialize(stream) as UserData;

                    textBox1.Text = data.Login;
                    textBox2.Text = data.Password;

                    newMessageNotifications = data.NotificationsEnabled;
                    newMessageSound = data.NotificationsSoundsEnabled;
                }
            }
        }

        void Current_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            globalLoadProgress.Visibility = Visibility.Hidden;

            NotificationMessage message = new NotificationMessage(exceptionIcon, "Ошибка во время работы", $"{e.Exception.Message}" +
                $"{Environment.NewLine}{Environment.NewLine}Кликни здесь, чтобы увидеть подробности", 4000, Colors.Tomato);

            message.NotifyClick += () => MessageBox.Show(this, e.Exception.ToString(), Title, MessageBoxButton.OK, MessageBoxImage.Error);
            message.Show();
        }

        double fixedTop = 0;
        bool autorun = false;

        [Serializable]
        class UserData
        {
            public string Login { get; set; }
            public string Password { get; set; }

            public bool NotificationsEnabled { get; set; } = true;
            public bool NotificationsSoundsEnabled { get; set; } = true;
        }

        void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (Environment.GetCommandLineArgs().Any(a => a.Contains("/autorun")))
            {
                autorun = true;
                Minimize();
            }

            UpdateLayout();
            
            Left = SystemParameters.PrimaryScreenWidth - ActualWidth;
            fixedTop = Top = SystemParameters.WorkArea.Height - ActualHeight;

            mainGrid.Width = ActualWidth;
            mainGrid.Height = ActualHeight;

            window_Activated(null, null);
            newMessagePlayer.Open(new Uri("NewMessageSound.mp3", UriKind.Relative));
        }

        protected override void OnContentRendered(EventArgs e)
        {
            if (autorun && !string.IsNullOrEmpty(textBox1.Text) && !string.IsNullOrEmpty(textBox2.Text))
            {
                autorun = false;
                Button_Click(null, null);
            }

            base.OnContentRendered(e);
        }

        static VkApi vk = null;
        int logginTries = 2;
        DateTime logginDate = DateTime.MinValue;

        async void Button_Click(object sender, RoutedEventArgs e)
        {
            globalLoadProgress.Visibility = Visibility.Visible;
            logginTries--;

            try
            {
                var services = new ServiceCollection();
                services.AddAudioBypass();

                vk = new VkApi(services);
                await vk.AuthorizeAsync(new ApiAuthParams()
                {
                    Login = textBox1.Text,
                    Password = textBox2.Text,
                    ApplicationId = 8220220,
                    Settings = Settings.All
                });

                VisualMessage.AccoundId = vk.UserId;
                StartMessagesHandling();

                Button_Click_1(null, null);
                dialogsTab.IsSelected = true;
                UpdateNoLoginGridVisisble();
                logginTries = 2;

                AccountSaveProfileInfoParams info = await vk.Account.GetProfileInfoAsync();
                userName.Text = $"{info.FirstName} {info.LastName}";
                logginDate = DateTime.Now;
                notifyIcon.Text = $"TrayVK - профиль: {userName.Text}";

                ReadOnlyCollection<User> users = await vk.Users.GetAsync(new List<long> { (long)vk.UserId }, ProfileFields.Photo50);
                userPicture.ImageSource = new BitmapImage(users[0].Photo50);
            }
            catch (Exception ex)
            {
                if (logginTries > 0 && !(ex is VkNet.AudioBypassService.Exceptions.VkAuthException))
                    Button_Click(null, null);

                else
                {
                    globalLoadProgress.Visibility = Visibility.Hidden;
                    logginTries = 2;
                    new NotificationMessage(exceptionIcon, "Не удалось войти", ex.Message, 3000, Colors.Tomato).Show();
                }
            }
        }

        void sendText_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.Return) && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                Button_Click_2(null, null);
        }

        int messagesQuequeCount = 0;
        bool sendingFilesNow = false;

        async void Button_Click_2(object sender, RoutedEventArgs e)
        {
            if (dialogsList.SelectedItem != null && messagesList.Items != null &&
                (!string.IsNullOrWhiteSpace(sendText.Text) || messageAttantments.Count > 0))
            {
                try
                {
                    messagesQuequeCount++;
                    messageSendProgress.Visibility = Visibility.Visible;

                    string message = sendText.Text;
                    sendText.Text = string.Empty;
                    attachmentsButton.Visibility = Visibility.Collapsed;

                    List<MediaAttachment> attachment = new List<MediaAttachment>();
                    long id = (dialogsList.SelectedItem as VisualUser).Source.Id;

                    if (messageAttantments.Count > 0)
                        sendingFilesNow = true;

                    foreach (var item in messageAttantments)
                    {
                        UploadServerInfo uploadServer = await vk.Docs.GetMessagesUploadServerAsync(id);
                        string response = await UploadFile(uploadServer.UploadUrl, item.FileName, item.Extension.ToLower(), item.Bytes);
                        attachment.Add((await vk.Docs.SaveAsync(response, item.Source != null ? item.Source.Name : item.FileName, null))[0].Instance);
                    }

                    sendingFilesNow = false;
                    messageAttantments.Clear();

                    long messageId = await vk.Messages.SendAsync(new MessagesSendParams
                    {
                        UserId = id,
                        Message = message,
                        Attachments = attachment,
                        RandomId = new Random().Next(999999)
                    });

                    Message newMessage = (await vk.Messages.GetByIdAsync(new List<ulong> { (ulong)messageId }, null)).First();
                    MainWindow_OnNewMessage(newMessage);
                }
                catch (Exception ex)
                {
                    new NotificationMessage(exceptionIcon, "Не удалось отправить сообщение",
                        ex.Message, 3000, Colors.Tomato).Show();
                }

                messagesQuequeCount--;

                if (messagesQuequeCount <= 0)
                    messageSendProgress.Visibility = Visibility.Collapsed;
            }
        }

        async Task<string> UploadFile(string serverUrl, string file, string fileExtension, byte[] bytes)
        {
            using (var client = new HttpClient())
            {
                MultipartFormDataContent requestContent = new MultipartFormDataContent();
                var content = new ByteArrayContent(bytes == null ? File.ReadAllBytes(file) : bytes);
                content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");
                requestContent.Add(content, "file", $"file.{fileExtension}");

                var response = await client.PostAsync(serverUrl, requestContent);
                return Encoding.Default.GetString(await response.Content.ReadAsByteArrayAsync());
            }
        }

        public class CustomFileInfo
        {
            public string FileName { get; set; }
            public string Extension { get; set; }
            public FileInfo Source { get; set; }
            public byte[] Bytes { get; set; }
        }

        ObservableCollection<CustomFileInfo> messageAttantments = new ObservableCollection<CustomFileInfo>();

        void MessageAttantments_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            attachmentCount.Text = messageAttantments.Count + " файлов";
            attachmentsButton.Visibility = messageAttantments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void Button_Click_3(object sender, RoutedEventArgs e)
        {
            System.Windows.Forms.OpenFileDialog dialog =
                new System.Windows.Forms.OpenFileDialog { Multiselect = true };

            dialogNow = true;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                AddMessageAttachments(dialog.FileNames);

            dialogNow = false;
        }

        void AddMessageAttachments(string[] files)
        {
            if (!sendingFilesNow)
            {
                foreach (var item in files.Where(w => !Directory.Exists(w)).Select(s => new FileInfo(s)))
                {
                    if (!string.IsNullOrEmpty(item.Extension))
                    {
                        string ext = item.Extension.StartsWith(".") ? item.Extension.Substring(1).ToUpper() : item.Extension.ToUpper();
                        messageAttantments.Add(new CustomFileInfo { FileName = item.FullName, Extension = ext, Source = item });
                    }
                }
            }
        }

        #region Events

        void MainWindow_OnMessageEdit(Message message)
        {
            Dispatcher.Invoke(() =>
            {
                var messages = dialogs.First(f => f.Key.Id == message.PeerId).Value;
                int index = messages.IndexOf(messages.First(f => f.Source.Id == message.Id));

                RemoveMessage(message);
                var newMessages = VisualMessage.GetVisualMessages(message);

                for (int i = 0; i < newMessages.Count; index++, i++)
                    messages.Insert(index, newMessages[i]);

                ScrollMessagesToEnd(message);
            });
        }

        void MainWindow_OnMessageDelete(Message message) =>
            Dispatcher.Invoke(() => RemoveMessage(message));

        void RemoveMessage(Message message)
        {
            foreach (var item in dialogs.Values)
            {
                int c = ObservableRangeCollection<VisualMessage>.RemoveAll(item, r => r.Source.Id == message.Id);
                if (c > 0) break;
            }
        }

        object lastSelection;

        async void DialogsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (profileClicked)
            {
                dialogsList.SelectedItem = lastSelection;
                profileClicked = false;
                return;
            }

            lastSelection = dialogsList.SelectedItem;
            allowShowScrollButton = false;

            if (dialogsList.SelectedItem != null && dialogsList.Items != null && dialogsList.Items.Count > 0 && dialogs.Any())
            {
                var dialog = dialogs.First(f => f.Key == (dialogsList.SelectedItem as VisualUser).Source);
                if (messagesList.ItemsSource == null) messagesList.Items.Clear();

                await Dispatcher.InvokeAsync(() =>
                {
                    messagesList.ItemsSource = dialog.Value;
                    ScrollMessagesToEnd();
                });
            }

            ChangeSelectDialogTipVisisble(dialogsList.SelectedItem == null);
            allowShowScrollButton = true;
        }

        void ScrollMessagesToEnd(Message message = null)
        {
            if (dialogsList.Items.Count > 0 && messagesList.Items.Count > 0)
            {
                if (message != null && (dialogsList.SelectedItem as VisualUser).Source != dialogs.First(f => f.Value.Any(a => a.Source == message)).Key)
                    return;

                messagesList.ScrollIntoView(messagesList.Items[messagesList.Items.Count - 1]);
            }
        }

        class IncomingMessage
        {
            public Message Message { get; set; }
            public bool Readed { get; set; }
        }

        List<IncomingMessage> incomingMessages = new List<IncomingMessage>();
        VisualUser windowShownDialogSelection = null;
        MediaPlayer newMessagePlayer = new MediaPlayer();

        void MainWindow_OnNewMessage(Message message)
        {
            Dispatcher.Invoke(async () =>
            {
                var pair = dialogs.First(f => f.Key.Id == message.PeerId);
                allowShowScrollButton = false;

                if (!pair.Value.Any(a => a.Source.Id == message.Id))
                {
                    pair.Value.AddRange(VisualMessage.GetVisualMessages(message));
                    ScrollMessagesToEnd(message);

                    bool showNotify = dialogsList.SelectedItem != null;
                    if (showNotify && (dialogsList.SelectedItem as VisualUser).Source == pair.Key) showNotify = false;

                    if (WindowState == WindowState.Minimized)
                        showNotify = true;

                    if (newMessageNotifications && message.FromId != vk.UserId && showNotify)
                    {
                        IncomingMessage last = new IncomingMessage { Message = message };
                        incomingMessages.Add(last);

                        await Task.Run(() => Thread.Sleep(1000));
                        incomingMessages.Remove(last);

                        if (!last.Readed)
                        {
                            NotificationMessage notify = new NotificationMessage(new BitmapImage(pair.Key.Photo50),
                                $"{pair.Key.FirstName} {pair.Key.LastName}", GetMessageTextPreview(message), 4000);

                            notify.NotifyClick += () =>
                            {
                                windowShownDialogSelection = users.First(f => f.Source == pair.Key);
                                WindowState = WindowState.Normal;
                            };

                            if (newMessageSound)
                            {
                                newMessagePlayer.Position = TimeSpan.Zero;
                                newMessagePlayer.Play();
                            }

                            notify.Show();
                        }
                    }
                }

                allowShowScrollButton = true;
            });
        }

        void MainWindow_OnImMessageReaded(long arg1, long messageId)
        {
            foreach (var item in incomingMessages)
            {
                if (item.Message.Id <= messageId)
                    item.Readed = true;
            }
        }

        string GetMessageTextPreview(Message message)
        {
            if (message.Attachments == null || !message.Attachments.Any()) return message.Text;
            else if (message.Attachments.All(a => a.Instance is Sticker)) return "Стикер";
            else if (message.Attachments.All(a => a.Instance is AudioMessage)) return "Голосовое сообщение";
            else if (message.Attachments.All(a => a.Instance is Wall)) return "Запись со стены";
            else if (message.Attachments.All(a => a.Instance is Call)) return "Звонок";
            else return $"{message.Attachments.Count} медиа-вложений";
        }

        #endregion

        public class VisualMessage
        {
            readonly Size PreferredImageSize = new Size(170, 230);
            public static long? AccoundId;
            public Message Source { get; set; }

            public string Text { get; set; }
            public Brush TextBrush { get; set; } = Brushes.White;
            public HorizontalAlignment Alignment { get; set; }
            public DateTime Time { get; set; }
            public string TimeFormat { get; set; } = "HH:mm";
            public Brush BackBrush { get; set; }
            public TextDecorationCollection Decorations { get; set; } = new TextDecorationCollection();

            public Visibility TextBoxVisible { get; set; } = Visibility.Collapsed;
            public BitmapImage Image { get; set; }
            public Visibility VideoPlayButtonVisible { get; set; } = Visibility.Collapsed;

            public Document DocumentToDownload { get; set; }
            public Visibility DocumentVisible { get; set; } = Visibility.Collapsed;
            public Visibility DocumentStarIconVisible { get; set; } = Visibility.Collapsed;
            public string DocumentName { get; set; }
            public string DocumentExt { get; set; }
            public string DocumentInfo { get; set; }

            public bool OpenInImageViewer { get; set; }
            public PhotoSize OriginalImage { get; set; }
            public BitmapImage LoadedOriginalImage { get; set; }

            public string ClickUrl { get; set; }
            public Visibility SeparatorVisisble { get; set; } = Visibility.Collapsed;

            public bool DeleteEnabled { get; set; } = true;
            public double DeleteOpacity { get; set; } = 1;

            public VisualMessage(Message message, Attachment attachment = null)
            {
                Source = message;

                if (attachment != null)
                {
                    if (attachment.Type == typeof(Photo))
                    {
                        ReadOnlyCollection<PhotoSize> sizes = (attachment.Instance as Photo).Sizes;

                        PhotoSize preview = sizes.OrderBy(ob => Math.Abs(ob.Height - PreferredImageSize.Height)).
                            ThenBy(tb => Math.Abs(tb.Width - PreferredImageSize.Width)).First();

                        OriginalImage = sizes.OrderBy(ob => ob.Width).ThenBy(th => th.Height).Last();
                        Image = new BitmapImage(preview.Url);
                        OpenInImageViewer = true;
                        ClickUrl = "0";
                    }
                    else if (attachment.Type == typeof(Video))
                    {
                        Video video = attachment.Instance as Video;
                        List<VideoImage> previews = video.Image.ToList();

                        VideoImage preview = previews.OrderBy(ob => Math.Abs(ob.Height - PreferredImageSize.Height)).
                            ThenBy(tb => Math.Abs(tb.Width - PreferredImageSize.Width)).First();

                        Image = new BitmapImage(preview.Url);
                        VideoPlayButtonVisible = Visibility.Visible;
                        ClickUrl = video.Player.AbsoluteUri;
                    }
                    else if (attachment.Type == typeof(Wall))
                    {
                        Wall wall = attachment.Instance as Wall;

                        DateTime date = ((DateTime)wall.Date).ToLocalTime();
                        string yearFormat = date.Year == DateTime.Now.Year ? string.Empty : " yyyy";

                        ClickUrl = $"https://vk.com/im?sel={message.PeerId}&w=wall{wall.FromId}_{wall.Id}";
                        AddMessageText(new Tuple<string, bool, Brush>("Запись со стены за " + date.ToString($"dd MMM{yearFormat} HH:mm"), true, null));
                    }
                    else if (attachment.Type == typeof(AudioMessage))
                    {
                        AudioMessage audio = attachment.Instance as AudioMessage;
                        string text = $"Голосовое сообщение ({new DateTime((long)(audio.Duration * TimeSpan.TicksPerSecond)):m:ss})";
                        if (!string.IsNullOrEmpty(audio.Transcript)) text += $":{Environment.NewLine}{audio.Transcript}";
                        AddMessageText(new Tuple<string, bool, Brush>(text, false, null));
                    }
                    else if (attachment.Type == typeof(Document))
                    {
                        DocumentVisible = Visibility.Visible;
                        Document document = attachment.Instance as Document;

                        DocumentExt = document.Ext.ToUpper();
                        DocumentName = document.Title.Replace("." + document.Ext, string.Empty);
                        DocumentInfo = $"{document.Type}  ·  {BytesToString((long)document.Size)}";
                        DocumentToDownload = document;
                    }
                    else if (attachment.Type == typeof(Call))
                    {
                        Call call = attachment.Instance as Call;

                        AddMessageText(new Tuple<string, bool, Brush>($"{(call.Video == true ? "Видеозвонок" : "Аудиозвонок")} " +
                            $"({call.Duration} сек)", true, null));
                    }
                    else if (attachment.Type == typeof(Sticker))
                    {
                        List<VkNet.Model.Image> stickers = (attachment.Instance as Sticker).Images.ToList();

                        VkNet.Model.Image sticker = stickers.OrderBy(ob => Math.Abs(ob.Height - PreferredImageSize.Height)).
                            ThenBy(tb => Math.Abs(tb.Width - PreferredImageSize.Width)).First();

                        Image = new BitmapImage(sticker.Url);
                    }
                    else if (attachment.Type != typeof(Link))
                    {
                        DocumentVisible = DocumentStarIconVisible = Visibility.Visible;
                        DocumentName = "Медиа-вложение";
                        DocumentInfo = attachment.Type.Name.ToUpper();
                    }
                }
                else
                    AddMessageText(GetMessageText(message));

                void AddMessageText(Tuple<string, bool, Brush> data)
                {
                    TextBoxVisible = Visibility.Visible;
                    Text = data.Item1;

                    Time = (message.UpdateTime != null ? (DateTime)message.UpdateTime :
                        (DateTime)message.Date).ToLocalTime();

                    if (message.UpdateTime != null)
                        TimeFormat = TimeFormat.Insert(0, "РЕД  ");

                    if (data.Item3 != null)
                        TextBrush = data.Item3;

                    if (data.Item2)
                    {
                        Brush underlineBrush = TextBrush.Clone();
                        underlineBrush.Opacity = 0.3;

                        Decorations.Add(new TextDecoration
                        {
                            Location = TextDecorationLocation.Underline,
                            Pen = new Pen(underlineBrush, 1)
                        });
                    }
                }

                if (message.FromId == AccoundId)
                {
                    Alignment = HorizontalAlignment.Right;
                    if (Image == null) BackBrush = new SolidColorBrush(Color.FromRgb(0, 112, 192)) { Opacity = 0.2 };
                }
                else
                {
                    Alignment = HorizontalAlignment.Left;
                    if (Image == null) BackBrush = new SolidColorBrush(Colors.White) { Opacity = 0.05 };

                    DeleteEnabled = false;
                    DeleteOpacity = 0.3;
                }

                if (string.IsNullOrEmpty(ClickUrl))
                    ClickUrl = $"https://vk.com/im?sel={message.PeerId}&msgid={message.Id}";
            }

            Tuple<string, bool, Brush> GetMessageText(Message message)
            {
                string text = message.Text;
                bool underLine = false;
                Brush brush = null;
                int lettersOrDigist = text.Count(c => char.IsLetterOrDigit(c));

                if (lettersOrDigist < text.Length && lettersOrDigist > 0 && Uri.IsWellFormedUriString(text, UriKind.RelativeOrAbsolute))
                {
                    brush = new SolidColorBrush(Color.FromRgb(148, 216, 246));
                    ClickUrl = message.Text;
                    underLine = true;
                }
                if (message.ReplyMessage != null)
                {
                    text = $"Ответ на {(message.ReplyMessage.FromId == AccoundId ? "ваше " : string.Empty)}сообщение";
                    underLine = true;
                }
                else if (message.ForwardedMessages != null && message.ForwardedMessages.Any())
                {
                    text = "Пересланные сообщения: " + message.ForwardedMessages.Count;
                    underLine = true;
                }

                return new Tuple<string, bool, Brush>(text, underLine, brush);
            }

            public static List<VisualMessage> GetVisualMessages(Message message)
            {
                List<VisualMessage> messages = new List<VisualMessage>();

                if (message.Attachments.Count == 0)
                    messages.Add(new VisualMessage(message));
                else
                {
                    messages = message.Attachments.Select(s => new VisualMessage(message, s)).ToList();
                    if (!string.IsNullOrEmpty(message.Text)) messages.Insert(0, new VisualMessage(message));
                }

                return messages;
            }
        }

        Dictionary<User, ObservableRangeCollection<VisualMessage>> dialogs =
            new Dictionary<User, ObservableRangeCollection<VisualMessage>>();

        class VisualUser
        {
            public User Source { get; set; }
            public string Name { get; set; }
            public BitmapImage Photo { get; set; }
            public string LastMessage { get; set; }
        }

        ObservableCollection<VisualUser> users = new ObservableCollection<VisualUser>();
        object syncLock = new object();

        async void Button_Click_1(object sender, RoutedEventArgs e)
        {
            globalLoadProgress.Visibility = Visibility.Visible;
            if (sidePanel.Width > 0) Border_MouseLeftButtonUp_2(null, null);

            GetConversationsResult result = await vk.Messages.GetConversationsAsync(new GetConversationsParams { Count = 6, Extended = true });
            var conv = result.Items.Select(s => s.Conversation).Where(w => w.Peer.Type == ConversationPeerType.User).ToList();

            users.Clear();
            dialogs.Values.ToList().ForEach(f => f.Clear());
            dialogs.Clear();

            if (dialogsList.ItemsSource == null)
                dialogsList.ItemsSource = users;

            foreach (var item in conv)
            {
                User user = result.Profiles.FirstOrDefault(a => a.Id == item.Peer.Id);
                if (user == null || dialogsList.Items.OfType<VisualUser>().Any(a => a.Source.Id == user.Id)) continue;

                MessageGetHistoryObject history = await vk.Messages.GetHistoryAsync
                    (new MessagesGetHistoryParams { Count = 35, UserId = user.Id });

                var messages = new ObservableRangeCollection<VisualMessage>(history.Messages.Reverse().
                    SelectMany(sm => VisualMessage.GetVisualMessages(sm)));

                messages.CollectionChanged += Value_CollectionChanged;
                string lastMessage = GetVisualUserLastMessageText(messages);

                users.Add(new VisualUser { Source = user, Name = $"{user.FirstName} {user.LastName}",
                    Photo = new BitmapImage(user.Photo50), LastMessage = lastMessage });

                dialogs.Add(user, messages);
                globalLoadProgress.Visibility = Visibility.Hidden;

                BindingOperations.EnableCollectionSynchronization(messages, syncLock);
            }

            foreach (var item in dialogs.Values)
                UpdateMessagesDateSeparators(item);
        }

        void Value_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            try
            {
                var dialog = dialogs.First(fd => fd.Value == sender);
                VisualUser user = users.First(f => f.Source == dialog.Key);

                user.LastMessage = GetVisualUserLastMessageText(dialog.Value);
                dialogsList.Items.Refresh();

                UpdateMessagesDateSeparators(dialog.Value);
            }
            catch { }
        }

        void UpdateMessagesDateSeparators(ObservableRangeCollection<VisualMessage> messages)
        {
            for (int i = 1; i < messages.Count; i++)
            {
                Message m1 = messages[i].Source;
                Message m2 = messages[i - 1].Source;

                if (m1.Date.HasValue && m2.Date.HasValue && messages[i].Time != DateTime.MinValue)
                {
                    if (m1.Date.Value.Day != m2.Date.Value.Day)
                        messages[i].SeparatorVisisble = Visibility.Visible;
                }
            }

            messagesList.Items.Refresh();
        }

        string GetVisualUserLastMessageText(ObservableRangeCollection<VisualMessage> messages)
        {
            Message message = messages.Last().Source;
            string from = message.FromId == vk.UserId ? "вы: " : string.Empty;

            return from + Regex.Replace(GetMessageTextPreview(message),
                @"\t|\n|\r", string.Empty);
        }

        #region MessagesHandler

        ulong ts;
        ulong? pts;

        async void StartMessagesHandling()
        {
            LongPollServerResponse longPoolServerResponse = await vk.Messages.GetLongPollServerAsync(needPts: true);
            ts = Convert.ToUInt64(longPoolServerResponse.Ts);
            pts = longPoolServerResponse.Pts;

            new Thread(LongPollEventLoop).Start();
        }

        event Action<Message> OnNewMessage;
        const long NEW_MESSAGE = 4;

        event Action<Message> OnMessageEdit;
        const long MESSAGE_EDIT = 5;

        event Action<Message> OnMessageDelete;
        const long MESSAGE_DELETE = 2;

        event Action<long, long> OnMessageReaded;
        event Action<long, long> OnImMessageReaded;

        const long IM_MESSAGE_READ = 6;
        const long PEER_MESSAGE_READ = 7;

        void LongPollEventLoop()
        {
            while (true)
            {
                try
                {
                    LongPollHistoryResponse longPollResponse =
                        vk.Messages.GetLongPollHistory(new MessagesGetLongPollHistoryParams() { Ts = ts, Pts = pts, });

                    pts = longPollResponse.NewPts;

                    for (int i = 0; i < longPollResponse.History.Count; i++)
                    {
                        if (longPollResponse.History[i][0] == NEW_MESSAGE)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (messagesQuequeCount > 0 && longPollResponse.Messages[i].FromId == vk.UserId) return;
                                OnNewMessage?.Invoke(longPollResponse.Messages[i]);
                            });
                        }

                        else if (longPollResponse.History[i][0] == MESSAGE_EDIT)
                            OnMessageEdit?.Invoke(longPollResponse.Messages[i]);

                        else if (longPollResponse.History[i][0] == MESSAGE_DELETE)
                            OnMessageDelete?.Invoke(longPollResponse.Messages[i]);

                        else if (longPollResponse.History[i][0] == PEER_MESSAGE_READ)
                            OnMessageReaded?.Invoke(longPollResponse.History[i][2], longPollResponse.History[i][2]);

                        else if (longPollResponse.History[i][0] == IM_MESSAGE_READ)
                            OnImMessageReaded?.Invoke(longPollResponse.History[i][2], longPollResponse.History[i][2]);
                    }

                    Thread.Sleep(50);
                }
                catch { }
            }
        }

        #endregion

        void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                using (FileStream stream = new FileStream("UserData", FileMode.Create))
                {
                    new BinaryFormatter().Serialize(stream, new UserData
                    {
                        Login = textBox1.Text,
                        Password = textBox2.Text,
                        NotificationsEnabled = newMessageNotifications,
                        NotificationsSoundsEnabled = newMessageSound
                    });
                }
            }
            catch { }

            notifyIcon.Icon = null;
            e.Cancel = true;
            Process.GetCurrentProcess().Kill();
        }

        void messagesList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            messagesList.SelectedItem = null;

        static string BytesToString(long byteCount)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            if (byteCount == 0) return "0" + suf[0];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return (Math.Sign(byteCount) * num).ToString() + suf[place];
        }

        void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            grid.Opacity = 0.2;
            new AttachmentsWindow(messageAttantments) { Owner = this }.ShowDialog();
            grid.Opacity = 1;
        }

        #region DragAndDrop

        void messagesList_DragOver(object sender, DragEventArgs e) =>
            dragAndDropZone.Visibility = Visibility.Visible;

        void messagesList_DragLeave(object sender, DragEventArgs e)
        {
            var bounds = new System.Windows.Rect(dragAndDropZone.PointToScreen(new Point(0, 0)),
                dragAndDropZone.RenderSize);

            var point = new Point(System.Windows.Forms.Control.MousePosition.X,
                System.Windows.Forms.Control.MousePosition.Y);

            if (!bounds.Contains(point))
                dragAndDropZone.Visibility = Visibility.Collapsed;
        }

        void messagesList_Drop(object sender, DragEventArgs e)
        {
            dragAndDropZone.Visibility = Visibility.Collapsed;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                AddMessageAttachments(files);
            }
        }

        void dragAndDropZone_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            messagesList.Opacity = dragAndDropZone.IsVisible ? 0.3 : 1;
            messagesBlur.Radius = dragAndDropZone.IsVisible ? 5 : 0;
        }

        #endregion

        void Border_MouseLeftButtonUp_1(object sender, MouseButtonEventArgs e)
        {
            VisualMessage source = (sender as FrameworkElement).DataContext as VisualMessage;

            if (source.OpenInImageViewer)
                new ImageViewer(source){ Owner = this }.Show();

            else if (source.DocumentToDownload != null)
            {
                System.Windows.Forms.SaveFileDialog dialog = new System.Windows.Forms.SaveFileDialog
                {
                    FileName = source.DocumentToDownload.Title,
                    Filter = $"|*.{source.DocumentToDownload.Ext.ToLower()}"
                };

                dialogNow = true;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    DownloadDocument(source.DocumentToDownload.Uri, dialog.FileName);

                dialogNow = false;
            }
            else
            {
                string link = source.ClickUrl;
                Process.Start(link.ToLower().StartsWith("http") ? link : "https://" + link);
                Minimize();
            }
        }

        void DownloadDocument(string url, string fileName)
        {
            try
            {
                using (WebClient web = new WebClient())
                {
                    web.DownloadFileAsync(new Uri(url), fileName);
                    web.DownloadFileCompleted += (s, e) =>
                    {
                        BitmapImage image = new BitmapImage(new Uri("pack://application:,,,/TrayVK;component/Resources/DownloadCompleteIcon.png"));
                        NotificationMessage notify = new NotificationMessage(image, "Загрузка завершена", $"Файл \"" +
                            $"{Path.GetFileName(fileName)}\" скачан. Кликни здесь чтобы найти его в проводнике", 4500, Color.FromRgb(0, 112, 192));

                        notify.NotifyClick += () => Process.Start("explorer", "/select, \"" + fileName + "\"");
                        notify.Show();
                    };
                }
            }
            catch (Exception ex)
            {
                new NotificationMessage(exceptionIcon, "Ошибка при скачивании",
                    ex.Message, 2500, Colors.Gold).Show();
            }
        }

        bool profileClicked = false;

        void Ellipse_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            profileClicked = true;  
            VisualUser user = (sender as FrameworkElement).DataContext as VisualUser;
            Process.Start("https://vk.com/id" + user.Source.Id);
        }

        bool shown = false;

        void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource is TabControl && shown)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    TabControl tabControl = e.OriginalSource as TabControl;
                    FrameworkElement content = tabControl.SelectedContent as FrameworkElement;
                    content.RenderTransformOrigin = new Point(0.5, 0);
                    ScaleTransform scale;

                    if (content.RenderTransform.Value == Matrix.Identity) content.RenderTransform = scale = new ScaleTransform();
                    else scale = content.RenderTransform as ScaleTransform;

                    if ((tabControl.SelectedItem as TabItem).Name == "dialogsTab")
                        UpdateNoLoginGridVisisble();

                    DoubleAnimation animation1 = new DoubleAnimation
                    {
                        From = 0.9,
                        To = 1,
                        Duration = TimeSpan.FromSeconds(0.5),
                        EasingFunction = new PowerEase { Power = 5 }
                    };

                    DoubleAnimation animation2 = new DoubleAnimation
                    {
                        From = 0.9,
                        To = 1,
                        Duration = TimeSpan.FromSeconds(0.5),
                        EasingFunction = new PowerEase { Power = 5 }
                    };

                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation1);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation2);
                }));
            }

            shown = true;
        }

        void UpdateNoLoginGridVisisble()
        {
            grid.Visibility = (vk != null && vk.IsAuthorized) ? Visibility.Visible : Visibility.Collapsed;
            noLiginGrid.Visibility = grid.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (messagesList.IsVisible && messagesList.Items.Count > 0 && e.Key == Key.V
                && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {

                if (Clipboard.ContainsImage())
                {
                    BitmapSource image = Clipboard.GetImage();
                    byte[] bytes = BitmapSourceToBytes(image);

                    messageAttantments.Add(new CustomFileInfo
                    {
                        FileName = $"Изображение {image.PixelWidth}x{image.PixelHeight}",
                        Extension = "PNG",
                        Bytes = bytes
                    });
                }
                else if (Clipboard.ContainsFileDropList())
                    AddMessageAttachments(Clipboard.GetFileDropList().OfType<string>().ToArray());
            }
        }

        byte[] BitmapSourceToBytes(BitmapSource source)
        {
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            using (MemoryStream stream = new MemoryStream())
            {
                encoder.Frames.Add(BitmapFrame.Create(source));
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        bool allowShowScrollButton = true;

        void messagesList_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            double scroll = (e.OriginalSource as ScrollViewer).ScrollableHeight - e.VerticalOffset;

            if (scroll > 30)
            {
                if (scrollButton.Margin.Top > 0 && allowShowScrollButton)
                {
                    scrollButton.BeginAnimation(MarginProperty, new ThicknessAnimation
                    {
                        To = new Thickness(),
                        Duration = TimeSpan.FromSeconds(0.5),
                        EasingFunction = new PowerEase { Power = 8 }
                    });
                }
            }
            else
            {
                if (scrollButton.Margin.Top < 45)
                {
                    scrollButton.BeginAnimation(MarginProperty, new ThicknessAnimation
                    {
                        To = new Thickness(0, 45, 0, 0),
                        Duration = TimeSpan.FromSeconds(0.5),
                        EasingFunction = new PowerEase { Power = 8 }
                    });
                }
            }
        }

        void ChangeSelectDialogTipVisisble(bool visible)
        {
            foreach (UIElement item in grid.Children)
            {
                if (Grid.GetColumn(item) == 1 && item != dragAndDropZone && item != messageSendProgress)
                    item.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            }

            dialogTip.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        void dialogsList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            object data = (e.OriginalSource as FrameworkElement).DataContext;
            if (data == null) dialogsList.SelectedItem = null;
        }

        bool aboutMessageNow = false;

        void TextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!aboutMessageNow)
            {
                BitmapImage image = new BitmapImage(new Uri("pack://application:,,,/TrayVK;component/Resources/InfoIcon.png"));

                NotificationMessage about = new NotificationMessage(image, "О программе", "TrayVK - клиентское приложение ВК для Windows. " +
                    "Для работы используется VK Api. Разработчик - Рыжкин Максим (QR Filing).", 8000, Color.FromRgb(0, 112, 192));

                about.Closed += (s, a) => aboutMessageNow = false;
                aboutMessageNow = true;
                about.Show();
            }
        }

        void Border_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            Border border = sender as Border;
            (border.Tag as Popup).IsOpen = true;
        }

        bool activateAnimation = true;
        bool dialogNow = false;

        void window_Activated(object sender, EventArgs e)
        {
            if (notificationClosed)
            {
                notificationClosed = false;
                return;
            }
            
            if (fixedTop != 0 && !ChildWindowOpen() && !deactivateEventNow && activateAnimation && !dialogNow)
            {
                DoubleAnimation animation = new DoubleAnimation
                {
                    From = 40,
                    To = 0,
                    Duration = TimeSpan.FromSeconds(0.7),
                    EasingFunction = new PowerEase { Power = 5 }
                };

                activateAnimation = false;
                animation.Completed += (s, a) =>
                {
                    Activate();
                    activateAnimation = true;

                    if (windowShownDialogSelection != null)
                    {
                        dialogsList.SelectedItem = windowShownDialogSelection;
                        windowShownDialogSelection = null;
                    }
                };

                translate.BeginAnimation(TranslateTransform.YProperty, animation);
            }
        }

        bool deactivateEventNow = false;
        public DateTime closeTime = DateTime.MinValue;
        bool notificationClosed = false;

        async void window_Deactivated(object sender, EventArgs e)
        {
            var nonifications = System.Windows.Application.Current.Windows.OfType<NotificationMessage>();
            if (nonifications.Count() > 0) await Task.Delay(100);

            if (nonifications.Any(a => a.closeClick))
            {
                notificationClosed = true;
                return;
            }

            if (!deactivateEventNow && !ChildWindowOpen() && !dialogNow)
            {
                deactivateEventNow = true;

                await Task.Run(() =>
                    { while (System.Windows.Forms.Control.MouseButtons != System.Windows.Forms.MouseButtons.None) { } });

                System.Windows.Rect rect = new System.Windows.Rect(Left, Top, ActualWidth, ActualHeight);
                var point = System.Windows.Forms.Control.MousePosition;

                if (!rect.Contains(new Point(point.X, point.Y))) Minimize();
                else Activate();

                deactivateEventNow = false;
            }
        }

        async void Minimize()
        {
            ShowInTaskbar = true;
            WindowState = WindowState.Minimized;
            closeTime = DateTime.Now;
            ShowInTaskbar = false;

            if (sidePanel.Width > 0)
                Border_MouseLeftButtonUp_2(null, null);

            if ((DateTime.Now - closeTime).TotalSeconds > 1)
                await vk.Account.SetOfflineAsync();
        }

        bool ChildWindowOpen()
        {
            return System.Windows.Application.Current.Windows.OfType<Window>().Where
                (w => w != this && !(w is NotificationMessage)).Count() > 0;
        }

        void window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
            else if (WindowState == WindowState.Normal)
            {
                foreach (var item in System.Windows.Application.Current.Windows.OfType<NotificationMessage>())
                    item.Hide();
            }
        }

        void NotifyIcon_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                if ((DateTime.Now - closeTime).TotalMilliseconds > 100)
                    WindowState = WindowState.Normal;
            }
            else if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                System.Drawing.Point mouse = System.Windows.Forms.Control.MousePosition;
                notifyMenu.IsOpen = true;

                notifyMenu.HorizontalOffset = mouse.X;
                notifyMenu.VerticalOffset = mouse.Y - 65;

                NotifyMenuCloser();
                notifyMenu.Focus();
            }
        }

        async void NotifyMenuCloser()
        {
            if (System.Windows.Forms.Control.MouseButtons != System.Windows.Forms.MouseButtons.None)
            {
                System.Drawing.Point mouse = System.Windows.Forms.Control.MousePosition;
                FrameworkElement element = VisualTreeHelper.GetChild(notifyMenu.Child, 0) as FrameworkElement;

                System.Windows.Rect bounds = new System.Windows.Rect(notifyMenu.HorizontalOffset,
                    notifyMenu.VerticalOffset - 30, element.ActualWidth, element.ActualHeight);

                if (!bounds.Contains(new Point(mouse.X, mouse.Y)))
                    notifyMenu.IsOpen = false;
            }

            if (notifyMenu.IsOpen)
            {
                await Task.Delay(1);
                NotifyMenuCloser();
            }
        }

        void StackPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            int index = Convert.ToInt32(((sender as FrameworkElement).DataContext as MenuItem).Tag);
            notifyMenu.IsOpen = false;

            if (index == 0) WindowState = WindowState.Normal;
            else if (index == 1) Process.Start("https://vk.com/im");
            else if (index == 2) Close();
        }

        void StackPanel_MouseLeftButtonUp_1(object sender, MouseButtonEventArgs e)
        {
            FrameworkElement element = sender as FrameworkElement;
            Popup popup = GetParentOfType<Popup>(element);

            int index = Convert.ToInt32((element.DataContext as MenuItem).Tag);
            popup.IsOpen = false;

            if (index == 0)
                Border_MouseLeftButtonUp_1(popup, null);

            else if (index == 1)
            {
                VisualMessage message = popup.DataContext as VisualMessage;

                vk.Messages.DeleteAsync(new List<ulong> { (ulong)message.Source.Id },
                    deleteForAll: (DateTime.Now - message.Time).TotalHours < 24);
            }
        }

        T GetParentOfType<T>(DependencyObject element) where T : DependencyObject
        {
            Type type = typeof(T);
            if (element == null) return null;
            DependencyObject parent = VisualTreeHelper.GetParent(element);
            if (parent == null && ((FrameworkElement)element).Parent is DependencyObject)
                parent = ((FrameworkElement)element).Parent;
            if (parent == null) return null;
            else if (parent.GetType() == type || parent.GetType().IsSubclassOf(type))
                return parent as T;
            return GetParentOfType<T>(parent);
        }

        void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ListBox listBox = sender as ListBox;
            listBox.SelectedItem = null;
        }

        async void Border_MouseLeftButtonUp_2(object sender, MouseButtonEventArgs e)
        {
            bool toShow = sidePanel.Width == 0;
            if (toShow && globalLoadProgress.Visibility == Visibility.Visible) return;

            if (toShow)
            {
                bool autoorize = vk != null;
                if (autoorize) autoorize = vk.IsAuthorized;
                
                noLogginInfo.Visibility = autoorize ? Visibility.Collapsed : Visibility.Visible;
                openProfile.Visibility = logginInfo.Visibility = noLogginInfo.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                logginTime.Text = $"Вход {DateSting(logginDate)}";
            }

            DoubleAnimation animation = new DoubleAnimation
            {
                To = toShow ? 240 : 0,
                Duration = TimeSpan.FromSeconds(0.3),
                EasingFunction = new PowerEase { Power = 5 }
            };

            translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
            {
                To = toShow ? 100 : 0,
                Duration = animation.Duration,
                EasingFunction = animation.EasingFunction
            });

            (sidePanel.RenderTransform as TranslateTransform).BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
            {
                To = toShow ? -100 : 0,
                Duration = animation.Duration,
                EasingFunction = animation.EasingFunction
            });

            outOfSidePanel.BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                To = toShow ? 0.8 : 0,
                Duration = animation.Duration,
                EasingFunction = animation.EasingFunction
            });

            sidePanel.BeginAnimation(WidthProperty, animation);

            if (sender is Grid) await Task.Delay(100);
            outOfSidePanel.Visibility = toShow ? Visibility.Visible : Visibility.Collapsed;
        }

        string DateSting(DateTime date)
        {
            const int SECOND = 1;
            const int MINUTE = 60 * SECOND;
            const int HOUR = 60 * MINUTE;
            const int DAY = 24 * HOUR;
            const int MONTH = 30 * DAY;

            var ts = new TimeSpan(DateTime.Now.Ticks - date.Ticks);
            double delta = Math.Abs(ts.TotalSeconds);

            if (delta < 1 * MINUTE)
                return ts.Seconds == 1 ? "сек назад" : ts.Seconds + " сек назад";

            if (delta < 2 * MINUTE)
                return "мин назад";

            if (delta < 45 * MINUTE)
                return ts.Minutes + " мин назад";

            if (delta < 90 * MINUTE)
                return "час назад";

            if (delta < 24 * HOUR)
                return ts.Hours + " ч назад";

            if (delta < DAY * 2)
                return "позавчера";

            if (delta < 48 * HOUR)
                return "вчера";

            if (delta < 30 * DAY)
                return ts.Days + " д назад";

            if (delta < 12 * MONTH)
            {
                int months = Convert.ToInt32(Math.Floor((double)ts.Days / 30));
                return months <= 1 ? "Месяц назад" : months + " мес. назад";
            }
            else
            {
                int years = Convert.ToInt32(Math.Floor((double)ts.Days / 365));
                return years <= 1 ? "год назад" : years + " лет назад";
            }
        }

        async void Border_MouseLeftButtonUp_3(object sender, MouseButtonEventArgs e)
        {
            Border_MouseLeftButtonUp_2(null, null);
            await vk.LogOutAsync();

            dialogs.Clear();
            users.Clear();
            logTab.IsSelected = true;
            notifyIcon.Text = trayNoLogText;
        }

        bool newMessageNotifications = true;
        bool newMessageSound = true;

        RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        static string exe = Process.GetCurrentProcess().MainModule.FileName;
        string fileName = Path.GetFileNameWithoutExtension(exe);

        void StackPanel_MouseLeftButtonUp_2(object sender, MouseButtonEventArgs e)
        {
            CheckBox checkBox = (sender as StackPanel).Children[1] as CheckBox;
            checkBox.IsChecked = !checkBox.IsChecked;

            int index = Convert.ToInt32((checkBox.DataContext as MenuItem).Tag);

            if (index == 0)
            {
                if (checkBox.IsChecked == true) key.SetValue(fileName, $"\"{exe}\" /autorun");
                else key.DeleteValue(fileName, false);

                checkBox.IsChecked = key.GetValue(fileName) != null;
            }
            else if (index == 1)
                newMessageNotifications = checkBox.IsChecked.Value;
            else if (index == 2)
                newMessageSound = checkBox.IsChecked.Value;
        }

        void CheckBox_Loaded(object sender, RoutedEventArgs e)
        {
            CheckBox checkBox = sender as CheckBox;
            int tag = Convert.ToInt32((checkBox.DataContext as MenuItem).Tag);

            if (tag == 0) checkBox.IsChecked = key.GetValue(fileName) != null;
            else if (tag == 1) checkBox.IsChecked = newMessageNotifications;
            else if (tag == 2) checkBox.IsChecked = newMessageSound;
        }

        void openProfile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Process.Start("https://vk.com/id" + vk.UserId);
            Minimize();
        }
    }
}
