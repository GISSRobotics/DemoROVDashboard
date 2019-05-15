using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Devices.SerialCommunication;
using System.Collections.ObjectModel;
using Windows.Storage.Streams;
using System.ComponentModel;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace DemoROVDashboard
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {

        ObservableCollection<Value> dataTable = new ObservableCollection<Value>();
        Value newValue = new Value();
        SerialDevice device = null;
        volatile string bigInputBuffer = "";
        volatile string cmdInputBuffer = "";
        const int BUFFER_LENGTH = 2000;
        volatile bool doReaderProcess = false;
        volatile bool readerProcessRunning = false;

        public MainPage()
        {
            this.InitializeComponent();

            FindSerialDevices();

            dataTable.Add(new Value { Key = "key123", Type = ValueType.STRING, ValueStr = "foobar" });

            Variables.ItemsSource = dataTable;
        }

        private void WriteToConsole(string text)
        {
            bigInputBuffer += text;
            if (bigInputBuffer.Length > BUFFER_LENGTH)
            {
                bigInputBuffer = bigInputBuffer.Substring(bigInputBuffer.Length - BUFFER_LENGTH);
            }
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { ConsoleView.Text = bigInputBuffer; ConsoleScroller.ChangeView(null, ConsoleScroller.ExtentHeight, null, true); });
        }

        private async void FindSerialDevices()
        {
            COMRefresh.IsEnabled = false;

            await DisconnectDevice();

            WriteToConsole("Searching for devices...\n");

            try
            {
                DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(SerialDevice.GetDeviceSelector());
                WriteToConsole("" + devices.Count + " device" + (devices.Count == 1 ? "" : "s") + " found.\n");
                COMSelector.ItemsSource = devices;
            }
            catch (Exception exception)
            {
                WriteToConsole("Couldn't find any devices: " + exception.Message + "\n");
            }

            COMRefresh.IsEnabled = true;
        }

        private async Task DisconnectDevice()
        {
            if (device != null)
            {
                WriteToConsole("Disconnecting...\n");
                doReaderProcess = false;
                while (readerProcessRunning)
                {
                    await Task.Delay(100);
                }
                try
                {
                    device.Dispose();
                    device = null;
                }
                catch (Exception exception)
                {
                    WriteToConsole("Failed to disconnect: " + exception.Message + "\n");
                    return;
                }


                WriteToConsole("Disconnected.\n");
            }
        }

        private async void ConnectToDevice(string Id)
        {
            await DisconnectDevice();

            WriteToConsole("Connecting...\n");
            try
            {
                device = await SerialDevice.FromIdAsync(Id);
                device.BaudRate = 9600;
                device.StopBits = SerialStopBitCount.One;
                device.DataBits = 8;
                device.Parity = SerialParity.None;
                device.Handshake = SerialHandshake.None;
            }
            catch (Exception exception)
            {
                WriteToConsole("Failed to connect: " + exception.Message + "\n");
                device = null;
                COMSelector.SelectedItem = null;
                return;
            }

            WriteToConsole("Connected.\n----------------------------------------\n\n");
            doReaderProcess = true;
            ReaderProcess();
        }

        private void COMSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count != 1)
            {
                return;
            }
            DeviceInformation deviceInfo = (DeviceInformation)e.AddedItems.First();
            ConnectToDevice(deviceInfo.Id);
        }

        private void COMRefresh_Click(object sender, RoutedEventArgs e)
        {
            FindSerialDevices();
        }

        private async void ReaderProcess()
        {
            readerProcessRunning = true;
            DataReader reader = new DataReader(device.InputStream);
            var buf = (new byte[1]).AsBuffer();
            int numError = 0;
            while (doReaderProcess && numError < 10)
            {
                try
                {
                    await device.InputStream.ReadAsync(buf, 1, InputStreamOptions.Partial);
                }
                catch (Exception exception)
                {
                    WriteToConsole("Error (" + (numError++) + "/10): " + exception.Message + "\n");
                    continue;
                }

                numError = 0;

                string newChar = System.Text.Encoding.UTF8.GetString(buf.ToArray());

                WriteToConsole(newChar);
                cmdInputBuffer += (char)buf.ToArray()[0];

                CheckCmd();
            }

            if (doReaderProcess)
            {
                DisconnectDevice();
            }
            doReaderProcess = false;
            readerProcessRunning = false;
        }

        private void CheckCmd()
        {
            if (cmdInputBuffer[0] != '~')
            {
                cmdInputBuffer = cmdInputBuffer.Substring(1);
            }
            else
            {
                if (cmdInputBuffer.EndsWith("\n") || cmdInputBuffer.EndsWith("\r"))
                {
                    // We got a command
                    if (cmdInputBuffer.Count((char a) => { return a == '-'; }) == 2)
                    {
                        // We got a variable!
                        // Syntax: ~[key]-[type]-[value]\n
                        cmdInputBuffer = cmdInputBuffer.Substring(1);
                        string key = cmdInputBuffer.Substring(0, cmdInputBuffer.IndexOf('-'));
                        cmdInputBuffer = cmdInputBuffer.Substring(key.Length + 1);
                        char typeStr = char.ToLower(cmdInputBuffer[0]);
                        ValueType type = ValueType.STRING;
                        switch (typeStr)
                        {
                            case 'i':
                                type = ValueType.INT;
                                break;
                            case 'f':
                                type = ValueType.FLOAT;
                                break;
                            default:
                                type = ValueType.STRING;
                                break;
                        }
                        cmdInputBuffer = cmdInputBuffer.Substring(2);
                        string value = cmdInputBuffer.Substring(0, cmdInputBuffer.Length - 1);

                        AddOrUpdateValue(new Value() { Key = key, Type = type, ValueRepr = value });
                    }

                    cmdInputBuffer = "";
                }
            }
        }

        private async void SendValue(Value value)
        {
            if (device == null)
            {
                return;
            }
            string serializedValue = value.Serialize();
            if (OutputShow.IsChecked == true)
            {
                WriteToConsole("[output] " + serializedValue);
            }
            DataWriter writer = new DataWriter(device.OutputStream);
            writer.WriteString(serializedValue);
            await writer.StoreAsync();
            writer.DetachStream();
            writer.Dispose();
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            string key = (string)(sender as Button).Tag;
            Value value = dataTable.FirstOrDefault((Value a) => { return a.Key == key; });
            SendValue(value);
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            string key = (string)(sender as Button).Tag;
            dataTable.Remove(dataTable.FirstOrDefault((Value a) => { return a.Key == key; }));
        }


        private void Add_Click(object sender, RoutedEventArgs e)
        {
            AddOrUpdateValue(newValue);
            SendValue(newValue);
        }

        private void AddOrUpdateValue(Value value)
        {
            Value entry = dataTable.FirstOrDefault((Value a) => { return a.Key == value.Key; });
            if (entry != null)
            {
                _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { entry.Type = value.Type; entry.ValueRepr = value.ValueRepr; });
            }
            else
            {
                dataTable.Add(new Value { Key = value.Key, Type = value.Type, ValueRepr = value.ValueRepr });
            }
        }
    }
}
