﻿using System;
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
using Windows.Gaming.Input;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace DemoROVDashboard
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        ObservableCollection<Value> dataTable = new ObservableCollection<Value>();
        readonly Value newValue = new Value();
        SerialDevice device = null;
        volatile string bigInputBuffer = "";
        volatile string cmdInputBuffer = "";
        const int BUFFER_LENGTH = 2000;
        volatile bool doReaderProcess = false;
        volatile bool readerProcessRunning = false;
        readonly List<RawGameController> gamepads = new List<RawGameController>();

        public MainPage()
        {
            this.InitializeComponent();

            FindSerialDevices();

            Variables.ItemsSource = dataTable;

            ControllerUpdateProcess();

            FullControllerUpdate();

            RawGameController.RawGameControllerAdded += ControllerAdded;
            RawGameController.RawGameControllerRemoved += ControllerRemoved;
        }

        private void ControllerAdded(object sender, RawGameController e)
        {
            if (!gamepads.Contains(e))
            {
                gamepads.Add(e);
            }

            FullControllerUpdate();
        }

        private async void ControllerRemoved(object sender, RawGameController e)
        {
            int indexRemoved = gamepads.IndexOf(e);

            if (indexRemoved > -1)
            {
                gamepads.RemoveAt(indexRemoved);
                int count = 1;
                while (count > 0)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        dataTable.Remove(dataTable.FirstOrDefault((Value a) => { return a.Key.StartsWith("HID/Gamepad/" + gamepads.Count); }));
                    });
                    count = dataTable.Count((Value a) => { return a.Key.StartsWith("HID/Gamepad/" + gamepads.Count); });
                }
            }

            FullControllerUpdate();
        }

        private async void FullControllerUpdate()
        {
            await AddOrUpdateValue($"HID/Gamepad", gamepads.Count, true);

            for (int i = 0; i < gamepads.Count; i++)
            {
                RawGameController gamepad = gamepads[i];
                await AddOrUpdateValue($"HID/Gamepad/{i}", gamepad.DisplayName, true);
                await AddOrUpdateValue($"HID/Gamepad/{i}/Axis", gamepad.AxisCount, true);
                await AddOrUpdateValue($"HID/Gamepad/{i}/Button", gamepad.ButtonCount, true);
                await AddOrUpdateValue($"HID/Gamepad/{i}/Switch", gamepad.SwitchCount, true);

                for (int j = 0; j < gamepad.ButtonCount; j++)
                {
                    await AddOrUpdateValue($"HID/Gamepad/{i}/Button/{j}/Name", gamepad.GetButtonLabel(j).ToString(), true);
                }

                for (int j = 0; j < gamepad.SwitchCount; j++)
                {
                    await AddOrUpdateValue($"HID/Gamepad/{i}/Switch/{j}/Kind", (int)gamepad.GetSwitchKind(j), true);
                }

                await AddOrUpdateValue($"HID/Gamepad/{i}/VID", (int)gamepad.HardwareVendorId, true);
                await AddOrUpdateValue($"HID/Gamepad/{i}/PID", (int)gamepad.HardwareProductId, true);
            }

            await ControllerReadingUpdate();
        }

        private async void ControllerUpdateProcess()
        {
            while (true)
            {
                await ControllerReadingUpdate();
                await Task.Delay(1);
            }
        }

        private async Task ControllerReadingUpdate()
        {
            for (int i = 0; i < gamepads.Count; i++)
            {
                RawGameController gamepad = gamepads[i];
                bool[] buttonArray = new bool[gamepad.ButtonCount];
                GameControllerSwitchPosition[] switchArray = new GameControllerSwitchPosition[gamepad.SwitchCount];
                double[] axisArray = new double[gamepad.AxisCount];
                ulong timestamp = gamepad.GetCurrentReading(buttonArray, switchArray, axisArray);
                System.Diagnostics.Debug.WriteLine(timestamp);

                for (int j = 0; j < gamepad.AxisCount; j++)
                {
                    await AddOrUpdateValue($"HID/Gamepad/{i}/Axis/{j}", axisArray[j], true);
                }

                for (int j = 0; j < gamepad.ButtonCount; j++)
                {
                    await AddOrUpdateValue($"HID/Gamepad/{i}/Button/{j}", buttonArray[j], true);
                }

                for (int j = 0; j < gamepad.SwitchCount; j++)
                {
                    await AddOrUpdateValue($"HID/Gamepad/{i}/Switch/{j}", (int)switchArray[j], true);
                }

                //AddOrUpdateValue($"HID/Gamepad/{i}/Battery", (int)gamepad.TryGetBatteryReport().Status, true);
            }
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

                await CheckCmd();
            }

            if (doReaderProcess)
            {
                await DisconnectDevice();
            }
            doReaderProcess = false;
            readerProcessRunning = false;
        }

        private async Task CheckCmd()
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

                        await AddOrUpdateValue(key, type, value);
                    }

                    cmdInputBuffer = "";
                }
            }
        }

        private async Task SendValue(Value value)
        {
            string serializedValue = value.Serialize();
            if (OutputShow.IsChecked == true)
            {
                WriteToConsole("[output] " + serializedValue);
            }
            if (device == null)
            {
                return;
            }
            DataWriter writer = new DataWriter(device.OutputStream);
            writer.WriteString(serializedValue);
            await writer.StoreAsync();
            writer.DetachStream();
            writer.Dispose();
        }

        private async void Send_Click(object sender, RoutedEventArgs e)
        {
            string key = (string)(sender as Button).Tag;
            Value value = dataTable.FirstOrDefault((Value a) => { return a.Key == key; });
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () => { await SendValue(value); });
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            string key = (string)(sender as Button).Tag;
            dataTable.Remove(dataTable.FirstOrDefault((Value a) => { return a.Key == key; }));
        }


        private async void Add_Click(object sender, RoutedEventArgs e)
        {
            await AddOrUpdateValue(newValue, true);
        }

        private async Task AddOrUpdateValue(Value value, bool send = false)
        {
            Value entry = dataTable.FirstOrDefault((Value a) => { return a.Key == value.Key; });
            if (entry != null)
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { entry.Type = value.Type; entry.ValueRepr = value.ValueRepr; });
            }
            else
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => {
                    dataTable.Add(new Value { Key = value.Key, Type = value.Type, ValueRepr = value.ValueRepr });
                    var a = (from item in dataTable
                                            orderby item.Key
                                            select item);
                    dataTable = new ObservableCollection<Value>(a);
                    Variables.ItemsSource = dataTable;
                });
            }

            if (send)
            {
                SendValue(value);
            }
        }

        private async Task AddOrUpdateValue(string Key, ValueType Type, string ValueRepr, bool send = false)
        {
            await AddOrUpdateValue(new Value { Key = Key, Type = Type, ValueRepr = ValueRepr }, send);
        }

        private async Task AddOrUpdateValue(string Key, int Value, bool send = false)
        {
            await AddOrUpdateValue(Key, ValueType.INT, Value.ToString(), send);
        }
        private async Task AddOrUpdateValue(string Key, double Value, bool send = false)
        {
            await AddOrUpdateValue(Key, ValueType.FLOAT, Value.ToString(), send);
        }

        private async Task AddOrUpdateValue(string Key, string Value, bool send = false)
        {
            await AddOrUpdateValue(Key, ValueType.STRING, Value, send);
        }

        private async Task AddOrUpdateValue(string Key, bool Value, bool send = false)
        {
            await AddOrUpdateValue(Key, Convert.ToInt32(Value), send);
        }
    }
}
