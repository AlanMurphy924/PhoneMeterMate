using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=391641

namespace PhoneMeterMate
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private const string NOT_READ = "Not yet read";
        private const string METER_DISCONNECTED = "OBC not connected to meter";

        private ApplicationDataContainer LocalSettings
        {
            get
            {
                return ApplicationData.Current.LocalSettings;
            }
        }

        public MainPage()
        {
            this.InitializeComponent();

            this.NavigationCacheMode = NavigationCacheMode.Required;
        }

        /// <summary>
        /// Invoked when this page is about to be displayed in a Frame.
        /// </summary>
        /// <param name="e">Event data that describes how this page was reached.
        /// This parameter is typically used to configure the page.</param>
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            // TODO: Prepare page for display here.

            // TODO: If your application contains multiple pages, ensure that you are
            // handling the hardware Back button by registering for the
            // Windows.Phone.UI.Input.HardwareButtons.BackPressed event.
            // If you are using the NavigationHelper provided by some templates,
            // this event is handled for you.

            txtMeterMateId.Text = MeterMateId;
        }

        private async void btnStart_Click(object sender, RoutedEventArgs e)
        {
            await StartProcessing();
        }

        private async Task StartProcessing()
        {
            stopProcessing = false;

            // Enable/disable buttons
            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;

            await ProcessMessages();
        }

        private bool stopProcessing = false;

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            StopProcessing();
        }

        private void StopProcessing()
        {
            // Enable/disable buttons
            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;

            SetTextBlock(txtTemperatureValue, NOT_READ, redBrush);
            SetTextBlock(txtDeliveryModeValue, NOT_READ, redBrush);
            SetTextBlock(txtProductFlowingValue, NOT_READ, redBrush);
            SetTextBlock(txtInErrorValue, NOT_READ, redBrush);
            SetTextBlock(txtInCalibrationValue, NOT_READ, redBrush);

            stopProcessing = true;
        }

        private void SetTextBlock(TextBlock block, string text, Brush brush)
        {
            block.Text = text;
            block.Foreground = brush;
        }

        private readonly Brush redBrush = new SolidColorBrush(Colors.Red);
        private readonly Brush greenBrush = new SolidColorBrush(Colors.LightGreen); 

        private async Task ProcessMessages()
        {
            using (StreamSocket socket = new StreamSocket())
            {
                HostName hostname = new HostName(string.Format("({0})", MeterMateId));
                //HostName hostname = new HostName("(00:0A:4F:01:21:78)");

                socket.Control.NoDelay = false;

                string command = string.Empty;

                try
                {
                    // Connect to the Bluetooth device
                    await socket.ConnectAsync(hostname, "{00001101-0000-1000-8000-00805f9b34fb}");

                    while (true)
                    {
                        // Check if we should stop
                        if (stopProcessing)
                        {
                            break;
                        }

                        // Get the command name
                        command = await ReadResponse(socket);

                        if (!string.IsNullOrWhiteSpace(command))
                        {
                            txtOutput.Text = command;

                            // Parse the JSON
                            JsonObject json = JsonObject.Parse(command);

                            string commandType = json.GetNamedString("Command");

                            if (string.Compare(commandType, "gt", StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                await ProcessTemperature(json);
                            }
                            else if (string.Compare(commandType, "gs", StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                await ProcessStatus(json);
                            }
                        }
                        else
                        {
                            Debug.WriteLine("Not connected. Retrying ...");

                            txtOutput.Text = "OBC Not connected.\r\nConnect & restart app.";

                            StopProcessing();
                        }
                    }
                }
                catch (Exception ex)
                {
                    string err = ex.Message;

                    txtOutput.Text = err;

                    SocketErrorStatus ses = SocketError.GetStatus(ex.HResult);

                    StopProcessing();
                }
                finally
                {
                    socket.Dispose();
                }
            }
        }

        private async Task ProcessStatus(JsonObject json)
        {
            if (GetResult(json) == 0)
            {
                // Retrieve status of delivery Mode
                bool inDeliveryMode = json.GetNamedBoolean("InDeliveryMode");

                SetTextBlock(txtDeliveryModeValue, inDeliveryMode.ToString(), greenBrush);

                // Retrieve status of product flowing
                bool isProductFlowing = json.GetNamedBoolean("ProductFlowing");

                SetTextBlock(txtProductFlowingValue, isProductFlowing.ToString(), greenBrush);

                // Retrieve status of error
                bool inError = json.GetNamedBoolean("Error");

                SetTextBlock(txtInErrorValue, inError.ToString(), greenBrush);

                // Retrieve status of calibration
                bool inCalibration = json.GetNamedBoolean("InCalibration");

                SetTextBlock(txtInCalibrationValue, inCalibration.ToString(), greenBrush);
            }
            else
            {
                SetTextBlock(txtDeliveryModeValue, METER_DISCONNECTED, redBrush);
                SetTextBlock(txtProductFlowingValue, METER_DISCONNECTED, redBrush);
                SetTextBlock(txtInErrorValue, METER_DISCONNECTED, redBrush);
                SetTextBlock(txtInCalibrationValue, METER_DISCONNECTED, redBrush);
            }

            await Task.Delay(1);
        }

        private async Task ProcessTemperature(JsonObject json)
        {
            if (GetResult(json) == 0)
            {
                // Retrieve the temperature
                double temperature = json.GetNamedNumber("Temp");

                // Output the temperature
                SetTextBlock(txtTemperatureValue, string.Format("{0:0.0} °C", temperature), greenBrush);
            }
            else
            {
                SetTextBlock(txtTemperatureValue, METER_DISCONNECTED, redBrush);
            }

            await Task.Delay(1);
        }

        private static int GetResult(JsonObject json)
        {
            return (int)json.GetNamedNumber("Result");
        }

        private async Task<string> ReadResponse(StreamSocket socket)
        {
            //txtSerialNo.Text = "Reading response ...";

            using (DataReader reader = new DataReader(socket.InputStream))
            {
                reader.InputStreamOptions = InputStreamOptions.Partial;

                reader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;

                reader.ByteOrder = ByteOrder.LittleEndian;

                StringBuilder command = new StringBuilder();

                string commandString = string.Empty;

                bool commandRetrieved = false;

                await reader.LoadAsync(1);

                while (reader.UnconsumedBufferLength > 0)
                {
                    byte b = reader.ReadByte();

                    switch (b)
                    {
                        case 0x02:
                            command.Clear();
                            break;

                        case 0x03:

                            try
                            {
                                // Case the boolean values correctly
                                commandString = command.Replace("False", "false").Replace("True", "true").ToString();

                                commandRetrieved = true;
                            }
                            catch (Exception ex)
                            {
                                //txtResult.Text = command.ToString();
                            }

                            command.Clear();

                            break;

                        default:
                            command.Append(Convert.ToChar(b));
                            break;
                    }

                    if (commandRetrieved)
                    {
                        break;
                    }

                    await reader.LoadAsync(1);
                }

                reader.DetachStream();

                return commandString;
            }
        }

        private string MeterMateId
        {
            get
            {
                if (LocalSettings.Values["meterMateId"] == null)
                {
                    return string.Empty;
                }

                return (string)LocalSettings.Values["meterMateId"];
            }

            set
            {
                LocalSettings.Values["meterMateId"] = value;  
            }
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            LocalSettings.Values["meterMateId"] = txtMeterMateId.Text;
        }
    }
}
