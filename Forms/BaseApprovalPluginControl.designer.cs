namespace ApprovalAdministrationTool
{
    partial class BaseApprovalPluginControl
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Code généré par le Concepteur de composants

        /// <summary>
        /// Method required for designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.toolStripMenu = new System.Windows.Forms.ToolStrip();
            this.tsbClose = new System.Windows.Forms.ToolStripButton();
            this.tssSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.buttonResetSettings = new System.Windows.Forms.ToolStripButton();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.dataGridControl1 = new ApprovalAdministrationTool.DataGridControl();
            this.webContentControl1 = new ApprovalAdministrationTool.Controls.WebContentControl();
            this.toolStripMenu.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.SuspendLayout();
            // 
            // toolStripMenu
            // 
            this.toolStripMenu.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.toolStripMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsbClose,
            this.tssSeparator1,
            this.buttonResetSettings});
            this.toolStripMenu.Location = new System.Drawing.Point(0, 0);
            this.toolStripMenu.Name = "toolStripMenu";
            this.toolStripMenu.Size = new System.Drawing.Size(630, 25);
            this.toolStripMenu.TabIndex = 4;
            this.toolStripMenu.Text = "toolStrip1";
            // 
            // tsbClose
            // 
            this.tsbClose.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.tsbClose.Name = "tsbClose";
            this.tsbClose.Size = new System.Drawing.Size(86, 22);
            this.tsbClose.Text = "Close this tool";
            this.tsbClose.Click += new System.EventHandler(this.tsbClose_Click);
            // 
            // tssSeparator1
            // 
            this.tssSeparator1.Name = "tssSeparator1";
            this.tssSeparator1.Size = new System.Drawing.Size(6, 25);
            // 
            // buttonResetSettings
            // 
            this.buttonResetSettings.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.buttonResetSettings.Name = "buttonResetSettings";
            this.buttonResetSettings.Size = new System.Drawing.Size(83, 22);
            this.buttonResetSettings.Text = "Reset settings";
            this.buttonResetSettings.ToolTipText = "Reset settings";
            this.buttonResetSettings.Click += new System.EventHandler(this.buttonResetSettings_Click);
            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(0, 25);
            this.splitContainer1.Margin = new System.Windows.Forms.Padding(2);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.dataGridControl1);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.webContentControl1);
            this.splitContainer1.Size = new System.Drawing.Size(630, 500);
            this.splitContainer1.SplitterDistance = 387;
            this.splitContainer1.SplitterIncrement = 2;
            this.splitContainer1.TabIndex = 5;
            this.splitContainer1.SplitterMoved += new System.Windows.Forms.SplitterEventHandler(this.splitContainer1_SplitterMoved);
            // 
            // dataGridControl1
            // 
            this.dataGridControl1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dataGridControl1.Location = new System.Drawing.Point(0, 0);
            this.dataGridControl1.Name = "dataGridControl1";
            this.dataGridControl1.Size = new System.Drawing.Size(384, 497);
            this.dataGridControl1.TabIndex = 0;
            // 
            // webContentControl1
            // 
            this.webContentControl1.Location = new System.Drawing.Point(3, 3);
            this.webContentControl1.Name = "webContentControl1";
            this.webContentControl1.Size = new System.Drawing.Size(170, 224);
            this.webContentControl1.TabIndex = 0;
            // 
            // BaseApprovalPluginControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.toolStripMenu);
            this.Name = "BaseApprovalPluginControl";
            this.Size = new System.Drawing.Size(630, 525);
            this.Load += new System.EventHandler(this.MyPluginControl_Load);
            this.toolStripMenu.ResumeLayout(false);
            this.toolStripMenu.PerformLayout();
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.ToolStrip toolStripMenu;
        private System.Windows.Forms.ToolStripButton tsbClose;
        private System.Windows.Forms.ToolStripButton buttonResetSettings;
        private System.Windows.Forms.ToolStripSeparator tssSeparator1;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private DataGridControl dataGridControl1;
        private Controls.WebContentControl webContentControl1;
    }
}
