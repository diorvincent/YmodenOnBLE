using System;
using Windows.Devices.Bluetooth;
using System.Threading;
using BLE_APPLY;

namespace YmodenOnBluetoolthLE
{
    class Program
    {
        static BLE_APPLY_Class bleApply;
        static int nDev = 0;
        static int nDevCount = 0;
        static string strDevName, strDevUUID, strOutputDevInfo;

        static void Main(string[] args)
        {
            try
            {
                bleApply = new BLE_APPLY_Class();
                Console.Write("搜索周围蓝牙设备，需要3s左右.");

                bleApply.Search_BLE_Dev(true);
                Thread.Sleep(3000);
                bleApply.Search_BLE_Dev(false);

                strDevName = string.Empty;
                strDevUUID = string.Empty;
                
                nDevCount = bleApply.DeviceList.Count;
                if(nDevCount==0)
                {
                    Console.Write("未发现蓝牙设备。");
                    return;
                }

                string[] DevNameLst = new string[nDevCount];
                string[] DevUUIDLst = new string[nDevCount];
                foreach (BluetoothLEDevice bleDev in bleApply.DeviceList)
                {
                    strOutputDevInfo = nDev.ToString() + ":" + bleDev.Name + "---" + bleDev.DeviceId;
                    Console.WriteLine(strOutputDevInfo);

                    DevNameLst[nDev] = bleDev.Name;
                    DevUUIDLst[nDev] = bleDev.DeviceId;
                    nDev++;
                    
                    //if (bleDev.Name.Contains("Minew"))
                    {
                        strDevUUID = bleDev.DeviceId;
                        strDevName = bleDev.Name;
                    }
                }
                if(DevNameLst.Length>0)
                    Console.Write("请选择需要连接的蓝牙设备：\t\n");
                else
                {
                    Console.Write("没有发现蓝牙设备，请检查设备连接是否正常。\t\n");
                    Console.Read();
                }

                int nSelectDev = Console.Read();
                char ch = Convert.ToChar(nSelectDev);
                int nSelectedDev = int.Parse(ch.ToString()) ;

                strDevName = DevNameLst[nSelectedDev];
                strDevUUID = DevUUIDLst[nSelectedDev];

                if (bleApply.Connect(strDevUUID))
                {
                    string strBinFile;
                    Ymodem ym = new Ymodem(bleApply);

                AG:
                    Console.Write("请选择发送文件选择模式：\t\n1:自动选择桌面名称为app.bin的文件；\t\n2:手动输入要发送bin文件的全路径；\t\n3:退出\t\n");
                    Console.Beep();
                ONCEMORE:
                    int nSendMode = Console.Read();
                 
                    if (nSendMode < 0x30)
                        goto ONCEMORE;

                    ch = Convert.ToChar(nSendMode);
                    nSelectedDev = int.Parse(ch.ToString());

                    if (nSelectedDev == 0x1)
                    {
                        //获取服务列表
                        bleApply.BtnServes();
                        Thread.Sleep(2000);
                        //获取特征值
                        bleApply.BtnFeatures();
                        Thread.Sleep(2000);
                        //获取操作
                        bleApply.BtnOpt(5);
                        Thread.Sleep(500);

                        strBinFile = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + @"\app.bin";
                        
                        ym.xymodem_send(strBinFile);
                    }
                    else if (nSelectedDev == 0x2)
                    {
                        //获取服务列表
                        bleApply.BtnServes();
                        Thread.Sleep(5000);
                        //获取特征值
                        bleApply.BtnFeatures();
                        Thread.Sleep(7000);
                        //获取操作
                        bleApply.BtnOpt(5);
                        Thread.Sleep(500);

                        Console.Write("请选择要发送给控制板的bin文件:\t\n");
                        Console.Read();
                        Console.Read();
                        strBinFile = Console.ReadLine();

                        ym.xymodem_send(strBinFile);
                    }
                    else if (nSendMode == 0x3)
                    {
                        bleApply.disCon_BLE_Dev(strDevName);
                        return;
                    }
                    else
                    {
                        Console.Write("输入错误，请重新输入！\t\n");
                        goto AG;
                    }
                }
                else
                {
                    Console.Write("控制板连接失败。\t\n");
                }
            }
            finally
            {
                bleApply.disCon_BLE_Dev(strDevName);
            }
        }
    }
}
