using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using MQTTnet;
//using MQTTnet.Client;
//using MQTTnet.Client.Options;
using MQTTnet.Core;
using MQTTnet.Core.Client;
using MQTTnet.Core.Packets;
using MQTTnet.Core.Protocol;

namespace barcodeReader
{
    public partial class Form1 : Form
    {
        private int trackState;
        private SerialPort comport;
        //console 參數
        private SerialPort My_SerialPort;
        private bool Console_receiving = false;
        private Thread t;
        //使用委派顯示 Console 畫面
        delegate void Display(string buffer);
        int end_alarm_time;//倒數
        DateTime dt1;

        private string username;
        private string station;
        private string[] MeterialName = new string[12];
        private int Track_index;

        HKWeb.NWSSoapClient navigo = null;
        private MqttClient mqttClient = null;
        private int mqttstate;//0->MQTT is OK else mqttstate=1

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            foreach (string m_Com in System.IO.Ports.SerialPort.GetPortNames())
            {
                cbComPort.Items.Add(m_Com);
            }
            //檢查WEBService
            navigo = new HKWeb.NWSSoapClient("NWSSoap");
            //連結MQTT
            Task.Run(async () => { await ConnectMqttServerAsync(); });

        }
        #region Auto TrackIN/OUT
        private string navigo_station(int fun,string MaterialName, string Account, string mpi_id)
        {
            string result = "";
            if (fun == 1)
            {
                result = navigo.MaterialAutoTrackIn(MaterialName, Account, mpi_id);
            }
            else if(fun == 2)
            {
                result = navigo.MaterialAutoTrackOut(MaterialName, Account);
            }

            Display d = new Display(SystemShow);
            this.Invoke(d, new Object[] { MaterialName + " Track In 結果：" + result + Environment.NewLine });
            //MQTT通知
            if (mqttstate == 0)
            {
                string topic = "NAVIGO";
                string mqttmessage = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + "  " + MaterialName + 
                    " Track In 結果：" + result;
                var appMsg = new MqttApplicationMessage(topic, Encoding.UTF8.GetBytes(mqttmessage), MqttQualityOfServiceLevel.AtMostOnce, false);
                mqttClient.PublishAsync(appMsg);
            }

            return result;
        }

        #endregion

        private void btn_openCom_Click(object sender, EventArgs e)
        {
            //setup timer
            //初始化 Timer interval 由 gw_sampling 設定---
            timer1.Interval = 1000;
            timer1.Tick += new EventHandler(timer1_Tick);
            dt1 = DateTime.Now;
            try
            {
                My_SerialPort = new SerialPort();

                if (My_SerialPort.IsOpen)
                {
                    My_SerialPort.Close();
                }

                //設定 Serial Port 參數
                My_SerialPort.PortName = cbComPort.Text;
                My_SerialPort.BaudRate = 9600;
                My_SerialPort.DataBits = 8;
                My_SerialPort.Parity = Parity.None;
                My_SerialPort.StopBits = StopBits.One;
                if (!My_SerialPort.IsOpen)
                {
                    //開啟 Serial Port
                    My_SerialPort.Open();

                    Console_receiving = true;
                    trackState = 0;//0程序未開始，1開始過站程序
                    timer1.Enabled = true;

                    //開啟執行續做接收動作
                    txt_log.AppendText(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + "  " + "OPEN " + cbComPort.Text + ":" +
                          "成功開始接收條碼訊息!" + Environment.NewLine);
                    t = new Thread(DoReceive);
                    t.IsBackground = true;
                    t.Start();
                }
            }
            catch (Exception ex)
            {
                txt_log.AppendText(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + "  " + "OPEN " + cbComPort.Text + ":" +
                      "失敗：" + ex.Message + Environment.NewLine);

            }
        }
        private void execute_Track(string meterialName)
        {
            string _message="";

            if (meterialName.ToUpper().Contains("MVS") || meterialName.ToUpper().Contains("MXS"))
            {//產品
                Track_index++;
                MeterialName[Track_index] = meterialName;
            }else if (meterialName.ToUpper().Contains(".") || meterialName.ToUpper().Contains("-"))
            {
                //名字
                username = meterialName;
            }
            else if (meterialName.ToUpper().Contains("TRACKIN") || meterialName.ToUpper().Contains("TRACKOUT"))
            {
                if (Track_index == -1)
                {
                    Display d = new Display(SystemShow);
                    this.Invoke(d, new Object[] { "沒有產品資訊" });
                    return;
                }                    
                else if (username.Equals(""))
                {
                    Display d = new Display(SystemShow);
                    this.Invoke(d, new Object[] { "沒有使用者"});

                    return;
                }
                else if (station.Equals(""))
                {
                    Display d = new Display(SystemShow);
                    this.Invoke(d, new Object[] { "沒有過帳站別" });
                    return;
                }
                else
                {

                    if (meterialName.ToUpper().Contains("TRACKIN"))
                    {
                        for (int i = 0; i <= Track_index; i++)
                        {
                            navigo_station(1, MeterialName[i], username, station);
                        }
                    }
                    else
                    {
                        for (int i = 0; i <= Track_index; i++)
                        {
                            navigo_station(2, MeterialName[i], username, station);
                        }
                    }
                    clearState(2);
                }
            }
            else
            {//站別
                station = meterialName;
            }
        }
        public void ConsoleShow(string buffer)
        {
            txt_syslog.AppendText(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + "  " + "message: " +
                buffer + Environment.NewLine);
            txt_syslog.Refresh();
        }
        public void LabelShow(string buffer)
        {
            lbl_timer.Text = buffer;
        }
        public void SystemShow(string buffer)
        {
            txt_log.AppendText(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + "  " + "message: " +
                buffer + Environment.NewLine);
            txt_log.Refresh();
        }
        private void clearState(int fun)
        {
            Track_index = -1;
            username = "";
            station = "";
            Array.Clear(MeterialName, 0, 12);

            switch (fun)
            {
                case 1://開始過站
                    end_alarm_time = 30;//timer
                    trackState = 1;

                    break;
                case 2://過完站
                    end_alarm_time = 30;//Track
                    trackState = 0;
                    
                    Display ll = new Display(LabelShow);
                    this.Invoke(ll, new Object[] { "目前沒有過站訊息" });
                    break;
            }
        }
        private void DoReceive()
        {
            Byte[] buffer = new Byte[1024];
            try
            {
                while (Console_receiving)
            {
                if (My_SerialPort.BytesToRead > 0)
                {
                    if (trackState == 0)
                    {
                        clearState(1);
                    }

                    Int32 length = My_SerialPort.Read(buffer, 0, buffer.Length);

                    string buf = Encoding.ASCII.GetString(buffer);
                    int w = buf.IndexOf('\r');
                    if (w > 0)
                    {
                        string new_buf = buf.Substring(0, w);

                        Array.Resize(ref buffer, length);
                        Display d = new Display(ConsoleShow);
                        this.Invoke(d, new Object[] { new_buf });

                        execute_Track(new_buf);
                    }
                    Array.Resize(ref buffer, 1024);
                    Array.Clear(buffer, 0, 1024);
                }
                Thread.Sleep(20);
            }
            }
            catch (Exception ex)
            {
                MessageBox.Show("訊息接收錯誤:"+ex.Message);
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (trackState == 1)
            {
                DateTime dt2 = DateTime.Now;
                TimeSpan ts = dt2 - dt1;
                if (ts.TotalSeconds >= 1)
                {
                    dt1 = DateTime.Now;
                    end_alarm_time = end_alarm_time - 1;
                    lbl_timer.Text = "結束倒數：" + end_alarm_time.ToString();
                    if (end_alarm_time == 0)
                    {
                        //送出時間到訊息
                        //trackState = 0;
                        //lbl_timer.Text = "目前沒有過站訊息";
                        clearState(2);
                    }
                }
            }
        }

        private async Task ConnectMqttServerAsync()
        {
            if (mqttClient == null)
            {
                mqttClient = new MqttClientFactory().CreateMqttClient() as MqttClient;               
                mqttClient.ApplicationMessageReceived += MqttClient_ApplicationMessageReceived;
                mqttClient.Connected += MqttClient_Connected;
                mqttClient.Disconnected += MqttClient_Disconnected;
            }

            try
            {
                var options = new MqttClientTcpOptions
                {
                    Server = txt_mqttip.Text,
                    ClientId = "HK",
                    CleanSession = true
                };

                await mqttClient.ConnectAsync(options);
                mqttstate = 0;
            }
            catch (Exception ex)
            {
                if (mqttstate == 0)                
                    mqttstate = 1;
                txt_log.AppendText(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + "  " + "連接到MQTT伺服器失敗！" + ex.Message + Environment.NewLine);
            }
        }
        private void MqttClient_Connected(object sender, EventArgs e)
        {
            Invoke((new Action(() =>
            {
                txt_log.AppendText(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + "  " + "已連接到MQTT伺服器" + Environment.NewLine);
                mqttstate = 0;                
                //訂閱自己CLIENTID
                string topic = "HK";
                mqttClient.SubscribeAsync(new List<TopicFilter> {
                    new TopicFilter(topic, MqttQualityOfServiceLevel.AtMostOnce)
                });
                //訂閱MPI廣播
                topic = "MPI";
                mqttClient.SubscribeAsync(new List<TopicFilter> {
                    new TopicFilter(topic, MqttQualityOfServiceLevel.AtMostOnce)
                });

            })));
        }
        private void MqttClient_ApplicationMessageReceived(object sender, MqttApplicationMessageReceivedEventArgs e)
        {
            //訊息接收
            Invoke((new Action(() =>
            {
                txt_log.AppendText(DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + "  " + ">> {Encoding.UTF8.GetString(e.ApplicationMessage.Payload)}{Environment.NewLine}");
            })));
        }

        private void MqttClient_Disconnected(object sender, EventArgs e)
        {
            if (chk_autoconnect.Checked)
            {
                Display d = new Display(SystemShow);
                this.Invoke(d, new Object[] { "已斷開MQTT連接" });
                Task.Run(async () => { await ConnectMqttServerAsync(); });
            }
            else
            {
                mqttstate = 1;
            }
        }
    }
}
