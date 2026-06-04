using Microsoft.VisualBasic.FileIO; // 清空回收站
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace GarbageCleaner
{
    public partial class Form1 : Form
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, int dwFlags);

        private const int SHERB_NOCONFIRMATION = 0x00000001;
        private const int SHERB_NOPROGRESSUI = 0x00000002;
        private const int SHERB_NOSOUND = 0x00000004;
        private const int V = 100;

        public Form1()
        {
            InitializeComponent();
            // 加载保存的用户设置（清理选项的勾选状态）
            LoadSettings();
            // 绑定验证事件
            foreach (CheckBox chk in new[] { chkTempFiles, chkRecycleBin, chkPrefetch, chkRecentDocs, chkEventLogs, chkEdgeCache, chkWindowsUpdate })
            {
                chk.CheckedChanged += (s, e) => UpdateCleanButtonState();
            }
            UpdateCleanButtonState();
            // 加载信息页内容
            LoadAboutInfo();
            this.Load += Form1_Load;
        }
        private CancellationTokenSource _cts;
        private List<string> _garbageFiles = new List<string>();

        private async void Form1_Load(object sender, EventArgs e)
        {
            // 获取当前 Windows 用户名
            string userName = Environment.UserName;
            lblGreeting.Text = $"Hi, {userName}!";

            await Task.Run(async () =>
            {
                bool updateAvailable = await UpdateChecker.IsNewVersionAvailable("CLoneLING", "WindowsGarbageCleaner");
                if (updateAvailable)
                {
                    this.Invoke(new Action(() =>
                    {
                        DialogResult result = MessageBox.Show("检测到新版本！是否前往 GitHub 下载？", "软件更新",
                                                              MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                        if (result == DialogResult.Yes)
                        {
                            System.Diagnostics.Process.Start("https://github.com/CLoneLING/WindowsGarbageCleaner/releases/latest");
                        }
                    }));
                }
            });
        }

        // ==================== 设置保存与加载 ====================
        private void LoadSettings()
        {
            chkTempFiles.Checked = Properties.Settings.Default.CleanTemp;
            chkRecycleBin.Checked = Properties.Settings.Default.CleanRecycle;
            chkPrefetch.Checked = Properties.Settings.Default.CleanPrefetch;
            chkRecentDocs.Checked = Properties.Settings.Default.CleanRecent;
            chkEventLogs.Checked = Properties.Settings.Default.CleanLogs;
            chkEdgeCache.Checked = Properties.Settings.Default.CleanEdge;
            chkWindowsUpdate.Checked = Properties.Settings.Default.CleanWU;
        }

        private void SaveSettings()
        {
            Properties.Settings.Default.CleanTemp = chkTempFiles.Checked;
            Properties.Settings.Default.CleanRecycle = chkRecycleBin.Checked;
            Properties.Settings.Default.CleanPrefetch = chkPrefetch.Checked;
            Properties.Settings.Default.CleanRecent = chkRecentDocs.Checked;
            Properties.Settings.Default.CleanLogs = chkEventLogs.Checked;
            Properties.Settings.Default.CleanEdge = chkEdgeCache.Checked;
            Properties.Settings.Default.CleanWU = chkWindowsUpdate.Checked;
            Properties.Settings.Default.Save();
        }

        // ==================== 至少勾选一项验证 ====================
        private bool IsAtLeastOneCategorySelected()
        {
            return chkTempFiles.Checked || chkRecycleBin.Checked || chkPrefetch.Checked ||
                   chkRecentDocs.Checked || chkEventLogs.Checked || chkEdgeCache.Checked ||
                   chkWindowsUpdate.Checked;
        }

        private void UpdateCleanButtonState()
        {
            btnClean.Enabled = IsAtLeastOneCategorySelected();
        }

        // ==================== 扫描垃圾 ====================
        private async void btnScan_Click_1(object sender, EventArgs e)
        {
            if (!IsAtLeastOneCategorySelected())
            {
                MessageBox.Show("请在“设置”页至少选择一个要清理的类别.", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tabControl1.SelectedTab = tabPageSettings;
                return;
            }

            // 保存用户勾选状态
            SaveSettings();

            // 重置界面
            listBoxResult.Items.Clear();
            _garbageFiles.Clear();
            btnScan.Enabled = false;
            btnClean.Enabled = false;
            btnCancel.Enabled = true;
            progressBar1.Value = 0;
            lblStatus.Text = "正在扫描...";

            _cts = new CancellationTokenSource();
            try
            {
                await Task.Run(() => ScanGarbage(_cts.Token), _cts.Token);
                lblStatus.Text = "扫描完成.";
                btnClean.Enabled = (_garbageFiles.Count > 0);
                if (_garbageFiles.Count == 0)
                    listBoxResult.Items.Add("未发现任何垃圾文件.");
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "扫描已取消.";
                listBoxResult.Items.Add("扫描被用户取消.");
            }
            finally
            {
                progressBar1.Value = V;
                btnScan.Enabled = true;
                btnCancel.Enabled = false;
            }
        }

        private void ScanGarbage(CancellationToken ct)
        {
            List<string> pathsToScan = new List<string>();

            // 根据勾选添加路径
            if (chkTempFiles.Checked)
            {
                pathsToScan.Add(Path.GetTempPath());
                pathsToScan.Add(Environment.GetFolderPath(Environment.SpecialFolder.InternetCache));
            }
            if (chkPrefetch.Checked)
                pathsToScan.Add(@"C:\Windows\Prefetch");
            if (chkRecentDocs.Checked)
                pathsToScan.Add(Environment.GetFolderPath(Environment.SpecialFolder.Recent));
            if (chkEventLogs.Checked)
                pathsToScan.Add(@"C:\Windows\Logs");
            if (chkEdgeCache.Checked)
            {
                string edgeCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                                @"Microsoft\Edge\User Data\Default\Cache");
                string edgeCodeCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                                    @"Microsoft\Edge\User Data\Default\Code Cache");
                if (Directory.Exists(edgeCache)) pathsToScan.Add(edgeCache);
                if (Directory.Exists(edgeCodeCache)) pathsToScan.Add(edgeCodeCache);
            }
            if (chkWindowsUpdate.Checked)
            {
                string wuDir = @"C:\Windows\SoftwareDistribution\Download";
                if (Directory.Exists(wuDir)) pathsToScan.Add(wuDir);
            }

            // 第一遍：计算总文件数（用于进度条）
            int totalFiles = 0;
            foreach (string root in pathsToScan)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    totalFiles += Directory.GetFiles(root, "*.*", System.IO.SearchOption.AllDirectories).Length;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
            }

            int processed = 0;
            // 第二遍：实际扫描添加文件
            foreach (string root in pathsToScan)
            {
                if (!Directory.Exists(root)) continue;
                string[] files = null;
                try { files = Directory.GetFiles(root, "*.*", System.IO.SearchOption.AllDirectories); } catch (UnauthorizedAccessException) { continue; }
                foreach (string file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        _garbageFiles.Add(file);
                        this.Invoke((MethodInvoker)delegate
                        {
                            listBoxResult.Items.Add(file);
                            if (totalFiles > 0)
                                progressBar1.Value = (int)((double)processed / totalFiles * 100);
                        });
                        processed++;
                    }
                    catch { /* 忽略无法访问的文件 */ }
                }
            }

            // 回收站特殊标记
            if (chkRecycleBin.Checked)
            {
                _garbageFiles.Add("RECYCLE_BIN");
                this.Invoke((MethodInvoker)delegate
                {
                    listBoxResult.Items.Add("回收站 - 待清空");
                });
            }
        }

        // ==================== 清理垃圾 ====================
        private async void btnClean_Click_1(object sender, EventArgs e)
        {
            if (_garbageFiles.Count == 0)
            {
                MessageBox.Show("没有发现可清理的垃圾.", "提示");
                return;
            }

            DialogResult dr = MessageBox.Show($"确定要清理 {_garbageFiles.Count} 个可清理项吗？\n部分文件可能无法删除.",
                                              "确认清理", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (dr != DialogResult.Yes) return;

            btnScan.Enabled = false;
            btnClean.Enabled = false;
            btnCancel.Enabled = true;
            progressBar1.Value = 0;
            lblStatus.Text = "正在清理...";

            _cts = new CancellationTokenSource();
            try
            {
                await Task.Run(() => CleanGarbage(_cts.Token), _cts.Token);
                lblStatus.Text = "清理完成.";
                progressBar1.Value = V;
                MessageBox.Show("垃圾清理完成！", "完成");
                // 重新扫描
                btnScan.PerformClick();
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "清理已取消.";
            }
            finally
            {
                btnScan.Enabled = true;
                btnCancel.Enabled = false;
            }
        }

        private void btnSelectAll_Click(object sender, EventArgs e)
        {
            chkTempFiles.Checked = true;
            chkRecycleBin.Checked = true;
            chkPrefetch.Checked = true;
            chkRecentDocs.Checked = true;
            chkEventLogs.Checked = true;
            chkEdgeCache.Checked = true;
            chkWindowsUpdate.Checked = true;
        }

        private void btnDeselectAll_Click(object sender, EventArgs e)
        {
            chkTempFiles.Checked = false;
            chkRecycleBin.Checked = false;
            chkPrefetch.Checked = false;
            chkRecentDocs.Checked = false;
            chkEventLogs.Checked = false;
            chkEdgeCache.Checked = false;
            chkWindowsUpdate.Checked = false;
        }

        private void CleanGarbage(CancellationToken ct)
        {
            // 处理 Edge 缓存：尝试结束 Edge 进程
            if (chkEdgeCache.Checked)
                KillProcess("msedge");

            // 处理 Windows Update 缓存：停止服务
            bool wuServiceStopped = false;
            if (chkWindowsUpdate.Checked)
            {
                StopService("wuauserv");
                wuServiceStopped = true;
            }

            try
            {
                int total = _garbageFiles.Count;
                int cleaned = 0;
                foreach (string item in _garbageFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    bool success = false;
                    try
                    {
                        if (item == "RECYCLE_BIN")
                        {
                            SHEmptyRecycleBin(this.Handle, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI);
                            success = true;
                        }
                        else if (File.Exists(item))
                        {
                            File.SetAttributes(item, FileAttributes.Normal);
                            File.Delete(item);
                            success = true;
                        }
                        else if (Directory.Exists(item))
                        {
                            // 对于 Windows Update 缓存目录，只清空内容，不删除目录本身
                            if (item == @"C:\Windows\SoftwareDistribution\Download")
                                CleanDirectoryContent(item);
                            else
                                Directory.Delete(item, true);
                            success = true;
                        }
                    }
                    catch { /* 忽略删除失败 */ }

                    if (success) cleaned++;
                    this.Invoke((MethodInvoker)delegate
                    {
                        progressBar1.Value = (int)((double)cleaned / total * 100);
                    });
                }
                this.Invoke((MethodInvoker)delegate
                {
                    listBoxResult.Items.Add($"成功清理 {cleaned} / {total} 项.");
                });
            }
            finally
            {
                if (wuServiceStopped)
                    StartService("wuauserv");
            }
        }

        // 辅助：清空目录内容但不删除目录
        private void CleanDirectoryContent(string dirPath)
        {
            if (!Directory.Exists(dirPath)) return;
            foreach (string file in Directory.GetFiles(dirPath))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); } catch { }
            }
            foreach (string subDir in Directory.GetDirectories(dirPath))
            {
                try { Directory.Delete(subDir, true); } catch { }
            }
        }

        // 辅助：结束进程
        private void KillProcess(string name)
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(name);
            foreach (var p in processes)
            {
                try { p.Kill(); p.WaitForExit(1000); } catch { }
            }
        }

        // 辅助：停止服务
        private void StopService(string serviceName)
        {
            try
            {
                var sc = new System.ServiceProcess.ServiceController(serviceName);
                if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                }
            }
            catch { }
        }

        private void StartService(string serviceName)
        {
            try
            {
                var sc = new System.ServiceProcess.ServiceController(serviceName);
                if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Stopped)
                {
                    sc.Start();
                    sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                }
            }
            catch { }
        }

        // ==================== 取消操作 ====================
        private void btnCancel_Click_1(object sender, EventArgs e)
        {
            _cts?.Cancel();
            btnCancel.Enabled = false;
            progressBar1.Value = V;
            lblStatus.Text = "正在取消...";
        }

        // ==================== 信息页内容加载 ====================
        private void LoadAboutInfo()
        {
            // Logo
            try { pbLogo.Image = Properties.Resources.StudioLogo; } catch { }

            // 版本号
            //Version ver = Assembly.GetExecutingAssembly().GetName().Version;
            //lblVersion.Text = $"版本：{ver.Major}.{ver.Minor}.{ver.Build}";

            // 更新日期
            string filePath = Assembly.GetExecutingAssembly().Location;
            DateTime lastUpdate = File.GetLastWriteTime(filePath);
            lblUpdateDate.Text = $"更新日期：{lastUpdate:yyyy年MM月dd日}";

            // 著作权（从 AssemblyInfo 读取）
            //var copyrightAttr = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>();
            //lblCopyright.Text = copyrightAttr?.Copyright ?? "Copyright © 2026 YuMoo_.  All rights reserved.";

            // 作者
            lblAuthor.Text = "作者：YuMoo_";
        }

        private void linkEmail_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"mailto:clonel@163.com",
                UseShellExecute = true
            });
        }


        private void PictureBoxDonate_Click(object sender, EventArgs e)
        {
            // 爱发电主页
            System.Diagnostics.Process.Start("https://ifdian.net/a/yumoo");
        }

        private void lblDonateLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            // 爱发电主页
            System.Diagnostics.Process.Start("https://ifdian.net/a/yumoo");
        }
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPageMain = new System.Windows.Forms.TabPage();
            this.listBoxResult = new System.Windows.Forms.ListBox();
            this.lblStatus = new System.Windows.Forms.Label();
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnClean = new System.Windows.Forms.Button();
            this.btnScan = new System.Windows.Forms.Button();
            this.lblGreeting = new System.Windows.Forms.Label();
            this.tabPageSettings = new System.Windows.Forms.TabPage();
            this.btnDeselectAll = new System.Windows.Forms.Button();
            this.btnSelectAll = new System.Windows.Forms.Button();
            this.chkWindowsUpdate = new System.Windows.Forms.CheckBox();
            this.chkEdgeCache = new System.Windows.Forms.CheckBox();
            this.chkEventLogs = new System.Windows.Forms.CheckBox();
            this.chkRecentDocs = new System.Windows.Forms.CheckBox();
            this.chkPrefetch = new System.Windows.Forms.CheckBox();
            this.chkRecycleBin = new System.Windows.Forms.CheckBox();
            this.chkTempFiles = new System.Windows.Forms.CheckBox();
            this.tabPageAbout = new System.Windows.Forms.TabPage();
            this.lblFeedback = new System.Windows.Forms.Label();
            this.btnToFeedback = new System.Windows.Forms.Button();
            this.lblStatusUpdate = new System.Windows.Forms.Label();
            this.btnCheckUpdate = new System.Windows.Forms.Button();
            this.btnToGitHub = new System.Windows.Forms.Button();
            this.linkEmail = new System.Windows.Forms.LinkLabel();
            this.lblAuthor = new System.Windows.Forms.Label();
            this.lblCopyright = new System.Windows.Forms.Label();
            this.lblUpdateDate = new System.Windows.Forms.Label();
            this.lblVersion = new System.Windows.Forms.Label();
            this.lblAppName = new System.Windows.Forms.Label();
            this.pbLogo = new System.Windows.Forms.PictureBox();
            this.tabPageDonate = new System.Windows.Forms.TabPage();
            this.lblDonateLink = new System.Windows.Forms.LinkLabel();
            this.lblDonateDesc = new System.Windows.Forms.Label();
            this.PictureBoxDonate = new System.Windows.Forms.PictureBox();
            this.backgroundWorker1 = new System.ComponentModel.BackgroundWorker();
            this.tabControl1.SuspendLayout();
            this.tabPageMain.SuspendLayout();
            this.tabPageSettings.SuspendLayout();
            this.tabPageAbout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pbLogo)).BeginInit();
            this.tabPageDonate.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.PictureBoxDonate)).BeginInit();
            this.SuspendLayout();
            // 
            // tabControl1
            // 
            this.tabControl1.Controls.Add(this.tabPageMain);
            this.tabControl1.Controls.Add(this.tabPageSettings);
            this.tabControl1.Controls.Add(this.tabPageAbout);
            this.tabControl1.Controls.Add(this.tabPageDonate);
            this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl1.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.tabControl1.Location = new System.Drawing.Point(0, 0);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(678, 464);
            this.tabControl1.TabIndex = 0;
            // 
            // tabPageMain
            // 
            this.tabPageMain.BackColor = System.Drawing.SystemColors.Control;
            this.tabPageMain.Controls.Add(this.listBoxResult);
            this.tabPageMain.Controls.Add(this.lblStatus);
            this.tabPageMain.Controls.Add(this.progressBar1);
            this.tabPageMain.Controls.Add(this.btnCancel);
            this.tabPageMain.Controls.Add(this.btnClean);
            this.tabPageMain.Controls.Add(this.btnScan);
            this.tabPageMain.Controls.Add(this.lblGreeting);
            this.tabPageMain.Location = new System.Drawing.Point(4, 33);
            this.tabPageMain.Name = "tabPageMain";
            this.tabPageMain.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageMain.Size = new System.Drawing.Size(670, 427);
            this.tabPageMain.TabIndex = 0;
            this.tabPageMain.Text = "主页";
            // 
            // listBoxResult
            // 
            this.listBoxResult.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.listBoxResult.FormattingEnabled = true;
            this.listBoxResult.HorizontalScrollbar = true;
            this.listBoxResult.IntegralHeight = false;
            this.listBoxResult.ItemHeight = 22;
            this.listBoxResult.Location = new System.Drawing.Point(12, 155);
            this.listBoxResult.Name = "listBoxResult";
            this.listBoxResult.Size = new System.Drawing.Size(640, 265);
            this.listBoxResult.TabIndex = 6;
            // 
            // lblStatus
            // 
            this.lblStatus.AutoSize = true;
            this.lblStatus.ForeColor = System.Drawing.Color.Gray;
            this.lblStatus.Location = new System.Drawing.Point(12, 128);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(46, 24);
            this.lblStatus.TabIndex = 5;
            this.lblStatus.Text = "就绪";
            // 
            // progressBar1
            // 
            this.progressBar1.Location = new System.Drawing.Point(12, 95);
            this.progressBar1.Name = "progressBar1";
            this.progressBar1.Size = new System.Drawing.Size(640, 25);
            this.progressBar1.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
            this.progressBar1.TabIndex = 4;
            // 
            // btnCancel
            // 
            this.btnCancel.Enabled = false;
            this.btnCancel.Location = new System.Drawing.Point(224, 50);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(100, 30);
            this.btnCancel.TabIndex = 3;
            this.btnCancel.Text = "取消";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click_1);
            // 
            // btnClean
            // 
            this.btnClean.Enabled = false;
            this.btnClean.Location = new System.Drawing.Point(118, 50);
            this.btnClean.Name = "btnClean";
            this.btnClean.Size = new System.Drawing.Size(100, 30);
            this.btnClean.TabIndex = 2;
            this.btnClean.Text = "清理垃圾";
            this.btnClean.UseVisualStyleBackColor = true;
            this.btnClean.Click += new System.EventHandler(this.btnClean_Click_1);
            // 
            // btnScan
            // 
            this.btnScan.BackColor = System.Drawing.SystemColors.ControlLight;
            this.btnScan.Location = new System.Drawing.Point(12, 50);
            this.btnScan.Name = "btnScan";
            this.btnScan.Size = new System.Drawing.Size(100, 30);
            this.btnScan.TabIndex = 1;
            this.btnScan.Text = "扫描垃圾";
            this.btnScan.UseVisualStyleBackColor = true;
            this.btnScan.Click += new System.EventHandler(this.btnScan_Click_1);
            // 
            // lblGreeting
            // 
            this.lblGreeting.AutoSize = true;
            this.lblGreeting.Font = new System.Drawing.Font("微软雅黑", 10F, System.Drawing.FontStyle.Bold);
            this.lblGreeting.Location = new System.Drawing.Point(12, 12);
            this.lblGreeting.Name = "lblGreeting";
            this.lblGreeting.Size = new System.Drawing.Size(93, 27);
            this.lblGreeting.TabIndex = 0;
            this.lblGreeting.Text = "Hi,User!";
            // 
            // tabPageSettings
            // 
            this.tabPageSettings.BackColor = System.Drawing.SystemColors.Control;
            this.tabPageSettings.Controls.Add(this.btnDeselectAll);
            this.tabPageSettings.Controls.Add(this.btnSelectAll);
            this.tabPageSettings.Controls.Add(this.chkWindowsUpdate);
            this.tabPageSettings.Controls.Add(this.chkEdgeCache);
            this.tabPageSettings.Controls.Add(this.chkEventLogs);
            this.tabPageSettings.Controls.Add(this.chkRecentDocs);
            this.tabPageSettings.Controls.Add(this.chkPrefetch);
            this.tabPageSettings.Controls.Add(this.chkRecycleBin);
            this.tabPageSettings.Controls.Add(this.chkTempFiles);
            this.tabPageSettings.Location = new System.Drawing.Point(4, 33);
            this.tabPageSettings.Name = "tabPageSettings";
            this.tabPageSettings.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageSettings.Size = new System.Drawing.Size(670, 427);
            this.tabPageSettings.TabIndex = 1;
            this.tabPageSettings.Text = "设置";
            // 
            // btnDeselectAll
            // 
            this.btnDeselectAll.Location = new System.Drawing.Point(105, 300);
            this.btnDeselectAll.Name = "btnDeselectAll";
            this.btnDeselectAll.Size = new System.Drawing.Size(75, 30);
            this.btnDeselectAll.TabIndex = 8;
            this.btnDeselectAll.Text = "全不选";
            this.btnDeselectAll.UseVisualStyleBackColor = true;
            this.btnDeselectAll.Click += new System.EventHandler(this.btnDeselectAll_Click);
            // 
            // btnSelectAll
            // 
            this.btnSelectAll.Location = new System.Drawing.Point(20, 300);
            this.btnSelectAll.Name = "btnSelectAll";
            this.btnSelectAll.Size = new System.Drawing.Size(75, 30);
            this.btnSelectAll.TabIndex = 7;
            this.btnSelectAll.Text = "全选";
            this.btnSelectAll.UseVisualStyleBackColor = true;
            this.btnSelectAll.Click += new System.EventHandler(this.btnSelectAll_Click);
            // 
            // chkWindowsUpdate
            // 
            this.chkWindowsUpdate.AutoSize = true;
            this.chkWindowsUpdate.BackColor = System.Drawing.Color.Transparent;
            this.chkWindowsUpdate.Location = new System.Drawing.Point(200, 80);
            this.chkWindowsUpdate.Name = "chkWindowsUpdate";
            this.chkWindowsUpdate.Size = new System.Drawing.Size(225, 28);
            this.chkWindowsUpdate.TabIndex = 6;
            this.chkWindowsUpdate.Text = "Windows Update 缓存";
            this.chkWindowsUpdate.UseVisualStyleBackColor = false;
            // 
            // chkEdgeCache
            // 
            this.chkEdgeCache.AutoSize = true;
            this.chkEdgeCache.BackColor = System.Drawing.Color.Transparent;
            this.chkEdgeCache.Location = new System.Drawing.Point(200, 30);
            this.chkEdgeCache.Name = "chkEdgeCache";
            this.chkEdgeCache.Size = new System.Drawing.Size(208, 28);
            this.chkEdgeCache.TabIndex = 5;
            this.chkEdgeCache.Text = "Microsoft Edge 缓存";
            this.chkEdgeCache.UseVisualStyleBackColor = false;
            // 
            // chkEventLogs
            // 
            this.chkEventLogs.AutoSize = true;
            this.chkEventLogs.BackColor = System.Drawing.Color.Transparent;
            this.chkEventLogs.Location = new System.Drawing.Point(20, 230);
            this.chkEventLogs.Name = "chkEventLogs";
            this.chkEventLogs.Size = new System.Drawing.Size(108, 28);
            this.chkEventLogs.TabIndex = 4;
            this.chkEventLogs.Text = "系统日志";
            this.chkEventLogs.UseVisualStyleBackColor = false;
            // 
            // chkRecentDocs
            // 
            this.chkRecentDocs.AutoSize = true;
            this.chkRecentDocs.BackColor = System.Drawing.Color.Transparent;
            this.chkRecentDocs.Location = new System.Drawing.Point(20, 180);
            this.chkRecentDocs.Name = "chkRecentDocs";
            this.chkRecentDocs.Size = new System.Drawing.Size(162, 28);
            this.chkRecentDocs.TabIndex = 3;
            this.chkRecentDocs.Text = "最近使用的文档";
            this.chkRecentDocs.UseVisualStyleBackColor = false;
            // 
            // chkPrefetch
            // 
            this.chkPrefetch.AutoSize = true;
            this.chkPrefetch.BackColor = System.Drawing.Color.Transparent;
            this.chkPrefetch.Location = new System.Drawing.Point(20, 130);
            this.chkPrefetch.Name = "chkPrefetch";
            this.chkPrefetch.Size = new System.Drawing.Size(148, 28);
            this.chkPrefetch.TabIndex = 2;
            this.chkPrefetch.Text = "Prefetch 缓存";
            this.chkPrefetch.UseVisualStyleBackColor = false;
            // 
            // chkRecycleBin
            // 
            this.chkRecycleBin.AutoSize = true;
            this.chkRecycleBin.BackColor = System.Drawing.Color.Transparent;
            this.chkRecycleBin.Location = new System.Drawing.Point(20, 80);
            this.chkRecycleBin.Name = "chkRecycleBin";
            this.chkRecycleBin.Size = new System.Drawing.Size(90, 28);
            this.chkRecycleBin.TabIndex = 1;
            this.chkRecycleBin.Text = "回收站";
            this.chkRecycleBin.UseVisualStyleBackColor = false;
            // 
            // chkTempFiles
            // 
            this.chkTempFiles.AutoSize = true;
            this.chkTempFiles.BackColor = System.Drawing.Color.Transparent;
            this.chkTempFiles.Location = new System.Drawing.Point(20, 30);
            this.chkTempFiles.Name = "chkTempFiles";
            this.chkTempFiles.Size = new System.Drawing.Size(162, 28);
            this.chkTempFiles.TabIndex = 0;
            this.chkTempFiles.Text = "Temp 临时文件";
            this.chkTempFiles.UseVisualStyleBackColor = false;
            // 
            // tabPageAbout
            // 
            this.tabPageAbout.BackColor = System.Drawing.SystemColors.Control;
            this.tabPageAbout.Controls.Add(this.lblFeedback);
            this.tabPageAbout.Controls.Add(this.btnToFeedback);
            this.tabPageAbout.Controls.Add(this.lblStatusUpdate);
            this.tabPageAbout.Controls.Add(this.btnCheckUpdate);
            this.tabPageAbout.Controls.Add(this.btnToGitHub);
            this.tabPageAbout.Controls.Add(this.linkEmail);
            this.tabPageAbout.Controls.Add(this.lblAuthor);
            this.tabPageAbout.Controls.Add(this.lblCopyright);
            this.tabPageAbout.Controls.Add(this.lblUpdateDate);
            this.tabPageAbout.Controls.Add(this.lblVersion);
            this.tabPageAbout.Controls.Add(this.lblAppName);
            this.tabPageAbout.Controls.Add(this.pbLogo);
            this.tabPageAbout.Location = new System.Drawing.Point(4, 33);
            this.tabPageAbout.Name = "tabPageAbout";
            this.tabPageAbout.Size = new System.Drawing.Size(670, 427);
            this.tabPageAbout.TabIndex = 2;
            this.tabPageAbout.Text = "信息";
            // 
            // lblFeedback
            // 
            this.lblFeedback.AutoSize = true;
            this.lblFeedback.Location = new System.Drawing.Point(300, 303);
            this.lblFeedback.Name = "lblFeedback";
            this.lblFeedback.Size = new System.Drawing.Size(201, 48);
            this.lblFeedback.TabIndex = 11;
            this.lblFeedback.Text = "发现了新的bug？\r\n                  前往反馈-->";
            // 
            // btnToFeedback
            // 
            this.btnToFeedback.BackColor = System.Drawing.Color.Transparent;
            this.btnToFeedback.Location = new System.Drawing.Point(524, 319);
            this.btnToFeedback.Name = "btnToFeedback";
            this.btnToFeedback.Size = new System.Drawing.Size(100, 32);
            this.btnToFeedback.TabIndex = 10;
            this.btnToFeedback.Text = "问题反馈";
            this.btnToFeedback.UseVisualStyleBackColor = false;
            this.btnToFeedback.Click += new System.EventHandler(this.btnToFeedback_Click);
            // 
            // lblStatusUpdate
            // 
            this.lblStatusUpdate.AutoSize = true;
            this.lblStatusUpdate.ForeColor = System.Drawing.Color.Gray;
            this.lblStatusUpdate.Location = new System.Drawing.Point(524, 90);
            this.lblStatusUpdate.Name = "lblStatusUpdate";
            this.lblStatusUpdate.Size = new System.Drawing.Size(0, 24);
            this.lblStatusUpdate.TabIndex = 9;
            // 
            // btnCheckUpdate
            // 
            this.btnCheckUpdate.Location = new System.Drawing.Point(420, 85);
            this.btnCheckUpdate.Name = "btnCheckUpdate";
            this.btnCheckUpdate.Size = new System.Drawing.Size(98, 35);
            this.btnCheckUpdate.TabIndex = 8;
            this.btnCheckUpdate.Text = "检查更新";
            this.btnCheckUpdate.UseVisualStyleBackColor = true;
            this.btnCheckUpdate.Click += new System.EventHandler(this.btnCheckUpdate_Click);
            // 
            // btnToGitHub
            // 
            this.btnToGitHub.BackColor = System.Drawing.Color.Transparent;
            this.btnToGitHub.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnToGitHub.Location = new System.Drawing.Point(73, 220);
            this.btnToGitHub.Name = "btnToGitHub";
            this.btnToGitHub.Size = new System.Drawing.Size(155, 64);
            this.btnToGitHub.TabIndex = 7;
            this.btnToGitHub.Text = "在GitHub上\r\n查看项目主页";
            this.btnToGitHub.UseVisualStyleBackColor = false;
            this.btnToGitHub.Click += new System.EventHandler(this.btnToGitHub_Click);
            // 
            // linkEmail
            // 
            this.linkEmail.AutoSize = true;
            this.linkEmail.LinkBehavior = System.Windows.Forms.LinkBehavior.HoverUnderline;
            this.linkEmail.Location = new System.Drawing.Point(300, 260);
            this.linkEmail.Name = "linkEmail";
            this.linkEmail.Size = new System.Drawing.Size(208, 24);
            this.linkEmail.TabIndex = 6;
            this.linkEmail.TabStop = true;
            this.linkEmail.Text = "邮箱：clonel@163.com";
            // 
            // lblAuthor
            // 
            this.lblAuthor.AutoSize = true;
            this.lblAuthor.Location = new System.Drawing.Point(300, 220);
            this.lblAuthor.Name = "lblAuthor";
            this.lblAuthor.Size = new System.Drawing.Size(126, 24);
            this.lblAuthor.TabIndex = 5;
            this.lblAuthor.Text = "作者：YuMoo";
            // 
            // lblCopyright
            // 
            this.lblCopyright.AutoSize = true;
            this.lblCopyright.Location = new System.Drawing.Point(300, 160);
            this.lblCopyright.Name = "lblCopyright";
            this.lblCopyright.Size = new System.Drawing.Size(330, 24);
            this.lblCopyright.TabIndex = 4;
            this.lblCopyright.Text = "© 2026 YuMoo_.   All rights reserved.";
            // 
            // lblUpdateDate
            // 
            this.lblUpdateDate.AutoSize = true;
            this.lblUpdateDate.Location = new System.Drawing.Point(300, 120);
            this.lblUpdateDate.Name = "lblUpdateDate";
            this.lblUpdateDate.Size = new System.Drawing.Size(262, 24);
            this.lblUpdateDate.TabIndex = 3;
            this.lblUpdateDate.Text = "更新日期：YYYY年MM月DD日";
            // 
            // lblVersion
            // 
            this.lblVersion.AutoSize = true;
            this.lblVersion.Location = new System.Drawing.Point(300, 90);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Size = new System.Drawing.Size(114, 24);
            this.lblVersion.TabIndex = 2;
            this.lblVersion.Text = "版本：v1.1.2";
            // 
            // lblAppName
            // 
            this.lblAppName.AutoSize = true;
            this.lblAppName.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblAppName.Location = new System.Drawing.Point(290, 20);
            this.lblAppName.Name = "lblAppName";
            this.lblAppName.Size = new System.Drawing.Size(334, 31);
            this.lblAppName.TabIndex = 1;
            this.lblAppName.Text = "Windows Garbage Cleaner";
            // 
            // pbLogo
            // 
            this.pbLogo.BackColor = System.Drawing.Color.Transparent;
            this.pbLogo.Image = global::GarbageCleaner.Properties.Resources.StudioLogo;
            this.pbLogo.Location = new System.Drawing.Point(30, 30);
            this.pbLogo.Name = "pbLogo";
            this.pbLogo.Size = new System.Drawing.Size(240, 150);
            this.pbLogo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pbLogo.TabIndex = 0;
            this.pbLogo.TabStop = false;
            // 
            // tabPageDonate
            // 
            this.tabPageDonate.BackColor = System.Drawing.SystemColors.Control;
            this.tabPageDonate.Controls.Add(this.lblDonateLink);
            this.tabPageDonate.Controls.Add(this.lblDonateDesc);
            this.tabPageDonate.Controls.Add(this.PictureBoxDonate);
            this.tabPageDonate.Location = new System.Drawing.Point(4, 33);
            this.tabPageDonate.Name = "tabPageDonate";
            this.tabPageDonate.Size = new System.Drawing.Size(670, 427);
            this.tabPageDonate.TabIndex = 3;
            this.tabPageDonate.Text = "支持我们";
            // 
            // lblDonateLink
            // 
            this.lblDonateLink.AutoSize = true;
            this.lblDonateLink.Cursor = System.Windows.Forms.Cursors.Hand;
            this.lblDonateLink.LinkBehavior = System.Windows.Forms.LinkBehavior.HoverUnderline;
            this.lblDonateLink.Location = new System.Drawing.Point(262, 250);
            this.lblDonateLink.Name = "lblDonateLink";
            this.lblDonateLink.Size = new System.Drawing.Size(136, 24);
            this.lblDonateLink.TabIndex = 2;
            this.lblDonateLink.TabStop = true;
            this.lblDonateLink.Text = "访问爱发电主页";
            this.lblDonateLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.lblDonateLink_LinkClicked);
            // 
            // lblDonateDesc
            // 
            this.lblDonateDesc.AutoSize = true;
            this.lblDonateDesc.Font = new System.Drawing.Font("微软雅黑", 10F);
            this.lblDonateDesc.Location = new System.Drawing.Point(115, 210);
            this.lblDonateDesc.Name = "lblDonateDesc";
            this.lblDonateDesc.Size = new System.Drawing.Size(452, 27);
            this.lblDonateDesc.TabIndex = 1;
            this.lblDonateDesc.Text = "如果本工具对您有帮助，欢迎支持我们继续开发！";
            this.lblDonateDesc.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // PictureBoxDonate
            // 
            this.PictureBoxDonate.BackColor = System.Drawing.Color.Transparent;
            this.PictureBoxDonate.Cursor = System.Windows.Forms.Cursors.Hand;
            this.PictureBoxDonate.Image = global::GarbageCleaner.Properties.Resources.DonateButton;
            this.PictureBoxDonate.Location = new System.Drawing.Point(250, 100);
            this.PictureBoxDonate.Name = "PictureBoxDonate";
            this.PictureBoxDonate.Size = new System.Drawing.Size(160, 90);
            this.PictureBoxDonate.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.PictureBoxDonate.TabIndex = 0;
            this.PictureBoxDonate.TabStop = false;
            this.PictureBoxDonate.Click += new System.EventHandler(this.PictureBoxDonate_Click);
            // 
            // backgroundWorker1
            // 
            this.backgroundWorker1.WorkerReportsProgress = true;
            this.backgroundWorker1.WorkerSupportsCancellation = true;
            // 
            // Form1
            // 
            this.ClientSize = new System.Drawing.Size(678, 464);
            this.Controls.Add(this.tabControl1);
            this.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MinimumSize = new System.Drawing.Size(700, 520);
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = " Windows 系统垃圾清理工具";
            this.tabControl1.ResumeLayout(false);
            this.tabPageMain.ResumeLayout(false);
            this.tabPageMain.PerformLayout();
            this.tabPageSettings.ResumeLayout(false);
            this.tabPageSettings.PerformLayout();
            this.tabPageAbout.ResumeLayout(false);
            this.tabPageAbout.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pbLogo)).EndInit();
            this.tabPageDonate.ResumeLayout(false);
            this.tabPageDonate.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.PictureBoxDonate)).EndInit();
            this.ResumeLayout(false);

        }

        private void btnToGitHub_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/CLoneLING/WindowsGarbageCleaner");
        }

        private async void btnCheckUpdate_Click(object sender, EventArgs e)
        {
            btnCheckUpdate.Enabled = false;
            lblStatusUpdate.Text = "正在检查更新..."; 

            bool updateAvailable = await UpdateChecker.IsNewVersionAvailable("CLoneLING", "WindowsGarbageCleaner");
            if (updateAvailable)
            {
                DialogResult result = MessageBox.Show("检测到新版本！是否前往 GitHub 下载？", "软件更新",
                                                      MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (result == DialogResult.Yes)
                {
                    System.Diagnostics.Process.Start("https://github.com/CLoneLING/WindowsGarbageCleaner/releases/latest");
                }
            }
            else
            {
                MessageBox.Show("您使用的已经是最新版本.", "软件更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            btnCheckUpdate.Enabled = true;
            lblStatusUpdate.Text = "";
        }

        private void btnToFeedback_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/CLoneLING/WindowsGarbageCleaner/issues/new");
        }
    }
}