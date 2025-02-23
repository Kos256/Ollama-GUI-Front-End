using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
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
using System.Windows.Shapes;

namespace Ollama_GUI_Front_End.Assets
{
    /// <summary>
    /// Interaction logic for ModelBrowser.xaml
    /// </summary>
    public partial class ModelBrowser : Window
    {
        public bool _intentionallyClosed = false;
        public bool _intentionallyClosedByAnimation = false;
        private Stopwatch _globalStopwatch = new Stopwatch();
        private int windowDefaultHeight;
        private bool _isResizing;
        private Point _lastMousePosition;

        private List<bool> manualCancelSources;
        private string browserStatus = "";

        public ModelBrowser()
        {
            InitializeComponent();
            this.Closing += ApplicationWantsToQuit;
            windowBarCollider.MouseLeftButtonDown += (s, e) => DragMove();
            windowDefaultHeight = (int)this.Height;
            this.Left = SystemParameters.WorkArea.Width / 2 - this.Width;

            // source 0 for loading anim
            manualCancelSources = new List<bool> { false };

            _globalStopwatch.Start();

            WinIntro();
            InitLoadingAnimation();
            LoadBrowser();
            UpdateWindowTitle("Ollama Model Browser");
        }

        private bool isFinishedLoadingBrowser = false;
        private void InitLoadingAnimation()
        {
            try
            {
                ColorAnimation gradientFadeOut = new ColorAnimation
                {
                    From = Color.FromRgb(24, 24, 24),
                    To = Colors.Black,
                    Duration = TimeSpan.FromSeconds(1)
                };
                ColorAnimation gradientToRed = new ColorAnimation
                {
                    From = Color.FromRgb(24, 24, 24),
                    To = Color.FromRgb(30, 0, 0),
                    Duration = TimeSpan.FromSeconds(1)
                };

                LinearGradientBrush gradientBrush = null;
                Dispatcher.Invoke(() => {
                    gradientBrush = loadingRect.Fill as LinearGradientBrush;
                });

                
                bool animationRan = false;
                bool shouldQuit = false;

                loadingSubtitle.Text = "Checking network access...";

                Task.Run(() =>
                {
                    float influence = 1f;
                    while (true)
                    {
                        if (shouldQuit) break;
                        Dispatcher.Invoke(() =>
                        {
                            if (manualCancelSources[0]) shouldQuit = true;

                            if (isFinishedLoadingBrowser && !animationRan)
                            {
                                animationRan = true;
                                gradientBrush.GradientStops[1].BeginAnimation(GradientStop.ColorProperty, gradientFadeOut);
                                gradientBrush.GradientStops[1].Color = Colors.Black;
                                //TransitionToMainGUI();
                            }

                            if (browserStatus == "no network" || browserStatus == "no internet")
                            {
                                loadingSubtitle.Text = $"There is no {browserStatus.Substring(3, browserStatus.Length-3)} connection?";
                                animationRan = true;
                                if (!animationRan)
                                {
                                    gradientBrush.GradientStops[1].BeginAnimation(GradientStop.ColorProperty, gradientToRed);
                                }
                                if (influence > 0f)
                                {
                                    influence -= 1;
                                }
                            }
                            if (browserStatus == "checking internet") loadingSubtitle.Text = "Checking internet access...";
                            if (browserStatus == "checking ollama") loadingSubtitle.Text = "Saying hi to the ollama servers...";

                            gradientBrush.GradientStops[1].Offset = 0.5f + Math.Sin(_globalStopwatch.Elapsed.TotalMilliseconds / 500) * 0.45f * influence;
                            
                        });

                        Thread.Sleep(8); // Add a slight delay to prevent high CPU usage
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Task encountered an error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        async void LoadBrowser()
        {
            if (NetworkInterface.GetIsNetworkAvailable())
            {
                // network succeeded, checking google
                browserStatus = "checking internet";
                if (await CanAccessInternet())
                {
                    // google succeeded, checking ollama
                    browserStatus = "checking ollama";
                    List<string> libraryModels = await FetchOnlineModelsAsync();
                    MessageBox.Show("Count: " + libraryModels.Count);
                    foreach (var model in libraryModels) MessageBox.Show(model);
                }
                else browserStatus = "no internet";
            }
            else
            {
                // left off here (BRO YOU NEED TO FIX IT IT THINKS THERE ARE NO MODELS, OKAY FUTURE KOS?)
                browserStatus = "no network";
            }
        }

        

        async Task<List<string>> FetchOnlineModelsAsync()
        {
            var url = "https://ollama.com/library";
            var httpClient = new HttpClient();
            var response = await httpClient.GetStringAsync(url);
            MessageBox.Show(response);

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(response);

            var modelNodes = htmlDoc.DocumentNode.SelectNodes("//div[@class='model-name']");
            if (modelNodes == null)
            {
                Console.WriteLine("No model elements found. The page structure might have changed.");
                return new List<string>();
            }

            var modelNames = modelNodes.Select(node => node.InnerText.Trim()).ToList();
            return modelNames;
        }

        private void WinIntro()
        {
            DoubleAnimation winIntroY = new DoubleAnimation
            {
                From = SystemParameters.WorkArea.Height - this.Height,
                To = SystemParameters.WorkArea.Height / 2 - this.Height / 2,
                Duration = TimeSpan.FromSeconds(1),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            DoubleAnimation winIntroO = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(1.5),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };

            this.BeginAnimation(Window.TopProperty, winIntroY);
            this.BeginAnimation(Window.OpacityProperty, winIntroO);
        }

        private void ResizeRectangle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isResizing = true;
            _lastMousePosition = e.GetPosition(this);
            resizeRectangleCollider.CaptureMouse();
        }
        private void ResizeRectangle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isResizing)
            {
                Point currentMousePosition = e.GetPosition(this);
                double deltaX = currentMousePosition.X - _lastMousePosition.X;
                double deltaY = currentMousePosition.Y - _lastMousePosition.Y;

                // Calculate new width and height
                double newWidth = this.Width + deltaX;
                double newHeight = this.Height + deltaY;

                // Apply new width and height while respecting minimum bounds
                if (newWidth >= this.MinWidth)
                {
                    this.Width = newWidth;
                    _lastMousePosition.X = currentMousePosition.X;
                }
                if (newHeight >= this.MinHeight)
                {
                    this.Height = newHeight;
                    _lastMousePosition.Y = currentMousePosition.Y;
                }
            }
        }
        private void ResizeRectangle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isResizing = false;
            resizeRectangleCollider.ReleaseMouseCapture();
        }

        async Task<bool> CanAccessInternet()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    HttpResponseMessage response = await client.GetAsync("https://www.google.com");
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        private void WinBarColliderMouseEnter(object sender, RoutedEventArgs e)
        {
            AnimateWinBarLine(true);
        }
        private void WinBarColliderMouseLeave(object sender, RoutedEventArgs e)
        {
            AnimateWinBarLine(false);
        }

        private void AnimateWinBarLine(bool extended)
        {
            float targetWidth = 400;
            float defaultWidth = 200;
            Color initialColor = (Color)ColorConverter.ConvertFromString("#19FFFFFF");
            Color targetColor = (Color)ColorConverter.ConvertFromString("#AAFFFFFF");

            // Width animations
            DoubleAnimation lineTrueW = new DoubleAnimation
            {
                From = defaultWidth,
                To = targetWidth,
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            DoubleAnimation lineFalseW = new DoubleAnimation
            {
                From = targetWidth,
                To = defaultWidth,
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };


            // Gradient stop animations
            ColorAnimation lineTrueGS = new ColorAnimation
            {
                From = initialColor,
                To = targetColor,
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ColorAnimation lineFalseGS = new ColorAnimation
            {
                From = targetColor,
                To = initialColor,
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };


            var gradientBrush = windowDragLine.Fill as LinearGradientBrush;

            if (extended)
            {
                windowDragLine.BeginAnimation(Rectangle.WidthProperty, lineTrueW);
                gradientBrush.GradientStops[1].BeginAnimation(GradientStop.ColorProperty, lineTrueGS);
                gradientBrush.GradientStops[2].BeginAnimation(GradientStop.ColorProperty, lineTrueGS);
            }
            else
            {
                windowDragLine.BeginAnimation(Rectangle.WidthProperty, lineFalseW);
                gradientBrush.GradientStops[1].BeginAnimation(GradientStop.ColorProperty, lineFalseGS);
                gradientBrush.GradientStops[2].BeginAnimation(GradientStop.ColorProperty, lineFalseGS);
            }
        }

        private void winCtrlBtnClose_Click(object sender, RoutedEventArgs e)
        {
            CloseAnimation();
        }

        private void CloseAnimation()
        {
            DoubleAnimation windowHeight = new DoubleAnimation
            {
                From = (double)windowDefaultHeight,
                To = 0,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                Duration = TimeSpan.FromSeconds(0.5)
            };
            DoubleAnimation windowPos = new DoubleAnimation
            {
                From = this.Top,
                To = this.Top + this.Height / 2,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                Duration = TimeSpan.FromSeconds(0.5)
            };
            DoubleAnimation windowOpac = new DoubleAnimation
            {
                From = 1,
                To = 0,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                Duration = TimeSpan.FromSeconds(0.5)
            };

            windowHeight.Completed += (s, e) =>
            {
                _intentionallyClosed = true;
                _intentionallyClosedByAnimation = true;
                this.Close();
            };


            windowPos.From = this.Top;
            windowPos.To = this.Top + this.Height / 2;
            this.BeginAnimation(Window.HeightProperty, windowHeight);
            this.BeginAnimation(Window.TopProperty, windowPos);
            this.BeginAnimation(Window.OpacityProperty, windowOpac);
        }

        private void UpdateWindowTitle(string name = "Ollama")
        {
            this.Title = name;
            windowBarTitle.Text = name;
        }

        private void ApplicationWantsToQuit(object sender, CancelEventArgs e)
        {
            if (!_intentionallyClosed) // if we don't want the app to quit
            {
                e.Cancel = true;

                if (true) CloseAnimation(); // change the if condition to a variable that outputs unsaved chats
            }
            else // if we are fine with the app quitting
            {
                
                for (int i = 0; i < manualCancelSources.Count; i++)
                {
                    manualCancelSources[i] = true;
                }
                
                CloseAnimation();
                if (!_intentionallyClosedByAnimation) e.Cancel = true;
            }
        }
    }
}
