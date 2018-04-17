﻿using Rca.Hue2Json.Settings;
using Rca.Hue2Json.View;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static Rca.Hue2Json.Logger;

namespace Rca.Hue2Json
{
    public partial class MainView : Form
    {
        #region Member
        Controller m_Controller;
        #endregion Member

        #region Constructor
        public MainView(string[] args)
        {
            //Pfad zur Konfigurationsdatei ermitteln
            var configPath = Properties.Settings.Default.DefaultSettingsFileName;
            if (args.Any(x => x.ToLower().Contains(".config")))
            {
                configPath = args.First(x => x.Contains(".config"));
            }

            //Konfigurationsdatei laden
            ProgramSettings settings = null;
            if (File.Exists(configPath))
                settings = ProgramSettings.FromFile(configPath);
            else
                settings = ProgramSettings.CreateDefault();

            //Form initialisieren
            InitializeComponent();
            devToolStripMenuItem1.Visible = false;
            setupParameterSelection();
            setAllParameters(true);
            toolStripStatusLabel_Bridge.Text = "keine Bridge verbunden";
            this.Text = "Hue2Json - " + Application.ProductVersion;

            //Controller initialisieren
            m_Controller = new Controller(settings);


            //DevMode aktivieren
            if (args.Any(x => x.ToLower().Contains("devmode")))
            {
                m_Controller.DevMode = true;
                devToolStripMenuItem1.Visible = true;
                btn_ReadParameters.Enabled = true;
                btn_ShowParameters.Enabled = true;
            }

            //Bridge-Simulation aktivieren
            if (args.Any(x => x.ToLower().Contains("sim")))
            {
                m_Controller.SimMode = true;
            }
        }
        #endregion Constructor

        #region Benutzereingaben verarbeiten
        async void sucheBridgeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Cursor = Cursors.WaitCursor;

            var bridgeInfos = await m_Controller.ScanBridges();

            if (bridgeInfos.Length > 0)
            {
                foreach (var info in bridgeInfos)
                {
                    var item = new ToolStripMenuItem()
                    {
                        Text = info.GetNameString(),
                        Tag = info
                    };
                    item.Click += connectBridge;

                    bridgeAuswahlToolStripMenuItem.DropDownItems.Add(item);
                }
                bridgeAuswahlToolStripMenuItem.Enabled = true;
            }
            else
            {
                MessageBox.Show("Es konnte keine Hue Bridge im Netzwerk gefunden werden.\nStellen Sie sicher das sich die Hue Bridge im selben Netzwerk befindet.", "Keine Hue Bridge gefunden", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            Cursor = DefaultCursor;
        }



        void connectBridge(object sender, EventArgs e)
        {
            if (bridgeAuswahlToolStripMenuItem.DropDownItems.Count > 1)
                foreach (ToolStripMenuItem item in bridgeAuswahlToolStripMenuItem.DropDownItems)
                    item.Checked = false;

            var menuItem = (ToolStripMenuItem)sender;
            menuItem.Checked = true;
            var bridge = (BridgeInfo)menuItem.Tag;


            switch (m_Controller.ConnectBridge(bridge))
            {
                case BridgeResult.SuccessfulConnected:
                    bridgeConnected(bridge);
                    break;
                case BridgeResult.UnauthorizedUser:
                case BridgeResult.MissingUser:
                    if (newUser(bridge))
                        bridgeConnected(bridge);
                    else
                        throw new Exception("Fehler beim anlegen eines neuen Bridge-Benutzers.");
                    break;
                default:
                    throw new Exception("Fehler beim Verbinden der Hue Bridge.");
            }
        }

        void btn_ReadParameters_Click(object sender, EventArgs e)
        {
            m_Controller.ReadParameters(getSelectedParams(), getAnonymizeOptions());
            
            if (MessageBox.Show("Parameter wurden erfolgreich ausgelesen.\nSollen diese in eine Datei gespeichert werden?",
                "Bridge gefunden", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                saveFileDialog.Title = "Parameter-Datei speichern";
                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    m_Controller.SaveParameterFile(saveFileDialog.FileName); //TODO: Ergebnis anzeigen
            }
            else //UNDONE: "Cancel" and "No" Handling!
            {
                //m_Controller.Parameters = null;
            }

            btn_ShowParameters.Enabled = true;
        }

        void btn_ShowParameters_Click(object sender, EventArgs e)
        {
            m_Controller.VisualizeParameters();

            //var paramView = new ParameterView();
            //paramView.ApplyParameters(m_Controller.Parameters);

            //paramView.ShowDialog();
        }

        void beendenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        void newUserToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            newUser(null);
        }
        #endregion Benutzereingaben verarbeiten

        #region Hilfsfunktionen
        void appendRestoreLogLine(string message)
        {
            if (this.InvokeRequired)
            {
                Action<string> del = appendRestoreLogLine;
                BeginInvoke(del, message);
            }
            else
                txt_RestoreOutput.AppendText(Environment.NewLine + message);
        }

        void setupParameterSelection()
        {
            //foreach (HueParameterEnum param in Enum.GetValues(typeof(HueParameterEnum)))
            //    if (param.HasDisplayName())
            //        clb_Parameter.Items.Add(param.DisplayName());

            foreach (HueParameterGroupEnum param in Enum.GetValues(typeof(HueParameterGroupEnum)))
                clb_Parameter.Items.Add(new HueParameterGroup(param));

            clb_Parameter.DisplayMember = "DisplayName";
            clb_Parameter.ValueMember = "Value";
        }

        /// <summary>
        /// Checkboxen der Parameterauswahl setzen
        /// </summary>
        /// <param name="state">Checkbox-Status</param>
        void setAllParameters(bool state)
        {
            for (int i = 0; i < clb_Parameter.Items.Count; i++)
                clb_Parameter.SetItemChecked(i, state);
        }

        /// <summary>
        /// Abfrage der ausgewählten Parameter
        /// </summary>
        /// <returns>Parameter-Gruppen (flagged enum)</returns>
        HueParameterGroupEnum getSelectedParams()
        {
            HueParameterGroupEnum paras = 0;

            foreach (HueParameterGroup item in clb_Parameter.CheckedItems)
                paras |= item.Value;

            return paras;
        }

        /// <summary>
        /// Abfrage ausgewählter Anonymisierungs-Optionen
        /// </summary>
        /// <returns>Anonymisierungs-Optionen, als Array</returns>
        AnonymizeOptions[] getAnonymizeOptions()
        {
            var opts = new List<AnonymizeOptions>();

            if (cbx_AnonSerials.Checked)
                opts.Add(AnonymizeOptions.Serials);

            if (cbx_AnonNames.Checked)
                opts.Add(AnonymizeOptions.Names);

            return opts.ToArray();
        }

        bool newUser(BridgeInfo bridge)
        {
            var result = false;
            var source = new CancellationTokenSource();
            var token = source.Token;

            var pressButtonDlg = new BridgeButtonView(source);

            var task = Task.Run(async () => {
                while (true)
                {
                    if (token.IsCancellationRequested || result)
                        break;

                    System.Diagnostics.Debug.WriteLine("Waiting for Link-Button");

                    switch (await m_Controller.CreateUser(bridge))
                    {
                        case BridgeResult.LinkButtonNotPressed:
                            continue;
                        case BridgeResult.UserAlreadyExists:
                            throw new NotImplementedException();
                        case BridgeResult.UserCreated:
                            MessageBox.Show("Der neue Benutzer wurde erfolgreich auf der Hue Bridge angelegt.", "Autorisierung erfolgreich", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            result = true;
                            break;
                    }


                    Thread.Sleep(100);
                }
            }, token);


            pressButtonDlg.ShowDialog();

            return result;
        }

        void bridgeConnected(BridgeInfo bridge)
        {
            toolStripStatusLabel_Bridge.Text = "Bridge verbunden (" + (bridge).IpAddress + ")";
            Properties.Settings.Default.lastBridgeIp = bridge.IpAddress;
            Properties.Settings.Default.Save();
            btn_ReadParameters.Enabled = true;
        }
        #endregion Hilfsfunktionen

    }
}
