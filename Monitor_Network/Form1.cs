using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using System.Management;
using System.Runtime.InteropServices;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Diagnostics;
using System.Data;

namespace Monitor_Network
{
    public partial class Network : Form
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private NotifyIcon netAct;
        private Icon netTx, netRx, netTxRx, netIdle;
        private Thread netActMonWorker;
        private bool isMonitoring = true;
        private bool isExiting = false;


        // Для хранения данных истории и фильтрации
        private DataTable historyTable;
        private ComboBox filterComboBox;
        private TextBox filterTextBox;
        private Button applyFilterButton;
        private Button clearFilterButton;
        private Button blockIpButton;
        private Button unblockIpButton;

        // Invoke для получения расширенной таблицы TCP
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable,
            ref int pdwSize,
            bool bOrder,
            int ulAf,
            int TableClass,
            int Reserved);

        private const int TCP_TABLE_OWNER_PID_ALL = 5;
        private const int AF_INET = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            public uint localPort;
            public uint remoteAddr;
            public uint remotePort;
            public int owningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPTABLE_OWNER_PID
        {
            public uint dwNumEntries;
            public MIB_TCPROW_OWNER_PID table;
        }

        
        /// Конструктор формы: загрузка иконок, настройка таймера, NotifyIcon и контекстного меню, запуск мониторинга.
        
        public Network()
        {
            // Загрузка иконок для отображения сетевой активности
            netTx = new Icon("TX.ico");
            netRx = new Icon("RX.ico");
            netTxRx = new Icon("TXRX.ico");
            netIdle = new Icon("net.ico");

            InitializeComponent();
            this.FormClosing += Network_FormClosing;           

            // Таймер для периодической записи TCP-соединений
            var siteMonitorTimer = new System.Windows.Forms.Timer { Interval = 10000 };
            siteMonitorTimer.Tick += async (sender, e) => await Task.Run(LogTcpConnection);
            siteMonitorTimer.Start();

            // Настройка NotifyIcon и контекстного меню
            netAct = new NotifyIcon { Icon = netIdle, Visible = true, Text = "Монитор сети" };
            var contextMenu = new ContextMenuStrip();

            var openItem = new ToolStripMenuItem("Открыть");
            openItem.Click += (sender, e) => ShowMainForm();
            contextMenu.Items.Add(openItem);           

            netAct.ContextMenuStrip = contextMenu;

            // Запуск фонового потока мониторинга активности
            netActMonWorker = new Thread(monitor);
            netActMonWorker.Start();

            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
        }
        

        private void BlockSelectedIp()
        {
            if(dataGridViewHistory.SelectedRows.Count == 0)
            {
                MessageBox.Show("Выберите строку для блокировки IP.");
                return;
            }
            var cell = dataGridViewHistory.SelectedRows[0].Cells["IpAddress"];
            if(cell == null || cell.Value == null)
            {
                MessageBox.Show("IP-адрес не найден.");
                return;
            }

            string ip = cell.Value.ToString();
            try
            {
                RunNetshCommand($"advfirewall firewall add rule name=\"Block_{ip}_in\" dir=in action=block remoteip={ip}");
                RunNetshCommand($"advfirewall firewall add rule name=\"Block_{ip}_out\" dir=out action=block remoteip={ip}");

                MessageBox.Show($"IP {ip} успешно заблокирован.");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Ошибка блокировки IP {ip}");
                MessageBox.Show($"Ошибка блокировки IP {ip}: {ex.Message}");
            }
        }


        private void UnblockSelectedIp()
        {
            if (dataGridViewHistory.SelectedRows.Count == 0)
            {
                MessageBox.Show("Выберите строку с IP для разблокировки.");
                return;
            }

            string ip = dataGridViewHistory.SelectedRows[0].Cells["IpAddress"].Value.ToString();

            try
            {
                RunNetshCommand($"advfirewall firewall delete rule name=\"Block_{ip}_in\"");
                RunNetshCommand($"advfirewall firewall delete rule name=\"Block_{ip}_out\"");
                MessageBox.Show($"IP {ip} разблокирован.");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Ошибка при разблокировке IP");
                MessageBox.Show($"Ошибка: {ex.Message}");
            }
        }


        // Выполняет команду netsh с правами администратора.        
        private void RunNetshCommand(string arguments)
        {
            var psi = new ProcessStartInfo("netsh", arguments)
            {
                Verb = "runas",
                CreateNoWindow = true,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var proc = Process.Start(psi))
            {
                proc.WaitForExit();
                if (proc.ExitCode != 0)
                    throw new InvalidOperationException($"netsh завершился с кодом {proc.ExitCode}");
            }
        }


        // Обработчик закрытия формы: при сворачивании в трей отменяет закрытие программы.
        private void Network_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !isExiting)
            {
                e.Cancel = true;
                this.WindowState = FormWindowState.Minimized;
                this.ShowInTaskbar = false;
            }
        }
        

        // Показывает основную форму и обновляет историю посещений.       
        private async void ShowMainForm()
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.WindowState = FormWindowState.Normal;
                this.ShowInTaskbar = true;
                await LoadGridHistory();
            }
            this.Activate();
        }

       
        // Обработчик кнопки обновления: загружает историю записей.        
        private async void btnRefresh_Click(object sender, EventArgs e)
        {
            panel8.Visible = true;            
            await LoadGridHistory();
            panel4.Visible = false;
            panel6.Visible = false;
        }

        
        // Загружает историю сетевых посещений из базы и обновляет DataGridView.       
        private async Task LoadGridHistory()
        {
            try
            {
                var data = await Task.Run(() => Database.GetNetworkHistory());
                if (dataGridViewHistory.InvokeRequired)
                {
                    dataGridViewHistory.Invoke((MethodInvoker)(() =>
                    {
                        dataGridViewHistory.DataSource = data;
                        dataGridViewHistory.Refresh();
                    }));
                }
                else
                {
                    dataGridViewHistory.DataSource = data;
                    dataGridViewHistory.Refresh();
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Ошибка загрузки истории");
                MessageBox.Show($"Ошибка загрузки данных: {ex.Message}");
            }
        }
        

        // Собирает все активные TCP-соединения, фильтрует порты 80/443 и логирует посещения в базу.        
        private void LogTcpConnection()
        {
            IntPtr tcpTablePtr = IntPtr.Zero;
            int bufferSize = 0;
            var loggedDomains = new HashSet<string>();

            try
            {
                uint result = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
                if (result != 0 && result != 122) return;

                tcpTablePtr = Marshal.AllocHGlobal(bufferSize);
                result = GetExtendedTcpTable(tcpTablePtr, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
                if (result != 0) return;

                var tcpTable = (MIB_TCPTABLE_OWNER_PID)Marshal.PtrToStructure(tcpTablePtr, typeof(MIB_TCPTABLE_OWNER_PID));
                int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));
                IntPtr rowPtr = IntPtr.Add(tcpTablePtr, sizeof(uint));

                for (int i = 0; i < tcpTable.dwNumEntries; i++)
                {
                    var row = (MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(rowPtr, typeof(MIB_TCPROW_OWNER_PID));
                    int remotePort = IPAddress.NetworkToHostOrder((short)row.remotePort);
                    if (remotePort == 80 || remotePort == 443)
                    {
                        string remoteIp = new IPAddress(row.remoteAddr).ToString();
                        string domain = GetDomainFromIp(remoteIp);
                        string processName = GetProcessName(row.owningPid);

                        if (!loggedDomains.Contains(domain))
                        {
                            Database.LogVisit(domain, processName, remoteIp);
                            loggedDomains.Add(domain);
                        }
                    }
                    rowPtr = IntPtr.Add(rowPtr, rowSize);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Ошибка мониторинга TCP-соединений");
            }
            finally
            {
                if (tcpTablePtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(tcpTablePtr);
            }
        }

        
        // Преобразует IP-адрес в доменное имя или возвращает IP, если разрешение не удалось.        
        private string GetDomainFromIp(string ip)
        {
            try
            {
                var address = IPAddress.Parse(ip);
                if (IPAddress.IsLoopback(address) || IsPrivateIP(address))
                    return ip;

                return Dns.GetHostEntry(address).HostName;
            }
            catch
            {
                return ip;
            }
        }


        
        private void btnBlockIp_Click(object sender, EventArgs e)
        {
            panel4.Visible = true;
            BlockSelectedIp();
            panel6.Visible = false;
            panel8.Visible = false;
        }

        private void btnUnlockIp_Click(object sender, EventArgs e)
        {
            panel6.Visible = true;
            UnblockSelectedIp();
            panel4.Visible = false;
            panel8.Visible = false;
        }

        
        // Проверяет, является ли IP-адрес частным.        
        private bool IsPrivateIP(IPAddress ip)
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork)
                return false;

            byte[] bytes = ip.GetAddressBytes();
            return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) || (bytes[0] == 192 && bytes[1] == 168);
        }

        
        // Получает имя процесса по PID или возвращает "Частный".        
        private string GetProcessName(int pid)
        {
            try
            {
                return Process.GetProcessById(pid).ProcessName;
            }
            catch
            {
                return "Частный";
            }
        }

        
        // Фоновый цикл: обновляет иконку NotifyIcon в зависимости от сетевой активности.        
        private void monitor()
        {
            var networkClass = new ManagementClass("Win32_PerfFormattedData_Tcpip_NetworkInterface");
            try
            {
                while (isMonitoring)
                {
                    Thread.Sleep(1000);
                    var instances = networkClass.GetInstances();

                    foreach (ManagementObject obj in instances)
                    {
                        int rx = Convert.ToInt32(obj["PacketsReceivedPerSec"]);
                        int tx = Convert.ToInt32(obj["PacketsSentPerSec"]);

                        if (rx > 0 && tx > 0)
                            netAct.Icon = netTxRx;
                        else if (tx > 0)
                            netAct.Icon = netTx;
                        else if (rx > 0)
                        {
                            netAct.Icon = netRx;
                            Thread.Sleep(1500);
                        }
                        else
                            netAct.Icon = netIdle;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Ошибка в потоке мониторинга сети");
            }
            finally
            {
                logger.Info("Поток мониторинга сети остановлен.");
            }
        }
    }
}
