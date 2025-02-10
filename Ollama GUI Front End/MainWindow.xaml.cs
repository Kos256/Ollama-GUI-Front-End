using OllamaSharp;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
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
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Net.Http.Json;
using System.Xml;

/*
 * TO-DO: 
 * 
 * > Add memory to the AI (That scales with memory cap)
 * > Add a prompt to the AI
 * > Fix memory cap buttons
 * > Add model selection menu
 * > Add model browser button (pops out a new window)
 * > MD to RTF parser (for user + AI)
 * > Conversation settings, add a remember memory cap checkbox setting
 * 
 * Later:
 * > Add Win + DownArrow
 */

namespace Ollama_GUI_Front_End
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public bool intentionallyClosed = false;
        public bool intentionallyMinimized = false;
        public bool firstTimeSession = false;
        public AppSaveData saveData;

        public OllamaApiClient ollama = new OllamaApiClient("http://localhost:11434");
        private string ollamaStatus = "";

        private List<bool> manualCancelSources;

        private int scrollViewerDefaultHeight, windowDefaultHeight; // constant ui measurements
        public int maxMemoryCap = 200; // constant values
        private int memcap;

        public MainWindow()
        {
            InitializeComponent();
            this.Closing += ApplicationWantsToQuit;
            this.StateChanged += ApplicationStateChanged;

            saveData = LoadAppSaveData();

            this.Left = SystemParameters.WorkArea.Width / 2 - this.Width / 2;
            this.Opacity = 0;
            windowBarCollider.MouseLeftButtonDown += (s, e) => DragMove();
            MainGrid.Visibility = Visibility.Hidden;
            scrollViewerDefaultHeight = (int)chatScroller.Height;
            windowDefaultHeight = (int)this.Height;

            textInput.Text = "Send a message";
            textInput.Foreground = new SolidColorBrush(Colors.DarkGray);
            textInput.Height = 50;
            inputLineCounterText.Text = "Cursor line: 1";
            memcap = saveData.Settings.MemoryCap;
            memoryCapTextbox.Text = memcap.ToString();
            if (memcap == 0) memoryCapTextbox.Text = "No cap";

            // source 0 for loading anim
            // source 1 for winctrl kbd shortcut
            manualCancelSources = new List<bool> { false, false };

            WindowControlKeyboardShortcuts();
            firstTimeSession = saveData.IsFirstRun;
            Thread.Sleep(1000);
            WinIntro();

            loadingSubtitle.Text = "Checking ollama status...";
            InitLoadingAnimation();
            CheckOllamaStatus();
        }

        private void WindowControlKeyboardShortcuts()
        {
            Thread thread = new Thread(() =>
            {
                while (!manualCancelSources[1])
                {

                    if ((Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) && Keyboard.IsKeyDown(Key.F4))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            CloseAnimation();
                        });
                    }
                    /* minimize shortcut is broken
                    if (Keyboard.IsKeyDown(Key.LWin) && Keyboard.IsKeyDown(Key.Down))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            Thread.Sleep(100);
                            MinimizeAnimation(true);
                        });
                    }*/

                    // Add a small delay to reduce CPU usage
                    Task.Delay(100).Wait();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

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

                LinearGradientBrush gradientBrush = null;
                Dispatcher.Invoke(() => {
                    gradientBrush = loadingRect.Fill as LinearGradientBrush;
                });

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

                bool isSubTextWelcome = false;
                bool animationRan = false;
                bool shouldQuit = false;

                Task.Run(() =>
                {
                    while (true)
                    {
                        if (shouldQuit) break;
                        Dispatcher.Invoke(() =>
                        {
                            if (loadingSubtitle.Text.Contains("Welcome"))
                            {
                                isSubTextWelcome = true;
                            }
                            if (manualCancelSources[0]) shouldQuit = true;

                            if (isSubTextWelcome && !animationRan)
                            {
                                animationRan = true;
                                gradientBrush.GradientStops[1].BeginAnimation(GradientStop.ColorProperty, gradientFadeOut);
                                gradientBrush.GradientStops[1].Color = Colors.Black;
                                TransitionToMainGUI();
                            }

                            gradientBrush.GradientStops[1].Offset = 0.5f + Math.Sin(stopwatch.Elapsed.TotalMilliseconds / 500) * 0.45f;
                        });

                        Thread.Sleep(16); // Add a slight delay to prevent high CPU usage
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Task encountered an error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void TransitionToMainGUI()
        {
            DoubleAnimation opacityOut = new DoubleAnimation
            {
                From=1, To=0,
                Duration = TimeSpan.FromSeconds(1),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut }
            };
            DoubleAnimation opacityIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(1),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut }
            };

            opacityOut.Completed += (s, e) =>
            {
                LoadingGrid.Visibility = Visibility.Hidden;
            };

            Task.Run(() =>
            {
                Thread.Sleep(1000);
                Dispatcher.Invoke(() =>
                {
                    UpdateWindowTitle("Untitled Conversation* - Ollama");

                    LoadingGrid.BeginAnimation(Grid.OpacityProperty, opacityOut);
                    MainGrid.Visibility = Visibility.Visible;
                    MainGrid.BeginAnimation(Grid.OpacityProperty, opacityIn);

                    try
                    {
                        ollama.SelectedModel = "llama3.1:latest";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Error in model selection", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                });
            });
        }

        private async Task CheckOllamaStatus()
        {
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                await ollama.ListLocalModelsAsync();
                ollamaStatus = "running";
                LoadModelsAsync();
            }
            catch (Exception ex)
            {
                // Check if the exception message mentions "actively refused"
                if (ex.Message.Contains("actively refused"))
                {
                    ollamaStatus = "not running";
                    StartOllama();
                }
                else
                {
                    // Handle other exceptions
                    MessageBox.Show($"Error checking Ollama status: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    ollamaStatus = $"error: {ex.Message}";
                }
            }
        }

        private async Task StartOllama()
        {
            try
            {
                // Check if Ollama is already running
                var processName = "ollama"; // Replace with the actual process name if different
                if (Process.GetProcessesByName(processName).Any())
                {
                    MessageBox.Show("Ollama is already running.", "Status", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Start Ollama process
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c ollama serve", // Adjust the command based on your specific needs
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Task.Run(() =>
                {
                    Dispatcher.Invoke(() => { loadingSubtitle.Text = "Launching ollama..."; });
                    Thread.Sleep(1000);
                    Dispatcher.Invoke(() =>
                    {
                        Process ollamaProcess = new Process { StartInfo = startInfo };
                        ollamaProcess.Start();

                        LoadModelsAsync();
                        ollamaStatus = "running";
                    });
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting Ollama: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadModelsAsync()
        {
            loadingSubtitle.Text = "Retrieving installed models...";
            try
            {
                StringBuilder sb = new StringBuilder();
                var models = await ollama.ListLocalModelsAsync();
                foreach (var model in models)
                {
                    sb.AppendLine($"Name: {model.Name}, Size: {model.Size}");

                    Border listItemBorder = new Border
                    {
                        Background = new SolidColorBrush(Colors.Black),
                        BorderBrush = new SolidColorBrush(Colors.White),
                        BorderThickness = new Thickness(2),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(10),
                        Margin = new Thickness(10, 10, 10, 0),

                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Top,
                    };
                    listItemBorder.Child = new TextBlock
                    {
                        Text = model.Name,
                        Foreground = Brushes.White,
                        FontSize = 20,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        TextWrapping = TextWrapping.Wrap
                    };
                    var litxt = listItemBorder.Child as TextBlock; // (li)st item (txt) block

                    modelListSP.Children.Add(listItemBorder);
                }



                loadingTitle.Text = "All set!";
                loadingTitle.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 0));
                Mouse.OverrideCursor = null;
                if (saveData.IsFirstRun)
                {
                    loadingSubtitle.Text = "Welcome!";
                    saveData.IsFirstRun = false;
                    SaveAppSaveData(saveData);
                }
                else
                {
                    loadingSubtitle.Text = "Welcome back!";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error retrieving models: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                intentionallyClosed = true;
                this.Close();
            }
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

        private void textInput_GotFocus(object sender, RoutedEventArgs e)
        {
            if (textInput.Text == "Send a message")
            {
                textInput.Foreground = new SolidColorBrush(Colors.White);
                textInput.Text = "";
            }
        }
        private void textInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (textInput.Text == "")
            {
                textInput.Foreground = new SolidColorBrush(Colors.Gray);
                textInput.Text = "Send a message";
            }
        }
        private void textInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (textInput.LineCount <= 3)
            {
                textInput.Height = 50 + 20 * (textInput.LineCount - 1);
                inputLineCounterText.Foreground = new SolidColorBrush(Color.FromArgb(127, 0, 145, 255));
            }
            else
            {
                textInput.Height = 50 + 20 * (3 - 1);
                inputLineCounterText.Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 145, 255));
            }

            inputLineCounterText.Margin = new Thickness
            {
                Left = inputLineCounterText.Margin.Left,
                Top = inputLineCounterText.Margin.Top,
                Right = inputLineCounterText.Margin.Right,
                Bottom = 90 + (textInput.Height - 50)
            };

            inputChatSeperatorRect.Margin = new Thickness(
                inputChatSeperatorRect.Margin.Left,
                inputChatSeperatorRect.Margin.Top,
                inputChatSeperatorRect.Margin.Right,
                (textInput.Height - 8) * 1.2 - 50
            );
            chatScroller.Height = scrollViewerDefaultHeight - (textInput.Height - 50);
        }

        private void textInput_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (textInput.SelectionLength == 0)
            {
                inputLineCounterText.Text = $"Cursor line: {textInput.GetLineIndexFromCharacterIndex(textInput.CaretIndex) + 1}";
            }
            else
            {
                int lineStart = textInput.GetLineIndexFromCharacterIndex(textInput.SelectionStart) + 1;
                int lineEnd = textInput.GetLineIndexFromCharacterIndex(textInput.SelectionStart + textInput.SelectionLength) + 1;
            
                if (lineStart != lineEnd)
                {
                    inputLineCounterText.Text = $"Cursor selection line: {lineStart}~{lineEnd}";
                }
                else
                {
                    inputLineCounterText.Text = $"Cursor selection line: {lineEnd}";
                }
            }
        }

        private void textInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (!textInput.IsReadOnly)
            {
                if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    // Shift+Enter is pressed, insert a new line
                    int caretIndex = textInput.CaretIndex;
                    textInput.Text = textInput.Text.Insert(caretIndex, Environment.NewLine);
                    textInput.CaretIndex = caretIndex + Environment.NewLine.Length;

                    // Mark the event as handled to prevent the default behavior
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter && textInput.Text != "") // enter key pressed
                {
                    string userInput = textInput.Text;
                    textInput.IsReadOnly = true;
                    textInput.Foreground = new SolidColorBrush(Colors.Gray);
                    RequestResponseFromOllama();
                    //textInput.Text = "";
                }

                if (Keyboard.Modifiers == ModifierKeys.Control) { textInput.AcceptsReturn = true; }
            }
        }
        private void textInput_KeyUp(object sender, KeyEventArgs e)
        {
            if ((e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl) && !Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
            {
                // Ctrl key was released
                textInput.AcceptsReturn = false;
            }
        }

        private void RequestResponseFromOllama()
        {
            if (LandingUIGrid.Visibility == Visibility.Visible) FadeOutLandingUI();
            Task.Run(() =>
            {
                string userInput = "";
                Thread.Sleep(400);
                Dispatcher.Invoke(() =>
                {
                    Border userMsg = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0, 19, 36)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(1, 51, 97)),
                        BorderThickness = new Thickness(2),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(10),
                        Margin = new Thickness(10),

                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom,
                    };
                    userMsg.Child = new TextBlock
                    {
                        Text = textInput.Text,
                        Foreground = Brushes.White,
                        FontSize = 20,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        MaxWidth = this.Width * 0.6,
                        MinWidth = 50,
                        TextWrapping = TextWrapping.Wrap
                    };
                    userInput = textInput.Text;
                    textInput.Text = "";

                    chatStackPanel.Children.Add(userMsg);
                    chatScroller.ScrollToBottom();
                });

                // now we get ollama's response
                Thread.Sleep(1000);
                Dispatcher.Invoke(async () =>
                {
                    Border botMsg = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(36, 36, 36)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(97, 97, 97)),
                        BorderThickness = new Thickness(2),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(10),
                        Margin = new Thickness(10),

                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Bottom,
                    };
                    botMsg.Child = new TextBlock
                    {
                        Text = "",
                        Foreground = Brushes.White,
                        FontSize = 20,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        MaxWidth = this.Width * 0.6,
                        MinWidth = 50,
                        TextWrapping = TextWrapping.Wrap
                    };
                    var botMsgTextBlock = botMsg.Child as TextBlock;

                    chatStackPanel.Children.Add(botMsg);
                    chatScroller.ScrollToBottom();

                    await INTERNAL_GenerateResponseStreams(userInput, botMsgTextBlock);

                });
            });
            
        }

        private async Task INTERNAL_GenerateResponseStreams(string userInput, TextBlock botMsgTextBlock)
        {
            await foreach (var stream in ollama.GenerateAsync(userInput))
            {
                Dispatcher.Invoke(() =>
                {
                    botMsgTextBlock.Text += stream.Response;
                });

                await Task.Delay(1);
            }

            textInput.IsReadOnly = false;
            textInput.Foreground = new SolidColorBrush(Colors.White);
        }

        private void FadeOutLandingUI()
        {
            DoubleAnimation fadeOut = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromSeconds(0.3)
            };

            fadeOut.Completed += (s, e) => { LandingUIGrid.Visibility = Visibility.Hidden; };

            LandingUIGrid.BeginAnimation(Grid.OpacityProperty, fadeOut);
        }

        private void memoryCapTextbox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+"); // Allows only numeric characters
            e.Handled = regex.IsMatch(e.Text);
        }
        private void memoryCapTextbox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
                // Use a regular expression to check if the pasted text is numeric
                if (!IsStringNumeric(text))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }
        private void memoryCapTextbox_TextChanged(object sender, TextChangedEventArgs e)
        {
            int memCapInput = 0;
            if (int.TryParse(memoryCapTextbox.Text, out _))
            {
                memCapInput = memoryCapTextbox.Text != "" ? int.Parse(memoryCapTextbox.Text) : 0;

                if (memCapInput > maxMemoryCap) memoryCapTextbox.Foreground = new SolidColorBrush(Colors.Red);
                else memoryCapTextbox.Foreground = new SolidColorBrush(Colors.White);
            }
        }
        private void memoryCapTextbox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (IsStringNumeric(memoryCapTextbox.Text)) memcap = int.Parse(memoryCapTextbox.Text);
            if (memcap > maxMemoryCap) memoryCapTextbox.Text = saveData.Settings.MemoryCap.ToString();

            if (memcap == 0) memoryCapTextbox.Text = "No cap";

            saveData.Settings.MemoryCap = memcap;
            SaveAppSaveData(saveData);
        }
        private void memoryCapTextbox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (memoryCapTextbox.Text == "No cap")
            {
                memoryCapTextbox.Text = "0";
            }
        }
        private void memoryCapTextbox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                memcap = int.Parse(memoryCapTextbox.Text);
                if (memcap > maxMemoryCap) memoryCapTextbox.Text = saveData.Settings.MemoryCap.ToString();
                textInput.Focus();

                saveData.Settings.MemoryCap = memcap;
                SaveAppSaveData(saveData);
            }
        }

        private void memCapBtnIncrease_Click(object sender, RoutedEventArgs e)
        {
            if (!((memcap + 1) > maxMemoryCap)) memcap += 1;
            memoryCapTextbox.Text = memcap.ToString();

            saveData.Settings.MemoryCap = memcap;
            SaveAppSaveData(saveData);
        }
        private void memCapBtnDecrease_Click(object sender, RoutedEventArgs e)
        {
            if (!((memcap - 1) < 0)) memcap -= 1;
            memoryCapTextbox.Text = memcap.ToString();

            if (memcap == 0) memoryCapTextbox.Text = "No cap";

            saveData.Settings.MemoryCap = memcap;
            SaveAppSaveData(saveData);
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
            float targetWidth = 500;
            float defaultWidth = 320;
            Color initialColor = (Color)ColorConverter.ConvertFromString("#19FFFFFF");
            Color targetColor = (Color)ColorConverter.ConvertFromString("#AAFFFFFF");

            // Width animations
            DoubleAnimation lineTrueW = new DoubleAnimation
            {
                From = defaultWidth, To = targetWidth,
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = new CubicEase { EasingMode= EasingMode.EaseOut }
            };
            DoubleAnimation lineFalseW = new DoubleAnimation
            {
                From = targetWidth, To = defaultWidth,
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
                From = windowDefaultHeight,
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
                intentionallyClosed = true;
                this.Close();
            };


            windowPos.From = this.Top;
            windowPos.To = this.Top + this.Height / 2;
            this.BeginAnimation(Window.HeightProperty, windowHeight);
            this.BeginAnimation(Window.TopProperty, windowPos);
            this.BeginAnimation(Window.OpacityProperty, windowOpac);
        }

        private void winCtrlBtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            intentionallyMinimized = true;
            MinimizeAnimation();
        }

        private void MinimizeAnimation(bool Min = true)
        {
            DoubleAnimation windowWidthOut = new DoubleAnimation
            {
                From = 800,
                To = 0,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                Duration = TimeSpan.FromSeconds(0.5)
            };
            DoubleAnimation windowWidthIn = new DoubleAnimation
            {
                From = 0,
                To = 800,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                Duration = TimeSpan.FromSeconds(0.3)
            };

            windowWidthOut.Completed += (s, e) => this.WindowState = WindowState.Minimized;

            if (Min)
            {
                this.BeginAnimation(Window.WidthProperty, windowWidthOut);
            }
            else
            {
                this.BeginAnimation(Window.WidthProperty, windowWidthIn);
            }
        }

        private void ApplicationStateChanged(object sender, EventArgs e)
        {
            //throw new NotImplementedException();

            if (this.WindowState == WindowState.Minimized)
            {
                // Perform actions when the application is minimized
                if (!intentionallyMinimized)
                {
                    this.WindowState = WindowState.Normal;
                }
            }
            if (this.WindowState == WindowState.Normal)
            {
                if (intentionallyMinimized) MinimizeAnimation(false);
                intentionallyMinimized = false;
            }
        }

        private void ApplicationWantsToQuit(object sender, CancelEventArgs e)
        {
            if (!intentionallyClosed) // if we don't want the app to quit
            {
                e.Cancel = true;
            }
            else // if we are fine with the app quitting
            {
                for (int i = 0; i < manualCancelSources.Count; i++)
                {
                    manualCancelSources[i] = true;
                }
            }
        }

        private void UpdateWindowTitle(string name="Ollama")
        {
            this.Title = name;
            windowBarTitle.Text = name;
        }

        private string RetrieveExecutableDirectory()
        {
            return System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        private AppSaveData LoadAppSaveData()
        {
            if (File.Exists("savedata.json"))
            {
                try
                {
                    string exeDirectory = RetrieveExecutableDirectory();
                    string filePath = System.IO.Path.Combine(exeDirectory, "savedata.json");

                    if (File.Exists(filePath))
                    {
                        string jsonString = File.ReadAllText(filePath);
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                            WriteIndented = true
                        };
                        return JsonSerializer.Deserialize<AppSaveData>(jsonString, options);
                    }
                    else
                    {
                        return new AppSaveData { IsFirstRun = true };
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading application data: {ex.Message}", "Error loading user save data", MessageBoxButton.OK, MessageBoxImage.Error);
                    return new AppSaveData { IsFirstRun = true };
                }
            }
            else
            {
                // MessageBox.Show($"A new savedata file was created...", "Where's the savedata file??", MessageBoxButton.OK, MessageBoxImage.Question);
                // Create default save data
                var defaultSaveData = new AppSaveData
                {
                    IsFirstRun = true,
                    Settings = new UserSettings
                    {
                        MemoryCap = 20
                    }
                };

                // Serialize the default save data to JSON
                string jsonData = JsonSerializer.Serialize(defaultSaveData, new JsonSerializerOptions { WriteIndented = true });

                // Write the JSON data to the file
                File.WriteAllText("savedata.json", jsonData);

                return defaultSaveData;
            }
        }

        private void SaveAppSaveData(AppSaveData data)
        {
            try
            {
                string exeDirectory = RetrieveExecutableDirectory();
                string filePath = System.IO.Path.Combine(exeDirectory, "savedata.json");

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                string jsonString = JsonSerializer.Serialize(data, options);
                File.WriteAllText(filePath, jsonString);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving application data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool IsStringNumeric(string text)
        {
            Regex regex = new Regex("[^0-9]+"); // Allows only numeric characters
            return !regex.IsMatch(text);
        }
    }

    public class AppSaveData
    {
        public bool IsFirstRun { get; set; }
        public Dictionary<string, List<string>> Conversations { get; set; } = new Dictionary<string, List<string>>();
        public UserSettings Settings { get; set; } = new UserSettings();
    }

    public class UserSettings
    {
        public int MemoryCap { get; set; }
    }
}