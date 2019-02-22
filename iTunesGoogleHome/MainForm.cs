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
using SamSeifert.Utilities.Extensions;
using Logger = SamSeifert.Utilities.Logger;

namespace iTunesGoogleHome
{
    public partial class MainForm : Form
    {
        DateTime lastChecked = DateTime.Now;
        private PushbulletClient Client;
        private WebSocket ws;

        private volatile bool _HesDeadJim = false;
        private bool hasHiddenOnStartup = false;

        private readonly PushHandler _RequestHandler;

        private static readonly String fileName = "pbkey.txt";

#if DEBUG
        bool _FakingRequests = false;
#endif

        public MainForm()
        {
            InitializeComponent();

            this._RequestHandler = new PushHandler(this.tbMatches);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            this.LoadFormState();

            Logger.AddTab();
            
            var tb = this.tbConsole;
            Logger.WriterSupportTabs = new Action<string, string>((String ln, String tab) =>
            {
                MainForm.ThreadSafeTextBoxWrite(tb, ln, tab);
            });

            Logger.WriteLine("Loading access token from: " + Path.Combine(TextSettings.Folder, fileName));
            this.tbPushBullet.Text = TextSettings.Read(fileName) ?? "";
            this.ActiveControl = this.label1; // Prevents textbox text from starting highlighted

            this.bStartPushbullet_Click(sender, e);

#if !DEBUG
            this.bTest.RemoveFromParent();
            this.bTest = null;
#endif
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
#if DEBUG
            {
#else
            if ((!_HesDeadJim) && (this.Visible))
            {
                this.Hide();
                e.Cancel = true;
            }
            else
            {
#endif
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

        private void CloseConnections()
        {
            if (this.ws != null)
            {
                (this.ws as IDisposable).Dispose();
                this.ws = null;
            }
        }

        private void CeaseAndDesist()
        {
            if (!this._HesDeadJim)
            {
                this._HesDeadJim = true;

                this.SaveFormState();

                this.CloseConnections();            
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
                this.CloseConnections();
                this.Client = new PushbulletClient(key, TimeZoneInfo.Local);
                ws = new WebSocket(string.Concat("wss://stream.pushbullet.com/websocket/", key));
                ws.OnMessage += Ws_OnMessage;
                ws.Connect();
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
                    if (!this.hasHiddenOnStartup)
                    {
                        this.hasHiddenOnStartup = true;
                        this.Hide();
                        Logger.WriteLine("Saving access token to: " + Path.Combine(TextSettings.Folder, fileName));
                        TextSettings.Save(fileName, this.Client.AccessToken);
                    }
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
                                            this._RequestHandler.NewNotification(push.Title, push.Body);
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

        private void bTest_Click(object sender, EventArgs e)
        {
#if DEBUG
            this._FakingRequests = true;
            this.label1.Text = "Fake Query:";

            var text = this.tbPushBullet.Text.Trim();

            if (text.Length == 0)
                this.tbPushBullet.Text = "play, artist adele";

            var str_split = this.tbPushBullet.Text.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (str_split.Length == 0)
                return;

            String title = str_split[0];
            String body = null;
            if (str_split.Length > 1)
                body = str_split[1];

            using (Logger.Time("Processing Request"))
            {
                for (int i = 0; i < 3; i++) // 3 attempts.
                {
                    using (Logger.Time("Attempt " + i))
                    {
                        //try
                        {
                            this._RequestHandler.NewNotification(title, body);
                            break;
                        }
                        /*
                        catch (Exception exc)
                        {
                            Logger.WriteException(this, "FindBestMatchAndPlay", exc);
                            System.Threading.Thread.Sleep(500); // Give itunes a breath.
                        }
                        //*/
                    }
                }
            }
#endif
        }
    }
}
