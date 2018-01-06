using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

using PushbulletSharp;
using PushbulletSharp.Filters;
using PushbulletSharp.Models.Responses.WebSocket;

using WebSocketSharp;

using SamSeifert.Utilities;
using Logger = SamSeifert.Utilities.Logger;

namespace iTunesGoogleHome
{
    public partial class MainForm : Form
    {
        DateTime lastChecked = DateTime.Now;
        private PushbulletClient Client;
        private WebSocket ws;

        private volatile bool _HesDeadJim = false;

        private readonly PushHandler _RequestHandler;

        public MainForm()
        {
            InitializeComponent();

            this._RequestHandler = new PushHandler(this.tbMatches);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            Logger.AddTab();
            
            var tb = this.tbConsole;
            Logger.WriterSupportTabs = new Action<string, string>((String ln, String tab) =>
            {
                MainForm.ThreadSafeTextBoxWrite(tb, ln, tab);
            });

            this.LoadFormState();

            this.tbPushBullet.Text = TextSettings.Read("pbkey.txt") ?? "";
            this.ActiveControl = this.label1; // Prevents textbox text from starting highlighted

            this.bStartPushbullet_Click(sender, e);
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if ((!_HesDeadJim) && (this.Visible))
            {
                this.Hide();
                e.Cancel = true;
            }
            else
            {
                this.CeaseAndDesist();
            }
        }

        private void _NotifyIcon_MouseDown(object sender, MouseEventArgs e)
        {
            if (MouseButtons.None != (e.Button & MouseButtons.Left))
                this.Visible = !this.Visible;
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Exiting will prevent Google Home from controlling iTunes", "Are you sure?", MessageBoxButtons.OKCancel)
                == DialogResult.OK)
            {
                this.CeaseAndDesist();
                this.Close();
            }
        }

        private void CeaseAndDesist()
        {
            if (!this._HesDeadJim)
            {
                this._HesDeadJim = true;

                this.SaveFormState();

                if (this.ws != null)
                {
                    (this.ws as IDisposable).Dispose();
                    this.ws = null;
                }

                if (!this.tbPushBullet.IsDisposed)
                {
                    TextSettings.Save("pbkey.txt", this.tbPushBullet.Text);
                }
            }
        }

        public static void ThreadSafeTextBoxWrite(TextBox tb, String ln, String tab = "")
        {
            if (ln.Length != 0)
                ln = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + tab + ln;
 
            Console.WriteLine(ln);

            if (tb == null) return;
            if (tb.IsDisposed) return;

            var act = new Action(() => {
                tb.AppendText(ln + Environment.NewLine);
            });

            if (tb.InvokeRequired) tb.BeginInvoke(act); // Don't Wait
            else act();
        }

        private void bStartPushbullet_Click(object sender, EventArgs e)
        {
            string key = this.tbPushBullet.Text.Trim();
            if (key.Length > 0)
            {
                this.Client = new PushbulletClient(key, TimeZoneInfo.Local);
                ws = new WebSocket(string.Concat("wss://stream.pushbullet.com/websocket/", key));
                ws.OnMessage += Ws_OnMessage;
                ws.Connect();
                this.bStartPushbullet.Enabled = false;

                // When We Connect, Hide (helpful on startup)
                Action act = () => { this.Hide(); };
                System.Threading.Timer timer = null;
                timer = new System.Threading.Timer((obj) =>
                {
                    this.BeginInvoke(act);
                    timer.Dispose();
                }, null, 250, System.Threading.Timeout.Infinite);
            }
        }

        private void Ws_OnMessage(object sender, MessageEventArgs e)
        {
            if (this._HesDeadJim) return;
            JavaScriptSerializer js = new JavaScriptSerializer();
            WebSocketResponse response = js.Deserialize<WebSocketResponse>(e.Data);
            this.BeginInvoke(new Action(() => {
                this.HandleResponseMainThread(response);
            }));
        }

        public void HandleResponseMainThread(WebSocketResponse response)
        {
            switch (response.Type)
            {
                // Heartbeat
                case "nop":
                    Logger.WriteLine("PushBullet Heartbeat");
                    break;
                case "tickle":
                    PushResponseFilter filter = new PushResponseFilter()
                    {
                        Active = true,
                        ModifiedDate = lastChecked
                    };

                    var pushes = Client.GetPushes(filter);
                    foreach (var push in pushes.Pushes)
                    {
                        if ((push.Created - lastChecked).TotalDays > 0)
                        {
                            lastChecked = push.Created;

                            using (Logger.Time("Processing Request"))
                            {
                                for (int i = 0; i < 3; i++) // 3 attempts.
                                {
                                    using (Logger.Time("Attempt " + i))
                                    {
                                        try
                                        {
                                            this._RequestHandler.NewNotification(push);
                                            break;
                                        }
                                        catch (Exception exc)
                                        {
                                            Logger.WriteException(this, "FindBestMatchAndPlay", exc);
                                            System.Threading.Thread.Sleep(500); // Give itunes a breath.
                                        }
                                    }
                                }
                            }
                        }
                        else Logger.WriteLine("Ignoring Old PushBullet: " + push.Title);
                    }
                    break;
                case "push":
                    Logger.WriteLine(string.Format("New push recieved on {0}.", DateTime.Now));
                    Logger.WriteLine("Push Type: " + response.Push.Type);
                    Logger.WriteLine("Response SubType: " + response.Subtype);
                    break;
                default:
                    Logger.WriteLine("PushBullet type not supported!");
                    break;
            }
        }
    }
}
