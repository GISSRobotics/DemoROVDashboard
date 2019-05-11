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
        SerialDevice device;
        volatile string bigInputBuffer = "";
        volatile string cmdInputBuffer = "";
        const int BUFFER_LENGTH = 2000;

        public MainPage()
        {
            this.InitializeComponent();

            SerialInit();

            dataTable.Add(new Value { Key = "key123", Type = ValueType.STRING, ValueStr = "foobar" });

            Variables.ItemsSource = dataTable;
        }

        private async void SerialInit()
        {
            int count = 0;

            DeviceInformationCollection devices = default(DeviceInformationCollection);

            while (count == 0)
            {
                bigInputBuffer += "Waiting for device...\n";
                ConsoleView.Text = bigInputBuffer;
                devices = await DeviceInformation.FindAllAsync(SerialDevice.GetDeviceSelector());
                count = devices.Count;
                await Task.Delay(1000);
            }

            bigInputBuffer += "Device found!\n\n";
            ConsoleView.Text = bigInputBuffer;

            device = await SerialDevice.FromIdAsync(devices.First().Id);
            device.BaudRate = 9600;
            device.StopBits = SerialStopBitCount.One;
            device.DataBits = 8;
            device.Parity = SerialParity.None;
            device.Handshake = SerialHandshake.None;

            await Task.Run(() => { ReaderProcess(); });
        }

        private async void ReaderProcess()
        {
            DataReader reader = new DataReader(device.InputStream);
            var buf = (new byte[1]).AsBuffer();
            while (true)
            {
                try
                {
                    await device.InputStream.ReadAsync(buf, 1, InputStreamOptions.Partial);
                } catch { }
                bigInputBuffer += (char)buf.ToArray()[0];
                if (bigInputBuffer.Length > BUFFER_LENGTH)
                {
                    bigInputBuffer = bigInputBuffer.Substring(bigInputBuffer.Length - BUFFER_LENGTH);
                }
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { ConsoleView.Text = bigInputBuffer; ConsoleScroller.ChangeView(null, ConsoleScroller.ExtentHeight, null, true); });
                cmdInputBuffer += (char)buf.ToArray()[0];

                await CheckCmd();
            }
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

                        Value entry = dataTable.FirstOrDefault((Value a) => { return a.Key == key; });
                        if (entry != null)
                        {
                            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { entry.Type = type; entry.ValueRepr = value; });
                            }
                        else
                        {
                            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { dataTable.Add(new Value() { Key = key, Type = type, ValueRepr = value }); });
                        }
                    }

                    cmdInputBuffer = "";
                }
            }
        }

        private async void SendValue(Value value)
        {
            string serializedValue = value.Serialize();
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
            dataTable.Add(new Value { Key = newValue.Key, Type = newValue.Type, ValueRepr = newValue.ValueRepr });
            SendValue(newValue);
            newValue = new Value();
        }
    }
}
