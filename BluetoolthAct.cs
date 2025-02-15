using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;

using Windows.Security.Cryptography;
using System.Net.NetworkInformation;
using Windows.Storage.Streams;

namespace YmodenOnBluetoolthLE
{
    public enum MsgType
    {
        NotifyTxt,
        BleDevice,
        BleSendData,
        BleRecData,
    }

    class BLEAct
    {

        #region global members

        private bool asyncLock = false;

        /// <summary>
        /// 搜索蓝牙设备对象
        /// </summary>
        private DeviceWatcher deviceWatcher;

        /// <summary>
        /// 当前连接的服务
        /// </summary>
        public GattDeviceService CurrentService { get; set; }

        /// <summary>
        /// 当前连接的蓝牙设备
        /// </summary>
        public BluetoothLEDevice CurrentDevice { get; set; }

        /// <summary>
        /// 写特征对象
        /// </summary>
        public GattCharacteristic CurrentWriteCharacteristic { get; set; }

        /// <summary>
        /// 写名称特征对象
        /// </summary>
        public GattCharacteristic CurrentNameCharacteristic { get; set; }

        /// <summary>
        /// 通知特征对象
        /// </summary>
        public GattCharacteristic CurrentNotifyCharacteristic { get; set; }

        /// <summary>
        /// 特性通知类型通知启用
        /// </summary>
        private const GattClientCharacteristicConfigurationDescriptorValue
            CHARACTERSITIC_NOTIFICATION_TYPE = GattClientCharacteristicConfigurationDescriptorValue.Notify;

        /// <summary>
        /// 存储检测到的设备
        /// </summary>
        private List<BluetoothLEDevice> DevicesList = new List<BluetoothLEDevice>();

        /// <summary>
        /// 定义搜索蓝牙设备委托
        /// </summary>
        /// <param name="type"></param>
        /// <param name="bluetoothLEDevice"></param>
        public delegate void DevicewatcherChangedEvent(MsgType type, BluetoothLEDevice bluetoothLEDevice);

        /// <summary>
        /// 搜索蓝牙事件
        /// </summary>
        public event DevicewatcherChangedEvent DevicewatcherChanged;

        /// <summary>
        /// 获取服务委托
        /// </summary>
        /// <param name="gattDeviceService"></param>
        public delegate void GattDeviceServiceAddedEvent(GattDeviceService gattDeviceService);

        /// <summary>
        /// 获取服务事件
        /// </summary>
        public event GattDeviceServiceAddedEvent GattDeviceServiceAdded;

        /// <summary>
        /// 获取特征委托
        /// </summary>
        /// <param name="gattCharacteristic"></param>
        public delegate void CharacteristicAddedEvent(GattCharacteristic gattCharacteristic);

        /// <summary>
        /// 获取特征事件
        /// </summary>
        public event CharacteristicAddedEvent CharacteristicAdded;

        /// <summary>
        /// 提示信息委托
        /// </summary>
        /// <param name="type"></param>
        /// <param name="message"></param>
        /// <param name="data"></param>
        public delegate bool MessageChangedEvent(MsgType type, string message, byte[] data = null);

        /// <summary>
        /// 提示信息事件
        /// </summary>
        public event MessageChangedEvent MessageChanged;

        ///// <summary>
        ///// 发布广播消息委托
        ///// </summary>
        ///// <param name=""></param>
        //public delegate void AdvertisementStatusEvent(BluetoothLEAdvertisementPublisher AdvStatusPublisher, BluetoothLEAdvertisementPublisherStatusChangedEventArgs e);
        ///// <summary>
        ///// 发布广播事件
        ///// </summary>
        //public event AdvertisementStatusEvent AdvertisementStatusChanged;
        

        /// <summary>
        /// 当前连接蓝牙的Mac
        /// </summary>
        private string CurrentDeviceMAC { get; set; }

        BluetoothLEAdvertisementWatcher watcher;

        #endregion


        public BLEAct()
        {

        }


        /// <summary>
        /// 搜索蓝牙设备 方法1
        /// </summary>
        public void StartBleDevicewatcher_1()
        {

            string[] requestedProperties = {
                "System.Devices.Aep.DeviceAddress",
                "System.Devices.Aep.IsConnected",
                "System.Devices.Aep.Bluetooth.Le.IsConnectable" ,
                "System.Devices.Aep.SignalStrength",
                "System.Devices.Aep.IsPresent"
           };
            string aqsAllBluetoothLEDevices = "(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";

            string Selector = "System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\"";
            string selector = "(" + Selector + ")" + " AND (System.Devices.Aep.CanPair:=System.StructuredQueryType.Boolean#True OR System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True)";


            this.deviceWatcher =
                DeviceInformation.CreateWatcher(
                 //  aqsAllBluetoothLEDevices,

                 //  BluetoothLEDevice.GetDeviceSelectorFromPairingState(false),
                 selector,
                    requestedProperties,
                    DeviceInformationKind.AssociationEndpoint);

            //在监控之前注册事件
            this.deviceWatcher.Added += DeviceWatcher_Added;
            this.deviceWatcher.Stopped += DeviceWatcher_Stopped;
            this.deviceWatcher.Updated += DeviceWatcher_Updated; ;
            this.deviceWatcher.Start();
            string msg = "自动发现设备中..";
            this.MessageChanged(MsgType.NotifyTxt, msg);
        }

        /// <summary>
        /// 搜索蓝牙设备 方法2
        /// </summary>
        public void StartBleDevicewatcher()
        {
            watcher = new BluetoothLEAdvertisementWatcher();

            watcher.ScanningMode = BluetoothLEScanningMode.Active;
            //允许接收广播消息
            //watcher.AllowExtendedAdvertisements = true;
            //var manufacturerDataWriter = new DataWriter();
            //var manufacturerData = new BluetoothLEManufacturerData { CompanyId = 0x3906 };
            //watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add(manufacturerData);
            //_

            // only activate the watcher when we're recieving values >= -80
            watcher.SignalStrengthFilter.InRangeThresholdInDBm = -80;

            // stop watching if the value drops below -90 (user walked away)
            watcher.SignalStrengthFilter.OutOfRangeThresholdInDBm = -90;

            // register callback for when we see an advertisements
            watcher.Received += OnAdvertisementReceived;
            watcher.Stopped += Watcher_Stopped;

            // wait 5 seconds to make sure the device is really out of range
            watcher.SignalStrengthFilter.OutOfRangeTimeout = TimeSpan.FromMilliseconds(5000);
            watcher.SignalStrengthFilter.SamplingInterval = TimeSpan.FromMilliseconds(2000);

            // starting watching for advertisements
            watcher.Start();
            string msg = "自动发现设备中..";

            this.MessageChanged(MsgType.NotifyTxt, msg);
        }

        private void Watcher_Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            DevicesList.Clear();
            string msg = "自动发现设备停止";
            this.MessageChanged(MsgType.NotifyTxt, msg);
        }

        private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementReceivedEventArgs eventArgs)
        {
            BluetoothLEDevice.FromBluetoothAddressAsync(eventArgs.BluetoothAddress).Completed = async (asyncInfo, asyncStatus) =>
            {
                if (asyncStatus == AsyncStatus.Completed)
                {
                    if (asyncInfo.GetResults() == null)
                    {
                        //this.MessAgeChanged(MsgType.NotifyTxt, "没有得到结果集");
                    }
                    else
                    {
                        BluetoothLEDevice currentDevice = asyncInfo.GetResults();
                        if (currentDevice.Name.StartsWith("Bluetooth"))
                        {
                            return;
                        }

                        Boolean contain = false;
                        foreach (BluetoothLEDevice device in DevicesList)//过滤重复的设备
                        {
                            if (device.DeviceId == currentDevice.DeviceId)
                            {
                                contain = true;
                            }
                        }
                        if (!contain)
                        {
                            byte[] _Bytes1 = BitConverter.GetBytes(currentDevice.BluetoothAddress);
                            Array.Reverse(_Bytes1);

                            this.DevicesList.Add(currentDevice);
                            this.MessageChanged(MsgType.NotifyTxt, "发现设备：" + currentDevice.Name + "  address:" + BitConverter.ToString(_Bytes1, 2, 6).Replace('-', ':').ToLower());
                            this.DevicewatcherChanged(MsgType.BleDevice, currentDevice);
                        }


                        //接收广播消息
                        foreach (var item in eventArgs.Advertisement.ManufacturerData)
                        {
                            var dataBuffer = item.Data;
                            var message = new byte[dataBuffer.Length];
                            using (var reader = DataReader.FromBuffer(dataBuffer))
                            {
                                reader.ReadBytes(message);
                            }
                            Console.WriteLine(BitConverter.ToString(message));
                        }
                    }

                }
            };
        }

        /// <summary>
        /// 停止搜索
        /// </summary>
        public void StopBleDeviceWatcher()
        {
            if (deviceWatcher != null)
                this.deviceWatcher.Stop();

            if (watcher != null && watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
                watcher.Stop();
        }

        /// <summary>
        /// 获取发现的蓝牙设备
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation args)
        {
            this.MessageChanged(MsgType.NotifyTxt, "发现设备：" + args.Id);
            this.Matching(args.Id, args);
        }

        private void DeviceWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate args)
        {

        }

        /// <summary>
        /// 停止搜索蓝牙设备
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void DeviceWatcher_Stopped(DeviceWatcher sender, object args)
        {
            string msg = "自动发现设备停止";
            this.MessageChanged(MsgType.NotifyTxt, msg);
        }


        /// <summary>
        /// 匹配
        /// </summary>
        /// <param name="device"></param>
        public void StartMatching(BluetoothLEDevice device)
        {
            this.CurrentDevice = device;
        }

        /// <summary>
        /// 获取蓝牙服务
        /// </summary>
        public async void FindService()
        {
            this.CurrentDevice.GetGattServicesAsync().Completed = async (asyncInfo, asyncStatu) =>
            {
                if (asyncStatu == AsyncStatus.Completed)
                {
                    var sevices = asyncInfo.GetResults().Services;
                    foreach (GattDeviceService ser in sevices)
                    {
                        this.GattDeviceServiceAdded(ser);
                    }
                }
            };
        }

        /// <summary>
        /// 获取特征
        /// </summary>
        /// <param name="gattDeviceService"></param>
        /// <param name="nReadWritePos">Read/Write position in GattDeviceService</param>
        public async void FindCharacteristic(GattDeviceService gattDeviceService)
        {
            int nPos = 0,  nReadWrite = 0;
            gattDeviceService.GetCharacteristicsAsync().Completed = async (asyncInfo, asyncStatu) =>
            {
                if (asyncStatu == AsyncStatus.Completed)
                {
                    var chara = asyncInfo.GetResults().Characteristics;
                    foreach (GattCharacteristic c in chara)
                    {
                        this.CharacteristicAdded(c);
                        Console.WriteLine("Characteristic::" + c.Uuid.ToString() + " Property::" + c.CharacteristicProperties.ToString());
                    }
                }
            };
        }


        /// <summary>
        /// 获取操作
        /// </summary>
        /// <param name="gattCharacteristic"></param>
        /// <returns></returns>
        public async Task SetOpteron(GattCharacteristic gattCharacteristic)
        {
            if (gattCharacteristic.CharacteristicProperties == (GattCharacteristicProperties.WriteWithoutResponse | GattCharacteristicProperties.Write))
            {              
                this.CurrentNameCharacteristic = gattCharacteristic;
            }
            if (gattCharacteristic.CharacteristicProperties == GattCharacteristicProperties.Notify)
            {
                //this.CurrentNameCharacteristic = gattCharacteristic;

                this.CurrentNotifyCharacteristic = gattCharacteristic;
                this.CurrentNotifyCharacteristic.ProtectionLevel = GattProtectionLevel.Plain;
                this.CurrentNotifyCharacteristic.ValueChanged += Characteristic_ValueChanged;
                await this.EnableNotifications(CurrentNotifyCharacteristic);

                this.Connect();
            }

            if (gattCharacteristic.CharacteristicProperties == (GattCharacteristicProperties.Write | GattCharacteristicProperties.Read)) 
            {
                this.CurrentWriteCharacteristic = gattCharacteristic;
            }
            
            /*
            if (gattCharacteristic.CharacteristicProperties == GattCharacteristicProperties.WriteWithoutResponse)
            {
                this.CurrentWriteCharacteristic = gattCharacteristic;
            }
            if (gattCharacteristic.CharacteristicProperties == GattCharacteristicProperties.Notify)
            {
                this.CurrentNotifyCharacteristic = gattCharacteristic;
                this.CurrentNotifyCharacteristic.ProtectionLevel = GattProtectionLevel.Plain;
                this.CurrentNotifyCharacteristic.ValueChanged += Characteristic_ValueChanged;
                await this.EnableNotifications(CurrentNotifyCharacteristic);
            }

            if (gattCharacteristic.CharacteristicProperties == (GattCharacteristicProperties.Read | GattCharacteristicProperties.Write))
            {
                this.CurrentWriteCharacteristic = gattCharacteristic;
            }
            */


        }

        public async Task Connect()
        {
            byte[] _Bytes1 = BitConverter.GetBytes(this.CurrentDevice.BluetoothAddress);
            Array.Reverse(_Bytes1);
            this.CurrentDeviceMAC = BitConverter.ToString(_Bytes1, 2, 6).Replace('-', ':').ToLower();

            string msg = "正在连接设备<" + this.CurrentDeviceMAC + "> ..";
            this.MessageChanged(MsgType.NotifyTxt, msg);
            this.CurrentDevice.ConnectionStatusChanged += CurrentDevice_ConnectionStatusChanged;

        }

        private void CurrentDevice_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected &&
                  CurrentDeviceMAC != null)
            {
                string msg = "设备已断开，自动重连";
                MessageChanged(MsgType.NotifyTxt, msg);

                if (!asyncLock)
                {
                    asyncLock = true;
                    this.CurrentDevice.Dispose();
                    this.CurrentDevice = null;

                    this.CurrentNotifyCharacteristic = null;
                    this.CurrentWriteCharacteristic = null;
                    SelectDeviceFromIdAsync(CurrentDeviceMAC);
                }
            }
            else
            {
                string msg = "设备已连接";
                MessageChanged(MsgType.NotifyTxt, msg);
            }
        }

        /// <summary>
        /// 搜索到的蓝牙设备
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        private async Task Matching(string id, DeviceInformation args = null)
        {
            try
            {
                BluetoothLEDevice.FromIdAsync(id).Completed = async (asyncInfo, asyncStatus) =>
                {
                    if (asyncStatus == AsyncStatus.Completed)
                    {
                        BluetoothLEDevice bleDevice = asyncInfo.GetResults();
                        if (bleDevice.Name.StartsWith("Bluetooth"))
                        {
                            return;
                        }

                        BluetoothLEDevice tmp = this.DevicesList.Where(p => p.Name == bleDevice.Name).FirstOrDefault();
                        if (tmp == null)
                        {//没有添加过
                         //  bool state = IsConnectable(bleDevice.DeviceInformation);
                            this.DevicesList.Add(bleDevice);

                            this.DevicewatcherChanged(MsgType.BleDevice, bleDevice);
                        }
                    }
                };
            }
            catch (Exception e)
            {
                string msg = "没有发现设备" + e.ToString();
                this.MessageChanged(MsgType.NotifyTxt, msg);
                this.StopBleDeviceWatcher();
            }
        }


        /// <summary>
        /// 主动断开连接
        /// </summary>
        public void Dispose()
        {
            CurrentDeviceMAC = null;
            //使用到的服务 （这里仅仅使用了一个服务）
            CurrentService?.Dispose();
            //蓝牙
            if (CurrentDevice != null)
                CurrentDevice.ConnectionStatusChanged -= CurrentDevice_ConnectionStatusChanged;
            //关闭
            CurrentDevice?.Dispose();

            CurrentDevice = null;
            CurrentService = null;

            //特征值
            CurrentNameCharacteristic = null;
            CurrentWriteCharacteristic = null;
            CurrentNotifyCharacteristic = null;
            this.MessageChanged(MsgType.NotifyTxt, "主动断开连接");
        }

        /// <summary>
        /// 按MAC地址直接组装设备ID查找设备
        /// </summary>
        /// <param name="MAC"></param>
        /// <returns></returns>
        public async Task SelectDeviceFromIdAsync(string MAC)
        {
            CurrentDeviceMAC = MAC;
            CurrentDevice = null;

            BluetoothAdapter.GetDefaultAsync().Completed = async (asyncInfo, asyncStatus) =>
            {
                if (asyncStatus == AsyncStatus.Completed)
                {
                    BluetoothAdapter bluetoothAdapter = asyncInfo.GetResults();
                    // ulong 转为byte数组
                    byte[] _Bytes1 = BitConverter.GetBytes(bluetoothAdapter.BluetoothAddress);
                    Array.Reverse(_Bytes1);
                    string macAddress = BitConverter.ToString(_Bytes1, 2, 6).Replace('-', ':').ToLower();
                    string Id = "BluetoothLe#BluetoothLe" + macAddress + "-" + MAC;
                    await Matching(Id);
                }
            };
        }

        /// <summary>
        /// 设置特征对象为接收通知对象
        /// </summary>
        /// <param name="characteristic"></param>
        /// <returns></returns>
        public async Task EnableNotifications(GattCharacteristic characteristic)
        {
            string msg = "收通知对象=" + CurrentDevice.ConnectionStatus;
            this.MessageChanged(MsgType.NotifyTxt, msg);

            characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(CHARACTERSITIC_NOTIFICATION_TYPE).Completed = async (asyncInfo, asyncStatus) =>
            {
                if (asyncStatus == AsyncStatus.Completed)
                {
                    GattCommunicationStatus status = asyncInfo.GetResults();
                    if (status == GattCommunicationStatus.Unreachable)
                    {
                        msg = "设备不可用";
                        this.MessageChanged(MsgType.NotifyTxt, msg);
                        if (CurrentNotifyCharacteristic != null && !asyncLock)
                        {
                            await this.EnableNotifications(CurrentNotifyCharacteristic);
                        }
                    }
                    asyncLock = false;
                    msg = "设备连接状态" + status;
                    this.MessageChanged(MsgType.NotifyTxt, msg);
                }
            };
        }

        /// <summary>
        /// 接受到蓝牙数据
        /// </summary>
        private void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] data;
            CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out data);
            string str = BitConverter.ToString(data);
            this.MessageChanged(MsgType.BleRecData, str, data);

            //GattReadResult result = sender.ReadValueAsync().GetResults();
            //if (result.Status == GattCommunicationStatus.Success)
            //{
            //    var reader = DataReader.FromBuffer(result.Value);
            //    byte[] input = new byte[reader.UnconsumedBufferLength];
            //    reader.ReadBytes(input);
            //    // Utilize the data as needed
            //    string str = BitConverter.ToString(input);
            //    this.MessageChanged(MsgType.BleRecData, str, input);
            //}
        }

        /// <summary>
        /// 发送数据接口
        /// </summary>
        /// <returns></returns>
        public async Task Write(byte[] data)
        {
            if (CurrentWriteCharacteristic != null)
            {
                CurrentWriteCharacteristic.WriteValueAsync(CryptographicBuffer.CreateFromByteArray(data), GattWriteOption.WriteWithResponse);
                string str = "发送数据：" + BitConverter.ToString(data);
                this.MessageChanged(MsgType.BleSendData, str, data);

                //var writer = new DataWriter();
                //// WriteByte used for simplicity. Other common functions - WriteInt16 and WriteSingle
                //writer.WriteBytes(data);

                //GattCommunicationStatus result = await CurrentWriteCharacteristic.WriteValueAsync(writer.DetachBuffer());
                //if (result == GattCommunicationStatus.Success)
                //{
                //    // Successfully wrote to device
                //    string str = "发送数据：" + BitConverter.ToString(data);
                //    this.MessageChanged(MsgType.BleSendData, str, data);
                //}
            }
        }

        /// <summary>
        /// 发送数据接口
        /// </summary>
        /// <returns></returns>
        public async Task WriteName(byte[] data)
        {
            if (CurrentNameCharacteristic != null)
            {
                CurrentNameCharacteristic.WriteValueAsync(CryptographicBuffer.CreateFromByteArray(data), GattWriteOption.WriteWithoutResponse);
                string str = "发送数据：" + BitConverter.ToString(data);
                this.MessageChanged(MsgType.BleSendData, str, data);
            }
        }

    }


    class BLE_APPLY
    {
        #region inner use members

        BLEAct bleCore;
        bool Closing;
        bool CheckForIllegalCrossThreadCalls;

        /// <summary>
        /// 存储检测到的设备
        /// </summary>
        public List<BluetoothLEDevice> DeviceList = new List<BluetoothLEDevice>();

        /// <summary>
        /// 当前蓝牙服务列表
        /// </summary>
        List<GattDeviceService> GattDeviceServices = new List<GattDeviceService>();

        /// <summary>
        /// 当前蓝牙服务特征列表
        /// </summary>
        List<GattCharacteristic> GattCharacteristics = new List<GattCharacteristic>();

        public bool mReadEnable;
        public bool mWriteEnable;

        public byte[] gRecvBuff;
        #endregion

        /// <summary>
        /// 关闭蓝牙
        /// </summary>
        public void disCon_BLE_Dev(string strBTAddress)
        {
            //找到在蓝牙列表里面执行当前蓝牙的对象
            string deviceName = strBTAddress;
            Windows.Devices.Bluetooth.BluetoothLEDevice bluetoothLEDevice =
                this.DeviceList.Where(u => u.Name == deviceName).FirstOrDefault();

            //从列表中移除
            if (bluetoothLEDevice != null)
            {
                this.DeviceList.Remove(bluetoothLEDevice);
            }

            //关闭该蓝牙的所有服务
            foreach (var sev in GattDeviceServices)
            {
                sev.Dispose();
            }
            //并清空
            GattDeviceServices.Clear();
            GattCharacteristics.Clear();

            if (bluetoothLEDevice != null)
            {//关闭列表中的蓝牙
                bluetoothLEDevice.Dispose();
            }
            bluetoothLEDevice = null;

            //蓝牙类的关闭
            bleCore.Dispose();
            //释放内存
            GC.Collect();
            Closing = true;
        }

        public BLE_APPLY()
        {
            Closing = false;
            CheckForIllegalCrossThreadCalls = false;
            bleCore = new BLEAct();
            bleCore.MessageChanged += BleCore_MessAgeChanged;
            bleCore.DevicewatcherChanged += BleCore_DeviceWatcherChanged;
            bleCore.GattDeviceServiceAdded += BleCore_GattDeviceServiceAdded;
            bleCore.CharacteristicAdded += BleCore_CharacteristicAdded;

            gRecvBuff = new byte[2];
        }

        /// <summary>
        /// 发送广播包(驱动下位机进入Ymoden协议)
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public void SendBroadcastPackage(byte[] data)
        {
            BluetoothLEAdvertisementPublisher searchPublisher = new BluetoothLEAdvertisementPublisher();

            var manufacturerDataWriter = new DataWriter();
            manufacturerDataWriter.WriteBytes(data);

            searchPublisher.Advertisement.ManufacturerData.Add(new BluetoothLEManufacturerData
            {
                CompanyId = 0x3906,  // 这个ID请根据情况自行设置
                Data = manufacturerDataWriter.DetachBuffer()
            });
        }

        // 异步线程
        private static void RunAsync(Action action)
        {
            ((Action)(delegate ()
            {
                action.Invoke();
            })).BeginInvoke(null, null);
        }

        /// <summary>
        /// 搜索蓝牙
        /// </summary>
        /// <param name="bStartStop">开始查找/停止</param>
        public void Search_BLE_Dev(bool bStartStop)
        {
            try
            {
                if (bStartStop)
                {
                    bleCore.StartBleDevicewatcher();
                }
                else
                {
                    bleCore.StopBleDeviceWatcher();
                }
            }
            catch (Exception ex)
            {
                Console.Write(ex.Message);
            }
        }

        /// <summary>
        /// 提示消息(接收到的消息)
        /// </summary>
        public bool BleCore_MessAgeChanged(MsgType type, string message, byte[] data)
        {
            bool bRight = false;
            if (Closing)
                return bRight;

            RunAsync(() =>
            {
                Console.WriteLine(message);
                if (type is MsgType.BleRecData)
                {
                    gRecvBuff = data;
                    bRight = true;
                }
            });
            return bRight;
        }

        /// <summary>
        /// 搜索蓝牙设备列表
        /// </summary>
        private void BleCore_DeviceWatcherChanged(MsgType type, BluetoothLEDevice bluetoothLEDevice)
        {
            if (Closing)
                return;

            RunAsync(() =>
            {
                this.DeviceList.Add(bluetoothLEDevice);
            });
        }

        /// <summary>
        /// 连接蓝牙
        /// </summary>
        public bool Connect(string deviceName)
        {
            Windows.Devices.Bluetooth.BluetoothLEDevice bluetoothLEDevice =
                this.DeviceList.Where(u => u.Name == deviceName).FirstOrDefault();

            if (bluetoothLEDevice == null)
            {
                Console.Write("没有发现此蓝牙，请重新搜索");
                return false;
            }
            //两个蓝牙进行匹配
            bleCore.StartMatching(bluetoothLEDevice);

            return true;
        }

        /// <summary>
        /// 获取服务
        /// </summary>
        public void BtnServes()
        {
            RunAsync(() =>
            {
                this.bleCore.FindService();
            });
        }

        /// <summary>
        /// 获取服务列表
        /// </summary>
        /// <param name="gattDeviceService"></param>
        private void BleCore_GattDeviceServiceAdded(GattDeviceService gattDeviceService)
        {
            RunAsync(() =>
             {
                 GattDeviceServices.Add(gattDeviceService);
                 Console.WriteLine("GattDeviceService::" + gattDeviceService.Uuid);
             });
        }

        /// <summary>
        /// 获取特征
        /// </summary>
        public void BtnFeatures()
        {     
            RunAsync(() =>
            {
                foreach (GattDeviceService gs in GattDeviceServices)
                {
                    if (gs.Device.Name.Contains("Minew"))
                    {
                        string strSelDevUUID;
                        strSelDevUUID = gs.Uuid.ToString();
                        var item = GattDeviceServices.Where(u => u.Uuid == new Guid(strSelDevUUID)).FirstOrDefault();
                        //获取蓝牙特征
                        this.bleCore.FindCharacteristic(item);
                    }
                }
            });
        }

        /// <summary>
        /// 获取特征列表
        /// </summary>
        /// <param name="gattCharacteristic"></param>
        private void BleCore_CharacteristicAdded(GattCharacteristic gattCharacteristic)
        {
            RunAsync(() =>
             {
                 GattCharacteristics.Add(gattCharacteristic);
             });
        }


        /// <summary>
        /// 获取操作
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void BtnOpt(int nCharacteristics =0) 
        {
            int nReadWritePos = 0;
            string strUUID = string.Empty;
            foreach (GattCharacteristic gc in GattCharacteristics)
            {
                if(nCharacteristics == 5)
                {
                    if (gc.CharacteristicProperties == (GattCharacteristicProperties.Notify))
                    {
                        strUUID = GattCharacteristics[nReadWritePos].Uuid.ToString();
                        var item = this.GattCharacteristics.Where(u => u.Uuid == new Guid(strUUID)).FirstOrDefault();
                        //获取操作
                        bleCore.SetOpteron(item);
                        break;
                    }
                }
                else if (nCharacteristics == 6)
                {
                    if (gc.CharacteristicProperties == (GattCharacteristicProperties.WriteWithoutResponse | GattCharacteristicProperties.Write))
                    {
                        strUUID = GattCharacteristics[nReadWritePos].Uuid.ToString();
                        var item = this.GattCharacteristics.Where(u => u.Uuid == new Guid(strUUID)).FirstOrDefault();
                        //获取操作
                        bleCore.SetOpteron(item);

                        break;
                    }               
                }
                else if(nCharacteristics == 0)
                {
                    if (gc.CharacteristicProperties == (GattCharacteristicProperties.Read | GattCharacteristicProperties.Write))
                    {
                        strUUID = GattCharacteristics[nReadWritePos].Uuid.ToString();
                        var item = this.GattCharacteristics.Where(u => u.Uuid == new Guid(strUUID)).FirstOrDefault();
                        //获取操作
                        bleCore.SetOpteron(item);

                        if (item.CharacteristicProperties == (GattCharacteristicProperties.Read | GattCharacteristicProperties.Write))
                        {
                            mReadEnable = true;
                            mWriteEnable = true;
                        }
                        break;
                    }
                }

                nReadWritePos++;
            }  
        }

        /// <summary>
        /// 读取
        /// </summary>
        /// <param name="strReadWriteInfo">从设备读取的数据</param>
        /// <param name="e"></param>
        public void Read(string strReadWriteInfo)
        {
            string str = strReadWriteInfo;
            string[] arr = str.Split(' ');

            byte[] buffer = new byte[7];
            for (int i = 0; i < arr.Length; i++)
            {
                buffer[i] = Convert.ToByte(arr[i], 16);
            }
            //CRC校验
            ushort crc = CRC(buffer, 5);

            buffer[5] = (byte)((crc & 0xFF00) >> 8);
            buffer[6] = (byte)((crc & 0x00FF));

            bleCore.Write(buffer);
        }

        /// <summary>
        /// 写入
        /// </summary>
        /// <param name="strReadWriteInfo">通过蓝牙要发送的信息</param>
        public void Write(string strReadWriteInfo)
        {
            //string str = strReadWriteInfo;
            //string[] arr = str.Split(' ');

            //byte[] buffer = new byte[9];
            //for (int i = 0; i < arr.Length; i++)
            //{
            //    buffer[i] = Convert.ToByte(arr[i], 16);
            //}
            ////CRC校验
            //ushort crc = CRC(buffer, 7);

            //buffer[7] = (byte)((crc & 0xFF00) >> 8);
            //buffer[8] = (byte)((crc & 0x00FF));

            byte[] buffer = Encoding.UTF8.GetBytes(strReadWriteInfo);
            bleCore.Write(buffer);
        }

        /// <summary>
        /// 写入
        /// </summary>
        /// <param name="ReadWriteInfo">通过蓝牙要发送的信息</param>
        public void Write(byte[] ReadWriteInfo)
        {
            //////CRC校验
            //ushort crc = CRC(ReadWriteInfo, ReadWriteInfo.Length);
            //byte[] CRCarr = new byte[2];
            //CRCarr[0] = (byte)((crc & 0xFF00) >> 8);
            //CRCarr[1] = (byte)((crc & 0x00FF));
            //byte[] buff = Combine(ReadWriteInfo, CRCarr);

            bleCore.WriteName(ReadWriteInfo);
        }

        /// <summary>
        /// 写入
        /// </summary>
        /// <param name="ReadWriteInfo">通过蓝牙发送下位机跳转到Ymoden的信息</param>
        public void WriteKey(byte[] ReadWriteInfo)
        {
           bleCore.WriteName(ReadWriteInfo);
        }

        /// <summary>
        /// 写入1个byte
        /// </summary>
        /// <param name="b"></param>
        public void WriteByte(byte b)
        {
            byte[] sendBT = new byte[] { b };
            bleCore.WriteName(sendBT);
        }

        /// <summary>
        /// CRC校验
        /// </summary>
        /// <param name="data"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public static ushort CRC(byte[] data, int length)
        {
            ushort tempCrcResult = 0xffff;
            for (int i = 0; i < length; i++)
            {
                tempCrcResult = (ushort)(tempCrcResult ^ data[i]);
                for (int j = 0; j < 8; j++)
                {
                    if ((tempCrcResult & 0x0001) == 1)
                        tempCrcResult = (ushort)((tempCrcResult >> 1) ^ 0xa001);
                    else tempCrcResult = (ushort)(tempCrcResult >> 1);
                }
            }
            return (tempCrcResult = (ushort)(((tempCrcResult & 0xff) << 8) | (tempCrcResult >> 8)));
        }


        ///<summary>
        ///combine 2 byte array
        /// </summary>
        public byte[] Combine(byte[] first, byte[] second)
        {
            byte[] bytes = new byte[first.Length + second.Length];
            System.Buffer.BlockCopy(first, 0, bytes, 0, first.Length);
            System.Buffer.BlockCopy(second, 0, bytes, first.Length, second.Length);
            return bytes;
        }
    }

}
