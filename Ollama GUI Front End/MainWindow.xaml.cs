using OllamaSharp; // the legend himself
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
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
using System.Security.Cryptography.X509Certificates;

/*
 * TO-DO: 
 * > Select first installed model it can find if the value in savedata is "none"
 * > Scale memory of the LLM with memory cap
 * > Make memcap evaluate numbers like 050 to 50 (trim any zeros before the actual number)
 * > Add model browser button (pops out a new window)
 * > MD to RTF parser (for user + LLM)
 * > Cancel generation button (interrupt the LLM generation)
 * 
 * Later:
 * > Add Win + DownArrow
 * > Add about section, with easter eggs. Links to vixile and my github.
 * > Add a ram warning that is shown when the memory cap is set to 0. Also allow for a savedata option to not show the warning next time.
 * 
 * Yogurt branch to merge:
 * 1. Main project folder assembly to src (make sure to fix sln file)
 */

/*
 * Version 2:
 * 
 * - Customizable colors
 * - Support for models that can interact with both text + images
 * - Maybe....canvas feature from chatgpt? Make code conversation easier to edit and ask... Althought might be a v3 feature
 */



namespace Ollama_GUI_Front_End
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public bool _intentionallyClosed = false;
        public bool _intentionallyClosedByAnimation = false;
        public bool _intentionallyMinimized = false;
        public string _UserDisplayName;

        public bool firstTimeSession = false;
        public AppSaveData saveData;

        public OllamaApiClient ollama = new OllamaApiClient("http://localhost:11434");
        private string ollamaStatus = "";

        private List<bool> manualCancelSources;

        private int scrollViewerDefaultHeight, windowDefaultHeight; // constant ui measurements
        public int maxMemoryCap = 200; // constant values


        // Values for LLM conversation ---------
        private string userInput;
        private int memcap;
        private string botOutput;
        private string botLastOutput = "";
        private List<KeyValuePair<bool, string>> conversationMemory = new List<KeyValuePair<bool, string>>();
        private string llmPrompt =
            "You are an AI assistant that helps out with the user's queries. You may conversate if you feel like it." +
            "\n\nHere's what you need to know about the current conversation's context:" +
            "\nThe user's name: {username}" +
            "\nRemember: Do not try to frequently refer to their name, keep its frequency varied between every 5 to 6 messages. " +
            "Also take note if the name provided looks like a full name. If it is a full name, use their first name when you need to refer to them " +
            "(unless explicitly told by the user to refer using full name as well)." +
            "\nYour previous message:" +
            "\n'''{msgB}'''" + // msgB = message by bot
            "\nThe user's current message:" +
            "\n'''{msgU}'''" + // msgU = message by user
            "\n\n" +
            "\nHere is the whole conversation {convolead}" +
            "\n{conversation}" +
            "\n\n" +

            "\n\nA certain thing to note: Whenever you see a triple apostrophe, that is strictly marked as the beginning or ending of the wrapped content. " +
            "For example: If you see 4 apostrophes at the end, that means that content originally had one apostrophe appended to it. Keep this in mind: " +
            "you WILL NOT replicate this formatting, this is strictly only for your reference. Do not wrap your response in triple apostrophes. " +
            "Do not generate a response for the user, generate only on your behalf. Do not include any predictions about what the user might say unless explicitly told by the conversation in context. Most of the times, however, you will not predict what the user will say." +
            "\n\nNow your response should be the next message in the conversation, or more appropriately a response to the user's current message maintaining context with your previous message and the conversation history."
        ;
        // -------------------------------------

        /*
         * Why use a bool for the key in conversation memory?
         * 
         * There is a value needed to differentiate if this message was sent by either the LLM or user.
         * Now of course this value is extremely simple, so it is good practice to not allowcate unneccesary amounts of r
         * The boolean corresponds to if the message was from a generated source or not.
         * Thus, true is the LLM and false is the user.
         * 
         * In v2, there are plans to make that bool key into an int key, that way maybe many users or many LLMs can chat on the same conversation.
         * This is just an idea though, I might not have the motivation to add it.
         * 
         */

        private Stopwatch _globalStopwatch = new Stopwatch();
        
        public MainWindow()
        {
            InitializeComponent();
            //using (var context = new PrincipalContext(ContextType.Machine))
            this.Closing += ApplicationWantsToQuit;
            this.StateChanged += ApplicationStateChanged;

            saveData = LoadAppSaveData();

            this.Left = SystemParameters.WorkArea.Width / 2 - this.Width / 2;
            this.Opacity = 0;
            windowBarCollider.MouseLeftButtonDown += (s, e) => DragMove();
            MainGrid.Visibility = Visibility.Hidden;
            ModelBrowserWaitOverlayGrid.Visibility = Visibility.Hidden;
            scrollViewerDefaultHeight = (int)chatScroller.Height;
            windowDefaultHeight = (int)this.Height;
            _globalStopwatch.Start();

            textInput.Text = "Send a message";
            textInput.Foreground = new SolidColorBrush(Colors.DarkGray);
            textInput.Height = 50;
            inputLineCounterText.Text = "Cursor line: 1";
            memcap = saveData.Settings.MemoryCap;
            memoryCapTextbox.Text = memcap.ToString();
            if (memcap == 0) memoryCapTextbox.Text = "No cap";
            _UserDisplayName = UserPrincipal.Current.DisplayName;

            // source 0 for loading anim
            // source 1 for winctrl kbd shortcut
            // source 2 for model browser wait overlay anim
            manualCancelSources = new List<bool> { false, false, false };

            //WindowControlKeyboardShortcuts();
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

                            gradientBrush.GradientStops[1].Offset = 0.5f + Math.Sin(_globalStopwatch.Elapsed.TotalMilliseconds / 500) * 0.45f;
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
                        ollama.SelectedModel = "llama3.1:latest"; // remove this and replace it with first model got
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

                if (!models.Any())
                {
                    MessageBox.Show("There are no models installed! Install some models using the command line and then relaunch this program.", "No models", MessageBoxButton.OK, MessageBoxImage.Error);
                    _intentionallyClosed = true;
                    this.Close();
                }

                foreach (var model in models)
                {
                    sb.AppendLine($"Name: {model.Name}, Size: {model.Size}"); // remove unused string builder naming list

                    LinearGradientBrush normalGradient = new LinearGradientBrush();
                    normalGradient.StartPoint = new Point(0.5, 0);
                    normalGradient.EndPoint = new Point(0.5, 1);
                    normalGradient.GradientStops.Add(new GradientStop(Color.FromRgb(0, 86, 125), 0f));
                    normalGradient.GradientStops.Add(new GradientStop(Color.FromRgb(0, 60, 102), 1f));

                    LinearGradientBrush hoverGradient = new LinearGradientBrush();
                    normalGradient.StartPoint = new Point(0.5, 0);
                    normalGradient.EndPoint = new Point(0.5, 1);
                    normalGradient.GradientStops.Add(new GradientStop(Color.FromRgb(0, 120, 180), 0f));  // Brighter
                    normalGradient.GradientStops.Add(new GradientStop(Color.FromRgb(0, 90, 160), 1f));  // Brighter

                    Border listItemBorder = new Border
                    {
                        //Background = new SolidColorBrush(Color.FromRgb(0, 60, 102)),
                        Background = normalGradient,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0, 183, 255)),
                        BorderThickness = new Thickness(2),
                        //CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(5),
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
                    listItemBorder.MouseLeave += (s, e) =>
                    {
                        listItemBorder.Background = normalGradient;
                        listItemBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 183, 255));
                    };
                    listItemBorder.MouseEnter += (s, e) => listItemBorder.Background = new LinearGradientBrush
                    {
                        StartPoint = new Point(0.5, 0),
                        EndPoint = new Point(0.5, 1),
                        GradientStops =
                        {
                            new GradientStop(Color.FromRgb(0, 134, 217), 0f), // Brighter color
                            new GradientStop(Color.FromRgb(0, 108, 191), 1f)    // Brighter color
                        }
                    };
                    listItemBorder.MouseLeftButtonDown += (s, e) =>
                    {
                        listItemBorder.Background = new LinearGradientBrush
                        {
                            StartPoint = new Point(0.5, 0),
                            EndPoint = new Point(0.5, 1),
                            GradientStops =
                            {
                                new GradientStop(Color.FromRgb(0, 153, 13), 0f), // Pressed color
                                new GradientStop(Color.FromRgb(0, 115, 10), 1f)    // Pressed color
                            }
                        };
                        listItemBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 200, 0));

                        saveData.Settings.LastSelectedModel = (listItemBorder.Child as TextBlock).Text;
                        SaveAppSaveData(saveData);
                        try
                        {
                            ollama.SelectedModel = saveData.Settings.LastSelectedModel;
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message, "Error while setting model", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        selectedModelText.Text = "Selected: " + (listItemBorder.Child as TextBlock).Text;
                    };
                    listItemBorder.MouseLeftButtonUp += (s, e) =>
                    {
                        listItemBorder.Background = new LinearGradientBrush
                        {
                            StartPoint = new Point(0.5, 0),
                            EndPoint = new Point(0.5, 1),
                            GradientStops =
                            {
                                new GradientStop(Color.FromRgb(0, 201, 17), 0f), // Released color
                                new GradientStop(Color.FromRgb(0, 153, 13), 1f)    // Released color
                            }
                        };
                        listItemBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 255, 0));
                    };

                    if (saveData.Settings.LastSelectedModel != "none")
                    {

                        if ((listItemBorder.Child as TextBlock).Text == saveData.Settings.LastSelectedModel) {
                            selectedModelText.Text = "Selected: " + (listItemBorder.Child as TextBlock).Text;
                        }
                    }
                    else
                    {
                        ollama.SelectedModel = models.First().Name;
                    }

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
                _intentionallyClosed = true;
                this.Close();
            }
        }

        private void openModelBrowserBtn_Click(object sender, RoutedEventArgs e)
        {
            ModelBrowserWaitOverlayGrid.Visibility = Visibility.Visible;
            List<Rectangle> rects = new List<Rectangle>();

            int rectCount = 10;
            int rectWidth = 20;
            int adjustableSpacing = 10;
            int spacing = (rectWidth * 2 + adjustableSpacing);

            for (int i = rectCount; i > 0; i--)
            {
                Rectangle tmprect = new Rectangle
                {
                    Width = rectWidth,
                    Height = rectWidth,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Fill = new SolidColorBrush(Colors.White),


                    //Margin = new Thickness(i * 50, 0, 0, 0)
                };

                
                double centerOffset = (i*spacing) - (spacing*rectCount/2f + rectWidth + adjustableSpacing/2);

                tmprect.Margin = new Thickness(centerOffset, 0, 0, 0);
                rects.Add(tmprect);
            }
            foreach ( Rectangle rect in rects )
            {
                ModelBrowserWaitOverlayGrid.Children.Add(rect);
            }


            Task.Run(() => {
                while (!manualCancelSources[2])
                {
                    Dispatcher.Invoke(() =>
                    {
                        int i = 0;
                        foreach (Rectangle rect in rects)
                        {
                            //rect.Height = Math.Abs(Math.Sin(180 * (_globalStopwatch.Elapsed.TotalMilliseconds + i * 100) / 1000 * Math.PI / 180)) * rect.Width * 2 + rect.Width / 2;
                            rect.Margin = new Thickness(
                                rect.Margin.Left,
                                rect.Margin.Top,
                                rect.Margin.Right,
                                Math.Abs(Math.Sin((_globalStopwatch.Elapsed.TotalMilliseconds/1000 * Math.PI / 180) * 180 + -i*3)) * rectWidth*3
                            );
                            
                            
                            i++;                        
                        }
                    });
                }

                Thread.Sleep(8); // Add a slight delay to prevent high CPU usage
            });
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
                    AddToConversationMemory(false, userInput);
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

                    botLastOutput = botOutput;
                    botOutput = "";
                    await INTERNAL_GenerateResponseStreams(userInput, botMsgTextBlock);

                    AddToConversationMemory(true, botOutput);
                });
            });
            
        }

        private void AddToConversationMemory(bool speaker, string message)
        {
            conversationMemory.Add(new KeyValuePair<bool, string>(speaker, message));
            //MessageBox.Show(conversationMemory[0]);
            if (memcap != 0)
            {
                while (conversationMemory.Count > memcap)
                {
                    conversationMemory.RemoveAt(0);
                }
            }
        }

        private async Task INTERNAL_GenerateResponseStreams(string userInput, TextBlock botMsgTextBlock)
        {
            // These are the last {memcap} messages from this conversation (the bottom one being the last message):
            StringBuilder conversationMemorySB = new StringBuilder();
            string formattedPrompt = llmPrompt;

            foreach (var message in conversationMemory)
            {
                string sender = (message.Key) ? "You" : "User";
                conversationMemorySB.Append($"'''{sender}: {message.Value}\n'''");
            }
            formattedPrompt = formattedPrompt
                .Replace("{username}", _UserDisplayName)
                .Replace("{msgB}", botLastOutput)
                .Replace("{msgU}", userInput)
                .Replace("{conversation}", conversationMemorySB.ToString())
                .Replace("{convolead}", 
                    (memcap>0)
                    ?"so far. These are the last {memcap} messages from this conversation (the bottom one being the last message):"
                    :"(with the bottom message being the latest message):"
                )
            ;

            MessageBox.Show(formattedPrompt);
            
            await foreach (var stream in ollama.GenerateAsync(formattedPrompt))
            {
                Dispatcher.Invoke(() =>
                {
                    botOutput += stream.Response;
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
                if (memoryCapTextbox.Text.Length > 5) throw new NotImplementedException();//MessageBox.Show("Implement rejection feature");
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

        private void winCtrlBtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            _intentionallyMinimized = true;
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
                if (!_intentionallyMinimized)
                {
                    this.WindowState = WindowState.Normal;
                }
            }
            if (this.WindowState == WindowState.Normal)
            {
                if (_intentionallyMinimized) MinimizeAnimation(false);
                _intentionallyMinimized = false;
            }
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
                        MemoryCap = 20,
                        LastSelectedModel = "none"
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
        public string LastSelectedModel { get; set; }
    }
}